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

    /// <summary>1 アプリぶんのカード</summary>
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
        public readonly StatusPill Pill = new StatusPill();
        public readonly FlatButton BtnStart = new FlatButton("起動", ButtonKind.Primary);
        public readonly FlatButton BtnStop = new FlatButton("終了", ButtonKind.Secondary);
        /// <summary>Windows の起動時に自動で起動するか (HKCU の Run キー)</summary>
        public readonly ToggleSwitch StartupSwitch = new ToggleSwitch("スタートアップ");
        /// <summary>登録先が違う / タスク マネージャーで無効のときに出す「登録し直す」</summary>
        public readonly FlatButton StartupFix = new FlatButton("登録し直す", ButtonKind.Secondary);
        /// <summary>利用者がトグルを切り替えたとき (プログラムからの変更では発生しない)</summary>
        public event EventHandler StartupToggled;
        public StartupState StartupInfo = new StartupState();
        readonly ToolTip tip = new ToolTip();
        /// <summary>ショートカットの行 (「履歴を呼び出す [Ctrl + Shift + V] 変更 解除」)</summary>
        readonly FlowLayoutPanel keys = new FlowLayoutPanel();
        public readonly List<ShortcutChip> Chips = new List<ShortcutChip>();
        public event Action<ShortcutSlot> ShortcutEdit, ShortcutClear;
        bool updatingStartup;
        string startupKey;

        public AppCard(AppEntry app)
        {
            App = app;
            Height = Theme.S(88);
            Padding = Theme.P(0);
            icon.Size = new Size(Theme.S(44), Theme.S(44));
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.BackColor = Theme.Surface;
            icon.Image = LoadIcon(app);
            name.Text = app.Name; name.Font = Theme.Title; name.ForeColor = Theme.Text; name.AutoSize = true; name.UseMnemonic = false; name.BackColor = Theme.Surface;
            role.Text = app.Role ?? ""; role.Font = Theme.Small; role.ForeColor = Theme.Accent; role.AutoSize = true; role.UseMnemonic = false; role.BackColor = Theme.Surface; role.Visible = role.Text.Length > 0;
            version.Text = string.IsNullOrEmpty(app.Version) ? "" : "v" + app.Version; version.Font = Theme.Small; version.ForeColor = Theme.SubText; version.AutoSize = true; version.BackColor = Theme.Surface;
            desc.Text = app.Description ?? ""; desc.Font = Theme.Small; desc.ForeColor = Theme.SubText; desc.AutoSize = false; desc.AutoEllipsis = true; desc.UseMnemonic = false; desc.BackColor = Theme.Surface;
            BtnStart.FitWidth(76); BtnStop.FitWidth(76);
            StartupSwitch.AutoSize = false; StartupSwitch.Size = StartupSwitch.GetPreferredSize(Size.Empty); StartupSwitch.Margin = Padding.Empty;
            StartupSwitch.CheckedChanged += (s, e) => { if (!updatingStartup && StartupToggled != null) StartupToggled(this, EventArgs.Empty); };
            StartupFix.Font = Theme.Small; StartupFix.Visible = false; StartupFix.FitWidth(76); StartupFix.Height = Theme.S(24);
            keys.WrapContents = false; keys.AutoSize = false; keys.BackColor = Theme.Surface; keys.Margin = Padding.Empty; keys.Padding = Padding.Empty; keys.Visible = false;
            Controls.AddRange(new Control[] { icon, name, role, version, desc, keys, StartupFix, Pill, StartupSwitch, BtnStart, BtnStop });
            tip.SetToolTip(icon, app.ExePath); tip.SetToolTip(name, app.ExePath);
            tip.SetToolTip(StartupSwitch, "オンにすると、Windows にサインインしたときに " + app.Name + " を自動で起動します");
        }

        /// <summary>ショートカットの行を作り直す (mgr が null なら行を出さない)</summary>
        public void SetShortcuts(IEnumerable<ShortcutSlot> slots, HubShortcuts mgr)
        {
            keys.SuspendLayout();
            foreach (var c in Chips) c.Dispose();
            Chips.Clear(); keys.Controls.Clear();
            if (mgr != null)
                foreach (var sl in slots)
                {
                    var chip = new ShortcutChip(sl, mgr, tip);
                    chip.EditRequested += x => { if (ShortcutEdit != null) ShortcutEdit(x); };
                    chip.ClearRequested += x => { if (ShortcutClear != null) ShortcutClear(x); };
                    Chips.Add(chip); keys.Controls.Add(chip);
                }
            keys.Visible = Chips.Count > 0;
            keys.ResumeLayout();
            Height = Theme.S(keys.Visible ? 112 : 88);
            PerformLayout();
        }

        /// <summary>ショートカットの値・重複の表示だけを更新する</summary>
        public void RefreshShortcuts(HubShortcuts mgr)
        {
            foreach (var c in Chips) c.Refresh_(mgr, tip);
        }

        /// <summary>スタートアップの状態を表示に反映する (トグルのイベントは発生させない)</summary>
        public void ApplyStartup(StartupState st)
        {
            StartupInfo = st ?? new StartupState();
            bool missing = !File.Exists(App.ExePath);
            string key = StartupInfo.Status + "|" + StartupInfo.Command + "|" + missing;
            if (key == startupKey) return;   // 変化がなければ何もしない (ちらつき防止)
            startupKey = key;
            updatingStartup = true;
            try
            {
                StartupSwitch.Checked = StartupInfo.Registered;
                StartupSwitch.Enabled = !(missing && !StartupInfo.Registered);   // exe がなければオンにできない (オフにはできる)
            }
            finally { updatingStartup = false; }
            switch (StartupInfo.Status)
            {
                case StartupStatus.PathMismatch:
                    desc.Text = "登録先が違います: " + Startup.ShortPath(StartupInfo.RegisteredPath, 30);
                    desc.ForeColor = Theme.Danger;
                    StartupFix.Text = "登録し直す";
                    StartupFix.Visible = !missing;
                    break;
                case StartupStatus.DisabledByUser:
                    desc.Text = "スタートアップはタスク マネージャーで無効になっています";
                    desc.ForeColor = Theme.Warning;
                    StartupFix.Text = "有効に戻す";
                    StartupFix.Visible = !missing;
                    break;
                default:
                    desc.Text = App.Description ?? "";
                    desc.ForeColor = Theme.SubText;
                    StartupFix.Visible = false;
                    break;
            }
            tip.SetToolTip(desc, StartupInfo.Command == null ? desc.Text : "登録内容: " + StartupInfo.Command);
            PerformLayout();
            Invalidate(true);
        }

        static Image LoadIcon(AppEntry app)
        {
            int px = Theme.S(44);
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
            BtnStart.Location = new Point(right - BtnStart.Width, (Height - BtnStart.Height) / 2); right = BtnStart.Left - Theme.S(14);
            // 状態ピルとスタートアップのトグルを縦に並べる
            int colW = Math.Max(Pill.Width, StartupSwitch.Width);
            int blockH = Pill.Height + Theme.S(6) + StartupSwitch.Height;
            int colTop = (Height - blockH) / 2;
            Pill.Location = new Point(right - colW, colTop);
            StartupSwitch.Location = new Point(right - colW, Pill.Bottom + Theme.S(6));
            right = right - colW - Theme.S(12);
            int x = icon.Right + Theme.S(14);
            int keysH = keys.Visible ? Theme.S(22) + Theme.S(10) : 0;
            int top = (Height - (name.PreferredHeight + Theme.S(4) + desc.Font.Height + keysH)) / 2;
            name.Location = new Point(x, top);
            int after = name.Right + Theme.S(8);
            if (role.Visible) { role.Location = new Point(after, top + name.PreferredHeight - role.PreferredHeight - 1); after = role.Right + Theme.S(6); }
            version.Location = new Point(after, top + name.PreferredHeight - version.PreferredHeight - 1);
            int descRight = right;
            if (StartupFix.Visible)
            {
                StartupFix.Location = new Point(right - StartupFix.Width, name.Bottom + Theme.S(4) + (desc.Font.Height + 2 - StartupFix.Height) / 2);
                descRight = StartupFix.Left - Theme.S(6);
            }
            desc.Bounds = new Rectangle(x, name.Bottom + Theme.S(4), Math.Max(10, descRight - x), desc.Font.Height + 2);
            if (keys.Visible) keys.Bounds = new Rectangle(x, desc.Bottom + Theme.S(8), Math.Max(10, right - x), Theme.S(22));
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

    /// <summary>ヘッダーの下の帯: 「Teal Hub」 スタートアップ / 開くショートカット … 再読み込み</summary>
    public class SelfBar : Panel
    {
        readonly FlowLayoutPanel left = new FlowLayoutPanel(), right = new FlowLayoutPanel();
        public SelfBar()
        {
            Dock = DockStyle.Top; Height = Theme.S(40); BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(16), 0, Theme.S(10), 1);
            foreach (var f in new[] { left, right })
            {
                f.WrapContents = false; f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink; f.BackColor = Theme.Surface; f.Padding = Padding.Empty; f.Margin = Padding.Empty;
            }
            right.FlowDirection = FlowDirection.RightToLeft;
            var lbl = new Label { Text = "Teal Hub", Font = Theme.Bold, ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, Theme.S(2), Theme.S(14), 0) };
            left.Controls.Add(lbl);
            Controls.Add(left); Controls.Add(right);
        }
        public void Add(Control c, bool atRight)
        {
            c.Margin = new Padding(0, 0, Theme.S(18), 0);
            (atRight ? right : left).Controls.Add(c);
            PerformLayout();
        }
        public void Remove(Control c) { left.Controls.Remove(c); right.Controls.Remove(c); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (right == null) return;
            int h = Height - 1;
            foreach (Control c in left.Controls) c.Margin = new Padding(c.Margin.Left, Math.Max(0, (Theme.S(26) - c.PreferredSize.Height) / 2), c.Margin.Right, 0);
            left.Location = new Point(Padding.Left, (h - left.Height) / 2);
            right.Location = new Point(Width - Padding.Right - right.Width, (h - right.Height) / 2);
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
        readonly ProcessControl ctl = new ProcessControl();
        readonly FlowLayoutPanel listPanel = new FlowLayoutPanel();
        readonly FooterBar footer = new FooterBar();
        readonly Timer timer = new Timer();
        readonly Label empty = new Label();
        readonly FlatButton btnStopAll;
        /// <summary>Teal Hub 自身のスタートアップ (ヘッダー)</summary>
        readonly ToggleSwitch selfStartup = new ToggleSwitch("スタートアップ");
        /// <summary>登録先の違い・古い自動起動などのお知らせ</summary>
        readonly FlowLayoutPanel notices = new FlowLayoutPanel();
        readonly ToolTip tips = new ToolTip();
        bool updatingSelf;
        StartupState selfState = new StartupState();
        List<AppEntry> allApps = new List<AppEntry>();   // Teal Hub 自身も含む (古い自動起動の判定用)
        List<LegacyStartupItem> legacy = new List<LegacyStartupItem>();
        readonly HashSet<string> dismissed = new HashSet<string>();
        string noticesKey;
        DateTime lastLegacyScan = DateTime.MinValue;
        List<AppEntry> apps = new List<AppEntry>();
        readonly List<AppCard> cards = new List<AppCard>();
        bool refreshing;
        readonly HubShortcuts shortcuts;
        readonly HeaderBar header;
        readonly SelfBar selfBar;
        ShortcutChip selfChip;
        readonly FlatButton btnShortcuts, btnUpdate;
        readonly ContextMenuStrip updateMenu = new ContextMenuStrip();
        ShortcutListForm listForm;
        HotkeySettingsForm editForm;
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
        public HubShortcuts Shortcuts { get { return shortcuts; } }
        public ShortcutListForm ListForm { get { return listForm != null && !listForm.IsDisposed ? listForm : null; } }
        public HotkeySettingsForm EditForm { get { return editForm != null && !editForm.IsDisposed ? editForm : null; } }
        public string SuiteVersion { get { return suiteVersion; } }
        public string UpdateButtonLabel { get { return btnUpdate.Text; } }
        public ContextMenuStrip UpdateMenu { get { return updateMenu; } }
        /// <summary>お知らせの帯 (テスト用)</summary>
        public System.Collections.Generic.IEnumerable<NoticeBar> NoticeBars { get { return notices.Controls.OfType<NoticeBar>(); } }

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

        public ProcessControl Control_ { get { return ctl; } }
        public IList<AppCard> Cards { get { return cards; } }
        public ToggleSwitch SelfStartup { get { return selfStartup; } }
        public IList<LegacyStartupItem> LegacyItems { get { return legacy; } }
        /// <summary>古い自動起動を退避するフォルダ (削除はしない)</summary>
        public static string RemovedStartupDir { get { return Path.Combine(DataStore.AppDir, "removed-startup"); } }
        /// <summary>表示中のお知らせの数 (テスト用)</summary>
        public int NoticeCount { get { return notices.Controls.Count; } }

        public MainForm() : this(null) { }

        /// <param name="shortcuts">全アプリのショートカット (null なら出さない)</param>
        public MainForm(HubShortcuts shortcuts)
        {
            this.shortcuts = shortcuts;
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(640, 400);

            header = HeaderBar.ForApp("TealSuite " + AppInfo.Version);
            btnStopAll = header.AddAction(new FlatButton("すべて終了", ButtonKind.Danger)).FitWidth(88);
            btnUpdate = header.AddAction(new FlatButton(UpdateButtonText, ButtonKind.Secondary)).FitWidth(112);
            btnShortcuts = header.AddAction(new FlatButton("ショートカット一覧", ButtonKind.Secondary)).FitWidth(96);
            btnShortcuts.Visible = shortcuts != null;
            // ヘッダーの下: Teal Hub 自身の設定 (スタートアップ・開くショートカット) と再読み込み
            selfBar = new SelfBar();
            var btnReload = new FlatButton("再読み込み", ButtonKind.Ghost);
            btnReload.Font = Theme.Small; btnReload.FitWidth(72);
            selfBar.Add(selfStartup, false);
            selfBar.Add(btnReload, true);
            updateMenu.Font = Theme.Body; updateMenu.ShowImageMargin = false;
            BuildUpdateMenu();
            btnUpdate.Click += (s, e) => { BuildUpdateMenu(); updateMenu.Show(btnUpdate, new Point(0, btnUpdate.Height)); };
            btnShortcuts.Click += (s, e) => ShowShortcutList();
            tips.SetToolTip(btnUpdate, "新しいバージョンの確認と、アップデート ファイル (.tealupdate) での更新。\nファイルをこの画面にドラッグしても始められます");
            tips.SetToolTip(btnShortcuts, "すべてのアプリのショートカットを一覧で確認・変更します");
            selfStartup.CheckedChanged += (s, e) => { if (!updatingSelf) SetSelfStartup(selfStartup.Checked); };
            btnStopAll.Click += (s, e) => StopAll();
            btnReload.Click += (s, e) => LoadManifest();
            tips.SetToolTip(selfStartup, shortcuts != null
                ? "オンにすると、Windows にサインインしたときに Teal Hub を通知領域で起動します。\nTeal Hub のショートカット (Alt + H など) は、Teal Hub が起動している間だけ使えます"
                : "オンにすると、Windows にサインインしたときに Teal Hub を自動で起動します");
            tips.SetToolTip(btnReload, "apps.json を読み直します (F5)");
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

            Controls.Add(listPanel);
            Controls.Add(empty);
            Controls.Add(notices);
            Controls.Add(footer);
            Controls.Add(selfBar);
            Controls.Add(header);

            AllowDrop = true;
            DragEnter += (s, e) => { e.Effect = DroppedUpdate(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += (s, e) => { var f = DroppedUpdate(e.Data); if (f != null) BeginInvoke((Action)(() => OpenUpdateFile(f))); };
            if (shortcuts != null) shortcuts.Changed += () => { if (!IsDisposed) RefreshShortcutViews(); };

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F5) { LoadManifest(); e.Handled = true; } };

            WindowMemory.Attach(this, 860, 640);
            timer.Interval = 1500;
            timer.Tick += (s, e) => Tick();
            Load += (s, e) => { if (!loaded) LoadManifest(); timer.Start(); };
            // ほかの場所でショートカットを置いたりしたときに備え、画面に戻ってきたら古い自動起動を探し直す
            Activated += (s, e) => { if ((DateTime.Now - lastLegacyScan).TotalSeconds > 5) ScanLegacy(); };
            FormClosed += (s, e) => timer.Stop();
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
                card.BtnStart.Click += (s, e) => StartOrShow(card);
                card.BtnStop.Click += (s, e) => StopApp(card, true);
                card.StartupToggled += (s, e) => SetAppStartup(card, card.StartupSwitch.Checked);
                card.StartupFix.Click += (s, e) => SetAppStartup(card, true);
                card.ShortcutEdit += x => EditShortcut(x);
                card.ShortcutClear += x => ClearShortcut(x, true);
                cards.Add(card);
                listPanel.Controls.Add(card);
            }
            if (shortcuts != null)
            {
                shortcuts.Load(apps);
                foreach (var c in cards) c.SetShortcuts(shortcuts.For(c.App.Id), shortcuts);
                if (selfChip != null) { selfBar.Remove(selfChip); selfChip.Dispose(); selfChip = null; }
                if (shortcuts.Self != null)
                {
                    selfChip = new ShortcutChip(shortcuts.Self, shortcuts, tips);
                    selfChip.EditRequested += x => EditShortcut(x);
                    selfChip.ClearRequested += x => ClearShortcut(x, true);
                    selfBar.Add(selfChip, false);
                }
            }
            FitCards();
            listPanel.ResumeLayout();
            if (ListForm != null) ListForm.Rebuild();

            empty.Text = error ?? "登録されているアプリはありません。";
            empty.Visible = cards.Count == 0;
            listPanel.Visible = cards.Count > 0;
            ScanLegacy();
            RefreshStatus();
        }

        static bool IsSelf(AppEntry a) { return a.Self || a.Id.Equals(Program.Id, StringComparison.OrdinalIgnoreCase); }

        // ---------- スタートアップ ----------

        /// <summary>カードのトグル・ヘッダーのトグル・お知らせを、レジストリの今の状態に合わせる</summary>
        public void RefreshStartup()
        {
            foreach (var c in cards) c.ApplyStartup(Startup.Get(c.App.Id, c.App.ExePath));
            selfState = Startup.Get(AppInfo.Id, Startup.CurrentExe);
            updatingSelf = true;
            try { selfStartup.Checked = selfState.Registered; }
            finally { updatingSelf = false; }
            UpdateNotices();
        }

        /// <summary>カードのトグル (または「登録し直す」)</summary>
        public void SetAppStartup(AppCard card, bool on)
        {
            if (on && !File.Exists(card.App.ExePath))
            {
                Dialogs.Warn(this, card.App.Name + " の exe が見つからないため、スタートアップに登録できません。\n\n" + card.App.ExePath);
                card.ApplyStartup(Startup.Get(card.App.Id, card.App.ExePath));
                return;
            }
            var err = Startup.TrySet(card.App.Id, card.App.ExePath, on);
            if (err != null) Dialogs.Error(this, card.App.Name + " のスタートアップを変更できませんでした。\n\n" + err);
            else footer.StatusText = card.App.Name + (on ? " を Windows の起動時に自動で起動します" : " の自動起動をやめました");
            RefreshStartup();
            ScanLegacy();
        }

        public void SetSelfStartup(bool on)
        {
            var err = Startup.TrySet(AppInfo.Id, Startup.CurrentExe, on);
            if (err != null) Dialogs.Error(this, "Teal Hub のスタートアップを変更できませんでした。\n\n" + err);
            else footer.StatusText = on ? "Teal Hub を Windows の起動時に自動で起動します" : "Teal Hub の自動起動をやめました";
            RefreshStartup();
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
                var bar = new NoticeBar(item.Item2, item.Item3, item.Item1.StartsWith("legacy") || (isUpdate && !updates.Downloading), isUpdate ? "内容を見る" : null, isUpdate);
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
            notices.Visible = list.Count > 0;
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
                footer.StatusText = cards.Count == 0 ? "" : string.Format("{0} 個のアプリ ・ {1} 個が実行中", cards.Count, running) + SelfKeyText();
            }
            finally { refreshing = false; }
        }

        string SelfKeyText()
        {
            var self = shortcuts == null ? null : shortcuts.Self;
            if (self == null || self.Current.IsEmpty || self.Error != null) return "";
            return " ・ " + self.Current.Display + " で開く";
        }

        void Tick()
        {
            if (shortcuts != null) shortcuts.RefreshFromDisk();   // アプリ側で変えたショートカット
            if (Visible && WindowState != FormWindowState.Minimized) RefreshStatus();   // 隠れている間はプロセスを調べない
        }

        // ---------- ショートカット ----------

        void RefreshShortcutViews()
        {
            foreach (var c in cards) c.RefreshShortcuts(shortcuts);
            if (selfChip != null) { selfChip.Refresh_(shortcuts, tips); selfBar.PerformLayout(); }
            if (ListForm != null) ListForm.RefreshValues();
            RefreshStatus();
        }

        public AppCard CardFor(string appId)
        {
            return cards.FirstOrDefault(c => c.App.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>ショートカットでアプリを開く (起動していなければ起動、起動中なら前面へ)</summary>
        public void OpenApp(AppEntry app)
        {
            var card = CardFor(app.Id);
            if (card == null) return;
            RefreshStatus();
            StartOrShow(card);
        }

        public void ShowShortcutList()
        {
            if (shortcuts == null) return;
            if (ListForm != null) { AppHost.BringToFront(listForm); return; }
            listForm = new ShortcutListForm(this, shortcuts);
            listForm.FormClosed += (s, e) => listForm = null;
            listForm.Show(this);
        }

        public static string EditTitle(ShortcutSlot s) { return s.AppName + " ・ " + s.Label; }

        /// <summary>変更の画面を開く (HotkeySettingsForm)</summary>
        public HotkeySettingsForm EditShortcut(ShortcutSlot slot)
        {
            if (EditForm != null) editForm.Close();
            editForm = new HotkeySettingsForm(slot, EditTitle(slot), null);
            editForm.FormClosed += (s, e) => { editForm = null; RefreshShortcutViews(); };
            var owner = ListForm != null && listForm.Visible ? (Form)listForm : this;
            if (owner.Visible) editForm.Show(owner); else editForm.Show();
            return editForm;
        }

        /// <summary>解除する (なしにする)</summary>
        public bool ClearShortcut(ShortcutSlot slot, bool confirm)
        {
            if (slot.Current.IsEmpty) return true;
            if (confirm && slot.IsSelf && !Dialogs.Confirm(this, slot.Current.Display + " で Teal Hub を開けなくなります。解除しますか？\n(通知領域のアイコンからは、いつでも開けます)", "ショートカットを解除")) return false;
            var err = slot.Apply(HotkeySpec.None);
            if (err != null) { Dialogs.Error(this, err); return false; }
            footer.StatusText = slot.Describe() + " のショートカットを解除しました";
            return true;
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
            try { if (ListForm != null) listForm.Close(); } catch { }
            try { if (EditForm != null) editForm.Close(); } catch { }
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
                if (ListForm != null) listForm.Hide();
                if (HideRequested != null) HideRequested(); else Hide();
            }
        }

        void StartOrShow(AppCard card)
        {
            if (card.Busy) return;
            if (card.Pids.Count > 0)
            {
                if (!ctl.Show(card.App)) footer.StatusText = card.App.Name + " の画面を前面に出せませんでした";
                return;
            }
            try
            {
                card.Busy = true;
                card.Apply(AppStatus.Busy, "起動中…");
                ctl.Start(card.App);
                footer.StatusText = card.App.Name + " を起動しました";
            }
            catch (Exception ex)
            {
                Dialogs.Error(this, card.App.Name + " を起動できませんでした。\n\n" + ex.Message);
            }
            finally
            {
                // 起動直後はプロセスが見えるまで少し待ってから状態を更新する
                var t = new Timer { Interval = 800 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); card.Busy = false; RefreshStatus(); };
                t.Start();
            }
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
