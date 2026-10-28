// Teal Plan - ダイアログ: 予定の追加・編集 / くり返す予定の「この回だけ・すべて」
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    /// <summary>予定の追加・編集。削除ボタンを押すと DialogResult.Abort で閉じる</summary>
    public class EventEditForm : Form
    {
        public readonly EventItem Event;
        public readonly TextBox tbTitle = new TextBox(), tbLocation = new TextBox(), tbTags = new TextBox(), tbMemo = new TextBox();
        public readonly CheckBox chkAllDay = FormGrid.Check("終日"), chkUntil = FormGrid.Check("この日まで");
        public readonly DateTimePicker dtStart = FormGrid.DatePicker(), dtEnd = FormGrid.DatePicker(), tmStart = TimePicker(), tmEnd = TimePicker(), dtUntil = FormGrid.DatePicker();
        public readonly ComboBox cbRepeat = Lay.Combo(TaskLogic.RepeatLabels), cbRemind = Lay.Combo(EventLogic.RemindChoices.Select(EventLogic.RemindLabel).ToArray());
        public readonly ColorSwatches Colors = new ColorSwatches();
        readonly WrapLabel error = new WrapLabel();
        public TagSuggest Tags { get; private set; }

        static DateTimePicker TimePicker()
        {
            return new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "H:mm", ShowUpDown = true, Font = Theme.Body, Width = Theme.S(80) };
        }

        public EventEditForm(EventItem ev, bool isNew)
        {
            Event = ev;
            DialogKit.Setup(this, isNew ? "予定を追加" : "予定を編集");
            var grid = new FormGrid(2, 250);
            Ui.SetCue(tbTitle, "例: 歯医者 / チーム定例");
            grid.Add("予定の名前 (必須)", new InputFrame(tbTitle), 2);
            grid.AddRaw(chkAllDay, 2);
            grid.Add("始まり", FormGrid.Inline(dtStart, tmStart));
            grid.Add("終わり", FormGrid.Inline(dtEnd, tmEnd));
            Ui.SetCue(tbLocation, "例: 駅前クリニック");
            grid.Add("場所", new InputFrame(tbLocation));
            grid.Add("色", Colors);
            grid.Add("くり返し", cbRepeat);
            grid.Add("くり返しの終わり", FormGrid.Inline(chkUntil, dtUntil));
            grid.Add("お知らせ (起動中だけ)", cbRemind);
            Ui.SetCue(tbTags, "例: 病院 家族 (空白で区切る)");
            grid.Add("タグ", new InputFrame(tbTags));
            Tags = TagSuggest.Attach(tbTags, PlanTagSource.Get, false);
            tbMemo.Multiline = true; tbMemo.AcceptsReturn = true; tbMemo.ScrollBars = ScrollBars.Vertical;
            grid.Add("メモ", new InputFrame(tbMemo, true) { Height = Theme.S(60) }, 2);
            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error, 2);

            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            if (!isNew)
            {
                var del = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
                del.Click += (s, e) => { DialogResult = DialogResult.Abort; };
                grid.AddRaw(DialogKit.Buttons(ok, cancel, del), 2);
            }
            else grid.AddRaw(DialogKit.Buttons(ok, cancel), 2);
            Controls.Add(grid);

            // 値を入れる
            tbTitle.Text = ev.Title ?? "";
            chkAllDay.Checked = ev.AllDay;
            var st = ev.StartAt == DateTime.MinValue ? DateTime.Today.AddHours(Math.Min(23, DateTime.Now.Hour + 1)) : ev.StartAt;
            var en = ev.AllDay ? Fmt.ParseIso(ev.End) ?? st.Date : ev.EndAt;
            if (en < st) en = ev.AllDay ? st.Date : st.AddHours(1);
            dtStart.Value = st.Date; tmStart.Value = DateTime.Today + (ev.AllDay ? new TimeSpan(10, 0, 0) : st.TimeOfDay);
            dtEnd.Value = en.Date; tmEnd.Value = DateTime.Today + (ev.AllDay ? new TimeSpan(11, 0, 0) : en.TimeOfDay);
            tbLocation.Text = ev.Location ?? "";
            Colors.Selected = Math.Max(0, Array.IndexOf(EventLogic.ColorKeys, ev.Color ?? "teal"));
            cbRepeat.SelectedIndex = (int)ev.Repeat;
            var until = Fmt.ParseIso(ev.Until);
            chkUntil.Checked = until.HasValue;
            dtUntil.Value = (until ?? st.Date.AddMonths(3)).Date;
            int ri = Array.IndexOf(EventLogic.RemindChoices, ev.RemindMin);
            cbRemind.SelectedIndex = Math.Max(0, ri);
            tbTags.Text = string.Join(" ", ev.Tags ?? new System.Collections.Generic.List<string>());
            tbMemo.Text = (ev.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

            EventHandler upd = (s, e) => UpdateEnabled();
            chkAllDay.CheckedChanged += upd; chkUntil.CheckedChanged += upd; cbRepeat.SelectedIndexChanged += upd;
            // 始まりを動かしたら、長さを保って終わりも動かす
            DateTime lastStart = Combine(dtStart, tmStart, chkAllDay.Checked);
            EventHandler moveEnd = (s, e) =>
            {
                var ns = Combine(dtStart, tmStart, chkAllDay.Checked);
                var ne = Combine(dtEnd, tmEnd, chkAllDay.Checked) + (ns - lastStart);
                lastStart = ns;
                dtEnd.Value = ne.Date; tmEnd.Value = DateTime.Today + ne.TimeOfDay;
            };
            dtStart.ValueChanged += moveEnd; tmStart.ValueChanged += moveEnd;
            UpdateEnabled();
            Shown += (s, e) => { tbTitle.Focus(); tbTitle.SelectAll(); };
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; } };
        }

        static DateTime Combine(DateTimePicker d, DateTimePicker t, bool allDay)
        {
            return allDay ? d.Value.Date : d.Value.Date + new TimeSpan(t.Value.Hour, t.Value.Minute, 0);
        }

        void UpdateEnabled()
        {
            tmStart.Visible = tmEnd.Visible = !chkAllDay.Checked;
            chkUntil.Enabled = cbRepeat.SelectedIndex > 0;
            dtUntil.Enabled = chkUntil.Enabled && chkUntil.Checked;
        }

        public void Commit()
        {
            var title = tbTitle.Text.Trim();
            if (title.Length == 0) { error.Text = "予定の名前を入力してください"; tbTitle.Focus(); return; }
            bool allDay = chkAllDay.Checked;
            var st = Combine(dtStart, tmStart, allDay);
            var en = Combine(dtEnd, tmEnd, allDay);
            if (en < st || (!allDay && en == st)) { error.Text = allDay ? "終わりの日は始まりの日より後にしてください" : "終わりの時刻は始まりより後にしてください"; dtEnd.Focus(); return; }
            var e = Event;
            e.Title = title;
            e.SetTimes(st, en, allDay);
            e.Location = tbLocation.Text.Trim().Length == 0 ? null : tbLocation.Text.Trim();
            e.Color = EventLogic.ColorKeys[Math.Max(0, Math.Min(EventLogic.ColorKeys.Length - 1, Colors.Selected))];
            e.Repeat = (Repeat)Math.Max(0, cbRepeat.SelectedIndex);
            e.Until = e.Repeat != Repeat.None && chkUntil.Checked ? Fmt.Iso(dtUntil.Value.Date) : null;
            int ri = cbRemind.SelectedIndex;
            e.RemindMin = ri >= 0 && ri < EventLogic.RemindChoices.Length ? EventLogic.RemindChoices[ri] : -1;
            e.Tags = TaskLogic.ParseTags(tbTags.Text);
            e.Memo = tbMemo.Text.Trim().Length == 0 ? null : tbMemo.Text;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>予定の色を選ぶ丸 (←→ でも選べる)</summary>
    public class ColorSwatches : Control
    {
        int selected;
        public event EventHandler SelectedChanged;
        public ColorSwatches()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Height = Theme.S(30); TabStop = true; BackColor = Theme.Surface; Cursor = Cursors.Hand;
        }
        public int Selected
        {
            get { return selected; }
            set { int v = Math.Max(0, Math.Min(EventLogic.ColorKeys.Length - 1, value)); if (v == selected) return; selected = v; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }
        Rectangle Dot(int i) { int d = Theme.S(22), gap = Theme.S(8); return new Rectangle(Theme.S(2) + i * (d + gap), (Height - d) / 2, d, d); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            for (int i = 0; i < EventLogic.ColorKeys.Length; i++)
            {
                var r = Dot(i);
                using (var b = new SolidBrush(EventLogic.ColorAt(i))) g.FillEllipse(b, r);
                if (i == selected)
                {
                    var o = r; o.Inflate(Theme.S(3), Theme.S(3));
                    using (var p = new Pen(Focused ? Theme.Text : Theme.BorderStrong, Theme.S(2))) g.DrawEllipse(p, o);
                    using (var p = new Pen(Color.White, Theme.S(2)))
                        g.DrawLines(p, new[] { new Point(r.X + r.Width / 4, r.Y + r.Height / 2), new Point(r.X + r.Width * 2 / 5, r.Y + r.Height * 2 / 3), new Point(r.Right - r.Width / 4, r.Y + r.Height / 3) });
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            for (int i = 0; i < EventLogic.ColorKeys.Length; i++) { var r = Dot(i); r.Inflate(Theme.S(4), Theme.S(4)); if (r.Contains(e.Location)) Selected = i; }
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Selected = selected - 1; e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Selected = selected + 1; e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>くり返す予定を変えるとき: 0 = この回だけ / 1 = すべての回 / -1 = やめる</summary>
    public class SeriesChoiceForm : Form
    {
        public int Choice = -1;
        public SeriesChoiceForm(string title, string message)
        {
            DialogKit.Setup(this, title);
            var grid = new FormGrid(1, 380);
            grid.Add(null, new WrapLabel { Text = message, Font = Theme.Body, ForeColor = Theme.Text });
            var one = new FlatButton("この回だけ", ButtonKind.Primary).FitWidth(96);
            var all = new FlatButton("すべての回", ButtonKind.Danger).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            one.Click += (s, e) => { Choice = 0; DialogResult = DialogResult.OK; };
            all.Click += (s, e) => { Choice = 1; DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(one, all, cancel), 1);
            Controls.Add(grid);
        }
    }
}
