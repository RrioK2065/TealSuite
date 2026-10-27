// TealSuite 共通コントロール: ボタン / ヘッダー / フッター / カード / 入力枠 / スライダー / スイッチ
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public enum ButtonKind { Primary, Secondary, Danger, Ghost }

    static class PaintUtil
    {
        /// <summary>透明でない一番近い親の背景色</summary>
        public static Color ParentBack(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Theme.Background;
        }
    }

    /// <summary>角丸フラットボタン</summary>
    public class FlatButton : Button
    {
        ButtonKind kind;
        bool hover, pressed;

        public FlatButton() : this("", ButtonKind.Secondary) { }
        /// <summary>画面に出ていなくても Click を起こす (テスト用。PerformClick は表示中でないと何もしない)</summary>
        public void SimulateClick() { if (Enabled) OnClick(EventArgs.Empty); }
        public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Text = text;
            this.kind = kind;
            Height = Theme.S(Theme.ButtonHeight);
            Margin = new Padding(Theme.S(4), 0, Theme.S(4), 0);
            FitWidth();
        }

        [DefaultValue(ButtonKind.Secondary)]
        public ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }

        /// <summary>文字幅に合わせて幅を決める</summary>
        public FlatButton FitWidth(int minWidth = 72)
        {
            int w = TextRenderer.MeasureText(Text ?? "", Font).Width + Theme.S(28) + (Image != null ? Image.Width + Theme.S(6) : 0);
            Width = Math.Max(Theme.S(minWidth), w);
            return this;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg, fg, border;
            switch (kind)
            {
                case ButtonKind.Primary:
                    bg = pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent; fg = Color.White; border = bg; break;
                case ButtonKind.Danger:
                    bg = pressed ? Theme.Danger : hover ? Theme.DangerSoft : Theme.Surface;
                    fg = pressed ? Color.White : Theme.Danger; border = Theme.Blend(Theme.Danger, Color.White, 0.45f); break;
                case ButtonKind.Ghost:
                    bg = pressed ? Theme.Border : hover ? Theme.SurfaceAlt : Color.Transparent; fg = Theme.Text; border = Color.Transparent; break;
                default:
                    bg = pressed ? Theme.Border : hover ? Theme.SurfaceAlt : Theme.Surface; fg = Theme.Text; border = Theme.BorderStrong; break;
            }
            if (!Enabled)
            {
                bg = kind == ButtonKind.Ghost ? Color.Transparent : Theme.SurfaceAlt;
                fg = Theme.Disabled; border = kind == ButtonKind.Ghost ? Color.Transparent : Theme.Border;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius)))
            {
                if (bg.A > 0) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (border.A > 0) using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues && Enabled)
            {
                var fr = new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f);
                using (var path = Theme.RoundRect(fr, Theme.S(Theme.Radius) - 2))
                using (var p = new Pen(kind == ButtonKind.Primary ? Color.FromArgb(200, Color.White) : Theme.Accent, 1.5f))
                    g.DrawPath(p, path);
            }

            var textRect = ClientRectangle;
            if (Image != null)
            {
                int tw = TextRenderer.MeasureText(Text ?? "", Font).Width;
                int total = Image.Width + (tw > 0 ? Theme.S(6) + tw : 0);
                int x = (Width - total) / 2;
                g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
                textRect = new Rectangle(x + Image.Width + Theme.S(6), 0, Width - x - Image.Width - Theme.S(6), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, Text, Font, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>ウィンドウ上部のヘッダー (アイコン + タイトル + サブタイトル + 右側に操作ボタン)</summary>
    public class HeaderBar : Panel
    {
        readonly Bitmap icon;
        readonly FlowLayoutPanel actions;
        public string Title { get; set; }
        public string Subtitle { get; set; }

        /// <summary>アプリの表示名と役割を出すヘッダー (detail は役割の後ろに続ける補足)</summary>
        public static HeaderBar ForApp(string detail, bool withAbout = true)
        {
            return new HeaderBar(AppInfo.DisplayName, AppInfo.HeaderSubtitle(detail), withAbout);
        }
        /// <summary>役割の後ろの補足だけを差し替える</summary>
        public void SetDetail(string detail) { Subtitle = AppInfo.HeaderSubtitle(detail); Invalidate(); }

        public HeaderBar(string title, string subtitle, bool withAbout = true)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
            Title = title; Subtitle = subtitle;
            Dock = DockStyle.Top;
            Height = Theme.S(Theme.HeaderHeight);
            BackColor = Theme.Surface;
            Padding = new Padding(0, Theme.S(3), 0, 1); // 上端の帯と下の区切り線を子コントロールで隠さない
            icon = Theme.AppIconBitmap(Theme.S(32));
            actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface,
                Padding = new Padding(0, (Height - 1 - Theme.S(Theme.ButtonHeight)) / 2 - Theme.S(3), Theme.S(10), 0)
            };
            Controls.Add(actions);
            if (withAbout)
            {
                var about = new FlatButton("このアプリについて", ButtonKind.Ghost);
                about.Font = Theme.Small; about.FitWidth();
                about.Click += (s, e) => AboutDialog.ShowFor(FindForm());
                actions.Controls.Add(about);
            }
        }

        /// <summary>右側にボタンなどを追加する (先に追加したものほど右)</summary>
        public T AddAction<T>(T c) where T : Control { actions.Controls.Add(c); return c; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int x = Theme.S(16);
            g.DrawImage(icon, x, (Height - icon.Height) / 2, icon.Width, icon.Height);
            x += icon.Width + Theme.S(12);
            int right = Width - actions.Width - Theme.S(8);
            var tf = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (string.IsNullOrEmpty(Subtitle))
            {
                TextRenderer.DrawText(g, Title, Theme.Header, new Rectangle(x, 0, right - x, Height), Theme.Text, tf | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int th = Theme.Header.Height, sh = Theme.Small.Height;
                int y = (Height - th - sh) / 2;
                TextRenderer.DrawText(g, Title, Theme.Header, new Rectangle(x, y, right - x, th), Theme.Text, tf);
                // 幅が足りず補足が途中で切れるときは、日本語の役割名だけを出す (役割名は必ず読めるように)
                string sub = Subtitle;
                if (!string.IsNullOrEmpty(AppInfo.Role) && sub.StartsWith(AppInfo.Role) && sub.Length > AppInfo.Role.Length
                    && TextRenderer.MeasureText(g, sub, Theme.Small, new Size(int.MaxValue, sh), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width > right - x - 1)
                    sub = AppInfo.Role;
                TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(x + 1, y + th, right - x, sh + 2), Theme.SubText, tf);
            }
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 0, Width, Theme.S(3)); // 上端のテーマカラーの帯
        }
    }

    /// <summary>ウィンドウ下部のステータスバー (左: 状態, 右: バージョン)</summary>
    public class FooterBar : Panel
    {
        readonly Label status = new Label();
        readonly Label version = new Label();
        public FooterBar()
        {
            Dock = DockStyle.Bottom;
            Height = Theme.S(Theme.FooterHeight);
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(14), 1, Theme.S(14), 0);
            status.Dock = DockStyle.Fill; status.Font = Theme.Small; status.ForeColor = Theme.SubText;
            status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.UseMnemonic = false;
            version.Dock = DockStyle.Right; version.AutoSize = false; version.Font = Theme.Small; version.ForeColor = Theme.SubText;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.Text = AppInfo.DisplayName + "  v" + AppInfo.Version;
            version.Width = TextRenderer.MeasureText(version.Text, Theme.Small).Width + Theme.S(8);
            Controls.Add(status); Controls.Add(version);
        }
        public string StatusText { get { return status.Text; } set { status.Text = value; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
        }
    }

    /// <summary>白い角丸カード。title を渡すと左上に見出しを描く。</summary>
    public class CardPanel : Panel
    {
        public string Title { get; set; }
        public CardPanel(string title = null)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Title = title;
            BackColor = Theme.Surface;
            Padding = title == null ? Theme.P(12) : Theme.P(14, 42, 14, 14);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius + 2)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            if (!string.IsNullOrEmpty(Title))
                TextRenderer.DrawText(g, Title, Theme.Title, new Rectangle(Theme.S(14), Theme.S(12), Width - Theme.S(28), Theme.Title.Height + 4),
                    Theme.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>枠なしの TextBox などを角丸の枠で包む。フォーカス時はアクセント色の枠になる。</summary>
    public class InputFrame : Panel
    {
        public Control Inner { get; private set; }
        public InputFrame(Control inner, bool multiline = false)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Inner = inner;
            BackColor = Theme.Surface;
            Padding = Theme.P(9, 6, 6, 6);
            var tb = inner as TextBoxBase;
            if (tb != null) tb.BorderStyle = BorderStyle.None;
            inner.BackColor = Theme.Surface;
            inner.Dock = DockStyle.Fill;
            if (inner.Font == null || inner.Font == Control.DefaultFont) inner.Font = Theme.Body;
            if (!multiline) Height = inner.PreferredSize.Height + Padding.Vertical + 2;
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();
            inner.EnabledChanged += (s, e) => Invalidate();
            Controls.Add(inner);
            Cursor = Cursors.IBeam;
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Inner.Focus(); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool focus = Inner.ContainsFocus;
            using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(focus ? Theme.Accent : Theme.BorderStrong, focus ? 1.6f : 1f)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>フラットなスライダー。Shift を押しながらドラッグすると ShiftStep 刻みにスナップする。</summary>
    public class FlatSlider : Control
    {
        int min = 0, max = 100, val = 0;
        bool dragging, hover;
        int lastMouseX;
        public int ShiftStep { get; set; }      // 0 ならスナップなし
        public int SmallChange { get; set; }
        public int LargeChange { get; set; }
        public bool Dimmed { get; set; }          // ミュート中などに灰色で描く
        public bool IsDragging { get { return dragging; } }
        /// <summary>値が変わったとき。ユーザー操作かどうかは IsUserChange で分かる。</summary>
        public event EventHandler ValueChanged;
        public event EventHandler DragCompleted;
        public bool IsUserChange { get; private set; }

        public FlatSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
            SmallChange = 1; LargeChange = 10; ShiftStep = 0;
            Height = Theme.S(28);
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; Invalidate(); } }
        public int Maximum { get { return max; } set { max = Math.Max(value, min); Value = val; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set { SetValue(value, false); }
        }

        void SetValue(int v, bool user)
        {
            v = Math.Max(min, Math.Min(max, v));
            if (v == val) return;
            val = v;
            Invalidate();
            IsUserChange = user;
            var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            IsUserChange = false;
        }

        int ThumbR { get { return Theme.S(dragging || hover ? 9 : 8); } }
        int TrackLeft { get { return Theme.S(10); } }
        int TrackRight { get { return Width - Theme.S(10); } }

        /// <summary>マウス位置 → 値 (Shift 押下中は ShiftStep 刻み)。テストしやすいよう public。</summary>
        public int ValueFromX(int x, bool shift)
        {
            int l = TrackLeft, r = TrackRight;
            if (r <= l) return min;
            double t = Math.Max(0, Math.Min(1, (x - l) / (double)(r - l)));
            double raw = min + t * (max - min);
            int v;
            if (shift && ShiftStep > 0) v = (int)(Math.Round(raw / ShiftStep, MidpointRounding.AwayFromZero) * ShiftStep);
            else v = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            return Math.Max(min, Math.Min(max, v));
        }

        static bool ShiftHeld { get { return (Control.ModifierKeys & Keys.Shift) == Keys.Shift; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            dragging = true; Capture = true; lastMouseX = e.X;
            SetValue(ValueFromX(e.X, ShiftHeld), true);
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { lastMouseX = e.X; SetValue(ValueFromX(e.X, ShiftHeld), true); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false; Invalidate();
            var h = DragCompleted; if (h != null) h(this, EventArgs.Empty);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;
            int step = ShiftHeld && ShiftStep > 0 ? ShiftStep : SmallChange;
            int v = val + (e.Delta > 0 ? step : -step);
            if (ShiftHeld && ShiftStep > 0) v = (int)Math.Round(v / (double)ShiftStep) * ShiftStep;
            SetValue(v, true);
            var hm = e as HandledMouseEventArgs; if (hm != null) hm.Handled = true;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
            }
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Shift && ShiftStep > 0 ? ShiftStep : SmallChange;
            int v = val;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: v -= step; break;
                case Keys.Right: case Keys.Up: v += step; break;
                case Keys.PageDown: v -= LargeChange; break;
                case Keys.PageUp: v += LargeChange; break;
                case Keys.Home: v = min; break;
                case Keys.End: v = max; break;
                case Keys.ShiftKey:
                    if (dragging) SetValue(ValueFromX(lastMouseX, true), true); // ドラッグ中に Shift を押したら即スナップ
                    return;
                default: return;
            }
            if (e.Shift && ShiftStep > 0 && e.KeyCode != Keys.Home && e.KeyCode != Keys.End)
                v = (int)Math.Round(v / (double)ShiftStep) * ShiftStep;
            SetValue(v, true);
            e.Handled = true;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int l = TrackLeft, r = TrackRight, cy = Height / 2, th = Theme.S(4);
            double t = max > min ? (val - min) / (double)(max - min) : 0;
            float tx = (float)(l + t * (r - l));
            Color fill = !Enabled ? Theme.Disabled : Dimmed ? Theme.BorderStrong : Theme.Accent;
            using (var path = Theme.RoundRect(new RectangleF(l, cy - th / 2f, r - l, th), th / 2f))
            using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, path);
            if (tx > l)
                using (var path = Theme.RoundRect(new RectangleF(l, cy - th / 2f, tx - l, th), th / 2f))
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            int tr = ThumbR;
            if (Focused && ShowFocusCues)
                using (var b = new SolidBrush(Color.FromArgb(50, Theme.Accent))) g.FillEllipse(b, tx - tr - 4, cy - tr - 4, (tr + 4) * 2, (tr + 4) * 2);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, tx - tr, cy - tr, tr * 2, tr * 2);
            using (var p = new Pen(fill, Theme.S(2))) g.DrawEllipse(p, tx - tr + 1, cy - tr + 1, tr * 2 - 2, tr * 2 - 2);
        }
    }

    /// <summary>オン/オフのスイッチ (CheckBox 互換)</summary>
    public class ToggleSwitch : CheckBox
    {
        public ToggleSwitch(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text; Font = Theme.Body; Cursor = Cursors.Hand; AutoSize = true;
            Margin = new Padding(0, Theme.S(3), Theme.S(8), Theme.S(3));
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var ts = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(Theme.S(36) + Theme.S(10) + ts.Width + Theme.S(4), Math.Max(Theme.S(24), ts.Height + Theme.S(4)));
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = Theme.S(36), h = Theme.S(20), y = (Height - h) / 2;
            Color on = Enabled ? Theme.Accent : Theme.Disabled;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, y + 0.5f, w - 1, h - 1), h / 2f))
            {
                using (var b = new SolidBrush(Checked ? on : Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Checked ? on : Theme.BorderStrong)) g.DrawPath(p, path);
            }
            int k = h - Theme.S(6);
            float kx = Checked ? w - k - Theme.S(3) : Theme.S(3);
            using (var b = new SolidBrush(Checked ? Color.White : Theme.SubText)) g.FillEllipse(b, kx, y + Theme.S(3), k, k);
            if (Focused && ShowFocusCues)
                using (var path = Theme.RoundRect(new RectangleF(-0.5f + 1, y - 1.5f + 1, w, h + 1), h / 2f + 1))
                using (var p = new Pen(Color.FromArgb(120, Theme.Accent), 1.5f)) g.DrawPath(p, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(w + Theme.S(10), 0, Width - w - Theme.S(10), Height),
                Enabled ? Theme.Text : Theme.Disabled, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>メッセージボックスの共通ラッパー (タイトルにアプリ名を出す)</summary>
    public static class Dialogs
    {
        public static void Info(IWin32Window owner, string msg) { MessageBox.Show(owner, msg, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public static void Warn(IWin32Window owner, string msg) { MessageBox.Show(owner, msg, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        public static void Error(IWin32Window owner, string msg) { MessageBox.Show(owner, msg, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        public static bool Confirm(IWin32Window owner, string msg, string title = null)
        {
            return MessageBox.Show(owner, msg, title ?? AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }

    public static class Ui
    {
        /// <summary>見出し用ラベル</summary>
        public static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = Theme.Small, ForeColor = Theme.SubText, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)), UseMnemonic = false };
        }
        /// <summary>空のスペーサー (Dock=Top のレイアウト用)</summary>
        public static Panel Spacer(int h, DockStyle dock = DockStyle.Top)
        {
            return new Panel { Dock = dock, Height = Theme.S(h), Width = Theme.S(h), BackColor = Color.Transparent };
        }
        /// <summary>TextBox に薄い説明文 (cue banner) を出す</summary>
        public static void SetCue(TextBox tb, string cue)
        {
            tb.HandleCreated += (s, e) => { try { if (AppInfo.IsWindows) Native.SendMessage(tb.Handle, 0x1501, (IntPtr)1, cue); } catch { } };
        }
    }
}
