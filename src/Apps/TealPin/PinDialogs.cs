// Teal Pin - 入力ダイアログ (自動で固定するルール / プリセットの名前)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pin
{
    /// <summary>起動中のアプリ (ルールの「アプリ」を選ぶ用)</summary>
    public class RunningApp
    {
        public string Exe, AppName, Title;
        public override string ToString() { return AppName + " (" + Exe + ")"; }
    }

    /// <summary>自動で固定するルールの追加・編集</summary>
    public class RuleForm : Form
    {
        public readonly TextBox ExeBox = new TextBox(), TitleBox = new TextBox();
        public readonly ComboBox PresetBox;
        public readonly ToggleSwitch EnabledSwitch = new ToggleSwitch("このルールを使う");
        public readonly FlatButton OkButton, PickButton;
        public readonly Label ErrorLabel;
        readonly List<PinPreset> presets;
        readonly ContextMenuStrip pickMenu = new ContextMenuStrip { ShowImageMargin = false };
        public PinRule Result { get; private set; }

        public RuleForm(PinRule rule, List<PinPreset> allPresets, List<RunningApp> running)
        {
            bool isNew = rule == null;
            DialogKit.Setup(this, isNew ? "自動で固定するルールを追加" : "ルールを編集");
            AutoSize = false;
            ClientSize = Theme.S(480, 410);
            Padding = Theme.P(16, 12, 16, 12);
            presets = allPresets ?? new List<PinPreset>();

            foreach (var tb in new[] { ExeBox, TitleBox }) { tb.Font = Theme.Body; tb.BorderStyle = BorderStyle.None; }
            ExeBox.MaxLength = 120; TitleBox.MaxLength = 100;
            Ui.SetCue(ExeBox, "例: notepad.exe、Teams.exe、vlc*");
            Ui.SetCue(TitleBox, "例: 会議 (空ならタイトルは問いません)");

            PickButton = new FlatButton("起動中のアプリから選ぶ ▾", ButtonKind.Secondary).FitWidth(120);
            pickMenu.Font = Theme.Body;
            var apps = (running ?? new List<RunningApp>()).Where(a => !string.IsNullOrEmpty(a.Exe))
                .GroupBy(a => a.Exe, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(a => a.AppName, StringComparer.CurrentCulture).ToList();
            foreach (var a in apps.Take(40))
            {
                var aa = a;
                pickMenu.Items.Add(new ToolStripMenuItem(a.AppName + "  ―  " + a.Exe, null, (s, e) => { ExeBox.Text = aa.Exe; }));
            }
            if (apps.Count == 0) pickMenu.Items.Add(new ToolStripMenuItem("(選べるアプリがありません)") { Enabled = false });
            PickButton.Click += (s, e) => pickMenu.Show(PickButton, new Point(0, PickButton.Height));

            PresetBox = Lay.Combo(new[] { "変えない (今の位置とサイズのまま)" }.Concat(presets.Select(p => p.Name)).ToArray());
            EnabledSwitch.Checked = true;

            if (rule != null)
            {
                ExeBox.Text = rule.Exe ?? "";
                TitleBox.Text = rule.TitleContains ?? "";
                int i = presets.FindIndex(p => p.Id == rule.PresetId);
                PresetBox.SelectedIndex = i >= 0 ? i + 1 : 0;
                EnabledSwitch.Checked = rule.Enabled;
            }

            ErrorLabel = (Label)Lay.Note("", Theme.Danger);
            OkButton = new FlatButton(isNew ? "追加" : "保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            OkButton.Click += (s, e) => Accept(rule);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = OkButton; CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(OkButton, cancel));

            Lay.Stack(this,
                Lay.SectionLabel("アプリ (exe の名前)"),
                Lay.Row(new InputFrame(ExeBox), PickButton),
                Lay.Note("ファイル名だけで指定します (.exe は省けます)。* で「〜で始まる」なども指定できます。"),
                Ui.Spacer(6),
                Lay.SectionLabel("タイトルに含む文字 (なくてもよい)"),
                new InputFrame(TitleBox),
                Ui.Spacer(6),
                Lay.SectionLabel("位置とサイズ"),
                PresetBox,
                Ui.Spacer(10),
                EnabledSwitch,
                Ui.Spacer(4),
                ErrorLabel);
            Controls.Add(bar);
            Shown += (s, e) => { ExeBox.Focus(); ExeBox.SelectAll(); };
        }

        void Accept(PinRule original)
        {
            var err = RuleMatcher.Validate(ExeBox.Text, TitleBox.Text);
            if (err != null) { ErrorLabel.Text = err; ExeBox.Focus(); return; }
            int pi = PresetBox.SelectedIndex - 1;
            Result = new PinRule
            {
                Id = original != null ? original.Id : PinSettings.NewId(),
                Exe = RuleMatcher.NormalizeExe(ExeBox.Text),
                TitleContains = TitleBox.Text.Trim().Length == 0 ? null : TitleBox.Text.Trim(),
                PresetId = pi >= 0 && pi < presets.Count ? presets[pi].Id : null,
                Enabled = EnabledSwitch.Checked,
            };
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>プリセットの名前 (保存・名前の変更)</summary>
    public class PresetNameForm : Form
    {
        public readonly TextBox NameBox = new TextBox();
        public readonly Label ErrorLabel;
        public readonly FlatButton OkButton;
        public string PresetName { get { return NameBox.Text.Trim(); } }

        public PresetNameForm(string title, string initial, string note, Func<string, string> validate)
        {
            DialogKit.Setup(this, title);
            AutoSize = false;
            ClientSize = Theme.S(420, 230);
            Padding = Theme.P(16, 12, 16, 12);
            NameBox.Text = initial ?? ""; NameBox.Font = Theme.Body; NameBox.MaxLength = 40; NameBox.BorderStyle = BorderStyle.None;
            Ui.SetCue(NameBox, "例: 動画を小さく、資料を見ながら");
            ErrorLabel = (Label)Lay.Note("", Theme.Danger);
            OkButton = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            OkButton.Click += (s, e) =>
            {
                var err = validate != null ? validate(PresetName) : (PresetName.Length == 0 ? "名前を入れてください" : null);
                if (err != null) { ErrorLabel.Text = err; NameBox.Focus(); return; }
                DialogResult = DialogResult.OK;
            };
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = OkButton; CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(OkButton, cancel));
            Lay.Stack(this, Lay.SectionLabel("プリセットの名前"), new InputFrame(NameBox), Ui.Spacer(8), Lay.Note(note ?? ""), Ui.Spacer(4), ErrorLabel);
            Controls.Add(bar);
            Shown += (s, e) => { NameBox.Focus(); NameBox.SelectAll(); };
        }
    }
}
