// Teal Hub (アプリ管理) - 常駐部分 (通知領域アイコン・全アプリのショートカット・画面の開閉)
//  - ×で閉じても終了せず通知領域に隠れる (初回だけ案内を出す)。終了は通知領域のメニュー
//  - Alt+H (初期設定、変更・解除も可) で画面を開く / 隠す。ほかのアプリを開くショートカットも Teal Hub が受け持つ
//  - --startup (ログイン時の自動起動) では画面を出さずに常駐する。もう一度起動すると画面を出す
//  - --settings <アプリ ID> で起動されたら、そのアプリの設定ページを開く (各アプリの「設定…」メニューから)
using System;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    [DataContract]
    public class HubSettings
    {
        [DataMember(Name = "trayTipShown", Order = 1)] public bool TrayTipShown;
        /// <summary>オンラインのアップデート確認をしない (既定は 1 日 1 回確認する)</summary>
        [DataMember(Name = "updateCheckOff", Order = 2)] public bool UpdateCheckOff;
        /// <summary>最後に確認した時刻 (UTC, ISO 8601)</summary>
        [DataMember(Name = "lastUpdateCheck", Order = 3, EmitDefaultValue = false)] public string LastUpdateCheck;
        /// <summary>通知領域でお知らせしたバージョン (同じバージョンは 1 回だけ)</summary>
        [DataMember(Name = "notifiedVersion", Order = 4, EmitDefaultValue = false)] public string NotifiedVersion;
    }

    public class HubTray : ApplicationContext
    {
        public const string SettingsFile = "settings.json";
        public readonly MainForm Window;
        public readonly HubShortcuts Shortcuts;
        public readonly HubUpdates Updates;
        readonly HubSettings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        bool exiting;

        public Action ExitAction = AppHost.RequestExit;
        public int TipCount { get; private set; }
        public bool IsExited { get { return exiting; } }
        public Func<bool> ForegroundCheck;

        public HubTray(string[] args, bool startHidden)
        {
            settings = DataStore.LoadQuiet(SettingsFile, () => new HubSettings());
            Shortcuts = new HubShortcuts();
            Window = new MainForm(Shortcuts) { HideOnClose = true };
            Shortcuts.ToggleHub = ToggleFromHotkey;
            Shortcuts.TriggerApp = a => Window.TriggerApp(a);
            Shortcuts.Changed += UpdateTrayText;
            Window.HideRequested += HideWindow;
            Window.FormClosed += (s, e) => { if (!exiting) ExitApp(); };
            Window.LoadManifest();   // 画面を出さなくてもショートカットは使えるように、ここで読み込む
            Updates = new HubUpdates(Window, settings, () => DataStore.TrySave(SettingsFile, settings));
            Updates.Balloon = (title, text) => { try { tray.ShowBalloonTip(10000, title, text, ToolTipIcon.Info); } catch { } };
            Updates.Changed += UpdateTrayText;
            Window.Updates = Updates;

            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
            menu.Items.Add(new ToolStripMenuItem("Teal Hub の設定…", null, (s, e) => OpenSettings(null)));
            menu.Items.Add(new ToolStripMenuItem("更新を確認", null, (s, e) => { ShowWindow(); Updates.CheckNow(); }));
            menu.Items.Add(new ToolStripMenuItem("アップデート ファイルを開く…", null, (s, e) => { ShowWindow(); Window.BeginInvoke((Action)(() => Window.OpenUpdateFileDialog())); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            UpdateTrayText();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.Visible = true;

            AppHost.ShowRequested = ShowWindow;   // 2 回目の起動 (スタート メニューなどからもう一度開いたとき)
            AppHost.ExitRequested = ExitApp;      // TealUpdater がアップデートの前に終了を頼むとき
            AppHost.ReloadRequested = () => Window.LoadManifest();   // apps.json が変わったとき
            Application.ApplicationExit += (s, e) => Cleanup();

            FileAssoc.Ensure(UpdaterLauncher.UpdaterExe);

            var self = Shortcuts.Self;
            if (self != null && self.Error != null)
                tray.ShowBalloonTip(8000, AppInfo.DisplayName, self.Error + "\nTeal Hub の「設定」で変更できます。", ToolTipIcon.Warning);

            HubLink.TakeRequest();   // 前回の残り (使わない)
            var target = HubLink.SettingsTarget(args);
            if (target != null) OpenSettings(target);
            else if (!startHidden) ShowWindow();
            // オンラインのアップデート確認 (ログイン直後の混雑を避けて少し待つ。テストでは TEALSUITE_NO_UPDATE_CHECK=1 で止める)
            if (Environment.GetEnvironmentVariable("TEALSUITE_NO_UPDATE_CHECK") != "1") Updates.Start(startHidden ? 60000 : 5000);
        }

        void UpdateTrayText()
        {
            var self = Shortcuts.Self;
            var t = AppInfo.DisplayName + (self != null && !self.Current.IsEmpty && self.Error == null ? " (" + self.Current.Display + " で開く)" : "");
            var rel = Updates != null ? Updates.Available : null;
            if (rel != null) t += "\n新しいバージョン " + rel.Version + " があります";
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        public bool WindowShown { get { return Window.Visible && Window.WindowState != FormWindowState.Minimized; } }

        /// <summary>設定ページを開いて画面を出す (appId が null / TealHub なら Teal Hub 自身)</summary>
        public void OpenSettings(string appId)
        {
            if (exiting) return;
            ShowWindow();
            if (!Window.ShowSettings(appId)) Window.ShowList();
        }

        public void ShowWindow()
        {
            if (exiting) return;
            // ほかのアプリの「設定…」から頼まれたページ (TealHub.exe --settings <ID>)
            var req = HubLink.TakeRequest();
            if (req != null) { AppHost.BringToFront(Window); if (!Window.ShowSettings(req)) Window.ShowList(); return; }
            bool wasHidden = !Window.Visible;
            AppHost.BringToFront(Window);
            if (wasHidden) Window.RefreshStatus();
        }

        public void HideWindow()
        {
            if (!Window.Visible) return;
            Window.Hide();
            if (!settings.TrayTipShown)
            {
                settings.TrayTipShown = true;
                DataStore.TrySave(SettingsFile, settings);
                TipCount++;
                var self = Shortcuts.Self;
                var how = self != null && self.Error == null && !self.Current.IsEmpty ? self.Current.Display + " か、通知領域のアイコンのクリックで開けます。" : "通知領域のアイコンのクリックで開けます。";
                tray.ShowBalloonTip(6000, "Teal Hub は通知領域に常駐しています", how + "\nアプリのショートカットも使えます (各アプリの「設定」で変更)。終了するときは、アイコンを右クリック →「終了」。", ToolTipIcon.Info);
            }
        }

        public void ToggleFromHotkey()
        {
            if (WindowShown && IsForeground()) HideWindow(); else ShowWindow();
        }

        public void ToggleFromTray()
        {
            if (WindowShown) HideWindow(); else ShowWindow();
        }

        bool IsForeground()
        {
            if (ForegroundCheck != null) return ForegroundCheck();
            try { if (AppInfo.IsWindows) return Native.GetForegroundWindow() == Window.Handle; } catch { }
            return Form.ActiveForm == Window;
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            try { Window.Shutdown(); } catch { }
            Cleanup();
            ExitAction();
        }

        void Cleanup()
        {
            try { Shortcuts.Dispose(); } catch { }
            try { if (Updates != null) Updates.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
