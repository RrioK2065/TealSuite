// Teal Clip (コピー履歴) - 常駐部分 (通知領域アイコン・監視・ショートカット・貼り付け)
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
        PopupForm popup;
        HistoryForm manager;
        HotkeySettingsForm settingsForm;
        uint ownSeq;
        string ownText;
        DateTime ownTime;
        bool exiting;
        public const string SettingsFile = "settings.json";

        /// <summary>初期設定のショートカット</summary>
        public static readonly HotkeySpec DefaultHotkey = HotkeySpec.Of(HotMods.Ctrl | HotMods.Shift, Keys.V);
        /// <summary>shortcuts.json での名前 (Teal Hub の一覧からも変更できる)</summary>
        public const string HotkeySlot = "popup", HotkeyLabel = "履歴を呼び出す";
        public readonly HotkeyController Hotkeys;
        public HotkeySpec Hotkey { get { return Hotkeys.Current; } }
        public string HotkeyError { get { return Hotkeys.Error; } }

        public TrayContext(string[] args)
        {
            settings = DataStore.LoadQuiet(SettingsFile, () => new ChSettings());
            History = HistoryStore.Load();
            if (Environment.GetEnvironmentVariable("TEALSUITE_DEMO") == "1" && History.Items.Count == 0) SeedDemo();
            History.Changed += () => { HistoryStore.Save(History); if (manager != null && !manager.IsDisposed) manager.RefreshList(); };

            watcher = new ClipboardWatcher();
            watcher.ClipboardChanged += OnClipboardChanged;
            readTimer.Interval = 80;
            readTimer.Tick += (s, e) => { readTimer.Stop(); ReadClipboard(); };

            Hotkeys = ShortcutStore.CreateOwned(HotkeySlot, HotkeyLabel, DefaultHotkey, settings.Hotkey);
            Hotkeys.Pressed += () => TogglePopup();
            Hotkeys.Changed += () =>
            {
                settings.Hotkey = Hotkeys.Current.ToSaved();   // 1.3 に戻したときのために settings.json にも残す
                DataStore.TrySave(SettingsFile, settings);
                tray.Text = TrayTip();
                if (manager != null && !manager.IsDisposed) manager.UpdateSubtitle();
            };

            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            var miShow = new ToolStripMenuItem("履歴を表示", null, (s, e) => ShowManager()) { Font = Theme.Bold };
            menu.Items.Add(miShow);
            menu.Items.Add(new ToolStripMenuItem("設定…", null, (s, e) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, (s, e) => ExitApp()));
            tray.ContextMenuStrip = menu;
            try { tray.Icon = new Icon(Theme.AppIcon, SystemInformation.SmallIconSize); } catch { tray.Icon = Theme.AppIcon; }
            tray.Text = TrayTip();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowManager(); };
            tray.Visible = true;

            AppHost.ShowRequested = ShowManager;
            AppHost.ExitRequested = ExitApp;
            AppHost.ReloadRequested = ReloadHotkey;   // Teal Hub でショートカットを変えたとき
            Application.ApplicationExit += (s, e) => Cleanup();

            if (HotkeyError != null)
                tray.ShowBalloonTip(8000, AppInfo.DisplayName, HotkeyError + "\n通知領域のアイコンを右クリック →「設定」で変更できます。", ToolTipIcon.Warning);
            else if (!settings.FirstRunTipShown && !AppHost.StartedAtLogin)   // ログイン時の自動起動では案内を出さない
            {
                tray.ShowBalloonTip(6000, AppInfo.DisplayName + " (コピー履歴) を起動しました", "通知領域に常駐しています。" + (Hotkey.IsEmpty ? "アイコンのクリックで履歴を表示できます。" : Hotkey.Display + " で履歴を呼び出せます。"), ToolTipIcon.Info);
                settings.FirstRunTipShown = true;
                DataStore.TrySave(SettingsFile, settings);
            }

            foreach (var a in args ?? new string[0])
            {
                if (a == "/show") ShowManager();
                else if (a == "/popup") TogglePopup();
                else if (a == "/settings") ShowSettings();
            }
        }

        /// <summary>Teal Hub が shortcuts.json を書き換えたら読み直す</summary>
        public void ReloadHotkey()
        {
            Hotkeys.Reload(ShortcutStore.Get(AppInfo.Id, HotkeySlot));
            tray.Text = TrayTip();
            if (manager != null && !manager.IsDisposed) manager.UpdateSubtitle();
        }

        string TrayTip()
        {
            if (Hotkey.IsEmpty) return AppInfo.DisplayName;
            var t = AppInfo.DisplayName + " (" + (HotkeyError == null ? Hotkey.Display : "ショートカット未登録") + ")";
            return t.Length > 63 ? t.Substring(0, 63) : t;
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
            if (manager == null || manager.IsDisposed) manager = new HistoryForm(this);
            AppHost.BringToFront(manager);
        }

        public void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { AppHost.BringToFront(settingsForm); return; }
            settingsForm = new HotkeySettingsForm(Hotkeys, "履歴を呼び出すショートカット", "Windows にサインインすると、通知領域に静かに常駐します。");
            settingsForm.FormClosed += (s, e) => settingsForm = null;
            settingsForm.Show();
            AppHost.BringToFront(settingsForm);
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            Cleanup();
            AppHost.RequestExit();
        }

        void Cleanup()
        {
            try { HistoryStore.Save(History); } catch { }
            try { watcher.Dispose(); } catch { }
            try { Hotkeys.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
        }
    }
}
