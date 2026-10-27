// Teal Tasks - カレンダー (月・週・日)。GDI+ で自前で描く。週は月曜はじまり
//  クリック: 日を選ぶ / 日の右上の「＋」: その日に追加 / ダブルクリック: 空き場所なら追加、タスクなら編集
//  ドラッグ: タスクを別の日 (週・日では別の時刻。15 分きざみ) へ動かす。週・日の「終日」欄に落とすと時刻なしになる
//  キー: ←→↑↓ 日を移動 / PageUp・PageDown 前後の期間 / Home 今日 / Enter 選んだ日に追加 / Space 選んだタスクの完了
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
        class Hit { public Rectangle Rect; public TaskItem Task; public DateTime Day; public bool AllDay; }

        public Func<IEnumerable<TaskItem>> Source = () => new TaskItem[0];
        public Func<DateTime> Clock = () => DateTime.Now;
        /// <summary>day, withTime</summary>
        public event Action<DateTime, bool> AddRequested;
        public event Action<TaskItem> TaskOpened;
        public event Action<TaskItem> TaskToggled;
        /// <summary>task, 新しい日時, 時刻つきか (受け取った側で TaskItem.SetDue して保存する)</summary>
        public event Action<TaskItem, DateTime, bool> TaskMoved;
        public event EventHandler ViewChanged;

        CalMode mode = CalMode.Month;
        DateTime anchor = DateTime.Today;
        public TaskItem SelectedTask;
        readonly List<Hit> hits = new List<Hit>();
        readonly List<KeyValuePair<Rectangle, DateTime>> addButtons = new List<KeyValuePair<Rectangle, DateTime>>();
        readonly VScrollBar vbar = new VScrollBar();
        DateTime? hoverDay;
        // ドラッグ
        Hit pressHit; Point pressPt; bool dragging; Point dragPt; int grabOffsetY;

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
            set { if (mode == value) return; mode = value; vbar.Visible = mode != CalMode.Month; UpdateScroll(true); Changed(); }
        }
        public DateTime Day
        {
            get { return anchor; }
            set { anchor = value.Date; Changed(); }
        }
        void Changed() { Invalidate(); if (ViewChanged != null) ViewChanged(this, EventArgs.Empty); }

        public void Navigate(int dir)
        {
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
                var tasks = Source().ToList();
                int max = VisibleDays().Select(d => tasks.Count(t => t.HasDue && !t.HasTime && t.DueDate.Value == d)).DefaultIfEmpty(0).Max();
                return Math.Max(1, Math.Min(3, max));
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
                // 7 時から (それより前のタスクがあればそこから)
                int first = 7 * 60;
                var days = VisibleDays();
                foreach (var t in Source()) if (t.HasTime && days.Contains(t.DueDate.Value)) first = Math.Min(first, (int)t.DueAt.Value.TimeOfDay.TotalMinutes);
                SetScroll(first * HourH / 60 - Theme.S(8));
            }
            else SetScroll(vbar.Value);
        }
        void SetScroll(int v) { vbar.Value = Math.Max(0, Math.Min(Math.Max(0, vbar.Maximum - vbar.LargeChange + 1), v)); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(false); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (mode == CalMode.Month) Navigate(e.Delta > 0 ? -1 : 1);
            else SetScroll(vbar.Value - e.Delta * HourH / 120);
        }

        // ------------------------------------------------------------ 描画

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear(); addButtons.Clear();
            if (mode == CalMode.Month) PaintMonth(g); else PaintWeek(g);
            if (dragging && pressHit != null) PaintGhost(g);
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

        void PaintMonth(Graphics g)
        {
            var today = Clock().Date;
            var tasks = Source().ToList();
            int w = ClientSize.Width, head = HeadH;
            float cw = w / 7f, ch = (ClientSize.Height - head) / 6f;
            using (var bg = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(bg, 0, 0, w, head);
            for (int i = 0; i < 7; i++)
                TextRenderer.DrawText(g, DowMon[i], Theme.Small, new Rectangle((int)(i * cw), 0, (int)cw, head), i == 6 ? Theme.Danger : i == 5 ? Theme.Accent : Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            var days = VisibleDays();
            using (var line = new Pen(Theme.Border))
            {
                for (int k = 0; k < 42; k++)
                {
                    var d = days[k];
                    var cell = new Rectangle((int)((k % 7) * cw), head + (int)((k / 7) * ch), (int)cw, (int)ch);
                    bool other = d.Month != anchor.Month;
                    if (other) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.Background, 0.7f))) g.FillRectangle(b, cell);
                    if (d == anchor) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, cell);
                    if (hoverDay == d && d != anchor) using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.SurfaceAlt, 0.6f))) g.FillRectangle(b, cell);
                    if (dragging && DayAt(dragPt) == d) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.3f))) g.FillRectangle(b, cell);
                    g.DrawRectangle(line, cell.X, cell.Y, cell.Width, cell.Height);

                    // 日付
                    string num = d.Day == 1 ? d.Month + "/" + d.Day : d.Day.ToString();
                    int nh = Theme.Small.Height + Theme.S(6);
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
                        using (var path = Theme.RoundRect(add, nh / 2f)) using (var b = new SolidBrush(hoverDay == d ? Theme.Accent : Theme.Surface)) g.FillPath(b, path);
                        TextRenderer.DrawText(g, "＋", Theme.Small, add, hoverDay == d ? Color.White : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                        addButtons.Add(new KeyValuePair<Rectangle, DateTime>(add, d));
                    }

                    // タスク
                    var list = TaskLogic.OnDay(tasks, d);
                    int y = numRect.Bottom + Theme.S(3);
                    int room = (cell.Bottom - Theme.S(3) - y) / (ChipH + Theme.S(2));
                    int show = Math.Min(list.Count, Math.Max(0, room));
                    for (int i = 0; i < show; i++)
                    {
                        var r = new Rectangle(cell.X + Theme.S(3), y, cell.Width - Theme.S(6), ChipH);
                        PaintChip(g, list[i], r, true);
                        hits.Add(new Hit { Rect = r, Task = list[i], Day = d });
                        y += ChipH + Theme.S(2);
                    }
                    // 入りきらない数は日付の横に (クリックでその日を選び、「日」表示で全部見られる)
                    if (list.Count > show)
                    {
                        int mx = numRect.Right + Theme.S(4), mr = (hoverDay == d || anchor == d) ? cell.Right - nh - Theme.S(8) : cell.Right - Theme.S(4);
                        Draw.Text(g, "ほか " + (list.Count - show) + " 件", Theme.Small, d == anchor ? Theme.Accent : Theme.SubText, new Rectangle(mx, numRect.Y, Math.Max(0, mr - mx), nh));
                    }
                }
            }
        }

        /// <summary>タスクの小さな札。優先度で色を変え、完了は薄く打ち消し線</summary>
        void PaintChip(Graphics g, TaskItem t, Rectangle r, bool showTime)
        {
            Color back, bar;
            if (t.Done) { back = Theme.SurfaceAlt; bar = Theme.Disabled; }
            else if (t.Priority == TaskItem.High) { back = Theme.DangerSoft; bar = Theme.Danger; }
            else if (t.Priority == TaskItem.Mid) { back = Theme.AccentSoft; bar = Theme.Accent; }
            else { back = Theme.SurfaceAlt; bar = Theme.BorderStrong; }
            bool sel = SelectedTask != null && SelectedTask.Id == t.Id;
            using (var path = Theme.RoundRect(r, Theme.S(4)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (sel) using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(bar)) g.FillRectangle(b, r.X, r.Y + Theme.S(2), Theme.S(3), r.Height - Theme.S(4));
            string text = (showTime && t.HasTime ? Fmt.Time(t.DueAt.Value) + " " : "") + (t.Done ? "✓ " : "") + t.Title;
            var tr = new Rectangle(r.X + Theme.S(6), r.Y, r.Width - Theme.S(8), Math.Min(r.Height, ChipH));
            Draw.Text(g, text, Theme.Small, t.Done ? Theme.SubText : Theme.Text, tr);
            if (r.Height >= ChipH * 2 && t.DurationMin > 0)
                Draw.Text(g, Fmt.Time(t.DueAt.Value) + "〜" + Fmt.Time(t.DueAt.Value.AddMinutes(t.DurationMin)), Theme.Small, Theme.SubText, new Rectangle(tr.X, tr.Bottom - Theme.S(2), tr.Width, ChipH));
        }

        void PaintWeek(Graphics g)
        {
            var now = Clock();
            var today = now.Date;
            var tasks = Source().ToList();
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
            // タスクのブロック
            for (int i = 0; i < days.Count; i++)
            {
                var list = tasks.Where(t => t.HasTime && t.DueDate.Value == days[i]).OrderBy(t => t.DueAt.Value).ThenByDescending(t => t.DurationMin).ToList();
                var lanes = Lanes(list);
                int laneCount = lanes.Count == 0 ? 1 : lanes.Values.Max() + 1;
                foreach (var t in list)
                {
                    int min = (int)t.DueAt.Value.TimeOfDay.TotalMinutes;
                    int dur = Math.Max(30, t.DurationMin);
                    int y = gr.Y + min * HourH / 60 - off;
                    int h = Math.Max(ChipH, dur * HourH / 60 - Theme.S(2));
                    // 重なるものは横に並べる (その重なりの中の列数で割る)
                    int lanesHere = list.Where(o => Overlaps(o, t)).Select(o => lanes[o]).DefaultIfEmpty(0).Max() + 1;
                    lanesHere = Math.Min(laneCount, Math.Max(lanesHere, 1));
                    float lw = (cw - Theme.S(6)) / lanesHere;
                    var r = new Rectangle(gr.X + (int)(i * cw) + Theme.S(3) + (int)(lanes[t] % lanesHere * lw), y + 1, (int)lw - Theme.S(2), h);
                    PaintChip(g, t, r, true);
                    hits.Add(new Hit { Rect = Rectangle.Intersect(r, gr), Task = t, Day = days[i] });
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
                    using (var path = Theme.RoundRect(add, ab / 2f)) using (var b = new SolidBrush(hoverDay == d ? Theme.Accent : Theme.Surface)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, "＋", Theme.Small, add, hoverDay == d ? Color.White : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    addButtons.Add(new KeyValuePair<Rectangle, DateTime>(add, d));
                }
            }
            // 終日 (時刻なし) 欄
            var ad = new Rectangle(0, head, ClientSize.Width, adh);
            using (var b = new SolidBrush(Theme.Blend(Theme.Surface, Theme.Background, 0.5f))) g.FillRectangle(b, ad);
            TextRenderer.DrawText(g, "終日", Theme.Small, new Rectangle(0, head, Gutter - Theme.S(6), ChipH + Theme.S(6)), Theme.SubText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (dragging && pressHit != null && InAllDay(dragPt))
            {
                var d = DayAt(dragPt);
                if (d.HasValue) using (var b = new SolidBrush(Theme.Blend(Theme.AccentSoft, Theme.AccentLight, 0.3f))) g.FillRectangle(b, gr.X + (int)(days.IndexOf(d.Value) * cw), head, (int)cw, adh);
            }
            int rows = AllDayRows;
            for (int i = 0; i < days.Count; i++)
            {
                var list = tasks.Where(t => t.HasDue && !t.HasTime && t.DueDate.Value == days[i]).OrderByDescending(t => t.Priority).ThenBy(t => t.Title, StringComparer.CurrentCulture).ToList();
                int y = head + Theme.S(4);
                int show = list.Count <= rows ? list.Count : rows - 1;
                for (int k = 0; k < show; k++)
                {
                    var r = new Rectangle(gr.X + (int)(i * cw) + Theme.S(3), y, (int)cw - Theme.S(6), ChipH);
                    PaintChip(g, list[k], r, false);
                    hits.Add(new Hit { Rect = r, Task = list[k], Day = days[i], AllDay = true });
                    y += ChipH + Theme.S(2);
                }
                if (list.Count > show) Draw.Text(g, "ほか " + (list.Count - show) + " 件", Theme.Small, Theme.SubText, new Rectangle(gr.X + (int)(i * cw) + Theme.S(8), y, (int)cw - Theme.S(10), ChipH));
            }
            using (var line = new Pen(Theme.Border))
            {
                g.DrawLine(line, 0, head, ClientSize.Width, head);
                g.DrawLine(line, 0, head + adh - 1, ClientSize.Width, head + adh - 1);
                for (int i = 0; i <= days.Count; i++) g.DrawLine(line, gr.X + (int)(i * cw), head, gr.X + (int)(i * cw), head + adh);
            }
        }

        static bool Overlaps(TaskItem a, TaskItem b)
        {
            var a0 = a.DueAt.Value; var a1 = a0.AddMinutes(Math.Max(30, a.DurationMin));
            var b0 = b.DueAt.Value; var b1 = b0.AddMinutes(Math.Max(30, b.DurationMin));
            return a0 < b1 && b0 < a1;
        }

        /// <summary>重なったタスクを横に並べるための列番号</summary>
        static Dictionary<TaskItem, int> Lanes(List<TaskItem> sorted)
        {
            var lanes = new Dictionary<TaskItem, int>();
            foreach (var t in sorted)
            {
                int lane = 0;
                while (sorted.Any(o => lanes.ContainsKey(o) && lanes[o] == lane && Overlaps(o, t))) lane++;
                lanes[t] = lane;
            }
            return lanes;
        }

        void PaintGhost(Graphics g)
        {
            var t = pressHit.Task;
            var when = DropTarget(dragPt);
            string label = when == null ? "ここには置けません" : (when.Item2 ? Fmt.MonthDay(when.Item1) + " " + Fmt.Time(when.Item1) : Fmt.MonthDay(when.Item1) + (mode == CalMode.Month && t.HasTime ? " " + Fmt.Time(t.DueAt.Value) : "") + (mode != CalMode.Month ? " (終日)" : ""));
            string text = t.Title + "  →  " + label;
            int w = Math.Min(Theme.S(320), Draw.Width(g, text, Theme.Small) + Theme.S(20));
            var r = new Rectangle(dragPt.X + Theme.S(12), dragPt.Y + Theme.S(8), w, ChipH + Theme.S(4));
            if (r.Right > ClientSize.Width) r.X = ClientSize.Width - r.Width - 2;
            if (r.Bottom > ClientSize.Height) r.Y = dragPt.Y - r.Height - Theme.S(8);
            using (var path = Theme.RoundRect(r, Theme.S(4)))
            {
                using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            }
            Draw.Text(g, text, Theme.Small, Color.White, new Rectangle(r.X + Theme.S(8), r.Y, r.Width - Theme.S(12), r.Height));
            if (mode != CalMode.Month && when != null && when.Item2)
            {
                // 置く場所の目安
                var days = VisibleDays(); var gr = GridRect; float cw = gr.Width / (float)days.Count;
                int i = days.IndexOf(when.Item1.Date);
                int y = gr.Y + (int)(when.Item1.TimeOfDay.TotalMinutes * HourH / 60) - vbar.Value;
                int h = Math.Max(ChipH, Math.Max(30, t.DurationMin) * HourH / 60);
                using (var p = new Pen(Theme.Accent, Theme.S(2)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, gr.X + (int)(i * cw) + Theme.S(3), y, (int)cw - Theme.S(6), h);
            }
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

        /// <summary>格子の位置の時刻 (15 分きざみ)</summary>
        DateTime? TimeAt(Point p, int offsetY)
        {
            var d = DayAt(p);
            var gr = GridRect;
            if (!d.HasValue || p.Y < gr.Y) return null;
            int min = (p.Y - offsetY - gr.Y + vbar.Value) * 60 / HourH;
            min = Math.Max(0, Math.Min(24 * 60 - 15, (int)Math.Round(min / 15.0) * 15));
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
            var t = TimeAt(p, pressHit != null && pressHit.Task.HasTime && !pressHit.AllDay ? grabOffsetY : 0);
            return t.HasValue ? Tuple.Create(t.Value, true) : null;
        }

        Hit HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Rect.Contains(p)) return hits[i]; return null; }

        // ------------------------------------------------------------ マウス

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            foreach (var a in addButtons) if (a.Key.Contains(e.Location)) { Day = a.Value; if (AddRequested != null) AddRequested(a.Value, false); return; }
            var h = HitAt(e.Location);
            if (h != null)
            {
                SelectedTask = h.Task;
                pressHit = h; pressPt = e.Location; grabOffsetY = e.Y - h.Rect.Y;
                if (e.Clicks >= 2) { pressHit = null; if (TaskOpened != null) TaskOpened(h.Task); return; }
                if (anchor != h.Day) Day = h.Day; else Invalidate();
                return;
            }
            SelectedTask = null;
            var d = DayAt(e.Location);
            if (d.HasValue)
            {
                Day = d.Value;
                if (e.Clicks >= 2 && AddRequested != null)
                {
                    if (mode != CalMode.Month && !InAllDay(e.Location) && e.Y >= GridRect.Y)
                    {
                        var t = TimeAt(e.Location, 0);
                        if (t.HasValue) { var v = t.Value; int m = v.Minute >= 30 ? 30 : 0; AddRequested(v.Date.AddHours(v.Hour).AddMinutes(m), true); return; }
                    }
                    AddRequested(d.Value, false);
                }
            }
            else Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressHit != null && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - pressPt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - pressPt.Y) > SystemInformation.DragSize.Height)) { dragging = true; Cursor = Cursors.SizeAll; }
                if (dragging)
                {
                    dragPt = e.Location;
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
            Cursor = overButton || HitAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var h = pressHit; bool wasDragging = dragging;
            pressHit = null; dragging = false; Cursor = Cursors.Default;
            if (h == null) return;
            if (wasDragging)
            {
                var target = DropTarget(e.Location);
                if (target != null && TaskMoved != null)
                {
                    var t = h.Task;
                    DateTime when = target.Item1; bool withTime = target.Item2;
                    // 月表示では時刻はそのまま、週・日の終日欄に置いたら時刻なし
                    if (mode == CalMode.Month && t.HasTime) { when = when.Date + t.DueAt.Value.TimeOfDay; withTime = true; }
                    bool same = t.HasDue && t.HasTime == withTime && t.DueAt.Value == when;
                    if (!same) { anchor = when.Date; TaskMoved(t, when, withTime); Changed(); }
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hoverDay != null && pressHit == null) { hoverDay = null; Invalidate(); } }
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
            if (e.Modifiers != Keys.None) return;
            switch (e.KeyCode)
            {
                case Keys.Left: MoveDay(-1); break;
                case Keys.Right: MoveDay(1); break;
                case Keys.Up: if (mode == CalMode.Month) MoveDay(-7); else SetScroll(vbar.Value - HourH); break;
                case Keys.Down: if (mode == CalMode.Month) MoveDay(7); else SetScroll(vbar.Value + HourH); break;
                case Keys.PageUp: Navigate(-1); break;
                case Keys.PageDown: Navigate(1); break;
                case Keys.Home: GoToday(); break;
                case Keys.Enter:
                    if (SelectedTask != null && TaskOpened != null) TaskOpened(SelectedTask);
                    else if (AddRequested != null) AddRequested(anchor, false);
                    break;
                case Keys.Space: if (SelectedTask != null && TaskToggled != null) TaskToggled(SelectedTask); break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }

        void MoveDay(int days)
        {
            SelectedTask = null;
            var before = VisibleDays();
            Day = anchor.AddDays(days);
            if (mode != CalMode.Month && !before.Contains(anchor)) UpdateScroll(false);
        }
    }
}
