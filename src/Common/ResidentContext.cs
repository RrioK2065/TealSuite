// TealSuite 共通: 画面を持つアプリ (Teal Pass・Teal Memo など) の「常駐」
//  - 常駐オン (Teal Hub の設定ページで切り替え): ×で閉じても終了せず通知領域に残る (初回だけ案内)。
//    スタートアップ (--startup) では画面を出さずに通知領域で待つ。終了は通知領域のメニュー
//  - 常駐オフ (初期設定): ×で閉じると終了 (今までどおり)。--startup では最小化で起動
//  - Teal Hub のショートカット (Hotkey イベント): 後ろにある / 隠れている → 前面へ、前面にある → 隠す (オフなら最小化)
//  - Teal Hub が設定を変えたら (Reload イベント) 読み直す。常駐をオフにしたとき画面が隠れていれば終了する
//  - Notify(): 通知領域のバルーンでお知らせ (1.7.0 から。Teal Sub・Teal Tasks のリマインダー)。
//    常駐オフのときは、お知らせの間だけアイコンを出して、しばらくしたら消す
using System;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace TealSuite.Common
{
    [DataContract]
    public class TrayState
    {
        [DataMember(Name = "trayTipShown", Order = 1)] public bool TrayTipShown;
    }

    public class ResidentContext : ApplicationContext
    {
        public const string StateFile = "tray.json";
        public readonly Form Window;
        readonly bool builtInResident;
        NotifyIcon tray;
        bool exiting, resident;
        TrayState state;

        /// <summary>テスト用: 終了の処理を差し替える</summary>
        public Action ExitAction = AppHost.RequestExit;
        public Func<bool> ForegroundCheck;
        public int TipCount { get; private set; }
        public bool IsExited { get { return exiting; } }
        public bool IsResident { get { return resident; } }
        public bool TrayVisible { get { return tray != null && tray.Visible; } }
        /// <summary>いま動いている常駐の仕組み (アプリに 1 つ)。お知らせを出すときに使う</summary>
        public static ResidentContext Current { get; private set; }
        /// <summary>お知らせ (バルーン) がクリックされたとき。null なら画面を開くだけ</summary>
        public Action BalloonClicked;
        /// <summary>テスト用: 最後に出したお知らせ</summary>
        public string LastNotice { get; private set; }
        public int NoticeCount { get; private set; }
        Timer hideTempTray;

        public ResidentContext(Form window) : this(window, false, AppHost.StartedAtLogin) { }

        /// <param name="builtInResident">Teal Hub で決めていないときの初期設定</param>
        public ResidentContext(Form window, bool builtInResident, bool atLogin)
        {
            Window = window;
            this.builtInResident = builtInResident;
            MainForm = window;
            Current = this;
            state = DataStore.LoadQuiet(StateFile, () => new TrayState());
            resident = ReadResident();
            window.FormClosing += OnClosing;
            window.FormClosed += (s, e) => { if (!exiting) Finish(); };
            AppHost.ShowRequested = ShowWindow;
            AppHost.HotkeyRequested = ToggleFromHotkey;
            AppHost.ExitRequested = ExitApp;
            AppHost.ReloadRequested = Reload;
            UpdateTray();
            if (atLogin && resident) return;   // 通知領域で静かに待つ
            // ログイン時 (常駐オフ) は最小化で起動 (WindowMemory が位置を復元した後に最小化する)
            if (atLogin) window.Load += (s, e) => window.WindowState = FormWindowState.Minimized;
            window.Show();
        }

        bool ReadResident()
        {
            return AppPrefs.IsResident(AppInfo.Id, AppPrefs.DefaultResident(AppInfo.Id, builtInResident, Startup.CurrentExe));
        }

        // MainForm を設定すると、フォームが閉じたときに ApplicationContext が勝手に終わるので、自分で扱う
        protected override void OnMainFormClosed(object sender, EventArgs e) { }

        void UpdateTray()
        {
            if (!resident)
            {
                if (tray != null && (hideTempTray == null || !hideTempTray.Enabled)) tray.Visible = false;
                return;
            }
            if (hideTempTray != null) hideTempTray.Stop();
            EnsureTray();
            tray.Visible = true;
        }

        void EnsureTray()
        {
            if (tray == null)
            {
                tray = new NotifyIcon();
                var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
                menu.Items.Add(new ToolStripMenuItem("開く", null, (s, e) => ShowWindow()) { Font = Theme.Bold });
                menu.Items.Add(new ToolStripMenuItem("設定 (Teal Hub)…", null, (s, e) => HubLink.OpenSettings(null, AppInfo.Id)));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
                tray.ContextMenuStrip = menu;
                try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
                tray.Text = AppInfo.DisplayName.Length > 63 ? AppInfo.DisplayName.Substring(0, 63) : AppInfo.DisplayName;
                tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { if (WindowShown) HideWindow(); else ShowWindow(); } };
                tray.BalloonTipClicked += (s, e) =>
                {
                    if (BalloonClicked != null) BalloonClicked(); else ShowWindow();
                };
            }
        }

        /// <summary>通知領域のバルーンでお知らせする。常駐オフでも、お知らせの間だけアイコンを出す</summary>
        public bool Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (exiting) return false;
            LastNotice = title + "\n" + text;
            NoticeCount++;
            try
            {
                EnsureTray();
                if (!resident)
                {
                    tray.Visible = true;
                    if (hideTempTray == null)
                    {
                        hideTempTray = new Timer { Interval = 15000 };
                        hideTempTray.Tick += (s, e) => { hideTempTray.Stop(); if (!resident && tray != null) tray.Visible = false; };
                    }
                    hideTempTray.Stop(); hideTempTray.Start();
                }
                if (title.Length > 63) title = title.Substring(0, 63);
                if (text.Length > 255) text = text.Substring(0, 252) + "…";
                tray.ShowBalloonTip(10000, title, text, icon);
                return true;
            }
            catch (Exception ex) { DataStore.Log("notify: " + ex.Message); return false; }
        }

        public bool WindowShown { get { return !Window.IsDisposed && Window.Visible && Window.WindowState != FormWindowState.Minimized; } }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (e.Cancel || exiting || !resident || AppHost.IsExiting) return;
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideWindow();
            }
        }

        public void ShowWindow()
        {
            if (exiting || Window.IsDisposed) return;
            AppHost.BringToFront(Window);
        }

        public void HideWindow()
        {
            if (exiting || !Window.Visible) return;
            if (!resident) { Window.WindowState = FormWindowState.Minimized; return; }
            Window.Hide();
            if (!state.TrayTipShown)
            {
                state.TrayTipShown = true;
                DataStore.TrySave(StateFile, state);
                TipCount++;
                try
                {
                    tray.ShowBalloonTip(6000, AppInfo.DisplayName + " は通知領域に常駐しています",
                        "通知領域のアイコンのクリックで開けます。\n終了するときは、アイコンを右クリック →「終了」。常駐をやめるときは Teal Hub の設定から。", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        /// <summary>Teal Hub のショートカット: 前面にあれば隠す、そうでなければ前面へ</summary>
        public void ToggleFromHotkey()
        {
            if (WindowShown && IsForeground()) HideWindow(); else ShowWindow();
        }

        bool IsForeground()
        {
            if (ForegroundCheck != null) return ForegroundCheck();
            try { if (AppInfo.IsWindows) return Native.GetForegroundWindow() == Window.Handle; } catch { }
            return Form.ActiveForm == Window;
        }

        /// <summary>Teal Hub が設定を変えたとき</summary>
        public void Reload()
        {
            if (exiting) return;
            bool now = ReadResident();
            if (now == resident) return;
            resident = now;
            UpdateTray();
            // 常駐をやめたのに画面が隠れたまま → 開く方法がないので終了する
            if (!resident && !Window.Visible) ExitApp();
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                if (!Window.IsDisposed)
                {
                    bool shown = Window.Visible;
                    Window.Close();
                    // 画面側が閉じるのを取りやめた (保存の確認など) ときは終了しない
                    if (shown && !Window.IsDisposed && Window.Visible) { exiting = false; return; }
                }
            }
            catch { }
            Finish();
        }

        void Finish()
        {
            exiting = true;
            try { if (hideTempTray != null) hideTempTray.Stop(); } catch { }
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            ExitAction();
        }
    }
}
