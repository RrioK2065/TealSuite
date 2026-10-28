// Teal Memo (メモ) - メイン画面 (左: 表示の切り替え・検索・一覧 / 右: エディタ)
//  1.8.0: ピン留め・ゴミ箱 (30 日)・#タグで絞り込み・.txt の書き出しと読み込み・文字の大きさ
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        /// <summary>表示の切り替え (すべて / ピン留め / #タグ / ゴミ箱)</summary>
        public readonly ComboBox ViewBox = Lay.Combo("すべてのメモ");
        readonly List<string> viewKeys = new List<string> { null };
        readonly FlatButton btnPin, btnDel, btnNew, btnMore;
        readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        readonly ToolTip tips = new ToolTip();
        MemoSettings settings;
        Font editorFont;

        /// <summary>今の表示 (null = すべて / "pinned" / "#タグ" / "trash")</summary>
        public string CurrentView { get { int i = ViewBox.SelectedIndex; return i >= 0 && i < viewKeys.Count ? viewKeys[i] : null; } }
        bool TrashView { get { return CurrentView == "trash"; } }
        public NoteFile Data { get { return data; } }
        public Note Current { get { return current; } }
        public ListBox List { get { return list; } }
        public TextBox Editor { get { return editor; } }
        public ContextMenuStrip MoreMenu { get { return moreMenu; } }
        public string FooterText { get { return footer.StatusText; } }

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(600, 400);
            KeyPreview = true;

            // ---- ヘッダー ----
            var header = HeaderBar.ForApp("入力は自動で保存されます");
            btnMore = header.AddAction(new FlatButton("その他 ▾", ButtonKind.Secondary)).FitWidth(84);
            btnPin = header.AddAction(new FlatButton("ピン留め", ButtonKind.Secondary)).FitWidth(84);
            btnDel = header.AddAction(new FlatButton("削除", ButtonKind.Danger)).FitWidth(84);
            btnNew = header.AddAction(new FlatButton("＋ 新規", ButtonKind.Primary));
            btnNew.FitWidth(96);
            btnNew.Click += (s, e) => NewNote();
            btnDel.Click += (s, e) => DeleteCurrent();
            btnPin.Click += (s, e) => { if (TrashView) RestoreCurrent(); else TogglePin(); };
            btnMore.Click += (s, e) => { BuildMoreMenu(); moreMenu.Show(btnMore, new Point(0, btnMore.Height)); };
            moreMenu.Font = Theme.Body; moreMenu.ShowImageMargin = false;
            tips.SetToolTip(btnNew, "新しいメモ (Ctrl+N)");
            tips.SetToolTip(btnDel, "選択中のメモをゴミ箱へ (Ctrl+Delete)。ゴミ箱から 30 日以内なら戻せます");
            tips.SetToolTip(btnPin, "選択中のメモを一覧のいちばん上に固定 (Ctrl+P)");
            tips.SetToolTip(btnMore, "書き出し・読み込み・文字の大きさ・ゴミ箱");

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
            leftCard.Controls.Add(Ui.Spacer(6));
            ViewBox.Dock = DockStyle.Top;
            ViewBox.SelectedIndexChanged += (s, e) => { if (!suppress) { RefreshList(); UpdateButtons(); } };
            tips.SetToolTip(ViewBox, "すべてのメモ・ピン留め・#タグ・ゴミ箱を切り替えます。\n本文に「#買い物」のように書くと、タグとして絞り込めます");
            leftCard.Controls.Add(ViewBox);

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
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(false); RefreshViews(); };

            // ---- データ読み込み (初回は旧メモアプリから引き継ぎ) ----
            int migrated = NoteStore.MigrateLegacy();
            string warn;
            data = NoteStore.Load(out warn);
            if (warn != null) Load += (s, e) => Dialogs.Warn(this, warn);
            settings = DataStore.LoadQuiet(MemoSettings.FileName, () => new MemoSettings());
            ApplyFontSize();
            RefreshViews();

            var ws = WindowMemory.Attach(this, 920, 620, "window", () => split.SplitterDistance);
            int sp = (ws != null && ws.Splitter >= Theme.S(150)) ? ws.Splitter : Theme.S(280);
            Load += (s, e) =>
            {
                try { split.Panel1MinSize = Theme.S(180); split.Panel2MinSize = Theme.S(240); split.SplitterDistance = Math.Min(sp, Math.Max(Theme.S(180), split.Width - Theme.S(260))); } catch { }
                if (!data.Notes.Any(n => !n.InTrash)) NewNote();
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
            if (n.Pinned && !n.InTrash)
            {
                // ピン留めの印 (右上の小さなティールのラベル)
                const string mark = "ピン留め";
                int mw = TextRenderer.MeasureText(g, mark, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(10);
                var mr = new Rectangle(e.Bounds.Right - Theme.S(8) - mw, r1.Y + (r1.Height - Theme.Small.Height - Theme.S(4)) / 2, mw, Theme.Small.Height + Theme.S(4));
                using (var b = new SolidBrush(sel ? Theme.Surface : Theme.AccentSoft)) g.FillRectangle(b, mr);
                TextRenderer.DrawText(g, mark, Theme.Small, mr, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                r1.Width = Math.Max(10, mr.Left - Theme.S(6) - r1.X);
            }
            TextRenderer.DrawText(g, n.Title, Theme.Title, r1, n.InTrash ? Theme.SubText : Theme.Text, tf);
            string meta = n.InTrash ? "あと " + NoteStore.DaysLeft(n, DateTime.UtcNow) + " 日で消えます   " + n.Preview : NoteStore.FormatTime(n.UpdatedLocal) + "   " + n.Preview;
            TextRenderer.DrawText(g, meta, Theme.Small, r2, Theme.SubText, tf);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.X + Theme.S(8), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.S(8), e.Bounds.Bottom - 1);
        }

        void RefreshList()
        {
            var items = NoteStore.View(data.Notes, searchBox.Text, CurrentView);
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
            editor.ReadOnly = n != null && n.InTrash;
            editor.BackColor = Theme.Surface;
            editor.SelectionStart = 0;
            editor.ScrollToCaret();
            suppress = false;
            UpdateStatus(null);
            UpdateButtons();
        }

        void EditorChanged(object sender, EventArgs e)
        {
            if (suppress || current == null) return;
            if (current.InTrash) return;
            current.Text = editor.Text.Replace("\r\n", "\n");
            current.Touch();
            dirty = true;
            saveTimer.Stop(); saveTimer.Start();
            // 先頭 (ピン留めならピン留めの中の先頭) でなければ並べ直す
            var order = NoteStore.View(data.Notes, searchBox.Text, CurrentView);
            int want = order.IndexOf(current);
            if (want >= 0 && (want >= list.Items.Count || list.Items[want] != current)) RefreshList(); else list.Invalidate();
            UpdateStatus("編集中…");
        }

        void UpdateStatus(string extra)
        {
            int live = data.Notes.Count(n => !n.InTrash);
            if (current == null)
            {
                footer.StatusText = TrashView ? "ゴミ箱は空です" : live == 0 ? "メモがありません。「＋ 新規」で作成できます" : "該当するメモがありません";
                return;
            }
            if (current.InTrash)
            {
                footer.StatusText = (extra != null ? extra + "   " : "") + "ゴミ箱のメモ (読むだけ)。あと " + NoteStore.DaysLeft(current, DateTime.UtcNow) + " 日で自動で消えます。「元に戻す」で一覧に戻せます";
                return;
            }
            var tags = current.Tags;
            string s = live + " 件   最終更新: " + current.UpdatedLocal.ToString("yyyy/MM/dd HH:mm:ss") + "   " + current.Text.Length + " 文字" + (tags.Count > 0 ? "   タグ: " + string.Join(" ", tags.Select(t => "#" + t)) : "");
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
            // ゴミ箱やほかのタグを見ていたら「すべてのメモ」に戻す (タグの表示なら、そのタグを最初の行に入れておく)
            var v = CurrentView;
            if (v != null && v.StartsWith("#")) n.Text = v + "\n";
            if (v != null) SelectView(null);
            RefreshList();
            SelectNote(n);
            dirty = true;
            SaveNow(false);
            editor.Focus();
        }

        /// <summary>通常はゴミ箱へ (確認なし・戻せる)。ゴミ箱の中なら確認して完全に削除</summary>
        public void DeleteCurrent() { DeleteCurrent(true); }
        public void DeleteCurrent(bool ask)
        {
            if (current == null) return;
            int idx = list.SelectedIndex;
            string title = current.Title;
            if (current.InTrash)
            {
                if (ask && !Dialogs.Confirm(this, "「" + title + "」を完全に削除しますか？\nこの操作は元に戻せません。", "メモの削除")) return;
                data.Notes.Remove(current);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(current.Text)) data.Notes.Remove(current);   // 空のメモはそのまま消す
                else NoteStore.Trash(current, DateTime.UtcNow);
            }
            bool trashed = data.Notes.Contains(current);
            current = null;
            dirty = true;
            SaveNow(false);
            RefreshViews();
            RefreshList();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(idx, list.Items.Count - 1));
            else SelectNote(null);
            if (trashed) footer.StatusText = "「" + title + "」をゴミ箱に移しました (" + NoteStore.TrashDays + " 日以内ならゴミ箱から戻せます)";
        }

        /// <summary>ゴミ箱のメモを一覧に戻す</summary>
        public void RestoreCurrent()
        {
            if (current == null || !current.InTrash) return;
            var n = current;
            NoteStore.Restore(n);
            dirty = true; SaveNow(false);
            RefreshViews();
            RefreshList();
            footer.StatusText = "「" + n.Title + "」を元に戻しました";
        }

        public void TogglePin()
        {
            if (current == null || current.InTrash) return;
            current.Pinned = !current.Pinned;
            dirty = true; SaveNow(false);
            RefreshList();
            UpdateButtons();
            footer.StatusText = current.Pinned ? "ピン留めしました (一覧のいちばん上に出ます)" : "ピン留めを外しました";
        }

        public void EmptyTrash(bool ask)
        {
            int n = data.Notes.Count(x => x.InTrash);
            if (n == 0) { footer.StatusText = "ゴミ箱は空です"; return; }
            if (ask && !Dialogs.Confirm(this, "ゴミ箱の " + n + " 件のメモを完全に削除しますか？\nこの操作は元に戻せません。", "ゴミ箱を空にする")) return;
            data.Notes.RemoveAll(x => x.InTrash);
            if (current != null && !data.Notes.Contains(current)) current = null;
            dirty = true; SaveNow(false);
            RefreshViews(); RefreshList();
            footer.StatusText = n + " 件を完全に削除しました";
        }

        // ---- 表示の切り替え (すべて / ピン留め / #タグ / ゴミ箱) ----

        /// <summary>選べる表示を作り直す (今の選択はできるだけ残す)</summary>
        public void RefreshViews()
        {
            var keep = CurrentView;
            var keys = new List<string> { null };
            var labels = new List<string> { "すべてのメモ (" + data.Notes.Count(n => !n.InTrash) + ")" };
            int pinned = data.Notes.Count(n => n.Pinned && !n.InTrash);
            if (pinned > 0) { keys.Add("pinned"); labels.Add("ピン留め (" + pinned + ")"); }
            foreach (var t in NoteStore.AllTags(data.Notes))
            {
                int c = data.Notes.Count(n => !n.InTrash && n.Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)));
                keys.Add("#" + t); labels.Add("#" + t + " (" + c + ")");
            }
            int trash = data.Notes.Count(n => n.InTrash);
            keys.Add("trash"); labels.Add("ゴミ箱 (" + trash + ")");
            int idx = keys.FindIndex(k => string.Equals(k, keep, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            bool same = labels.Count == ViewBox.Items.Count && labels.Select((l, i) => (string)ViewBox.Items[i] == l).All(x => x) && ViewBox.SelectedIndex == idx;
            if (same) return;
            bool was = suppress;
            suppress = true;
            ViewBox.BeginUpdate();
            ViewBox.Items.Clear();
            ViewBox.Items.AddRange(labels.ToArray());
            viewKeys.Clear(); viewKeys.AddRange(keys);
            ViewBox.SelectedIndex = idx;
            ViewBox.EndUpdate();
            suppress = was;
            if (idx == 0 && keep != null && !was) RefreshList();   // 見ていたタグが無くなった
            UpdateButtons();
        }

        /// <summary>表示を選ぶ (null / "pinned" / "#タグ" / "trash")。なければ false</summary>
        public bool SelectView(string key)
        {
            RefreshViews();
            int i = viewKeys.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            ViewBox.SelectedIndex = i;
            return true;
        }

        void UpdateButtons()
        {
            bool trash = TrashView;
            btnPin.Text = trash ? "元に戻す" : (current != null && current.Pinned ? "ピン外す" : "ピン留め");
            btnPin.Kind = trash ? ButtonKind.Primary : ButtonKind.Secondary;
            btnDel.Text = trash ? "完全に削除" : "削除";
            tips.SetToolTip(btnPin, trash ? "選択中のメモを一覧に戻します" : "選択中のメモを一覧のいちばん上に固定 (Ctrl+P)");
            tips.SetToolTip(btnDel, trash ? "選択中のメモを完全に削除します (元に戻せません)" : "選択中のメモをゴミ箱へ (Ctrl+Delete)。ゴミ箱から 30 日以内なら戻せます");
        }

        // ---- その他メニュー ----

        public void BuildMoreMenu()
        {
            moreMenu.Items.Clear();
            bool trash = TrashView;
            var exp = new ToolStripMenuItem("このメモを .txt に書き出す…", null, (s, e) => ExportCurrent()) { Enabled = current != null };
            moreMenu.Items.Add(exp);
            moreMenu.Items.Add(new ToolStripMenuItem("すべてのメモを .txt でフォルダーに書き出す…", null, (s, e) => ExportAll()) { Enabled = data.Notes.Any(n => !n.InTrash) });
            moreMenu.Items.Add(new ToolStripMenuItem(".txt ファイルを読み込む…", null, (s, e) => ImportFiles()));
            moreMenu.Items.Add(new ToolStripSeparator());
            var size = new ToolStripMenuItem("文字の大きさ");
            for (int i = 0; i < MemoSettings.SizeLabels.Length; i++)
            {
                int k = i;
                size.DropDownItems.Add(new ToolStripMenuItem(MemoSettings.SizeLabels[i], null, (s, e) => SetFontSize(k)) { Checked = settings.SafeSize == i });
            }
            size.DropDown.Font = Theme.Body;
            moreMenu.Items.Add(size);
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(new ToolStripMenuItem(trash ? "すべてのメモを表示" : "ゴミ箱を見る", null, (s, e) => SelectView(trash ? null : "trash")));
            moreMenu.Items.Add(new ToolStripMenuItem("ゴミ箱を空にする…", null, (s, e) => EmptyTrash(true)) { Enabled = data.Notes.Any(n => n.InTrash) });
        }

        public void SetFontSize(int k)
        {
            settings.FontSize = k;
            DataStore.TrySave(MemoSettings.FileName, settings);
            ApplyFontSize();
            footer.StatusText = "文字の大きさ: " + MemoSettings.SizeLabels[settings.SafeSize];
        }

        void ApplyFontSize()
        {
            var old = editorFont;
            float scale = MemoSettings.SizeScale[settings.SafeSize];
            editorFont = scale == 1f ? null : new Font(Theme.Editor.FontFamily, Theme.Editor.Size * scale, Theme.Editor.Style, Theme.Editor.Unit);
            editor.Font = editorFont ?? Theme.Editor;
            if (old != null) old.Dispose();
        }
        public float EditorFontSize { get { return editor.Font.Size; } }

        void ExportCurrent()
        {
            if (current == null) return;
            var used = new List<string>();
            var p = FileKit.SaveText(this, NoteStore.ToText(current), TextKit.SafeFileName(current.Title, ".txt", used), "テキスト (*.txt)|*.txt");
            if (p != null) footer.StatusText = "書き出しました: " + p;
        }

        void ExportAll()
        {
            using (var d = new FolderBrowserDialog { Description = "メモを書き出すフォルダーを選んでください (1 件ずつ .txt になります)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int n = ExportAllTo(d.SelectedPath);
                    footer.StatusText = n + " 件のメモを書き出しました: " + d.SelectedPath;
                }
                catch (Exception ex) { Dialogs.Error(this, "書き出せませんでした。\n" + ex.Message); }
            }
        }

        /// <summary>ゴミ箱以外のメモを folder に 1 件ずつ .txt で書き出す (同じ名前のファイルは上書きしない)</summary>
        public int ExportAllTo(string folder)
        {
            Directory.CreateDirectory(folder);
            var used = new List<string>(Directory.GetFiles(folder).Select(Path.GetFileName));
            int n = 0;
            foreach (var note in data.Notes.Where(x => !x.InTrash).OrderByDescending(x => x.Updated, StringComparer.Ordinal))
            {
                var name = TextKit.SafeFileName(note.Title, ".txt", used);
                var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(NoteStore.ToText(note))).ToArray();
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
                n++;
            }
            return n;
        }

        void ImportFiles()
        {
            using (var d = new OpenFileDialog { Filter = "テキスト (*.txt;*.md)|*.txt;*.md|すべてのファイル (*.*)|*.*", Multiselect = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                int n = ImportFiles(d.FileNames);
                if (n > 0) footer.StatusText = n + " 件のメモを読み込みました";
            }
        }

        /// <summary>テキスト ファイルをメモとして取り込む (1 ファイル = 1 メモ。2 MB より大きいものは飛ばす)</summary>
        public int ImportFiles(IEnumerable<string> paths)
        {
            int n = 0;
            var skipped = new List<string>();
            Note last = null;
            foreach (var p in paths)
            {
                try
                {
                    var fi = new FileInfo(p);
                    if (!fi.Exists || fi.Length > 2 * 1024 * 1024) { skipped.Add(Path.GetFileName(p)); continue; }
                    var text = TextKit.Decode(File.ReadAllBytes(p));
                    if (string.IsNullOrWhiteSpace(text)) { skipped.Add(Path.GetFileName(p)); continue; }
                    last = NoteStore.FromText(text, fi.LastWriteTimeUtc);
                    data.Notes.Add(last);
                    n++;
                }
                catch (Exception ex) { DataStore.Log("import " + p + ": " + ex.Message); skipped.Add(Path.GetFileName(p)); }
            }
            if (n > 0)
            {
                dirty = true; SaveNow(false);
                SelectView(null);
                current = last;
                RefreshViews(); RefreshList();
            }
            if (skipped.Count > 0 && IsHandleCreated)
                Dialogs.Warn(this, "読み込めなかったファイルがあります (空・大きすぎる・開けない):\n" + string.Join("\n", skipped.Take(10)));
            return n;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: NewNote(); return true;
                case Keys.Control | Keys.S: saveTimer.Stop(); dirty = true; SaveNow(true); return true;
                case Keys.Control | Keys.F: searchBox.Focus(); searchBox.SelectAll(); return true;
                case Keys.Control | Keys.Delete: DeleteCurrent(); return true;
                case Keys.Control | Keys.P: if (TrashView) RestoreCurrent(); else TogglePin(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
