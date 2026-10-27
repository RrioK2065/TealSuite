// スタートアップ (Windows の起動時に自動で起動) のテスト
// レジストリの代わりに MemoryRunKey / FileRunKey、shell:startup の代わりに一時フォルダを使う。
extern alias AM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using S = AM::TealSuite.Common.Startup;
using AM::TealSuite.Common;

static class StartupTests
{
    static Action<bool, string> Check;

    static string Win(string p) { return p; }

    public static void Run(Action<bool, string> check, string tmp)
    {
        Check = check;
        var root = Path.Combine(tmp, "startup");
        Directory.CreateDirectory(root);
        var exeDir = Path.Combine(root, "Program Files", "TealSuite 新しい場所", "apps", "TealClip");
        Directory.CreateDirectory(exeDir);
        var exe = Path.Combine(exeDir, "TealClip.exe");
        File.WriteAllText(exe, "");

        // ----- 名前とコマンドライン
        Check(S.ValueName("TealClip") == "TealSuite.TealClip", "Run キーの値の名前は TealSuite.<id>");
        var cmd = S.CommandFor(exe);
        Check(cmd == "\"" + exe + "\" --startup", "コマンドは \"<フルパス>\" --startup (空白・日本語を含むパスも引用符で囲む)");
        Check(S.ParsePath(cmd) == exe, "引用符つきのコマンドから exe のパスを取り出す");
        Check(S.ParsePath(@"C:\Program Files\Old App\ClipboardHistory.exe /tray") == @"C:\Program Files\Old App\ClipboardHistory.exe", "引用符なし・空白入りのパス");
        Check(S.ParsePath(@"C:\x\a.exe") == @"C:\x\a.exe" && S.ParsePath("") == null && S.ParsePath(null) == null, "引数なし・空");
        Environment.SetEnvironmentVariable("TEALTEST_DIR", root);
        Check(S.ParsePath("%TEALTEST_DIR%/x.exe --startup") == root + "/x.exe", "環境変数 (%LOCALAPPDATA% など) を展開");
        Check(S.SamePath(exe, Path.Combine(exeDir, ".", "TealClip.exe")) && !S.SamePath(exe, exe + "x") && !S.SamePath(null, exe), "同じファイルかどうかの判定");
        Check(S.HasStartupArg(new[] { "--startup" }) && S.HasStartupArg(new[] { "/show", "/STARTUP" }) && !S.HasStartupArg(new[] { "/show" }) && !S.HasStartupArg(null), "--startup 引数の判定");

        // ----- オン/オフ・登録先の違い
        var key = new MemoryRunKey();
        S.RunKey = key;
        S.Folder = new StartupFolder(Path.Combine(root, "Startup"), p => null);
        Check(S.Get("TealClip", exe).Status == StartupStatus.Off, "最初はオフ");
        S.Enable("TealClip", exe);
        var st = S.Get("TealClip", exe);
        Check(st.Status == StartupStatus.On && st.Registered && key.Values["TealSuite.TealClip"] == cmd, "オンにすると HKCU\\…\\Run に書く: " + key.Values["TealSuite.TealClip"]);
        Check(S.Get("TealClip", Path.Combine(root, "別の場所", "TealClip.exe")).Status == StartupStatus.PathMismatch, "exe を移動すると「登録先が違う」");
        var moved = Path.Combine(root, "移動先", "TealClip.exe");
        var mm = S.Get("TealClip", moved);
        Check(mm.Registered && mm.RegisteredPath == exe, "登録先が違っても登録中として扱い、登録先を表示できる");
        S.Enable("TealClip", moved);
        Check(S.Get("TealClip", moved).Status == StartupStatus.On, "登録し直すと直る");
        key.Disabled.Add("TealSuite.TealClip");
        Check(S.Get("TealClip", moved).Status == StartupStatus.DisabledByUser && !S.Get("TealClip", moved).Registered, "タスク マネージャーで無効にされた状態を読む");
        S.Enable("TealClip", moved);
        Check(S.Get("TealClip", moved).Status == StartupStatus.On && key.Disabled.Count == 0, "オンにし直すとタスク マネージャーの無効も解除");
        S.Disable("TealClip");
        Check(S.Get("TealClip", moved).Status == StartupStatus.Off && !key.Values.ContainsKey("TealSuite.TealClip"), "オフにすると値を消す");
        bool threw = false;
        try { S.Disable("TealClip"); S.Disable("TealPass"); } catch { threw = true; }
        Check(!threw, "登録がないときにオフにしてもエラーにならない");
        key.Values["OneDrive"] = "\"C:\\Users\\taro\\OneDrive.exe\" /background";
        S.Enable("TealPass", exe); S.Disable("TealPass");
        Check(key.Values.Count == 1 && key.Values.ContainsKey("OneDrive"), "ほかのアプリの登録には触れない");

        // ----- 書き込みエラーは例外にせず理由を返す
        S.RunKey = new FailingRunKey();
        var err = S.TrySet("TealClip", exe, true);
        Check(err != null && err.Contains("アクセス"), "書き込めないときは理由を返す (" + err + ")");
        S.RunKey = key;

        // ----- 古い自動起動 (1.0 の exe・手作業のショートカット)
        var startupDir = Path.Combine(root, "Startup");
        Directory.CreateDirectory(startupDir);
        var targets = new Dictionary<string, string>();
        S.Folder = new StartupFolder(startupDir, p => { string t; return targets.TryGetValue(Path.GetFileName(p), out t) ? t : null; });
        File.WriteAllText(Path.Combine(startupDir, "ClipboardHistory.exe - ショートカット.lnk"), "lnk");
        targets["ClipboardHistory.exe - ショートカット.lnk"] = @"C:\Users\taro\AppSuite\apps\ClipboardHistory\ClipboardHistory.exe";
        File.WriteAllText(Path.Combine(startupDir, "コピー履歴.lnk"), "lnk");           // 実行先が TealClip.exe (名前からは分からない)
        targets["コピー履歴.lnk"] = @"C:\Tools\TealSuite\apps\TealClip\TealClip.exe";
        File.WriteAllText(Path.Combine(startupDir, "TealClip.exe - ショートカット.lnk"), "lnk"); // 実行先が読めない → 名前で判定
        File.WriteAllText(Path.Combine(startupDir, "Dropbox.lnk"), "lnk");
        targets["Dropbox.lnk"] = @"C:\Program Files\Dropbox\Dropbox.exe";
        File.WriteAllText(Path.Combine(startupDir, "desktop.ini"), "");
        key.Values["ClipboardHistory"] = @"C:\AppSuite\apps\ClipboardHistory\ClipboardHistory.exe";
        S.Enable("TealClip", exe);   // 正式な登録は「古い」扱いしない
        var apps = new[]
        {
            new KeyValuePair<string, string[]>("TealHub", new[] { "AppManager" }),
            new KeyValuePair<string, string[]>("TealClip", new[] { "ClipboardHistory" }),
            new KeyValuePair<string, string[]>("TealMemo", new[] { "Memo" }),
        };
        var found = S.FindLegacy(apps);
        Func<string, bool> has = n => found.Any(f => Path.GetFileName(f.Name) == n && f.AppId == "TealClip");
        Check(has("ClipboardHistory") && found.First(f => f.Name == "ClipboardHistory").Kind == LegacyKind.RunValue, "レジストリの ClipboardHistory.exe を検出");
        Check(has("ClipboardHistory.exe - ショートカット.lnk"), "スタートアップ フォルダーの ClipboardHistory.exe のショートカットを検出");
        Check(has("コピー履歴.lnk"), "手作業で置いた TealClip.exe のショートカットを検出 (実行先で判定)");
        Check(has("TealClip.exe - ショートカット.lnk"), "実行先が読めないショートカットは名前で判定");
        Check(found.Count == 4 && !found.Any(f => f.Name.Contains("Dropbox") || f.Name == "OneDrive" || f.Name.StartsWith("TealSuite.")), "関係ないもの・正式な登録は対象外 (" + found.Count + " 件)");
        Check(found.First(f => f.Name == "ClipboardHistory").Describe().Contains("ClipboardHistory.exe"), "表示用の説明: " + found.First(f => f.Name == "ClipboardHistory").Describe());

        Check(found.First(f => f.Name == "ClipboardHistory").ExeName == "ClipboardHistory.exe" && found.First(f => f.Name == "ClipboardHistory").Where == "レジストリ「ClipboardHistory」", "お知らせ用の短い表示 (exe 名と場所)");
        Check(S.ShortPath(@"D:\Old\AppSuite\apps\TealPass\TealPass.exe", 20) == @"D:\…\TealPass\TealPass.exe" && S.ShortPath(@"C:\a.exe") == @"C:\a.exe", "長い登録先は縮めて表示");

        // 置き換え: 削除せず退避し、正式な登録に切り替える
        var backup = Path.Combine(root, "data", "removed-startup");
        S.Disable("TealClip");
        S.ReplaceLegacy(found, "TealClip", exe, backup);
        Check(S.Get("TealClip", exe).Status == StartupStatus.On, "置き換えると TealSuite.TealClip が登録される");
        Check(!key.Values.ContainsKey("ClipboardHistory") && key.Values.ContainsKey("OneDrive"), "古いレジストリの値だけを消す");
        Check(File.ReadAllText(Path.Combine(backup, "removed-startup.txt")).Contains("ClipboardHistory.exe"), "消した値の内容は removed-startup.txt に記録");
        Check(File.Exists(Path.Combine(backup, "ClipboardHistory.exe - ショートカット.lnk")) && File.Exists(Path.Combine(backup, "コピー履歴.lnk")) && !File.Exists(Path.Combine(startupDir, "コピー履歴.lnk")), "ショートカットは削除せず退避フォルダへ移動");
        Check(File.Exists(Path.Combine(startupDir, "Dropbox.lnk")) && File.Exists(Path.Combine(startupDir, "desktop.ini")), "ほかのショートカットはそのまま");
        Check(S.FindLegacy(apps).Count == 0, "置き換え後は何も見つからない");
        File.WriteAllText(Path.Combine(startupDir, "コピー履歴.lnk"), "again");
        S.RemoveLegacy(S.FindLegacy(apps)[0], backup);
        Check(File.Exists(Path.Combine(backup, "コピー履歴 (2).lnk")), "同じ名前を退避するときは上書きせず (2) を付ける");

        // ----- ファイル版 (mono での画面確認用)
        var file = Path.Combine(root, "runkey.txt");
        var fk = new FileRunKey(file);
        fk.Set("TealSuite.TealMemo", "\"C:\\ユーザー\\テスト\\TealMemo.exe\" --startup");
        fk.Disabled.Add("TealSuite.TealPass"); fk.Set("TealSuite.TealPass", "\"C:\\x\\TealPass.exe\" --startup");
        var fk2 = new FileRunKey(file);
        Check(fk2.Get("TealSuite.TealMemo") == "\"C:\\ユーザー\\テスト\\TealMemo.exe\" --startup" && fk2.IsDisabledByUser("TealSuite.TealPass"), "TEALSUITE_RUNKEY_FILE の読み書き (日本語のパス・無効の印)");
        fk2.Delete("TealSuite.TealMemo"); fk2.ClearDisabled("TealSuite.TealPass");
        var fk3 = new FileRunKey(file);
        Check(fk3.Get("TealSuite.TealMemo") == null && !fk3.IsDisabledByUser("TealSuite.TealPass") && fk3.Get("TealSuite.TealPass") != null, "削除・無効の解除も保存される");
    }

