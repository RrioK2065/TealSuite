// Teal Macro (連打＆マクロ) - 常駐部分 (通知領域アイコン・ショートカット・画面の開閉・実行中の小さな表示)
//  - 常駐するかは Teal Hub の設定 (初期設定はオン)。常駐オフでも、連打・再生・記録の間は通知領域に残る
//  - ショートカットは Teal Hub が受け付ける。1 つめ (連打の開始 / 停止、初期設定 Ctrl+Alt+R) は Hotkey イベント、
//    2 つめ以降 (位置を記録・マクロの記録・すべて停止・マクロごと) は Hotkey.<slot> イベントで頼んでくる。
//    起動していないときは Teal Hub が --show / --slot=<slot> 付きで起動するので、起動したらすぐその動きをする
//  - 終了するときは、実行中のものを止め (押したままのキー・ボタンは離す)、フックを外す
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public class MacroTray : ApplicationContext
    {
        public readonly MainForm Window;
        public readonly MacroCore Core;
        public readonly bool Demo;
        public readonly MacroSettings Settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu;
        readonly SlotEvents slotEvents;
        RunBar runBar;
        bool exiting, cleaned, hiddenForRun;
        FormWindowState stateBeforeHide = FormWindowState.Normal;

        public Action ExitAction = AppHost.RequestExit;
        public bool Resident { get; private set; }
        public bool IsExited { get { return exiting; } }
        public bool TrayVisible { get { return tray.Visible; } }
        public ContextMenuStrip TrayMenu { get { return menu; } }
        public string LastNotice { get; private set; }
        public int TipCount { get; private set; }
        public RunBar Bar { get { return runBar; } }
        public IList<string> ListenedSlots { get { return slotEvents.Slots; } }

        public MacroTray(string[] args, bool startHidden) : this(args, startHidden, null, null) { }

        /// <summary>api・hook を渡すとそれを使う (テスト用)。null なら Windows では本物、それ以外ではデモ</summary>
        public MacroTray(string[] args, bool startHidden, IInputApi testApi, IHookSource testHook)
        {
            Settings = MacroStore.LoadSettings();
            var macros = MacroStore.LoadMacros();
            Demo = testApi != null || !AppInfo.IsWindows || Environment.GetEnvironmentVariable("TEALSUITE_DEMO") == "1";
            IInputApi api = testApi ?? (Demo ? (IInputApi)DemoInput() : new Win32Input());
            IHookSource hook = testHook ?? (Demo ? (IHookSource)new FakeHook() : new InputHook());
            Core = new MacroCore(Settings, macros, api, hook);
            Core.Post = a => AppHost.OnUi(a);
            if (testApi != null) Core.Post = a => a();
            var ih = hook as InputHook;
            if (ih != null) ih.StopPressed = Core.OnHookStop;
            Core.Notice = ShowNotice;
            Core.HideMain = HideForRun;
            Core.Changed += OnCoreChanged;
            Core.MacrosChanged += SyncSlots;
            if (!Demo) Core.PickOnScreen = PickOnScreen;
            LoadPrefs();

            Window = new MainForm(Core);
            Window.HideRequested += HideWindow;
            Window.SettingsRequested += ShowSettings;
            Window.FormClosed += (s, e) => { if (!exiting) ExitApp(); };

            menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            BuildMenu();
            menu.Opening += (s, e) => BuildMenu();
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };

            slotEvents = new SlotEvents(AppInfo.Id);
            slotEvents.Pressed = s => Core.OnHotkey(s);
            if (testApi != null) slotEvents.Post = a => a();

            AppHost.ShowRequested = ShowWindow;
            AppHost.HotkeyRequested = () => Core.OnHotkey(null);
            AppHost.ExitRequested = ExitApp;
            AppHost.ReloadRequested = Reload;
            Application.ApplicationExit += (s, e) => Cleanup();

            var startSlot = HotkeySlots.StartSlot(args ?? new string[0]);
            if (startHidden && !Resident && startSlot == null)
            {
                var t = new Timer { Interval = 50 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); ExitApp(); };
                t.Start();
                return;
            }

            try { MacroStore.PublishSlots(Core.Macros); } catch (Exception ex) { DataStore.Log("slots: " + ex.Message); }
            SyncSlots();
            UpdateTrayVisible();

            if (startSlot != null) Core.OnHotkey(startSlot);         // Teal Hub の 2 つめ以降のショートカットで起動された
            else if (AppHost.ShowByHotkey) Core.OnHotkey(null);     // 1 つめ (連打の開始 / 停止) で起動された
            else if (!startHidden) ShowWindow();
            foreach (var a in args ?? new string[0])
            {
                if (a == "/show") ShowWindow();
                else if (a == "/settings") ShowSettings();
                else if (a == "/macro") { ShowWindow(); Window.ShowPage(1); }
            }
        }

        /// <summary>デモ (Windows 以外・TEALSUITE_DEMO=1): 本物の入力はしない。画面の見本用のウィンドウを 2 つ置く</summary>
        static FakeInput DemoInput()
        {
            var f = new FakeInput();
            f.AddWindow("notepad.exe", "Notepad", "メモ帳", new Rectangle(200, 150, 800, 600));
            f.AddWindow("chrome.exe", "Chrome_WidgetWin_1", "ブラウザー", new Rectangle(1000, 100, 900, 800));
            return f;
        }

        void LoadPrefs()
        {
            string exe = null;
            try { exe = Application.ExecutablePath; } catch { }
            Resident = AppPrefs.IsResident(AppInfo.Id, AppPrefs.DefaultResident(AppInfo.Id, true, exe));
            Core.ReloadHotkeys();
        }

        public void Reload()
        {
            LoadPrefs();
            if (Window != null && !Window.IsDisposed) Window.RefreshAll();
            tray.Text = TrayTip();
            UpdateTrayVisible();
            MaybeExitWhenIdle();
        }

        /// <summary>受け付けるショートカット (Teal Hub からの Hotkey.<slot>) を、今あるマクロに合わせる</summary>
        public void SyncSlots()
        {
            var slots = new List<string> { MacroCore.SlotPick, MacroCore.SlotRecord, MacroCore.SlotStop };
            slots.AddRange(Core.Macros.Macros.Select(m => m.Slot));
            try { slotEvents.Sync(slots); } catch (Exception ex) { DataStore.Log("slot events: " + ex.Message); }
        }

        string TrayTip()
        {
            var t = AppInfo.DisplayName;
            if (Core.IsBusy) t += " (" + Core.StateTitle + ")";
            else if (!Core.ToggleKey.IsEmpty) t += " (" + Core.ToggleKey.Display + " で連打)";
            return t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void UpdateTrayVisible()
        {
            if (exiting) return;
            tray.Visible = Resident || Core.IsBusy || Window.Visible;
            tray.Text = TrayTip();
        }

        void MaybeExitWhenIdle()
        {
            if (exiting || Resident || Window.Visible || Core.IsBusy || hiddenForRun) return;
            if (Window.IsHandleCreated) Window.BeginInvoke((Action)ExitApp); else ExitApp();
        }

        // ---------------------------------------------------------------- 状態
        CoreState lastState = CoreState.Idle;
        void OnCoreChanged()
        {
            if (exiting) return;
            var st = Core.State;
            if (st != CoreState.Idle)
            {
                if (runBar == null || runBar.IsDisposed)
                {
                    runBar = new RunBar();
                    runBar.StopClicked += () => Core.StopAll(RunJob.ReasonUser);
                }
                runBar.SetState(st, Core.StateTitle, Core.StateDetail);
                if (!runBar.Visible) { runBar.PlaceBottomRight(); try { runBar.Show(); } catch { } }
            }
            else if (runBar != null && runBar.Visible) runBar.Hide();
            if (st != lastState)
            {
                lastState = st;
                tray.Text = TrayTip();
                UpdateTrayVisible();
                if (st == CoreState.Idle) MaybeExitWhenIdle();
            }
        }

        /// <summary>マクロの記録・再生のあいだ画面を最小化する / 戻す</summary>
        void HideForRun(bool hide)
        {
            if (Window == null || Window.IsDisposed) return;
            if (hide)
            {
                if (hiddenForRun || !Window.Visible || Window.WindowState == FormWindowState.Minimized) return;
                hiddenForRun = true;
                stateBeforeHide = Window.WindowState;
                Window.WindowState = FormWindowState.Minimized;
            }
            else
            {
                if (!hiddenForRun) return;
                hiddenForRun = false;
                if (Window.Visible && Window.WindowState == FormWindowState.Minimized) Window.WindowState = stateBeforeHide;
            }
        }

        void PickOnScreen(Action<TargetPoint> done)
        {
            bool wasVisible = Window.Visible && Window.WindowState != FormWindowState.Minimized;
            if (wasVisible) Window.WindowState = FormWindowState.Minimized;
            var t = new Timer { Interval = 250 };   // 最小化のアニメーションが終わってから
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                var ov = new PickOverlay(Core.Api, p =>
                {
                    if (wasVisible) { Window.WindowState = FormWindowState.Normal; AppHost.BringToFront(Window); }
                    done(p);
                });
                ov.Show();
            };
            t.Start();
        }

        void ShowNotice(string msg, bool isError)
        {
            LastNotice = msg;
            if (Window != null && !Window.IsDisposed) Window.Flash(msg, isError);
            // 画面が見えていないとき (ショートカットで動かしたとき) は通知領域からお知らせ
            bool visible = Window != null && Window.Visible && Window.WindowState != FormWindowState.Minimized;
            if (!visible && tray.Visible && !Core.IsBusy)
            {
                TipCount++;
                try { tray.ShowBalloonTip(4000, AppInfo.DisplayName, msg, isError ? ToolTipIcon.Warning : ToolTipIcon.Info); } catch { }
            }
        }

        // ---------------------------------------------------------------- メニュー
        public void RefreshMenu() { BuildMenu(); }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
            menu.Items.Add(new ToolStripSeparator());
            bool clicking = Core.State == CoreState.Clicking || Core.CountdownIsClick;
            menu.Items.Add(new ToolStripMenuItem((clicking ? "■ 連打を止める" : "▶ 連打を始める") + Keys_(Core.ToggleKey), null, (s, e) => Core.ToggleClicker(false)) { Enabled = Core.State != CoreState.Recording });
            menu.Items.Add(new ToolStripMenuItem("📍 今のマウスの位置を記録" + Keys_(Core.PickKey), null, (s, e) => Core.AddPointFromCursor()));
            bool rec = Core.State == CoreState.Recording;
            menu.Items.Add(new ToolStripMenuItem((rec ? "■ 記録を終える" : "● マクロを記録") + Keys_(Core.RecordKey), null, (s, e) => Core.ToggleRecording()));
            var play = new ToolStripMenuItem("▶ マクロを再生") { Enabled = Core.Macros.Macros.Count > 0 && !rec };
            foreach (var m in Core.Macros.Macros.Take(30))
            {
                var mm = m;
                play.DropDownItems.Add(new ToolStripMenuItem(m.Name + Keys_(Core.MacroKey(m)), null, (s, e) => Core.PlayMacro(mm.Id, false)) { Enabled = m.Steps.Count > 0 });
            }
            menu.Items.Add(play);
            menu.Items.Add(new ToolStripMenuItem("■ すべて停止" + Keys_(Core.StopKey), null, (s, e) => Core.StopAll(RunJob.ReasonUser)) { Enabled = Core.IsBusy });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("設定 (Teal Hub)…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
        }

        static string Keys_(HotkeySpec k) { return k.IsEmpty ? "" : "\t" + k.Display; }

        // ---------------------------------------------------------------- 画面の開閉
        public bool WindowShown { get { return Window.Visible && Window.WindowState != FormWindowState.Minimized; } }

        public void ShowWindow()
        {
            if (exiting) return;
            hiddenForRun = false;
            AppHost.BringToFront(Window);
            UpdateTrayVisible();
        }

        public void HideWindow()
        {
            if (!Window.Visible) return;
            Window.Hide();
            if (!Resident && !Core.IsBusy)
            {
                if (Window.IsHandleCreated) Window.BeginInvoke((Action)ExitApp); else ExitApp();
                return;
            }
            UpdateTrayVisible();
            if (Resident && !Settings.TrayTipShown)
            {
                Settings.TrayTipShown = true;
                Core.SaveSettings();
                TipCount++;
                var how = Core.ToggleKey.IsEmpty ? "" : Core.ToggleKey.Display + " で、どの画面からでも連打を開始 / 停止できます。";
                try { tray.ShowBalloonTip(6000, AppInfo.DisplayName + " は通知領域に常駐しています", how + "\n終了するときは、アイコンを右クリック →「終了」。", ToolTipIcon.Info); } catch { }
            }
        }

        public void ToggleFromTray() { if (WindowShown) HideWindow(); else ShowWindow(); }

        public void ShowSettings()
        {
            if (exiting) return;
            HubLink.OpenSettings(Window.Visible ? Window : null, AppInfo.Id);
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            Cleanup();
            try { Window.Shutdown(); } catch { }
            ExitAction();
        }

        /// <summary>実行中のものを止め、フック・イベント・表示を片づける (何度呼んでもよい)</summary>
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { if (Core.State == CoreState.Recording) Core.StopRecording(); } catch { }
            try { Core.Dispose(); } catch { }
            try { slotEvents.Dispose(); } catch { }
            try { if (runBar != null) runBar.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
