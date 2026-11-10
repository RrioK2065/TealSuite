// Teal Macro - 画面の小物 (選ぶボタンの列・実行中の表示・中身に合わせて高さが決まるカード・キーを入れる欄・数の欄)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    /// <summary>丸いボタンを横に並べて 1 つ選ぶ (「左クリック | 右クリック | …」)。←→ キーでも選べる</summary>
    public class ChoiceChips : Control
    {
        readonly List<string> items = new List<string>();
        readonly List<Rectangle> rects = new List<Rectangle>();
        int selected, hover = -1;
        public event EventHandler SelectedChanged;

        public ChoiceChips(params string[] labels)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            items.AddRange(labels);
            Font = Theme.Body; TabStop = true; Cursor = Cursors.Hand;
            Height = Font.Height + Theme.S(18);
            Layout_();
            Width = rects.Count == 0 ? Theme.S(100) : rects.Last().Right + 2;
        }
        public int Count { get { return items.Count; } }
        public string this[int i] { get { return items[i]; } }
        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = Math.Max(0, Math.Min(items.Count - 1, value));
                if (v == selected) return;
                selected = v; Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }
        public string SelectedText { get { return items[selected]; } }

        void Layout_()
        {
            rects.Clear();
            int x = 1;
            foreach (var t in items)
            {
                int w = TextRenderer.MeasureText(t, Font).Width + Theme.S(26);
                rects.Add(new Rectangle(x, 1, w, Height - 3));
                x += w + Theme.S(6);
            }
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left && selected > 0) { SelectedIndex = selected - 1; e.Handled = true; }
            if (e.KeyCode == Keys.Right && selected < items.Count - 1) { SelectedIndex = selected + 1; e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Layout_();
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Layout_();
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h >= 0) SelectedIndex = h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Layout_();
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i];
                bool sel = i == selected;
                Color back = sel ? (Enabled ? Theme.Accent : Theme.Disabled) : (i == hover ? Theme.SurfaceAlt : Theme.Surface);
                Color line = sel ? back : Theme.BorderStrong;
                using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f))
                {
                    using (var b = new SolidBrush(back)) g.FillPath(b, path);
                    using (var p = new Pen(line)) g.DrawPath(p, path);
                    if (sel && Focused) using (var p = new Pen(Theme.AccentLight, 2f)) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, items[i], Font, r, sel ? Color.White : (Enabled ? Theme.Text : Theme.Disabled),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>いまの状態を大きく出す帯 (待機中 = 灰色、実行中 = ティール、記録中 = 赤、カウントダウン = 橙)</summary>
    public class StatusBanner : Control
    {
        public string Title = "待機中", Detail = "";
        public CoreState State;
        int blink;
        readonly Timer pulse = new Timer { Interval = 500 };

        public StatusBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(58);
            pulse.Tick += (s, e) => { blink++; Invalidate(); };
        }

        public void Set(CoreState st, string title, string detail)
        {
            State = st; Title = title ?? ""; Detail = detail ?? "";
            if (st == CoreState.Idle) pulse.Stop(); else if (!pulse.Enabled) pulse.Start();
            Invalidate();
        }

        public Color BackFor(CoreState st)
        {
            switch (st)
            {
                case CoreState.Clicking:
                case CoreState.Playing: return Theme.Accent;
                case CoreState.Recording: return Theme.Danger;
                case CoreState.Countdown: return Theme.Warning;
                default: return Theme.Surface;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool idle = State == CoreState.Idle;
            var back = BackFor(State);
            var fore = idle ? Theme.Text : Color.White;
            var sub = idle ? Theme.SubText : Color.FromArgb(235, 255, 255, 255);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius + 2)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var p = new Pen(idle ? Theme.Border : back)) g.DrawPath(p, path);
            }
            int d = Theme.S(14), x = Theme.S(18);
            int cy = Height / 2;
            Color dot = idle ? Theme.Disabled : (blink % 2 == 0 ? Color.White : Color.FromArgb(150, 255, 255, 255));
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, x, cy - d / 2, d, d);
            int tx = x + d + Theme.S(12);
            var titleW = TextRenderer.MeasureText(Title, Theme.Header).Width;
            TextRenderer.DrawText(g, Title, Theme.Header, new Rectangle(tx, 0, titleW + 4, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            int dx = tx + titleW + Theme.S(14);
            TextRenderer.DrawText(g, Detail, Theme.Body, new Rectangle(dx, 0, Math.Max(10, Width - dx - Theme.S(14)), Height), sub,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>中身 (Dock=Top で積んだもの) に合わせて高さが決まるカード</summary>
    public class FitCard : CardPanel
    {
        bool sizing;
        public FitCard(string title) : base(title) { Dock = DockStyle.Top; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (sizing) return;
            sizing = true;
            try
            {
                int need = Padding.Top;
                foreach (Control c in Controls) if (c.Visible) need = Math.Max(need, c.Bottom);
                need += Padding.Bottom;
                if (Height != need) Height = need;
            }
            finally { sizing = false; }
        }
    }

    /// <summary>押すキーを入れる欄。欄をクリックしてキーを押すと、そのキー (Ctrl などとの組み合わせも) が入る</summary>
    public class KeyCaptureBox : TextBox
    {
        string saved = "";
        public event EventHandler KeyChanged;
        public KeyCaptureBox()
        {
            ReadOnly = true; BackColor = Theme.Surface; Font = Theme.Body; ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
        }
        /// <summary>保存用の文字 ("Space"、"Ctrl+C")。空なら未設定</summary>
        public string KeyValue
        {
            get { return saved; }
            set { saved = value ?? ""; Text = saved.Length == 0 ? "" : KeyText.Display(saved); }
        }
        protected override bool IsInputKey(Keys keyData) { return true; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { return Capture_(keyData) || base.ProcessCmdKey(ref msg, keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { e.Handled = e.SuppressKeyPress = true; Capture_(e.KeyData); }
        protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }
        /// <summary>キーを受け取る (テストでも使う)。Ctrl・Shift だけを押したときは決めない</summary>
        public bool Capture_(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin || k == Keys.None) return false;
            var spec = KeyText.FromKeyData(keyData);
            if (spec.IsEmpty) return false;
            KeyValue = spec.ToSaved();
            if (KeyChanged != null) KeyChanged(this, EventArgs.Empty);
            return true;
        }
    }

    /// <summary>
    /// 数を入れる欄 (枠つき)。↑↓ キー・ホイール・右の ▲▼ で増減。範囲の外は直す。
    /// (NumericUpDown は mono で数字が出ないことがあり、見た目もそろわないため自前で)
    /// </summary>
    public class NumBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        Panel arrowPanel;
        decimal min, max = 100, val, inc = 1;
        bool setting;
        public event EventHandler ValueChanged;

        public NumBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(8), Theme.S(5), 1, Theme.S(4));
            Box.BorderStyle = BorderStyle.None; Box.Font = Theme.Body; Box.TextAlign = HorizontalAlignment.Right; Box.BackColor = Theme.Surface; Box.Dock = DockStyle.Fill;
            var arrows = new Panel { Dock = DockStyle.Right, Width = Theme.S(18), BackColor = Theme.Surface, Cursor = Cursors.Hand };
            arrows.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = arrows.Width, h = arrows.Height, a = Theme.S(4), cx = w / 2 - 1;
                using (var b = new SolidBrush(Enabled ? Theme.SubText : Theme.Disabled))
                {
                    int y1 = h / 2 - Theme.S(3), y2 = h / 2 + Theme.S(3);
                    g.FillPolygon(b, new[] { new Point(cx - a, y1), new Point(cx + a, y1), new Point(cx, y1 - a) });
                    g.FillPolygon(b, new[] { new Point(cx - a, y2), new Point(cx + a, y2), new Point(cx, y2 + a) });
                }
            };
            arrows.MouseDown += (s, e) => { Box.Focus(); Step(e.Y < arrows.Height / 2 ? 1 : -1); };
            arrowPanel = arrows;
            Controls.Add(Box); Controls.Add(arrows);
            Height = Box.PreferredHeight + Padding.Vertical + 2;
            Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up) { Step(1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Commit(); e.Handled = e.SuppressKeyPress = true; }
            };
            Box.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && min < 0)) e.Handled = true; };
            Box.TextChanged += (s, e) => { if (!setting) TryLive(); };
            Box.LostFocus += (s, e) => { Commit(); Invalidate(); };
            Box.GotFocus += (s, e) => Invalidate();
            Box.MouseWheel += (s, e) => Step(e.Delta > 0 ? 1 : -1);
            Show_();
        }

        public decimal Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; } }
        public decimal Maximum { get { return max; } set { max = Math.Max(value, min); Value = val; } }
        public decimal Increment { get { return inc; } set { inc = value <= 0 ? 1 : value; } }
        public decimal Value
        {
            get { return val; }
            set
            {
                var v = Math.Max(min, Math.Min(max, Math.Round(value)));
                bool changed = v != val;
                val = v;
                Show_();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void Show_()
        {
            setting = true;
            try { var t = val.ToString(max >= 10000 ? "#,0" : "0"); if (Box.Text != t) Box.Text = t; }
            finally { setting = false; }
        }

        void Step(int dir)
        {
            if (!Enabled) return;
            Commit();
            decimal step = inc;
            if (inc == 1 && max >= 1000 && Math.Abs(val) >= 1000) step = 100;   // 大きな数は大きく動かす
            Value = val + dir * step;
            Box.SelectionStart = Box.TextLength;
        }

        bool Parse(out decimal v)
        {
            var t = (Box.Text ?? "").Replace(",", "").Trim();
            return decimal.TryParse(t, out v);
        }

        /// <summary>入力中: 範囲内の数ならすぐ反映 (外ならフォーカスが離れたときに直す)</summary>
        void TryLive()
        {
            decimal v;
            if (!Parse(out v) || v < min || v > max) return;
            if (v == val) return;
            val = v;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void Commit()
        {
            decimal v;
            if (Parse(out v)) Value = v; else Show_();
            Show_();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.Enabled = Enabled;
            if (arrowPanel != null) arrowPanel.Invalidate();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool focus = Box.ContainsFocus;
            using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(focus ? Theme.Accent : (Enabled ? Theme.BorderStrong : Theme.Border), focus ? 1.6f : 1f)) g.DrawPath(p, path);
            }
        }
    }

    public static class MUi
    {
        public static NumBox Num(decimal min, decimal max, decimal value, int width = 90)
        {
            var n = new NumBox { Width = Theme.S(width) };
            n.Minimum = min; n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            return n;
        }
        public static Label Text(string t, Font f = null, Color? c = null)
        {
            return new Label { Text = t, AutoSize = true, Font = f ?? Theme.Body, ForeColor = c ?? Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Padding = new Padding(0, Theme.S(5), 0, 0) };
        }
        /// <summary>横に並べる 1 行 (高さは中身に合わせる)</summary>
        public static FlowLayoutPanel Line(params Control[] items)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, AutoSize = false, Padding = new Padding(0, Theme.S(2), 0, Theme.S(2)) };
            int h = 0;
            foreach (var c in items)
            {
                if (c == null) continue;
                c.Margin = new Padding(0, 0, Theme.S(8), 0);
                f.Controls.Add(c);
                h = Math.Max(h, c.Height);
            }
            f.Height = h + f.Padding.Vertical + Theme.S(2);
            return f;
        }
        public static CheckBox Check(string t)
        {
            var c = new CheckBox { Text = t, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Padding = new Padding(0, Theme.S(3), 0, 0) };
            return c;
        }
        public static FlatButton Big(string text, ButtonKind kind, int width)
        {
            var b = new FlatButton(text, kind) { Font = Theme.Header, Width = Theme.S(width), Height = Theme.S(46) };
            return b;
        }
    }
}
