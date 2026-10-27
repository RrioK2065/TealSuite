// TealSuite スモークテスト (mono で実行: tests/run_tests.sh)
// 各 exe を extern alias で参照し、画面以外のロジックを確かめる。
extern alias PM;
extern alias AM;
extern alias MEMO;
extern alias CH;
extern alias VM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

static class SmokeTests
{
    static int pass, fail;
    static void Check(bool ok, string name)
    {
        if (ok) { pass++; Console.WriteLine("  ok   " + name); }
        else { fail++; Console.WriteLine("  FAIL " + name); }
    }
    static void Section(string s) { Console.WriteLine("== " + s); }

    static int Main(string[] args)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "tealsuite-test-" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(tmp);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", Path.Combine(tmp, "data"));
        string dist = args.Length > 0 ? args[0] : "dist/TealSuite";

        if (args.Length > 2 && args[1] == "seed") { Seed(args[2]); return 0; }

        Run("Teal Pass: 生成", TestGenerator);
        Run("Teal Pass: 強さ判定", TestStrength);
        Run("Teal Pass: 暗号化保存 (DPAPI)", TestVault);
        Run("Teal Hub: apps.json と名前", () => TestManifest(dist));
        Run("Teal Hub: 起動と終了", () => TestProcess(tmp));
        Run("Teal Memo: 旧メモアプリの引き継ぎ", () => TestMemo(tmp));
        Run("1.0 (AppSuite) のデータの引き継ぎ", () => TestSuiteMigration(tmp));
        Run("Teal Clip: 履歴とショートカット", TestClipboardHistory);
        Run("Teal Volume: スライダーと表示名", TestVolume);
        Run("全体ショートカット: 使える組み合わせと登録", () => HotkeyTests.RunRules(Check));
        Run("Teal Volume: 常駐 (隠す・開く・Alt+V・終了)", () => HotkeyTests.RunVolumeTray(Check, tmp));
        Run("スタートアップ: 登録・登録先の違い・古い自動起動", () => StartupTests.Run(Check, tmp));
        Run("スタートアップ: Teal Hub の画面", () => StartupTests.RunHub(Check, tmp, dist));
        Run("ショートカット: 持ち主・重複・解除・保存", () => ShortcutTests.RunOwnership(Check, tmp, dist));
        Run("ショートカット: アプリが持つキー (Teal Clip) の読み直し", () => ShortcutTests.RunAppOwned(Check, tmp));
        Run("Teal Hub: 常駐 (Alt+H)・カード・一覧・アップデートの入口", () => ShortcutTests.RunHubUi(Check, tmp, dist));
        Run("アップデート: ファイルの形・パス・バージョン・ハッシュ", () => UpdaterTests.RunFormat(Check));
        Run("アップデート: 確認と apps.json のまとめ直し", () => UpdaterTests.RunPlanAndMerge(Check, tmp));
        Run("アップデート: 適用・失敗時の巻き戻し・前のバージョンに戻す", () => UpdaterTests.RunApply(Check, tmp));
        Run("アップデート: TealUpdater の画面", () => UpdaterTests.RunForm(Check, tmp));
        Run("オンライン アップデート: バージョン・リリース・sha256・接続先・ダウンロード", () => OnlineUpdateTests.RunLogic(Check, tmp, dist));
        Run("オンライン アップデート: お知らせ・ボタン・メニュー・TealUpdater へ", () => OnlineUpdateTests.RunUi(Check, tmp, dist));
        Console.WriteLine();
        Console.WriteLine("結果: " + pass + " ok / " + fail + " fail");
        try { Directory.Delete(tmp, true); } catch { }
        return fail == 0 ? 0 : 1;
    }

    static void Run(string name, Action a)
    {
        Section(name);
        try { a(); }
        catch (Exception ex) { Check(false, "例外: " + ex); }
    }

    // ---------- Teal Pass ----------
    static void TestGenerator()
    {
        var o = new PM::TealSuite.Pass.PasswordOptions { Length = 20, Upper = true, Lower = true, Digits = true, Symbols = true, ExcludeAmbiguous = true };
        bool lenOk = true, allSets = true, noAmb = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 2000; i++)
        {
            var p = PM::TealSuite.Pass.PasswordGenerator.Generate(o);
            seen.Add(p);
            lenOk &= p.Length == 20;
            allSets &= p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit) && p.Any(c => PM::TealSuite.Pass.PasswordGenerator.SymbolChars.IndexOf(c) >= 0);
            noAmb &= !p.Any(c => PM::TealSuite.Pass.PasswordGenerator.AmbiguousChars.IndexOf(c) >= 0);
        }
        Check(lenOk, "長さが指定どおり (20)");
        Check(allSets, "選んだ文字種が必ず入る");
        Check(noAmb, "紛らわしい文字を除外");
        Check(seen.Count == 2000, "2000 回生成して重複なし");

        var digitsOnly = new PM::TealSuite.Pass.PasswordOptions { Length = 4, Upper = false, Lower = false, Digits = true, Symbols = false, ExcludeAmbiguous = false };
        var d = PM::TealSuite.Pass.PasswordGenerator.Generate(digitsOnly);
        Check(d.Length == 4 && d.All(char.IsDigit), "数字だけ 4 桁: " + d);
        var none = new PM::TealSuite.Pass.PasswordOptions { Length = 200, Upper = false, Lower = false, Digits = false, Symbols = false };
        var n = PM::TealSuite.Pass.PasswordGenerator.Generate(none);
        Check(n.Length == 64 && n.All(char.IsLower), "全部オフ・長さ超過は補正 (小文字・64 文字)");

        // 一様性 (0..9 を 100000 回: 各 ~10000)
        var counts = new int[10];
        using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
            for (int i = 0; i < 100000; i++) counts[PM::TealSuite.Pass.PasswordGenerator.Uniform(rng, 10)]++;
        Check(counts.All(c => c > 9500 && c < 10500), "乱数が偏らない (" + string.Join(",", counts) + ")");
    }

    static void TestStrength()
    {
        Func<string, int> L = s => PM::TealSuite.Pass.StrengthMeter.Evaluate(s).Level;
        Check(L("") == 0, "空 → とても弱い");
        Check(L("aaaaaaaa") == 0, "aaaaaaaa → とても弱い");
        Check(L("12345678") <= 1, "12345678 → 弱い以下");
        Check(L("Tr0ub4dor") >= 2, "Tr0ub4dor → 普通以上 (" + L("Tr0ub4dor") + ")");
        var g = PM::TealSuite.Pass.PasswordGenerator.Generate(new PM::TealSuite.Pass.PasswordOptions { Length = 20 });
        Check(L(g) == 4, "生成した 20 文字 → とても強い");
    }

    static void TestVault()
    {
        PM::TealSuite.Common.AppInfo.Init("TealPass", "Teal Pass", "");
        var v = new PM::TealSuite.Pass.VaultFile();
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "テスト銀行", Url = "https://bank.example", UserId = "taro", Password = "S3cret-Pa55-あいう", Memo = "秘密の質問: 犬の名前" });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "Mail", UserId = "taro@example.com", Password = "x" });
        PM::TealSuite.Pass.VaultStore.Save(v);
        var path = PM::TealSuite.Common.DataStore.PathOf("vault.dat");
        var raw = File.ReadAllBytes(path);
        var rawText = Encoding.UTF8.GetString(raw) + Encoding.Unicode.GetString(raw);
        Check(File.Exists(path), "vault.dat を保存");
        Check(!rawText.Contains("S3cret") && !rawText.Contains("taro") && !rawText.Contains("テスト銀行"), "ファイルに平文が含まれない");
        var back = PM::TealSuite.Pass.VaultStore.Load();
        Check(back.Entries.Count == 2 && back.Entries[0].Password == "S3cret-Pa55-あいう" && back.Entries[0].Memo == "秘密の質問: 犬の名前", "読み戻して内容が一致");
        PM::TealSuite.Pass.VaultStore.Save(back);
        Check(File.Exists(PM::TealSuite.Common.DataStore.PathOf("vault.bak")), "1 つ前の版を vault.bak に残す");
        raw[raw.Length - 5] ^= 0x55;
        File.WriteAllBytes(path, raw);
        bool threw = false;
        try { PM::TealSuite.Pass.VaultStore.Load(); } catch { threw = true; }
        Check(threw, "改ざんされたファイルは読み込みエラー");
        var aside = PM::TealSuite.Pass.VaultStore.SetAsideBroken();
        Check(aside != null && File.Exists(aside) && !File.Exists(path), "読めないファイルは別名で残す (削除しない)");
    }

    // ---------- Teal Hub ----------
    static void TestManifest(string dist)
    {
        var path = Path.Combine(dist, "apps.json");
        var apps = AM::TealSuite.Hub.Manifest.Load(path);
        Check(apps.Count == 4, "Teal Hub 自身を除いて 4 件 (" + string.Join(", ", apps.Select(a => a.Id)) + ")");
        Check(apps.All(a => File.Exists(a.ExePath)), "全 exe がビルド出力に存在");
        Check(apps.All(a => a.IconPath != null && File.Exists(a.IconPath)), "全アイコンが存在");
        Check(apps.All(a => a.Version == "1.5.0"), "バージョン 1.5.0");
        Check(apps.All(a => a.Name.StartsWith("Teal ") && !string.IsNullOrEmpty(a.Role)), "名前は Teal ○○ で役割つき (" + string.Join(", ", apps.Select(a => a.Name + "/" + a.Role)) + ")");
        Check(apps.All(a => a.Exe == "apps/" + a.Id + "/" + a.Id + ".exe"), "exe は apps/<id>/<id>.exe");
        // exe 内の AppSpec と apps.json の名前・役割・バージョンが一致し、旧 ID が登録されている
        var legacy = new Dictionary<string, string> { { "TealHub", "AppManager" }, { "TealPass", "PasswordMaker" }, { "TealMemo", "Memo" }, { "TealClip", "ClipboardHistory" }, { "TealVolume", "VolumeMixer" } };
        var all = AM::TealSuite.Hub.Manifest.Load(path, true);
        Check(all.Count == 5 && all.All(a => legacy.ContainsKey(a.Id)), "登録は 5 件 (Teal Hub を含む)");
        foreach (var a in all)
        {
            var asm = System.Reflection.Assembly.LoadFrom(a.ExePath);
            var prog = asm.GetTypes().First(t => t.Name == "Program" && t.GetField("Spec") != null);
            object spec = prog.GetField("Spec").GetValue(null);
            Func<string, object> F = n => spec.GetType().GetField(n).GetValue(spec);
            var ids = (string[])F("LegacyIds");
            var ver = asm.GetName().Version;
            Check((string)F("Id") == a.Id && (string)F("Name") == a.Name && (string)F("Role") == a.Role && ids.Length == 1 && ids[0] == legacy[a.Id] && ver.ToString(3) == a.Version
                && a.LegacyIds != null && a.LegacyIds.SequenceEqual(ids),
                a.Name + " — " + a.Role + "  (" + Path.GetFileName(a.ExePath) + ", v" + ver.ToString(3) + ", 旧 ID " + ids[0] + ")");
        }
    }

    static void TestProcess(string tmp)
    {
        var dir = Path.Combine(tmp, "pc");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "dummy.sh");
        File.WriteAllText(script, "#!/bin/sh\ntrap '' TERM\nwhile true; do sleep 0.2; done\n");
        Process.Start("chmod", "+x " + script).WaitForExit();
        File.WriteAllText(Path.Combine(dir, "apps.json"), "{\"manifestVersion\":1,\"apps\":[{\"id\":\"Dummy\",\"name\":\"ダミー\",\"exe\":\"dummy.sh\",\"version\":\"1.0.0\"}]}");
        var apps = AM::TealSuite.Hub.Manifest.Load(Path.Combine(dir, "apps.json"));
        var ctl = new AM::TealSuite.Hub.ProcessControl();
        Check(ctl.Snapshot(apps)["Dummy"].Count == 0, "起動前は停止中");
        ctl.Start(apps[0]);
        System.Threading.Thread.Sleep(500);
        var pids = ctl.Snapshot(apps)["Dummy"];
        Check(pids.Count == 1, "起動後は実行中 (pid " + string.Join(",", pids) + ")");
        var sw = Stopwatch.StartNew();
        var r = ctl.RequestStop(apps[0], pids, 1500);
        Check(r == AM::TealSuite.Hub.StopResult.StillRunning && sw.ElapsedMilliseconds >= 1400, "終了要求に応じないアプリはタイムアウトで検出 (" + r + ")");
        Check(ctl.Kill(pids), "強制終了できる");
        System.Threading.Thread.Sleep(300);
        Check(ctl.Snapshot(apps)["Dummy"].Count == 0, "終了後は停止中");

        // 素直に終わるアプリ
        var quick = Path.Combine(dir, "quick.sh");
        File.WriteAllText(quick, "#!/bin/sh\nsleep 30\n");
        Process.Start("chmod", "+x " + quick).WaitForExit();
        File.WriteAllText(Path.Combine(dir, "apps.json"), "{\"manifestVersion\":1,\"apps\":[{\"id\":\"Quick\",\"name\":\"q\",\"exe\":\"quick.sh\",\"version\":\"1.0.0\"}]}");
        var apps2 = AM::TealSuite.Hub.Manifest.Load(Path.Combine(dir, "apps.json"));
        ctl.Start(apps2[0]);
        System.Threading.Thread.Sleep(300);
        var p2 = ctl.Snapshot(apps2)["Quick"];
        Check(p2.Count == 1, "2 つ目のアプリも検出");
        Check(ctl.Kill(p2) && ctl.Snapshot(apps2)["Quick"].Count == 0, "終了後に停止中へ戻る");
    }

    // ---------- Teal Memo ----------
    static void TestMemo(string tmp)
    {
        var legacy = Path.Combine(tmp, "legacy");
        Directory.CreateDirectory(legacy);
        var legacyFile = Path.Combine(legacy, "notes.json");
        File.WriteAllText(legacyFile, "{\"version\":1,\"notes\":[{\"id\":\"a\",\"text\":\"買い物\\n牛乳\",\"created\":\"2026-01-01T00:00:00Z\",\"updated\":\"2026-01-02T00:00:00Z\"}]}");
        Environment.SetEnvironmentVariable("MEMOAPP_DATA_DIR", legacy);
        MEMO::TealSuite.Common.AppInfo.Init("TealMemo", "Teal Memo", "");
        int n = MEMO::TealSuite.Memo.NoteStore.MigrateLegacy();
        Check(n == 1, "旧メモアプリのデータを 1 件引き継ぎ");
        Check(File.Exists(legacyFile), "旧データは消さない");
        Check(MEMO::TealSuite.Memo.NoteStore.MigrateLegacy() == -1 || MEMO::TealSuite.Memo.NoteStore.MigrateLegacy() == 0, "2 回目は引き継がない (上書きしない)");
    }

    // ---------- AppSuite 1.0 → TealSuite 1.1 のデータ引き継ぎ ----------
    static void TestSuiteMigration(string tmp)
    {
        string oldRoot = Path.Combine(tmp, "AppSuite"), newRoot = Path.Combine(tmp, "TealSuite");
        // 1.0 と同じ場所・同じ暗号鍵で保存した vault.dat を用意
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldRoot);
        PM::TealSuite.Common.AppInfo.Init("PasswordMaker", "PasswordMaker", "");
        var v = new PM::TealSuite.Pass.VaultFile();
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "旧データ", UserId = "hanako", Password = "Old-Pa55" });
        PM::TealSuite.Pass.VaultStore.Save(v);
        PM::TealSuite.Common.DataStore.Save("window.json", new PM::TealSuite.Common.WindowState());
        var oldDir = PM::TealSuite.Common.DataStore.AppDir;
        var oldFiles = Directory.GetFiles(oldDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        var oldBytes = File.ReadAllBytes(Path.Combine(oldDir, "vault.dat"));

        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", newRoot);
        Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", oldRoot);
        PM::TealSuite.Common.AppInfo.Init(new PM::TealSuite.Common.AppSpec { Id = "TealPass", Name = "Teal Pass", LegacyIds = new[] { "PasswordMaker" } });
        var from = PM::TealSuite.Common.DataStore.MigrateLegacyData();
        var newDir = PM::TealSuite.Common.DataStore.AppDir;
        Check(from != null && Path.GetFileName(newDir) == "TealPass" && File.Exists(Path.Combine(newDir, "vault.dat")), "AppSuite\\PasswordMaker → TealSuite\\TealPass にコピー");
        Check(File.Exists(Path.Combine(newDir, "window.json")) && File.Exists(Path.Combine(newDir, PM::TealSuite.Common.DataStore.MigratedMarker)), "設定ファイルと引き継ぎ記録 (migrated-from.txt)");
        var back = PM::TealSuite.Pass.VaultStore.Load();
        Check(back.Entries.Count == 1 && back.Entries[0].Password == "Old-Pa55", "引き継いだ vault.dat を新しい ID で復号できる");
        Check(Directory.GetFiles(oldDir).Select(Path.GetFileName).OrderBy(x => x).SequenceEqual(oldFiles) && File.ReadAllBytes(Path.Combine(oldDir, "vault.dat")).SequenceEqual(oldBytes), "元のフォルダは消さず、変更もしない");
        Check(!Directory.Exists(newDir + ".migrating"), "一時フォルダが残らない");
        back.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "新規", Password = "n" });
        PM::TealSuite.Pass.VaultStore.Save(back);
        Check(PM::TealSuite.Common.DataStore.MigrateLegacyData() == null && PM::TealSuite.Pass.VaultStore.Load().Entries.Count == 2, "2 回目は引き継がない (新しいデータを上書きしない)");

        PM::TealSuite.Common.AppInfo.Init(new PM::TealSuite.Common.AppSpec { Id = "TealX", Name = "x", LegacyIds = new[] { "Nothing" } });
        Check(PM::TealSuite.Common.DataStore.MigrateLegacyData() == null && !Directory.Exists(PM::TealSuite.Common.DataStore.AppDir), "旧データがなければ何もしない");

        // メモ: AppSuite\Memo を引き継いだら、旧メモアプリ (MemoApp) からは重ねて取り込まない
        Directory.CreateDirectory(Path.Combine(oldRoot, "Memo"));
        File.WriteAllText(Path.Combine(oldRoot, "Memo", "notes.json"), "{\"version\":1,\"notes\":[{\"id\":\"m1\",\"text\":\"1.0 のメモ\",\"created\":\"2026-02-01T00:00:00Z\",\"updated\":\"2026-02-01T00:00:00Z\"}]}");
        MEMO::TealSuite.Common.AppInfo.Init(new MEMO::TealSuite.Common.AppSpec { Id = "TealMemo", Name = "Teal Memo", LegacyIds = new[] { "Memo" } });
        Check(MEMO::TealSuite.Common.DataStore.MigrateLegacyData() != null, "AppSuite\\Memo → TealSuite\\TealMemo");
        Check(MEMO::TealSuite.Memo.NoteStore.MigrateLegacy() <= 0, "引き継いだ後は MemoApp から二重に取り込まない");
        string w;
        var notes = MEMO::TealSuite.Memo.NoteStore.Load(out w);
        Check(notes.Notes.Count == 1 && notes.Notes[0].Text == "1.0 のメモ", "1.0 のメモがそのまま読める");
        Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", null);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", Path.Combine(tmp, "data"));
    }

    // ---------- Teal Clip / Teal Volume (CHTests.cs / VMTests.cs) ----------
    static void TestClipboardHistory() { CHTests.Run(Check); }
    static void TestVolume() { VMTests.Run(Check); }

    /// <summary>スクリーンショット用の見本データを dir に作る (実データには使わない)</summary>
    static void Seed(string dir)
    {
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", dir);
        PM::TealSuite.Common.AppInfo.Init("TealPass", "Teal Pass", "");
        var v = new PM::TealSuite.Pass.VaultFile();
        Func<int, string> gen = len => PM::TealSuite.Pass.PasswordGenerator.Generate(new PM::TealSuite.Pass.PasswordOptions { Length = len });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "Amazon", Url = "https://www.amazon.co.jp", UserId = "taro@example.com", Password = gen(16), Memo = "プライム会員。支払いは楽天カード", Updated = DateTime.Now.AddDays(-3) });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "Google", Url = "https://accounts.google.com", UserId = "taro.yamada@gmail.com", Password = gen(20), Memo = "2 段階認証はスマホのアプリ\n予備コードは机の引き出し", Updated = DateTime.Now.AddDays(-12) });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "みずほ銀行", Url = "https://www.mizuhobank.co.jp", UserId = "1234567890", Password = gen(12), Memo = "お客さま番号は通帳の表紙裏", Updated = DateTime.Now.AddDays(-40) });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "Netflix", Url = "https://www.netflix.com", UserId = "taro@example.com", Password = "netflix2024", Memo = "家族で共有", Updated = DateTime.Now.AddDays(-80) });
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "会社の VPN", Url = "", UserId = "t.yamada", Password = gen(24), Memo = "90 日ごとに変更", Updated = DateTime.Now.AddDays(-1) });
        PM::TealSuite.Pass.VaultStore.Save(v);

        MEMO::TealSuite.Common.AppInfo.Init("TealMemo", "Teal Memo", "");
        var notes = new MEMO::TealSuite.Memo.NoteFile();
        Action<string, int> add = (t, h) => { var n = MEMO::TealSuite.Memo.Note.Create(); n.Text = t; n.Updated = DateTime.UtcNow.AddHours(-h).ToString("o"); notes.Notes.Add(n); };
        add("買い物リスト\r\n・牛乳\r\n・卵\r\n・食パン\r\n・洗剤 (詰め替え)", 1);
        add("来週の予定\r\n月: 歯医者 10:00\r\n水: 打ち合わせ 15:00 (第2会議室)\r\n金: 飲み会", 5);
        add("読みたい本\r\n・三体\r\n・プロジェクト・ヘイル・メアリー", 30);
        add("Wi-Fi の場所\r\nルーターは本棚の上。再起動はコンセントを抜いて 10 秒", 72);
        MEMO::TealSuite.Memo.NoteStore.Save(notes);
        Console.WriteLine("seeded " + dir);
    }
}
