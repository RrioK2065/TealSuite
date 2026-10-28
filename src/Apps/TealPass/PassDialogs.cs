// Teal Pass (パスワード管理) - 1.8.0 のダイアログ (安全チェック・以前のパスワード)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pass
{
    /// <summary>安全チェックの結果。行をダブルクリック (または「表示」) でその登録を開く</summary>
    public class HealthForm : Form
    {
        public VaultEntry Chosen;
        readonly RowList rows = new RowList(2);

        public HealthForm(HealthReport r, IList<VaultEntry> all)
        {
            Theme.ApplyForm(this);
            Text = "安全チェック";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(560, 520);
            KeyPreview = true;

            var outer = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            var card = new CardPanel(r.Total == 0 ? "問題は見つかりませんでした" : "見直したほうがよいパスワード  " + r.Total + " 件") { Dock = DockStyle.Fill, Padding = Theme.P(16, 10, 16, 12) };
            outer.Controls.Add(card);

            var tiles = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Height = new StatTile().PreferredHeight, BackColor = Theme.Surface };
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            tiles.Controls.Add(Tile("使い回し", r.Reused, r.Reused > 0 ? Theme.Danger : Theme.Text), 0, 0);
            tiles.Controls.Add(Tile("弱い", r.Weak, r.Weak > 0 ? Theme.Warning : Theme.Text), 1, 0);
            tiles.Controls.Add(Tile("1 年以上そのまま", r.Old, r.Old > 0 ? Theme.Warning : Theme.Text), 2, 0);

            rows.Dock = DockStyle.Fill;
            rows.EmptyText = all.Count == 0 ? "まだパスワードが登録されていません。" : "すべてのパスワードに問題はありません。\nこの調子で、サイトごとに違うパスワードを使いましょう。";
            rows.PaintRow = (g, b, o, sel) =>
            {
                var en = (VaultEntry)o;
                var iss = r.Of(en);
                int x = b.X + Theme.S(12), w = b.Width - Theme.S(24);
                int h1 = Theme.Title.Height, top = b.Y + (b.Height - h1 - Theme.Small.Height - Theme.S(4)) / 2;
                Draw.Text(g, (en.Favorite ? "★ " : "") + en.Site + (string.IsNullOrEmpty(en.UserId) ? "" : "  ·  " + en.UserId), Theme.Title, Theme.Text, new Rectangle(x, top, w, h1));
                var why = PassTools.IssueText(iss);
                var same = PassTools.SamePassword(all, en);
                if (same.Count > 0) why += " (" + string.Join("、", same.Take(3).Select(s => s.Site)) + (same.Count > 3 ? " ほか" : "") + ")";
                Draw.Text(g, why, Theme.Small, (iss & PassIssue.Reused) != 0 ? Theme.Danger : Theme.Blend(Theme.Warning, Color.Black, 0.25f), new Rectangle(x, top + h1 + Theme.S(4), w, Theme.Small.Height + 2));
            };
            rows.SetItems(r.Issues.OrderByDescending(kv => (int)(kv.Value & PassIssue.Reused)).ThenByDescending(kv => (int)(kv.Value & PassIssue.Weak))
                .ThenBy(kv => kv.Key.Site, StringComparer.CurrentCultureIgnoreCase).Select(kv => kv.Key).ToList());
            rows.DoubleClick += (s, e) => Open();

            var note = Lay.Note("同じパスワードをほかのサイトでも使っていると、1 か所から漏れたときに全部が危険になります。\n" +
                "「表示」で開いて「編集」→「生成」で新しいパスワードに変え、そのサイトでも変更してください。");
            note.Dock = DockStyle.Bottom;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(Theme.ButtonHeight + 12), Dock = DockStyle.Bottom, Padding = Theme.P(0, 10, 0, 0), BackColor = Theme.Surface };
            var close = new FlatButton("閉じる", ButtonKind.Secondary).FitWidth(88);
            var open = new FlatButton("表示", ButtonKind.Primary).FitWidth(88);
            close.DialogResult = DialogResult.Cancel;
            open.Click += (s, e) => Open();
            open.Enabled = rows.Items.Count > 0;
            buttons.Controls.Add(close); buttons.Controls.Add(open);
            CancelButton = close;

            card.Controls.Add(rows);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(tiles);
            card.Controls.Add(note);
            card.Controls.Add(buttons);
            Controls.Add(outer);
        }

        public RowList Rows { get { return rows; } }

        static Control Tile(string label, int value, Color c)
        {
            var t = new StatTile { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(8), 0) };
            t.Set(label, value + " 件", null, c);
            return t;
        }

        void Open()
        {
            var en = rows.Selected as VaultEntry;
            if (en == null) return;
            Chosen = en;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>以前のパスワード (新しい順)。コピーできる</summary>
    public class HistoryForm : Form
    {
        readonly RowList rows = new RowList(2);
        readonly HashSet<PasswordHistoryItem> shown = new HashSet<PasswordHistoryItem>();

        public HistoryForm(VaultEntry en, Action<string> copy)
        {
            Theme.ApplyForm(this);
            Text = "以前のパスワード - " + en.Site;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(460, 400);

            var outer = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            var card = new CardPanel("以前のパスワード  " + en.HistoryCount + " 件 (最大 " + PassTools.HistoryMax + " 件)") { Dock = DockStyle.Fill, Padding = Theme.P(16, 10, 16, 12) };
            outer.Controls.Add(card);
            rows.Dock = DockStyle.Fill;
            rows.PaintRow = (g, b, o, sel) =>
            {
                var h = (PasswordHistoryItem)o;
                int x = b.X + Theme.S(12), w = b.Width - Theme.S(24);
                int h1 = Theme.Mono.Height, top = b.Y + (b.Height - h1 - Theme.Small.Height - Theme.S(4)) / 2;
                Draw.Text(g, shown.Contains(h) ? h.Password : new string('●', Math.Min(16, Math.Max(6, h.Password.Length))), Theme.Mono, Theme.Text, new Rectangle(x, top, w, h1));
                Draw.Text(g, h.Changed.ToString("yyyy/MM/dd HH:mm") + " まで使っていたもの", Theme.Small, Theme.SubText, new Rectangle(x, top + h1 + Theme.S(4), w, Theme.Small.Height + 2));
            };
            rows.SetItems(en.History ?? new List<PasswordHistoryItem>());

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(Theme.ButtonHeight + 12), Dock = DockStyle.Bottom, Padding = Theme.P(0, 10, 0, 0), BackColor = Theme.Surface };
            var close = new FlatButton("閉じる", ButtonKind.Secondary).FitWidth(88);
            var bCopy = new FlatButton("コピー", ButtonKind.Primary).FitWidth(88);
            var bShow = new FlatButton("表示", ButtonKind.Secondary).FitWidth(72);
            close.DialogResult = DialogResult.Cancel;
            bCopy.Click += (s, e) => { var h = rows.Selected as PasswordHistoryItem; if (h != null) copy(h.Password); };
            bShow.Click += (s, e) => { var h = rows.Selected as PasswordHistoryItem; if (h == null) return; if (!shown.Remove(h)) shown.Add(h); rows.Invalidate(); };
            buttons.Controls.Add(close); buttons.Controls.Add(bCopy); buttons.Controls.Add(bShow);
            CancelButton = close;
            var note = Lay.Note("パスワードを変えたとき、前のものをここに残します (暗号化して保存)。\nサイト側の変更がうまくいかなかったときなどに使ってください。");
            note.Dock = DockStyle.Bottom;
            card.Controls.Add(rows);
            card.Controls.Add(note);
            card.Controls.Add(buttons);
            Controls.Add(outer);
        }
    }
}
