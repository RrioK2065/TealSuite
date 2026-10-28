// Teal Clip (コピー履歴) - 1.8.0 のダイアログ (定型文の入力・記録の設定)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    /// <summary>定型文・項目の文字を入力する</summary>
    public class SnippetForm : Form
    {
        public readonly TextBox Box = new TextBox();
        public string Value { get { return Box.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"); } }

        public SnippetForm(string title, string text)
        {
            DialogKit.Setup(this, title);
            AutoSize = false;
            ClientSize = Theme.S(460, 300);
            Padding = Theme.P(16, 12, 16, 12);
            Box.Multiline = true; Box.AcceptsReturn = true; Box.ScrollBars = ScrollBars.Vertical; Box.WordWrap = true;
            Box.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            var frame = new InputFrame(Box, true) { Dock = DockStyle.Fill };
            var note = Lay.Note("あいさつ文・住所・よく使う URL などを登録しておくと、ショートカットの一覧からすぐ貼り付けられます。");
            note.Dock = DockStyle.Top;
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => { if (Box.Text.Trim().Length == 0) { Box.Focus(); return; } DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            var btns = DialogKit.Buttons(ok, cancel);
            bar.Controls.Add(btns);
            Controls.Add(frame);
            Controls.Add(Ui.Spacer(8));
            Controls.Add(note);
            Controls.Add(bar);
            Shown += (s, e) => { Box.Focus(); Box.SelectionStart = Box.TextLength; };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter)) { if (Box.Text.Trim().Length > 0) DialogResult = DialogResult.OK; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>記録の設定 (件数・自動削除・除外キーワード・パスワードらしい文字)</summary>
    public class ClipOptionsForm : Form
    {
        public readonly ComboBox SizeBox = Lay.Combo(History.Capacities.Select(n => n + " 件").ToArray());
        public readonly ComboBox DeleteBox = Lay.Combo(ChSettings.AutoDeleteLabels);
        public readonly TextBox WordsBox = new TextBox();
        public readonly ToggleSwitch PassSwitch = new ToggleSwitch("パスワードらしい文字は記録しない");

        public int Capacity { get { return History.Capacities[Math.Max(0, SizeBox.SelectedIndex)]; } }
        public int AutoDeleteDays { get { return ChSettings.AutoDeleteChoices[Math.Max(0, DeleteBox.SelectedIndex)]; } }
        public List<string> Words { get { return WordsBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList(); } }
        public bool SkipPasswords { get { return PassSwitch.Checked; } }

        public ClipOptionsForm(ChSettings s)
        {
            DialogKit.Setup(this, "記録の設定");
            AutoSize = false;
            ClientSize = Theme.S(460, 470);
            Padding = Theme.P(16, 12, 16, 12);
            int si = Array.IndexOf(History.Capacities, History.ClampCapacity(s.HistorySize));
            SizeBox.SelectedIndex = si < 0 ? 0 : si;
            int di = Array.IndexOf(ChSettings.AutoDeleteChoices, s.AutoDeleteDays);
            DeleteBox.SelectedIndex = di < 0 ? 0 : di;
            WordsBox.Multiline = true; WordsBox.AcceptsReturn = true; WordsBox.ScrollBars = ScrollBars.Vertical;
            WordsBox.Text = string.Join("\r\n", s.Words);
            PassSwitch.Checked = s.SkipPasswordLike;

            var grid = new FormGrid(2, 200);
            grid.Dock = DockStyle.Top;
            grid.Add("保存する件数", SizeBox);
            grid.Add("古い履歴を自動で消す", DeleteBox);
            var wordsFrame = new InputFrame(WordsBox, true) { Dock = DockStyle.Fill };
            var notes = Lay.Note("ショートカットで出る一覧には、上から 10 件 (1〜0 キー) が並びます。ピン留め・定型文は自動では消えません。");
            notes.Dock = DockStyle.Top;
            var wl = Lay.SectionLabel("記録しないことば (1 行に 1 つ。これを含む文字は記録しません)");
            wl.Dock = DockStyle.Top;
            PassSwitch.Dock = DockStyle.Bottom;
            var passNote = Lay.Note("空白なしで英大文字・小文字・数字・記号が混ざった 8〜64 文字を記録しません。Teal Pass からのコピーは、この設定がなくても記録しません。");
            passNote.Dock = DockStyle.Bottom;

            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = null; CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(ok, cancel));

            Controls.Add(wordsFrame);
            Controls.Add(wl);
            Controls.Add(Ui.Spacer(10));
            Controls.Add(notes);
            Controls.Add(grid);
            Controls.Add(Ui.Spacer(8, DockStyle.Bottom));
            Controls.Add(passNote);
            Controls.Add(PassSwitch);
            Controls.Add(bar);
        }
    }
}
