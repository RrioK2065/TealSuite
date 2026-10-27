// Teal Pass (パスワード管理) - 画面部品 (強さメーター / 縦積みヘルパー)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pass
{
    /// <summary>5 段階の強さメーター + ラベル</summary>
    public class StrengthBar : Control
    {
        Strength st;
        public StrengthBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(34);
            Font = Theme.Small;
        }
        public Strength Value { get { return st; } }
        public void SetPassword(string pw) { st = StrengthMeter.Evaluate(pw); Invalidate(); }

        public static Color LevelColor(int level)
        {
            switch (level)
            {
                case 0: return Theme.Danger;
                case 1: return Color.FromArgb(0xE8, 0x70, 0x2E);   // オレンジ
                case 2: return Theme.Warning;                       // 黄
                case 3: return Color.FromArgb(0x26, 0x9C, 0x93);   // 明るいティール
                default: return Theme.Accent;                       // ティール (とても強い)
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int segs = 5, gap = Theme.S(4), h = Theme.S(6);
            float w = (Width - gap * (segs - 1)) / (float)segs;
            bool empty = st.Bits <= 0;
            var c = LevelColor(st.Level);
            for (int i = 0; i < segs; i++)
            {
                var r = new RectangleF(i * (w + gap), 1, w, h);
                using (var path = Theme.RoundRect(r, h / 2f))
                using (var b = new SolidBrush(!empty && i <= st.Level ? c : Theme.SurfaceAlt)) g.FillPath(b, path);
            }
            string text = empty ? "強さ: -" : string.Format("強さ: {0}  (約 {1:0} ビット)", st.Label, st.Bits);
            TextRenderer.DrawText(g, text, Font, new Rectangle(0, h + Theme.S(4), Width, Height - h - Theme.S(4)),
                empty ? Theme.SubText : Theme.Blend(c, Theme.Text, 0.25f), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    public static class Ly
    {
        /// <summary>上から順に Dock=Top で積む (WinForms は後から追加したものが上に来るので逆順に追加)</summary>
        public static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                var c = topToBottom[i];
                if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top;
                parent.Controls.Add(c);
            }
        }

        /// <summary>入力欄 + 右側のボタン群の 1 行</summary>
        public static Panel FieldRow(Control field, params Control[] buttons)
        {
            var row = new Panel { Height = field.Height, BackColor = Color.Transparent };
            field.Dock = DockStyle.Fill;
            row.Controls.Add(field);
            // Dock=Right は後から追加したものほど外側 (右端) に来る → 左から field | b0 | b1 の順になる
            for (int i = 0; i < buttons.Length; i++)
            {
                row.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(6), BackColor = Color.Transparent });
                buttons[i].Dock = DockStyle.Right;
                row.Controls.Add(buttons[i]);
            }
            row.Height = Math.Max(field.Height, Theme.S(Theme.ButtonHeight));
            return row;
        }

        public static Label SectionLabel(string text)
        {
            return new Label { Text = text, Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = false, Height = Theme.Small.Height + Theme.S(8), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, BackColor = Color.Transparent };
        }
    }
}
