// Teal Plan - カレンダー (月・週・日)。GDI+ で自前で描く。週は月曜はじまり
//  並べるのは CalItem (タスク・予定・ほかのアプリの読むだけの項目)。Items に期間を渡して受け取る
//  クリック: 日・項目を選ぶ / 日の「＋」: 予定かタスクを追加するメニュー
//  ダブルクリック: 空き場所なら新しい予定 (月・終日欄は終日、週・日はその時刻から 1 時間)、項目なら開く
//  ドラッグ: 空き場所をなぞると、その範囲で新しい予定 (月・終日欄は複数日の終日)
//            項目をつかむと別の日・時刻へ動かす (15 分きざみ)。時刻つきの予定・タスクは下の端で長さを変える
//            読むだけの項目 (ほかのアプリ) は動かない
//  キー: ←→↑↓ 日を移動 / PageUp・PageDown 前後の期間 / Home 今日 / Enter 開く・追加 / Space タスクの完了 / Delete 削除
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public enum CalMode { Month, Week, Day }

    public class CalendarView : Control
    {
        class Hit
        {
            public Rectangle Rect; public CalItem Item; public DateTime Day; public bool AllDay;
            /// <summary>この札の上端の時刻 (日をまたぐ予定の 2 日目なら 0:00)</summary>
            public DateTime SegStart;
            public bool Resizable;
        }
        enum Drag { None, Move, Resize, Create }

        /// <summary>期間 [from, to) の項目</summary>
        public Func<DateTime, DateTime, IEnumerable<CalItem>> Items = (a, b) => new CalItem[0];
        public Func<DateTime> Clock = () => DateTime.Now;
        /// <summary>タスクを追加 (day, withTime)</summary>
        public event Action<DateTime, bool> AddRequested;
        /// <summary>予定を追加 (始まり, 終わり (終日なら最後の日), 終日か)</summary>
        public event Action<DateTime, DateTime, bool> EventRequested;
        /// <summary>「＋」や Enter: 予定かタスクかを選ぶメニューを出す (日, 出す位置 (この中の座標))</summary>
        public event Action<DateTime, Point> AddMenuRequested;
        public event Action<CalItem> ItemOpened;
        public event Action<CalItem> ItemToggled;
        public event Action<CalItem> ItemDeleteRequested;
        /// <summary>item, 新しい始まり, 時刻つきか</summary>
        public event Action<CalItem, DateTime, bool> ItemMoved;
        /// <summary>item, 新しい終わり (時刻つきだけ)</summary>
        public event Action<CalItem, DateTime> ItemResized;
        public event EventHandler ViewChanged;

        CalMode mode = CalMode.Month;
        DateTime anchor = DateTime.Today;
        /// <summary>選んでいる項目の鍵 (CalItem.Key)</summary>
        public string SelectedKey;
        /// <summary>日を動かさない (「今日」タブの時間割)</summary>
        public bool Fixed;
        readonly List<Hit> hits = new List<Hit>();
        readonly List<KeyValuePair<Rectangle, DateTime>> addButtons = new List<KeyValuePair<Rectangle, DateTime>>();
        readonly VScrollBar vbar = new VScrollBar();
        List<CalItem> items = new List<CalItem>();
        DateTime? hoverDay;
        // ドラッグ
        Hit pressHit; Point pressPt; Drag drag; bool dragging; Point dragPt; int grabOffsetY; int grabDayOffset;
        bool pressAllDay; DateTime? pressTime; DateTime? pressDay;

        public CalendarView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Surface; Font = Theme.Small; TabStop = true;
            vbar.Dock = DockStyle.Right; vbar.Visible = false; vbar.SmallChange = HourH / 2;
            vbar.ValueChanged += (s, e) => Invalidate();
            Controls.Add(vbar);
        }

        public CalMode Mode
        {
            get { return mode; }
            set { if (mode == value) return; mode = value; vbar.Visible = mode != CalMode.Month; Reload(); UpdateScroll(true); Changed(); }
        }
        public DateTime Day
        {
            get { return anchor; }
            set { anchor = value.Date; Reload(); Changed(); }
        }
        void Changed() { Invalidate(); if (ViewChanged != null) ViewChanged(this, EventArgs.Empty); }

        /// <summary>項目を読み直して描き直す</summary>
        public void Reload()
        {
            var days = VisibleDays();
            try { items = (Items(days[0], days[days.Count - 1].AddDays(1)) ?? new CalItem[0]).Where(x => x != null).ToList(); }
            catch { items = new List<CalItem>(); }
            Invalidate();
        }
        /// <summary>今表示している項目 (テスト・一覧との行き来用)</summary>
        public IList<CalItem> ShownItems { get { return items; } }

        public CalItem SelectedItem
        {
            get { return SelectedKey == null ? null : items.FirstOrDefault(x => x.Key == SelectedKey); }
            set { SelectedKey = value == null ? null : value.Key; Invalidate(); }
        }
        public TaskItem SelectedTask
        {
            get { var x = SelectedItem; return x != null ? x.Task : null; }
            set { SelectedKey = value == null ? null : "t:" + value.Id; Invalidate(); }
        }

        public void Navigate(int dir)
        {
            if (Fixed) return;
            if (mode == CalMode.Month) Day = anchor.AddMonths(dir);
            else if (mode == CalMode.Week) Day = anchor.AddDays(7 * dir);
            else Day = anchor.AddDays(dir);
        }
        public void GoToday() { Day = Clock().Date; UpdateScroll(true); }

        public string PeriodText
        {
            get
            {
                if (mode == CalMode.Month) return anchor.Year + "年" + anchor.Month + "月";
                if (mode == CalMode.Day) return Fmt.Date(anchor);
                var ws = TaskLogic.WeekStart(anchor); var we = ws.AddDays(6);
                return ws.Year + "/" + ws.Month + "/" + ws.Day + " 〜 " + (we.Year != ws.Year ? we.Year + "/" : "") + we.Month + "/" + we.Day;
            }
        }

        /// <summary>今表示している日 (月表示は 6 週間ぶん)</summary>
        public List<DateTime> VisibleDays()
        {
            var list = new List<DateTime>();
            if (mode == CalMode.Day) { list.Add(anchor); return list; }
            if (mode == CalMode.Week) { var ws = TaskLogic.WeekStart(anchor); for (int i = 0; i < 7; i++) list.Add(ws.AddDays(i)); return list; }
            var first = TaskLogic.WeekStart(new DateTime(anchor.Year, anchor.Month, 1));
            for (int i = 0; i < 42; i++) list.Add(first.AddDays(i));
            return list;
        }

        // ------------------------------------------------------------ 寸法
        static int ChipH { get { return Theme.Small.Height + Theme.S(6); } }
        static int HourH { get { return Theme.S(44); } }
        int HeadH { get { return Theme.Small.Height + Theme.S(12) + (mode == CalMode.Month ? 0 : Theme.Bold.Height); } }
        int Gutter { get { return mode == CalMode.Month ? 0 : Theme.S(52); } }
        int AllDayRows
        {
            get
            {
                if (mode == CalMode.Month) return 0;
                var days = VisibleDays();
                var rows = Allocate(items.Where(x => x.AllDay).ToList(), days);
                int need = rows.Count == 0 ? 1 : rows.Values.Max() + 1;
                return Math.Max(1, Math.Min(3, need));
            }
        }
        int AllDayH { get { return AllDayRows * (ChipH + Theme.S(2)) + Theme.S(8); } }
        Rectangle GridRect
        {
            get
            {
                int w = ClientSize.Width - (vbar.Visible ? vbar.Width : 0);
                int top = HeadH + (mode == CalMode.Month ? 0 : AllDayH);
                return new Rectangle(Gutter, top, Math.Max(1, w - Gutter), Math.Max(1, ClientSize.Height - top));
            }
        }

        void UpdateScroll(bool toMorning)
        {
            if (mode == CalMode.Month) return;
            var gr = GridRect;
            int total = HourH * 24;
            vbar.Minimum = 0; vbar.Maximum = Math.Max(0, total - 1); vbar.LargeChange = Math.Max(1, gr.Height);
            if (toMorning)
            {
                // 7 時から (それより前の項目があればそこから)
                int first = 7 * 60;
                var days = VisibleDays();
                foreach (var x in items) if (!x.AllDay && days.Contains(x.Start.Date)) first = Math.Min(first, (int)x.Start.TimeOfDay.TotalMinutes);
                SetScroll(first * HourH / 60 - Theme.S(8));
            }
            else SetScroll(vbar.Value);
        }
        /// <summary>指定の時刻が見えるようにスクロール</summary>
        public void ScrollTo(DateTime t) { if (mode != CalMode.Month) SetScroll((int)(t.TimeOfDay.TotalMinutes * HourH / 60) - Theme.S(8)); }
        public int ScrollValue { get { return vbar.Value; } }
        void SetScroll(int v) { vbar.Value = Math.Max(0, Math.Min(Math.Max(0, vbar.Maximum - vbar.LargeChange + 1), v)); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(false); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (mode == CalMode.Month) Navigate(e.Delta > 0 ? -1 : 1);
            else SetScroll(vbar.Value - e.Delta * HourH / 120);
        }

        // ------------------------------------------------------------ 並べ方

        /// <summary>日をまたぐ札を段に割り当てる (同じ段で重ならないように、上から詰める)</summary>
        static Dictionary<CalItem, int> Allocate(List<CalItem> list, List<DateTime> cols)
        {
            var rows = new Dictionary<CalItem, int>();
            var used = new List<bool[]>();
            DateTime first = cols[0], last = cols[cols.Count - 1];
            foreach (var x in EventLogic.Sort(list))
            {
                if (x.LastDay < first || x.Start.Date > last) continue;
                int c0 = Math.Max(0, (x.Start.Date - first).Days), c1 = Math.Min(cols.Count - 1, (x.LastDay - first).Days);
                int row = 0;
                while (true)
                {
                    if (row >= used.Count) used.Add(new bool[cols.Count]);
                    bool free = true;
                    for (int c = c0; c <= c1; c++) if (used[row][c]) { free = false; break; }
                    if (free) break;
                    row++;
                }
                for (int c = c0; c <= c1; c++) used[row][c] = true;
                rows[x] = row;
            }
            return rows;
        }

        DateTime? resizeEnd;

        /// <summary>週・日の時間割に置く、その日の部分 (始まり, 終わり)</summary>
        List<Tuple<CalItem, DateTime, DateTime>> Segments(DateTime day)
        {
            var list = new List<Tuple<CalItem, DateTime, DateTime>>();
            foreach (var x in items)
            {
                if (x.AllDay) continue;
                var end = x.End;
                if (drag == Drag.Resize && dragging && pressHit != null && pressHit.Item == x && resizeEnd.HasValue) end = resizeEnd.Value;
                if (end <= x.Start) end = x.Start.AddMinutes(30);
                var s = x.Start < day ? day : x.Start;
                var e = end > day.AddDays(1) ? day.AddDays(1) : end;
                if (s < e && x.Start < day.AddDays(1) && end > day) list.Add(Tuple.Create(x, s, e));
            }
            return list.OrderBy(t => t.Item2).ThenByDescending(t => t.Item3 - t.Item2).ToList();
        }

        // ------------------------------------------------------------ 描画

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear(); addButtons.Clear();
            if (mode == CalMode.Month) PaintMonth(g); else PaintWeek(g);
            if (dragging && drag == Drag.Move && pressHit != null) PaintGhost(g);
            if (dragging && drag == Drag.Create) PaintCreate(g);
            if (Focused)
                using (var p = new Pen(Theme.AccentLight)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        static readonly string[] DowMon = { "月", "火", "水", "木", "金", "土", "日" };

        Color DowColor(DateTime d)
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) return Theme.Danger;
            if (d.DayOfWeek == DayOfWeek.Saturday) return Theme.Accent;
            return Theme.SubText;
        }

        Rectangle MonthCell(int k)
        {
            int head = HeadH;
            float cw = ClientSize.Width / 7f, ch = (ClientSize.Height - head) / 6f;
            return new Rectangle((int)((k % 7) * cw), head + (int)((k / 7) * ch), (int)cw, (int)ch);
        }

        /// <summary>なぞって作る途中の範囲 (始まり, 終わり, 終日か)</summary>
        Tuple<DateTime, DateTime, bool> CreateRange()
        {
            if (pressAllDay || mode == CalMode.Month)
            {
                var d1 = pressDay; var d2 = DayAt(dragPt) ?? d1;
                if (!d1.HasValue || !d2.HasValue) return null;
                var a = d1.Value < d2.Value ? d1.Value : d2.Value; var b = d1.Value < d2.Value ? d2.Value : d1.Value;
                return Tuple.Create(a, b, true);
            }
            if (!pressTime.HasValue) return null;
            var gr = GridRect;
            int min = (Math.Max(gr.Y, Math.Min(gr.Bottom, dragPt.Y)) - gr.Y + vbar.Value) * 60 / HourH;
            min = Math.Max(0, Math.Min(24 * 60, (int)Math.Floor(min / 15.0) * 15));
            var day = pressTime.Value.Date;
            var t2 = day.AddMinutes(min);
            var s = pressTime.Value < t2 ? pressTime.Value : t2;
            var e = pressTime.Value < t2 ? t2.AddMinutes(15) : pressTime.Value.AddMinutes(15);
            if (e > day.AddDays(1)) e = day.AddDays(1);
            return Tuple.Create(s, e, false);
        }

        void PaintMonth(Graphics g)
        {
            var today = Clock().Date;
            int w = ClientSize.Width, head = HeadH;
            using (var bg = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(bg, 0, 0, w, head);
            float cw0 = w / 7f;
            for (int i = 0; i < 7; i++)
                TextRenderer.DrawText(g, DowMon[i], Theme.Small, new Rectangle((int)(i * cw0), 0, (int)cw0, head), i == 6 ? Theme.Danger : i == 5 ? Theme.Accent : Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            var days = VisibleDays();
            var create = dragging && drag == Drag.Create ? CreateRange() : null;
            int nh = Theme.Small.Height + Theme.S(6);
            using (var line = new Pen(Theme.Border))
            {
                for (int k = 0; k < 42; k++)
                {
                    var d = days[k];
                    var cell = MonthCell(k);
                    bool other = d.Month != anchor.Month;
                    if (other) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.Background, 0.7f))) g.FillRectangle(b, cell);
                    if (d == anchor) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, cell);
                    if (hoverDay == d && d != anchor) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.SurfaceAlt, 0.6f))) g.FillRectangle(b, cell);
                    if (dragging && drag == Drag.Move && DayAt(dragPt).HasValue && pressHit != null)
                    {
                        var ns = DayAt(dragPt).Value.AddDays(-grabDayOffset);
                        if (d >= ns && d <= ns.AddDays((pressHit.Item.LastDay - pressHit.Item.Start.Date).Days)) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.3f))) g.FillRectangle(b, cell);
                    }
                    if (create != null && d >= create.Item1 && d <= create.Item2) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.35f))) g.FillRectangle(b, cell);
                    g.DrawRectangle(line, cell.X, cell.Y, cell.Width, cell.Height);

                    // 日付
                    string num = d.Day == 1 ? d.Month + "/" + d.Day : d.Day.ToString();
                    var numRect = new Rectangle(cell.X + Theme.S(4), cell.Y + Theme.S(3), Draw.Width(g, num, Theme.Small) + Theme.S(10), nh);
                    if (d == today)
                    {
                        using (var path = Theme.RoundRect(numRect, nh / 2f)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                        TextRenderer.DrawText(g, num, Theme.Small, numRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                    else TextRenderer.DrawText(g, num, d == anchor ? Theme.Bold : Theme.Small, numRect, other ? Theme.Disabled : DowColor(d) == Theme.SubText ? Theme.Text : DowColor(d), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                    // ＋ (ポインタを乗せた日と選んだ日)
                    if (hoverDay == d || anchor == d)
                    {
                        var add = new Rectangle(cell.Right - nh - Theme.S(4), cell.Y + Theme.S(3), nh, nh);
                        PaintAddButton(g, add, hoverDay == d);
                        addButtons.Add(new KeyValuePair<Rectangle, DateTime>(add, d));
                    }
                }
            }
            // 札 (週ごとに段を割り当てる。複数日の予定は横に伸びる帯)
            for (int wk = 0; wk < 6; wk++)
            {
                var cols = days.GetRange(wk * 7, 7);
                var rowOf = Allocate(items, cols);
                var c0 = MonthCell(wk * 7);
                int top = c0.Y + Theme.S(3) + nh + Theme.S(3);
                int room = Math.Max(0, (c0.Bottom - Theme.S(3) - top) / (ChipH + Theme.S(2)));
                var hidden = new int[7];
                var total = new int[7];
                foreach (var x in rowOf.Keys) for (int c = 0; c < 7; c++) if (x.OnDay(cols[c])) total[c]++;
                foreach (var kv in rowOf)
                {
                    var x = kv.Key; int row = kv.Value;
                    int a = Math.Max(0, (x.Start.Date - cols[0]).Days), b = Math.Min(6, (x.LastDay - cols[0]).Days);
                    // 入りきらない段は「ほか N 件」に (日付の横に出すので、段は全部使える)
                    if (row >= room) { for (int c = a; c <= b; c++) hidden[c]++; continue; }
                    int ca = a, cb = b;
                    if (ca > cb) continue;
                    var ra = MonthCell(wk * 7 + ca); var rb = MonthCell(wk * 7 + cb);
                    var r = new Rectangle(ra.X + Theme.S(3), top + row * (ChipH + Theme.S(2)), rb.Right - ra.X - Theme.S(6), ChipH);
                    bool contL = x.Start.Date < cols[ca], contR = x.LastDay > cols[cb];
                    PaintItem(g, x, r, true, contL, contR);
                    hits.Add(new Hit { Rect = r, Item = x, Day = cols[ca], SegStart = x.Start, AllDay = true });
                }
                for (int c = 0; c < 7; c++)
                {
                    if (hidden[c] <= 0) continue;
                    var cell = MonthCell(wk * 7 + c);
                    var d = cols[c];
                    var numW = Draw.Width(g, d.Day == 1 ? d.Month + "/" + d.Day : d.Day.ToString(), Theme.Small) + Theme.S(10);
                    int mx = cell.X + Theme.S(4) + numW + Theme.S(4), mr = (hoverDay == d || anchor == d) ? cell.Right - nh - Theme.S(8) : cell.Right - Theme.S(4);
                    Draw.Text(g, "ほか " + hidden[c] + " 件", Theme.Small, d == anchor ? Theme.Accent : Theme.SubText, new Rectangle(mx, cell.Y + Theme.S(3), Math.Max(0, mr - mx), nh));
                }
            }
        }

        static void PaintAddButton(Graphics g, Rectangle add, bool hot)
        {
            using (var path = Theme.RoundRect(add, add.Height / 2f)) using (var b = new SolidBrush(hot ? Theme.Accent : Theme.Surface)) g.FillPath(b, path);
            TextRenderer.DrawText(g, "＋", Theme.Small, add, hot ? Color.White : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        /// <summary>
        /// 1 件の札。タスク = 薄い地に丸いチェック (優先度の色)、終日の予定 = 色の帯に白い文字、
        /// 時刻つきの予定 = 月表示では色の点 + 時刻、週・日では色の薄い地に左の線、読むだけの項目 = 点線の枠
        /// </summary>
        void PaintItem(Graphics g, CalItem x, Rectangle r, bool monthStyle, bool contL = false, bool contR = false, DateTime? segStart = null, DateTime? segEnd = null)
        {
            bool sel = SelectedKey != null && SelectedKey == x.Key;
            int rad = Theme.S(4);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var tr = new Rectangle(r.X + Theme.S(6), r.Y, r.Width - Theme.S(8), Math.Min(r.Height, ChipH));
            if (x.Kind == CalKind.External)
            {
                using (var path = Theme.RoundRect(r, rad))
                {
                    using (var b = new SolidBrush(Theme.Blend(Theme.Surface, x.Color, 0.06f))) g.FillPath(b, path);
                    using (var p = new Pen(sel ? Theme.Accent : x.Color, sel ? Theme.S(2) : 1) { DashStyle = DashStyle.Dash }) g.DrawPath(p, path);
                }
                string t = (string.IsNullOrEmpty(x.Badge) ? "" : x.Badge + " ") + (x.AllDay || monthStyle && !x.AllDay ? (x.AllDay ? "" : Fmt.Time(x.Start) + " ") : "") + x.Title;
                TextRenderer.DrawText(g, t, Theme.Small, tr, Theme.Blend(x.Color, Theme.Text, 0.4f), flags);
                if (r.Height >= ChipH * 2 && !x.AllDay)
                    Draw.Text(g, EventLogic.TimeText(x) + (x.SourceName != null ? "  ・  " + x.SourceName : ""), Theme.Small, Theme.SubText, new Rectangle(tr.X, tr.Bottom - Theme.S(2), tr.Width, ChipH));
                return;
            }
            if (x.Kind == CalKind.Task)
            {
                Color back, bar;
                if (x.Done) { back = Theme.SurfaceAlt; bar = Theme.Disabled; }
                else if (x.Priority == TaskItem.High) { back = Theme.DangerSoft; bar = Theme.Danger; }
                else if (x.Priority == TaskItem.Mid) { back = Theme.AccentSoft; bar = Theme.Accent; }
                else { back = Theme.SurfaceAlt; bar = Theme.BorderStrong; }
                using (var path = Theme.RoundRect(r, rad))
                {
                    using (var b = new SolidBrush(back)) g.FillPath(b, path);
                    if (sel) using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawPath(p, path);
                }
                // 丸いチェック (予定と見分ける印)
                int cs = Math.Min(Theme.S(10), ChipH - Theme.S(8));
                var cr = new Rectangle(r.X + Theme.S(4), r.Y + (Math.Min(r.Height, ChipH) - cs) / 2, cs, cs);
                if (x.Done)
                {
                    using (var b = new SolidBrush(bar)) g.FillEllipse(b, cr);
                    using (var p = new Pen(Color.White, Math.Max(1f, 1.5f * (float)Theme.ScaleFactor)))
                        g.DrawLines(p, new[] { new Point(cr.X + cs / 4, cr.Y + cs / 2), new Point(cr.X + cs / 2 - 1, cr.Bottom - cs / 4 - 1), new Point(cr.Right - cs / 5, cr.Y + cs / 4) });
                }
                else using (var p = new Pen(bar, Math.Max(1f, 1.5f * (float)Theme.ScaleFactor))) g.DrawEllipse(p, cr);
                var ttr = new Rectangle(cr.Right + Theme.S(4), tr.Y, r.Right - cr.Right - Theme.S(6), tr.Height);
                string text = (!x.AllDay && (monthStyle || r.Height < ChipH * 2) ? Fmt.Time(x.Start) + " " : "") + x.Title;
                TextRenderer.DrawText(g, text, Theme.Small, ttr, x.Done ? Theme.SubText : Theme.Text, flags);
                if (r.Height >= ChipH * 2 && !x.AllDay)
                    Draw.Text(g, Fmt.Time(x.Start) + "〜" + Fmt.Time(x.End) + "  ・  タスク", Theme.Small, Theme.SubText, new Rectangle(ttr.X, ttr.Bottom - Theme.S(2), ttr.Width, ChipH));
                return;
            }
            // 予定
            if (x.AllDay || (monthStyle && x.MultiDay))
            {
                var fill = x.Color;
                var rr = r;
                using (var path = Theme.RoundRect(rr, rad))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (sel) using (var p = new Pen(Theme.Text, Theme.S(2))) g.DrawPath(p, path);
                }
                string text = (contL ? "◀ " : "") + (x.AllDay ? "" : Fmt.Time(x.Start) + " ") + x.Title + (x.Repeats ? " ↻" : "");
                var r2 = new Rectangle(tr.X, tr.Y, tr.Width - (contR ? Theme.S(12) : 0), tr.Height);
                TextRenderer.DrawText(g, text, Theme.Small, r2, Color.White, flags);
                if (contR) TextRenderer.DrawText(g, "▶", Theme.Small, new Rectangle(r.Right - Theme.S(14), r.Y, Theme.S(12), tr.Height), Color.White, flags);
                return;
            }
            if (monthStyle)
            {
                // 月表示の時刻つき予定: 色の点 + 時刻 + 名前 (地は塗らない)
                if (sel) using (var path = Theme.RoundRect(r, rad)) using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawPath(p, path);
                int ds = Theme.S(8);
                using (var b = new SolidBrush(x.Color)) g.FillEllipse(b, r.X + Theme.S(4), r.Y + (ChipH - ds) / 2, ds, ds);
                var r3 = new Rectangle(r.X + Theme.S(4) + ds + Theme.S(4), tr.Y, r.Width - ds - Theme.S(10), tr.Height);
                TextRenderer.DrawText(g, Fmt.Time(x.Start) + " " + x.Title, Theme.Small, r3, Theme.Text, flags);
                return;
            }
            // 週・日の時間割の予定ブロック
            var soft = Theme.Blend(Theme.Surface, x.Color, 0.2f);
            using (var path = Theme.RoundRect(r, rad))
            {
                using (var b = new SolidBrush(soft)) g.FillPath(b, path);
                using (var p = new Pen(sel ? Theme.Text : Theme.Blend(soft, x.Color, 0.5f), sel ? Theme.S(2) : 1)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(x.Color)) g.FillRectangle(b, r.X + 1, r.Y + Theme.S(2), Theme.S(3), r.Height - Theme.S(4));
            var dark = Theme.Blend(x.Color, Theme.Text, 0.55f);
            bool tall = r.Height >= ChipH * 2;
            string line1 = tall ? x.Title : Fmt.Time(x.Start) + " " + x.Title;
            TextRenderer.DrawText(g, line1 + (x.Repeats ? " ↻" : ""), Theme.Bold.Size > 0 && tall ? Theme.Small : Theme.Small, tr, dark, flags);
            int y = tr.Bottom - Theme.S(2);
            if (tall)
            {
                Draw.Text(g, EventLogic.TimeText(x), Theme.Small, Theme.Blend(x.Color, Theme.SubText, 0.5f), new Rectangle(tr.X, y, tr.Width, ChipH));
                y += ChipH - Theme.S(4);
            }
            if (!string.IsNullOrEmpty(x.Location) && r.Bottom - y >= ChipH - Theme.S(2))
                Draw.Text(g, "@" + x.Location, Theme.Small, Theme.SubText, new Rectangle(tr.X, y, tr.Width, ChipH));
            if (sel && !x.ReadOnly)
                using (var p = new Pen(x.Color, Theme.S(2))) g.DrawLine(p, r.X + r.Width / 2 - Theme.S(8), r.Bottom - Theme.S(3), r.X + r.Width / 2 + Theme.S(8), r.Bottom - Theme.S(3));
        }

        void PaintWeek(Graphics g)
        {
            var now = Clock();
            var today = now.Date;
            var days = VisibleDays();
            var gr = GridRect;
            float cw = gr.Width / (float)days.Count;
            int head = HeadH, adh = AllDayH;

            // 時間の格子
            g.SetClip(gr);
            int off = vbar.Value;
            using (var line = new Pen(Theme.Border))
            using (var half = new Pen(Theme.Blend(Theme.Border, Theme.Surface, 0.5f)) { DashStyle = DashStyle.Dot })
            {
                for (int i = 0; i < days.Count; i++)
                {
                    var col = new Rectangle(gr.X + (int)(i * cw), gr.Y, (int)cw, gr.Height);
                    if (days[i] == anchor && mode == CalMode.Week) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.AccentSoft, 0.5f))) g.FillRectangle(b, col);
                    if (days[i].DayOfWeek == DayOfWeek.Saturday || days[i].DayOfWeek == DayOfWeek.Sunday) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.Background, 0.6f))) g.FillRectangle(b, col);
                }
                for (int h = 0; h <= 24; h++)
                {
                    int y = gr.Y + h * HourH - off;
                    g.DrawLine(line, gr.X, y, gr.Right, y);
                    if (h < 24) g.DrawLine(half, gr.X, y + HourH / 2, gr.Right, y + HourH / 2);
                }
                for (int i = 0; i <= days.Count; i++) g.DrawLine(line, gr.X + (int)(i * cw), gr.Y, gr.X + (int)(i * cw), gr.Bottom);
            }
            // なぞって作っている範囲
            var create = dragging && drag == Drag.Create ? CreateRange() : null;
            if (create != null && !create.Item3)
            {
                int i = days.IndexOf(create.Item1.Date);
                if (i >= 0)
                {
                    int y0 = gr.Y + (int)((create.Item1 - create.Item1.Date).TotalMinutes * HourH / 60) - off;
                    int y1 = gr.Y + (int)((create.Item2 - create.Item1.Date).TotalMinutes * HourH / 60) - off;
                    var r = new Rectangle(gr.X + (int)(i * cw) + Theme.S(3), y0 + 1, (int)cw - Theme.S(6), Math.Max(Theme.S(8), y1 - y0 - Theme.S(2)));
                    using (var path = Theme.RoundRect(r, Theme.S(4)))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(150, Theme.AccentLight))) g.FillPath(b, path);
                        using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawPath(p, path);
                    }
                    Draw.Text(g, Fmt.Time(create.Item1) + "〜" + Fmt.Time(create.Item2) + "  新しい予定", Theme.Small, Theme.Text, new Rectangle(r.X + Theme.S(6), r.Y, r.Width - Theme.S(8), ChipH));
                }
            }
            // 時刻つきの項目
            for (int i = 0; i < days.Count; i++)
            {
                var segs = Segments(days[i]);
                var lanes = Lanes(segs);
                foreach (var sg in segs)
                {
                    int y = gr.Y + (int)((sg.Item2 - days[i]).TotalMinutes * HourH / 60) - off;
                    int h = Math.Max(ChipH, (int)((sg.Item3 - sg.Item2).TotalMinutes * HourH / 60) - Theme.S(2));
                    // 重なるものは横に並べる (その重なりの中の列数で割る)
                    int lanesHere = segs.Where(o => o.Item2 < sg.Item3 && sg.Item2 < o.Item3).Select(o => lanes[o]).DefaultIfEmpty(0).Max() + 1;
                    float lw = (cw - Theme.S(6)) / Math.Max(1, lanesHere);
                    var r = new Rectangle(gr.X + (int)(i * cw) + Theme.S(3) + (int)(lanes[sg] % lanesHere * lw), y + 1, (int)lw - Theme.S(2), h);
                    PaintItem(g, sg.Item1, r, false);
                    bool resizable = !sg.Item1.ReadOnly && sg.Item3 == (sg.Item1.End <= sg.Item1.Start ? sg.Item1.Start.AddMinutes(30) : sg.Item1.End);
                    hits.Add(new Hit { Rect = Rectangle.Intersect(r, gr), Item = sg.Item1, Day = days[i], SegStart = sg.Item2, Resizable = resizable && r.Bottom <= gr.Bottom });
                }
            }
            // 今の時刻の線
            if (days.Contains(today))
            {
                int i = days.IndexOf(today);
                int y = gr.Y + (int)(now.TimeOfDay.TotalMinutes * HourH / 60) - off;
                using (var p = new Pen(Theme.Danger, Theme.S(2))) g.DrawLine(p, gr.X + (int)(i * cw), y, gr.X + (int)((i + 1) * cw), y);
                using (var b = new SolidBrush(Theme.Danger)) g.FillEllipse(b, gr.X + (int)(i * cw) - Theme.S(4), y - Theme.S(4), Theme.S(8), Theme.S(8));
            }
            g.ResetClip();

            // 時刻の目盛り
            g.SetClip(new Rectangle(0, gr.Y, Gutter, gr.Height));
            for (int h = 1; h < 24; h++)
            {
                int y = gr.Y + h * HourH - off;
                TextRenderer.DrawText(g, h + ":00", Theme.Small, new Rectangle(0, y - Theme.Small.Height / 2 - 1, Gutter - Theme.S(6), Theme.Small.Height + 2), Theme.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            g.ResetClip();

            // 見出し (曜日と日付)
            using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, 0, 0, ClientSize.Width, head + adh);
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                var col = new Rectangle(gr.X + (int)(i * cw), 0, (int)cw, head);
                if (d == anchor && mode == CalMode.Week) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, col);
                TextRenderer.DrawText(g, Fmt.DayOfWeek(d), Theme.Small, new Rectangle(col.X, Theme.S(4), col.Width, Theme.Small.Height + Theme.S(2)), DowColor(d), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                string num = mode == CalMode.Day || d.Day == 1 ? d.Month + "/" + d.Day : d.Day.ToString();
                int nh = Theme.Bold.Height + Theme.S(4);
                int nw = Math.Max(nh, Draw.Width(g, num, Theme.Bold) + Theme.S(10));
                var nr = new Rectangle(col.X + (col.Width - nw) / 2, Theme.S(6) + Theme.Small.Height, nw, nh);
                if (d == today) using (var path = Theme.RoundRect(nr, nh / 2f)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                TextRenderer.DrawText(g, num, Theme.Bold, nr, d == today ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (hoverDay == d || anchor == d)
                {
                    int ab = Theme.Small.Height + Theme.S(6);
                    var add = new Rectangle(col.Right - ab - Theme.S(4), Theme.S(4), ab, ab);
                    PaintAddButton(g, add, hoverDay == d);
                    addButtons.Add(new KeyValuePair<Rectangle, DateTime>(add, d));
                }
            }
            // 終日欄 (時刻なしのタスク・終日の予定。複数日は横に伸びる帯)
            var ad = new Rectangle(0, head, ClientSize.Width, adh);
            using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.Background, 0.5f))) g.FillRectangle(b, ad);
            TextRenderer.DrawText(g, "終日", Theme.Small, new Rectangle(0, head, Gutter - Theme.S(6), ChipH + Theme.S(6)), Theme.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (dragging && drag == Drag.Move && pressHit != null && InAllDay(dragPt))
            {
                var d = DayAt(dragPt);
                if (d.HasValue) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.3f))) g.FillRectangle(b, gr.X + (int)(days.IndexOf(d.Value) * cw), head, (int)cw, adh);
            }
            if (create != null && create.Item3)
            {
                int a = days.IndexOf(create.Item1), bb = days.IndexOf(create.Item2);
                if (a >= 0 && bb >= 0) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.45f))) g.FillRectangle(b, gr.X + (int)(a * cw), head, (int)((bb - a + 1) * cw), adh);
            }
            int rows = AllDayRows;
            var allDay = items.Where(x => x.AllDay).ToList();
            var rowOf = Allocate(allDay, days);
            var hidden = new int[days.Count];
            var total = new int[days.Count];
            foreach (var x in rowOf.Keys) for (int c = 0; c < days.Count; c++) if (x.OnDay(days[c])) total[c]++;
            foreach (var kv in rowOf)
            {
                var x = kv.Key; int row = kv.Value;
                int a = Math.Max(0, (x.Start.Date - days[0]).Days), bcol = Math.Min(days.Count - 1, (x.LastDay - days[0]).Days);
                Func<int, bool> fits = c => row < rows && !(row == rows - 1 && total[c] > rows);
                for (int c = a; c <= bcol; c++) if (!fits(c)) hidden[c]++;
                while (a <= bcol && !fits(a)) a++;
                while (bcol >= a && !fits(bcol)) bcol--;
                if (a > bcol) continue;
                var r = new Rectangle(gr.X + (int)(a * cw) + Theme.S(3), head + Theme.S(4) + row * (ChipH + Theme.S(2)), (int)((bcol - a + 1) * cw) - Theme.S(6), ChipH);
                PaintItem(g, x, r, false, x.Start.Date < days[a], x.LastDay > days[bcol]);
                hits.Add(new Hit { Rect = r, Item = x, Day = days[a], SegStart = x.Start, AllDay = true });
            }
            for (int c = 0; c < days.Count; c++)
                if (hidden[c] > 0) Draw.Text(g, "ほか " + hidden[c] + " 件", Theme.Small, Theme.SubText, new Rectangle(gr.X + (int)(c * cw) + Theme.S(8), head + Theme.S(4) + (rows - 1) * (ChipH + Theme.S(2)), (int)cw - Theme.S(10), ChipH));
            using (var line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, head, ClientSize.Width, head);
                g.DrawLine(line, 0, head + adh - 1, ClientSize.Width, head + adh - 1);
                for (int i = 0; i <= days.Count; i++) g.DrawLine(line, gr.X + (int)(i * cw), head, gr.X + (int)(i * cw), head + adh);
            }
        }

        /// <summary>重なった項目を横に並べるための列番号</summary>
        static Dictionary<Tuple<CalItem, DateTime, DateTime>, int> Lanes(List<Tuple<CalItem, DateTime, DateTime>> sorted)
        {
            var lanes = new Dictionary<Tuple<CalItem, DateTime, DateTime>, int>();
            foreach (var t in sorted)
            {
                int lane = 0;
                while (sorted.Any(o => lanes.ContainsKey(o) && lanes[o] == lane && o.Item2 < t.Item3 && t.Item2 < o.Item3)) lane++;
                lanes[t] = lane;
            }
            return lanes;
        }

        string GhostLabel(CalItem x, Tuple<DateTime, bool> when)
        {
            if (when == null) return "ここには置けません";
            var start = NewStart(x, when);
            if (when.Item2) return Fmt.MonthDay(start) + " " + Fmt.Time(start) + (x.Kind == CalKind.Event ? "〜" + Fmt.Time(start + (x.AllDay ? TimeSpan.FromHours(1) : x.End - x.Start)) : "");
            if (mode == CalMode.Month && !x.AllDay) return Fmt.MonthDay(start) + " " + Fmt.Time(start);
            var days = (x.LastDay - x.Start.Date).Days;
            return Fmt.MonthDay(start) + (x.AllDay && days > 0 ? "〜" + Fmt.MonthDay(start.AddDays(days)) : "") + (mode != CalMode.Month ? " (終日)" : "");
        }

        /// <summary>ドロップ先から、その項目の新しい始まり</summary>
        DateTime NewStart(CalItem x, Tuple<DateTime, bool> when)
        {
            if (when.Item2) return when.Item1 - (pressHit != null ? pressHit.SegStart - x.Start : TimeSpan.Zero);
            var day = when.Item1.Date.AddDays(-grabDayOffset);
            if (mode == CalMode.Month && !x.AllDay) return day + x.Start.TimeOfDay;
            return day;
        }

        void PaintGhost(Graphics g)
        {
            var x = pressHit.Item;
            var when = DropTarget(dragPt);
            string text = x.Title + "  →  " + GhostLabel(x, when);
            int w = Math.Min(Theme.S(340), Draw.Width(g, text, Theme.Small) + Theme.S(20));
            var r = new Rectangle(dragPt.X + Theme.S(12), dragPt.Y + Theme.S(8), w, ChipH + Theme.S(4));
            if (r.Right > ClientSize.Width) r.X = ClientSize.Width - r.Width - 2;
            if (r.Bottom > ClientSize.Height) r.Y = dragPt.Y - r.Height - Theme.S(8);
            using (var path = Theme.RoundRect(r, Theme.S(4)))
            using (var b = new SolidBrush(x.Kind == CalKind.Event ? x.Color : Theme.Accent)) g.FillPath(b, path);
            Draw.Text(g, text, Theme.Small, Color.White, new Rectangle(r.X + Theme.S(8), r.Y, r.Width - Theme.S(12), r.Height));
            if (mode != CalMode.Month && when != null && when.Item2)
            {
                // 置く場所の目安
                var days = VisibleDays(); var gr = GridRect; float cw = gr.Width / (float)days.Count;
                var start = NewStart(x, when);
                int i = days.IndexOf(start.Date);
                if (i < 0) return;
                int y = gr.Y + (int)(start.TimeOfDay.TotalMinutes * HourH / 60) - vbar.Value;
                var len = x.AllDay ? TimeSpan.FromHours(x.Kind == CalKind.Task ? 0.5 : 1) : x.End - x.Start;
                int h = Math.Max(ChipH, (int)(len.TotalMinutes * HourH / 60));
                using (var p = new Pen(Theme.Accent, Theme.S(2)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, gr.X + (int)(i * cw) + Theme.S(3), y, (int)cw - Theme.S(6), h);
            }
        }

        void PaintCreate(Graphics g)
        {
            var c = CreateRange();
            if (c == null) return;
            string text = c.Item3 ? "新しい予定  " + Fmt.MonthDay(c.Item1) + (c.Item2 > c.Item1 ? "〜" + Fmt.MonthDay(c.Item2) : "") + " 終日" : "新しい予定  " + Fmt.MonthDay(c.Item1) + " " + Fmt.Time(c.Item1) + "〜" + Fmt.Time(c.Item2);
            int w = Math.Min(Theme.S(340), Draw.Width(g, text, Theme.Small) + Theme.S(20));
            var r = new Rectangle(dragPt.X + Theme.S(12), dragPt.Y + Theme.S(8), w, ChipH + Theme.S(4));
            if (r.Right > ClientSize.Width) r.X = ClientSize.Width - r.Width - 2;
            if (r.Bottom > ClientSize.Height) r.Y = dragPt.Y - r.Height - Theme.S(8);
            using (var path = Theme.RoundRect(r, Theme.S(4))) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            Draw.Text(g, text, Theme.Small, Color.White, new Rectangle(r.X + Theme.S(8), r.Y, r.Width - Theme.S(12), r.Height));
        }

        // ------------------------------------------------------------ 位置から日付・時刻

        bool InAllDay(Point p) { return mode != CalMode.Month && p.Y >= HeadH && p.Y < HeadH + AllDayH; }

        DateTime? DayAt(Point p)
        {
            var days = VisibleDays();
            if (mode == CalMode.Month)
            {
                int head = HeadH;
                if (p.Y < head || p.X < 0 || p.X >= ClientSize.Width) return null;
                int c = (int)(p.X / (ClientSize.Width / 7f)), r = (int)((p.Y - head) / ((ClientSize.Height - head) / 6f));
                if (c < 0 || c > 6 || r < 0 || r > 5) return null;
                return days[r * 7 + c];
            }
            var gr = GridRect;
            if (p.X < gr.X || p.X >= gr.Right) return null;
            int i = (int)((p.X - gr.X) / (gr.Width / (float)days.Count));
            return i >= 0 && i < days.Count ? days[i] : (DateTime?)null;
        }

        /// <summary>格子の位置の時刻 (15 分きざみ。floor なら切り捨て)</summary>
        DateTime? TimeAt(Point p, int offsetY, bool floor = false)
        {
            var d = DayAt(p);
            var gr = GridRect;
            if (!d.HasValue || p.Y < gr.Y) return null;
            int min = (p.Y - offsetY - gr.Y + vbar.Value) * 60 / HourH;
            min = Math.Max(0, Math.Min(24 * 60 - 15, (int)(floor ? Math.Floor(min / 15.0) : Math.Round(min / 15.0)) * 15));
            return d.Value.AddMinutes(min);
        }

        /// <summary>ドロップ先 (日時, 時刻つきか)。置けなければ null</summary>
        Tuple<DateTime, bool> DropTarget(Point p)
        {
            if (mode == CalMode.Month)
            {
                var d = DayAt(p);
                return d.HasValue ? Tuple.Create(d.Value, false) : null;
            }
            if (InAllDay(p)) { var d = DayAt(p); return d.HasValue ? Tuple.Create(d.Value, false) : null; }
            var t = TimeAt(p, pressHit != null && !pressHit.AllDay ? grabOffsetY : 0);
            return t.HasValue ? Tuple.Create(t.Value, true) : null;
        }

        Hit HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Rect.Contains(p)) return hits[i]; return null; }
        bool OnResizeEdge(Hit h, Point p) { return h != null && h.Resizable && !h.AllDay && p.Y >= h.Rect.Bottom - Theme.S(6); }

        /// <summary>その日の場所 (メニューを出す位置)</summary>
        public Point DayPoint(DateTime d)
        {
            var days = VisibleDays();
            int i = days.IndexOf(d.Date);
            if (i < 0) return new Point(Theme.S(20), Theme.S(20));
            if (mode == CalMode.Month) { var c = MonthCell(i); return new Point(c.X + Theme.S(8), c.Y + Theme.S(24)); }
            var gr = GridRect; float cw = gr.Width / (float)days.Count;
            return new Point(gr.X + (int)(i * cw) + Theme.S(8), HeadH);
        }

        // ------------------------------------------------------------ マウス

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            foreach (var a in addButtons)
                if (a.Key.Contains(e.Location))
                {
                    if (!Fixed) Day = a.Value;
                    if (AddMenuRequested != null) AddMenuRequested(a.Value, new Point(a.Key.Left, a.Key.Bottom));
                    else if (AddRequested != null) AddRequested(a.Value, false);
                    return;
                }
            drag = Drag.None; pressHit = null; pressTime = null; pressDay = null;
            var h = HitAt(e.Location);
            if (h != null)
            {
                SelectedKey = h.Item.Key;
                if (e.Clicks >= 2) { if (ItemOpened != null) ItemOpened(h.Item); return; }
                pressPt = e.Location; grabOffsetY = e.Y - h.Rect.Y;
                var pd = DayAt(e.Location);
                grabDayOffset = pd.HasValue ? Math.Max(0, (pd.Value - h.Item.Start.Date).Days) : 0;
                if (!h.Item.ReadOnly) { pressHit = h; drag = OnResizeEdge(h, e.Location) ? Drag.Resize : Drag.Move; }
                if (anchor != h.Day && !Fixed && mode != CalMode.Month) Day = h.Day;
                else if (anchor != (pd ?? h.Day) && !Fixed && mode == CalMode.Month) Day = pd ?? h.Day;
                else Invalidate();
                return;
            }
            SelectedKey = null;
            var d = DayAt(e.Location);
            if (d.HasValue)
            {
                if (!Fixed || d.Value == anchor) { if (anchor != d.Value) Day = d.Value; else Invalidate(); }
                bool timed = mode != CalMode.Month && !InAllDay(e.Location) && e.Y >= GridRect.Y;
                if (e.Clicks >= 2)
                {
                    if (timed)
                    {
                        var t = TimeAt(e.Location, 0, true);
                        if (t.HasValue)
                        {
                            var v = t.Value; var s = v.Date.AddHours(v.Hour).AddMinutes(v.Minute >= 30 ? 30 : 0);
                            RequestEvent(s, s.AddHours(1), false);
                            return;
                        }
                    }
                    RequestEvent(d.Value, d.Value, true);
                    return;
                }
                // なぞって予定を作る準備
                pressPt = e.Location; drag = Drag.Create;
                pressAllDay = !timed;
                pressDay = d;
                pressTime = timed ? TimeAt(e.Location, 0, true) : null;
            }
            else Invalidate();
        }

        void RequestEvent(DateTime s, DateTime e, bool allDay)
        {
            if (EventRequested != null) EventRequested(s, e, allDay);
            else if (AddRequested != null) AddRequested(s, !allDay);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != Drag.None && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - pressPt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - pressPt.Y) > SystemInformation.DragSize.Height))
                {
                    dragging = true;
                    Cursor = drag == Drag.Resize ? Cursors.SizeNS : drag == Drag.Move ? Cursors.SizeAll : Cursors.Cross;
                }
                if (dragging)
                {
                    dragPt = e.Location;
                    if (drag == Drag.Resize) resizeEnd = ResizeTarget(e.Location);
                    // 端に寄せたらスクロール
                    if (mode != CalMode.Month)
                    {
                        var gr = GridRect;
                        if (e.Y > ClientSize.Height - Theme.S(20)) SetScroll(vbar.Value + HourH / 4);
                        else if (e.Y < gr.Y + Theme.S(16) && e.Y >= gr.Y) SetScroll(vbar.Value - HourH / 4);
                    }
                    Invalidate();
                }
                return;
            }
            var d = DayAt(e.Location);
            if (d != hoverDay) { hoverDay = d; Invalidate(); }
            bool overButton = addButtons.Any(a => a.Key.Contains(e.Location));
            var h = HitAt(e.Location);
            Cursor = OnResizeEdge(h, e.Location) ? Cursors.SizeNS : overButton || h != null ? Cursors.Hand : Cursors.Default;
        }

        DateTime? ResizeTarget(Point p)
        {
            if (pressHit == null) return null;
            var x = pressHit.Item;
            var gr = GridRect;
            int min = (p.Y - gr.Y + vbar.Value) * 60 / HourH;
            min = Math.Max(0, Math.Min(24 * 60, (int)Math.Round(min / 15.0) * 15));
            var end = pressHit.SegStart.Date.AddMinutes(min);
            if (end < x.Start.AddMinutes(15)) end = x.Start.AddMinutes(15);
            return end;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = pressHit; bool wasDragging = dragging; var mode0 = drag;
            dragPt = e.Location;
            var create = mode0 == Drag.Create && wasDragging ? CreateRange() : null;
            var rEnd = mode0 == Drag.Resize && wasDragging ? ResizeTarget(e.Location) : null;
            pressHit = null; dragging = false; drag = Drag.None; resizeEnd = null; Cursor = Cursors.Default;
            if (create != null) { Invalidate(); RequestEvent(create.Item1, create.Item2, create.Item3); return; }
            if (h == null || !wasDragging) { Invalidate(); return; }
            var x = h.Item;
            if (mode0 == Drag.Resize)
            {
                if (rEnd.HasValue && rEnd.Value != x.End && ItemResized != null) ItemResized(x, rEnd.Value);
                Invalidate();
                return;
            }
            pressHit = h;
            var target = DropTarget(e.Location);
            DateTime when = target != null ? NewStart(x, target) : DateTime.MinValue;
            pressHit = null;
            if (target != null && ItemMoved != null)
            {
                bool withTime = target.Item2 || (mode == CalMode.Month && !x.AllDay);
                bool same = x.AllDay == !withTime && x.Start == when;
                if (!same) { if (!Fixed) anchor = when.Date; ItemMoved(x, when, withTime); Reload(); Changed(); }
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverDay != null && drag == Drag.None) { hoverDay = null; Invalidate(); } }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ------------------------------------------------------------ キーボード

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.Enter: case Keys.Space: return true; }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Modifiers != Keys.None) return;
            if (!HandleKey(e.KeyCode)) return;
            e.Handled = true; e.SuppressKeyPress = true;
        }

        /// <summary>キー操作 (テストからも呼ぶ)。扱ったら true</summary>
        public bool HandleKey(Keys k)
        {
            var sel = SelectedItem;
            switch (k)
            {
                case Keys.Left: MoveDay(-1); break;
                case Keys.Right: MoveDay(1); break;
                case Keys.Up: if (mode == CalMode.Month) MoveDay(-7); else SetScroll(vbar.Value - HourH); break;
                case Keys.Down: if (mode == CalMode.Month) MoveDay(7); else SetScroll(vbar.Value + HourH); break;
                case Keys.PageUp: Navigate(-1); break;
                case Keys.PageDown: Navigate(1); break;
                case Keys.Home: if (!Fixed) GoToday(); break;
                case Keys.Enter:
                    if (sel != null) { if (ItemOpened != null) ItemOpened(sel); }
                    else if (AddMenuRequested != null) AddMenuRequested(anchor, DayPoint(anchor));
                    else if (AddRequested != null) AddRequested(anchor, false);
                    break;
                case Keys.Space: if (sel != null && sel.Kind == CalKind.Task && ItemToggled != null) ItemToggled(sel); break;
                case Keys.Delete: if (sel != null && !sel.ReadOnly && ItemDeleteRequested != null) ItemDeleteRequested(sel); break;
                default: return false;
            }
            return true;
        }

        void MoveDay(int days)
        {
            if (Fixed) return;
            SelectedKey = null;
            var before = VisibleDays();
            Day = anchor.AddDays(days);
            if (mode != CalMode.Month && !before.Contains(anchor)) UpdateScroll(false);
        }

        // ------------------------------------------------------------ テスト用 (マウス操作をまねる)

        /// <summary>時間割の 2 つの時刻をなぞったのと同じ (予定の作成を頼む)</summary>
        public void SimulateCreate(DateTime a, DateTime b, bool allDay)
        {
            if (allDay) RequestEvent(a < b ? a.Date : b.Date, a < b ? b.Date : a.Date, true);
            else RequestEvent(a < b ? a : b, a < b ? b : a, false);
        }
        public void SimulateMove(CalItem x, DateTime when, bool withTime) { if (!x.ReadOnly && ItemMoved != null) { ItemMoved(x, when, withTime); Reload(); } }
        public void SimulateResize(CalItem x, DateTime end) { if (!x.ReadOnly && ItemResized != null) { ItemResized(x, end); Reload(); } }
    }
}
