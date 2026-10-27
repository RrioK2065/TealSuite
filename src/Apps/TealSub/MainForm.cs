// Teal Sub (サブスク整理) - メイン画面
//  タブ: ダッシュボード / 一覧 / 見直し候補 / 解約済み (Ctrl+1〜4、または ←→)
//  Ctrl+N 追加 / Ctrl+F 検索 / Enter 編集 / Delete 削除 / Ctrl+U 今日使った
//  お知らせ: 起動中 (常駐中) だけ、30 分ごとと日付が変わったときに確かめる
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Sub
{
    public class MainForm : Form
    {
        public SubData Data;
        readonly HeaderBar header;
        readonly FooterBar footer = new FooterBar();
        public readonly SegmentTabs Tabs = new SegmentTabs("ダッシュボード", "一覧", "見直し候補", "解約済み");
        readonly Panel content = new Panel { Dock = DockStyle.Fill };
        readonly Panel[] views = new Panel[4];
        readonly Timer remindTimer = new Timer();
        ResidentContext resident;
        DateTime lastCheckDay = DateTime.MinValue;
        readonly ToolTip tips = new ToolTip();

        // ダッシュボード
        readonly Panel dashEmpty = new CardPanel();
        readonly Panel dashMain = new Panel { Dock = DockStyle.Fill };
        public readonly StatTile TileMonthly = new StatTile(), TileYearly = new StatTile(), TileCount = new StatTile();
        readonly WrapLabel dashAlert = new WrapLabel();
        readonly CategoryChart chart = new CategoryChart();
        readonly RowList upcoming = new RowList(2) { Dock = DockStyle.Fill, EmptyText = "これから 30 日の支払いはありません" };
        // 一覧
        public readonly TextBox Search = new TextBox();
        readonly ComboBox filter = Lay.Combo("支払い中 (契約中+解約予定)", "契約中", "解約予定", "すべて");
        readonly ComboBox sort = Lay.Combo("次の支払いが近い順", "月額が高い順", "名前順", "カテゴリ順");
        public readonly RowList List = new RowList(2) { Dock = DockStyle.Fill };
        // 見直し候補
        readonly WrapLabel reviewSummary = new WrapLabel();
        readonly Panel reviewCards = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        // 解約済み
        readonly WrapLabel cancelledSummary = new WrapLabel();
        readonly RowList cancelledList = new RowList(2) { Dock = DockStyle.Fill, EmptyText = "解約したサブスクはまだありません。\n解約ガイドから「解約した」を押すと、ここに移って浮いた金額がわかります。" };

        DateTime Today { get { return DateTime.Today; } }

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(760, 520);
            KeyPreview = true;

            header = HeaderBar.ForApp("このパソコンの中だけで管理します");
            var btnSettings = header.AddAction(new FlatButton("設定", ButtonKind.Secondary).FitWidth(72));
            var btnAdd = header.AddAction(new FlatButton("＋ 追加", ButtonKind.Primary).FitWidth(96));
            btnAdd.Click += (s, e) => AddNew();
            btnSettings.Click += (s, e) => OpenSettings();
            tips.SetToolTip(btnAdd, "サブスクを追加 (Ctrl+N)");
            tips.SetToolTip(btnSettings, "お知らせ・CSV・バックアップ");

            Tabs.Dock = DockStyle.Top;
            Tabs.SelectedChanged += (s, e) => ShowTab(Tabs.SelectedIndex);

            for (int i = 0; i < views.Length; i++) views[i] = new Panel { Dock = DockStyle.Fill, Visible = i == 0, BackColor = Theme.Background };
            BuildDashboard(views[0]);
            BuildList(views[1]);
            BuildReview(views[2]);
            BuildCancelled(views[3]);
            foreach (var v in views) content.Controls.Add(v);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12, 8, 12, 12), BackColor = Theme.Background };
            body.Controls.Add(content);
            body.Controls.Add(Ui.Spacer(8));
            body.Controls.Add(Tabs);
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            string warn;
            Data = DataStore.LoadOrDefault(SubData.FileName, () => new SubData(), out warn);
            Data.Normalize();
            if (warn != null) Load += (s, e) => Dialogs.Warn(this, warn);

            WindowMemory.Attach(this, 980, 680);
            remindTimer.Interval = 5000;
            remindTimer.Tick += (s, e) => { remindTimer.Interval = 30 * 60 * 1000; CheckReminders(); };
            Load += (s, e) =>
            {
                RefreshAll();
                int tab = StartTab();
                if (tab > 0) Tabs.SelectedIndex = tab;
                remindTimer.Start();
            };
            // 日付が変わったら (スリープ明けなど) 表示とお知らせを更新
            Activated += (s, e) => { if (lastCheckDay != Today) { RefreshAll(); CheckReminders(); } };
            Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
            FormClosed += (s, e) => { Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged; remindTimer.Stop(); };
        }

        void OnTimeChanged(object sender, EventArgs e) { AppHost.OnUi(() => { RefreshAll(); CheckReminders(); }); }

        /// <summary>--tab=1〜4 (またはタブ名) で最初に開くタブ (画面写真用)</summary>
        static int StartTab()
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (!a.StartsWith("--tab=")) continue;
                var v = a.Substring(6);
                int n;
                if (int.TryParse(v, out n) && n >= 1 && n <= 4) return n - 1;
                switch (v) { case "list": return 1; case "review": return 2; case "cancelled": return 3; }
            }
            return 0;
        }

        public void AttachResident(ResidentContext ctx)
        {
            resident = ctx;
            ctx.BalloonClicked = () => { ctx.Window.Show(); if (ctx.Window.WindowState == FormWindowState.Minimized) ctx.Window.WindowState = FormWindowState.Normal; AppHost.BringToFront(ctx.Window); Tabs.SelectedIndex = 0; };
        }

        // ================================================================ 共通

        void ShowTab(int i)
        {
            for (int k = 0; k < views.Length; k++) views[k].Visible = k == i;
            if (i == 1) List.Focus();
            if (i == 2) LayoutCards();
        }

        public void Save()
        {
            if (!DataStore.TrySave(SubData.FileName, Data)) footer.StatusText = "保存できませんでした (データフォルダを確かめてください)";
        }

        public void RefreshAll()
        {
            lastCheckDay = Today;
            RefreshDashboard();
            RefreshList();
            RefreshReview();
            RefreshCancelled();
            int paying = Data.Subs.Count(x => x.Paying);
            footer.StatusText = paying + " 件を支払い中  ・  月 " + Fmt.Yen(SubLogic.MonthlyTotal(Data.Subs)) + "  ・  年 " + Fmt.Yen(SubLogic.YearlyTotal(Data.Subs));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: AddNew(); return true;
                case Keys.Control | Keys.D1: Tabs.SelectedIndex = 0; return true;
                case Keys.Control | Keys.D2: Tabs.SelectedIndex = 1; return true;
                case Keys.Control | Keys.D3: Tabs.SelectedIndex = 2; return true;
                case Keys.Control | Keys.D4: Tabs.SelectedIndex = 3; return true;
                case Keys.Control | Keys.F: Tabs.SelectedIndex = 1; Search.Focus(); Search.SelectAll(); return true;
                case Keys.Control | Keys.U: { var s = CurrentSub(); if (s != null) MarkUsed(s); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        Subscription CurrentSub()
        {
            if (Tabs.SelectedIndex == 1) return List.SelectedItem as Subscription;
            if (Tabs.SelectedIndex == 3) return cancelledList.SelectedItem as Subscription;
            return null;
        }

        // ================================================================ 操作

        public void AddNew()
        {
            var s = new Subscription { Created = Fmt.Iso(Today) };
            s.StartDate = Today;
            using (var f = new SubEditForm(s, true))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            Data.Subs.Add(s);
            Save(); RefreshAll();
            SelectInList(s);
            footer.StatusText = "「" + s.Name + "」を追加しました";
        }

        public void Edit(Subscription s)
        {
            if (s == null) return;
            var copy = s.Clone();
            using (var f = new SubEditForm(copy, false))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            int i = Data.Subs.IndexOf(s);
            if (i >= 0) Data.Subs[i] = copy;
            Save(); RefreshAll();
            SelectInList(copy);
        }

        void Delete(Subscription s)
        {
            if (s == null) return;
            if (!Dialogs.Confirm(this, "「" + s.Name + "」を削除しますか?\n(解約しただけなら、削除せずに「解約済み」にしておくと浮いた金額を記録できます)")) return;
            Data.Subs.Remove(s);
            Save(); RefreshAll();
        }

        void MarkUsed(Subscription s)
        {
            s.LastUsedDate = Today;
            Save(); RefreshAll();
            footer.StatusText = "「" + s.Name + "」の最後に使った日を今日にしました";
        }

        void SetStatus(Subscription s, SubStatus st)
        {
            if (st == SubStatus.Cancelled && s.Status != SubStatus.Cancelled) s.CancelledDate = Today;
            if (st != SubStatus.Cancelled) s.CancelledOn = null;
            s.Status = st;
            Save(); RefreshAll();
            footer.StatusText = "「" + s.Name + "」を" + SubLogic.StatusLabel(st) + "にしました";
        }

        public void OpenGuide(Subscription s)
        {
            using (var f = new CancelGuideForm(s))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (f.Choice == CancelGuideForm.Result.MarkCancelled) SetStatus(s, SubStatus.Cancelled);
                else if (f.Choice == CancelGuideForm.Result.MarkCancelling) SetStatus(s, SubStatus.Cancelling);
            }
        }

        void SelectInList(Subscription s)
        {
            for (int i = 0; i < List.Items.Count; i++) if (((Subscription)List.Items[i]).Id == s.Id) { List.SelectedIndex = i; return; }
        }

        public void InsertSamples()
        {
            Data.Subs.AddRange(SubLogic.SampleData(Today));
            Save(); RefreshAll();
            footer.StatusText = "見本データを入れました (不要になったら一覧から削除できます)";
        }

        void OpenSettings()
        {
            bool res = resident != null && resident.IsResident;
            using (var f = new SubSettingsForm(Data.Settings, res, ExportCsv, Backup, Restore))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            Data.Normalize();
            Save(); RefreshAll();
            CheckReminders();
        }

        void ExportCsv()
        {
            var p = FileKit.SaveText(this, SubLogic.ToCsv(Data.Subs, Today), "サブスク一覧-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", "CSV (*.csv)|*.csv");
            if (p != null) footer.StatusText = "CSV に書き出しました: " + p;
        }
        void Backup()
        {
            var p = FileKit.ExportJson(this, Data, "TealSub");
            if (p != null) footer.StatusText = "バックアップしました: " + p;
        }
        void Restore()
        {
            var d = FileKit.ImportJson<SubData>(this, SubData.FileName, x => x.Subs != null);
            if (d == null) return;
            d.Normalize();
            Data = d;
            Save(); RefreshAll();
            Dialogs.Info(this, "バックアップから " + d.Subs.Count + " 件を戻しました。");
        }

        // ================================================================ お知らせ

        public List<Notice> CheckReminders()
        {
            lastCheckDay = Today;
            var list = SubLogic.DueNotices(Data, Today);
            if (list.Count == 0) return list;
            var ctx = resident ?? ResidentContext.Current;
            if (ctx == null) return list;
            bool shown;
            if (list.Count <= 2)
            {
                shown = true;
                foreach (var n in list) shown &= ctx.Notify(n.Title, n.Text, ToolTipIcon.Info);
            }
            else
            {
                var text = string.Join("\n", list.Take(4).Select(n => "・" + n.Title.Substring(n.Title.IndexOf(':') + 1).Trim() + " " + n.Text.Substring(0, n.Text.IndexOf(')') + 1)));
                if (list.Count > 4) text += "\nほか " + (list.Count - 4) + " 件";
                shown = ctx.Notify("Teal Sub: 支払い・無料期間のお知らせ " + list.Count + " 件", text, ToolTipIcon.Info);
            }
            if (shown)
            {
                foreach (var n in list) Data.Notified.Add(n.Key);
                SubLogic.PruneNotified(Data, Today);
                Save();
            }
            return list;
        }

        // ================================================================ ダッシュボード

        void BuildDashboard(Panel v)
        {
            // データが空のとき
            dashEmpty.Dock = DockStyle.Top;
            dashEmpty.Padding = Theme.P(20, 16, 20, 16);
            var t1 = new WrapLabel { Text = "まだサブスクが登録されていません", Font = Theme.Title, ForeColor = Theme.Text };
            var t2 = new WrapLabel { Text = "動画・音楽・クラウドなど、毎月や毎年お金がかかっているサービスを登録すると、合計金額・次の支払い日・見直したほうがよいものがひと目でわかります。\nまずは「＋ 追加」から 1 件登録してみましょう。どんな画面になるか見たいときは、見本データを入れて試せます (あとで削除できます)。", Font = Theme.Body, ForeColor = Theme.SubText };
            var add = new FlatButton("＋ 最初のサブスクを追加", ButtonKind.Primary).FitWidth(120);
            var sample = new FlatButton("見本データを入れる", ButtonKind.Secondary).FitWidth(120);
            add.Click += (s, e) => AddNew();
            sample.Click += (s, e) => InsertSamples();
            var row = Lay.Flow(add, sample);
            Lay.Stack(dashEmpty, t1, Ui.Spacer(6), t2, Ui.Spacer(12), row);
            dashEmpty.Resize += (s, e) => FitHeight(dashEmpty);

            // 数字のタイル
            var tiles = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Height = TileMonthly.PreferredHeight, BackColor = Theme.Background };
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            foreach (var t in new[] { TileMonthly, TileYearly, TileCount }) { t.Dock = DockStyle.Fill; t.Margin = new Padding(0, 0, Theme.S(8), 0); tiles.Controls.Add(t); }
            TileCount.Margin = Padding.Empty;

            dashAlert.Font = Theme.Body; dashAlert.Dock = DockStyle.Top; dashAlert.Padding = Theme.P(2, 8, 2, 0);
            dashAlert.Cursor = Cursors.Hand;
            dashAlert.Click += (s, e) => Tabs.SelectedIndex = 2;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = Theme.P(0, 8, 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var chartCard = new CardPanel("カテゴリ別 (月あたり)") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(8), 0) };
            chart.Dock = DockStyle.Fill;
            chartCard.Controls.Add(chart);
            var upCard = new CardPanel("これから 30 日の支払い") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            upcoming.PaintRow = PaintUpcoming;
            upcoming.DoubleClick += (s, e) => { var kv = upcoming.SelectedItem as UpItem; if (kv != null) Edit(kv.Sub); };
            upcoming.KeyDown += (s, e) => { var kv = upcoming.SelectedItem as UpItem; if (kv != null && e.KeyCode == Keys.Enter) { Edit(kv.Sub); e.Handled = true; } };
            upCard.Controls.Add(upcoming);
            grid.Controls.Add(chartCard, 0, 0);
            grid.Controls.Add(upCard, 1, 0);

            dashMain.Controls.Add(grid);
            dashMain.Controls.Add(dashAlert);
            dashMain.Controls.Add(tiles);
            v.Controls.Add(dashMain);
            v.Controls.Add(dashEmpty);
        }

        static void FitHeight(Panel p)
        {
            int h = p.Padding.Vertical;
            foreach (Control c in p.Controls) if (c.Visible) h += c.Height;
            if (p.Height != h) p.Height = h;
        }

        class UpItem { public Subscription Sub; public DateTime Date; public bool Trial; }

        void RefreshDashboard()
        {
            bool empty = Data.Subs.Count == 0;
            dashEmpty.Visible = empty;
            dashMain.Visible = !empty;
            if (empty) { FitHeight(dashEmpty); return; }
            var today = Today;
            var paying = Data.Subs.Where(s => s.Paying).ToList();
            decimal m = SubLogic.MonthlyTotal(Data.Subs), y = SubLogic.YearlyTotal(Data.Subs);
            int trials = paying.Count(s => SubLogic.InTrial(s, today));
            TileMonthly.Set("月あたりの合計", Fmt.Yen(m), "年払い・週払いも月あたりに換算", Theme.Accent);
            TileYearly.Set("1 年の合計", Fmt.Yen(y), "1 日あたり約 " + Fmt.Yen(y / 365m));
            int cancelling = paying.Count(s => s.Status == SubStatus.Cancelling);
            TileCount.Set("支払い中のサブスク", paying.Count + " 件",
                (trials > 0 ? "無料期間中 " + trials + " 件" : "無料期間中なし") + (cancelling > 0 ? "  ・  解約予定 " + cancelling + " 件" : ""));

            var cands = SubLogic.Candidates(Data.Subs, today);
            if (cands.Count > 0)
            {
                dashAlert.ForeColor = Theme.Warning;
                dashAlert.Text = "見直し候補が " + cands.Count + " 件あります。全部やめると 1 年で最大 " + Fmt.Yen(SubLogic.PossibleSaving(cands)) + " 浮きます  →  「見直し候補」を見る (Ctrl+3)";
            }
            else
            {
                dashAlert.ForeColor = Theme.Success;
                dashAlert.Text = "いまのところ見直し候補はありません。使う頻度や最後に使った日を入れておくと、使っていないものを見つけやすくなります。";
            }

            chart.SetData(SubLogic.ByCategory(Data.Subs), m);

            var items = new List<UpItem>();
            foreach (var kv in SubLogic.Upcoming(Data.Subs, today, 30)) items.Add(new UpItem { Sub = kv.Key, Date = kv.Value });
            foreach (var s in paying)
            {
                var t = s.TrialEndDate;
                if (t.HasValue && t.Value >= today && t.Value <= today.AddDays(30) && s.Status == SubStatus.Active && !items.Any(i => i.Sub == s && i.Date == t.Value))
                    items.Add(new UpItem { Sub = s, Date = t.Value, Trial = true });
            }
            upcoming.SetItems(items.OrderBy(i => i.Date).ThenBy(i => i.Sub.Name).ToList());
        }

        void PaintUpcoming(Graphics g, Rectangle r, object o, bool sel)
        {
            var it = (UpItem)o;
            var today = Today;
            int pad = Theme.S(12), lh = Theme.Body.Height + Theme.S(2);
            int days = (int)(it.Date - today).TotalDays;
            // 左: 日付
            int dw = Theme.S(84);
            var dc = days <= Data.Settings.RemindDays ? Theme.Warning : Theme.Text;
            Draw.Text(g, Fmt.MonthDay(it.Date), Theme.Bold, dc, new Rectangle(r.X + pad, r.Y + Theme.S(5), dw, lh));
            Draw.Text(g, Fmt.Relative(it.Date, today), Theme.Small, days <= Data.Settings.RemindDays ? Theme.Warning : Theme.SubText, new Rectangle(r.X + pad, r.Y + Theme.S(5) + lh, dw, lh));
            // 右: 金額
            string amt = Fmt.Yen(it.Sub.Amount);
            int aw = Draw.Width(g, amt, Theme.Bold) + Theme.S(4);
            Draw.Text(g, amt, Theme.Bold, Theme.Text, new Rectangle(r.Right - pad - aw, r.Y + Theme.S(5), aw, lh), TextFormatFlags.Right);
            // 中: 名前と内容
            int x = r.X + pad + dw, w = r.Right - pad - aw - Theme.S(8) - x;
            Draw.Text(g, it.Sub.Name, Theme.Body, Theme.Text, new Rectangle(x, r.Y + Theme.S(5), w, lh));
            string sub = it.Trial ? "無料期間が終わり、有料になります" : (it.Sub.Status == SubStatus.Cancelling ? "解約予定 (この日までに解約を)" : SubLogic.CycleLabel(it.Sub) + "  ・  " + it.Sub.Category);
            Draw.Text(g, sub, Theme.Small, it.Trial || it.Sub.Status == SubStatus.Cancelling ? Theme.Warning : Theme.SubText, new Rectangle(x, r.Y + Theme.S(5) + lh, w, lh));
        }

        // ================================================================ 一覧

        void BuildList(Panel v)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            Search.Font = Theme.Body; Search.BorderStyle = BorderStyle.None;
            Ui.SetCue(Search, "名前・カテゴリ・メモで検索…  (Ctrl+F)");
            Search.TextChanged += (s, e) => RefreshList();
            Search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Search.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { List.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            filter.Width = Theme.S(200); sort.Width = Theme.S(170);
            filter.SelectedIndexChanged += (s, e) => RefreshList();
            sort.SelectedIndexChanged += (s, e) => RefreshList();
            tips.SetToolTip(filter, "表示するもの"); tips.SetToolTip(sort, "並べ方");
            var top = Lay.Row(new InputFrame(Search), sort, filter);

            List.PaintRow = PaintSubRow;
            List.EmptyText = "該当するサブスクはありません";
            List.DoubleClick += (s, e) => Edit(List.SelectedItem as Subscription);
            List.KeyDown += (s, e) =>
            {
                var sub = List.SelectedItem as Subscription;
                if (sub == null) return;
                if (e.KeyCode == Keys.Enter) { Edit(sub); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { Delete(sub); e.Handled = true; }
            };

            var bUsed = new FlatButton("今日使った", ButtonKind.Secondary).FitWidth(88);
            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(72);
            var bGuide = new FlatButton("解約ガイド", ButtonKind.Secondary).FitWidth(88);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
            bUsed.Click += (s, e) => { var x = List.SelectedItem as Subscription; if (x != null) MarkUsed(x); };
            bEdit.Click += (s, e) => Edit(List.SelectedItem as Subscription);
            bGuide.Click += (s, e) => { var x = List.SelectedItem as Subscription; if (x != null) OpenGuide(x); };
            bDel.Click += (s, e) => Delete(List.SelectedItem as Subscription);
            tips.SetToolTip(bUsed, "最後に使った日を今日にする (Ctrl+U)");
            tips.SetToolTip(bEdit, "編集 (Enter / ダブルクリック)");
            tips.SetToolTip(bDel, "削除 (Delete)");
            var buttons = Lay.Flow(bUsed, bEdit, bGuide, bDel);
            buttons.Dock = DockStyle.Bottom;

            card.Controls.Add(List);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(top); top.Dock = DockStyle.Top;
            var sp = Ui.Spacer(8); sp.Dock = DockStyle.Bottom;
            card.Controls.Add(sp);
            card.Controls.Add(buttons);
            v.Controls.Add(card);
        }

        public void RefreshList()
        {
            var q = Search.Text.Trim();
            var today = Today;
            IEnumerable<Subscription> src = Data.Subs;
            switch (filter.SelectedIndex)
            {
                case 0: src = src.Where(s => s.Paying); break;
                case 1: src = src.Where(s => s.Status == SubStatus.Active); break;
                case 2: src = src.Where(s => s.Status == SubStatus.Cancelling); break;
            }
            if (q.Length > 0)
                src = src.Where(s => (s.Name + " " + s.Category + " " + s.Memo + " " + s.Payment).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            switch (sort.SelectedIndex)
            {
                case 1: src = src.OrderByDescending(s => SubLogic.MonthlyCost(s)).ThenBy(s => s.Name); break;
                case 2: src = src.OrderBy(s => s.Name, StringComparer.CurrentCulture); break;
                case 3: src = src.OrderBy(s => Array.IndexOf(SubLogic.Categories, s.Category) < 0 ? 99 : Array.IndexOf(SubLogic.Categories, s.Category)).ThenBy(s => s.Category).ThenBy(s => s.Name); break;
                default: src = src.OrderBy(s => SubLogic.NextRenewal(s, today) ?? DateTime.MaxValue).ThenBy(s => s.Name); break;
            }
            var keep = List.SelectedItem as Subscription;
            List.SetItems(src.ToList(), o => keep != null && ((Subscription)o).Id == keep.Id);
            List.EmptyText = Data.Subs.Count == 0 ? "まだ登録がありません。「＋ 追加」(Ctrl+N) から登録してください。" : "該当するサブスクはありません";
        }

        void PaintSubRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var s = (Subscription)o;
            var today = Today;
            int pad = Theme.S(14), lh = Theme.Body.Height + Theme.S(2), y1 = r.Y + Theme.S(5), y2 = y1 + lh;
            // 右: 金額 + 周期 / 月あたり
            string amt = Fmt.Yen(s.Amount) + " / " + SubLogic.CycleLabel(s).Replace("毎", "");
            int aw = Math.Max(Draw.Width(g, amt, Theme.Bold), Theme.S(120)) + Theme.S(4);
            Draw.Text(g, amt, Theme.Bold, s.Paying ? Theme.Text : Theme.SubText, new Rectangle(r.Right - pad - aw, y1, aw, lh), TextFormatFlags.Right);
            Draw.Text(g, "月あたり " + Fmt.Yen(SubLogic.MonthlyCost(s)), Theme.Small, Theme.SubText, new Rectangle(r.Right - pad - aw, y2, aw, lh), TextFormatFlags.Right);
            // 左: 名前 + 状態チップ
            int x = r.X + pad, right = r.Right - pad - aw - Theme.S(10);
            int cx = right;
            if (s.Status != SubStatus.Active)
                cx = Draw.Chip(g, SubLogic.StatusLabel(s.Status), s.Status == SubStatus.Cancelled ? Theme.SubText : Theme.Warning, s.Status == SubStatus.Cancelled ? Theme.SurfaceAlt : Theme.Blend(Theme.Surface, Theme.Warning, 0.15f), cx, y1 + lh / 2, true) - Theme.S(6);
            if (SubLogic.InTrial(s, today))
                cx = Draw.Chip(g, "無料期間 〜" + Fmt.MonthDay(s.TrialEndDate.Value), Theme.Accent, Theme.AccentSoft, cx, y1 + lh / 2, true) - Theme.S(6);
            Draw.Text(g, s.Name, Theme.Bold, Theme.Text, new Rectangle(x, y1, Math.Max(10, cx - x), lh));
            // 2 行目
            var next = SubLogic.NextRenewal(s, today);
            var parts = new List<string>();
            if (next.HasValue) parts.Add("次の支払い " + Fmt.MonthDay(next.Value) + " (" + Fmt.Relative(next.Value, today) + ")");
            else if (s.CancelledDate.HasValue) parts.Add(Fmt.Date(s.CancelledDate.Value) + " に解約");
            parts.Add(s.Category);
            parts.Add(SubLogic.UsageLabel(s.Usage));
            if (s.LastUsedDate.HasValue) parts.Add("最後に使った日 " + Fmt.Relative(s.LastUsedDate.Value, today));
            if (!string.IsNullOrEmpty(s.Payment)) parts.Add(s.Payment);
            bool soon = next.HasValue && (next.Value - today).TotalDays <= Data.Settings.RemindDays;
            Draw.Text(g, string.Join("  ・  ", parts), Theme.Small, soon ? Theme.Warning : Theme.SubText, new Rectangle(x, y2, right - x, lh));
        }

        // ================================================================ 見直し候補

        void BuildReview(Panel v)
        {
            reviewSummary.Font = Theme.Body; reviewSummary.Dock = DockStyle.Top; reviewSummary.Padding = Theme.P(2, 0, 2, 8);
            reviewCards.BackColor = Theme.Background;
            reviewCards.Resize += (s, e) => LayoutCards();
            v.Controls.Add(reviewCards);
            v.Controls.Add(reviewSummary);
        }

        public List<ReviewItem> ReviewItems = new List<ReviewItem>();

        void RefreshReview()
        {
            ReviewItems = SubLogic.Candidates(Data.Subs, Today);
            reviewCards.SuspendLayout();
            foreach (Control c in reviewCards.Controls.Cast<Control>().ToList()) { reviewCards.Controls.Remove(c); c.Dispose(); }
            int active = Data.Subs.Count(s => s.Status == SubStatus.Active);
            if (ReviewItems.Count == 0)
            {
                reviewSummary.ForeColor = Theme.Text;
                reviewSummary.Text = active == 0 ? "契約中のサブスクがありません。" : "見直し候補はありません (契約中 " + active + " 件を確認しました)。";
                var tip = new CardPanel("点数のつけ方") { Dock = DockStyle.Top };
                tip.Controls.Add(new WrapLabel { Dock = DockStyle.Top, Font = Theme.Body, ForeColor = Theme.SubText, Text = HowScored });
                reviewCards.Controls.Add(tip);
            }
            else
            {
                reviewSummary.ForeColor = Theme.Text;
                reviewSummary.Text = "見直し候補 " + ReviewItems.Count + " 件 (契約中 " + active + " 件のうち)。全部やめると 1 年で " + Fmt.Yen(SubLogic.PossibleSaving(ReviewItems)) + "、月あたり " + Fmt.Yen(SubLogic.PossibleSaving(ReviewItems) / 12m) + " 浮きます。\n点数は使う頻度・最後に使った日・1 回あたりの金額・同じカテゴリの重なり・無料期間の終わりから計算しています (使った日を記録すると正確になります)。";
                for (int i = ReviewItems.Count - 1; i >= 0; i--)
                {
                    reviewCards.Controls.Add(Ui.Spacer(10));
                    reviewCards.Controls.Add(MakeCard(ReviewItems[i]));
                }
            }
            reviewCards.ResumeLayout();
            LayoutCards();
        }

        const string HowScored = "・使う頻度が「ほぼ使わない」+40、「月に数回」+20\n・最後に使ってから 90 日以上 +35、45 日以上 +25、30 日以上 +15\n・1 回あたりの金額が ¥1,000 以上 +25、¥400 以上 +12\n・同じカテゴリに 2 件以上 (いちばんよく使うもの以外) +15\n・無料期間があと 7 日以内に終わる +25\n合計 30 点以上で「見直し候補」、60 点以上で「見直しをおすすめ」になります。";

        Control MakeCard(ReviewItem it)
        {
            var s = it.Sub;
            var card = new CardPanel { Dock = DockStyle.Top, Padding = Theme.P(16, 12, 16, 12) };
            var head = new ReviewHeader(it);
            var reasons = new List<Control>();
            foreach (var r in it.Reasons) reasons.Add(new WrapLabel { Text = "・" + r, Font = Theme.Body, ForeColor = Theme.Text, Padding = Theme.P(4, 1, 0, 1) });
            var bGuide = new FlatButton("解約ガイド", it.Strong ? ButtonKind.Primary : ButtonKind.Secondary).FitWidth(88);
            var bLater = new FlatButton("解約予定にする", ButtonKind.Secondary).FitWidth(96);
            var bUsed = new FlatButton("今日使った", ButtonKind.Secondary).FitWidth(80);
            var bEdit = new FlatButton("編集", ButtonKind.Ghost).FitWidth(64);
            bGuide.Click += (o, e) => OpenGuide(s);
            bLater.Click += (o, e) => SetStatus(s, SubStatus.Cancelling);
            bUsed.Click += (o, e) => MarkUsed(s);
            bEdit.Click += (o, e) => Edit(s);
            tips.SetToolTip(bUsed, "使っているなら、最後に使った日を今日にすると候補から外れることがあります");
            var row = Lay.Flow(bGuide, bLater, bUsed, bEdit);
            var all = new List<Control> { head, Ui.Spacer(4) };
            all.AddRange(reasons);
            all.Add(Ui.Spacer(8)); all.Add(row);
            Lay.Stack(card, all.ToArray());
            card.Margin = Padding.Empty;
            return card;
        }

        /// <summary>カードの高さを中身に合わせる (WrapLabel の高さは幅で変わる)</summary>
        void LayoutCards()
        {
            if (!reviewCards.Visible && Tabs.SelectedIndex != 2) return;
            reviewCards.SuspendLayout();
            foreach (Control c in reviewCards.Controls)
            {
                var card = c as CardPanel;
                if (card == null) continue;
                card.Width = reviewCards.ClientSize.Width;
                card.PerformLayout();
                FitHeight(card);
                card.PerformLayout();
                FitHeight(card);
            }
            reviewCards.ResumeLayout(true);
        }

        /// <summary>見直しカードの見出し (名前・点数・浮く金額)</summary>
        class ReviewHeader : Control
        {
            readonly ReviewItem it;
            public ReviewHeader(ReviewItem it)
            {
                this.it = it;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Height = Theme.Title.Height + Theme.Small.Height + Theme.S(10);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Surface);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int th = Theme.Title.Height + Theme.S(2);
                string save = "やめると年 " + Fmt.Yen(it.YearlySaving);
                int sw = Draw.Width(g, save, Theme.Title) + Theme.S(4);
                Draw.Text(g, save, Theme.Title, Theme.Accent, new Rectangle(Width - sw, 0, sw, th), TextFormatFlags.Right);
                string chip = (it.Strong ? "見直しをおすすめ" : "見直し候補") + "  " + it.Score + " 点";
                int cx = Draw.Chip(g, chip, it.Strong ? Theme.Danger : Theme.Warning, it.Strong ? Theme.DangerSoft : Theme.Blend(Theme.Surface, Theme.Warning, 0.15f), 0, th / 2, false);
                Draw.Text(g, it.Sub.Name, Theme.Title, Theme.Text, new Rectangle(cx + Theme.S(10), 0, Math.Max(10, Width - sw - cx - Theme.S(20)), th));
                var s = it.Sub;
                string line = Fmt.Yen(s.Amount) + " (" + SubLogic.CycleLabel(s) + ")  ・  " + s.Category + "  ・  " + SubLogic.UsageLabel(s.Usage)
                    + (s.LastUsedDate.HasValue ? "  ・  最後に使った日 " + Fmt.Date(s.LastUsedDate.Value) : "  ・  最後に使った日は未記録");
                Draw.Text(g, line, Theme.Small, Theme.SubText, new Rectangle(0, th + Theme.S(2), Width, Theme.Small.Height + Theme.S(4)));
            }
        }

        // ================================================================ 解約済み

        void BuildCancelled(Panel v)
        {
            cancelledSummary.Font = Theme.Body; cancelledSummary.Dock = DockStyle.Top; cancelledSummary.Padding = Theme.P(2, 0, 2, 8);
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            cancelledList.PaintRow = PaintCancelled;
            cancelledList.DoubleClick += (s, e) => Edit(cancelledList.SelectedItem as Subscription);
            cancelledList.KeyDown += (s, e) =>
            {
                var sub = cancelledList.SelectedItem as Subscription;
                if (sub == null) return;
                if (e.KeyCode == Keys.Enter) { Edit(sub); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { Delete(sub); e.Handled = true; }
            };
            var bBack = new FlatButton("契約中に戻す", ButtonKind.Secondary).FitWidth(96);
            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(72);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
            bBack.Click += (s, e) => { var x = cancelledList.SelectedItem as Subscription; if (x != null) SetStatus(x, SubStatus.Active); };
            bEdit.Click += (s, e) => Edit(cancelledList.SelectedItem as Subscription);
            bDel.Click += (s, e) => Delete(cancelledList.SelectedItem as Subscription);
            var buttons = Lay.Flow(bBack, bEdit, bDel);
            buttons.Dock = DockStyle.Bottom;
            card.Controls.Add(cancelledList);
            var sp = Ui.Spacer(8); sp.Dock = DockStyle.Bottom;
            card.Controls.Add(sp);
            card.Controls.Add(buttons);
            v.Controls.Add(card);
            v.Controls.Add(cancelledSummary);
        }

        void RefreshCancelled()
        {
            var today = Today;
            var list = Data.Subs.Where(s => s.Status == SubStatus.Cancelled).OrderByDescending(s => s.CancelledDate ?? DateTime.MinValue).ToList();
            decimal saved = list.Sum(s => SubLogic.SavedSoFar(s, today));
            decimal perYear = list.Sum(s => SubLogic.YearlyCost(s));
            cancelledSummary.ForeColor = list.Count > 0 ? Theme.Success : Theme.Text;
            cancelledSummary.Text = list.Count == 0 ? "解約したサブスクはありません。"
                : "解約したサブスク " + list.Count + " 件。これまでに浮いた金額の目安は " + Fmt.Yen(saved) + "、これからも 1 年で " + Fmt.Yen(perYear) + " 浮きます。";
            var keep = cancelledList.SelectedItem as Subscription;
            cancelledList.SetItems(list, o => keep != null && ((Subscription)o).Id == keep.Id);
        }

        void PaintCancelled(Graphics g, Rectangle r, object o, bool sel)
        {
            var s = (Subscription)o;
            var today = Today;
            int pad = Theme.S(14), lh = Theme.Body.Height + Theme.S(2), y1 = r.Y + Theme.S(5), y2 = y1 + lh;
            string saved = "浮いた金額 " + Fmt.Yen(SubLogic.SavedSoFar(s, today));
            int aw = Draw.Width(g, saved, Theme.Bold) + Theme.S(4);
            Draw.Text(g, saved, Theme.Bold, Theme.Success, new Rectangle(r.Right - pad - aw, y1, aw, lh), TextFormatFlags.Right);
            string py = "年 " + Fmt.Yen(SubLogic.YearlyCost(s));
            Draw.Text(g, py, Theme.Small, Theme.SubText, new Rectangle(r.Right - pad - aw, y2, aw, lh), TextFormatFlags.Right);
            int x = r.X + pad, w = r.Right - pad - aw - Theme.S(10) - x;
            Draw.Text(g, s.Name, Theme.Bold, Theme.Text, new Rectangle(x, y1, w, lh));
            string line = (s.CancelledDate.HasValue ? Fmt.Date(s.CancelledDate.Value) + " に解約 (" + Fmt.Relative(s.CancelledDate.Value, today) + ")" : "解約日は未記録")
                + "  ・  " + Fmt.Yen(s.Amount) + " (" + SubLogic.CycleLabel(s) + ")  ・  " + s.Category;
            Draw.Text(g, line, Theme.Small, Theme.SubText, new Rectangle(x, y2, w, lh));
        }
    }

    /// <summary>カテゴリ別の横棒グラフ (GDI+)</summary>
    public class CategoryChart : Control
    {
        List<CategoryTotal> items = new List<CategoryTotal>();
        decimal total;
        public CategoryChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SetData(List<CategoryTotal> list, decimal monthlyTotal) { items = list; total = monthlyTotal; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (items.Count == 0 || total <= 0)
            {
                Draw.Text(g, "支払い中のサブスクはありません", Theme.Body, Theme.SubText, new Rectangle(0, 0, Width, Theme.S(30)));
                return;
            }
            int rowH = Theme.Body.Height + Theme.S(14);
            int maxRows = Math.Max(1, Height / rowH);
            var list = items;
            if (items.Count > maxRows && maxRows >= 2)
            {
                list = items.Take(maxRows - 1).ToList();
                var rest = items.Skip(maxRows - 1).ToList();
                list.Add(new CategoryTotal { Category = "ほか " + rest.Count + " 種類", Monthly = rest.Sum(x => x.Monthly), Count = rest.Sum(x => x.Count) });
            }
            int labelW = 0;
            foreach (var it in list) labelW = Math.Max(labelW, Draw.Width(g, it.Category, Theme.Body));
            labelW = Math.Min(labelW + Theme.S(8), Width / 3);
            int amountW = Draw.Width(g, "¥00,000 (100%)", Theme.Small) + Theme.S(6);
            int barX = labelW, barMax = Math.Max(Theme.S(20), Width - labelW - amountW - Theme.S(8));
            decimal max = list.Max(x => x.Monthly);
            int y = 0, idx = 0;
            foreach (var it in list)
            {
                Draw.Text(g, it.Category, Theme.Body, Theme.Text, new Rectangle(0, y, labelW - Theme.S(4), rowH));
                int bw = max > 0 ? (int)Math.Max(Theme.S(3), barMax * (double)(it.Monthly / max)) : 0;
                var color = Theme.Blend(Theme.Accent, Theme.Surface, Math.Min(0.6f, idx * 0.09f));
                using (var b = new SolidBrush(color))
                using (var path = Theme.RoundRect(new RectangleF(barX, y + Theme.S(6), bw, rowH - Theme.S(12)), Theme.S(3)))
                    g.FillPath(b, path);
                int pct = (int)Math.Round(it.Monthly * 100m / total);
                Draw.Text(g, Fmt.Yen(it.Monthly) + " (" + pct + "%)", Theme.Small, Theme.SubText, new Rectangle(barX + bw + Theme.S(6), y, amountW, rowH));
                y += rowH; idx++;
            }
        }
    }
}
