// Teal Pass (パスワード管理) - 登録・編集ダイアログ
using System;
using System.Drawing;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pass
{
    public class EntryForm : Form
    {
        public readonly VaultEntry Entry;
        readonly Func<string> generate;
        public readonly TextBox tbSite = new TextBox(), tbUrl = new TextBox(), tbUserId = new TextBox(), tbPassword = new TextBox(), tbMemo = new TextBox();
        readonly StrengthBar strength = new StrengthBar();
        readonly Label error = new Label();
        readonly FlatButton btnShow = new FlatButton("表示", ButtonKind.Secondary);

        public EntryForm(VaultEntry entry, bool isNew, Func<string> generator)
        {
            Entry = entry; generate = generator;
            Theme.ApplyForm(this);
            Text = isNew ? "パスワードを追加" : "パスワードを編集";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(480, 560);
            KeyPreview = true;

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(18, 10, 18, 12) };
            var outer = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            outer.Controls.Add(card);

            tbSite.Text = entry.Site; tbUrl.Text = entry.Url; tbUserId.Text = entry.UserId; tbPassword.Text = entry.Password;
            tbMemo.Text = (entry.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            tbPassword.Font = Theme.Mono;
            tbPassword.UseSystemPasswordChar = !isNew;
            btnShow.Text = tbPassword.UseSystemPasswordChar ? "表示" : "隠す";
            btnShow.FitWidth(64);
            btnShow.Click += (s, e) => { tbPassword.UseSystemPasswordChar = !tbPassword.UseSystemPasswordChar; btnShow.Text = tbPassword.UseSystemPasswordChar ? "表示" : "隠す"; };
            var btnGen = new FlatButton("生成", ButtonKind.Secondary).FitWidth(64);
            btnGen.Click += (s, e) => { tbPassword.Text = generate(); tbPassword.UseSystemPasswordChar = false; btnShow.Text = "隠す"; };
            tbPassword.TextChanged += (s, e) => strength.SetPassword(tbPassword.Text);
            strength.SetPassword(tbPassword.Text);
            Ui.SetCue(tbSite, "例: Google、Amazon"); Ui.SetCue(tbUrl, "https://"); Ui.SetCue(tbUserId, "メールアドレスやユーザー名");

            tbMemo.Multiline = true; tbMemo.AcceptsReturn = true; tbMemo.ScrollBars = ScrollBars.Vertical; tbMemo.WordWrap = true;
            var memoFrame = new InputFrame(tbMemo, true) { Dock = DockStyle.Fill };

            error.ForeColor = Theme.Danger; error.Font = Theme.Small; error.Height = Theme.Small.Height + Theme.S(6); error.UseMnemonic = false;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(Theme.ButtonHeight + 12), Dock = DockStyle.Bottom, Padding = Theme.P(0, 10, 0, 0), BackColor = Theme.Surface };
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            CancelButton = cancel;

            card.Controls.Add(memoFrame);
            card.Controls.Add(buttons);
            Ly.Stack(card,
                Ly.SectionLabel("サイト名 / サービス名 (必須)"), new InputFrame(tbSite),
                Ly.SectionLabel("URL"), new InputFrame(tbUrl),
                Ly.SectionLabel("ユーザー名 / ID"), new InputFrame(tbUserId),
                Ly.SectionLabel("パスワード"), Ly.FieldRow(new InputFrame(tbPassword), btnShow, btnGen),
                Ui.Spacer(6), strength,
                error,
                Ly.SectionLabel("メモ (秘密の質問の答え、契約内容など)"));
            Controls.Add(outer);
            Shown += (s, e) => tbSite.Focus();
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; } };
        }

        void Commit()
        {
            if (tbSite.Text.Trim().Length == 0) { error.Text = "サイト名を入力してください。"; tbSite.Focus(); return; }
            if (tbPassword.Text.Length == 0 && !Dialogs.Confirm(this, "パスワードが空です。このまま保存しますか？")) { tbPassword.Focus(); return; }
            Entry.Site = tbSite.Text.Trim();
            Entry.Url = tbUrl.Text.Trim();
            Entry.UserId = tbUserId.Text.Trim();
            Entry.Password = tbPassword.Text;
            Entry.Memo = tbMemo.Text;
            Entry.Updated = DateTime.Now;
            DialogResult = DialogResult.OK;
        }
    }
}
