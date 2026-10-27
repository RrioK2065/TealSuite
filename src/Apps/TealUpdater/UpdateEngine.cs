// TealUpdater - アップデートの確認・適用・元に戻す
//   ・%APPDATA%\TealSuite (利用者のデータ) には一切さわらない。さわるのはスイートのフォルダ (TealHub.exe のある所) だけ
//   ・置き換える前のファイルは backup\<元のバージョン>\ に残す。途中で失敗したらそこから自動で戻す
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace TealSuite.Updater
{
    public class InstalledApp
    {
        public string Id, Name, Version, Exe;
        public bool Resident, Self;
        public List<string> LegacyIds = new List<string>();
        public bool Matches(string id)
        {
            return string.Equals(Id, id, StringComparison.OrdinalIgnoreCase) || LegacyIds.Any(l => string.Equals(l, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>今入っているスイート (apps.json から読む)</summary>
    public class InstalledSuite
    {
        public string Dir;
        public JObj AppsJson;   // 読めなければ null
        public string SuiteName = "TealSuite";
        public string SuiteVersion = "";
        public List<InstalledApp> Apps = new List<InstalledApp>();

        public static InstalledSuite Load(string dir)
        {
            var s = new InstalledSuite { Dir = Path.GetFullPath(dir) };
            var path = Path.Combine(s.Dir, UpdateEngine.AppsJsonName);
            if (!File.Exists(path)) return s;
            s.AppsJson = Json.ParseObj(File.ReadAllText(path, Encoding.UTF8));
            s.SuiteName = s.AppsJson.Str("suite") ?? s.SuiteName;
            foreach (var item in s.AppsJson.Arr("apps") ?? new List<object>())
            {
                var a = item as JObj;
                if (a == null || string.IsNullOrEmpty(a.Str("id"))) continue;
                s.Apps.Add(new InstalledApp
                {
                    Id = a.Str("id"), Name = a.Str("name") ?? a.Str("id"), Version = a.Str("version") ?? "", Exe = a.Str("exe") ?? "",
                    Resident = a.Bool("resident"), Self = a.Bool("self") || a.Str("id") == "TealHub", LegacyIds = a.Strs("legacyIds"),
                });
            }
            s.SuiteVersion = s.AppsJson.Str("suiteVersion") ?? "";
            if (s.SuiteVersion.Length == 0)
            {
                // 1.3 までの apps.json には suiteVersion がない。Teal Hub のバージョンで代用する
                var hub = s.Apps.FirstOrDefault(a => a.Self);
                if (hub != null) s.SuiteVersion = hub.Version;
            }
            return s;
        }

        public InstalledApp Find(string id) { return Apps.FirstOrDefault(a => a.Matches(id)); }
        public string FullPath(string rel) { return Path.GetFullPath(Path.Combine(Dir, (rel ?? "").Replace('/', Path.DirectorySeparatorChar))); }
    }

    public class AppChange
    {
        public string Id, Name, From, To;
        public bool IsNew;
        public int Files;
        public string Describe
        {
            get
            {
                if (IsNew) return "新しいアプリ ・ " + To;
                if (VersionUtil.Compare(From, To) == 0) return Files > 0 ? To + " (入れ直し)" : To + " (変更なし)";
                return From + " → " + To;
            }
        }
    }

    public class UpdatePlan
    {
        public string From = "", To = "";
        public List<string> Problems = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<AppChange> Changes = new List<AppChange>();
        public int OtherFiles;      // どのアプリにも属さないファイル (README、TealUpdater など)
        public int DeleteCount;
        public bool CanApply { get { return Problems.Count == 0; } }
        public IEnumerable<AppChange> NewApps { get { return Changes.Where(c => c.IsNew); } }
    }

    public class BackupInfo
    {
        public string Dir, Name, From, To, Date, RestoredAt;
        public List<string> Replaced = new List<string>(), Created = new List<string>(), Deleted = new List<string>();
        public bool HadAppsJson;
        public bool Restored { get { return !string.IsNullOrEmpty(RestoredAt); } }
    }

    public class UpdateResult
    {
        public bool Ok;
        public string Error;
        public bool RolledBack;
        public string RollbackError;
        public string BackupDir;
        public string From, To;
        public List<AppChange> Changes = new List<AppChange>();
    }

    public class UpdateEngine
    {
        public const string BackupDirName = "backup";
        public const string StagingDirName = ".update-staging";
        public const string BackupInfoName = "backup.json";
        public const string AppsJsonName = "apps.json";
        public const string BackupFilesDir = "files";

        public readonly string Dir;
        /// <summary>ファイルを 1 つ置き換える直前に呼ぶ (テストでわざと失敗させるため)</summary>
        public Action<string> BeforeReplaceFile;
        /// <summary>進み具合 (0〜100, 説明)</summary>
        public Action<int, string> Progress;
        public int CopyRetries = 20;
        public int RetryDelayMs = 250;

        public UpdateEngine(string suiteDir) { Dir = Path.GetFullPath(suiteDir); }

        public string BackupRoot { get { return Path.Combine(Dir, BackupDirName); } }
        public string StagingDir { get { return Path.Combine(Dir, StagingDirName); } }

        void Report(int pct, string what) { var p = Progress; if (p != null) p(pct, what); }

        string Target(string rel) { return Path.Combine(Dir, rel.Replace('/', Path.DirectorySeparatorChar)); }

        // ---------------------------------------------------------------- 確認

        /// <summary>Teal Hub がダウンロードしたときの「このバージョンのはず」(--expect-version)。null なら確かめない</summary>
        public string ExpectVersion;

        public UpdatePlan Plan(UpdatePackage pkg) { return PlanChecked(pkg, InstalledSuite.Load(Dir)); }

        public UpdatePlan PlanChecked(UpdatePackage pkg, InstalledSuite inst)
        {
            var plan = Plan(pkg, inst);
            if (!string.IsNullOrEmpty(ExpectVersion) && VersionUtil.Compare(ExpectVersion, pkg.Version) != 0)
                plan.Problems.Add("ダウンロードしたファイルのバージョン (" + pkg.Version + ") が、公開されているバージョン (" + ExpectVersion + ") と違います。");
            return plan;
        }

        public static UpdatePlan Plan(UpdatePackage pkg, InstalledSuite inst)
        {
            var plan = new UpdatePlan { From = inst.SuiteVersion, To = pkg.Version, DeleteCount = pkg.Delete.Count };
            if (inst.AppsJson == null)
                plan.Problems.Add("このフォルダに TealSuite (apps.json) が見つかりません。\n" + inst.Dir);
            if (!string.Equals(pkg.Suite, inst.SuiteName, StringComparison.OrdinalIgnoreCase))
                plan.Problems.Add("このアップデート ファイルは " + (pkg.Suite.Length > 0 ? pkg.Suite : "(不明)") + " 用です (" + inst.SuiteName + " 用ではありません)。");
            if (inst.AppsJson != null)
            {
                if (!VersionUtil.IsValid(inst.SuiteVersion))
                    plan.Problems.Add("今のバージョンがわかりません (apps.json に suiteVersion がありません)。");
                else
                {
                    int cmp = VersionUtil.Compare(pkg.Version, inst.SuiteVersion);
                    if (cmp < 0) plan.Problems.Add("今の " + inst.SuiteVersion + " より古いバージョン (" + pkg.Version + ") です。古いバージョンに戻すときは「前のバージョンに戻す」を使ってください。");
                    else if (cmp == 0) plan.Warnings.Add("今と同じバージョン (" + pkg.Version + ") です。ファイルを入れ直します。");
                    if (pkg.MinFromVersion.Length > 0 && VersionUtil.Compare(inst.SuiteVersion, pkg.MinFromVersion) < 0)
                        plan.Problems.Add("このアップデートは " + pkg.MinFromVersion + " 以降に入れるものです (今は " + inst.SuiteVersion + ")。先に " + pkg.MinFromVersion + " にしてください。");
                }
            }

            var filesByApp = pkg.Files.GroupBy(f => f.App ?? "", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in pkg.Apps)
            {
                var id = a.Str("id");
                var cur = inst.Find(id);
                int n = 0;
                if (filesByApp.ContainsKey(id)) { n += filesByApp[id]; covered.Add(id); }
                if (cur != null && !string.Equals(cur.Id, id, StringComparison.OrdinalIgnoreCase) && filesByApp.ContainsKey(cur.Id)) { n += filesByApp[cur.Id]; covered.Add(cur.Id); }
                var change = new AppChange { Id = id, Name = a.Str("name") ?? id, To = a.Str("version") ?? "", From = cur != null ? cur.Version : "", IsNew = cur == null, Files = n };
                if (change.IsNew && string.IsNullOrEmpty(a.Str("exe"))) plan.Problems.Add(change.Name + " の exe が書かれていません。");
                plan.Changes.Add(change);
            }
            foreach (var kv in filesByApp)
            {
                if (covered.Contains(kv.Key)) continue;
                var cur = kv.Key.Length > 0 ? inst.Find(kv.Key) : null;
                if (cur != null) plan.Changes.Add(new AppChange { Id = cur.Id, Name = cur.Name, From = cur.Version, To = cur.Version, Files = kv.Value });
                else plan.OtherFiles += kv.Value;
            }
            return plan;
        }

        // ---------------------------------------------------------------- apps.json のまとめ直し

        /// <summary>
        /// 今の apps.json にアップデートのアプリ情報を重ねる。
        ///  ・同じアプリ (id か legacyIds が一致) は、アップデートに書かれた項目だけ上書き。利用者が足した項目は残す
        ///  ・新しいアプリは末尾に足す。アップデートに無いアプリ (利用者が登録したものなど) はそのまま残す
        ///  ・suiteVersion をアップデートのバージョンにする
        /// </summary>
        public static JObj MergeAppsJson(JObj installed, UpdatePackage pkg)
        {
            JObj result;
            if (installed == null)
            {
                result = new JObj();
                result["manifestVersion"] = new JNum("1");
                result["suite"] = pkg.Suite;
            }
            else result = installed.Clone();
            result.InsertAfter("suite", "suiteVersion", pkg.Version);
            var apps = result.Arr("apps");
            if (apps == null) { apps = new List<object>(); result["apps"] = apps; }
            foreach (var src in pkg.Apps)
            {
                var id = src.Str("id");
                var srcLegacy = src.Strs("legacyIds");
                JObj dst = null;
                foreach (var item in apps)
                {
                    var a = item as JObj;
                    if (a == null) continue;
                    var aid = a.Str("id") ?? "";
                    if (string.Equals(aid, id, StringComparison.OrdinalIgnoreCase)
                        || srcLegacy.Any(l => string.Equals(l, aid, StringComparison.OrdinalIgnoreCase))
                        || a.Strs("legacyIds").Any(l => string.Equals(l, id, StringComparison.OrdinalIgnoreCase)))
                    { dst = a; break; }
                }
                if (dst == null) { apps.Add(src.Clone()); continue; }
                foreach (var k in src.Keys) dst[k] = Json.Clone(src[k]);
            }
            return result;
        }

        // ---------------------------------------------------------------- 適用

        public UpdateResult Apply(UpdatePackage pkg)
        {
            var inst = InstalledSuite.Load(Dir);
            var plan = PlanChecked(pkg, inst);
            var res = new UpdateResult { From = plan.From, To = plan.To, Changes = plan.Changes };
            if (!plan.CanApply) { res.Error = string.Join("\n", plan.Problems); return res; }

            // 1) 取り出してハッシュを確かめる (ここまではスイートのファイルに何もしない)
            try { Stage(pkg); }
            catch (Exception ex) { TryDeleteDir(StagingDir); res.Error = ex.Message; return res; }

            // 2) 置き換える前のファイルを残す
            var info = new BackupInfo { From = plan.From, To = plan.To, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
            var createdDirs = new List<string>();
            try
            {
                info.Dir = NewBackupDir(plan.From);
                info.Name = Path.GetFileName(info.Dir);
                res.BackupDir = info.Dir;
                Report(40, "今のファイルを残しています…");
                foreach (var f in pkg.Files)
                {
                    if (File.Exists(Target(f.Path))) { CopyWithRetry(Target(f.Path), BackupFile(info, f.Path)); info.Replaced.Add(f.Path); }
                    else info.Created.Add(f.Path);
                }
                foreach (var d in pkg.Delete)
                    if (File.Exists(Target(d))) { CopyWithRetry(Target(d), BackupFile(info, d)); info.Deleted.Add(d); }
                var appsJson = Path.Combine(Dir, AppsJsonName);
                info.HadAppsJson = File.Exists(appsJson);
                if (info.HadAppsJson) File.Copy(appsJson, Path.Combine(info.Dir, AppsJsonName), true);
                WriteBackupInfo(info);
            }
            catch (Exception ex)
            {
                TryDeleteDir(StagingDir);
                if (info.Dir != null) TryDeleteDir(info.Dir);
                res.BackupDir = null;
                res.Error = "今のファイルを残せませんでした。何も変更していません。\n" + ex.Message;
                return res;
            }

            // 3) 置き換え → 削除 → apps.json。失敗したら backup から戻す
            try
            {
                int i = 0;
                foreach (var f in pkg.Files)
                {
                    Report(50 + 40 * i++ / Math.Max(1, pkg.Files.Count), "置き換えています: " + f.Path);
                    var cb = BeforeReplaceFile; if (cb != null) cb(f.Path);
                    var dst = Target(f.Path);
                    var dir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dir)) { createdDirs.Add(dir); Directory.CreateDirectory(dir); }
                    CopyWithRetry(Path.Combine(StagingDir, f.Path.Replace('/', Path.DirectorySeparatorChar)), dst);
                }
                foreach (var d in info.Deleted) DeleteWithRetry(Target(d));
                Report(92, "アプリの一覧 (apps.json) を更新しています…");
                var merged = MergeAppsJson(inst.AppsJson, pkg);
                WriteAtomic(Path.Combine(Dir, AppsJsonName), Json.Write(merged));
                TryDeleteDir(StagingDir);
                Report(100, "完了しました");
                res.Ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                Report(95, "失敗したので元に戻しています…");
                try
                {
                    RestoreFiles(info);
                    foreach (var dir in Enumerable.Reverse(createdDirs)) TryDeleteEmptyDirs(dir);
                    res.RolledBack = true;
                    TryDeleteDir(info.Dir);
                    res.BackupDir = null;
                }
                catch (Exception rex) { res.RollbackError = rex.Message; }
                TryDeleteDir(StagingDir);
                return res;
            }
        }

        void Stage(UpdatePackage pkg)
        {
            if (string.IsNullOrEmpty(pkg.SourcePath)) throw new InvalidOperationException("アップデート ファイルの場所がわかりません");
            TryDeleteDir(StagingDir);
            Directory.CreateDirectory(StagingDir);
            using (var zip = UpdatePackage.OpenZip(pkg.SourcePath))
            {
                int i = 0;
                foreach (var f in pkg.Files)
                {
                    Report(5 + 30 * i++ / Math.Max(1, pkg.Files.Count), "確かめています: " + f.Path);
                    var e = zip.GetEntry(f.EntryName);
                    if (e == null) throw new InvalidDataException("アップデート ファイルが壊れています (" + f.Path + " がありません)");
                    var dst = Path.Combine(StagingDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    using (var src = e.Open())
                    using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write)) src.CopyTo(o);
                    var len = new FileInfo(dst).Length;
                    var hash = UpdatePackage.Sha256OfFile(dst);
                    if (hash != f.Sha256 || (f.Size >= 0 && len != f.Size))
                        throw new InvalidDataException("アップデート ファイルが壊れています (" + f.Path + " の中身が一致しません)。もう一度ダウンロードしてください。何も変更していません。");
                }
            }
        }

        string NewBackupDir(string from)
        {
            var baseName = VersionUtil.IsValid(from) ? from : "unknown";
            Directory.CreateDirectory(BackupRoot);
            var dir = Path.Combine(BackupRoot, baseName);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(BackupRoot, baseName + "-" + n);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string BackupFile(BackupInfo info, string rel)
        {
            var p = Path.Combine(info.Dir, BackupFilesDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return p;
        }

        /// <summary>backup の内容で元に戻す (置き換えたもの・消したものを戻し、新しく入れたものを消す)</summary>
        void RestoreFiles(BackupInfo info)
        {
            var errors = new List<string>();
            foreach (var rel in info.Replaced.Concat(info.Deleted))
            {
                try
                {
                    var dst = Target(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    CopyWithRetry(Path.Combine(info.Dir, BackupFilesDir, rel.Replace('/', Path.DirectorySeparatorChar)), dst);
                }
                catch (Exception ex) { errors.Add(rel + ": " + ex.Message); }
            }
            foreach (var rel in info.Created)
            {
                try { var t = Target(rel); if (File.Exists(t)) DeleteWithRetry(t); TryDeleteEmptyDirs(Path.GetDirectoryName(t)); }
                catch (Exception ex) { errors.Add(rel + ": " + ex.Message); }
            }
            var saved = Path.Combine(info.Dir, AppsJsonName);
            try { if (info.HadAppsJson && File.Exists(saved)) WriteAtomic(Path.Combine(Dir, AppsJsonName), File.ReadAllText(saved, Encoding.UTF8)); }
            catch (Exception ex) { errors.Add(AppsJsonName + ": " + ex.Message); }
            if (errors.Count > 0) throw new IOException("一部のファイルを戻せませんでした:\n" + string.Join("\n", errors.Take(5)));
        }

        // ---------------------------------------------------------------- 元に戻す (前のバージョン)

        /// <summary>残してある backup (新しい順。戻し済みのものは除く)</summary>
        public List<BackupInfo> ListBackups()
        {
            var list = new List<BackupInfo>();
            if (!Directory.Exists(BackupRoot)) return list;
            foreach (var d in Directory.GetDirectories(BackupRoot))
            {
                var b = ReadBackupInfo(d);
                if (b != null && !b.Restored) list.Add(b);
            }
            return list.OrderByDescending(b => b.Date, StringComparer.Ordinal).ThenByDescending(b => b.Name, StringComparer.Ordinal).ToList();
        }

        public UpdateResult Restore(BackupInfo info)
        {
            var res = new UpdateResult { From = InstalledSuite.Load(Dir).SuiteVersion, To = info.From, BackupDir = info.Dir };
            try
            {
                Report(30, "前のファイルに戻しています…");
                RestoreFiles(info);
                info.RestoredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                WriteBackupInfo(info);
                Report(100, "完了しました");
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        public static BackupInfo ReadBackupInfo(string dir)
        {
            try
            {
                var o = Json.ParseObj(File.ReadAllText(Path.Combine(dir, BackupInfoName), Encoding.UTF8));
                return new BackupInfo
                {
                    Dir = dir, Name = Path.GetFileName(dir), From = o.Str("from") ?? "", To = o.Str("to") ?? "", Date = o.Str("date") ?? "",
                    RestoredAt = o.Str("restoredAt"), HadAppsJson = o.Bool("hadAppsJson"),
                    Replaced = o.Strs("replaced"), Created = o.Strs("created"), Deleted = o.Strs("deleted"),
                };
            }
            catch { return null; }
        }

        static void WriteBackupInfo(BackupInfo b)
        {
            var o = new JObj();
            o["from"] = b.From; o["to"] = b.To; o["date"] = b.Date;
            if (b.Restored) o["restoredAt"] = b.RestoredAt;
            o["hadAppsJson"] = b.HadAppsJson;
            o["replaced"] = b.Replaced.Cast<object>().ToList();
            o["created"] = b.Created.Cast<object>().ToList();
            o["deleted"] = b.Deleted.Cast<object>().ToList();
            WriteAtomic(Path.Combine(b.Dir, BackupInfoName), Json.Write(o));
        }

        // ---------------------------------------------------------------- ファイル操作

        void CopyWithRetry(string src, string dst)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(src, dst, true); return; }
                catch (IOException) { if (i >= CopyRetries) throw; }
                catch (UnauthorizedAccessException) { if (i >= CopyRetries) throw; }
                Thread.Sleep(RetryDelayMs);
            }
        }

        void DeleteWithRetry(string path)
        {
            for (int i = 0; ; i++)
            {
                try { if (File.Exists(path)) File.Delete(path); return; }
                catch (IOException) { if (i >= CopyRetries) throw; }
                catch (UnauthorizedAccessException) { if (i >= CopyRetries) throw; }
                Thread.Sleep(RetryDelayMs);
            }
        }

        public static void WriteAtomic(string path, string text)
        {
            // 共通部品の書き込み (一時ファイル → 置き換え)。データフォルダには関係しない
            TealSuite.Common.DataStore.WriteFileAtomic(path, new UTF8Encoding(false).GetBytes(text));
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        void TryDeleteEmptyDirs(string dir)
        {
            try
            {
                while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && dir.Length > Dir.Length
                       && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
        }
    }
}
