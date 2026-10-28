// Teal Pass (パスワード管理) - メイン画面 (左: 生成 / 中: 一覧 / 右: 詳細)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pass
{
    public class MainForm : Form
    {
        public const string SettingsFile = "settings.json";
        VaultFile vault = new VaultFile();
        PmSettings settings;
        bool vaultReady;
        readonly SecureClipboard clip = new SecureClipboard();
        readonly FooterBar footer = new FooterBar();
        readonly Timer uiTimer = new Timer();
        string lastAction = "";

        // 生成
        readonly TextBox genBox = new TextBox();
        readonly StrengthBar genStrength = new StrengthBar();
        readonly FlatSlider lenSlider = new FlatSlider();
        readonly Label lenValue = new Label();
        readonly ToggleSwitch tUpper = new ToggleSwitch("大文字 (A-Z)"), tLower = new ToggleSwitch("小文字 (a-z)"),
            tDigits = new ToggleSwitch("数字 (0-9)"), tSymbols = new ToggleSwitch("記号 (!@#$ など)"),
            tAmbig = new ToggleSwitch("紛らわしい文字を除く (0とO、1とl など)");
        // 一覧
        readonly TextBox search = new TextBox();
        readonly ListBox list = new ListBox();
        readonly CardPanel listCard = new CardPanel("保存したパスワード");
        /// <summary>1.8.0: 一覧の絞り込み (すべて / お気に入り / 要注意)</summary>
        public readonly ComboBox FilterBox = Lay.Combo("すべて", "★ お気に入り", "要注意");
        readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        HealthReport health = new HealthReport();
        // 詳細
        readonly Panel detail = new Panel();
        readonly Label emptyDetail = new Label();
        readonly Label dSite = new Label();
        readonly LinkLabel dUrl = new LinkLabel();
        readonly TextBox dUser = new TextBox(), dPass = new TextBox(), dMemo = new TextBox();
        readonly FlatButton btnReveal = new FlatButton("表示", ButtonKind.Secondary);
        readonly Label dUpdated = new Label();
        readonly StrengthBar dStrength = new StrengthBar();
        readonly Label dIssue = new Label();
        readonly FlatButton btnFav = new FlatButton("☆ お気に入り", ButtonKind.Secondary);
        readonly FlatButton btnHistory = new FlatButton("以前のパスワード", ButtonKind.Secondary);

        public HealthReport Health { get { return health; } }
        public ListBox List { get { return list; } }
        public ContextMenuStrip MoreMenu { get { return moreMenu; } }
        public string IssueText { get { return dIssue.Visible ? dIssue.Text : ""; } }

        public VaultFile Vault { get { return vault; } }

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(960, 600);
            KeyPreview = true;
            settings = DataStore.LoadQuiet(SettingsFile, () => new PmSettings());
            if (settings.Generator == null) settings.Generator = new PasswordOptions();
            settings.Generator.Normalize();
            clip.Seconds = Math.Max(5, Math.Min(300, settings.ClipboardClearSeconds));

            var header = HeaderBar.ForApp("生成・保管・メモ");
            var btnAdd = header.AddAction(new FlatButton("＋ 追加", ButtonKind.Primary)).FitWidth(96);
            btnAdd.Click += (s, e) => AddEntry(null);
            new ToolTip().SetToolTip(btnAdd, "パスワードを登録 (Ctrl+N)");
            var btnCheck = header.AddAction(new FlatButton("安全チェック", ButtonKind.Secondary)).FitWidth(110);
            btnCheck.Click += (s, e) => ShowHealth();
            new ToolTip().SetToolTip(btnCheck, "使い回し・弱い・1 年以上変えていないパスワードを調べます");
            var btnMore = header.AddAction(new FlatButton("その他 ▾", ButtonKind.Secondary)).FitWidth(90);
            moreMenu.Font = Theme.Body; moreMenu.ShowImageMargin = false;
            btnMore.Click += (s, e) => { BuildMoreMenu(); moreMenu.Show(btnMore, new Point(0, btnMore.Height)); };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(340)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(280)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var gen = BuildGenerator(); gen.Margin = new Padding(0, 0, Theme.S(10), 0);
            var lst = BuildList(); lst.Margin = new Padding(0, 0, Theme.S(10), 0);
            var det = BuildDetail(); det.Margin = new Padding(0);
            grid.Controls.Add(gen, 0, 0); grid.Controls.Add(lst, 1, 0); grid.Controls.Add(det, 2, 0);

            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(header);

            WindowMemory.Attach(this, 1100, 660);
            uiTimer.Interval = 1000;
            uiTimer.Tick += (s, e) => UpdateFooter();
            clip.Cleared += () => { lastAction = "クリップボードを消去しました"; UpdateFooter(); };
            KeyDown += OnKey;
            Load += (s, e) => { LoadVault(); Regenerate(); uiTimer.Start(); };
            FormClosing += (s, e) => SaveSettings();
            FormClosed += (s, e) => clip.Dispose();   // 常駐中に隠すだけのときは、コピーの自動消去を続ける
        }

        // ================= 生成 =================
        Control BuildGenerator()
        {
            var card = new CardPanel("パスワード生成") { Dock = DockStyle.Fill };
            genBox.ReadOnly = true; genBox.Font = Theme.MonoLarge; genBox.BackColor = Theme.Surface; genBox.ForeColor = Theme.Text;
            var genFrame = new InputFrame(genBox);
            var bCopy = new FlatButton("コピー", ButtonKind.Primary).FitWidth(80);
            var bRegen = new FlatButton("再生成", ButtonKind.Secondary).FitWidth(80);
            var bSave = new FlatButton("保存…", ButtonKind.Secondary).FitWidth(80);
            bCopy.Click += (s, e) => CopySecret(genBox.Text, "生成したパスワード");
            bRegen.Click += (s, e) => Regenerate();
            bSave.Click += (s, e) => AddEntry(genBox.Text);
            var tip = new ToolTip();
            tip.SetToolTip(bRegen, "新しく作り直す (F5)");
            tip.SetToolTip(bSave, "このパスワードでサイトを登録");
            var row = new FlowLayoutPanel { Height = Theme.S(Theme.ButtonHeight + 4), BackColor = Theme.Surface, WrapContents = false };
            foreach (var b in new Control[] { bCopy, bRegen, bSave }) { b.Margin = new Padding(0, 0, Theme.S(8), 0); row.Controls.Add(b); }

            var lenRow = new Panel { Height = Theme.Small.Height + Theme.S(10), BackColor = Theme.Surface };
            var lenCap = Ly.SectionLabel("長さ"); lenCap.Dock = DockStyle.Fill;
            lenValue.Dock = DockStyle.Right; lenValue.Width = Theme.S(80); lenValue.TextAlign = ContentAlignment.BottomRight; lenValue.Font = Theme.Bold; lenValue.ForeColor = Theme.Accent;
            lenRow.Controls.Add(lenCap); lenRow.Controls.Add(lenValue);
            lenSlider.Minimum = PasswordOptions.MinLength; lenSlider.Maximum = PasswordOptions.MaxLength; lenSlider.ShiftStep = 4; lenSlider.LargeChange = 4;
            lenSlider.Value = settings.Generator.Length;
            lenSlider.ValueChanged += (s, e) => { lenValue.Text = lenSlider.Value + " 文字"; if (built) Regenerate(); };
            lenValue.Text = lenSlider.Value + " 文字";

            var o = settings.Generator;
            tUpper.Checked = o.Upper; tLower.Checked = o.Lower; tDigits.Checked = o.Digits; tSymbols.Checked = o.Symbols; tAmbig.Checked = o.ExcludeAmbiguous;
            // AutoSize の FlowLayoutPanel を Dock=Top にすると、Windows のレイアウトが再入してスタックが溢れる
            var toggles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = false, BackColor = Theme.Surface, Padding = Theme.P(0, 4, 0, 0) };
            int togglesH = toggles.Padding.Vertical;
            foreach (var t in new[] { tUpper, tLower, tDigits, tSymbols, tAmbig })
            {
                var tt = t;
                tt.CheckedChanged += (s, e) =>
                {
                    if (!tUpper.Checked && !tLower.Checked && !tDigits.Checked && !tSymbols.Checked && tt != tAmbig) { tt.Checked = true; return; } // 最低 1 種類
                    if (built) Regenerate();
                };
                toggles.Controls.Add(tt);
                togglesH += tt.GetPreferredSize(Size.Empty).Height + tt.Margin.Vertical;
            }
            toggles.Height = togglesH;
            var hint = new Label { Text = "Shift を押しながらドラッグで 4 文字ずつ", Font = Theme.Small, ForeColor = Theme.Disabled, Height = Theme.Small.Height + Theme.S(4), UseMnemonic = false };
            Ly.Stack(card, genFrame, Ui.Spacer(8), genStrength, Ui.Spacer(4), row, Ui.Spacer(10), lenRow, lenSlider, hint, Ly.SectionLabel("使う文字"), toggles);
            built = true;
            return card;
        }
        bool built;

        public PasswordOptions CurrentOptions()
        {
            return new PasswordOptions
            {
                Length = lenSlider.Value, Upper = tUpper.Checked, Lower = tLower.Checked,
                Digits = tDigits.Checked, Symbols = tSymbols.Checked, ExcludeAmbiguous = tAmbig.Checked,
            }.Normalize();
        }

        public void Regenerate()
        {
            genBox.Text = PasswordGenerator.Generate(CurrentOptions());
            genStrength.SetPassword(genBox.Text);
        }

        void SaveSettings()
        {
            settings.Generator = CurrentOptions();
            DataStore.TrySave(SettingsFile, settings);
        }

        // ================= 一覧 =================
        Control BuildList()
        {
            listCard.Dock = DockStyle.Fill;
            Ui.SetCue(search, "検索 (サイト名・ID・メモ)  Ctrl+F");
            search.TextChanged += (s, e) => RefreshList(null);
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { search.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.Handled = e.SuppressKeyPress = true; }
            };
            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.BackColor = Theme.Surface;
            list.DrawMode = DrawMode.OwnerDrawFixed; list.IntegralHeight = false;
            list.ItemHeight = Math.Min(255, Theme.Title.Height + Theme.Small.Height + Theme.S(16));
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) => ShowDetail(list.SelectedItem as VaultEntry);
            list.DoubleClick += (s, e) => EditSelected();
            listCard.Controls.Add(list);
            listCard.Controls.Add(Ui.Spacer(8));
            listCard.Controls.Add(new InputFrame(search) { Dock = DockStyle.Top });
            listCard.Controls.Add(Ui.Spacer(6));
            FilterBox.Dock = DockStyle.Top;
            FilterBox.SelectedIndexChanged += (s, e) => RefreshList(null);
            listCard.Controls.Add(FilterBox);
            return listCard;
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var en = (VaultEntry)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);
            if (sel)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 3, e.Bounds.Height - 3);
                using (var path = Theme.RoundRect(r, Theme.S(Theme.Radius))) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, path);
            }
            // 頭文字のバッジ
            int d = Theme.S(30), x = e.Bounds.X + Theme.S(8), y = e.Bounds.Y + (e.Bounds.Height - d) / 2;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.RoundRect(new RectangleF(x, y, d, d), Theme.S(7)))
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Blend(Theme.Accent, Color.White, 0.82f))) g.FillPath(b, path);
            string initial = string.IsNullOrEmpty(en.Site) ? "?" : en.Site.Substring(0, 1).ToUpperInvariant();
            TextRenderer.DrawText(g, initial, Theme.Bold, new Rectangle(x, y, d, d), sel ? Color.White : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int tx = x + d + Theme.S(10), tw = e.Bounds.Right - tx - Theme.S(6);
            int top = e.Bounds.Y + (e.Bounds.Height - Theme.Title.Height - Theme.Small.Height - Theme.S(2)) / 2;
            var f = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var iss = health.Of(en);
            if (iss != PassIssue.None)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int left = Draw.Chip(g, "要注意", Theme.Blend(Theme.Warning, Color.Black, 0.25f), Theme.Blend(Theme.Warning, Color.White, 0.85f), e.Bounds.Right - Theme.S(8), top + Theme.Title.Height / 2, true);
                tw = left - Theme.S(6) - tx;
            }
            if (en.Favorite)
            {
                int sw = TextRenderer.MeasureText(g, "★", Theme.Title, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(3);
                TextRenderer.DrawText(g, "★", Theme.Title, new Rectangle(tx, top, sw, Theme.Title.Height), Theme.Accent, f);
                tx += sw; tw -= sw;
            }
            TextRenderer.DrawText(g, en.Site, Theme.Title, new Rectangle(tx, top, tw, Theme.Title.Height), Theme.Text, f);
            TextRenderer.DrawText(g, string.IsNullOrEmpty(en.UserId) ? "(ID なし)" : en.UserId, Theme.Small, new Rectangle(tx, top + Theme.Title.Height + Theme.S(2), tw, Theme.Small.Height + 2), Theme.SubText, f);
        }

        void RefreshList(VaultEntry select)
        {
            var cur = select ?? list.SelectedItem as VaultEntry;
            var q = search.Text.Trim();
            health = PassTools.Check(vault.Entries, DateTime.Now);
            int fi = FilterBox.SelectedIndex;
            var items = vault.Entries.Where(x => x.Matches(q))
                .Where(x => fi == 1 ? x.Favorite : fi == 2 ? health.Of(x) != PassIssue.None : true)
                .OrderBy(x => x.Favorite ? 0 : 1).ThenBy(x => x.Site, StringComparer.CurrentCultureIgnoreCase).ToArray();
            UpdateFilterLabels();
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items);
            list.EndUpdate();
            if (cur != null && items.Contains(cur)) list.SelectedItem = cur;
            else if (items.Length > 0) list.SelectedIndex = 0;
            else ShowDetail(null);
            listCard.Title = q.Length > 0 || fi > 0 ? string.Format("保存したパスワード  {0} / {1}", items.Length, vault.Entries.Count) : string.Format("保存したパスワード  {0} 件", vault.Entries.Count);
            listCard.Invalidate();
        }

        // ================= 詳細 =================
        Control BuildDetail()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(20, 16, 20, 16) };
            emptyDetail.Dock = DockStyle.Fill; emptyDetail.TextAlign = ContentAlignment.MiddleCenter; emptyDetail.ForeColor = Theme.SubText; emptyDetail.Font = Theme.Body;
            emptyDetail.Text = "一覧から選ぶと、ここに詳細が表示されます。\n\n新しく登録するには「＋ 追加」(Ctrl+N)";
            detail.Dock = DockStyle.Fill; detail.BackColor = Theme.Surface; detail.Visible = false;

            dSite.Font = Theme.Header; dSite.ForeColor = Theme.Text; dSite.AutoEllipsis = true; dSite.UseMnemonic = false; dSite.Height = Theme.Header.Height + Theme.S(4);
            dUrl.Font = Theme.Body; dUrl.LinkColor = Theme.Accent; dUrl.ActiveLinkColor = Theme.AccentPressed; dUrl.AutoEllipsis = true; dUrl.UseMnemonic = false; dUrl.Height = Theme.Body.Height + Theme.S(6);
            dUrl.LinkBehavior = LinkBehavior.HoverUnderline;
            dUrl.LinkClicked += (s, e) => OpenUrl();

            foreach (var tb in new[] { dUser, dPass }) { tb.ReadOnly = true; tb.BackColor = Theme.Surface; tb.ForeColor = Theme.Text; }
            dPass.Font = Theme.Mono; dPass.UseSystemPasswordChar = true;
            var bCopyUser = new FlatButton("コピー", ButtonKind.Secondary).FitWidth(72);
            var bCopyPass = new FlatButton("コピー", ButtonKind.Primary).FitWidth(72);
            btnReveal.FitWidth(64);
            bCopyUser.Click += (s, e) => { var en = Selected; if (en != null) CopyPlain(en.UserId, "ユーザー名 / ID"); };
            bCopyPass.Click += (s, e) => { var en = Selected; if (en != null) CopySecret(en.Password, "パスワード"); };
            btnReveal.Click += (s, e) => SetReveal(dPass.UseSystemPasswordChar);
            var tip = new ToolTip();
            tip.SetToolTip(bCopyUser, "ID をコピー (Ctrl+B)");
            tip.SetToolTip(bCopyPass, "パスワードをコピー (Ctrl+C)。" + clip.Seconds + " 秒後に自動で消去します");

            dMemo.ReadOnly = true; dMemo.Multiline = true; dMemo.ScrollBars = ScrollBars.Vertical; dMemo.BackColor = Theme.Surface; dMemo.WordWrap = true;
            var memoFrame = new InputFrame(dMemo, true) { Dock = DockStyle.Fill };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 12, 0, 0), BackColor = Theme.Surface };
            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(80);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(80);
            btnFav.FitWidth(120); btnHistory.FitWidth(150);
            btnFav.Click += (s, e) => ToggleFavorite();
            btnHistory.Click += (s, e) => ShowHistory();
            dIssue.Font = Theme.Small; dIssue.ForeColor = Theme.Warning; dIssue.AutoSize = false; dIssue.Height = Theme.Small.Height + Theme.S(6);
            dIssue.TextAlign = ContentAlignment.MiddleLeft; dIssue.UseMnemonic = false; dIssue.AutoEllipsis = true;
            var bottom2 = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 12, 0, 0), BackColor = Theme.Surface };
            btnFav.Dock = DockStyle.Left; btnHistory.Dock = DockStyle.Left;
            bottom2.Controls.Add(btnHistory);
            bottom2.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.S(8) });
            bottom2.Controls.Add(btnFav);
            btnFav.BringToFront();
            bEdit.Click += (s, e) => EditSelected();
            bDel.Click += (s, e) => DeleteSelected();
            dUpdated.Dock = DockStyle.Fill; dUpdated.Font = Theme.Small; dUpdated.ForeColor = Theme.SubText; dUpdated.TextAlign = ContentAlignment.MiddleLeft;
            bottom.Controls.Add(dUpdated);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(8) });
            bEdit.Dock = DockStyle.Right; bottom.Controls.Add(bEdit);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(8) });
            bDel.Dock = DockStyle.Right; bottom.Controls.Add(bDel);

            detail.Controls.Add(memoFrame);
            detail.Controls.Add(bottom2);
            detail.Controls.Add(bottom);
            Ly.Stack(detail, dSite, dUrl, Ui.Spacer(6),
                Ly.SectionLabel("ユーザー名 / ID"), Ly.FieldRow(new InputFrame(dUser), bCopyUser),
                Ly.SectionLabel("パスワード"), Ly.FieldRow(new InputFrame(dPass), btnReveal, bCopyPass),
                Ui.Spacer(6), dStrength, dIssue,
                Ly.SectionLabel("メモ"));
            card.Controls.Add(detail);
            card.Controls.Add(emptyDetail);
            return card;
        }

        VaultEntry Selected { get { return list.SelectedItem as VaultEntry; } }

        void ShowDetail(VaultEntry en)
        {
            detail.Visible = en != null;
            emptyDetail.Visible = en == null;
            SetReveal(false);
            if (en == null) return;
            dSite.Text = en.Site;
            dUrl.Text = string.IsNullOrEmpty(en.Url) ? "(URL なし)" : en.Url;
            dUrl.Enabled = !string.IsNullOrEmpty(en.Url);
            dUser.Text = en.UserId;
            dPass.Text = en.Password;
            dStrength.SetPassword(en.Password);
            dMemo.Text = (en.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            dUpdated.Text = "更新: " + en.Updated.ToString("yyyy/MM/dd HH:mm") + "   作成: " + en.Created.ToString("yyyy/MM/dd");
            btnFav.Text = en.Favorite ? "★ お気に入り" : "☆ お気に入り";
            btnHistory.Text = "以前のパスワード (" + en.HistoryCount + ")";
            btnHistory.Enabled = en.HistoryCount > 0;
            var iss = health.Of(en);
            dIssue.Visible = iss != PassIssue.None;
            if (iss != PassIssue.None)
            {
                var txt = "⚠ " + PassTools.IssueText(iss);
                var same = PassTools.SamePassword(vault.Entries, en);
                if (same.Count > 0) txt += " (" + string.Join("、", same.Take(3).Select(x => x.Site)) + (same.Count > 3 ? " ほか" : "") + ")";
                dIssue.Text = txt;
            }
        }

        void SetReveal(bool show)
        {
            dPass.UseSystemPasswordChar = !show;
            btnReveal.Text = show ? "隠す" : "表示";
        }

        void OpenUrl()
        {
            var en = Selected;
            if (en == null || string.IsNullOrEmpty(en.Url)) return;
            var u = en.Url.Trim();
            if (!u.Contains("://")) u = "https://" + u;
            Uri uri;
            if (Uri.TryCreate(u, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) AppHost.OpenUrl(uri.AbsoluteUri);
            else Dialogs.Warn(this, "この URL は開けません: " + en.Url);
        }

        // ================= 操作 =================
        void OnKey(object sender, KeyEventArgs e)
        {
            bool inText = ActiveControl is TextBoxBase || (ActiveControl is InputFrame);
            if (e.Control && e.KeyCode == Keys.N) { AddEntry(null); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { Regenerate(); e.Handled = true; }
            else if (list.Focused && e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (list.Focused && e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = true; }
            else if (list.Focused && e.Control && e.KeyCode == Keys.C) { var en = Selected; if (en != null) CopySecret(en.Password, "パスワード"); e.Handled = true; }
            else if (!inText && e.Control && e.KeyCode == Keys.B) { var en = Selected; if (en != null) CopyPlain(en.UserId, "ユーザー名 / ID"); e.Handled = true; }
        }

        void AddEntry(string password)
        {
            if (!vaultReady) return;
            var en = new VaultEntry { Password = password ?? PasswordGenerator.Generate(CurrentOptions()) };
            using (var f = new EntryForm(en, true, () => PasswordGenerator.Generate(CurrentOptions())))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            en.Created = en.Updated = DateTime.Now;
            en.PasswordChanged = en.Created;
            vault.Entries.Add(en);
            if (Persist()) { search.Text = ""; RefreshList(en); lastAction = "「" + en.Site + "」を追加しました"; UpdateFooter(); }
        }

        void EditSelected()
        {
            var en = Selected;
            if (en == null) return;
            var copy = en.Clone();
            using (var f = new EntryForm(copy, false, () => PasswordGenerator.Generate(CurrentOptions())))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            PassTools.ApplyEdit(en, copy, DateTime.Now);
            var idx = vault.Entries.IndexOf(en);
            vault.Entries[idx] = copy;
            if (Persist()) { RefreshList(copy); lastAction = "「" + copy.Site + "」を更新しました"; UpdateFooter(); }
            else vault.Entries[idx] = en;
        }

        void DeleteSelected()
        {
            var en = Selected;
            if (en == null) return;
            if (!Dialogs.Confirm(this, "「" + en.Site + "」を削除しますか？\n\nこの操作は元に戻せません。", "削除の確認")) return;
            int idx = list.SelectedIndex;
            vault.Entries.Remove(en);
            if (!Persist()) { vault.Entries.Add(en); return; }
            RefreshList(null);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
            lastAction = "「" + en.Site + "」を削除しました"; UpdateFooter();
        }

        bool Persist()
        {
            try { VaultStore.Save(vault); return true; }
            catch (Exception ex)
            {
                DataStore.Log("vault save: " + ex);
                Dialogs.Error(this, "保存できませんでした。\n\n" + ex.Message);
                return false;
            }
        }

        void LoadVault()
        {
            try
            {
                vault = VaultStore.Load();
                vaultReady = true;
            }
            catch (Exception ex)
            {
                DataStore.Log("vault load: " + ex);
                bool crypto = ex is CryptographicException;
                var msg = "保存データを読み込めませんでした。\n" +
                    (crypto ? "別の Windows ユーザーやパソコンで作られたデータは開けません。\n" : "ファイルが壊れている可能性があります。\n") +
                    "\n「はい」: このファイルを別名で残し (削除はしません)、空の状態で始めます。\n「いいえ」: アプリを終了します。\n\n" + DataStore.PathOf(VaultStore.FileName);
                if (MessageBox.Show(this, msg, AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                { Close(); return; }
                try { VaultStore.SetAsideBroken(); } catch (Exception ex2) { Dialogs.Error(this, "ファイルを退避できませんでした。\n" + ex2.Message); Close(); return; }
                vault = new VaultFile();
                vaultReady = true;
            }
            RefreshList(null);
            lastAction = vault.Entries.Count + " 件を読み込みました";
            UpdateFooter();
        }

        public void CopySecret(string text, string what)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { clip.Copy(text, true); lastAction = what + "をコピーしました"; }
            catch (Exception ex) { lastAction = "コピーできませんでした: " + ex.Message; }
            UpdateFooter();
        }

        void CopyPlain(string text, string what)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { clip.Copy(text, false); lastAction = what + "をコピーしました"; }
            catch (Exception ex) { lastAction = "コピーできませんでした: " + ex.Message; }
            UpdateFooter();
        }

        // ================= 1.8.0 =================
        void UpdateFilterLabels()
        {
            var labels = new[] { "すべて (" + vault.Entries.Count + ")", "★ お気に入り (" + vault.Entries.Count(x => x.Favorite) + ")", "要注意 (" + health.Total + ")" };
            bool same = true;
            for (int i = 0; i < 3; i++) if ((string)FilterBox.Items[i] != labels[i]) same = false;
            if (same) return;
            for (int i = 0; i < 3; i++) FilterBox.Items[i] = labels[i];
        }

        public void ToggleFavorite()
        {
            var en = Selected;
            if (en == null) return;
            en.Favorite = !en.Favorite;
            if (!Persist()) { en.Favorite = !en.Favorite; return; }
            RefreshList(en);
            lastAction = en.Favorite ? "「" + en.Site + "」をお気に入りにしました" : "「" + en.Site + "」をお気に入りから外しました";
            UpdateFooter();
        }

        public void SelectEntry(VaultEntry en)
        {
            if (en == null) return;
            if (!list.Items.Contains(en)) { search.Text = ""; FilterBox.SelectedIndex = 0; }
            if (list.Items.Contains(en)) list.SelectedItem = en;
        }

        void BuildMoreMenu()
        {
            moreMenu.Items.Clear();
            moreMenu.Items.Add(new ToolStripMenuItem("ブラウザーの CSV から読み込む…", null, (s, e) => ImportCsv()) { Enabled = vaultReady });
            moreMenu.Items.Add(new ToolStripMenuItem("CSV に書き出す…", null, (s, e) => ExportCsv()) { Enabled = vaultReady && vault.Entries.Count > 0 });
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(new ToolStripMenuItem("安全チェック…", null, (s, e) => ShowHealth()));
            moreMenu.Items.Add(new ToolStripMenuItem("データ フォルダーを開く", null, (s, e) => AppHost.OpenFolder(DataStore.AppDir)));
        }

        /// <summary>CSV の文字列を取り込む (テストからも呼ぶ)。追加した件数</summary>
        public ImportResult ImportCsvText(string text)
        {
            var r = PassTools.ImportCsv(text, vault.Entries, DateTime.Now);
            if (r.Error != null || r.Added.Count == 0) return r;
            vault.Entries.AddRange(r.Added);
            if (!Persist()) { foreach (var a in r.Added) vault.Entries.Remove(a); r.Error = "保存できませんでした。"; r.Added.Clear(); return r; }
            search.Text = ""; FilterBox.SelectedIndex = 0;
            RefreshList(r.Added[0]);
            lastAction = r.Added.Count + " 件を読み込みました";
            UpdateFooter();
            return r;
        }

        void ImportCsv()
        {
            if (!vaultReady) return;
            Dialogs.Info(this, "Chrome・Edge・Firefox などで書き出したパスワードの CSV を読み込みます。\n\n" +
                "・Chrome / Edge: 設定 → パスワード マネージャー → 設定 → パスワードをエクスポート\n" +
                "・Firefox: パスワード → … → ファイルにエクスポート\n\n" +
                "同じサイト・ID・パスワードのものはとばします。\n読み込んだあとは、CSV ファイルを必ず削除してください (暗号化されていません)。");
            using (var d = new OpenFileDialog { Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string text;
                try
                {
                    var fi = new System.IO.FileInfo(d.FileName);
                    if (fi.Length > 20 * 1024 * 1024) { Dialogs.Warn(this, "ファイルが大きすぎます (20 MB まで)。"); return; }
                    text = TextKit.Decode(System.IO.File.ReadAllBytes(d.FileName));
                }
                catch (Exception ex) { Dialogs.Error(this, "読み込めませんでした。\n" + ex.Message); return; }
                var r = ImportCsvText(text);
                if (r.Error != null) { Dialogs.Error(this, r.Error); return; }
                var msg = r.Added.Count + " 件を追加しました。";
                if (r.Duplicates > 0) msg += "\n登録済みのため " + r.Duplicates + " 件をとばしました。";
                if (r.Skipped > 0) msg += "\nサイト名やパスワードがない " + r.Skipped + " 行をとばしました。";
                msg += "\n\n元の CSV ファイルは削除しておくと安全です。";
                Dialogs.Info(this, msg);
            }
        }

        void ExportCsv()
        {
            if (!vaultReady || vault.Entries.Count == 0) return;
            if (!Dialogs.Confirm(this, "すべてのパスワードを、暗号化されていない CSV ファイルに書き出します。\n" +
                "ファイルを開ける人は誰でもパスワードを読めます。\n\n" +
                "ほかのパスワード管理やブラウザーへ移すときだけ使い、終わったら必ず削除してください。\n書き出しますか?", "CSV に書き出す")) return;
            var path = FileKit.SaveText(this, PassTools.ToCsv(vault.Entries), "TealPass-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", "CSV ファイル (*.csv)|*.csv");
            if (path != null) { lastAction = vault.Entries.Count + " 件を CSV に書き出しました"; UpdateFooter(); }
        }

        void ShowHealth()
        {
            if (!vaultReady) return;
            health = PassTools.Check(vault.Entries, DateTime.Now);
            using (var f = new HealthForm(health, vault.Entries))
            {
                f.ShowDialog(this);
                if (f.Chosen != null) { FilterBox.SelectedIndex = 0; search.Text = ""; RefreshList(f.Chosen); SelectEntry(f.Chosen); }
            }
        }

        void ShowHistory()
        {
            var en = Selected;
            if (en == null || en.HistoryCount == 0) return;
            using (var f = new HistoryForm(en, (pw) => CopySecret(pw, "以前のパスワード")))
                f.ShowDialog(this);
        }

        void UpdateFooter()
        {
            var rem = clip.RemainingSeconds;
            footer.StatusText = rem > 0 ? lastAction + "  ・  " + rem + " 秒後にクリップボードから消去します" : lastAction;
        }
    }
}
