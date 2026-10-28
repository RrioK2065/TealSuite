// Teal Pin (最前面に固定) - 常駐部分 (通知領域アイコン・ショートカット・Windows のできごと・画面の開閉)
//  - 常駐するかは Teal Hub の設定 (初期設定はオン)。自動で固定するルールとタイトルバーのメニューは、起動している間だけ働く
//  - ショートカット (初期設定 Ctrl+Alt+T) は Teal Hub が受け付け、Hotkey イベントで「前面のウィンドウを固定 / 解除」を頼んでくる。
//    Teal Pin が起動していないときは Teal Hub が --show 付きで起動するので、起動したらすぐ前面のウィンドウを固定する
//  - 常駐オフでも、固定中のウィンドウがある間は通知領域に残る (終了すると固定が外れるため)
//  - 終了するときは、固定をすべて外し、枠線を消し、フックを外す。落ちて固定が残ったときは、次に起動したときに外す (pinned.json)
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pin
{
    public class PinTray : ApplicationContext
    {
        /// <summary>
        /// 初期設定のショートカット。Ctrl+Alt+T にした理由: T は「Top (最前面)」。Windows 自体・Office・ブラウザーでは Ctrl+Alt+T を使っておらず、
        /// ほかのアプリともぶつかりにくい (Ctrl+T・Ctrl+Shift+T はブラウザーのタブで使われるので避けた)。PowerToys の「常に手前に表示」の Win+Ctrl+T とも重ならない
        /// </summary>
        public static readonly HotkeySpec DefaultHotkey = HotkeySpec.Of(HotMods.Ctrl | HotMods.Alt, Keys.T);

        public readonly MainForm Window;
        public readonly PinManager Manager;
        public readonly bool Demo;
        readonly PinSettings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer timer = new Timer { Interval = 250 };
        WinEventSource events;
        TitleBarMenu titleBar;
        PinToast toast;
        bool exiting, cleaned;
        ToolStripMenuItem miPinFront, miUnpinAll, miTitleBar, miBorder;
        ToolStripSeparator sepPins;
        readonly ContextMenuStrip menu;

        public Action ExitAction = AppHost.RequestExit;
        public int TipCount { get; private set; }
        public bool IsExited { get { return exiting; } }
        public HotkeySpec Hotkey { get; private set; }
        public bool Resident { get; private set; }
        public bool TrayVisible { get { return tray.Visible; } }
        public ContextMenuStrip TrayMenu { get { return menu; } }
        /// <summary>最後のお知らせ (テスト用)</summary>
        public string LastNotice { get; private set; }
        public bool TitleBarActive { get { return titleBar != null && titleBar.Enabled; } }
        public bool EventsActive { get { return events != null; } }
        public int Recovered { get; private set; }

        public PinTray(string[] args, bool startHidden) : this(args, startHidden, null) { }

        /// <summary>api を渡すとそれを使う (テスト用)。null なら Windows では本物、それ以外ではデモ</summary>
        public PinTray(string[] args, bool startHidden, IWindowApi testApi)
        {
            settings = PinStore.Load();
            LoadPrefs();
            Demo = testApi != null || !AppInfo.IsWindows || Environment.GetEnvironmentVariable("TEALSUITE_DEMO") == "1";
            IWindowApi api = testApi ?? (Demo ? (IWindowApi)FakeWindows.Demo() : new Win32Windows());
            Func<IBorder> borders;
            if (Demo) borders = () => new FakeBorder(); else borders = () => new StripBorder();
            int selfPid = 0;
            try { selfPid = Process.GetCurrentProcess().Id; } catch { }
            if (Demo && testApi == null) selfPid = -1;

            // 前回落ちて残った固定を外す
            try
            {
                var rec = DataStore.LoadQuiet(PinStore.RecoveryFile, () => new PinRecordFile());
                Recovered = PinManager.Recover(api, rec);
                if (rec.Pinned.Count > 0) DataStore.TrySave(PinStore.RecoveryFile, new PinRecordFile());
            }
            catch (Exception ex) { DataStore.Log("recover: " + ex.Message); }

            Manager = new PinManager(api, borders, settings, selfPid);
            Manager.Notice = ShowNotice;
            Manager.SaveRecords = f => DataStore.TrySave(PinStore.RecoveryFile, f);
            Manager.Changed += OnPinsChanged;

            Window = new MainForm(Manager);
            Window.SaveSettings = () => PinStore.Save(settings);
            Window.SettingsChanged = ApplySettings;
            Window.HideRequested += HideWindow;
            Window.SettingsRequested += ShowSettings;
            Window.FormClosed += (s, e) => { if (!exiting) ExitApp(); };
            Window.HotkeyText = Hotkey.IsEmpty ? null : Hotkey.Display;

            menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = true };
            BuildMenu();
            menu.Opening += (s, e) => BuildMenu();
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFromTray(); };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

            AppHost.ShowRequested = ShowWindow;
            AppHost.HotkeyRequested = () => PinForeground();
            AppHost.ExitRequested = ExitApp;
            AppHost.ReloadRequested = Reload;
            Application.ApplicationExit += (s, e) => Cleanup();

            if (startHidden && !Resident)
            {
                var t = new Timer { Interval = 50 };
                t.Tick += (s, e) => { t.Stop(); t.Dispose(); ExitApp(); };
                t.Start();
                return;
            }

            if (!Demo)
            {
                try
                {
                    toast = new PinToast();
                    events = new WinEventSource();
                    events.OnWindow = (ev, h) => Manager.OnEvent(ev, h, DateTime.Now);
                    events.OnMenu = (ev, h) => { if (titleBar != null) titleBar.OnMenu(ev, h); };
                    if (!events.Start()) { DataStore.Log("SetWinEventHook failed"); events.Dispose(); events = null; }
                    titleBar = new TitleBarMenu(Manager, (Win32Windows)api, a => AppHost.OnUi(a), selfPid);
                }
                catch (Exception ex) { DataStore.Log("hooks: " + ex); }
            }
            ApplySettings();
            timer.Tick += (s, e) => { try { Manager.Tick(DateTime.Now); } catch (Exception ex) { DataStore.Log("tick: " + ex.Message); } };
            timer.Start();
            if (settings.RulesOnStart) { try { Manager.ScanRules(); } catch (Exception ex) { DataStore.Log("rules: " + ex.Message); } }
            UpdateTrayVisible();

            if (AppHost.ShowByHotkey) PinForeground();   // Teal Hub のショートカットで起動された
            else if (!startHidden) ShowWindow();
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
            Resident = AppPrefs.IsResident(AppInfo.Id, AppPrefs.DefaultResident(AppInfo.Id, true, exe));
        }

        public void Reload()
        {
            LoadPrefs();
            if (Window != null && !Window.IsDisposed) Window.HotkeyText = Hotkey.IsEmpty ? null : Hotkey.Display;
            tray.Text = TrayTip();
            UpdateTrayVisible();
            MaybeExitWhenIdle();
        }

        /// <summary>タイトルバーのメニュー・枠線の設定を反映する</summary>
        public void ApplySettings()
        {
            if (titleBar != null) titleBar.SetEnabled(settings.TitleBarMenu);
            Manager.RefreshBorders();
        }

        string TrayTip()
        {
            var t = AppInfo.DisplayName;
            if (Manager != null && Manager.Count > 0) t += " (" + Manager.Count + " 個を固定中)";
            else if (!Hotkey.IsEmpty) t += " (" + Hotkey.Display + " で固定)";
            return t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void UpdateTrayVisible()
        {
            if (exiting) return;
            tray.Visible = Resident || Manager.Count > 0 || Window.Visible;
            tray.Text = TrayTip();
        }

        void OnPinsChanged()
        {
            tray.Text = TrayTip();
            UpdateTrayVisible();
            MaybeExitWhenIdle();
        }

        /// <summary>常駐オフで、画面も閉じていて、固定もないなら終わる</summary>
        void MaybeExitWhenIdle()
        {
            if (exiting || Resident || Window.Visible || Manager.Count > 0) return;
            if (Window.IsHandleCreated) Window.BeginInvoke((Action)ExitApp); else ExitApp();
        }

        // ---------------------------------------------------------------- お知らせ
        void ShowNotice(string msg, IntPtr h, bool isError)
        {
            LastNotice = msg;
            if (Window.Visible) Window.Flash(msg);
            if (!isError && !settings.Toast) return;
            if (toast == null) return;
            try
            {
                var api = Manager.Api;
                Rectangle frame = Rectangle.Empty, work = Rectangle.Empty;
                double scale = 1;
                if (h != IntPtr.Zero && api.IsWindow(h) && !api.IsMinimized(h))
                {
                    frame = api.FrameBounds(h); work = api.WorkArea(h); scale = api.Scale(h);
                }
                else
                {
                    var c = Win32Windows.CursorPhysical();
                    work = Win32Windows.WorkAreaAt(c); scale = Win32Windows.ScaleAt(c);
                }
                toast.Show(msg, frame, work, scale, isError);
            }
            catch (Exception ex) { DataStore.Log("toast: " + ex.Message); }
        }

        // ---------------------------------------------------------------- 固定
        /// <summary>ショートカット: 前面のウィンドウを固定 / 解除</summary>
        public PinResult PinForeground()
        {
            if (exiting) return PinResult.Fail("終了中です");
            var r = Manager.ToggleForeground(PinManager.HowHotkey);
            UpdateTrayVisible();
            return r;
        }

        /// <summary>通知領域のメニューを作り直す (開くたびに自動で。テスト用にも)</summary>
        public void RefreshMenu() { BuildMenu(); }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
            menu.Items.Add(new ToolStripSeparator());

            var last = Manager.LastForeground;
            string lastTitle = null;
            if (last != IntPtr.Zero && Manager.Api.IsWindow(last) && !Manager.IsPinned(last) && Manager.Check(last) == null) lastTitle = Manager.Api.Title(last);
            if (!string.IsNullOrEmpty(lastTitle))
            {
                var t = lastTitle.Length > 34 ? lastTitle.Substring(0, 34) + "…" : lastTitle;
                var hh = last;
                miPinFront = new ToolStripMenuItem("📌 「" + t + "」を最前面に固定", null, (s, e) => Manager.Pin(hh, PinManager.HowTray, null));
            }
            else miPinFront = new ToolStripMenuItem("📌 前面のウィンドウを固定 (" + (Hotkey.IsEmpty ? "ショートカット未設定" : Hotkey.Display) + ")") { Enabled = false };
            menu.Items.Add(miPinFront);

            if (Manager.Count > 0)
            {
                sepPins = new ToolStripSeparator();
                menu.Items.Add(sepPins);
                menu.Items.Add(new ToolStripMenuItem("固定中のウィンドウ") { Enabled = false });
                foreach (var pw in Manager.Pins.ToList())
                {
                    var p = pw;
                    var title = p.Title.Length > 40 ? p.Title.Substring(0, 40) + "…" : p.Title;
                    var mi = new ToolStripMenuItem(title, Window.IconFor(p.ExePath, p.AppName));
                    mi.DropDownItems.Add(new ToolStripMenuItem("前面に出す", null, (s, e) => Manager.Activate(p.Handle)));
                    mi.DropDownItems.Add(new ToolStripMenuItem("固定を外す", null, (s, e) => Manager.Unpin(p.Handle)));
                    var sizes = new ToolStripMenuItem("位置とサイズ");
                    foreach (var pr in settings.AllPresets())
                    {
                        var ppr = pr;
                        sizes.DropDownItems.Add(new ToolStripMenuItem(pr.Name, null, (s, e) => Manager.ApplyPreset(p.Handle, ppr)));
                    }
                    mi.DropDownItems.Add(sizes);
                    menu.Items.Add(mi);
                }
            }
            miUnpinAll = new ToolStripMenuItem("すべての固定を外す", null, (s, e) => Manager.UnpinAll()) { Enabled = Manager.Count > 0 };
            menu.Items.Add(miUnpinAll);
            menu.Items.Add(new ToolStripSeparator());
            miTitleBar = new ToolStripMenuItem("タイトルバーの右クリックメニュー", null, (s, e) => { settings.TitleBarMenu = !settings.TitleBarMenu; SaveAndApply(); }) { Checked = settings.TitleBarMenu };
            miBorder = new ToolStripMenuItem("ティールの枠線", null, (s, e) => { settings.ShowBorder = !settings.ShowBorder; SaveAndApply(); }) { Checked = settings.ShowBorder };
            menu.Items.Add(miTitleBar);
            menu.Items.Add(miBorder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("設定 (Teal Hub)…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripMenuItem("終了 (固定はすべて外れます)", null, (s, e) => ExitApp()));
        }

        void SaveAndApply()
        {
            PinStore.Save(settings);
            ApplySettings();
            if (Window != null && !Window.IsDisposed) Window.ReloadSettings();
        }

        // ---------------------------------------------------------------- 画面の開閉
        public bool WindowShown { get { return Window.Visible && Window.WindowState != FormWindowState.Minimized; } }

        public void ShowWindow()
        {
            if (exiting) return;
            AppHost.BringToFront(Window);
            UpdateTrayVisible();
        }

        public void HideWindow()
        {
            if (!Window.Visible) return;
            Window.Hide();
            if (!Resident && Manager.Count == 0)
            {
                if (Window.IsHandleCreated) Window.BeginInvoke((Action)ExitApp); else ExitApp();
                return;
            }
            UpdateTrayVisible();
            if (!Resident)
            {
                TipCount++;
                tray.ShowBalloonTip(6000, AppInfo.DisplayName + " は通知領域に残っています", "固定中のウィンドウがあるためです。固定をすべて外すと終了します。", ToolTipIcon.Info);
                return;
            }
            if (!settings.TrayTipShown)
            {
                settings.TrayTipShown = true;
                PinStore.Save(settings);
                TipCount++;
                var how = !Hotkey.IsEmpty ? Hotkey.Display + " で前面のウィンドウを固定できます。" : "タイトルバーの右クリックで固定できます。";
                tray.ShowBalloonTip(6000, AppInfo.DisplayName + " は通知領域に常駐しています", how + "\n終了するときは、アイコンを右クリック →「終了」。", ToolTipIcon.Info);
            }
        }

        public void ToggleFromTray()
        {
            if (WindowShown) HideWindow(); else ShowWindow();
        }

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

        /// <summary>固定をすべて外し、枠線・フックを片づける (何度呼んでもよい)</summary>
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { timer.Stop(); timer.Dispose(); } catch { }
            try { if (titleBar != null) titleBar.Dispose(); } catch { }
            try { if (events != null) events.Dispose(); } catch { }
            try { Manager.Dispose(); } catch { }   // UnpinAll + 枠線を消す
            try { DataStore.TrySave(PinStore.RecoveryFile, new PinRecordFile()); } catch { }
            try { if (toast != null) toast.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
