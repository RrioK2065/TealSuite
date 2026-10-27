// Teal Hub (アプリ管理) - ショートカットの表示部品 (カードの行・一覧画面)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public enum KeyCapState { Normal, None, Duplicate, Error }

    /// <summary>キーの組み合わせを表す小さな札 (Alt + H / なし)</summary>
    public class KeyCap : Control
    {
        KeyCapState state;
        public KeyCap()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Font = Theme.Small;
            Height = Theme.S(22);
        }
        public KeyCapState State { get { return state; } }
        public void Set(string text, KeyCapState st)
        {
            Text = text; state = st;
            var f = st == KeyCapState.None ? Theme.Small : BoldSmall;
            Width = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + Theme.S(18) + (st == KeyCapState.Duplicate || st == KeyCapState.Error ? Theme.S(14) : 0);
            Invalidate();
        }
        static Font boldSmall;
        static Font BoldSmall { get { return boldSmall ?? (boldSmall = new Font(Theme.Small, FontStyle.Bold)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtilBack());
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, border;
            switch (state)
            {
                case KeyCapState.None: bg = Theme.SurfaceAlt; fg = Theme.SubText; border = Theme.Border; break;
                case KeyCapState.Duplicate: bg = Theme.DangerSoft; fg = Theme.Danger; border = Theme.Danger; break;
                case KeyCapState.Error: bg = Color.FromArgb(0xFF, 0xF6, 0xE5); fg = Color.FromArgb(0x8A, 0x55, 0x00); border = Theme.Warning; break;
                default: bg = Theme.AccentSoft; fg = Theme.AccentPressed; border = Theme.AccentLight; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(4)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            var text = (state == KeyCapState.Duplicate || state == KeyCapState.Error ? "⚠ " : "") + Text;
            TextRenderer.DrawText(g, text, state == KeyCapState.None ? Theme.Small : BoldSmall, new Rectangle(0, 0, Width, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        Color PaintUtilBack()
        {
            for (Control c = Parent; c != null; c = c.Parent) if (c.BackColor.A == 255) return c.BackColor;
            return Theme.Surface;
        }

        public static KeyCapState StateOf(HubShortcuts mgr, ShortcutSlot s)
        {
            if (s.Current.IsEmpty) return KeyCapState.None;
            if (mgr != null && mgr.DuplicateOf(s) != null) return KeyCapState.Duplicate;
            if (s.Error != null) return KeyCapState.Error;
            return KeyCapState.Normal;
        }

        public static string TipOf(HubShortcuts mgr, ShortcutSlot s)
        {
            if (s.Current.IsEmpty) return "ショートカットは登録していません";
            var dup = mgr == null ? null : mgr.DuplicateOf(s);
            if (dup != null) return "⚠ " + dup.Describe() + " と同じ組み合わせです。どちらかを変更してください";
            if (s.Error != null) return "⚠ " + s.Error;
            return s.HubOwned ? "Teal Hub が起動している間 (通知領域にいる間も) 使えます"
                              : s.AppName + " が起動している間に使えます (" + s.AppName + " が登録します)";
        }
    }

    /// <summary>ショートカット 1 つぶんの「名前 [キー] 変更 解除」(カードの下段に並べる)</summary>
    public class ShortcutChip : FlowLayoutPanel
    {
        public readonly ShortcutSlot Slot;
        readonly Label name = new Label();
        public readonly KeyCap Cap = new KeyCap();
        public readonly LinkLabel Edit = new LinkLabel();
        public readonly LinkLabel Clear = new LinkLabel();
        public event Action<ShortcutSlot> EditRequested, ClearRequested;

        public ShortcutChip(ShortcutSlot slot, HubShortcuts mgr, ToolTip tip)
        {
            Slot = slot;
            WrapContents = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0, 0, Theme.S(14), 0); Padding = Padding.Empty;
            BackColor = Theme.Surface;
            name.Text = slot.Label; name.Font = Theme.Small; name.ForeColor = Theme.SubText; name.AutoSize = true; name.UseMnemonic = false;
            name.Margin = new Padding(0, Theme.S(4), Theme.S(4), 0);
            Cap.Margin = new Padding(0, 0, Theme.S(6), 0);
            foreach (var l in new[] { Edit, Clear })
            {
                l.AutoSize = true; l.Font = Theme.Small; l.LinkColor = Theme.Accent; l.ActiveLinkColor = Theme.AccentPressed;
                l.LinkBehavior = LinkBehavior.HoverUnderline; l.Margin = new Padding(0, Theme.S(4), Theme.S(4), 0); l.UseMnemonic = false;
            }
            Clear.Text = "解除";
            Edit.LinkClicked += (s, e) => { if (EditRequested != null) EditRequested(Slot); };
            Clear.LinkClicked += (s, e) => { if (ClearRequested != null) ClearRequested(Slot); };
            Controls.AddRange(new Control[] { name, Cap, Edit, Clear });
            Refresh_(mgr, tip);
        }

        public void Refresh_(HubShortcuts mgr, ToolTip tip)
        {
            Cap.Set(Slot.Current.DisplayOrNone, KeyCap.StateOf(mgr, Slot));
            Edit.Text = Slot.Current.IsEmpty ? "登録" : "変更";
            Clear.Visible = !Slot.Current.IsEmpty;
            if (tip != null) { tip.SetToolTip(Cap, KeyCap.TipOf(mgr, Slot)); tip.SetToolTip(Edit, Slot.Describe() + " のショートカットを" + (Slot.Current.IsEmpty ? "登録" : "変更") + "します"); tip.SetToolTip(Clear, "ショートカットをなしにします"); }
        }
    }
}
