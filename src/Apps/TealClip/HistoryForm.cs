// Teal Clip (コピー履歴) - 履歴の管理画面 (コピー・ピン留め・削除・すべて削除)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    public class HistoryForm : Form
    {
        readonly TrayContext ctx;
        readonly HeaderBar header;
        readonly ListBox list = new ListBox();
        readonly TextBox preview = new TextBox();
        readonly FooterBar footer = new FooterBar();
        readonly FlatButton btnCopy, btnPin, btnDel, btnEdit;
        readonly TextBox search = new TextBox();
        readonly CardPanel card;
        readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        bool suppress;

        public ListBox List { get { return list; } }
        public TextBox SearchBox { get { return search; } }
        public ContextMenuStrip MoreMenu { get { return moreMenu; } }
        public string FooterText { get { return footer.StatusText; } }

        public HistoryForm(TrayContext ctx)
        {
            this.ctx = ctx;
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(520, 420);
            KeyPreview = true;

            header = HeaderBar.ForApp("");
            var btnMore = header.AddAction(new FlatButton("その他 ▾", ButtonKind.Secondary)).FitWidth(90);
            var btnSnippet = header.AddAction(new FlatButton("＋ 定型文", ButtonKind.Primary)).FitWidth(100);
            moreMenu.Font = Theme.Body; moreMenu.ShowImageMargin = false;
            btnMore.Click += (s, e) => { BuildMoreMenu(); moreMenu.Show(btnMore, new Point(0, btnMore.Height)); };
            btnSnippet.Click += (s, e) => AddSnippet();
            new ToolTip().SetToolTip(btnSnippet, "よく使う文 (あいさつ・住所など) を登録します。ピン留めとして、いつも上に並びます");
            UpdateSubtitle();

            // 一覧
            card = new CardPanel("最近コピーした文字") { Dock = DockStyle.Fill };
            Ui.SetCue(search, "履歴を検索  Ctrl+F");
            search.TextChanged += (s, e) => RefreshList();
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && search.Text.Length > 0) { search.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Min(255, Theme.Body.Height + Theme.Small.Height + Theme.S(14));
            list.IntegralHeight = false;
            list.DrawItem += DrawRow;
            list.SelectedIndexChanged += (s, e) => { if (!suppress) UpdateSelection(); };
            list.DoubleClick += (s, e) => CopySelected();
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopySelected(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.P) { TogglePin(); e.Handled = e.SuppressKeyPress = true; }
            };

            // 下: 全文プレビュー + 操作
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(150), BackColor = Theme.Surface, Padding = Theme.P(0, 8, 0, 0) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Surface, Padding = Theme.P(0, 8, 0, 0), WrapContents = false };
            btnDel = new FlatButton("削除", ButtonKind.Secondary);
            btnPin = new FlatButton("ピン留め", ButtonKind.Secondary); btnPin.FitWidth(110);
            btnCopy = new FlatButton("コピー", ButtonKind.Primary); btnCopy.FitWidth(96);
            btnEdit = new FlatButton("編集", ButtonKind.Secondary);
            bar.Controls.Add(btnCopy); bar.Controls.Add(btnPin); bar.Controls.Add(btnEdit); bar.Controls.Add(btnDel);
            btnEdit.Click += (s, e) => EditSelected();
            btnCopy.Click += (s, e) => CopySelected();
            btnPin.Click += (s, e) => TogglePin();
            btnDel.Click += (s, e) => DeleteSelected();
            preview.Multiline = true; preview.ReadOnly = true; preview.BorderStyle = BorderStyle.None; preview.BackColor = Theme.Surface;
            preview.ScrollBars = ScrollBars.Vertical; preview.Font = Theme.Body; preview.WordWrap = true;
            var pf = new InputFrame(preview, true) { Dock = DockStyle.Fill };
            bottom.Controls.Add(pf);
            bottom.Controls.Add(bar);

            card.Controls.Add(list);
            card.Controls.Add(Ui.Spacer(6));
            card.Controls.Add(new InputFrame(search) { Dock = DockStyle.Top });
            card.Controls.Add(bottom);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12) };
            body.Controls.Add(card);
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            WindowMemory.Attach(this, 680, 560, "historyWindow");
            FormClosing += (s, e) =>
            {
                // × で閉じたら隠す (常駐していれば通知領域に残る。常駐しなければ少しして終了する)
                if (e.CloseReason == CloseReason.UserClosing && !AppHost.IsExiting && !ctx.IsExited) { e.Cancel = true; Hide(); }
            };
            RefreshList();
        }

        /// <summary>見出しの下の 1 行 (テスト用にも使う)</summary>
        public string SubtitleText { get; private set; }

        public void UpdateSubtitle()
        {
            // 「閉じても常駐」の説明は起動時の通知に任せ、ヘッダーは呼び出しキーだけにする (幅が足りないため)
            SubtitleText = (ctx.Paused ? "記録を一時停止中 ・ " : "") + (ctx.Hotkey.IsEmpty ? "ショートカットなし" : "呼び出し: " + ctx.Hotkey.Display) + (ctx.Resident ? "" : " ・ 常駐オフ");
            header.SetDetail(SubtitleText);
            header.Invalidate();
        }

        public void RefreshList()
        {
            var keep = list.SelectedItem as ClipItem;
            suppress = true;
            list.BeginUpdate();
            list.Items.Clear();
            var q = search.Text.Trim();
            foreach (var i in q.Length == 0 ? ctx.History.Items : ctx.History.Search(q)) list.Items.Add(i);
            card.Title = q.Length == 0 ? "最近コピーした文字 (最大 " + ctx.History.Capacity + " 件)" : "検索結果  " + list.Items.Count + " / " + ctx.History.Items.Count + " 件";
            card.Invalidate();
            list.Invalidate();
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = keep != null && list.Items.Contains(keep) ? list.Items.IndexOf(keep) : 0;
            suppress = false;
            UpdateSelection();
        }

        ClipItem Selected { get { return list.SelectedItem as ClipItem; } }

        void UpdateSelection()
        {
            var it = Selected;
            preview.Text = it == null ? "" : it.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            btnCopy.Enabled = btnDel.Enabled = btnPin.Enabled = btnEdit.Enabled = it != null;
            btnPin.Text = it != null && it.Pinned ? "ピンを外す" : "ピン留め";
            footer.StatusText = (ctx.Paused ? "記録を一時停止中 ・ " : "") + ctx.History.Items.Count + " / " + ctx.History.Capacity + " 件 ・ ピン留め " + ctx.History.PinnedCount + " 件 ・ ピン留めした項目は古くなっても消えません";
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var it = (ClipItem)list.Items[e.Index];
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
            if (sel)
                using (var path = Theme.RoundRect(new RectangleF(r.X + 1, r.Y + 2, r.Width - 3, r.Height - 4), Theme.S(Theme.Radius)))
                using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, path);
            int bs = Theme.S(24);
            var br = new Rectangle(r.X + Theme.S(8), r.Y + (r.Height - bs) / 2, bs, bs);
            using (var path = Theme.RoundRect(br, Theme.S(5)))
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.SurfaceAlt)) g.FillPath(b, path);
            int pos = ctx.History.Items.IndexOf(it);
            if (pos >= 0 && pos < History.PopupRows)
                TextRenderer.DrawText(g, pos == 9 ? "0" : (pos + 1).ToString(), Theme.Bold, br, sel ? Color.White : Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var tf = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int x = br.Right + Theme.S(10), right = r.Right - Theme.S(10);
            int y = r.Y + (r.Height - Theme.Body.Height - Theme.Small.Height) / 2;
            var meta = When(it.Time) + "  ・  " + it.Text.Length + " 文字" + (it.Pinned ? "  ・  ★ ピン留め" : "");
            TextRenderer.DrawText(g, it.OneLine, Theme.Body, new Rectangle(x, y, right - x, Theme.Body.Height), Theme.Text, tf);
            TextRenderer.DrawText(g, meta, Theme.Small, new Rectangle(x, y + Theme.Body.Height, right - x, Theme.Small.Height + 2), it.Pinned ? Theme.Accent : Theme.SubText, tf);
        }

        static string When(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "たった今";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " 分前";
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            return t.ToString("M/d HH:mm");
        }

        void CopySelected()
        {
            var it = Selected; if (it == null) return;
            if (ctx.SetClipboardOwn(it.Text)) footer.StatusText = "クリップボードにコピーしました。貼り付けたい場所で Ctrl+V を押してください。";
        }

        void TogglePin()
        {
            var it = Selected; if (it == null) return;
            if (!it.Pinned && ctx.History.PinnedCount >= ctx.History.PinLimit) { Dialogs.Info(this, "ピン留めできるのは " + ctx.History.PinLimit + " 件までです。\n「その他」→「記録の設定」で件数を増やすと、もっとピン留めできます。"); return; }
            ctx.History.SetPinned(it, !it.Pinned);
        }

        void DeleteSelected()
        {
            var it = Selected; if (it == null) return;
            int idx = list.SelectedIndex;
            ctx.History.Remove(it);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
        }

        void ClearAll()
        {
            if (ctx.History.Items.Count == 0) return;
            if (!Dialogs.Confirm(this, "履歴をすべて削除しますか？ピン留めした項目も消えます。\nこの操作は元に戻せません。")) return;
            ctx.History.Clear();
        }

        void BuildMoreMenu()
        {
            moreMenu.Items.Clear();
            moreMenu.Items.Add(new ToolStripMenuItem(ctx.Paused ? "記録を再開する" : "記録を一時停止", null, (s, e) => ctx.SetPaused(!ctx.Paused)));
            moreMenu.Items.Add(new ToolStripMenuItem("記録の設定 (件数・自動削除・除外)…", null, (s, e) => ShowOptions()));
            moreMenu.Items.Add(new ToolStripMenuItem("ショートカット・常駐の設定 (Teal Hub)…", null, (s, e) => ctx.ShowSettings()));
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(new ToolStripMenuItem("すべて削除…", null, (s, e) => ClearAll()) { Enabled = ctx.History.Items.Count > 0 });
        }

        void ShowOptions()
        {
            using (var f = new ClipOptionsForm(ctx.Settings))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (f.Capacity < ctx.History.Items.Count && !Dialogs.Confirm(this, "件数を " + f.Capacity + " 件に減らすと、ピン留めしていない古い履歴が消えます。よろしいですか?")) return;
                int removed = ctx.ApplySettings(f.Capacity, f.AutoDeleteDays, f.Words, f.SkipPasswords);
                footer.StatusText = "記録の設定を保存しました" + (removed > 0 ? " (" + removed + " 件を整理)" : "");
            }
        }

        public void AddSnippet()
        {
            if (ctx.History.PinnedCount >= ctx.History.PinLimit) { Dialogs.Info(this, "ピン留め (定型文) は " + ctx.History.PinLimit + " 件までです。\n「その他」→「記録の設定」で件数を増やすか、使わないピンを外してください。"); return; }
            using (var f = new SnippetForm("定型文を追加", ""))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                var it = ctx.History.AddSnippet(f.Value);
                if (it == null) return;
                search.Text = "";
                list.SelectedItem = it;
                footer.StatusText = "定型文を追加しました (ピン留め)。" + (ctx.Hotkey.IsEmpty ? "" : ctx.Hotkey.Display + " ですぐ貼り付けられます。");
            }
        }

        void EditSelected()
        {
            var it = Selected; if (it == null) return;
            using (var f = new SnippetForm(it.Pinned ? "定型文を編集" : "文字を編集", it.Text))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (ctx.History.UpdateText(it, f.Value)) { list.SelectedItem = it; footer.StatusText = "書き換えました"; }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
