// Teal Hub のショートカット (HubShortcuts / ショートカット一覧 / 常駐) とアップデートの入口 (FileAssoc / UpdaterLauncher) のテスト
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

    /// <summary>ショートカットの持ち主・重複・解除・保存</summary>
    public static void RunOwnership(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "shortcut-data");
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        var taken = new HashSet<string>();
        C.GlobalHotkey.FakeRegister = s => taken.Contains(s.ToString()) ? C.GlobalHotkey.ConflictMessage(s) : null;
        try
        {
            var apps = H.Manifest.Load(Path.Combine(dist, "apps.json"));
            var mgr = new H.HubShortcuts();
            var reloads = new List<string>();
            mgr.SignalReload = id => { reloads.Add(id); return true; };
            int toggles = 0; var opened = new List<string>();
            mgr.ToggleHub = () => toggles++;
            mgr.OpenApp = a => opened.Add(a.Id);
            mgr.Load(apps);

            var self = mgr.Self;
            Check(self != null && self.Current.ToString() == "Alt+H" && self.HubOwned && mgr.IsActive(self), "Teal Hub 自身は Alt+H (Teal Hub が登録)");
            var pass = mgr.Find("TealPass", C.ShortcutStore.OpenSlotFor("TealPass"));
            var memo = mgr.Find("TealMemo", C.ShortcutStore.OpenSlotFor("TealMemo"));
            Check(pass != null && memo != null && pass.Current.IsEmpty && memo.Current.IsEmpty && pass.HubOwned, "Teal Pass・Teal Memo を開くショートカットは Teal Hub が持ち、初期設定はなし");
            var clip = mgr.Find("TealClip", "popup");
            var vol = mgr.Find("TealVolume", "open");
            Check(clip != null && clip.Owner == "TealClip" && clip.Current.ToString() == "Ctrl+Shift+V" && !clip.HubOwned, "Teal Clip の Ctrl+Shift+V は Teal Clip 自身が持つ");
            Check(vol != null && vol.Owner == "TealVolume" && vol.Current.ToString() == "Alt+V" && !mgr.IsActive(vol), "Teal Volume の Alt+V も Teal Volume 自身 (Teal Hub は登録しない)");
            Check(mgr.Find("TealClip", C.ShortcutStore.OpenSlotFor("TealClip")) != null && mgr.Find("TealVolume", C.ShortcutStore.OpenSlotFor("TealVolume")) == null, "「開く」を持たない Teal Clip には Teal Hub の「開く」も用意、Teal Volume には用意しない");
            Check(mgr.ActiveCount == 1, "最初に登録するのは Alt+H だけ (" + mgr.ActiveCount + ")");
            Check(C.ShortcutStore.Get("TealHub", "open") == "Alt+H", "今の値を shortcuts.json に書いておく (ほかのアプリの重複確認用)");

            // 変更・押す・解除
            Check(pass.Apply(P("Alt+P")) == null && pass.Current.ToString() == "Alt+P" && mgr.IsActive(pass), "Teal Pass に Alt+P を登録");
            mgr.SimulatePress(pass); mgr.SimulatePress(self);
            Check(opened.SequenceEqual(new[] { "TealPass" }) && toggles == 1, "押すと Teal Pass を開く / Alt+H で Teal Hub を開閉");
            Check(C.ShortcutStore.Get("TealHub", C.ShortcutStore.OpenSlotFor("TealPass")) == "Alt+P", "Teal Hub の shortcuts.json に保存");

            // 重複
            var why = memo.Apply(P("Alt+P"));
            Check(why != null && why.Contains("Teal Pass") && memo.Current.IsEmpty, "同じ組み合わせは登録しない (" + why + ")");
            why = memo.Apply(P("Alt+V"));
            Check(why != null && why.Contains("Teal Volume"), "アプリが持つ Alt+V とも重ならない");
            Check(mgr.DuplicateOf(memo, P("Alt+H")) == self && mgr.DuplicateOf(memo, C.HotkeySpec.None) == null, "DuplicateOf: 空の値は重複にならない");
            Check(memo.Apply(C.HotkeySpec.Of(C.HotMods.Ctrl, Keys.C)) != null, "不可の組み合わせ (Ctrl+C) は登録しない");
            taken.Add("Alt+M");
            why = memo.Apply(P("Alt+M"));
            Check(why != null && why.Contains("ほかのアプリ") && memo.Current.IsEmpty, "ほかのアプリが使っていれば理由を返す");

            // 解除
            Check(pass.Apply(C.HotkeySpec.None) == null && pass.Current.IsEmpty && !mgr.IsActive(pass), "解除すると登録を外す");
            Check(C.ShortcutStore.Get("TealHub", C.ShortcutStore.OpenSlotFor("TealPass")) == "none", "解除は「なし」(none) として保存 (初期設定に戻らない)");
            Check(memo.Apply(P("Alt+P")) == null, "解除した組み合わせは別のアプリで使える");
            Check(self.Apply(C.HotkeySpec.None) == null && !mgr.IsActive(self) && C.ShortcutStore.Get("TealHub", "open") == "none", "Teal Hub の Alt+H も解除できる");

            // アプリが持つショートカット: 保存してアプリに読み直しを頼む
            reloads.Clear();
            Check(clip.Apply(P("Ctrl+Alt+C")) == null && reloads.SequenceEqual(new[] { "TealClip" }) && !mgr.IsActive(clip), "Teal Clip の変更は保存して Teal Clip に読み直しを頼む (Teal Hub は登録しない)");
            Check(C.ShortcutStore.Get("TealClip", "popup") == "Ctrl+Alt+C", "Teal Clip の shortcuts.json に保存");
            Check(CH::TealSuite.Common.ShortcutStore.Get("TealClip", "popup") == "Ctrl+Alt+C", "Teal Clip (別の exe) からも同じ値が読める");
            Check(vol.Apply(C.HotkeySpec.None) == null && reloads.Last() == "TealVolume" && C.ShortcutStore.Get("TealVolume", "open") == "none", "Teal Volume の Alt+V も解除できる");

            // アプリの側で変えたら読み直す
            C.ShortcutStore.Set("TealVolume", "Teal Volume", "open", "画面を開く / 隠す", P("Alt+B"));
            Check(mgr.RefreshFromDisk() && vol.Current.ToString() == "Alt+B", "アプリ側で変えた値を読み直す");

            // 読み直し (再起動) で値が残る
            mgr.Dispose();
            var mgr2 = new H.HubShortcuts();
            mgr2.SignalReload = id => true;
            mgr2.Load(apps);
            Check(mgr2.Self.Current.IsEmpty && !mgr2.IsActive(mgr2.Self), "再起動しても解除したまま (Alt+H)");
            Check(mgr2.Find("TealMemo", C.ShortcutStore.OpenSlotFor("TealMemo")).Current.ToString() == "Alt+P" && mgr2.Find("TealClip", "popup").Current.ToString() == "Ctrl+Alt+C", "変更した値も残る");
            Check(mgr2.ActiveCount == 1, "登録するのは Teal Hub が持つ値だけ (Alt+P)");

            // 重なっている値 (手で書き換えたなど) は一覧で警告
            C.ShortcutStore.Set("TealHub", "Teal Hub", C.ShortcutStore.OpenSlotFor("TealPass"), "Teal Pass を開く", P("Alt+B"));
            mgr2.Dispose();
            var mgr3 = new H.HubShortcuts(); mgr3.SignalReload = id => true; mgr3.Load(apps);
            var p3 = mgr3.Find("TealPass", C.ShortcutStore.OpenSlotFor("TealPass"));
            Check(mgr3.DuplicateCount == 1 && H.KeyCap.StateOf(mgr3, p3) == H.KeyCapState.Duplicate, "重なっている組み合わせを数え、キーの表示を警告に");
            Check(H.KeyCap.TipOf(mgr3, p3).Contains("Teal Volume"), "どれと重なっているかを表示 (" + H.KeyCap.TipOf(mgr3, p3) + ")");

            // 1.3 の settings.json の "hotkey" を引き継ぐ
            mgr3.Dispose();
            FreshData(tmp, "shortcut-legacy");
            Directory.CreateDirectory(Path.Combine(C.DataStore.Root, "TealVolume"));
            File.WriteAllText(Path.Combine(C.DataStore.Root, "TealVolume", "settings.json"), "{\"hotkey\":\"Ctrl+Alt+V\"}");
            var mgr4 = new H.HubShortcuts(); mgr4.SignalReload = id => true; mgr4.Load(apps);
            Check(mgr4.Find("TealVolume", "open").Current.ToString() == "Ctrl+Alt+V", "1.3 の settings.json の値 (Ctrl+Alt+V) を表示");
            mgr4.Dispose();

            // apps.json に shortcuts がない (古い apps.json) ときは組み込みの既定
            var noDecl = H.Manifest.Load(Path.Combine(dist, "apps.json"));
            foreach (var a in noDecl) a.Shortcuts = null;
            FreshData(tmp, "shortcut-old");
            var mgr5 = new H.HubShortcuts(); mgr5.SignalReload = id => true; mgr5.Load(noDecl);
            Check(mgr5.Find("TealClip", "popup") != null && mgr5.Find("TealClip", "popup").Current.ToString() == "Ctrl+Shift+V" && mgr5.Find("TealVolume", "open").Current.ToString() == "Alt+V", "apps.json に shortcuts がなくても既定 (Ctrl+Shift+V・Alt+V)");
            mgr5.Dispose();
        }
        finally
        {
            C.GlobalHotkey.FakeRegister = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    /// <summary>アプリが持つショートカット (Teal Clip): Teal Hub が変えた値を読み直す</summary>
    public static void RunAppOwned(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "shortcut-owned");
        CH::TealSuite.Common.AppInfo.Init("TealClip", "Teal Clip", "");
        CH::TealSuite.Common.GlobalHotkey.FakeRegister = s => null;
        try
        {
            var hk = CH::TealSuite.Common.ShortcutStore.CreateOwned("popup", "履歴を呼び出す", CH::TealSuite.Clip.TrayContext.DefaultHotkey, "Ctrl+Alt+H");
            Check(hk.Current.ToString() == "Ctrl+Alt+H" && CH::TealSuite.Common.ShortcutStore.Get("TealClip", "popup") == "Ctrl+Alt+H", "1.3 の settings.json の値を引き継いで shortcuts.json に書く");
            // Teal Hub が解除した → 読み直し
            CH::TealSuite.Common.ShortcutStore.Set("TealClip", "Teal Clip", "popup", "履歴を呼び出す", CH::TealSuite.Common.HotkeySpec.None);
            Check(hk.Reload(CH::TealSuite.Common.ShortcutStore.Get("TealClip", "popup")) && hk.Current.IsEmpty && !hk.Active, "Teal Hub で解除 → 読み直して登録を外す");
            CH::TealSuite.Common.ShortcutStore.Set("TealClip", "Teal Clip", "popup", "履歴を呼び出す", CH::TealSuite.Clip.TrayContext.DefaultHotkey);
            Check(hk.Reload(CH::TealSuite.Common.ShortcutStore.Get("TealClip", "popup")) && hk.Current.ToString() == "Ctrl+Shift+V" && hk.Active, "Teal Hub で変更 → 読み直して登録");
            // Teal Hub が使っている値とは重ならない
            CH::TealSuite.Common.ShortcutStore.Set("TealHub", "Teal Hub", "open", "画面を開く / 隠す", CH::TealSuite.Common.HotkeySpec.Of(CH::TealSuite.Common.HotMods.Alt, Keys.H));
            CH::TealSuite.Common.HotkeySpec altH; CH::TealSuite.Common.HotkeySpec.TryParse("Alt+H", out altH);
            var err = hk.Apply(altH);
            Check(err != null && err.Contains("Teal Hub") && hk.Current.ToString() == "Ctrl+Shift+V", "Teal Clip の設定画面でも Teal Hub の Alt+H と重ならない (" + err + ")");
            Check(hk.Apply(CH::TealSuite.Common.HotkeySpec.None) == null && hk.Current.IsEmpty && CH::TealSuite.Common.ShortcutStore.Get("TealClip", "popup") == "none", "Teal Clip の設定画面で解除して保存");
            hk.Dispose();
        }
        finally
        {
            CH::TealSuite.Common.GlobalHotkey.FakeRegister = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    /// <summary>画面: カードのキー表示・一覧・変更画面の解除・常駐 (Alt+H で開閉)・アップデートの入口</summary>
    public static void RunHubUi(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        FreshData(tmp, "shortcut-ui");
        Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        C.Startup.RunKey = new C.MemoryRunKey();
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
            mgr.SignalReload = id => true;
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
            Check(f.Visible, "後ろにあれば前面に出す");

            var clipCard = f.CardFor("TealClip");
            var passCard = f.CardFor("TealPass");
            Check(clipCard.Chips.Count == 2 && clipCard.Chips[0].Cap.Text.Contains("Ctrl") && clipCard.Chips.Any(c => c.Slot.Label == "画面を開く"), "Teal Clip のカード: 履歴 (Ctrl + Shift + V) と開く");
            Check(passCard.Chips.Count == 1 && passCard.Chips[0].Cap.State == H.KeyCapState.None, "Teal Pass のカード: 開く (なし)");

            var pslot = passCard.Chips[0].Slot;
            Check(pslot.Apply(P("Alt+P")) == null && passCard.Chips[0].Cap.Text.Contains("Alt") && passCard.Chips[0].Cap.State == H.KeyCapState.Normal, "変更するとカードの表示も変わる");

            f.ShowShortcutList();
            var list = f.ListForm;
            Check(list != null && list.Rows.Count == mgr.Slots.Count && list.Rows.Count == 6, "ショートカット一覧に全部の行 (" + (list == null ? 0 : list.Rows.Count) + ")");
            var prow = list.Rows.First(r => r.Slot == pslot);
            Check(prow.Cap.Text.Contains("Alt") && prow.Clear.Enabled, "一覧にも Alt + P と「解除」");
            Check(f.ClearShortcut(pslot, false) && pslot.Current.IsEmpty, "解除");
            list.RefreshValues();
            Check(prow.Cap.State == H.KeyCapState.None && !prow.Clear.Enabled, "解除すると一覧は「なし」になり、解除ボタンは押せない");

            // 変更画面の「解除」
            var vslot = mgr.Find("TealVolume", "open");
            var ef = f.EditShortcut(vslot);
            ef.ClearForTest(); ef.SaveForTest();
            Check(vslot.Current.IsEmpty && C.ShortcutStore.Get("TealVolume", "open") == "none", "変更画面の「解除」→ 保存でなしにできる");
            if (f.EditForm != null) f.EditForm.Close();
            list.Close();

            // アップデートの入口
            var pkg = Path.Combine(tmp, "TealSuite-9.9.9.tealupdate");
            File.WriteAllText(pkg, "x");
            f.OpenUpdateFile(pkg);
            Check(launched.Count == 1 && launched[0] == "TealUpdater.exe \"" + Path.GetFullPath(pkg) + "\"", "アップデート ファイルを開くと TealUpdater.exe に渡す: " + (launched.Count > 0 ? launched[0] : ""));
            Check(!H.UpdaterLauncher.HasBackups, "backup がなければ「前のバージョンに戻す」は案内だけ");

            tray.ExitApp();
            Check(tray.IsExited && !mgr.IsActive(mgr.Self), "終了するとショートカットも外す");
        }
        finally
        {
            C.GlobalHotkey.FakeRegister = null;
            H.FileAssoc.Store = null;
            H.UpdaterLauncher.StartProcess = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }
}
