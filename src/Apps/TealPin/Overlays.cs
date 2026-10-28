// Teal Pin - 画面に重ねる小さなウィンドウ (Windows のときだけ)
//  - StripBorder … 固定したウィンドウのまわりのティールの枠線。細い 4 本のウィンドウ (上・下・左・右) で囲む。
//      重ねるウィンドウはすべて「クリックが通り抜ける (WS_EX_TRANSPARENT)」「前に出ない (WS_EX_NOACTIVATE)」
//      「Alt+Tab に出ない (WS_EX_TOOLWINDOW)」「最前面 (WS_EX_TOPMOST)」。
//      1 枚の大きな透明ウィンドウ (中をくり抜く) にしないのは、動かすたびに大きな範囲を描き直してちらつくため。
//      4 本は DeferWindowPos でまとめて同時に動かし、固定したウィンドウの「すぐ上」に重ねる
//      (ほかの最前面のウィンドウ・メニューを枠線で隠さない)
//  - PinToast   … 「📌 最前面に固定しました」の小さなお知らせ (クリックは通り抜ける)
//  - TitlePopup … タイトルバーを右クリックしたときにシステム メニューの横に出す「📌 最前面に固定」
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pin
{
    public class OverlayWindow : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase; public PinNative.RECT rcPaint; public bool fRestore; public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out PinNative.RECT r);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        readonly int exStyle;
        readonly byte alpha;
        readonly bool clickThrough, rounded;
        bool visible, disposed;
        public Rectangle Bounds { get; private set; }
        public bool Visible { get { return visible; } }

        public OverlayWindow(bool clickThrough, byte alpha, bool rounded)
        {
            this.clickThrough = clickThrough;
            this.alpha = alpha;
            this.rounded = rounded;
            exStyle = PinNative.WS_EX_LAYERED | PinNative.WS_EX_TOOLWINDOW | PinNative.WS_EX_NOACTIVATE | PinNative.WS_EX_TOPMOST
                | (clickThrough ? PinNative.WS_EX_TRANSPARENT : 0);
        }

        public void Ensure()
        {
            if (Handle != IntPtr.Zero || disposed) return;
            using (DpiScope.Enter())   // 物理 px で動かせるように、モニターごとの DPI に対応したウィンドウとして作る
            {
                var cp = new CreateParams { Caption = "", Style = unchecked((int)PinNative.WS_POPUP), ExStyle = exStyle, X = -32000, Y = -32000, Width = 1, Height = 1 };
                CreateHandle(cp);
            }
            PinNative.SetLayeredWindowAttributes(Handle, 0, alpha, 2 /* LWA_ALPHA */);
            if (rounded) { try { int v = 3; DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref v, 4); } catch { } }
        }

        /// <summary>r (物理 px) に出す。after: このウィンドウの下に入れる (HWND_TOPMOST ならいちばん上)</summary>
        public void ShowAt(Rectangle r, IntPtr after)
        {
            Ensure();
            if (Handle == IntPtr.Zero) return;
            using (DpiScope.Enter())
                PinNative.SetWindowPos(Handle, after, r.X, r.Y, r.Width, r.Height, PinNative.SWP_NOACTIVATE | PinNative.SWP_SHOWWINDOW | PinNative.SWP_NOOWNERZORDER);
            Bounds = r; visible = true;
            Redraw();
        }

        public void HideNow()
        {
            if (Handle == IntPtr.Zero || !visible) { visible = false; return; }
            PinNative.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, PinNative.SWP_HIDEWINDOW | PinNative.SWP_NOMOVE | PinNative.SWP_NOSIZE | PinNative.SWP_NOZORDER | PinNative.SWP_NOACTIVATE);
            visible = false;
        }

        internal void MarkPlaced(Rectangle r) { Bounds = r; visible = true; }

        public void Redraw() { if (Handle != IntPtr.Zero) InvalidateRect(Handle, IntPtr.Zero, true); }

        protected virtual void OnPaint(Graphics g, Size size) { g.Clear(Theme.Accent); }

        void PaintTo(IntPtr hdc)
        {
            PinNative.RECT rc;
            GetClientRect(Handle, out rc);
            using (var g = Graphics.FromHdc(hdc))
                OnPaint(g, new Size(rc.Right - rc.Left, rc.Bottom - rc.Top));
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0084: // WM_NCHITTEST
                    m.Result = clickThrough ? new IntPtr(-1) /* HTTRANSPARENT */ : new IntPtr(1);
                    return;
                case 0x0021: // WM_MOUSEACTIVATE
                    m.Result = new IntPtr(3); // MA_NOACTIVATE
                    return;
                case 0x0014: // WM_ERASEBKGND: ここで塗ってしまう (大きさを変えた直後に透明・黒が見えないように)
                    try { PaintTo(m.WParam); } catch { }
                    m.Result = new IntPtr(1);
                    return;
                case 0x000F: // WM_PAINT
                    {
                        PAINTSTRUCT ps;
                        var hdc = BeginPaint(Handle, out ps);
                        try { if (hdc != IntPtr.Zero) PaintTo(hdc); } catch { }
                        EndPaint(Handle, ref ps);
                        m.Result = IntPtr.Zero;
                        return;
                    }
            }
            base.WndProc(ref m);
        }

        public virtual void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (Handle != IntPtr.Zero) DestroyHandle(); } catch { }
        }
    }

    /// <summary>ティールの枠線 (4 本)</summary>
    public class StripBorder : IBorder
    {
        readonly OverlayWindow[] strips = { new OverlayWindow(true, 235, false), new OverlayWindow(true, 235, false), new OverlayWindow(true, 235, false), new OverlayWindow(true, 235, false) };
        readonly HashSet<IntPtr> mine = new HashSet<IntPtr>();
        bool visible, disposed;

        public bool Visible { get { return visible; } }

        void Ensure()
        {
            foreach (var s in strips) { s.Ensure(); if (s.Handle != IntPtr.Zero) mine.Add(s.Handle); }
        }

        /// <summary>target のすぐ上にあるウィンドウ (自分の枠線はとばす)。なければ HWND_TOPMOST</summary>
        IntPtr Above(IntPtr target)
        {
            var h = PinNative.GetWindow(target, PinNative.GW_HWNDPREV);
            int guard = 0;
            while (h != IntPtr.Zero && mine.Contains(h) && guard++ < 16) h = PinNative.GetWindow(h, PinNative.GW_HWNDPREV);
            return h == IntPtr.Zero ? PinNative.HWND_TOPMOST : h;
        }

        public void Place(Rectangle[] rects, IntPtr target)
        {
            if (disposed || rects == null || rects.Length != 4) return;
            Ensure();
            using (DpiScope.Enter())
            {
                var after = Above(target);
                const uint flags = PinNative.SWP_NOACTIVATE | PinNative.SWP_SHOWWINDOW | PinNative.SWP_NOOWNERZORDER;
                var info = PinNative.BeginDeferWindowPos(4);
                for (int i = 0; i < 4 && info != IntPtr.Zero; i++)
                {
                    var r = rects[i];
                    info = PinNative.DeferWindowPos(info, strips[i].Handle, after, r.X, r.Y, r.Width, r.Height, flags);
                }
                if (info == IntPtr.Zero || !PinNative.EndDeferWindowPos(info))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var r = rects[i];
                        PinNative.SetWindowPos(strips[i].Handle, after, r.X, r.Y, r.Width, r.Height, flags);
                    }
                }
            }
            for (int i = 0; i < 4; i++) strips[i].MarkPlaced(rects[i]);
            visible = true;
        }

        public bool OrderOk(IntPtr target)
        {
            if (!visible) return false;
            var h = target;
            for (int i = 0; i < 4; i++)
            {
                h = PinNative.GetWindow(h, PinNative.GW_HWNDPREV);
                if (h == IntPtr.Zero || !mine.Contains(h)) return false;
            }
            return true;
        }

        public void Hide()
        {
            foreach (var s in strips) s.HideNow();
            visible = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var s in strips) s.Dispose();
            visible = false;
        }
    }

    /// <summary>画面の拡大率に合わせた文字 (重ねるウィンドウは物理 px なので、Teal Pin の画面の拡大率との比で大きさを決める)</summary>
    static class OverlayFonts
    {
        public static Font Make(Font baseFont, double monitorScale, FontStyle style)
        {
            double sys = Theme.ScaleFactor <= 0 ? 1 : Theme.ScaleFactor;
            float size = (float)(baseFont.SizeInPoints * monitorScale / sys);
            try { return new Font(baseFont.FontFamily, Math.Max(6f, size), style); } catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, Math.Max(6f, size), style); }
        }
    }

    /// <summary>小さなお知らせ (ウィンドウの上のほうの真ん中に 1.4 秒)</summary>
    public class PinToast : OverlayWindow
    {
        readonly Timer timer = new Timer();
        string text = "";
        bool error;
        Font font;

        public PinToast() : base(true, 242, true)
        {
            timer.Tick += (s, e) => { timer.Stop(); HideNow(); };
        }

        /// <summary>frame: 対象のウィンドウの見えている枠 (物理 px)。空ならマウスの近く</summary>
        public void Show(string msg, Rectangle frame, Rectangle work, double scale, bool isError)
        {
            text = msg ?? "";
            error = isError;
            if (font != null) font.Dispose();
            font = OverlayFonts.Make(Theme.Bold, scale, FontStyle.Bold);
            int padX = (int)Math.Round(16 * scale), padY = (int)Math.Round(9 * scale);
            Size ts;
            using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp))
                ts = TextRenderer.MeasureText(g, text, font, new Size((int)(560 * scale), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int w = ts.Width + padX * 2, h = ts.Height + padY * 2;
            Point p;
            if (frame.Width > 0 && frame.Height > 0)
                p = new Point(frame.Left + (frame.Width - w) / 2, frame.Top + (int)Math.Round(44 * scale));
            else
            {
                var c = Win32Windows.CursorPhysical();
                p = new Point(c.X - w / 2, c.Y - h - (int)Math.Round(20 * scale));
            }
            var r = new Rectangle(p, new Size(w, h));
            if (work.Width > 0) r = PresetMath.Fit(r, work);
            ShowAt(r, PinNative.HWND_TOPMOST);
            timer.Stop();
            timer.Interval = isError ? 3200 : 1400;
            timer.Start();
        }

        protected override void OnPaint(Graphics g, Size size)
        {
            g.Clear(error ? Color.FromArgb(0x3A, 0x2A, 0x2A) : Theme.Accent);
            if (font == null) return;
            TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, size.Width, size.Height), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        public override void Dispose() { timer.Dispose(); if (font != null) font.Dispose(); base.Dispose(); }
    }

    /// <summary>タイトルバーの右クリックで出す「📌 最前面に固定 / 固定を外す」</summary>
    public class TitlePopup : OverlayWindow
    {
        string text = "", sub = "";
        bool hover, message;
        Font font, small;
        double scale = 1;

        public TitlePopup() : base(false, 250, true) { }

        public bool Hover { get { return hover; } set { if (hover != value) { hover = value; Redraw(); } } }
        public bool IsMessage { get { return message; } }

        public Size Prepare(string label, string note, bool isMessage, double monitorScale)
        {
            text = label ?? ""; sub = note ?? ""; message = isMessage; hover = false; scale = monitorScale;
            if (font != null) font.Dispose();
            if (small != null) small.Dispose();
            font = OverlayFonts.Make(Theme.Bold, scale, FontStyle.Bold);
            small = OverlayFonts.Make(Theme.Small, scale, FontStyle.Regular);
            Size a, b;
            using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp))
            {
                a = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                b = sub.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, sub, small, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            int pad = (int)Math.Round(12 * scale);
            int w = Math.Max(a.Width, b.Width) + pad * 2 + (int)Math.Round(6 * scale);
            int h = a.Height + (b.Height > 0 ? b.Height + (int)Math.Round(2 * scale) : 0) + (int)Math.Round(16 * scale);
            return new Size(Math.Max(w, (int)Math.Round(180 * scale)), h);
        }

        protected override void OnPaint(Graphics g, Size size)
        {
            var back = message ? Color.FromArgb(0xFF, 0xF8, 0xE8) : hover ? Theme.AccentSoft : Color.White;
            g.Clear(back);
            int line = Math.Max(1, (int)Math.Round(scale));
            using (var p = new Pen(message ? Theme.Warning : Theme.Accent, line)) g.DrawRectangle(p, line / 2, line / 2, size.Width - line, size.Height - line);
            if (!message) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 0, Math.Max(2, (int)Math.Round(4 * scale)), size.Height);
            if (font == null) return;
            int pad = (int)Math.Round(12 * scale) + (message ? 0 : (int)Math.Round(4 * scale));
            int th = font.Height, sh = sub.Length > 0 ? small.Height : 0;
            int y = (size.Height - th - (sh > 0 ? sh + (int)Math.Round(2 * scale) : 0)) / 2;
            TextRenderer.DrawText(g, text, font, new Rectangle(pad, y, size.Width - pad, th), message ? Color.FromArgb(0x7A, 0x4A, 0x00) : Theme.Accent,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (sh > 0)
                TextRenderer.DrawText(g, sub, small, new Rectangle(pad, y + th + (int)Math.Round(2 * scale), size.Width - pad, sh), Theme.SubText,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        public override void Dispose() { if (font != null) font.Dispose(); if (small != null) small.Dispose(); base.Dispose(); }
    }
}
