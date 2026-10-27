// TealUpdater (アップデート ファイルの読み込み・確認・適用・失敗時の巻き戻し・前のバージョンに戻す) のテスト
// 一時フォルダに小さな「スイート」を作り、その中で置き換える (本物の dist には触らない)
extern alias UP;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using U = UP::TealSuite.Updater;

static class UpdaterTests
{
    static Action<bool, string> Check;
    /// <summary>コマンドラインを引数に分ける (テスト用の簡単なもの: "…" で囲んだ部分は 1 つ)</summary>
    static string[] SplitArgs(string cmd)
    {
        var list = new List<string>(); var cur = new StringBuilder(); bool q = false, any = false;
        foreach (var ch in cmd)
        {
            if (ch == '"') { q = !q; any = true; continue; }
            if (ch == ' ' && !q) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; continue; }
            cur.Append(ch);
        }
        if (any || cur.Length > 0) list.Add(cur.ToString());
        return list.ToArray();
    }
    static byte[] B(string s) { return Encoding.UTF8.GetBytes(s); }
    static string Sha(byte[] b) { return U.UpdatePackage.Sha256Of(new MemoryStream(b)); }
    static string Q(string s) { return U.Json.Quote(s); }

    const string InstalledJson = @"{
  ""manifestVersion"": 1,
  ""suite"": ""TealSuite"",
  ""suiteVersion"": ""1.4.0"",
  ""apps"": [
    {""id"": ""TealHub"", ""name"": ""Teal Hub"", ""exe"": ""TealHub.exe"", ""version"": ""1.4.0"", ""self"": true},
    {""id"": ""TealPass"", ""name"": ""Teal Pass"", ""exe"": ""apps/TealPass/TealPass.exe"", ""version"": ""1.4.0"", ""legacyIds"": [""PasswordManager""], ""myNote"": ""利用者のメモ""},
    {""id"": ""MyTool"", ""name"": ""自分で足したツール"", ""exe"": ""C:/Tools/MyTool.exe"", ""version"": ""0.1""}
  ]
}";

    /// <summary>一時フォルダに 1.4.0 のスイート (偽物のファイル) を作る</summary>
    static string MakeSuite(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "apps", "TealPass"));
        Directory.CreateDirectory(Path.Combine(root, "apps", "Old"));
        File.WriteAllText(Path.Combine(root, "apps.json"), InstalledJson);
        File.WriteAllText(Path.Combine(root, "TealHub.exe"), "hub 1.4.0");
        File.WriteAllText(Path.Combine(root, "apps", "TealPass", "TealPass.exe"), "pass 1.4.0");
        File.WriteAllText(Path.Combine(root, "README.txt"), "readme 1.4.0");
        File.WriteAllText(Path.Combine(root, "apps", "Old", "old.dll"), "old");
        return root;
    }

    class Pkg
    {
        public string Version = "1.5.0", MinFrom = "1.4.0", Suite = "TealSuite";
        public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public Dictionary<string, string> FileApp = new Dictionary<string, string>();
        public List<string> Delete = new List<string>();
        public List<string> AppsJson = new List<string>();
        public Dictionary<string, string> BadHash = new Dictionary<string, string>();
        public HashSet<string> Missing = new HashSet<string>();

        public string Manifest()
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":1,\"suite\":" + Q(Suite) + ",\"version\":" + Q(Version) + ",\"minFromVersion\":" + Q(MinFrom) + ",\"notes\":\"テスト\",\"files\":[");
            sb.Append(string.Join(",", Files.Select(kv => "{\"path\":" + Q(kv.Key) + ",\"sha256\":" + Q(BadHash.ContainsKey(kv.Key) ? BadHash[kv.Key] : Sha(kv.Value)) + ",\"size\":" + kv.Value.Length + ",\"app\":" + Q(FileApp.ContainsKey(kv.Key) ? FileApp[kv.Key] : "") + "}")));
            sb.Append("],\"delete\":[" + string.Join(",", Delete.Select(Q)) + "],\"apps\":[" + string.Join(",", AppsJson) + "]}");
            return sb.ToString();
        }

        public string Write(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("update.json").Open(), new UTF8Encoding(false))) w.Write(Manifest());
                foreach (var kv in Files)
                {
                    if (Missing.Contains(kv.Key)) continue;
                    using (var s = z.CreateEntry("files/" + kv.Key).Open()) s.Write(kv.Value, 0, kv.Value.Length);
                }
            }
            return path;
        }
    }

    /// <summary>1.5.0: Teal Hub と Teal Pass を新しく、README を置き換え、古い dll を消し、新しいアプリ Teal Note を足す</summary>
    static Pkg Sample()
    {
        var p = new Pkg();
        p.Files["TealHub.exe"] = B("hub 1.5.0"); p.FileApp["TealHub.exe"] = "TealHub";
        p.Files["apps/TealPass/TealPass.exe"] = B("pass 1.5.0"); p.FileApp["apps/TealPass/TealPass.exe"] = "TealPass";
        p.Files["apps/TealNote/TealNote.exe"] = B("note 1.5.0"); p.FileApp["apps/TealNote/TealNote.exe"] = "TealNote";
        p.Files["README.txt"] = B("readme 1.5.0");
        p.Delete.Add("apps/Old/old.dll");
        p.AppsJson.Add("{\"id\":\"TealHub\",\"name\":\"Teal Hub\",\"exe\":\"TealHub.exe\",\"version\":\"1.5.0\",\"self\":true}");
        p.AppsJson.Add("{\"id\":\"TealPass\",\"name\":\"Teal Pass\",\"exe\":\"apps/TealPass/TealPass.exe\",\"version\":\"1.5.0\",\"legacyIds\":[\"PasswordManager\"]}");
        p.AppsJson.Add("{\"id\":\"TealNote\",\"name\":\"Teal Note\",\"exe\":\"apps/TealNote/TealNote.exe\",\"version\":\"1.5.0\"}");
        return p;
    }

    static string Read(string root, string rel) { var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); return File.Exists(p) ? File.ReadAllText(p) : null; }

    static string Snapshot(string root)
    {
        return string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(root.Length).Replace('\\', '/'))
            .Where(f => !f.StartsWith("/backup/"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => f + "=" + File.ReadAllText(root + f)));
    }

    static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); return false; } catch (T) { return true; } catch { return false; }
    }

    public static void RunFormat(Action<bool, string> check)
    {
        Check = check;
        // MiniJson
        var o = U.Json.ParseObj("{\"a\":1,\"b\":[true,null,\"x\\u3042\\n\"],\"c\":{\"d\":-2.5e3},\"e\":\"日本語\"}");
        Check(o.Int("a", 0) == 1 && o.Arr("b").Count == 3 && (bool)o.Arr("b")[0] && o.Arr("b")[1] == null && (string)o.Arr("b")[2] == "xあ\n" && o.Obj("c").Str("d") == "-2.5e3" && o.Str("e") == "日本語", "JSON を読む (数値・配列・null・エスケープ・日本語)");
        var again = U.Json.ParseObj(U.Json.Write(o));
        Check(U.Json.Write(again) == U.Json.Write(o) && string.Join(",", again.Keys) == "a,b,c,e", "書いて読み直しても同じ (項目の順番も保つ)");
        o.InsertAfter("a", "z", "new");
        Check(string.Join(",", o.Keys) == "a,z,b,c,e", "InsertAfter で指定の位置に足す");
        Check(Throws<FormatException>(() => U.Json.Parse("{\"a\":}")) && Throws<FormatException>(() => U.Json.Parse("[1,2")) && Throws<FormatException>(() => U.Json.Parse("{} x")), "壊れた JSON は FormatException");
        var installed = U.Json.ParseObj(InstalledJson);
        Check(U.Json.ParseObj(U.Json.Write(installed)).Arr("apps").Count == 3, "apps.json の形も読み書きできる");

        // update.json
        var p = U.UpdatePackage.Parse(Sample().Manifest());
        Check(p.Version == "1.5.0" && p.MinFromVersion == "1.4.0" && p.Files.Count == 4 && p.Delete.Count == 1 && p.Apps.Count == 3 && p.Files[0].Sha256.Length == 64 && p.Files[0].Size == 9, "update.json を読む (ファイル 4・削除 1・アプリ 3)");
        Check(p.AppName("TealNote") == "Teal Note", "アプリ名を引ける");

        Func<Func<string, string>, bool> bad = edit => Throws<FormatException>(() => U.UpdatePackage.Parse(edit(Sample().Manifest())));
        Check(bad(s => s.Replace("\"format\":1", "\"format\":2")), "新しい形式 (format 2) は読まない");
        Check(bad(s => s.Replace("\"format\":1,", "")), "format がなければ読まない");
        Check(bad(s => s.Replace("\"version\":\"1.5.0\"", "\"version\":\"1.x\"")), "バージョンの形が違えば読まない");
        Check(bad(s => s.Replace("\"minFromVersion\":\"1.4.0\"", "\"minFromVersion\":\"abc\"")), "minFromVersion の形が違えば読まない");
        Check(bad(s => s.Replace("\"path\":\"README.txt\"", "\"path\":\"TealHub.exe\"")), "同じパスが 2 回あれば読まない");
        Check(bad(s => s.Replace("\"delete\":[", "\"delete\":[\"README.txt\",")), "置き換えと削除の両方にあれば読まない");
        Check(bad(s => s.Replace("{\"id\":\"TealNote\"", "{\"id\":\"TealPass\"")), "apps に同じ id が 2 回あれば読まない");
        Check(bad(s => s.Replace("{\"id\":\"TealNote\",", "{")), "apps に id がなければ読まない");
        var shortHash = Sample(); shortHash.BadHash["README.txt"] = "abc";
        Check(Throws<FormatException>(() => U.UpdatePackage.Parse(shortHash.Manifest())), "sha256 の形が違えば読まない");
        Check(Throws<FormatException>(() => U.UpdatePackage.Parse("{\"format\":1,\"suite\":\"TealSuite\",\"version\":\"1.5.0\"}")), "中身が何もなければ読まない");

        // パスの確認 (スイートのフォルダの外や大事なファイルには書かない)
        var badPaths = new[] { "../evil.exe", "apps/../../x", "/etc/passwd", "C:/Windows/x.dll", "\\\\server\\x", "~/x", "apps.json", "backup/1.4.0/x", ".update-staging/x", "a/./b", "a//b/" };
        Check(badPaths.All(x => U.UpdatePackage.CheckPath(U.UpdatePackage.NormalizePath(x)) != null), "スイートの外・apps.json・backup などのパスは不可");
        Check(U.UpdatePackage.CheckPath(U.UpdatePackage.NormalizePath("apps\\TealPass\\TealPass.exe")) == null && U.UpdatePackage.NormalizePath("apps\\TealPass\\TealPass.exe") == "apps/TealPass/TealPass.exe", "\\ 区切りは / にそろえる");
        Check(bad(s => s.Replace("\"path\":\"README.txt\"", "\"path\":\"../README.txt\"")) && bad(s => s.Replace("\"delete\":[\"apps/Old/old.dll\"", "\"delete\":[\"../x\"")), "update.json に外のパスがあれば読まない");

        // バージョン
        Check(U.VersionUtil.Compare("1.10.0", "1.9.9") > 0 && U.VersionUtil.Compare("1.4", "1.4.0") == 0 && U.VersionUtil.Compare("1.4.0", "1.4.0.1") < 0, "バージョンは数字ごとに比べる (1.10 > 1.9、1.4 = 1.4.0)");
        Check(U.VersionUtil.IsValid("1.4.0") && U.VersionUtil.IsValid("1.4.0.0") && !U.VersionUtil.IsValid("1") && !U.VersionUtil.IsValid("1.4.0-beta") && !U.VersionUtil.IsValid(""), "バージョンの形 (1.4.0 / 1.4.0.0 は可、1・1.4.0-beta は不可)");

        // ハッシュ
        Check(Sha(B("abc")) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "sha256 (abc の既知の値)");
    }

    public static void RunPlanAndMerge(Action<bool, string> check, string tmp)
    {
        Check = check;
        var root = MakeSuite(Path.Combine(tmp, "upd-plan"));
        var inst = U.InstalledSuite.Load(root);
        Check(inst.SuiteVersion == "1.4.0" && inst.Apps.Count == 3 && inst.Find("PasswordManager") != null && inst.Find("PasswordManager").Id == "TealPass", "今のスイートを読む (旧 ID でも見つかる)");

        var plan = U.UpdateEngine.Plan(U.UpdatePackage.Parse(Sample().Manifest()), inst);
        var hub = plan.Changes.First(c => c.Id == "TealHub");
        var note = plan.Changes.First(c => c.Id == "TealNote");
        Check(plan.CanApply && plan.From == "1.4.0" && plan.To == "1.5.0", "1.4.0 → 1.5.0 は入れられる");
        Check(hub.From == "1.4.0" && hub.To == "1.5.0" && !hub.IsNew && hub.Files == 1, "アプリごとに古い → 新しいバージョン (" + hub.Describe + ")");
        Check(note.IsNew && note.Files == 1 && plan.NewApps.Count() == 1, "新しいアプリ (Teal Note) がわかる");
        Check(plan.OtherFiles == 1 && plan.DeleteCount == 1, "共通のファイル (README) 1 個・削除 1 個");

        // Teal Hub がダウンロードしたファイル: 公開されているバージョンと中身のバージョンが違えば断る
        var eng0 = new U.UpdateEngine(root) { ExpectVersion = "1.6.0" };
        var pm = eng0.PlanChecked(U.UpdatePackage.Parse(Sample().Manifest()), inst);
        Check(!pm.CanApply && pm.Problems.Any(x => x.Contains("1.6.0") && x.Contains("1.5.0")), "--expect-version と中身のバージョンが違えば入れない");
        eng0.ExpectVersion = "1.5.0";
        Check(eng0.PlanChecked(U.UpdatePackage.Parse(Sample().Manifest()), inst).CanApply, "--expect-version が同じなら入れられる");
        var ua = U.UpdaterArgs.Parse(new[] { "C:/t/TealSuite-update-1.5.0.tealupdate", "--expect-version", "1.5.0" });
        Check(ua.ExpectVersion == "1.5.0" && ua.Package.EndsWith(".tealupdate") && U.UpdaterArgs.Parse(SplitArgs(ua.ToCommandLine())).ExpectVersion == "1.5.0", "--expect-version の読み取りと受け渡し: " + ua.ToCommandLine());

        Func<Action<Pkg>, U.UpdatePlan> planOf = edit => { var p = Sample(); edit(p); return U.UpdateEngine.Plan(U.UpdatePackage.Parse(p.Manifest()), inst); };
        Check(!planOf(p => p.Version = "1.3.0").CanApply, "古いバージョンは入れない (前に戻すのは「前のバージョンに戻す」)");
        var same = planOf(p => p.Version = "1.4.0");
        Check(same.CanApply && same.Warnings.Count == 1, "同じバージョンは入れ直し (注意だけ)");
        Check(!planOf(p => { p.MinFrom = "1.4.5"; p.Version = "1.6.0"; }).CanApply, "minFromVersion より古いスイートには入れない");
        Check(!planOf(p => p.Suite = "OtherSuite").CanApply, "ほかのスイート用のファイルは入れない");
        Check(!planOf(p => p.AppsJson[2] = "{\"id\":\"TealNote\",\"name\":\"Teal Note\",\"version\":\"1.5.0\"}").CanApply, "新しいアプリに exe がなければ入れない");
        var noJson = Path.Combine(tmp, "upd-nojson"); Directory.CreateDirectory(noJson);
        Check(!U.UpdateEngine.Plan(U.UpdatePackage.Parse(Sample().Manifest()), U.InstalledSuite.Load(noJson)).CanApply, "apps.json がないフォルダには入れない");
        var j13 = InstalledJson.Replace("  \"suiteVersion\": \"1.4.0\",\n", "").Replace("\"version\": \"1.4.0\", \"self\"", "\"version\": \"1.3.0\", \"self\"");
        var old = U.InstalledSuite.Load(MakeSuite(Path.Combine(tmp, "upd-13")));
        File.WriteAllText(Path.Combine(old.Dir, "apps.json"), j13.Replace("\r", ""));
        old = U.InstalledSuite.Load(old.Dir);
        Check(old.SuiteVersion == "1.3.0", "suiteVersion がない 1.3 の apps.json は Teal Hub のバージョンで代用 (" + old.SuiteVersion + ")");

        // apps.json のまとめ直し
        var merged = U.UpdateEngine.MergeAppsJson(inst.AppsJson, U.UpdatePackage.Parse(Sample().Manifest()));
        var apps = merged.Arr("apps").Cast<U.JObj>().ToList();
        Check(merged.Str("suiteVersion") == "1.5.0" && string.Join(",", merged.Keys.Take(3)) == "manifestVersion,suite,suiteVersion", "suiteVersion を新しくする");
        Check(string.Join(",", apps.Select(a => a.Str("id"))) == "TealHub,TealPass,MyTool,TealNote", "新しいアプリは末尾に足し、自分で足したアプリ (MyTool) は残す");
        var pass = apps.First(a => a.Str("id") == "TealPass");
        Check(pass.Str("version") == "1.5.0" && pass.Str("myNote") == "利用者のメモ", "同じアプリはアップデートの項目だけ上書き (利用者の項目は残す)");
        Check(apps.First(a => a.Str("id") == "MyTool").Str("exe") == "C:/Tools/MyTool.exe", "MyTool の中身はそのまま");
        var byLegacy = Sample(); byLegacy.AppsJson[1] = "{\"id\":\"PasswordManager\",\"version\":\"1.5.0\"}";
        var m2 = U.UpdateEngine.MergeAppsJson(inst.AppsJson, U.UpdatePackage.Parse(byLegacy.Manifest()));
        Check(m2.Arr("apps").Count == 4, "旧 ID で書かれていても同じアプリとして扱う (増えない)");
        var m3 = U.UpdateEngine.MergeAppsJson(U.Json.ParseObj(j13), U.UpdatePackage.Parse(Sample().Manifest()));
        Check(m3.Str("suiteVersion") == "1.5.0" && string.Join(",", m3.Keys.Take(3)) == "manifestVersion,suite,suiteVersion", "suiteVersion がなければ suite の後ろに足す");
        Check(U.Json.ParseObj(U.Json.Write(merged)).Arr("apps").Count == 4, "まとめ直した apps.json は読み直せる");
    }

    public static void RunApply(Action<bool, string> check, string tmp)
    {
        Check = check;
        // 成功
        var root = MakeSuite(Path.Combine(tmp, "upd-apply"));
        var before = Snapshot(root);
        var pkgPath = Sample().Write(Path.Combine(tmp, "TealSuite-1.5.0.tealupdate"));
        var pkg = U.UpdatePackage.Read(pkgPath);
        Check(pkg.SourcePath == Path.GetFullPath(pkgPath) && pkg.Files.Count == 4, "zip から読む");
        var eng = new U.UpdateEngine(root) { RetryDelayMs = 1, CopyRetries = 2 };
        var steps = new List<int>(); eng.Progress = (pct, s) => steps.Add(pct);
        var res = eng.Apply(pkg);
        Check(res.Ok && res.Error == null, "アップデートできた" + (res.Error != null ? " (" + res.Error + ")" : ""));
        Check(Read(root, "TealHub.exe") == "hub 1.5.0" && Read(root, "apps/TealPass/TealPass.exe") == "pass 1.5.0" && Read(root, "apps/TealNote/TealNote.exe") == "note 1.5.0" && Read(root, "README.txt") == "readme 1.5.0", "ファイルを置き換え、新しいアプリも入る");
        Check(Read(root, "apps/Old/old.dll") == null, "削除するファイルを消す");
        var inst = U.InstalledSuite.Load(root);
        Check(inst.SuiteVersion == "1.5.0" && inst.Find("TealNote") != null && inst.Find("MyTool") != null && inst.Find("TealPass").Version == "1.5.0", "apps.json: 1.5.0・Teal Note を追加・MyTool は残る");
        Check(!Directory.Exists(Path.Combine(root, ".update-staging")), "作業用フォルダは消す");
        Check(steps.Count > 3 && steps.Last() == 100, "進み具合を知らせる");
        var bdir = Path.Combine(root, "backup", "1.4.0");
        Check(res.BackupDir == bdir && File.ReadAllText(Path.Combine(bdir, "files", "TealHub.exe")) == "hub 1.4.0" && File.ReadAllText(Path.Combine(bdir, "files", "apps", "Old", "old.dll")) == "old" && File.ReadAllText(Path.Combine(bdir, "apps.json")) == InstalledJson, "backup/1.4.0/ に前のファイルと apps.json を残す");
        var list = eng.ListBackups();
        Check(list.Count == 1 && list[0].From == "1.4.0" && list[0].To == "1.5.0" && list[0].Created.SequenceEqual(new[] { "apps/TealNote/TealNote.exe" }) && list[0].Deleted.Count == 1 && list[0].Replaced.Count == 3, "backup.json に置き換え 3・追加 1・削除 1 を記録");

        // 前のバージョンに戻す
        var rr = eng.Restore(list[0]);
        Check(rr.Ok && Snapshot(root) == before, "「前のバージョンに戻す」で 1.4.0 と同じファイルに戻る (追加したアプリも消える)");
        Check(U.InstalledSuite.Load(root).SuiteVersion == "1.4.0" && eng.ListBackups().Count == 0, "戻したら apps.json も 1.4.0、その backup はもう出さない");
        Check(Directory.Exists(bdir), "backup フォルダ自体は消さない");

        // もう一度アップデート → 別の backup フォルダ
        var res2 = eng.Apply(U.UpdatePackage.Read(pkgPath));
        Check(res2.Ok && res2.BackupDir != bdir && Path.GetFileName(res2.BackupDir).StartsWith("1.4.0"), "同じバージョンからもう一度入れると別の backup (" + Path.GetFileName(res2.BackupDir ?? "") + ")");

        // 途中で失敗 → 元に戻す
        root = MakeSuite(Path.Combine(tmp, "upd-fail"));
        before = Snapshot(root);
        eng = new U.UpdateEngine(root) { RetryDelayMs = 1, CopyRetries = 2 };
        int n = 0;
        eng.BeforeReplaceFile = p => { if (++n == 3) throw new IOException("テスト: " + p + " が使用中"); };
        res = eng.Apply(U.UpdatePackage.Read(pkgPath));
        Check(!res.Ok && res.RolledBack && res.RollbackError == null && res.Error.Contains("使用中"), "置き換えの途中で失敗したら元に戻す (" + res.Error + ")");
        Check(Snapshot(root) == before, "ファイルも apps.json も 1.4.0 のまま (途中まで置き換えた分も戻る)");
        Check(!Directory.Exists(Path.Combine(root, "apps", "TealNote")) && !Directory.Exists(Path.Combine(root, ".update-staging")), "作ったフォルダ・作業用フォルダも残さない");
        Check(eng.ListBackups().Count == 0, "失敗したときの backup は残さない (戻す対象にしない)");

        // ハッシュが違う → 何も変えない
        root = MakeSuite(Path.Combine(tmp, "upd-hash"));
        before = Snapshot(root);
        var badPkg = Sample(); badPkg.BadHash["README.txt"] = new string('0', 64);
        var badPath = badPkg.Write(Path.Combine(tmp, "bad-hash.tealupdate"));
        eng = new U.UpdateEngine(root) { RetryDelayMs = 1 };
        int replaced = 0; eng.BeforeReplaceFile = p => replaced++;
        res = eng.Apply(U.UpdatePackage.Read(badPath));
        Check(!res.Ok && !res.RolledBack && replaced == 0 && res.Error.Contains("README.txt"), "ハッシュが合わなければ置き換えを始めない (" + res.Error + ")");
        Check(Snapshot(root) == before && !Directory.Exists(Path.Combine(root, "backup")) && !Directory.Exists(Path.Combine(root, ".update-staging")), "何も変えず、backup も作業用フォルダも残さない");

        // 壊れたファイル
        var missPkg = Sample(); missPkg.Missing.Add("README.txt");
        var missPath = missPkg.Write(Path.Combine(tmp, "missing.tealupdate"));
        Check(Throws<InvalidDataException>(() => U.UpdatePackage.Read(missPath)), "中のファイルが足りなければ開かない");
        var notZip = Path.Combine(tmp, "not-zip.tealupdate"); File.WriteAllText(notZip, "これは zip ではない");
        Check(Throws<InvalidDataException>(() => U.UpdatePackage.Read(notZip)), "zip でなければ開かない");
        var noManifest = Path.Combine(tmp, "no-manifest.tealupdate");
        using (var z = ZipFile.Open(noManifest, ZipArchiveMode.Create)) z.CreateEntry("x.txt");
        Check(Throws<InvalidDataException>(() => U.UpdatePackage.Read(noManifest)), "update.json がなければ開かない");

        // 古いバージョンは Apply でも断る (画面を通さなくても)
        var older = Sample(); older.Version = "1.3.0"; older.MinFrom = "";
        res = eng.Apply(U.UpdatePackage.Read(older.Write(Path.Combine(tmp, "older.tealupdate"))));
        Check(!res.Ok && Snapshot(root) == before, "古いバージョンは Apply でも入れない");
    }

    /// <summary>画面 (確認 → アップデート → 完了 → 元に戻す)。アプリの終了・起動は差し替える</summary>
    public static void RunForm(Action<bool, string> check, string tmp)
    {
        Check = check;
        UP::TealSuite.Common.AppInfo.Init("TealUpdater", "Teal Updater", "");
        var root = MakeSuite(Path.Combine(tmp, "upd-form"));
        var pkgPath = Sample().Write(Path.Combine(tmp, "TealSuite-1.5.0.tealupdate"));
        var closed = new List<string>(); var restarted = new List<string>();
        var running = new List<U.RunningApp>();
        U.UpdaterForm.FindRunning = s => running.ToList();
        U.UpdaterForm.CloseApps = (apps, ms) => { closed.AddRange(apps.Select(a => a.App.Id)); return new List<U.RunningApp>(); };
        U.UpdaterForm.RestartApps = (s, ids, hub) => { restarted.AddRange(ids); if (hub) restarted.Add("(hub)"); return new List<string>(); };
        U.UpdaterForm.AskKill = (o, m) => false;
        try
        {
            var inst = U.InstalledSuite.Load(root);
            running.Add(new U.RunningApp { App = inst.Find("TealPass"), ExePath = Path.Combine(root, "apps", "TealPass", "TealPass.exe"), Pids = new List<int> { 1234 } });
            var eng = new U.UpdateEngine(root) { RetryDelayMs = 1 };
            using (var f = U.UpdaterForm.ForPackage(eng, pkgPath))
            {
                f.Show();
                Check(f.State == U.UpdaterState.Confirm && f.HeaderSubtitle.Contains("1.4.0") && f.HeaderSubtitle.Contains("1.5.0"), "確認画面: " + f.HeaderSubtitle);
                Check(f.Rows.Count == 5 && f.Rows.Any(r => r.NameLabel.Text == "共通のファイル") && f.Rows.Any(r => r.NameLabel.Text == "Teal Note" && r.ValueLabel.Text.Contains("新しいアプリ")) && f.Rows.Any(r => r.NameLabel.Text == "Teal Pass" && r.ValueLabel.Text.Contains("1.4.0") && r.ValueLabel.Text.Contains("1.5.0")), "アプリごとの変更と新しいアプリを表示 (" + f.Rows.Count + " 行)");
                Check(f.InfoText.Contains("Teal Pass") && f.InfoText.Contains("テスト") && f.InfoText.Contains("backup"), "起動中のアプリ・お知らせ・backup の説明");
                Check(f.PrimaryButton.Enabled && f.PrimaryButton.Text == "アップデート", "「アップデート」ボタン");
                f.ClickPrimaryForTest();
                Check(f.WaitIdle(10000) && f.State == U.UpdaterState.Done, "アップデートが終わる (" + f.State + (f.LastResult != null && f.LastResult.Error != null ? ": " + f.LastResult.Error : "") + ")");
                Check(closed.SequenceEqual(new[] { "TealPass" }), "起動中のアプリを先に終了");
                Check(restarted.Contains("TealPass") && restarted.Contains("(hub)"), "終わったら起動し直す (" + string.Join(",", restarted) + ")");
                Check(U.InstalledSuite.Load(root).SuiteVersion == "1.5.0" && f.UndoVisible, "1.5.0 になり、「元に戻す」が出る");
            }
            restarted.Clear();
            using (var f = U.UpdaterForm.ForRollback(new U.UpdateEngine(root)))
            {
                f.Show();
                Check(f.State == U.UpdaterState.Confirm && f.PrimaryButton.Enabled && f.HeaderSubtitle.Contains("1.4.0"), "前のバージョンに戻す画面: " + f.HeaderSubtitle);
                f.ClickPrimaryForTest();
                Check(f.WaitIdle(10000) && f.State == U.UpdaterState.Done && U.InstalledSuite.Load(root).SuiteVersion == "1.4.0" && Read(root, "TealHub.exe") == "hub 1.4.0", "1.4.0 に戻る");
            }
            using (var f = U.UpdaterForm.ForRollback(new U.UpdateEngine(root)))
            {
                f.Show();
                Check(!f.PrimaryButton.Enabled || f.State == U.UpdaterState.Help || f.State == U.UpdaterState.Failed, "戻せる backup がなければ戻さない");
            }
            // 入れられないファイル
            var older = Sample(); older.Version = "1.3.0"; older.MinFrom = "";
            using (var f = U.UpdaterForm.ForPackage(new U.UpdateEngine(root), older.Write(Path.Combine(tmp, "older2.tealupdate"))))
            {
                f.Show();
                Check(!f.PrimaryButton.Enabled && f.NoticeText.Contains("古い"), "古いバージョンは理由を出してボタンを押せない");
            }
            using (var f = U.UpdaterForm.ForPackage(new U.UpdateEngine(root), Path.Combine(tmp, "not-zip.tealupdate")))
            {
                f.Show();
                Check(f.State == U.UpdaterState.Failed || !f.PrimaryButton.Enabled, "壊れたファイルはエラー表示");
            }
            var a = UP::TealSuite.Updater.UpdaterArgs.Parse(new[] { "--suite", root, "x.tealupdate" });
            var b = UP::TealSuite.Updater.UpdaterArgs.Parse(new[] { "--rollback", "--from-temp" });
            Check(a.Package == Path.GetFullPath("x.tealupdate") && a.SuiteDir == root && b.Rollback && b.FromTemp, "コマンドライン引数 (ファイル・--suite・--rollback)");
        }
        finally
        {
            U.UpdaterForm.FindRunning = U.SuiteProcesses.Find;
            U.UpdaterForm.CloseApps = U.SuiteProcesses.CloseAll;
            U.UpdaterForm.RestartApps = U.SuiteProcesses.Restart;
        }
    }
}
