// Teal Volume (音量操作) - 常駐部分 (通知領域アイコン・全体ショートカット・画面の開閉)
//  - 1.6.0: 常駐するかは Teal Hub の設定 (初期設定はオフ)。
//      常駐オン: ×で閉じても終了せず通知領域に隠れる (初回だけ案内を出す)。終了は通知領域のメニューか Teal Hub の「終了」
//      常駐オフ: ×で閉じたら終了する
//  - ショートカット (初期設定 Alt+V) は Teal Hub が受け付け、Hotkey イベントで開く / 隠すを頼んでくる
//  - --startup (ログイン時の自動起動) では画面を出さずに常駐する (常駐オフなら何もせず終了)
using System;
using System.Collections.Generic;
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
        /// <summary>1.8.0: 音量のプリセット</summary>
        [DataMember(Name = "presets", Order = 3, EmitDefaultValue = false)] public List<VolumePreset> Presets;
        /// <summary>1.8.0: 上に固定したアプリ (exe のパス)</summary>
        [DataMember(Name = "pinned", Order = 4, EmitDefaultValue = false)] public List<string> Pinned;
        /// <summary>1.8.0: 一覧に出さないアプリ (exe のパス)</summary>
        [DataMember(Name = "hidden", Order = 5, EmitDefaultValue = false)] public List<string> Hidden;

        public void Normalize()
        {
            if (Presets == null) Presets = new List<VolumePreset>();
            if (Pinned == null) Pinned = new List<string>();
            if (Hidden == null) Hidden = new List<string>();
            Presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            foreach (var p in Presets) if (p.Apps == null) p.Apps = new List<PresetApp>();
        }
    }

    public class VolumeTray : ApplicationContext
    {
        public const string SettingsFile = "settings.json";
        /// <summary>初期設定のショートカット (Teal Hub が受け付ける)</summary>
        public static readonly HotkeySpec DefaultHotkey = HotkeySpec.Of(HotMods.Alt, Keys.V);

        public readonly MainForm Window;
        readonly VmSettings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        bool exiting;

        /// <summary>本当に終了するときの処理 (テストでは差し替える)</summary>
        public Action ExitAction = AppHost.RequestExit;
        /// <summary>案内の通知を出した回数 (テスト用)</summary>
        public int TipCount { get; private set; }
        public bool IsExited { get { return exiting; } }
        /// <summary>ショートカット (表示用。押されたら Teal Hub が Hotkey イベントで知らせてくる)</summary>
        public HotkeySpec Hotkey { get; private set; }
        /// <summary>常駐するか (Teal Hub の設定。初期設定はオフ = 閉じたら終了)</summary>
        public bool Resident { get; private set; }
        public bool TrayVisible { get { return tray.Visible; } }

        public VolumeTray(string[] args, bool startHidden)
        {
            settings = DataStore.LoadQuiet(SettingsFile, () => new VmSettings());
            settings.Normalize();
            LoadPrefs();

            Window = new MainForm();
            Window.Prefs = settings;
            Window.SavePrefs = () => DataStore.TrySave(SettingsFile, settings);
            Window.HideRequested += HideWindow;
            Window.SettingsRequested += ShowSettings;
            Window.FormClosed += (s, e) => { if (!exiting) ExitApp(); };   // Windows の終了 (シャットダウン) など
            UpdateHotkeyText();

            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
            var miMute = new ToolStripMenuItem("すべてのアプリをミュート", null, (s, e) => Window.ToggleMuteAll());
            var miPresets = new ToolStripMenuItem("プリセット");
            menu.Items.Add(miMute);
            menu.Items.Add(miPresets);
            menu.Opening += (s, e) =>
            {
                miMute.Text = Window.AllAppsMuted() ? "すべてのアプリのミュートを解除" : "すべてのアプリをミュート";
                miPresets.DropDownItems.Clear();
                foreach (var p in settings.Presets.ToArray())
                {
                    var pp = p;
                    miPresets.DropDownItems.Add(new ToolStripMenuItem(p.Name, null, (s2, e2) => Window.ApplyPreset(pp)));
                }
                if (settings.Presets.Count == 0) miPresets.DropDownItems.Add(new ToolStripMenuItem("(まだありません。画面の「プリセット」で保存できます)") { Enabled = false });
            };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("設定 (Teal Hub)…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            // 1 回のクリックで開く / 隠す。ダブルクリックは必ず開く (1 回目のクリックで隠れても戻す)
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.Visible = Resident;

            AppHost.ShowRequested = ShowWindow;        // 2 回目の起動・Teal Hub の「表示」
            AppHost.HotkeyRequested = ToggleFromHotkey; // Teal Hub がショートカットを受けたとき
            AppHost.ExitRequested = ExitApp;           // Teal Hub の「終了」
            AppHost.ReloadRequested = Reload;          // Teal Hub でショートカット・常駐を変えたとき
            Application.ApplicationExit += (s, e) => Cleanup();

            if (startHidden && !Resident)
            {
                // ログイン時に起動されたが常駐しない設定 → 何も出さずに終わる (メッセージループが始まってから)
                var t = new Timer { Interval = 50 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); ExitApp(); };
                t.Start();
                return;
            }
            if (!startHidden) ShowWindow();
            foreach (var a in args ?? new string[0])
            {
                if (a == "/show") ShowWindow();
                else if (a == "/settings") ShowSettings();
            }
        }

        void LoadPrefs()
        {
            Hotkey = HotkeySpec.FromSaved(AppPrefs.HotkeyOf(AppInfo.Id), DefaultHotkey);
            string exe = null;
            try { exe = Application.ExecutablePath; } catch { }
            Resident = AppPrefs.IsResident(AppInfo.Id, AppPrefs.DefaultResident(AppInfo.Id, false, exe));
        }

        /// <summary>Teal Hub が設定 (app-settings.json) を変えたら読み直す</summary>
        public void Reload()
        {
            LoadPrefs();
            UpdateHotkeyText();
            if (!exiting) tray.Visible = Resident;
            if (!Resident && !Window.Visible && !exiting) ExitApp();   // 隠れていたのに常駐をやめた
        }

        string TrayTip()
        {
            if (Hotkey.IsEmpty) return AppInfo.DisplayName;
            var t = AppInfo.DisplayName + " (" + Hotkey.Display + " で開く)";
            return t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void UpdateHotkeyText()
        {
            Window.HotkeyText = !Hotkey.IsEmpty ? Hotkey.Display + " で開閉" : null;
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
            if (!Resident)
            {
                // 常駐しない設定: 閉じたら終了 (閉じる処理の途中なので、少しあとで)
                if (Window.IsHandleCreated) Window.BeginInvoke((Action)ExitApp); else ExitApp();
                return;
            }
            if (!settings.TrayTipShown)
            {
                settings.TrayTipShown = true;
                DataStore.TrySave(SettingsFile, settings);
                TipCount++;
                var how = !Hotkey.IsEmpty ? Hotkey.Display + " か、通知領域のアイコンのクリックで開けます。" : "通知領域のアイコンのクリックで開けます。";
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

        /// <summary>設定 (ショートカット・常駐・スタートアップ) は Teal Hub の設定ページで変える</summary>
        public void ShowSettings()
        {
            if (exiting) return;
            HubLink.OpenSettings(Window.Visible ? Window : null, AppInfo.Id);
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
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