    class FailingRunKey : MemoryRunKey
    {
        public override void Set(string name, string command) { throw new UnauthorizedAccessException("レジストリへのアクセスが拒否されました"); }
    }

    /// <summary>Teal Hub の画面: 設定ページのスタートアップ (常駐オンのときだけ)・Teal Hub 自身・お知らせ</summary>
    public static void RunHub(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var root = Path.Combine(tmp, "startup-hub");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var startupDir = Path.Combine(root, "Startup");
        Directory.CreateDirectory(startupDir);
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", Path.Combine(root, "data"));
        Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
        AppInfo.Init(new AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        var key = new MemoryRunKey();
        S.RunKey = key;
        S.Folder = new StartupFolder(startupDir, p => null);
        GlobalHotkey.FakeRegister = s => null;
        try
        {
            using (var f = new AM::TealSuite.Hub.MainForm())
            {
                f.LoadManifest();
                f.Shortcuts.SignalReload = id => true;
                var clip = f.Cards.First(c => c.App.Id == "TealClip");
                var pass = f.Cards.First(c => c.App.Id == "TealPass");
                Check(f.Cards.All(c => !c.SummaryText.Contains("スタートアップ: オン")), "最初はどのアプリもスタートアップはオフ");
                Check(f.ShowSettings("TealClip") && f.SettingsPage.StartupSwitch.Enabled && !f.SettingsPage.StartupSwitch.Checked, "Teal Clip (常駐オン) の設定ページでスタートアップを選べる");
                f.SettingsPage.StartupSwitch.Checked = true;
                Check(key.Get("TealSuite.TealClip") == S.CommandFor(clip.App.ExePath), "設定ページのトグルをオンにすると登録: " + key.Get("TealSuite.TealClip"));
                f.SettingsPage.StartupSwitch.Checked = false;
                Check(key.Get("TealSuite.TealClip") == null, "オフにすると登録を消す");

                // 常駐オフのアプリに古い登録が残っている (登録先も違う)
                key.Values["TealSuite.TealPass"] = "\"D:\\古い場所\\TealPass.exe\" --startup";
                f.RefreshStartup();
                f.ShowSettings("TealPass");
                var page = f.SettingsPage;
                Check(page.StartupSwitch.Checked && page.StartupSwitch.Enabled && !page.ResidentSwitch.Checked, "常駐オフでも登録が残っていれば表示し、オフにはできる");
                Check(!f.SetAppStartup(pass.App, true) && key.Get("TealSuite.TealPass").Contains("古い場所"), "常駐オフのまま「登録し直す」ことはしない");
                page.StartupSwitch.Checked = false;
                Check(key.Get("TealSuite.TealPass") == null && !page.StartupSwitch.Enabled, "残っていた登録を消すと、トグルは選べなくなる");

                // 常駐オンで登録先が違う → 「この場所で登録し直す」
                f.ShowSettings("TealClip");
                key.Values["TealSuite.TealClip"] = "\"D:\\古い場所\\TealClip.exe\" --startup";
                f.RefreshStartup();
                Check(page.StartupSwitch.Checked && page.StartupFix.Text == "この場所で登録し直す" && page.StartupNoteText.Contains("古い場所"), "登録先の違いを表示 (" + page.StartupNoteText + ")");
                f.SetAppStartup(clip.App, true);   // 「この場所で登録し直す」と同じ
                Check(key.Get("TealSuite.TealClip") == S.CommandFor(clip.App.ExePath) && !page.StartupNoteText.Contains("古い場所"), "「この場所で登録し直す」で今の場所に直る");
                Check(clip.SummaryText.EndsWith("スタートアップ: オン"), "カードの要約にも反映 (" + clip.SummaryText + ")");

                // Teal Hub 自身
                f.ShowSettings(null);
                Check(page.IsSelfPage && page.StartupSwitch.Enabled, "Teal Hub の設定ページ");
                page.StartupSwitch.Checked = true;
                Check(key.Get("TealSuite.TealHub") == S.CommandFor(S.CurrentExe), "Teal Hub 自身もスタートアップに登録できる");
                key.Values["TealSuite.TealHub"] = "\"C:\\old\\TealHub.exe\" --startup";
                f.RefreshStartup();
                f.ShowList();
                Check(f.NoticeCount == 1 && f.SelfBar_.SummaryText.Contains("登録先が違います"), "Teal Hub 自身の登録先が違うとお知らせを出す");
                f.SetSelfStartup(true);
                Check(f.NoticeCount == 0, "登録し直すとお知らせが消える");

                // 1.0 の自動起動
                key.Values["ClipboardHistory"] = "C:\\AppSuite\\apps\\ClipboardHistory\\ClipboardHistory.exe";
                File.WriteAllText(Path.Combine(startupDir, "ClipboardHistory.exe - ショートカット.lnk"), "lnk");
                S.TrySet("TealClip", clip.App.ExePath, false);
                f.ScanLegacy();
                Check(f.LegacyItems.Count == 2 && f.NoticeCount == 1, "1.0 の ClipboardHistory.exe の自動起動を見つけてお知らせ (" + f.LegacyItems.Count + " 件)");
                Check(key.Values.ContainsKey("ClipboardHistory") && File.Exists(Path.Combine(startupDir, "ClipboardHistory.exe - ショートカット.lnk")), "ボタンを押すまでは何も変えない");
                f.ReplaceLegacy("TealClip", false);
                Check(!key.Values.ContainsKey("ClipboardHistory") && !File.Exists(Path.Combine(startupDir, "ClipboardHistory.exe - ショートカット.lnk"))
                    && File.Exists(Path.Combine(AM::TealSuite.Hub.MainForm.RemovedStartupDir, "ClipboardHistory.exe - ショートカット.lnk")), "「Teal Clip に置き換える」で古い登録を退避");
                Check(key.Get("TealSuite.TealClip") == S.CommandFor(clip.App.ExePath) && clip.SummaryText.EndsWith("スタートアップ: オン") && f.NoticeCount == 0, "Teal Clip がスタートアップに入り、お知らせが消える");
                f.Shortcuts.Dispose();
            }
        }
        finally
        {
            GlobalHotkey.FakeRegister = null;
            Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }
}
