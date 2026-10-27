// Teal Clip (コピー履歴) - 常駐部分 (通知領域アイコン・監視・ショートカット・貼り付け)
//  1.6.0: ショートカットは Teal Hub が受け付ける (押されると Hotkey イベントでポップアップを出す)
//         常駐 (初期設定オン) をオフにすると、通知領域に残らず、画面を閉じたら終了する
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    public class TrayContext : ApplicationContext
    {
        public readonly History History;
        readonly ChSettings settings;
        readonly ClipboardWatcher watcher;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer readTimer = new System.Windows.Forms.Timer();
        /// <summary>常駐しないとき: 画面がすべて閉じたら少し待って終了する</summary>
        readonly System.Windows.Forms.Timer exitTimer = new System.Windows.Forms.Timer();
        PopupForm popup;
        HistoryForm manager;
        uint ownSeq;
        string ownText;
        DateTime ownTime;
        bool exiting;
        public const string SettingsFile = "settings.json";
        /// <summary>常駐しないとき、画面が閉じてから終了するまで (ミリ秒)</summary>
        public const int AutoExitMs = 1500;

        /// <summary>初期設定のショートカット</summary>
        public static readonly HotkeySpec DefaultHotkey = HotkeySpec.Of(HotMods.Ctrl | HotMods.Shift, Keys.V);
        /// <summary>ショートカットは Teal Hub が受け付ける (押されると Hotkey イベントで知らせてくる)。ここでは表示に使うだけ</summary>
        public HotkeySpec Hotkey { get; private set; }
        /// <summary>常駐するか (Teal Hub の設定。初期設定はオン)</summary>
        public bool Resident { get; private set; }
        public bool TrayVisible { get { return tray.Visible; } }
        public bool IsExited { get { return exiting; } }
        /// <summary>テスト用: 終了のしかた (既定は AppHost.RequestExit)</summary>
        public Action ExitAction = AppHost.RequestExit;
        public HistoryForm Manager { get { return manager != null && !manager.IsDisposed ? manager : null; } }
        public PopupForm Popup { get { return popup != null && !popup.IsDisposed ? popup : null; } }

        public TrayContext(string[] args)
        {
            settings = DataStore.LoadQuiet(SettingsFile, () => new ChSettings());
            History = HistoryStore.Load();
            if (Environment.GetEnvironmentVariable("TEALSUITE_DEMO") == "1" && History.Items.Count == 0) SeedDemo();
            History.Changed += () => { HistoryStore.Save(History); if (manager != null && !manager.IsDisposed) manager.RefreshList(); };
            LoadPrefs();

            watcher = new ClipboardWatcher();
            watcher.ClipboardChanged += OnClipboardChanged;
            readTimer.Interval = 80;
            readTimer.Tick += (s, e) => { readTimer.Stop(); ReadClipboard(); };
            exitTimer.Interval = AutoExitMs;
            exitTimer.Tick += (s, e) => { exitTimer.Stop(); CheckAutoExit(); };

            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            var miShow = new ToolStripMenuItem("履歴を表示", null, (s, e) => ShowManager()) { Font = Theme.Bold };
            menu.Items.Add(miShow);
            menu.Items.Add(new ToolStripMenuItem("設定 (Teal Hub)…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowManager(); };
            tray.Visible = Resident;

            AppHost.ShowRequested = ShowManager;
            AppHost.HotkeyRequested = TogglePopup;   // Teal Hub がショートカットを受けたとき
            AppHost.ExitRequested = ExitApp;
            AppHost.ReloadRequested = Reload;        // Teal Hub でショートカット・常駐を変えたとき
            Application.ApplicationExit += (s, e) => Cleanup();

            bool any = false;
            foreach (var a in args ?? new string[0])
            {
                if (a == "/show") { ShowManager(); any = true; }
                else if (a == "/popup" || string.Equals(a, AppPrefs.ShowArg, StringComparison.OrdinalIgnoreCase)) { TogglePopup(); any = true; }
                else if (a == "/settings") { ShowSettings(); }
            }
            if (!Resident)
            {
                // 常駐しないときは、画面を出して、閉じたら終了する
                // (ログイン時に起動されても常駐しないなら、何も出さずに終わる。メッセージループが始まってから終了する)
                if (AppHost.StartedAtLogin || (args != null && Startup.HasStartupArg(args))) { exitTimer.Interval = 50; exitTimer.Start(); return; }
                if (!any) ShowManager();
                return;
            }
            if (!settings.FirstRunTipShown && !AppHost.StartedAtLogin)   // ログイン時の自動起動では案内を出さない
            {
                tray.ShowBalloonTip(6000, AppInfo.DisplayName + " (コピー履歴) を起動しました", "通知領域に常駐しています。" + (Hotkey.IsEmpty ? "アイコンのクリックで履歴を表示できます。" : Hotkey.Display + " で履歴を呼び出せます (Teal Hub が起動しているとき)。"), ToolTipIcon.Info);
                settings.FirstRunTipShown = true;
                DataStore.TrySave(SettingsFile, settings);
            }
        }

        void LoadPrefs()
        {
            Hotkey = HotkeySpec.FromSaved(AppPrefs.HotkeyOf(AppInfo.Id), DefaultHotkey);
            Resident = AppPrefs.IsResident(AppInfo.Id, true);
        }

        /// <summary>Teal Hub が設定 (app-settings.json) を変えたら読み直す</summary>
        public void Reload()
        {
            LoadPrefs();
            tray.Text = TrayTip();
            tray.Visible = Resident && !exiting;
            if (manager != null && !manager.IsDisposed) manager.UpdateSubtitle();
            if (!Resident) ScheduleAutoExit();
        }

        string TrayTip()
        {
            if (Hotkey.IsEmpty) return AppInfo.DisplayName;
            var t = AppInfo.DisplayName + " (" + Hotkey.Display + ")";
            return t.Length > 63 ? t.Substring(0, 63) : t;
        }

        /// <summary>常駐しないとき: 画面が全部隠れていたら終了の準備</summary>
        void ScheduleAutoExit()
        {
            if (Resident || exiting) return;
            exitTimer.Stop(); exitTimer.Interval = AutoExitMs; exitTimer.Start();
        }
        public bool AutoExitPending { get { return exitTimer.Enabled; } }

        /// <summary>常駐しないのに、どの画面も出ていなければ終了する。終了したら true</summary>
        public bool CheckAutoExit()
        {
            if (Resident || exiting) return false;
            bool shown = (manager != null && !manager.IsDisposed && manager.Visible) || (popup != null && !popup.IsDisposed && popup.Visible);
            if (shown) return false;
            ExitApp();
            return true;
        }

        void SeedDemo()
        {
            var now = DateTime.Now;
            var demo = new[] { "会議は 15:00 から第2会議室です", "https://example.com/report/2026-10", "〒100-0001 東京都千代田区千代田1-1",
                "お世話になっております。\r\n資料を添付いたしますのでご確認ください。", "SELECT * FROM orders WHERE status = 'open';", "ありがとうございます！", "tanaka@example.co.jp" };
            for (int i = demo.Length - 1; i >= 0; i--) History.Add(demo[i], now.AddMinutes(-i * 7));
            History.SetPinned(History.Items.First(x => x.Text.StartsWith("〒")), true);
        }

        // ---------- 監視 ----------
        void OnClipboardChanged()
        {
            if (AppInfo.IsWindows && ownSeq != 0 && W32.GetClipboardSequenceNumber() == ownSeq) return; // 自分で置いたもの
            readTimer.Stop(); readTimer.Start(); // 連続した通知はまとめる
        }

        static uint fmtExclude, fmtViewerIgnore, fmtCanInclude;

        /// <summary>パスワード管理アプリなどが「記録しないで」と印を付けた内容か</summary>
        static bool IsMarkedPrivate()
        {
            if (!AppInfo.IsWindows) return false;
            if (fmtExclude == 0)
            {
                fmtExclude = W32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
                fmtViewerIgnore = W32.RegisterClipboardFormat("Clipboard Viewer Ignore");
                fmtCanInclude = W32.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            }
            if (W32.IsClipboardFormatAvailable(fmtExclude) || W32.IsClipboardFormatAvailable(fmtViewerIgnore)) return true;
            if (W32.IsClipboardFormatAvailable(fmtCanInclude))
            {
                try
                {
                    var o = Clipboard.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                    if (o != null && o.Length >= 4) { var b = o.ToArray(); if (BitConverter.ToInt32(b, 0) == 0) return true; }
                }
                catch { }
            }
            return false;
        }

        void ReadClipboard()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (IsMarkedPrivate()) return;
                    if (!Clipboard.ContainsText(TextDataFormat.UnicodeText) && !Clipboard.ContainsText()) return;
                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (string.IsNullOrEmpty(text)) text = Clipboard.GetText();
                    if (text == ownText && (DateTime.Now - ownTime).TotalSeconds < 3) return;
                    History.Add(text);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(40); } // 他アプリが使用中
                catch (Exception ex) { DataStore.Log("read clipboard: " + ex.Message); return; }
            }
        }

        /// <summary>自分でクリップボードに置く (履歴には記録しない)</summary>
        public bool SetClipboardOwn(string text)
        {
            ownText = text; ownTime = DateTime.Now;
            try
            {
                var obj = new DataObject();
                obj.SetData(DataFormats.UnicodeText, true, text);
                Clipboard.SetDataObject(obj, true, 10, 50);
                if (AppInfo.IsWindows) ownSeq = W32.GetClipboardSequenceNumber();
                return true;
            }
            catch (Exception ex) { DataStore.Log("set clipboard: " + ex.Message); return false; }
        }

        // ---------- ポップアップ ----------
        public void TogglePopup()
        {
            if (popup != null && !popup.IsDisposed && popup.Visible) { popup.Hide(); return; }
            IntPtr prev = AppInfo.IsWindows ? W32.GetForegroundWindow() : IntPtr.Zero;
            if (popup == null || popup.IsDisposed)
            {
                popup = new PopupForm(this);
                popup.Chosen += PasteItem;
                popup.VisibleChanged += (s, e) => { if (!popup.Visible) ScheduleAutoExit(); };
            }
            popup.ShowAt(Cursor.Position, prev);
        }

        void PasteItem(ClipItem item, IntPtr target)
        {
            popup.Hide();
            if (!SetClipboardOwn(item.Text)) { tray.ShowBalloonTip(3000, AppInfo.DisplayName, "クリップボードに書き込めませんでした。", ToolTipIcon.Warning); return; }
            if (!AppInfo.IsWindows) return;
            if (!Paster.Restore(target)) return; // 戻れなければクリップボードに入れるだけ
            Thread.Sleep(30);
            Paster.SendCtrlV();
        }

        // ---------- 画面 ----------
        public void ShowManager()
        {
            if (manager == null || manager.IsDisposed)
            {
                manager = new HistoryForm(this);
                manager.VisibleChanged += (s, e) => { if (manager != null && !manager.Visible) ScheduleAutoExit(); };
            }
            AppHost.BringToFront(manager);
        }

        /// <summary>設定 (ショートカット・常駐・スタートアップ) は Teal Hub の設定ページで変える</summary>
        public void ShowSettings()
        {
            HubLink.OpenSettings(Manager, AppInfo.Id);
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            Cleanup();
            ExitAction();
        }

        void Cleanup()
        {
            try { HistoryStore.Save(History); } catch { }
            try { watcher.Dispose(); } catch { }
            try { exitTimer.Stop(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
