// Teal Clip (コピー履歴) - ショートカットで出る小さな一覧 (マウスカーソルの近くに表示)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    public class PopupForm : Form
    {
        readonly TrayContext ctx;
        IntPtr target;
        int selected, hover = -1;
        int rowH, headH, footH;
        public event Action<ClipItem, IntPtr> Chosen;

        public PopupForm(TrayContext ctx)
        {
            this.ctx = ctx;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Theme.Surface;
            rowH = Theme.S(34); headH = Theme.S(34); footH = Theme.S(28);
            Width = Theme.S(400);
            Deactivate += (s, e) => { if (Visible) Hide(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;  // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;     // WS_EX_TOOLWINDOW (Alt+Tab に出さない)
                return cp;
            }
        }

        int Count { get { return ctx.History.Items.Count; } }

        public void ShowAt(Point cursor, IntPtr previous)
        {
            target = previous;
            selected = 0; hover = -1;
            Height = headH + Math.Max(1, Count) * rowH + footH + Theme.S(8);
            var wa = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X + Theme.S(8), y = cursor.Y + Theme.S(8);
            if (x + Width > wa.Right) x = Math.Max(wa.Left, cursor.X - Width - Theme.S(8));
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, cursor.Y - Height - Theme.S(8));
            Location = new Point(x, y);
            Invalidate();
            if (!Visible) Show();
            AppHost.BringToFront(this);
        }

        Rectangle RowRect(int i) { return new Rectangle(Theme.S(6), headH + i * rowH, Width - Theme.S(12), rowH); }

        static string Badge(int i) { return i == 9 ? "0" : (i + 1).ToString(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            using (var p = new Pen(Theme.BorderStrong)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            var tf = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

            // 見出し
            TextRenderer.DrawText(g, AppInfo.DisplayName, Theme.Bold, new Rectangle(Theme.S(14), 0, Width / 2, headH), Theme.Text, tf);
            TextRenderer.DrawText(g, ctx.Hotkey.IsEmpty ? "" : ctx.Hotkey.Display, Theme.Small, new Rectangle(Width / 2, 0, Width / 2 - Theme.S(14), headH), Theme.SubText, tf | TextFormatFlags.Right);

            var items = ctx.History.Items;
            if (items.Count == 0)
                TextRenderer.DrawText(g, "まだ履歴がありません。文字をコピーするとここに並びます。", Theme.Body, RowRect(0), Theme.SubText, tf | TextFormatFlags.HorizontalCenter);
            for (int i = 0; i < items.Count; i++)
            {
                var r = RowRect(i);
                bool sel = i == selected, hov = i == hover;
                if (sel || hov)
                    using (var path = Theme.RoundRect(new RectangleF(r.X, r.Y + 1, r.Width, r.Height - 2), Theme.S(Theme.Radius)))
                    using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.SurfaceAlt)) g.FillPath(b, path);
                // 番号バッジ
                int bs = Theme.S(22);
                var br = new Rectangle(r.X + Theme.S(8), r.Y + (r.Height - bs) / 2, bs, bs);
                using (var path = Theme.RoundRect(br, Theme.S(5)))
                using (var b = new SolidBrush(sel ? Theme.Accent : Theme.SurfaceAlt)) g.FillPath(b, path);
                TextRenderer.DrawText(g, Badge(i), Theme.Bold, br, sel ? Color.White : Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int tx = br.Right + Theme.S(10);
                int right = r.Right - Theme.S(8);
                if (items[i].Pinned)
                {
                    var pr = new Rectangle(right - Theme.S(20), r.Y, Theme.S(20), r.Height);
                    TextRenderer.DrawText(g, "★", Theme.Small, pr, Theme.Accent, tf | TextFormatFlags.HorizontalCenter);
                    right = pr.Left - Theme.S(2);
                }
                TextRenderer.DrawText(g, items[i].OneLine, Theme.Body, new Rectangle(tx, r.Y, right - tx, r.Height), Theme.Text, tf);
            }
            // 下の案内
            int fy = Height - footH - Theme.S(2);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Theme.S(10), fy, Width - Theme.S(10), fy);
            TextRenderer.DrawText(g, "1–0 / ↑↓ Enter で貼り付け ・ Esc で閉じる", Theme.Small,
                new Rectangle(Theme.S(14), fy, Width - Theme.S(28), footH), Theme.SubText, tf);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitRow(e.Location);
            if (h != hover) { hover = h; if (h >= 0) selected = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = HitRow(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0) Choose(h);
        }

        int HitRow(Point p)
        {
            for (int i = 0; i < Count; i++) if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k == Keys.Escape) { Hide(); return true; }
            if (k == Keys.Up) { if (Count > 0) selected = (selected - 1 + Count) % Count; Invalidate(); return true; }
            if (k == Keys.Down || k == Keys.Tab) { if (Count > 0) selected = (selected + 1) % Count; Invalidate(); return true; }
            if (k == Keys.Return || k == Keys.Space) { Choose(selected); return true; }
            int n = -1;
            if (k >= Keys.D0 && k <= Keys.D9) n = k - Keys.D0;
            else if (k >= Keys.NumPad0 && k <= Keys.NumPad9) n = k - Keys.NumPad0;
            if (n >= 0) { Choose(n == 0 ? 9 : n - 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Choose(int i)
        {
            if (i < 0 || i >= Count) return;
            var item = ctx.History.Items[i];
            if (Chosen != null) Chosen(item, target);
        }
    }
}
