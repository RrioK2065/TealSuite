// Teal Memo (メモ) - メイン画面 (左: 検索と一覧 / 右: エディタ)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Memo
{
    public class MainForm : Form
    {
        NoteFile data;
        Note current;
        bool suppress, dirty;

        readonly TextBox searchBox = new TextBox();
        readonly ListBox list = new ListBox();
        readonly TextBox editor = new TextBox();
        readonly SplitContainer split = new SplitContainer();
        readonly FooterBar footer = new FooterBar();
        readonly Timer saveTimer = new Timer();

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(600, 400);
            KeyPreview = true;

            // ---- ヘッダー ----
            var header = HeaderBar.ForApp("入力は自動で保存されます");
            var btnDel = header.AddAction(new FlatButton("削除", ButtonKind.Danger));
            var btnNew = header.AddAction(new FlatButton("＋ 新規", ButtonKind.Primary));
            btnNew.FitWidth(96);
            btnNew.Click += (s, e) => NewNote();
            btnDel.Click += (s, e) => DeleteCurrent();
            var tips = new ToolTip();
            tips.SetToolTip(btnNew, "新しいメモ (Ctrl+N)");
            tips.SetToolTip(btnDel, "選択中のメモを削除 (Ctrl+Delete)");

            // ---- 左: 検索 + 一覧 ----
            var leftCard = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            searchBox.Font = Theme.Body;
            Ui.SetCue(searchBox, "検索…  (Ctrl+F)");
            searchBox.TextChanged += (s, e) => RefreshList();
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { searchBox.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            var searchFrame = new InputFrame(searchBox) { Dock = DockStyle.Top };

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Min(255, Theme.Title.Height + Theme.Small.Height + Theme.S(16));
            list.IntegralHeight = false;
            list.DrawItem += DrawListItem;
            list.SelectedIndexChanged += (s, e) => { if (!suppress) SelectNote(list.SelectedItem as Note); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteCurrent(); e.Handled = true; } };
            leftCard.Controls.Add(list);
            leftCard.Controls.Add(Ui.Spacer(8));
            leftCard.Controls.Add(searchFrame);

            // ---- 右: エディタ ----
            var rightCard = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(16, 12, 6, 12) };
            editor.Dock = DockStyle.Fill;
            editor.Multiline = true; editor.WordWrap = true; editor.AcceptsTab = true; editor.AcceptsReturn = true;
            editor.ScrollBars = ScrollBars.Vertical;
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = Theme.Surface;
            editor.Font = Theme.Editor;
            editor.MaxLength = 0;
            editor.HideSelection = false;
            editor.TextChanged += EditorChanged;
            editor.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.A) { editor.SelectAll(); e.Handled = e.SuppressKeyPress = true; } };
            rightCard.Controls.Add(editor);

            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = Theme.S(10);
            split.BackColor = Theme.Background;
            split.Panel1.Controls.Add(leftCard);
            split.Panel2.Controls.Add(rightCard);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12), BackColor = Theme.Background };
            body.Controls.Add(split);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            saveTimer.Interval = 700;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(false); };

            // ---- データ読み込み (初回は旧メモアプリから引き継ぎ) ----
            int migrated = NoteStore.MigrateLegacy();
            string warn;
            data = NoteStore.Load(out warn);
            if (warn != null) Load += (s, e) => Dialogs.Warn(this, warn);

            var ws = WindowMemory.Attach(this, 920, 620, "window", () => split.SplitterDistance);
            int sp = (ws != null && ws.Splitter >= Theme.S(150)) ? ws.Splitter : Theme.S(280);
            Load += (s, e) =>
            {
                try { split.Panel1MinSize = Theme.S(180); split.Panel2MinSize = Theme.S(240); split.SplitterDistance = Math.Min(sp, Math.Max(Theme.S(180), split.Width - Theme.S(260))); } catch { }
                if (data.Notes.Count == 0) NewNote();
                else { RefreshList(); if (list.Items.Count > 0) list.SelectedIndex = 0; else SelectNote(null); }
                if (migrated >= 0) footer.StatusText = "以前のメモアプリから " + migrated + " 件のメモを引き継ぎました";
                editor.Focus();
            };
            FormClosing += (s, e) => { saveTimer.Stop(); SaveNow(false); };
        }

        // ---- 一覧描画 ----
        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var n = (Note)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var g = e.Graphics;
            using (var bg = new SolidBrush(sel ? Theme.AccentSoft : Theme.Surface)) g.FillRectangle(bg, e.Bounds);
            if (sel) using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + Theme.S(6), Theme.S(3), e.Bounds.Height - Theme.S(12));
            int x = e.Bounds.X + Theme.S(12), w = e.Bounds.Width - Theme.S(18);
            var r1 = new Rectangle(x, e.Bounds.Y + Theme.S(7), w, Theme.Title.Height + 2);
            var r2 = new Rectangle(x, r1.Bottom + Theme.S(1), w, Theme.Small.Height + 2);
            var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, n.Title, Theme.Title, r1, Theme.Text, tf);
            TextRenderer.DrawText(g, NoteStore.FormatTime(n.UpdatedLocal) + "   " + n.Preview, Theme.Small, r2, Theme.SubText, tf);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.X + Theme.S(8), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.S(8), e.Bounds.Bottom - 1);
        }

        void RefreshList()
        {
            var items = NoteStore.Filter(data.Notes, searchBox.Text);
            suppress = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var n in items) list.Items.Add(n);
                list.SelectedIndex = current == null ? -1 : items.IndexOf(current);
            }
            finally { list.EndUpdate(); suppress = false; }
            if (current == null || !items.Contains(current))
            {
                if (items.Count > 0) list.SelectedIndex = 0; else SelectNote(null);
            }
        }

        void SelectNote(Note n)
        {
            current = n;
            suppress = true;
            editor.Text = n == null ? "" : (n.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            editor.Enabled = n != null;
            editor.SelectionStart = 0;
            editor.ScrollToCaret();
            suppress = false;
            UpdateStatus(null);
        }

        void EditorChanged(object sender, EventArgs e)
        {
            if (suppress || current == null) return;
            current.Text = editor.Text.Replace("\r\n", "\n");
            current.Touch();
            dirty = true;
            saveTimer.Stop(); saveTimer.Start();
            if (list.Items.Count > 0 && list.Items[0] != current) RefreshList(); else list.Invalidate();
            UpdateStatus("編集中…");
        }

        void UpdateStatus(string extra)
        {
            if (current == null)
            {
                footer.StatusText = data.Notes.Count == 0 ? "メモがありません。「＋ 新規」で作成できます" : "該当するメモがありません";
                return;
            }
            string s = data.Notes.Count + " 件   最終更新: " + current.UpdatedLocal.ToString("yyyy/MM/dd HH:mm:ss") + "   " + current.Text.Length + " 文字";
            footer.StatusText = extra != null ? extra + "   " + s : s;
        }

        bool SaveNow(bool showMessage)
        {
            if (!dirty && !showMessage) return true;
            try
            {
                NoteStore.Save(data);
                dirty = false;
                if (current != null) UpdateStatus("保存しました");
                return true;
            }
            catch (Exception ex)
            {
                footer.StatusText = "保存に失敗しました: " + ex.Message;
                DataStore.Log("save: " + ex);
                if (showMessage) Dialogs.Error(this, "保存に失敗しました。\n" + ex.Message);
                return false;
            }
        }

        void NewNote()
        {
            var n = Note.Create();
            data.Notes.Add(n);
            current = n;
            if (searchBox.Text.Length > 0) searchBox.Text = "";
            RefreshList();
            SelectNote(n);
            dirty = true;
            SaveNow(false);
            editor.Focus();
        }

        void DeleteCurrent()
        {
            if (current == null) return;
            if (!Dialogs.Confirm(this, "「" + current.Title + "」を削除しますか？\nこの操作は元に戻せません。", "メモの削除")) return;
            int idx = list.SelectedIndex;
            data.Notes.Remove(current);
            current = null;
            dirty = true;
            SaveNow(false);
            RefreshList();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(idx, list.Items.Count - 1));
            else SelectNote(null);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: NewNote(); return true;
                case Keys.Control | Keys.S: saveTimer.Stop(); dirty = true; SaveNow(true); return true;
                case Keys.Control | Keys.F: searchBox.Focus(); searchBox.SelectAll(); return true;
                case Keys.Control | Keys.Delete: DeleteCurrent(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
