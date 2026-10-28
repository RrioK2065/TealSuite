// Teal Hub のショートカット (HubShortcuts / 設定ページ / 常駐) とアップデートの入口 (FileAssoc / UpdaterLauncher) のテスト
// Windows 以外では RegisterHotKey を呼べないので、GlobalHotkey.FakeRegister で登録の成否を差し替える。
extern alias AM;
extern alias CH;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using C = AM::TealSuite.Common;
using H = AM::TealSuite.Hub;

static class ShortcutTests
{
    static Action<bool, string> Check;
    static C.HotkeySpec P(string s) { C.HotkeySpec k; return C.HotkeySpec.TryParse(s, out k) ? k : C.HotkeySpec.None; }

    class FakeAssoc : H.IAssocStore
    {
        public string Command; public int Count;
        public string GetCommand() { return Command; }
        public void Register(string command, string icon, string description) { Command = command; Count++; }
    }

    static string FreshData(string tmp, string name)
    {
        var d = Path.Combine(tmp, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        return d;
    }

    static void WriteJson(string appId, string file, string json)
    {
        var d = C.DataStore.DirFor(appId);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, file), json);
    }

    /// <summary>ショートカット (全部 Teal Hub が受け付ける)・重複・解除・保存・常駐・1.5 からの引き継ぎ</summary>
    public static void RunOwnership(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "shortcut-data");
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        var key = new C.MemoryRunKey();
        C.Startup.RunKey = key;
        C.Startup.Folder = new C.StartupFolder(Path.Combine(tmp, "shortcut-startup"), p => null);
        var taken = new HashSet<string>();
        C.GlobalHotkey.FakeRegister = s => taken.Contains(s.ToString()) ? C.GlobalHotkey.ConflictMessage(s) : null;
        try
        {
            var apps = H.Manifest.Load(Path.Combine(dist, "apps.json"));
            var mgr = new H.HubShortcuts();
            var reloads = new List<string>();
            mgr.SignalReload = id => { reloads.Add(id); return true; };
            int toggles = 0; var triggered = new List<string>();
            mgr.ToggleHub = () => toggles++;
            mgr.TriggerApp = a => triggered.Add(a.Id);
            mgr.Load(apps);

            var self = mgr.Self;
            var pass = mgr.For("TealPass"); var memo = mgr.For("TealMemo");
            var clip = mgr.For("TealClip"); var vol = mgr.For("TealVolume");
            Check(mgr.Slots.Count == 7 && mgr.For("TealUpdater") == null, "ショートカットは Teal Hub と 6 アプリの 7 つ (TealUpdater はなし)");
            Check(self != null && self.Current.ToString() == "Alt+H" && mgr.IsActive(self), "Teal Hub 自身は Alt+H");
            Check(clip.Current.ToString() == "Ctrl+Shift+V" && vol.Current.ToString() == "Alt+V", "初期設定: Teal Clip は Ctrl+Shift+V、Teal Volume は Alt+V");
            Check(pass.Current.IsEmpty && memo.Current.IsEmpty && pass.Label == "画面を開く", "Teal Pass・Teal Memo は初期設定なし");
            Check(mgr.IsActive(clip) && mgr.IsActive(vol) && mgr.ActiveCount == 3, "Clip・Volume のキーも Teal Hub が登録する (" + mgr.ActiveCount + " 個)");
            Check(!mgr.Migrated && C.AppPrefs.Exists, "初めてなら引き継ぎなしで app-settings.json を作る");
            Check(H.HubShortcuts.LabelFor(vol.App) == "ミキサーを開く / 隠す" && clip.Label.Contains("履歴"), "何をするキーかの名前 (apps.json の label)");

            // 常駐の初期設定
            Check(H.HubShortcuts.IsResident(clip.App) && !H.HubShortcuts.IsResident(vol.App) && !H.HubShortcuts.IsResident(pass.App) && !H.HubShortcuts.IsResident(memo.App), "常駐の初期設定: Teal Clip だけオン");
            Check(H.HubShortcuts.IsResident(null), "Teal Hub 自身は常に常駐");

            // 押したとき
            mgr.SimulatePress(self); mgr.SimulatePress(vol);
            Check(toggles == 1 && triggered.SequenceEqual(new[] { "TealVolume" }), "押すと Teal Hub の開閉 / アプリの呼び出し");

            // 変更・重複
            reloads.Clear();
            Check(pass.Apply(P("Alt+P")) == null && mgr.IsActive(pass) && C.AppPrefs.HotkeyOf("TealPass") == "Alt+P", "Teal Pass に Alt+P を登録して保存");
            Check(reloads.Contains("TealPass"), "変えたらアプリに知らせる (Reload)");
            mgr.SimulatePress(pass);
            Check(triggered.Last() == "TealPass", "Alt+P で Teal Pass を呼ぶ");
            var dup = memo.Apply(P("Alt+P"));
            Check(dup != null && dup.Contains("Teal Pass") && dup.Contains("先にそちらを解除") && memo.Current.IsEmpty, "重複は日本語で断る: " + dup);
            var dup2 = pass.Apply(P("Alt+V"));
            Check(dup2 != null && dup2.Contains("Teal Volume") && pass.Current.ToString() == "Alt+P", "ほかのアプリのキーとも重複させない");
            Check(mgr.DuplicateOf(memo, P("Alt+H")) == self, "Teal Hub の Alt+H とも重複を調べる");
            var bad = memo.Apply(C.HotkeySpec.Of(C.HotMods.Ctrl, Keys.C));
            Check(bad != null && memo.Current.IsEmpty, "使えない組み合わせは断る: " + bad);
            taken.Add("Alt+M");
            var used = memo.Apply(P("Alt+M"));
            Check(used != null && used.Contains("ほかのアプリ") && memo.Current.IsEmpty, "ほかのソフトが使用中なら元のまま: " + used);
            Check(memo.Apply(P("Alt+Shift+M")) == null && mgr.IsActive(memo), "別のキーなら登録できる");

            // 解除 = なし (初期設定に戻さない)
            Check(clip.Apply(C.HotkeySpec.None) == null && clip.Current.IsEmpty && !mgr.IsActive(clip) && C.AppPrefs.HotkeyOf("TealClip") == "none", "解除すると「なし」として保存");
            Check(pass.Apply(C.HotkeySpec.None) == null && C.AppPrefs.HotkeyOf("TealPass") == "none", "Teal Pass も解除");
            Check(vol.Apply(P("Ctrl+Alt+V")) == null, "Teal Volume を Ctrl+Alt+V に");

            // 常駐の切り替え
            reloads.Clear();
            Check(mgr.SetResident(vol.App, true) == null && H.HubShortcuts.IsResident(vol.App) && reloads.Contains("TealVolume"), "常駐をオンにして保存・アプリに知らせる");
            Check(mgr.SetResident(clip.App, false) == null && !H.HubShortcuts.IsResident(clip.App), "Teal Clip の常駐もオフにできる");

            // 一時停止 (キー入力中)
            mgr.SuspendAll();
            Check(mgr.ActiveCount == 0 && mgr.Suspended, "キー入力中は全部外す");
            mgr.ResumeAll();
            Check(mgr.ActiveCount == 3 && !mgr.Suspended, "入力が終わったら戻す (" + mgr.ActiveCount + ")");
            mgr.Dispose();
            Check(mgr.ActiveCount == 0, "終了で全部外す");

            // 読み直し (保存した値がそのまま)
            var mgr2 = new H.HubShortcuts(); mgr2.SignalReload = id => true;
            mgr2.Load(apps);
            Check(mgr2.For("TealClip").Current.IsEmpty && mgr2.For("TealPass").Current.IsEmpty, "解除したキーは次回も「なし」(初期設定に戻らない)");
            Check(mgr2.For("TealMemo").Current.SameAs(P("Alt+Shift+M")) && mgr2.For("TealVolume").Current.ToString() == "Ctrl+Alt+V", "変更したキーを次回も使う");
            Check(H.HubShortcuts.IsResident(mgr2.For("TealVolume").App) && !H.HubShortcuts.IsResident(mgr2.For("TealClip").App), "常駐の設定も残る");
            Check(CH::TealSuite.Common.AppPrefs.HotkeyOf("TealVolume") == "Ctrl+Alt+V", "アプリ側 (Teal Clip の exe) からも同じ設定を読める");
            mgr2.Dispose();

            // 手で書き換えて重複した → あとのほうは使わずに知らせる
            var prefs = C.AppPrefs.Load();
            prefs.GetOrAdd("TealPass").Hotkey = "Ctrl+Alt+V";
            C.AppPrefs.Save(prefs);
            var mgr3 = new H.HubShortcuts(); mgr3.SignalReload = id => true;
            mgr3.Load(apps);
            var p3 = mgr3.For("TealPass"); var v3 = mgr3.For("TealVolume");
            Check(mgr3.IsActive(p3) != mgr3.IsActive(v3) && (p3.Error ?? v3.Error ?? "").Contains("重なって"), "重複した設定ファイルは片方だけ使い、理由を表示");
            mgr3.Dispose();

            // ---- 1.5 からの引き継ぎ
            FreshData(tmp, "shortcut-migrate");
            WriteJson("TealHub", "shortcuts.json", "{\"app\":\"TealHub\",\"appName\":\"Teal Hub\",\"items\":[{\"slot\":\"open\",\"label\":\"x\",\"keys\":\"Ctrl+Alt+H\"},{\"slot\":\"open:TealPass\",\"label\":\"x\",\"keys\":\"Alt+P\"},{\"slot\":\"open:TealMemo\",\"label\":\"x\",\"keys\":\"none\"}]}");
            WriteJson("TealClip", "shortcuts.json", "{\"app\":\"TealClip\",\"appName\":\"Teal Clip\",\"items\":[{\"slot\":\"popup\",\"label\":\"x\",\"keys\":\"Ctrl+Alt+C\"}]}");
            WriteJson("TealVolume", "settings.json", "{\"hotkey\":\"Ctrl+Alt+F9\"}");
            var volApp = apps.First(a => a.Id == "TealVolume");
            key.Values.Clear();
            C.Startup.TrySet("TealVolume", volApp.ExePath, true);
            var m4 = new H.HubShortcuts(); m4.SignalReload = id => true;
            m4.Load(apps);
            Check(m4.Migrated, "1.5 の設定を見つけたら引き継ぐ (お知らせを出す)");
            Check(m4.Self.Current.ToString() == "Ctrl+Alt+H" && m4.For("TealPass").Current.ToString() == "Alt+P", "Teal Hub が持っていたキー (Teal Hub・Teal Pass) を引き継ぐ");
            Check(m4.For("TealMemo").Current.IsEmpty, "1.5 で「なし」にしていたら「なし」のまま");
            Check(m4.For("TealClip").Current.ToString() == "Ctrl+Alt+C", "Teal Clip が持っていた Ctrl+Alt+C を引き継ぐ");
            Check(m4.For("TealVolume").Current.ToString() == "Ctrl+Alt+F9", "1.3 の Teal Volume (settings.json) のキーも引き継ぐ");
            Check(H.HubShortcuts.IsResident(m4.For("TealVolume").App) && !H.HubShortcuts.IsResident(m4.For("TealPass").App), "スタートアップに登録していたアプリは常駐オンで引き継ぐ");
            m4.Dispose();
            var m5 = new H.HubShortcuts(); m5.SignalReload = id => true;
            m5.Load(apps);
            Check(!m5.Migrated && m5.For("TealClip").Current.ToString() == "Ctrl+Alt+C", "引き継ぎは 1 回だけ (2 回目からは app-settings.json)");
            m5.Dispose();

            // 古い apps.json (shortcuts・resident なし) でも Clip・Volume は決まった初期設定
            FreshData(tmp, "shortcut-oldmanifest");
            var oldJson = Path.Combine(tmp, "old-apps.json");
            File.WriteAllText(oldJson, "{\"apps\":[{\"id\":\"TealClip\",\"name\":\"Teal Clip\",\"exe\":\"apps/TealClip/TealClip.exe\"},{\"id\":\"TealVolume\",\"name\":\"Teal Volume\",\"exe\":\"apps/TealVolume/TealVolume.exe\"},{\"id\":\"TealPass\",\"name\":\"Teal Pass\",\"exe\":\"apps/TealPass/TealPass.exe\"}]}");
            var oldApps = H.Manifest.Load(oldJson);
            var m6 = new H.HubShortcuts(); m6.SignalReload = id => true;
            m6.Load(oldApps);
            Check(m6.For("TealClip").Current.ToString() == "Ctrl+Shift+V" && m6.For("TealVolume").Current.ToString() == "Alt+V" && m6.For("TealPass").Current.IsEmpty, "apps.json に shortcuts がなくても初期設定は同じ");
            Check(H.HubShortcuts.IsResident(m6.For("TealClip").App) && !H.HubShortcuts.IsResident(m6.For("TealPass").App), "apps.json に resident がなくても Teal Clip は常駐オン");
            m6.Dispose();
        }
        finally
        {
            C.GlobalHotkey.FakeRegister = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    static void WritePrefs(string json)
    {
        var p = CH::TealSuite.Common.AppPrefs.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, json);
    }

    static void Pump(Func<bool> until, int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (!until() && DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }

    /// <summary>Teal Clip: 常駐オン / オフ、Teal Hub からの Hotkey・Reload</summary>
    public static void RunClipTray(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "clip-tray");
        CH::TealSuite.Common.AppInfo.Init("TealClip", "Teal Clip", "");
        try
        {
            // 常駐オン (初期設定)
            var t = new CH::TealSuite.Clip.TrayContext(new string[0]);
            t.ExitAction = () => { };
            Application.DoEvents();
            Check(t.Resident && t.TrayVisible && t.Manager == null && t.Popup == null, "初期設定は常駐オン: 通知領域にだけ出る");
            Check(t.Hotkey.ToString() == "Ctrl+Shift+V", "ショートカットは初期設定の Ctrl+Shift+V");
            CH::TealSuite.Common.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(t.Popup.Visible, "Teal Hub からの Hotkey で履歴を呼び出す");
            t.Popup.Hide(); Application.DoEvents();
            Check(!t.IsExited && !t.AutoExitPending, "常駐中は閉じても終了しない");

            // Teal Hub でキーを変えた (Reload)
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealClip\",\"hotkey\":\"Ctrl+Alt+C\",\"resident\":true}]}");
            CH::TealSuite.Common.AppHost.ReloadRequested(); Application.DoEvents();
            Check(t.Hotkey.ToString() == "Ctrl+Alt+C" && t.TrayVisible, "Reload でキーを読み直す");

            // 常駐オフにした → 画面が出ていなければ終わる
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealClip\",\"hotkey\":\"Ctrl+Alt+C\",\"resident\":false}]}");
            bool exited = false; t.ExitAction = () => exited = true;
            CH::TealSuite.Common.AppHost.ReloadRequested();
            Pump(() => exited, 3000);
            Check(!t.Resident && !t.TrayVisible && exited, "常駐をオフにすると通知領域から消え、少しして終了");

            // 常駐オフで普通に起動 → 管理画面を出し、閉じたら終了
            var t2 = new CH::TealSuite.Clip.TrayContext(new string[0]);
            bool ex2 = false; t2.ExitAction = () => ex2 = true;
            Application.DoEvents();
            Check(!t2.TrayVisible && t2.Manager != null && t2.Manager.Visible, "常駐オフでは起動すると履歴の画面を出す");
            Check(t2.Manager.Text.Length > 0 && t2.Manager.SubtitleText.Contains("常駐オフ") && t2.Manager.SubtitleText.Contains("Ctrl + Alt + C"), "画面に「常駐オフ」とキー (" + t2.Manager.SubtitleText + ")");
            t2.Manager.Close(); Application.DoEvents();
            Check(t2.AutoExitPending || ex2, "閉じると終了の準備");
            Pump(() => ex2, 3000);
            Check(ex2, "常駐オフでは閉じると終了");

            // 常駐オフで、Teal Hub がショートカットで起動 (--show) → 履歴だけ出して、閉じたら終了
            var t3 = new CH::TealSuite.Clip.TrayContext(new[] { CH::TealSuite.Common.AppPrefs.ShowArg });
            bool ex3 = false; t3.ExitAction = () => ex3 = true;
            Application.DoEvents();
            Check(t3.Popup != null && t3.Popup.Visible && t3.Manager == null, "--show で起動したら履歴をその場に出す");
            t3.Popup.Hide();
            Pump(() => ex3, 3000);
            Check(ex3, "選んだり閉じたりしたら終了");

            // 常駐オフでログイン時に起動 (古い登録が残っていた) → 何も出さずに終了
            var t4 = new CH::TealSuite.Clip.TrayContext(new[] { CH::TealSuite.Common.Startup.Arg });
            bool ex4 = false; t4.ExitAction = () => ex4 = true;
            Pump(() => ex4, 1000);
            Check(ex4 && t4.Manager == null && t4.Popup == null, "常駐オフで --startup のときは何も出さずに終了");

            // 設定は Teal Hub の設定ページ
            var launched = new List<string>();
            CH::TealSuite.Common.HubLink.LaunchHook = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
            var t5 = new CH::TealSuite.Clip.TrayContext(new string[0]);
            t5.ExitAction = () => { };
            t5.ShowSettings();
            Check(launched.Count == 1 && launched[0] == "TealHub.exe --settings TealClip", "「設定」で Teal Hub の Teal Clip 設定ページを開く (" + (launched.Count > 0 ? launched[0] : "") + ")");
            CH::TealSuite.Common.HubLink.LaunchHook = null;
            t5.ExitApp();
        }
        finally
        {
            CH::TealSuite.Common.HubLink.LaunchHook = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    /// <summary>Teal Hub: 常駐 (Alt+H)・カードの設定ボタン・設定ページ・ショートカットで起動・アップデートの入口</summary>
    public static void RunHubUi(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "shortcut-ui");
        Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        var key = new C.MemoryRunKey();
        C.Startup.RunKey = key;
        C.Startup.Folder = new C.StartupFolder(Path.Combine(tmp, "shortcut-ui-startup"), p => null);
        C.GlobalHotkey.FakeRegister = s => null;
        var assoc = new FakeAssoc();
        H.FileAssoc.Store = assoc;
        var launched = new List<string>();
        H.UpdaterLauncher.StartProcess = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
        try
        {
            var tray = new H.HubTray(new string[0], true);
            tray.ExitAction = () => { };
            var f = tray.Window;
            var mgr = tray.Shortcuts;
            var reloads = new List<string>();
            mgr.SignalReload = id => { reloads.Add(id); return true; };
            var started = new List<string>(); bool running = false;
            f.Control_.StartHook = (a, args) => started.Add(a.Id + " " + args);
            f.Control_.HotkeyHook = a => running;
            Check(!f.Visible && mgr.IsActive(mgr.Self), "--startup では隠れて起動し、Alt+H は登録済み");
            Check(assoc.Count == 1 && assoc.Command == H.FileAssoc.CommandFor(H.UpdaterLauncher.UpdaterExe), ".tealupdate を TealUpdater.exe に関連付け: " + assoc.Command);
            Check(!H.FileAssoc.Ensure(H.UpdaterLauncher.UpdaterExe) && assoc.Count == 1, "関連付けが同じなら書き直さない");

            mgr.SimulatePress(mgr.Self);
            Check(f.Visible, "Alt+H で開く");
            tray.ForegroundCheck = () => true;
            mgr.SimulatePress(mgr.Self);
            Check(!f.Visible && tray.TipCount == 1, "前面にあればもう一度 Alt+H で隠す (初回だけ案内)");
            tray.ForegroundCheck = () => false;
            mgr.SimulatePress(mgr.Self);
            Check(f.Visible && !f.IsSettingsShown, "後ろにあれば前面に出す (一覧の画面)");

            // カード: 設定の要約と「設定」ボタン
            var clipCard = f.CardFor("TealClip"); var passCard = f.CardFor("TealPass"); var volCard = f.CardFor("TealVolume");
            Check(f.Cards.Count == 6 && f.Cards.All(c => c.BtnSettings.Visible && c.BtnSettings.Enabled), "6 つのカードすべてに「設定」ボタン");
            Check(f.SelfBar_.BtnSettings.Visible, "Teal Hub 自身の帯にも「設定」ボタン");
            Check(clipCard.SummaryText == "ショートカット: Ctrl + Shift + V ・ 常駐: オン ・ スタートアップ: オフ", "Teal Clip の要約: " + clipCard.SummaryText);
            Check(passCard.SummaryText == "ショートカット: なし ・ 常駐: オフ", "Teal Pass の要約 (常駐オフならスタートアップは出さない): " + passCard.SummaryText);
            Check(volCard.SummaryText.StartsWith("ショートカット: Alt + V ・ 常駐: オフ"), "Teal Volume の要約: " + volCard.SummaryText);

            // Teal Clip の設定ページ
            clipCard.BtnSettings.PerformClick();
            var page = f.SettingsPage;
            Check(f.IsSettingsShown && page.AppId == "TealClip" && !passCard.Visible, "「設定」を押すと画面が Teal Clip の設定ページに切り替わる");
            Check(page.TitleText == "Teal Clip の設定" && page.SubText.Contains("v1.9.0"), "見出し: " + page.TitleText + " / " + page.SubText);
            Check(page.StatusText.Contains("Ctrl + Shift + V") && page.Box.Value.ToString() == "Ctrl+Shift+V", "ショートカット欄に今のキー (" + page.StatusText + ")");
            Check(page.ResidentSwitch.Checked && page.ResidentSwitch.Enabled && page.StartupSwitch.Enabled && !page.StartupSwitch.Checked, "常駐はオン、スタートアップは選べる (今はオフ)");
            Check(page.ResidentNoteText.Contains("記録を続けます"), "Teal Clip は常駐の意味を説明");
            page.StartupSwitch.Checked = true;
            Check(C.Startup.Get("TealClip", clipCard.App.ExePath).Status == C.StartupStatus.On && clipCard.SummaryText.EndsWith("スタートアップ: オン"), "スタートアップをオン (" + clipCard.SummaryText + ")");
            reloads.Clear();
            page.ResidentSwitch.Checked = false;
            Check(!H.HubShortcuts.IsResident(clipCard.App) && reloads.Contains("TealClip"), "常駐をオフにして保存し、Teal Clip に知らせる");
            Check(C.Startup.Get("TealClip", clipCard.App.ExePath).Command == null && !page.StartupSwitch.Checked && !page.StartupSwitch.Enabled, "常駐をオフにするとスタートアップも解除され、選べなくなる");
            Check(page.StartupNoteText.Contains("常駐") && f.FooterText.Contains("スタートアップも解除"), "理由を表示 (" + page.StartupNoteText + ")");
            Check(page.ResidentNoteText.Contains("注意") && page.ResidentNoteText.Contains("履歴に残りません"), "Teal Clip の常駐オフは注意を出す");
            page.ResidentSwitch.Checked = true;
            Check(H.HubShortcuts.IsResident(clipCard.App) && page.StartupSwitch.Enabled, "常駐をオンに戻すとスタートアップを選べる");

            // ショートカットの変更・解除・初期設定に戻す
            page.Box.SetValue(P("Ctrl+Alt+C"));
            Check(page.Register() && mgr.For("TealClip").Current.ToString() == "Ctrl+Alt+C" && C.AppPrefs.HotkeyOf("TealClip") == "Ctrl+Alt+C", "設定ページで Ctrl+Alt+C を登録");
            Check(page.MessageText.Contains("登録しました") && clipCard.SummaryText.StartsWith("ショートカット: Ctrl + Alt + C"), "メッセージとカードにも反映");
            page.Box.SetValue(P("Alt+V"));
            Check(!page.Register() && page.MessageText.Contains("Teal Volume") && mgr.For("TealClip").Current.ToString() == "Ctrl+Alt+C", "重複は日本語で断る: " + page.MessageText);
            page.CancelCapture();
            Check(page.Box.Value.ToString() == "Ctrl+Alt+C", "Esc (取り消し) で今の値に戻す");
            Check(page.ResetLink.Visible && page.ResetToDefault() && mgr.For("TealClip").Current.ToString() == "Ctrl+Shift+V", "「初期設定に戻す」で Ctrl+Shift+V");
            Check(page.Clear(true) && mgr.For("TealClip").Current.IsEmpty && C.AppPrefs.HotkeyOf("TealClip") == "none" && page.MessageText.Contains("解除しました"), "「解除」でなしに (アプリは確認なし)");
            Check(!page.BtnClear.Enabled && page.StatusText.Contains("なし"), "解除後は「解除」を押せない (" + page.StatusText + ")");
            page.Box.SetValue(C.HotkeySpec.None);
            Check(!page.Register() && page.MessageText.Contains("先に"), "キーを押していなければ登録しない");

            // 戻る
            page.BtnBack.PerformClick();
            Check(!f.IsSettingsShown && passCard.Visible && clipCard.SummaryText.StartsWith("ショートカット: なし"), "「戻る」で一覧に戻る");

            // Teal Pass (常駐オフ): スタートアップは選べない
            passCard.BtnSettings.PerformClick();
            Check(f.IsSettingsShown && page.AppId == "TealPass" && !page.ResidentSwitch.Checked, "Teal Pass の設定ページ (常駐オフ)");
            Check(!page.StartupSwitch.Enabled && page.StartupNoteText.Contains("常駐"), "常駐オフではスタートアップを選べない: " + page.StartupNoteText);
            Check(!f.SetAppStartup(passCard.App, true) && C.Startup.Get("TealPass", passCard.App.ExePath).Command == null, "常駐オフのアプリはスタートアップに登録しない");
            page.Box.SetValue(P("Alt+P"));
            Check(page.Register() && mgr.IsActive(mgr.For("TealPass")), "Teal Pass に Alt+P");
            page.ResidentSwitch.Checked = true;
            Check(page.StartupSwitch.Enabled, "常駐をオンにするとスタートアップを選べる");
            page.StartupSwitch.Checked = true;
            Check(C.Startup.Get("TealPass", passCard.App.ExePath).Registered, "Teal Pass をスタートアップに");
            f.ShowList();
            Check(passCard.SummaryText == "ショートカット: Alt + P ・ 常駐: オン ・ スタートアップ: オン", "要約: " + passCard.SummaryText);

            // Teal Hub 自身の設定ページ
            f.SelfBar_.BtnSettings.PerformClick();
            Check(f.IsSettingsShown && page.IsSelfPage && page.TitleText == "Teal Hub の設定", "Teal Hub 自身の設定ページ");
            Check(page.ResidentSwitch.Checked && !page.ResidentSwitch.Enabled, "Teal Hub は常に常駐 (切り替え不可)");
            page.Box.SetValue(P("Ctrl+Alt+H"));
            Check(page.Register() && mgr.Self.Current.ToString() == "Ctrl+Alt+H" && mgr.IsActive(mgr.Self), "Teal Hub のキーを Ctrl+Alt+H に");
            page.StartupSwitch.Checked = true;
            Check(C.Startup.Get("TealHub", C.Startup.CurrentExe).Registered, "Teal Hub をスタートアップに");
            page.StartupSwitch.Checked = false;
            Check(C.Startup.Get("TealHub", C.Startup.CurrentExe).Command == null, "Teal Hub のスタートアップを解除");
            Check(!f.ShowSettings("NoSuchApp") && f.IsSettingsShown, "知らないアプリの設定ページは開かない");
            f.ShowList();

            // ショートカットで起動 / 呼び出し
            int t0 = 0; var now = new DateTime(2026, 10, 27, 9, 0, 0);
            f.Now = () => now.AddMilliseconds(t0);
            var vslot = mgr.For("TealVolume");
            started.Clear();
            Check(f.TriggerApp(volCard.App) == "start" && started.SequenceEqual(new[] { "TealVolume --show" }), "起動していなければ --show を付けて起動 (" + string.Join(",", started) + ")");
            Check(f.TriggerApp(volCard.App) == "wait" && started.Count == 1, "続けて押しても 2 回起動しない");
            t0 = 2500;
            Pump(() => !volCard.Busy, 3000);
            Check(f.TriggerApp(volCard.App) == "start" && started.Count == 2, "少し待てばもう一度起動できる");
            running = true;
            Check(f.TriggerApp(volCard.App) == "signal" && started.Count == 2, "起動中なら Hotkey イベントで知らせる (起動しない)");
            running = false;
            t0 = 6000; Pump(() => !volCard.Busy, 3000);
            mgr.SimulatePress(vslot);
            Check(started.Count == 3, "Alt+V を押すと Teal Hub が Teal Volume を呼ぶ");

            // --settings (アプリの「設定」から) / 2 つめの起動
            f.ShowList();
            Check(C.HubLink.SettingsTarget(new[] { "--settings", "TealMemo" }) == "TealMemo" && C.HubLink.SettingsTarget(new[] { "--startup" }) == null, "--settings <ID> を読む");
            C.HubLink.WriteRequest("TealMemo");
            tray.ShowWindow();
            Check(f.IsSettingsShown && page.AppId == "TealMemo", "起動中の Teal Hub に頼むと Teal Memo の設定ページを開く");
            Check(C.HubLink.TakeRequest() == null, "頼みごとは 1 回で消える");
            f.ShowList();
            tray.OpenSettings("TealVolume");
            Check(f.Visible && f.IsSettingsShown && page.AppId == "TealVolume", "OpenSettings で Teal Volume の設定ページ");
            f.ShowList();

            // アップデートの入口
            var pkg = Path.Combine(tmp, "TealSuite-9.9.9.tealupdate");
            File.WriteAllText(pkg, "x");
            f.OpenUpdateFile(pkg);
            Check(launched.Count == 1 && launched[0] == "TealUpdater.exe \"" + Path.GetFullPath(pkg) + "\"", "アップデート ファイルを開くと TealUpdater.exe に渡す: " + (launched.Count > 0 ? launched[0] : ""));
            Check(!H.UpdaterLauncher.HasBackups, "backup がなければ「前のバージョンに戻す」は案内だけ");

            tray.ExitApp();
            Check(tray.IsExited && !mgr.IsActive(mgr.Self) && mgr.ActiveCount == 0, "終了するとショートカットも外す");

            // アプリの「設定」から起動された Teal Hub
            var tray2 = new H.HubTray(new[] { C.HubLink.SettingsArg, "TealClip" }, false);
            tray2.ExitAction = () => { };
            tray2.Shortcuts.SignalReload = id => true;
            Application.DoEvents();
            Check(tray2.Window.Visible && tray2.Window.IsSettingsShown && tray2.Window.SettingsPage.AppId == "TealClip", "--settings TealClip で起動すると、最初から Teal Clip の設定ページ");
            tray2.ExitApp();
        }
        finally
        {
            C.GlobalHotkey.FakeRegister = null;
            H.FileAssoc.Store = null;
            H.UpdaterLauncher.StartProcess = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DEMO", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }
}
