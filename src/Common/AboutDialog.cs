// TealSuite 共通: 「このアプリについて」
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public class AboutDialog : Form
    {
        public static void ShowFor(IWin32Window owner)
        {
            using (var d = new AboutDialog()) d.ShowDialog(owner);
        }

        public AboutDialog()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.DisplayName + " について";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(440, 270);
            BackColor = Theme.Surface;

            var icon = new PictureBox { Image = Theme.AppIconBitmap(Theme.S(64)), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(Theme.S(24), Theme.S(24)) };
            int x = Theme.S(24 + 64 + 18);
            var name = new Label { Text = AppInfo.DisplayName, Font = Theme.Header, AutoSize = true, Location = new Point(x, Theme.S(26)), UseMnemonic = false };
            var ver = new Label { Text = (string.IsNullOrEmpty(AppInfo.Role) ? "" : AppInfo.Role + "  ・  ") + "バージョン " + AppInfo.Version + "   (" + AppInfo.SuiteName + ")", Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = true, Location = new Point(x, Theme.S(56)) };
            var desc = new Label { Text = AppInfo.Description ?? "", AutoSize = false, Location = new Point(x, Theme.S(80)), Size = new Size(ClientSize.Width - x - Theme.S(20), Theme.S(40)), UseMnemonic = false };
            var dataCap = new Label { Text = "データの保存先 (このPCのこのユーザーのみ)", Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = true, Location = new Point(Theme.S(24), Theme.S(132)) };
            var dataPath = new TextBox { Text = DataStore.AppDir, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, Font = Theme.Small, Location = new Point(Theme.S(24), Theme.S(152)), Width = ClientSize.Width - Theme.S(48) };

            var open = new FlatButton("データフォルダを開く", ButtonKind.Secondary);
            open.Location = new Point(Theme.S(24), ClientSize.Height - Theme.S(24) - open.Height);
            open.Click += (s, e) => AppHost.OpenFolder(DataStore.AppDir);
            var close = new FlatButton("閉じる", ButtonKind.Primary) { DialogResult = DialogResult.OK };
            close.FitWidth(96);
            close.Location = new Point(ClientSize.Width - Theme.S(24) - close.Width, open.Top);
            AcceptButton = close; CancelButton = close;

            Controls.AddRange(new Control[] { icon, name, ver, desc, dataCap, dataPath, open, close });
        }
    }
}
