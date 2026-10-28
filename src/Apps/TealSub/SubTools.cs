// Teal Sub (サブスク整理) - 1.8.0 の追加: 料金の変更履歴・月の予算・これから 12 か月の支払い予定・CSV の読み込み
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Sub
{
    /// <summary>料金の変更 1 回分 (編集で金額を変えたときに記録)</summary>
    [DataContract]
    public class PriceChange
    {
        [DataMember(Name = "date", Order = 1)] public string Date;
        [DataMember(Name = "from", Order = 2)] public long From;
        [DataMember(Name = "to", Order = 3)] public long To;
        [IgnoreDataMember] public DateTime? DateValue { get { return Fmt.ParseIso(Date); } }
        [IgnoreDataMember] public bool Increase { get { return To > From; } }
    }

    public class MonthPlan
    {
        public DateTime Month;                     // その月の 1 日
        public decimal Total;
        public List<KeyValuePair<Subscription, DateTime>> Items = new List<KeyValuePair<Subscription, DateTime>>();
    }

    public class PaymentTotal { public string Payment; public decimal Monthly; public int Count; }

    public class SubImportResult
    {
        public string Error;
        public List<Subscription> Items = new List<Subscription>();
        public int Duplicates, Skipped;
    }

    public static class SubPlan
    {
        public const int MaxHistory = 20;
        public const int RecentDays = 90;

        // ---------------------------------------------------------------- 料金の変更履歴

        /// <summary>金額が変わっていたら after に記録する。記録したら true</summary>
        public static bool RecordPrice(long before, Subscription after, DateTime today)
        {
            if (after == null || before == after.Amount || before <= 0) return false;
            if (after.PriceHistory == null) after.PriceHistory = new List<PriceChange>();
            after.PriceHistory.Add(new PriceChange { Date = Fmt.Iso(today.Date), From = before, To = after.Amount });
            if (after.PriceHistory.Count > MaxHistory) after.PriceHistory.RemoveRange(0, after.PriceHistory.Count - MaxHistory);
            return true;
        }

        public static PriceChange Latest(Subscription s)
        {
            return s.PriceHistory == null || s.PriceHistory.Count == 0 ? null : s.PriceHistory[s.PriceHistory.Count - 1];
        }

        /// <summary>days 日以内の値上げ (なければ null)</summary>
        public static PriceChange RecentIncrease(Subscription s, DateTime today, int days = RecentDays)
        {
            var p = Latest(s);
            if (p == null || !p.Increase || !p.DateValue.HasValue) return null;
            return (today.Date - p.DateValue.Value).TotalDays <= days ? p : null;
        }

        public static string HistoryText(Subscription s)
        {
            if (s.PriceHistory == null || s.PriceHistory.Count == 0) return null;
            return string.Join("\n", s.PriceHistory.AsEnumerable().Reverse().Take(5).Select(p =>
                (p.DateValue.HasValue ? Fmt.Date(p.DateValue.Value) : "日付不明") + "  " + Fmt.Yen(p.From) + " → " + Fmt.Yen(p.To)
                + (p.Increase ? " (値上げ +" + Fmt.Yen(p.To - p.From) + ")" : " (値下げ -" + Fmt.Yen(p.From - p.To) + ")")));
        }

        // ---------------------------------------------------------------- 予算

        /// <summary>予算に対する残り (マイナスなら超えている)。予算なしなら null</summary>
        public static decimal? BudgetLeft(IEnumerable<Subscription> subs, long budget)
        {
            if (budget <= 0) return null;
            return budget - Math.Round(SubLogic.MonthlyTotal(subs), 0);
        }

        public static string BudgetText(IEnumerable<Subscription> subs, long budget)
        {
            var left = BudgetLeft(subs, budget);
            if (!left.HasValue) return null;
            return left.Value >= 0 ? "予算 " + Fmt.Yen(budget) + " まであと " + Fmt.Yen(left.Value)
                                   : "予算 " + Fmt.Yen(budget) + " を " + Fmt.Yen(-left.Value) + " 超えています";
        }

        // ---------------------------------------------------------------- 支払い予定

        /// <summary>今月から months か月分の、実際の支払い日にもとづく予定 (解約予定は次の 1 回だけ、無料期間中は有料になってから)</summary>
        public static List<MonthPlan> Forecast(IEnumerable<Subscription> subs, DateTime today, int months = 12)
        {
            today = today.Date;
            var first = new DateTime(today.Year, today.Month, 1);
            var end = first.AddMonths(months);
            var plans = new List<MonthPlan>();
            for (int i = 0; i < months; i++) plans.Add(new MonthPlan { Month = first.AddMonths(i) });
            foreach (var s in subs)
            {
                if (!s.Paying) continue;
                var next = SubLogic.NextRenewal(s, today);
                if (!next.HasValue) continue;
                var anchor = SubLogic.Anchor(s);
                // next が何回目かを探す
                int k = 0;
                while (SubLogic.Nth(s, anchor, k) < next.Value && k < 100000) k++;
                for (int guard = 0; guard < 600; guard++, k++)
                {
                    var d = SubLogic.Nth(s, anchor, k);
                    if (d >= end) break;
                    int idx = (d.Year - first.Year) * 12 + d.Month - first.Month;
                    if (idx >= 0 && idx < months)
                    {
                        plans[idx].Items.Add(new KeyValuePair<Subscription, DateTime>(s, d));
                        plans[idx].Total += s.Amount;
                    }
                    if (s.Status == SubStatus.Cancelling) break;
                }
            }
            foreach (var p in plans) p.Items = p.Items.OrderBy(x => x.Value).ThenBy(x => x.Key.Name, StringComparer.CurrentCulture).ToList();
            return plans;
        }

        /// <summary>支払い方法ごとの月額 (大きい順。空欄は「未設定」)</summary>
        public static List<PaymentTotal> ByPayment(IEnumerable<Subscription> subs)
        {
            return subs.Where(s => s.Paying)
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Payment) ? "未設定" : s.Payment.Trim())
                .Select(g => new PaymentTotal { Payment = g.Key, Monthly = g.Sum(s => SubLogic.MonthlyCost(s)), Count = g.Count() })
                .OrderByDescending(p => p.Monthly).ThenBy(p => p.Payment, StringComparer.Ordinal).ToList();
        }

        // ---------------------------------------------------------------- CSV の読み込み (書き出した CSV・自分で作った表)

        static string Clean(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length > 1 && v[0] == '\'' && "=+-@".IndexOf(v[1]) >= 0) v = v.Substring(1);
            return v;
        }

        public static bool ParseCycle(string text, out Cycle cycle, out int months)
        {
            months = 0; cycle = Cycle.Monthly;
            var t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0 || t == "毎月" || t == "月" || t == "月額" || t == "monthly") return true;
            if (t == "毎年" || t == "年" || t == "年額" || t == "yearly" || t == "annual") { cycle = Cycle.Yearly; return true; }
            if (t == "毎週" || t == "週" || t == "weekly") { cycle = Cycle.Weekly; return true; }
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d{1,2})\s*(か|ヶ|ケ|カ|ヵ)?月(ごと|毎)?$");
            if (m.Success)
            {
                months = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (months < 1 || months > 60) return false;
                if (months == 12) { cycle = Cycle.Yearly; months = 0; }
                else if (months == 1) months = 0;
                else cycle = Cycle.EveryNMonths;
                return true;
            }
            return false;
        }

        static long ParseAmount(string v)
        {
            v = Clean(v).Replace(",", "").Replace("円", "").Replace("¥", "").Replace("￥", "").Trim();
            decimal d;
            if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d) || d < 0) return -1;
            return (long)Math.Round(d);
        }

        static DateTime? ParseDate(string v)
        {
            v = Clean(v);
            if (v.Length == 0) return null;
            DateTime d;
            if (DateTime.TryParseExact(v, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy/M/d", "yyyy-M-d", "yyyy.M.d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d.Date;
            return null;
        }

        static string Key(Subscription s) { return (s.Name ?? "").Trim().ToLowerInvariant() + "|" + s.Amount + "|" + s.CycleText + "|" + s.CycleMonths; }

        public static SubImportResult ImportCsv(string text, IEnumerable<Subscription> existing, DateTime today)
        {
            var r = new SubImportResult();
            var rows = TextKit.ParseCsv(text);
            if (rows.Count < 1) { r.Error = "CSV が空です。"; return r; }
            var h = rows[0];
            int cName = TextKit.Column(h, "名前", "サービス名", "サービス", "name"),
                cAmount = TextKit.Column(h, "金額(円)", "金額", "料金", "amount", "price"),
                cCycle = TextKit.Column(h, "周期", "支払いの周期", "cycle"),
                cNext = TextKit.Column(h, "次の支払い日", "更新日", "next"),
                cStart = TextKit.Column(h, "契約を始めた日", "開始日", "start"),
                cCat = TextKit.Column(h, "カテゴリ", "category"),
                cPay = TextKit.Column(h, "支払い方法", "payment"),
                cUsage = TextKit.Column(h, "使う頻度", "usage"),
                cLast = TextKit.Column(h, "最後に使った日", "lastused"),
                cStatus = TextKit.Column(h, "状態", "status"),
                cTrial = TextKit.Column(h, "無料期間の終わり", "trialend"),
                cCancelled = TextKit.Column(h, "解約日", "cancelledon"),
                cUrl = TextKit.Column(h, "解約ページ", "url"),
                cMemo = TextKit.Column(h, "メモ", "memo", "note");
            if (cName < 0 || cAmount < 0) { r.Error = "サブスクの CSV として読めませんでした。\n1 行目に「名前」と「金額」の見出しが必要です (Teal Sub で書き出した CSV はそのまま読めます)。"; return r; }
            var seen = new HashSet<string>(existing.Select(Key));
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var name = Clean(TextKit.Cell(row, cName));
                long amount = ParseAmount(TextKit.Cell(row, cAmount));
                Cycle cy; int months;
                if (name.Length == 0 || amount < 0 || !ParseCycle(Clean(TextKit.Cell(row, cCycle)), out cy, out months)) { r.Skipped++; continue; }
                var s = new Subscription { Name = name, Amount = amount, Created = Fmt.Iso(today.Date) };
                s.Cycle = cy; s.CycleMonths = months;
                var cat = Clean(TextKit.Cell(row, cCat)); s.Category = cat.Length == 0 ? "その他" : cat;
                var pay = Clean(TextKit.Cell(row, cPay)); s.Payment = pay.Length == 0 ? null : pay;
                int u = Array.IndexOf(SubLogic.UsageLabels, Clean(TextKit.Cell(row, cUsage))); if (u >= 0) s.Usage = (Usage)u;
                int st = Array.IndexOf(SubLogic.StatusLabels, Clean(TextKit.Cell(row, cStatus))); if (st >= 0) s.Status = (SubStatus)st;
                s.LastUsedDate = ParseDate(TextKit.Cell(row, cLast));
                s.TrialEndDate = ParseDate(TextKit.Cell(row, cTrial));
                s.CancelledDate = ParseDate(TextKit.Cell(row, cCancelled));
                if (s.Status == SubStatus.Cancelled && !s.CancelledDate.HasValue) s.CancelledDate = today.Date;
                var url = Clean(TextKit.Cell(row, cUrl)); s.CancelUrl = SubLogic.IsOpenableUrl(url) ? url : null;
                var memo = cMemo >= 0 && cMemo < row.Count ? (row[cMemo] ?? "") : ""; s.Memo = memo.Length == 0 ? null : memo.Replace("\r\n", "\n");
                // 起点: 契約を始めた日 → 次の支払い日 → 今日。無料期間の終わりより前の起点はそのまま (Anchor が無料期間を見る)
                var start = ParseDate(TextKit.Cell(row, cStart)) ?? ParseDate(TextKit.Cell(row, cNext));
                if (s.TrialEndDate.HasValue && (!start.HasValue || start.Value > s.TrialEndDate.Value)) start = today.Date < s.TrialEndDate.Value ? today.Date : s.TrialEndDate.Value;
                s.StartDate = start ?? today.Date;
                var k = Key(s);
                if (seen.Contains(k)) { r.Duplicates++; continue; }
                seen.Add(k);
                r.Items.Add(s);
            }
            if (r.Items.Count == 0 && r.Duplicates == 0 && r.Skipped == 0) r.Error = "読み込めるサブスクがありませんでした。";
            return r;
        }
    }

    // ==================================================================== 支払い予定の画面

    /// <summary>これから 12 か月の支払い予定 (月ごとの棒グラフ + 選んだ月の内訳 + 支払い方法ごと)</summary>
    public class ForecastForm : Form
    {
        public readonly List<MonthPlan> Plans;
        public readonly MonthBars Bars = new MonthBars();
        public readonly RowList Detail = new RowList(1) { Dock = DockStyle.Fill };
        public readonly RowList Payments = new RowList(1) { Dock = DockStyle.Fill };
        readonly Label monthTitle = new Label { AutoSize = false, Dock = DockStyle.Top, UseMnemonic = false };
        readonly DateTime today;

        public ForecastForm(IList<Subscription> subs, DateTime today, long budget)
        {
            this.today = today.Date;
            Theme.ApplyForm(this);
            Text = AppInfo.TitleFor("支払い予定");
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;
            Size = Theme.S(860, 620); MinimumSize = Theme.S(640, 480);
            Plans = SubPlan.Forecast(subs, today, 12);
            decimal sum = Plans.Sum(p => p.Total);
            var header = new HeaderBar("これから 12 か月の支払い", "合計 " + Fmt.Yen(sum) + "  ・  1 か月平均 " + Fmt.Yen(sum / 12m) + "  ・  実際の支払い日で数えています", false);

            Bars.Dock = DockStyle.Top; Bars.Height = Theme.S(190);
            Bars.SetData(Plans, budget);
            Bars.SelectedChanged += (s, e) => ShowMonth(Bars.SelectedIndex);
            var barCard = new CardPanel("月ごとの支払い (棒をクリックすると内訳)") { Dock = DockStyle.Top, Height = Theme.S(240) };
            barCard.Controls.Add(Bars);

            monthTitle.Font = Theme.Bold; monthTitle.Height = Theme.Bold.Height + Theme.S(8); monthTitle.ForeColor = Theme.Text;
            Detail.PaintRow = PaintDetail; Detail.EmptyText = "この月の支払いはありません";
            var detCard = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10), Margin = new Padding(0, 0, Theme.S(8), 0) };
            detCard.Controls.Add(Detail); detCard.Controls.Add(monthTitle);

            var pays = SubPlan.ByPayment(subs);
            Payments.PaintRow = PaintPayment; Payments.EmptyText = "支払い中のサブスクはありません";
            Payments.SetItems(pays.Cast<object>().ToList());
            var payCard = new CardPanel("支払い方法ごと (月あたり)") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            payCard.Controls.Add(Payments);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = Theme.P(0, 8, 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.Controls.Add(detCard, 0, 0); grid.Controls.Add(payCard, 1, 0);

            var close = new FlatButton("閉じる", ButtonKind.Secondary).FitWidth(88);
            close.DialogResult = DialogResult.Cancel; CancelButton = close;
            var foot = Lay.Flow(close); foot.Dock = DockStyle.Bottom; foot.FlowDirection = FlowDirection.RightToLeft;

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12, 8, 12, 12), BackColor = Theme.Background };
            body.Controls.Add(grid); body.Controls.Add(barCard); body.Controls.Add(foot);
            Controls.Add(body); Controls.Add(header);
            // 今月の残りがもう無いときは、次に支払いがある月を最初に見せる
            int first = Math.Max(0, Plans.FindIndex(p => p.Total > 0));
            Bars.SelectedIndex = first;
            ShowMonth(first);
        }

        void ShowMonth(int i)
        {
            if (i < 0 || i >= Plans.Count) return;
            var p = Plans[i];
            monthTitle.Text = p.Month.ToString("yyyy年M月") + " の支払い  " + Fmt.Yen(p.Total) + " (" + p.Items.Count + " 件)";
            Detail.SetItems(p.Items.Cast<object>().ToList());
        }

        void PaintDetail(Graphics g, Rectangle r, object o, bool sel)
        {
            var kv = (KeyValuePair<Subscription, DateTime>)o;
            int pad = Theme.S(10), lh = r.Height;
            Draw.Text(g, kv.Value.ToString("M/d") + " (" + "日月火水木金土"[(int)kv.Value.DayOfWeek] + ")", Theme.Body, Theme.SubText, new Rectangle(r.X + pad, r.Y, Theme.S(80), lh));
            string amt = Fmt.Yen(kv.Key.Amount);
            int aw = Draw.Width(g, amt, Theme.Bold) + Theme.S(4);
            Draw.Text(g, amt, Theme.Bold, Theme.Text, new Rectangle(r.Right - pad - aw, r.Y, aw, lh), TextFormatFlags.Right);
            int x = r.X + pad + Theme.S(84);
            string name = kv.Key.Name + (kv.Key.Status == SubStatus.Cancelling ? "  (解約予定)" : "") + (string.IsNullOrWhiteSpace(kv.Key.Payment) ? "" : "  ・  " + kv.Key.Payment);
            Draw.Text(g, name, Theme.Body, Theme.Text, new Rectangle(x, r.Y, Math.Max(10, r.Right - pad - aw - x - Theme.S(6)), lh));
        }

        void PaintPayment(Graphics g, Rectangle r, object o, bool sel)
        {
            var p = (PaymentTotal)o;
            int pad = Theme.S(10), lh = r.Height;
            string amt = Fmt.Yen(p.Monthly) + " / 月";
            int aw = Draw.Width(g, amt, Theme.Bold) + Theme.S(4);
            Draw.Text(g, amt, Theme.Bold, Theme.Text, new Rectangle(r.Right - pad - aw, r.Y, aw, lh), TextFormatFlags.Right);
            Draw.Text(g, p.Payment + "  (" + p.Count + " 件)", Theme.Body, p.Payment == "未設定" ? Theme.SubText : Theme.Text, new Rectangle(r.X + pad, r.Y, Math.Max(10, r.Right - pad * 2 - aw), lh));
        }
    }

    /// <summary>12 か月の棒グラフ (クリックで月を選ぶ。予算があれば線を引く)</summary>
    public class MonthBars : Control
    {
        List<MonthPlan> plans = new List<MonthPlan>();
        long budget;
        int selected = -1;
        public event EventHandler SelectedChanged;
        public MonthBars()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand; TabStop = true;
        }
        public void SetData(List<MonthPlan> list, long monthlyBudget) { plans = list; budget = monthlyBudget; Invalidate(); }
        public int SelectedIndex
        {
            get { return selected; }
            set { int v = Math.Max(-1, Math.Min(plans.Count - 1, value)); if (v == selected) return; selected = v; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }
        Rectangle Slot(int i)
        {
            int w = Math.Max(1, Width / Math.Max(1, plans.Count));
            return new Rectangle(i * w, 0, w, Height);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            for (int i = 0; i < plans.Count; i++) if (Slot(i).Contains(e.Location)) { SelectedIndex = i; return; }
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left && selected > 0) SelectedIndex = selected - 1;
            else if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (plans.Count == 0) return;
            int labelH = Theme.Small.Height + Theme.S(4), valueH = Theme.Small.Height + Theme.S(2);
            int top = valueH + Theme.S(2), bottom = Height - labelH;
            decimal max = Math.Max(1, plans.Max(p => p.Total));
            if (budget > 0) max = Math.Max(max, budget);
            for (int i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                var slot = Slot(i);
                int bw = Math.Max(Theme.S(6), slot.Width - Theme.S(14));
                int bh = (int)((bottom - top) * (double)(p.Total / max));
                if (p.Total > 0) bh = Math.Max(Theme.S(3), bh);
                var bar = new Rectangle(slot.X + (slot.Width - bw) / 2, bottom - bh, bw, bh);
                bool sel = i == selected;
                bool over = budget > 0 && p.Total > budget;
                var color = over ? Theme.Warning : sel ? Theme.Accent : Theme.Blend(Theme.Accent, Theme.Surface, 0.45f);
                if (bh > 0)
                    using (var b = new SolidBrush(color))
                    using (var path = Theme.RoundRect(new RectangleF(bar.X, bar.Y, bar.Width, bar.Height), Theme.S(3)))
                        g.FillPath(b, path);
                var v = p.Total >= 10000 ? (Math.Round(p.Total / 1000m, 0) / 10m).ToString("0.#", CultureInfo.InvariantCulture) + "万" : Fmt.Yen(p.Total).Replace("¥", "");
                Draw.Text(g, v, Theme.Small, sel ? Theme.Accent : Theme.SubText, new Rectangle(slot.X, Math.Max(0, bar.Y - valueH), slot.Width, valueH), TextFormatFlags.HorizontalCenter);
                var lbl = p.Month.Month == 1 || i == 0 ? p.Month.ToString("yy/M") : p.Month.Month + "月";
                Draw.Text(g, lbl, sel ? Theme.Bold : Theme.Small, sel ? Theme.Accent : Theme.SubText, new Rectangle(slot.X, bottom + Theme.S(2), slot.Width, labelH), TextFormatFlags.HorizontalCenter);
            }
            if (budget > 0)
            {
                int y = bottom - (int)((bottom - top) * (double)(budget / max));
                using (var pen = new Pen(Theme.Danger, Theme.S(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawLine(pen, 0, y, Width, y);
                Draw.Text(g, "月の予算 " + Fmt.Yen(budget), Theme.Small, Theme.Danger, new Rectangle(Theme.S(4), Math.Max(0, y - valueH), Theme.S(200), valueH));
            }
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, bottom, Width, bottom);
        }
    }
}
