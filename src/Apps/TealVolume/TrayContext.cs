// Teal Volume (音量操作) - 常駐部分 (通知領域アイコン・全体ショートカット・画面の開閉)
//  - ×で閉じても終了せず通知領域に隠れる (初回だけ案内を出す)。終了は通知領域のメニューか Teal Hub の「終了」
//  - 全体ショートカット (初期設定 Alt+V、解除も可) で画面を開く / 隠す。値は shortcuts.json の "open" (Teal Hub の一覧からも変更できる)
//  - --startup (ログイン時の自動起動) では画面を出さずに常駐する
using System;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Volume
{
    [DataContract]
    public class VmSettings
    {
        [DataMember(Name = "hotkey", Order = 1)] public string Hotkey = VolumeTray.DefaultHotkey.ToString();
        [DataMember(Name = "trayTipShown", Order = 2)] public bool TrayTipShown;
    }

    public class VolumeTray : ApplicationContext
    {
        public const string SettingsFile = "settings.json";
        /// <summary>初期設定のショートカット</summary>
        public static readonly HotkeySpec DefaultHotkey = HotkeySpec.Of(HotMods.Alt, Keys.V);
        public const string HotkeySlot = "open", HotkeyLabel = "画面を開く / 隠す";

        public readonly MainForm Window;
        public readonly HotkeyController Hotkeys;
        readonly VmSettings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        HotkeySettingsForm settingsForm;
        bool exiting;

        /// <summary>本当に終了するときの処理 (テストでは差し替える)</summary>
        public Action ExitAction = AppHost.RequestExit;
        /// <summary>案内の通知を出した回数 (テスト用)</summary>
        public int TipCount { get; private set; }
        public bool IsExited { get { return exiting; } }
        public HotkeySettingsForm OpenSettings { get { return settingsForm != null && !settingsForm.IsDisposed ? settingsForm : null; } }

        public VolumeTray(string[] args, bool startHidden)
        {
            settings = DataStore.LoadQuiet(SettingsFile, () => new VmSettings());
            Hotkeys = ShortcutStore.CreateOwned(HotkeySlot, HotkeyLabel, DefaultHotkey, settings.Hotkey);
            Hotkeys.Pressed += ToggleFromHotkey;
            Hotkeys.Changed += () =>
            {
                settings.Hotkey = Hotkeys.Current.ToSaved();   // 1.3 に戻したときのために settings.json にも残す
                DataStore.TrySave(SettingsFile, settings);
                UpdateHotkeyText();
            };

            Window = new MainForm();
            Window.HideRequested += HideWindow;
            Window.SettingsRequested += ShowSettings;
            Window.FormClosed += (s, e) => { if (!exiting) ExitApp(); };   // Windows の終了 (シャットダウン) など
            UpdateHotkeyText();

            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
            menu.Items.Add(new ToolStripMenuItem("設定…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            // 1 回のクリックで開く / 隠す。ダブルクリックは必ず開く (1 回目のクリックで隠れても戻す)
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.Visible = true;

            AppHost.ShowRequested = ShowWindow;   // 2 回目の起動・Teal Hub の「表示」
            AppHost.ExitRequested = ExitApp;      // Teal Hub の「終了」
            AppHost.ReloadRequested = ReloadHotkey; // Teal Hub でショートカットを変えたとき
            Application.ApplicationExit += (s, e) => Cleanup();

            if (Hotkeys.Error != null)
                tray.ShowBalloonTip(8000, AppInfo.DisplayName, Hotkeys.Error + "\n通知領域のアイコンを右クリック →「設定」で変更できます。", ToolTipIcon.Warning);

            if (!startHidden) ShowWindow();
            foreach (var a in args ?? new string[0])
            {
                if (a == "/show") ShowWindow();
                else if (a == "/settings") ShowSettings();
            }
        }

        /// <summary>Teal Hub が shortcuts.json を書き換えたら読み直す</summary>
        public void ReloadHotkey()
        {
            Hotkeys.Reload(ShortcutStore.Get(AppInfo.Id, HotkeySlot));
            UpdateHotkeyText();
        }

        string TrayTip()
        {
            if (Hotkeys.Current.IsEmpty) return AppInfo.DisplayName;
            var t = AppInfo.DisplayName + " (" + (Hotkeys.Error == null ? Hotkeys.Current.Display + " で開く" : "ショートカット未登録") + ")";
            return t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void UpdateHotkeyText()
        {
            Window.HotkeyText = Hotkeys.Error == null && !Hotkeys.Current.IsEmpty ? Hotkeys.Current.Display + " で開閉" : null;
            tray.Text = TrayTip();
        }

        // ---------- 画面の開閉 ----------
        public bool WindowShown { get { return Window.Visible && Window.WindowState != FormWindowState.Minimized; } }

        public void ShowWindow()
        {
            if (exiting) return;
            AppHost.BringToFront(Window);   // 隠れていれば表示、最小化なら元に戻して前面へ (表示時に最新の状態に更新)
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
                var how = Hotkeys.Error == null && !Hotkeys.Current.IsEmpty ? Hotkeys.Current.Display + " か、通知領域のアイコンのクリックで開けます。" : "通知領域のアイコンのクリックで開けます。";
                tray.ShowBalloonTip(6000, AppInfo.DisplayName + " は通知領域に常駐しています", how + "\n終了するときは、アイコンを右クリック →「終了」。", ToolTipIcon.Info);
            }
        }

        /// <summary>ショートカット: 前面に出ていれば隠す。隠れている・後ろにあるときは前面に出す。</summary>
        public void ToggleFromHotkey()
        {
            if (WindowShown && IsForeground()) HideWindow(); else ShowWindow();
        }

        /// <summary>通知領域のクリック: 表示中なら隠す、隠れていれば開く</summary>
        public void ToggleFromTray()
        {
            if (WindowShown) HideWindow(); else ShowWindow();
        }

        /// <summary>テスト用 (Windows 以外) に、前面かどうかを差し替える</summary>
        public Func<bool> ForegroundCheck;

        bool IsForeground()
        {
            if (ForegroundCheck != null) return ForegroundCheck();
            try
            {
                if (AppInfo.IsWindows) return Native.GetForegroundWindow() == Window.Handle;
            }
            catch { }
            return Form.ActiveForm == Window;
        }

        public void ShowSettings()
        {
            if (exiting) return;
            if (settingsForm != null && !settingsForm.IsDisposed) { AppHost.BringToFront(settingsForm); return; }
            settingsForm = new HotkeySettingsForm(Hotkeys, "画面を開くショートカット", "Windows にサインインすると、画面を出さずに通知領域に常駐します。");
            settingsForm.FormClosed += (s, e) => { settingsForm = null; UpdateHotkeyText(); };
            settingsForm.Show();
            AppHost.BringToFront(settingsForm);
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            try { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close(); } catch { }
            try { Window.Shutdown(); } catch { }
            Cleanup();
            ExitAction();
        }

        void Cleanup()
        {
            try { Hotkeys.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
