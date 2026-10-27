// TealSuite 共通: ショートカットのキーを拾う入力欄 (Teal Hub の設定ページで使う)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TealSuite.Common
{
    /// <summary>押したキーの組み合わせをそのまま表示する入力欄</summary>
    public class HotkeyBox : TextBox
    {
        HotkeySpec value;
        public HotkeySpec Value { get { return value; } }
        public event EventHandler Captured;
        public HotkeyBox() { ReadOnly = true; ShortcutsEnabled = false; BackColor = Theme.Surface; Cursor = Cursors.Hand; TextAlign = HorizontalAlignment.Center; }

        public void SetValue(HotkeySpec v) { value = v; Text = EmptyText(); }
        string EmptyText() { return value.IsEmpty ? "なし" : value.Display; }

        static bool WinDown() { return GlobalHotkey.WinKeyDown(); }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift)) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            var spec = HotkeySpec.FromKeyEvent(keyData, WinDown());
            if (spec.IsEmpty)
            {
                // 修飾キーだけ押している途中: 途中経過を表示
                Text = spec.Mods == HotMods.None ? "" : new HotkeySpec { Mods = spec.Mods, Key = Keys.A }.Display.Replace(" + A", " + …");
                return true;
            }
            SetValue(spec);
            if (Captured != null) Captured(this, EventArgs.Empty);
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (Value.IsEmpty || Text.EndsWith("…") || Text.Length == 0) Text = EmptyText();
        }
    }
}
