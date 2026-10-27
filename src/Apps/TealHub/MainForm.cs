// Teal Hub (アプリ管理) - メイン画面 (アプリごとのカード一覧・スタートアップ・ショートカット・アップデート)
//  - 通知領域に常駐する版 (HubTray) では ×で閉じても隠れるだけ (HideOnClose)。終了は通知領域のメニュー
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public enum AppStatus { Stopped, Running, Busy, Missing }

    /// <summary>状態を表す小さなピル (● 実行中 など)</summary>
    public class StatusPill : Control
    {
        AppStatus status = AppStatus.Stopped;
        string busyText = "処理中…";
        public StatusPill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Font = Theme.Small;
            Size = new Size(Theme.S(122), Theme.S(24));
        }
        public AppStatus Status { get { return status; } }
        public void Set(AppStatus s, string busy = null) { status = s; if (busy != null) busyText = busy; Invalidate(); }
        public string Label
        {
            get
            {
                switch (status)
                {
                    case AppStatus.Running: return "実行中";
                    case AppStatus.Busy: return busyText;
                    case AppStatus.Missing: return "見つかりません";
                    default: return "停止中";
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg, dot;
            switch (status)
            {
                case AppStatus.Running: bg = Theme.SuccessSoft; fg = Theme.Success; dot = Theme.Success; break;
                case AppStatus.Busy: bg = Theme.AccentSoft; fg = Theme.Accent; dot = Theme.Accent; break;
                case AppStatus.Missing: bg = Theme.DangerSoft; fg = Theme.Danger; dot = Theme.Danger; break;
                default: bg = Theme.SurfaceAlt; fg = Theme.SubText; dot = Theme.Disabled; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, (Height - 1) / 2f)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            int d = Theme.S(7);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, Theme.S(10), (Height - d) / 2f, d, d);
            TextRenderer.DrawText(g, Label, Font, new Rectangle(Theme.S(21), 0, Width - Theme.S(25), Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>1 アプリぶんのカード (名前・説明・設定の要約 / 状態・設定・起動・終了)</summary>
    public class AppCard : CardPanel
    {
        public readonly AppEntry App;
        public List<int> Pids = new List<int>();
        public bool Busy;
        readonly PictureBox icon = new PictureBox();
        readonly Label name = new Label();
        readonly Label version = new Label();
        readonly Label role = new Label();
        readonly Label desc = new Label();
        /// <summary>「ショートカット: Alt + V ・ 常駐: オン ・ スタートアップ: オフ」</summary>
        readonly Label summary = new Label();
        public readonly StatusPill Pill = new StatusPill();
        public readonly FlatButton BtnSettings = MainForm.GearButton();
        public readonly FlatButton BtnStart = new FlatButton("起動", ButtonKind.Primary);
        public readonly FlatButton BtnStop = new FlatButton("終了", ButtonKind.Secondary);
        readonly ToolTip tip = new ToolTip();
        public string SummaryText { get { return summary.Text; } }
        public Color SummaryColor { get { return summary.ForeColor; } }

        public AppCard(AppEntry app)
        {
            App = app;
            Height = Theme.S(100);
            Padding = Theme.P(0);
            icon.Size = new Size(Theme.S(44), Theme.S(44));
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.BackColor = Theme.Surface;
            icon.Image = LoadIcon(app, Theme.S(44));
            name.Text = app.Name; name.Font = Theme.Title; name.ForeColor = Theme.Text; name.AutoSize = true; name.UseMnemonic = false; name.BackColor = Theme.Surface;
            role.Text = app.Role ?? ""; role.Font = Theme.Small; role.ForeColor = Theme.Accent; role.AutoSize = true; role.UseMnemonic = false; role.BackColor = Theme.Surface; role.Visible = role.Text.Length > 0;
            version.Text = string.IsNullOrEmpty(app.Version) ? "" : "v" + app.Version; version.Font = Theme.Small; version.ForeColor = Theme.SubText; version.AutoSize = true; version.BackColor = Theme.Surface;
            desc.Text = app.Description ?? ""; desc.Font = Theme.Small; desc.ForeColor = Theme.SubText; desc.AutoSize = false; desc.AutoEllipsis = true; desc.UseMnemonic = false; desc.BackColor = Theme.Surface;
            summary.Font = Theme.Small; summary.ForeColor = Theme.SubText; summary.AutoSize = false; summary.AutoEllipsis = true; summary.UseMnemonic = false; summary.BackColor = Theme.Surface;
            BtnStart.FitWidth(76); BtnStop.FitWidth(76);
            Controls.AddRange(new Control[] { icon, name, role, version, desc, summary, Pill, BtnSettings, BtnStart, BtnStop });
            tip.SetToolTip(icon, app.ExePath); tip.SetToolTip(name, app.ExePath); tip.SetToolTip(desc, app.Description ?? "");
            tip.SetToolTip(BtnSettings, app.Name + " のショートカット・常駐・スタートアップを設定します");
        }

        /// <summary>設定の要約を表示する (problem: 色)</summary>
        public void SetSummary(string text, Color color)
        {
            if (summary.Text == text && summary.ForeColor == color) return;
            summary.Text = text; summary.ForeColor = color;
            tip.SetToolTip(summary, text + "\n(「設定」で変更できます)");
        }

        public static Image LoadIcon(AppEntry app, int px)
        {
            Bitmap b = null;
            if (app.IconPath != null && File.Exists(app.IconPath)) b = Theme.BitmapFromIconFile(app.IconPath, px);
            if (b == null && File.Exists(app.ExePath) && AppInfo.IsWindows) b = Theme.BitmapFromIconFile(app.ExePath, px);
            return b;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(16), gap = Theme.S(8);
            icon.Location = new Point(pad, (Height - icon.Height) / 2);
            int right = Width - pad;
            BtnStop.Location = new Point(right - BtnStop.Width, (Height - BtnStop.Height) / 2); right = BtnStop.Left - gap;
            BtnStart.Location = new Point(right - BtnStart.Width, (Height - BtnStart.Height) / 2); right = BtnStart.Left - gap;
            BtnSettings.Location = new Point(right - BtnSettings.Width, (Height - BtnSettings.Height) / 2); right = BtnSettings.Left - Theme.S(12);
            Pill.Location = new Point(right - Pill.Width, (Height - Pill.Height) / 2); right = Pill.Left - Theme.S(12);
            int x = icon.Right + Theme.S(14);
            int line = desc.Font.Height + 2;
            int top = (Height - (name.PreferredHeight + Theme.S(4) + line * 2 + Theme.S(2))) / 2;
            name.Location = new Point(x, top);
            int after = name.Right + Theme.S(8);
            if (role.Visible) { role.Location = new Point(after, top + name.PreferredHeight - role.PreferredHeight - 1); after = role.Right + Theme.S(6); }
            version.Location = new Point(after, top + name.PreferredHeight - version.PreferredHeight - 1);
            desc.Bounds = new Rectangle(x, name.Bottom + Theme.S(4), Math.Max(10, right - x), line);
            summary.Bounds = new Rectangle(x, desc.Bottom + Theme.S(2), Math.Max(10, right - x), line);
        }

        public void Apply(AppStatus s, string busyText = null)
        {
            Pill.Set(s, busyText);
            bool running = s == AppStatus.Running;
            BtnStart.Text = running ? "表示" : "起動";
            BtnStart.Kind = running ? ButtonKind.Secondary : ButtonKind.Primary;
            BtnStart.Enabled = s == AppStatus.Running || s == AppStatus.Stopped;
            BtnStop.Enabled = running;
            BtnStop.Kind = running ? ButtonKind.Danger : ButtonKind.Secondary;
        }
    }

    /// <summary>お知らせの帯 (文 + ボタン)</summary>
    public class NoticeBar : Panel
    {
        readonly Label text = new Label();
        public readonly FlatButton Action;
        public readonly FlatButton Dismiss;
        public readonly FlatButton Extra;
        readonly bool info;
        public string Message { get { return text.Text; } }
        public NoticeBar(string message, string action, bool dismissable, string extra = null, bool info = false)
        {
            this.info = info;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Background;
            Height = Theme.S(52);
            text.Text = message; text.Font = Theme.Small; text.ForeColor = Theme.Text; text.AutoEllipsis = true; text.UseMnemonic = false;
            text.BackColor = info ? Theme.AccentSoft : Color.FromArgb(0xFF, 0xF6, 0xE5); text.TextAlign = ContentAlignment.MiddleLeft;
            Action = new FlatButton(action, ButtonKind.Primary).FitWidth(96);
            Controls.Add(text); Controls.Add(Action);
            if (extra != null) { Extra = new FlatButton(extra, ButtonKind.Secondary).FitWidth(72); Controls.Add(Extra); }
            if (dismissable) { Dismiss = new FlatButton("あとで", ButtonKind.Ghost).FitWidth(56); Controls.Add(Dismiss); }
            new ToolTip().SetToolTip(text, message);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Action == null) return;   // コンストラクターの途中
            int pad = Theme.S(12), right = Width - pad;
            if (Dismiss != null) { Dismiss.Location = new Point(right - Dismiss.Width, (Height - Dismiss.Height) / 2); right = Dismiss.Left - Theme.S(6); }
            Action.Location = new Point(right - Action.Width, (Height - Action.Height) / 2); right = Action.Left - Theme.S(6);
            if (Extra != null) { Extra.Location = new Point(right - Extra.Width, (Height - Extra.Height) / 2); right = Extra.Left - Theme.S(6); }
            right -= Theme.S(4);
            text.Bounds = new Rectangle(Theme.S(30), 1, Math.Max(10, right - Theme.S(30)), Height - 2);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bg = info ? Theme.AccentSoft : Color.FromArgb(0xFF, 0xF6, 0xE5);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(6)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(info ? Theme.AccentLight : Color.FromArgb(0xF0, 0xC8, 0x80))) g.DrawPath(p, path);
            }
            int d = Theme.S(8);
            using (var b = new SolidBrush(info ? Theme.Accent : Theme.Warning)) g.FillEllipse(b, Theme.S(13), (Height - d) / 2f, d, d);
        }
    }

    /// <summary>ヘッダーの下の帯: 「Teal Hub」 設定の要約 … 設定 再読み込み</summary>
    public class SelfBar : Panel
    {
        readonly Label lbl = new Label(), summary = new Label();
        public readonly FlatButton BtnSettings = MainForm.GearButton();
        public readonly FlatButton BtnReload = new FlatButton("再読み込み", ButtonKind.Ghost);
        public string SummaryText { get { return summary.Text; } }
        public SelfBar()
        {
            Dock = DockStyle.Top; Height = Theme.S(44); BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(16), 0, Theme.S(10), 1);
            lbl.Text = "Teal Hub"; lbl.Font = Theme.Bold; lbl.ForeColor = Theme.Text; lbl.AutoSize = true; lbl.UseMnemonic = false; lbl.BackColor = Theme.Surface;
            summary.Font = Theme.Small; summary.ForeColor = Theme.SubText; summary.AutoSize = false; summary.AutoEllipsis = true; summary.UseMnemonic = false; summary.BackColor = Theme.Surface;
            BtnReload.FitWidth(72);
            Controls.AddRange(new Control[] { lbl, summary, BtnSettings, BtnReload });
        }
        public void SetSummary(string text, Color color) { if (summary.Text != text) summary.Text = text; summary.ForeColor = color; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (BtnReload == null) return;
            int h = Height - 1, gap = Theme.S(8);
            BtnReload.Location = new Point(Width - Padding.Right - BtnReload.Width, (h - BtnReload.Height) / 2);
            BtnSettings.Location = new Point(BtnReload.Left - gap - BtnSettings.Width, (h - BtnSettings.Height) / 2);
            lbl.Location = new Point(Padding.Left, (h - lbl.PreferredHeight) / 2);
            int x = lbl.Left + lbl.PreferredWidth + Theme.S(14);
            int sh = summary.Font.Height + 2;
            summary.Bounds = new Rectangle(x, (h - sh) / 2, Math.Max(10, BtnSettings.Left - Theme.S(12) - x), sh);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    public class MainForm : Form
    {
        const int StopTimeoutMs = 5000;
        /// <summary>ショートカットでの起動を、続けて押しても 1 回にする間隔</summary>
        public const int LaunchDebounceMs = 2000;
        readonly ProcessControl ctl = new ProcessControl();
        readonly FlowLayoutPanel listPanel = new FlowLayoutPanel();
        readonly FooterBar footer = new FooterBar();
        readonly Timer timer = new Timer();
        readonly Label empty = new Label();
        readonly FlatButton btnStopAll;
        /// <summary>登録先の違い・古い自動起動などのお知らせ</summary>
        readonly FlowLayoutPanel notices = new FlowLayoutPanel();
        readonly ToolTip tips = new ToolTip();
        StartupState selfState = new StartupState();
        readonly Dictionary<string, StartupState> appStartup = new Dictionary<string, StartupState>(StringComparer.OrdinalIgnoreCase);
        List<AppEntry> allApps = new List<AppEntry>();   // Teal Hub 自身も含む (古い自動起動の判定用)
        List<LegacyStartupItem> legacy = new List<LegacyStartupItem>();
        readonly HashSet<string> dismissed = new HashSet<string>();
        readonly Dictionary<string, DateTime> lastLaunch = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        string noticesKey;
        DateTime lastLegacyScan = DateTime.MinValue;
        List<AppEntry> apps = new List<AppEntry>();
        readonly List<AppCard> cards = new List<AppCard>();
        bool refreshing;
        readonly HubShortcuts shortcuts;
        readonly HeaderBar header;
        readonly SelfBar selfBar;
        readonly AppSettingsPage page;
        readonly FlatButton btnUpdate;
        readonly ContextMenuStrip updateMenu = new ContextMenuStrip();
        bool loaded, allowClose;
        string suiteVersion;
        HubUpdates updates;
        string updateDismissed;
        const string UpdateButtonText = "アップデート ▾";

        /// <summary>×で閉じたときに終了せず隠す (通知領域に常駐する版)</summary>
        public bool HideOnClose;
        /// <summary>×で隠れたとき (HubTray が案内を出す)</summary>
        public event Action HideRequested;
        /// <summary>アップデート ファイルが選ばれた・ドロップされたとき (null なら UpdaterLauncher で開く)</summary>
        public Action<string> UpdateFileChosen;
        /// <summary>テスト用: 今の時刻 (連打の判定)</summary>
        public Func<DateTime> Now = () => DateTime.Now;
        public HubShortcuts Shortcuts { get { return shortcuts; } }
        public string SuiteVersion { get { return suiteVersion; } }
        public string UpdateButtonLabel { get { return btnUpdate.Text; } }
        public ContextMenuStrip UpdateMenu { get { return updateMenu; } }
        /// <summary>お知らせの帯 (テスト用)</summary>
        public IEnumerable<NoticeBar> NoticeBars { get { return notices.Controls.OfType<NoticeBar>(); } }
        public SelfBar SelfBar_ { get { return selfBar; } }
        public HeaderBar Header { get { return header; } }
        /// <summary>設定ページ (表示中かどうかは IsSettingsShown)</summary>
        public AppSettingsPage SettingsPage { get { return page; } }
        public bool IsSettingsShown { get { return settingsShown; } }
        bool settingsShown;

        /// <summary>オンラインのアップデート確認 (HubTray が設定する。null なら出さない)</summary>
        public HubUpdates Updates
        {
            get { return updates; }
            set
            {
                updates = value;
                if (updates != null) updates.Changed += () => { if (!IsDisposed) RefreshUpdateUi(); };
                RefreshUpdateUi();
            }
        }
        public string FooterText { get { return footer.StatusText; } }
        public string HeaderSubtitle { get { return header.Subtitle; } }
        public void SetStatus(string text) { footer.StatusText = text; }

        public ProcessControl Control_ { get { return ctl; } }
        public IList<AppCard> Cards { get { return cards; } }
        public IList<AppEntry> Apps { get { return apps; } }
        public IList<LegacyStartupItem> LegacyItems { get { return legacy; } }
        /// <summary>古い自動起動を退避するフォルダ (削除はしない)</summary>
        public static string RemovedStartupDir { get { return Path.Combine(DataStore.AppDir, "removed-startup"); } }
        /// <summary>表示中のお知らせの数 (テスト用)</summary>
        public int NoticeCount { get { return notices.Controls.Count; } }
        /// <summary>Teal Hub 以外で、スタートアップがオンのアプリがあるか</summary>
        public bool AnyAppStartupOn { get { return appStartup.Values.Any(x => x.Registered); } }

        /// <summary>ティール色の歯車つき「設定」ボタン</summary>
        public static FlatButton GearButton()
        {
            var b = new FlatButton("設定", ButtonKind.Secondary);
            b.Image = GearIcon.Get(Theme.S(16), Theme.Accent);
            b.FitWidth(72);
            return b;
        }

        public MainForm() : this(null) { }

        /// <param name="shortcuts">全アプリのショートカット (null ならショートカットなし)</param>
        public MainForm(HubShortcuts shortcuts)
        {
            this.shortcuts = shortcuts ?? new HubShortcuts();
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(640, 420);

            header = HeaderBar.ForApp("TealSuite " + AppInfo.Version);
            btnStopAll = header.AddAction(new FlatButton("すべて終了", ButtonKind.Danger)).FitWidth(88);
            btnUpdate = header.AddAction(new FlatButton(UpdateButtonText, ButtonKind.Secondary)).FitWidth(112);
            // ヘッダーの下: Teal Hub 自身の設定の要約・設定ボタン・再読み込み
            selfBar = new SelfBar();
            selfBar.BtnSettings.Click += (s, e) => ShowSettings(null);
            selfBar.BtnReload.Click += (s, e) => LoadManifest();
            tips.SetToolTip(selfBar.BtnSettings, "Teal Hub のショートカットとスタートアップを設定します");
            tips.SetToolTip(selfBar.BtnReload, "apps.json を読み直します (F5)");
            updateMenu.Font = Theme.Body; updateMenu.ShowImageMargin = false;
            BuildUpdateMenu();
            btnUpdate.Click += (s, e) => { BuildUpdateMenu(); updateMenu.Show(btnUpdate, new Point(0, btnUpdate.Height)); };
            tips.SetToolTip(btnUpdate, "新しいバージョンの確認と、アップデート ファイル (.tealupdate) での更新。\nファイルをこの画面にドラッグしても始められます");
            btnStopAll.Click += (s, e) => StopAll();
            tips.SetToolTip(btnStopAll, "実行中のアプリをすべて終了します");

            listPanel.Dock = DockStyle.Fill;
            listPanel.FlowDirection = FlowDirection.TopDown;
            listPanel.WrapContents = false;
            listPanel.AutoScroll = true;
            listPanel.Padding = Theme.P(Theme.Pad, Theme.Pad, Theme.Pad, Theme.Pad);
            listPanel.BackColor = Theme.Background;
            listPanel.Resize += (s, e) => FitCards();

            empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter;
            empty.Font = Theme.Body; empty.ForeColor = Theme.SubText; empty.Visible = false; empty.UseMnemonic = false;

            notices.Dock = DockStyle.Top;
            notices.FlowDirection = FlowDirection.TopDown;
            notices.WrapContents = false;
            notices.AutoSize = true;
            notices.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            notices.Padding = Theme.P(Theme.Pad, Theme.Pad, Theme.Pad, 0);
            notices.BackColor = Theme.Background;
            notices.Visible = false;
            Resize += (s, e) => FitNotices();

            page = new AppSettingsPage(this, this.shortcuts);
            page.BackRequested += () => ShowList();

            Controls.Add(listPanel);
            Controls.Add(empty);
            Controls.Add(notices);
            Controls.Add(page);
            Controls.Add(footer);
            Controls.Add(selfBar);
            Controls.Add(header);

            AllowDrop = true;
            DragEnter += (s, e) => { e.Effect = DroppedUpdate(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += (s, e) => { var f = DroppedUpdate(e.Data); if (f != null) BeginInvoke((Action)(() => OpenUpdateFile(f))); };
            this.shortcuts.Changed += () => { if (!IsDisposed) RefreshStatus(); };
            if (this.shortcuts.TriggerApp == null) this.shortcuts.TriggerApp = a => TriggerApp(a);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F5 && !page.Box_Focused()) { LoadManifest(); e.Handled = true; } };

            WindowMemory.Attach(this, 860, 640);
            timer.Interval = 1500;
            timer.Tick += (s, e) => Tick();
            Load += (s, e) => { if (!loaded) LoadManifest(); timer.Start(); };
            // ほかの場所でショートカットを置いたりしたときに備え、画面に戻ってきたら古い自動起動を探し直す
            Activated += (s, e) => { if ((DateTime.Now - lastLegacyScan).TotalSeconds > 5) ScanLegacy(); };
            FormClosed += (s, e) => timer.Stop();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && settingsShown)
            {
                if (page.Box_Focused()) page.CancelCapture(); else ShowList();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void LoadManifest()
        {
            loaded = true;
            var path = Manifest.DefaultPath;
            suiteVersion = File.Exists(path) ? Manifest.SuiteVersion(path) : AppInfo.Version;
            header.SetDetail("TealSuite " + suiteVersion);
            string error = null;
            try { allApps = Manifest.Load(path, true); apps = allApps.Where(a => !IsSelf(a)).ToList(); }
            catch (FileNotFoundException) { allApps = new List<AppEntry>(); apps = new List<AppEntry>(); error = "apps.json が見つかりません。\n" + path; }
            catch (Exception ex) { allApps = new List<AppEntry>(); apps = new List<AppEntry>(); error = "apps.json を読み込めませんでした。\n" + ex.Message; DataStore.Log("manifest: " + ex); }

            listPanel.SuspendLayout();
            foreach (var c in cards) c.Dispose();
            listPanel.Controls.Clear();
            cards.Clear();
            foreach (var a in apps)
            {
                var card = new AppCard(a) { Margin = new Padding(0, 0, 0, Theme.S(10)) };
                card.BtnStart.Click += (s, e) => StartOrShow(card, null);
                card.BtnStop.Click += (s, e) => StopApp(card, true);
                card.BtnSettings.Click += (s, e) => ShowSettings(card.App.Id);
                cards.Add(card);
                listPanel.Controls.Add(card);
            }
            shortcuts.Load(apps);
            FitCards();
            listPanel.ResumeLayout();

            empty.Text = error ?? "登録されているアプリはありません。";
            ScanLegacy();
            // 設定ページを開いていたら、読み直したアプリで開き直す (アプリが消えていたら一覧へ)
            if (settingsShown)
            {
                if (page.IsSelfPage) ShowSettings(null);
                else if (FindApp(page.AppId) != null && !IsSelf(FindApp(page.AppId))) ShowSettings(page.AppId);
                else ShowList();
            }
            else ShowList();
            RefreshStatus();
            if (shortcuts.Migrated) footer.StatusText = "1.5 までのショートカットの設定を引き継ぎました";
        }

        static bool IsSelf(AppEntry a) { return a.Self || a.Id.Equals(Program.Id, StringComparison.OrdinalIgnoreCase); }

        // ---------- 画面の切り替え (一覧 ⇔ 設定ページ) ----------

        /// <summary>設定ページに切り替える (appId が null / "TealHub" なら Teal Hub 自身)。見つからなければ false</summary>
        public bool ShowSettings(string appId)
        {
            AppEntry app = null;
            if (appId != null && !appId.Equals(Program.Id, StringComparison.OrdinalIgnoreCase))
            {
                app = apps.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
                if (app == null) { footer.StatusText = appId + " は apps.json にありません"; return false; }
            }
            SuspendLayout();
            listPanel.Visible = false; empty.Visible = false; notices.Visible = false; selfBar.Visible = false;
            page.ShowFor(app);
            page.Visible = true;
            settingsShown = true;
            page.BringToFront();
            ResumeLayout();
            page.BtnBack.Focus();
            return true;
        }

        /// <summary>アプリの一覧に戻る</summary>
        public void ShowList()
        {
            if (settingsShown && page.Box_Focused()) shortcuts.ResumeAll();
            SuspendLayout();
            page.Visible = false;
            settingsShown = false;
            selfBar.Visible = true;
            empty.Visible = cards.Count == 0;
            listPanel.Visible = cards.Count > 0;
            notices.Visible = notices.Controls.Count > 0;
            ResumeLayout();
            RefreshStatus();
        }

        // ---------- スタートアップ・常駐 ----------

        StartupState StartupOf(AppEntry a)
        {
            StartupState st;
            return appStartup.TryGetValue(a.Id, out st) ? st : new StartupState();
        }

        /// <summary>レジストリの今の状態を読み直して、要約・設定ページ・お知らせに反映する</summary>
        public void RefreshStartup()
        {
            appStartup.Clear();
            foreach (var a in apps) appStartup[a.Id] = Startup.Get(a.Id, a.ExePath);
            selfState = Startup.Get(AppInfo.Id, Startup.CurrentExe);
            foreach (var c in cards) UpdateSummary(c);
            UpdateSelfSummary();
            if (settingsShown) page.RefreshState();
            UpdateNotices();
        }

        /// <summary>カードの要約: 「ショートカット: Alt + V ・ 常駐: オン ・ スタートアップ: オフ」</summary>
        void UpdateSummary(AppCard c)
        {
            var parts = new List<string>();
            var color = Theme.SubText;
            var slot = shortcuts.For(c.App.Id);
            if (slot == null || slot.Current.IsEmpty) parts.Add("ショートカット: なし");
            else if (slot.Error != null || (!slot.Active && !shortcuts.Suspended)) { parts.Add("ショートカット: " + slot.Current.Display + " (使えません)"); color = Theme.Danger; }
            else parts.Add("ショートカット: " + slot.Current.Display);
            bool resident = HubShortcuts.IsResident(c.App);
            parts.Add("常駐: " + (resident ? "オン" : "オフ"));
            var st = StartupOf(c.App);
            switch (st.Status)
            {
                case StartupStatus.PathMismatch: parts.Add("スタートアップ: 登録先が違います"); color = Theme.Danger; break;
                case StartupStatus.DisabledByUser: parts.Add("スタートアップ: 無効 (タスク マネージャー)"); if (color != Theme.Danger) color = Theme.Warning; break;
                default:
                    // 常駐オフのアプリはスタートアップを選べないので、登録が残っていなければ出さない
                    if (resident || st.Registered) parts.Add("スタートアップ: " + (st.Registered ? "オン" : "オフ"));
                    break;
            }
            c.SetSummary(string.Join(" ・ ", parts), color);
        }

        void UpdateSelfSummary()
        {
            var self = shortcuts.Self;
            string key;
            var color = Theme.SubText;
            if (self == null || self.Current.IsEmpty) key = "なし";
            else if (self.Error != null || (!self.Active && !shortcuts.Suspended)) { key = self.Current.Display + " (使えません)"; color = Theme.Danger; }
            else key = self.Current.Display;
            string st = selfState.Status == StartupStatus.PathMismatch ? "登録先が違います" : selfState.Status == StartupStatus.DisabledByUser ? "無効 (タスク マネージャー)" : selfState.Registered ? "オン" : "オフ";
            if (selfState.Status != StartupStatus.On && selfState.Registered && color == Theme.SubText) color = Theme.Warning;
            selfBar.SetSummary("ショートカット: " + key + " ・ 常駐: 常に ・ スタートアップ: " + st, color);
        }

        /// <summary>スタートアップのオン・オフ (または「登録し直す」)。変えられたら true</summary>
        public bool SetAppStartup(AppEntry app, bool on)
        {
            if (on && !HubShortcuts.IsResident(app))
            {
                footer.StatusText = app.Name + " は常駐がオフのため、スタートアップに登録できません";
                RefreshStartup();
                return false;
            }
            if (on && !File.Exists(app.ExePath))
            {
                Dialogs.Warn(this, app.Name + " の exe が見つからないため、スタートアップに登録できません。\n\n" + app.ExePath);
                RefreshStartup();
                return false;
            }
            var err = Startup.TrySet(app.Id, app.ExePath, on);
            if (err != null) Dialogs.Error(this, app.Name + " のスタートアップを変更できませんでした。\n\n" + err);
            else footer.StatusText = app.Name + (on ? " を Windows の起動時に通知領域で起動します" : " の自動起動をやめました");
            RefreshStartup();
            ScanLegacy();
            return err == null;
        }

        public bool SetSelfStartup(bool on)
        {
            var err = Startup.TrySet(AppInfo.Id, Startup.CurrentExe, on);
            if (err != null) Dialogs.Error(this, "Teal Hub のスタートアップを変更できませんでした。\n\n" + err);
            else footer.StatusText = on ? "Teal Hub を Windows の起動時に通知領域で起動します" : "Teal Hub の自動起動をやめました";
            RefreshStartup();
            return err == null;
        }

        /// <summary>常駐のオン・オフ。オフにしたらスタートアップも解除する (常駐しないアプリは自動起動しても意味がないため)</summary>
        public bool SetResident(AppEntry app, bool on)
        {
            var err = shortcuts.SetResident(app, on);
            if (err != null) { Dialogs.Error(this, app.Name + " の常駐の設定を保存できませんでした。\n\n" + err); RefreshStartup(); return false; }
            string msg = on ? app.Name + " は×で閉じても通知領域に残ります" : app.Name + " は×で閉じると終了します";
            if (!on && Startup.Get(app.Id, app.ExePath).Command != null)
            {
                var e2 = Startup.TrySet(app.Id, app.ExePath, false);
                msg += e2 == null ? " (スタートアップも解除しました)" : " (スタートアップを解除できませんでした: " + e2 + ")";
            }
            footer.StatusText = msg;
            RefreshStartup();
            return true;
        }

        /// <summary>古い exe (1.0) や手作業のショートカットによる自動起動を探す</summary>
        public void ScanLegacy()
        {
            lastLegacyScan = DateTime.Now;
            try
            {
                legacy = Startup.FindLegacy(allApps.Select(a => new KeyValuePair<string, string[]>(a.Id, a.LegacyIds ?? new string[0])));
            }
            catch (Exception ex) { DataStore.Log("legacy startup: " + ex); legacy = new List<LegacyStartupItem>(); }
            UpdateNotices();
        }

        AppEntry FindApp(string id)
        {
            return allApps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        string ExeFor(AppEntry a) { return IsSelf(a) ? Startup.CurrentExe : a.ExePath; }

        /// <summary>古い自動起動をやめて、正式なスタートアップ登録に置き換える (確認してから)</summary>
        public bool ReplaceLegacy(string appId, bool confirm)
        {
            var app = FindApp(appId);
            var items = legacy.Where(x => x.AppId == appId).ToList();
            if (app == null || items.Count == 0) return false;
            var exe = ExeFor(app);
            if (confirm && !Dialogs.Confirm(this,
                "次の自動起動をやめて、" + app.Name + " のスタートアップ (" + exe + ") に置き換えますか？\n\n"
                + string.Join("\n", items.Select(i => "・" + i.Describe())) + "\n\n"
                + "ショートカットは削除せず、次のフォルダーに移動します (レジストリの値は内容を記録してから消します):\n" + RemovedStartupDir,
                app.Name + " に置き換える")) return false;
            try
            {
                Startup.ReplaceLegacy(items, appId, exe, RemovedStartupDir);
                footer.StatusText = "古い自動起動を " + app.Name + " に置き換えました";
            }
            catch (Exception ex)
            {
                DataStore.Log("replace legacy: " + ex);
                Dialogs.Error(this, "置き換えられませんでした。\n\n" + ex.Message);
            }
            ScanLegacy();
            RefreshStartup();
            return true;
        }

        void UpdateNotices()
        {
            var list = new List<Tuple<string, string, string, Action>>();   // (キー, 文, ボタン, 処理)
            var rel = updates != null ? updates.Available : null;
            if (rel != null && (updateDismissed != rel.Version || updates.Downloading))
            {
                var msg = updates.Downloading
                    ? "TealSuite " + rel.Version + " をダウンロードしています… " + Math.Max(0, updates.DownloadPercent) + "%"
                    : "新しいバージョン TealSuite " + rel.Version + " があります (今は " + updates.Installed + ")" + (rel.Summary.Length > 0 ? "\n" + rel.Summary : "");
                list.Add(Tuple.Create("update|" + rel.Version + "|" + (updates.Downloading ? "dl" + updates.DownloadPercent : ""), msg, "今すぐアップデート", (Action)(() => updates.StartUpdate())));
            }
            if (selfState.Status == StartupStatus.PathMismatch)
                list.Add(Tuple.Create("self", "Teal Hub のスタートアップの登録先が違います。\n登録先: " + selfState.RegisteredPath, "この場所で登録し直す", (Action)(() => SetSelfStartup(true))));
            else if (!selfState.Registered && AnyAppStartupOn && !dismissed.Contains("selfoff"))
                list.Add(Tuple.Create("selfoff", "Teal Hub のスタートアップがオフです。Windows の起動後、Teal Hub を開くまでショートカットは使えません。", "Teal Hub も登録する", (Action)(() => SetSelfStartup(true))));
            foreach (var g in legacy.GroupBy(x => x.AppId))
            {
                var app = FindApp(g.Key);
                if (app == null) continue;
                string key = "legacy|" + g.Key + "|" + string.Join("|", g.Select(i => i.Name));
                if (dismissed.Contains(key)) continue;
                string id = g.Key;
                var exes = g.Select(i => i.ExeName).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                list.Add(Tuple.Create(key, "古い自動起動が " + g.Count() + " 件見つかりました" + (exes.Count > 0 ? " (" + string.Join("、", exes) + ")" : "") + "\n" + string.Join("、", g.Select(i => i.Where)),
                    app.Name + " に置き換える", (Action)(() => ReplaceLegacy(id, true))));
            }
            string k = string.Join("\n", list.Select(t => t.Item1 + t.Item2));
            if (k == noticesKey) return;
            noticesKey = k;
            notices.SuspendLayout();
            foreach (Control c in notices.Controls.Cast<Control>().ToList()) c.Dispose();
            notices.Controls.Clear();
            foreach (var t in list)
            {
                var item = t;
                bool isUpdate = item.Item1.StartsWith("update|");
                var bar = new NoticeBar(item.Item2, item.Item3, item.Item1.StartsWith("legacy") || item.Item1 == "selfoff" || (isUpdate && !updates.Downloading), isUpdate ? "内容を見る" : null, isUpdate);
                if (isUpdate)
                {
                    var r = updates.Available;
                    bar.Action.Enabled = !updates.Downloading;
                    bar.Extra.Click += (s, e) => updates.ShowNotes(r);
                    if (bar.Dismiss != null) bar.Dismiss.Click += (s, e) => { updateDismissed = r.Version; noticesKey = null; UpdateNotices(); };
                    tips.SetToolTip(bar, ReleaseNotesForm.CleanNotes(r.Notes));
                }
                if (item.Item1.StartsWith("legacy"))
                    tips.SetToolTip(bar, string.Join("\n", legacy.Where(x => item.Item1.StartsWith("legacy|" + x.AppId + "|")).Select(x => x.Describe())));
                bar.Action.Click += (s, e) => item.Item4();
                if (bar.Dismiss != null && !isUpdate) bar.Dismiss.Click += (s, e) => { dismissed.Add(item.Item1); noticesKey = null; UpdateNotices(); };
                bar.Margin = new Padding(0, 0, 0, Theme.S(8));
                notices.Controls.Add(bar);
            }
            notices.Visible = list.Count > 0 && !settingsShown;
            FitNotices();
            notices.ResumeLayout();
        }

        void FitNotices()
        {
            int w = ClientSize.Width - notices.Padding.Horizontal;
            foreach (Control c in notices.Controls) c.Width = Math.Max(Theme.S(300), w);
        }

        void FitCards()
        {
            int w = listPanel.ClientSize.Width - listPanel.Padding.Horizontal;
            // 縦スクロールバーが出るときはその幅を引く (横スクロールバーを出さない)
            int total = listPanel.Padding.Vertical + cards.Sum(c => c.Height + c.Margin.Vertical) - (cards.Count > 0 ? cards[cards.Count - 1].Margin.Bottom : 0);
            if (total > listPanel.ClientSize.Height && !listPanel.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
            foreach (var c in cards) c.Width = Math.Max(Theme.S(300), w);
        }

        public void RefreshStatus()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var snap = ctl.Snapshot(apps);
                int running = 0;
                foreach (var c in cards)
                {
                    List<int> pids;
                    c.Pids = snap.TryGetValue(c.App.Id, out pids) ? pids : new List<int>();
                    if (c.Pids.Count > 0) running++;
                    if (c.Busy) continue;
                    if (c.Pids.Count > 0) c.Apply(AppStatus.Running);
                    else if (!File.Exists(c.App.ExePath)) c.Apply(AppStatus.Missing);
                    else c.Apply(AppStatus.Stopped);
                }
                btnStopAll.Enabled = running > 0;
                RefreshStartup();
                if (!settingsShown)
                    footer.StatusText = cards.Count == 0 ? "" : string.Format("{0} 個のアプリ ・ {1} 個が実行中", cards.Count, running) + SelfKeyText();
            }
            finally { refreshing = false; }
        }

        string SelfKeyText()
        {
            var self = shortcuts.Self;
            if (self == null || self.Current.IsEmpty || self.Error != null) return "";
            return " ・ " + self.Current.Display + " で開く";
        }

        void Tick()
        {
            if (Visible && WindowState != FormWindowState.Minimized) RefreshStatus();   // 隠れている間はプロセスを調べない
        }

        public AppCard CardFor(string appId)
        {
            return cards.FirstOrDefault(c => c.App.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// アプリのショートカットが押されたとき:
        ///  起動中なら「ショートカットが押された」と知らせる (Clip は履歴、Volume はミキサー、ほかは前面へ)。
        ///  起動していなければ --show を付けて起動する (続けて押しても 2 秒は 1 回だけ)。
        /// </summary>
        public string TriggerApp(AppEntry app)
        {
            var card = CardFor(app.Id);
            if (card == null) return "none";
            if (ctl.SendHotkey(app)) return "signal";
            RefreshStatus();
            if (card.Pids.Count > 0)
            {
                // Hotkey イベントに応えない (古いバージョンなど) → 前面に出すだけ
                if (!ctl.Show(app)) footer.StatusText = app.Name + " の画面を前面に出せませんでした";
                return "show";
            }
            DateTime last;
            var now = Now();
            if (lastLaunch.TryGetValue(app.Id, out last) && (now - last).TotalMilliseconds < LaunchDebounceMs) return "wait";
            if (card.Busy) return "wait";
            lastLaunch[app.Id] = now;
            return StartOrShow(card, AppPrefs.ShowArg) ? "start" : "error";
        }

        // ---------- アップデート ----------

        static string DroppedUpdate(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return null;
            return files.FirstOrDefault(f => f.EndsWith(UpdaterLauncher.Extension, StringComparison.OrdinalIgnoreCase));
        }

        // ---------- アップデート (オンライン) ----------

        /// <summary>「アップデート ▾」のメニューを今の状態で作り直す</summary>
        public void BuildUpdateMenu()
        {
            updateMenu.Items.Clear();
            updateMenu.Font = Theme.Body; updateMenu.ShowImageMargin = false;
            var rel = updates != null ? updates.Available : null;
            if (rel != null)
            {
                updateMenu.Items.Add(new ToolStripMenuItem(rel.Version + " にアップデート…", null, (s, e) => updates.ShowNotes(rel)) { Font = Theme.Bold, Enabled = !updates.Downloading });
                updateMenu.Items.Add(new ToolStripSeparator());
            }
            if (updates != null)
                updateMenu.Items.Add(new ToolStripMenuItem(updates.Checking ? "確認しています…" : "更新を確認", null, (s, e) => updates.CheckNow()) { Enabled = !updates.Checking && !updates.Downloading, Font = rel == null ? Theme.Bold : Theme.Body });
            updateMenu.Items.Add(new ToolStripMenuItem("アップデート ファイルを開く…", null, (s, e) => ChooseUpdateFile()) { Font = updates == null ? Theme.Bold : Theme.Body });
            updateMenu.Items.Add(new ToolStripMenuItem("前のバージョンに戻す…", null, (s, e) => UpdaterLauncher.Rollback(this)));
            updateMenu.Items.Add(new ToolStripSeparator());
            if (updates != null)
            {
                var auto = new ToolStripMenuItem("自動で更新を確認する (1 日 1 回)") { Checked = updates.AutoCheckEnabled, CheckOnClick = true };
                auto.Click += (s, e) => updates.SetAutoCheck(auto.Checked);
                updateMenu.ShowCheckMargin = true;
                updateMenu.Items.Add(auto);
                updateMenu.Items.Add(new ToolStripMenuItem("リリース ページを開く", null, (s, e) => HubUpdates.OpenReleasePage(rel != null ? rel.PageUrl : null)));
            }
            updateMenu.Items.Add(new ToolStripMenuItem("アップデートのしかた", null, (s, e) => Dialogs.Info(this, UpdaterLauncher.HowTo)));
        }

        void RefreshUpdateUi()
        {
            var rel = updates != null ? updates.Available : null;
            btnUpdate.Text = rel != null ? "● " + rel.Version + " ▾" : UpdateButtonText;
            tips.SetToolTip(btnUpdate, rel != null ? "新しいバージョン " + rel.Version + " があります。クリックしてアップデート" : "新しいバージョンの確認と、アップデート ファイル (.tealupdate) での更新。\nファイルをこの画面にドラッグしても始められます");
            btnUpdate.Invalidate();
            noticesKey = null;
            UpdateNotices();
        }

        public void OpenUpdateFileDialog() { ChooseUpdateFile(); }

        void ChooseUpdateFile()
        {
            using (var dlg = new OpenFileDialog { Title = "アップデート ファイルを開く", Filter = "TealSuite のアップデート (*" + UpdaterLauncher.Extension + ")|*" + UpdaterLauncher.Extension + "|すべてのファイル (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenUpdateFile(dlg.FileName);
            }
        }

        public void OpenUpdateFile(string path)
        {
            if (UpdateFileChosen != null) { UpdateFileChosen(path); return; }
            UpdaterLauncher.Open(this, path);
        }

        // ---------- 閉じる ----------

        /// <summary>本当に閉じる (Teal Hub の終了)</summary>
        public void Shutdown()
        {
            allowClose = true;
            timer.Stop();
            if (Visible) Close();
            if (!IsDisposed) Dispose();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);   // 先に位置を保存させる (WindowMemory)
            if (e.Cancel || allowClose || !HideOnClose || AppHost.IsExiting) return;
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (HideRequested != null) HideRequested(); else Hide();
            }
        }

        /// <summary>起動 (起動中なら前面へ)。args はコマンドライン引数。起動できたら true</summary>
        bool StartOrShow(AppCard card, string args)
        {
            if (card.Busy) return false;
            if (card.Pids.Count > 0)
            {
                if (!ctl.Show(card.App)) footer.StatusText = card.App.Name + " の画面を前面に出せませんでした";
                return true;
            }
            bool ok = false;
            try
            {
                card.Busy = true;
                card.Apply(AppStatus.Busy, "起動中…");
                ctl.Start(card.App, args);
                footer.StatusText = card.App.Name + " を起動しました";
                ok = true;
            }
            catch (Exception ex)
            {
                if (args == null || Visible) Dialogs.Error(this, card.App.Name + " を起動できませんでした。\n\n" + ex.Message);
                else DataStore.Log("hotkey start " + card.App.Id + ": " + ex.Message);
                footer.StatusText = card.App.Name + " を起動できませんでした";
            }
            finally
            {
                // 起動直後はプロセスが見えるまで少し待ってから状態を更新する
                var t = new Timer { Interval = 800 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); card.Busy = false; RefreshStatus(); };
                t.Start();
            }
            return ok;
        }

        /// <summary>穏やかに終了 → 応答がなければ確認して強制終了。終わったら true。</summary>
        public async Task<bool> StopApp(AppCard card, bool askBeforeKill)
        {
            if (card.Busy) return false;
            RefreshStatus();
            var pids = card.Pids.ToList();
            if (pids.Count == 0) return true;
            card.Busy = true;
            card.Apply(AppStatus.Busy, "終了中…");
            try
            {
                var r = await Task.Run(() => ctl.RequestStop(card.App, pids, StopTimeoutMs));
                if (r == StopResult.StillRunning)
                {
                    if (askBeforeKill && !Dialogs.Confirm(this, card.App.Name + " が応答しません。強制終了しますか？\n\n保存していない内容は失われることがあります。", "強制終了の確認"))
                    { footer.StatusText = card.App.Name + " の終了を取りやめました"; return false; }
                    var killed = await Task.Run(() => ctl.Kill(pids));
                    footer.StatusText = killed ? card.App.Name + " を強制終了しました" : card.App.Name + " を終了できませんでした";
                    return killed;
                }
                footer.StatusText = card.App.Name + " を終了しました";
                return true;
            }
            finally
            {
                card.Busy = false;
                RefreshStatus();
            }
        }

        async void StopAll()
        {
            RefreshStatus();
            var running = cards.Where(c => c.Pids.Count > 0 && !c.Busy).ToList();
            if (running.Count == 0) return;
            if (!Dialogs.Confirm(this, "実行中の " + running.Count + " 個のアプリをすべて終了しますか？\n(" + string.Join("、", running.Select(c => c.App.Name)) + ")", "すべて終了")) return;
            btnStopAll.Enabled = false;
            var tasks = running.Select(c => StopApp(c, true)).ToList();
            var results = await Task.WhenAll(tasks);
            footer.StatusText = results.All(x => x) ? "すべてのアプリを終了しました" : "終了できなかったアプリがあります";
            RefreshStatus();
        }
    }
}
