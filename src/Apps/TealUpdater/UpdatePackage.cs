// TealUpdater - アップデート ファイル (.tealupdate) の中身
//   .tealupdate は zip。直下に update.json、置き換えるファイルは files/<スイート内のパス> に入っている。
//   update.json の例:
//   {
//     "format": 1, "suite": "TealSuite", "version": "1.5.0", "minFromVersion": "1.4.0",
//     "created": "2026-10-26T12:00:00Z", "kind": "full", "notes": "変更点…",
//     "files": [ {"path": "apps/TealPass/TealPass.exe", "sha256": "…", "size": 123456, "app": "TealPass"} ],
//     "delete": [ "apps/Old/Old.exe" ],
//     "apps": [ { apps.json と同じ形のアプリ情報 } ]
//   }
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TealSuite.Updater
{
    public class PackageFile
    {
        public string Path;     // スイートのフォルダからの相対パス ("/" 区切り)
        public string Sha256;   // 小文字 16 進
        public long Size;
        public string App;      // どのアプリのファイルか (なければ "")
        public string EntryName { get { return UpdatePackage.PayloadPrefix + Path; } }
    }

    public class UpdatePackage
    {
        public const int CurrentFormat = 1;
        public const string ManifestName = "update.json";
        public const string PayloadPrefix = "files/";
        public const string Extension = ".tealupdate";

        public int Format;
        public string Suite, Version, MinFromVersion, Created, Notes, Kind;
        public List<PackageFile> Files = new List<PackageFile>();
        public List<string> Delete = new List<string>();
        public List<JObj> Apps = new List<JObj>();
        public string SourcePath;

        public static ZipArchive OpenZip(string path)
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, false);
        }

        /// <summary>zip から update.json を読み、中身がそろっているかも確かめる (ハッシュは適用するときに確かめる)</summary>
        public static UpdatePackage Read(string zipPath)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("アップデート ファイルが見つかりません。\n" + zipPath);
            ZipArchive zip;
            try { zip = OpenZip(zipPath); }
            catch (InvalidDataException) { throw new InvalidDataException("アップデート ファイルとして開けません (zip 形式ではありません)。"); }
            using (zip)
            {
                var e = zip.GetEntry(ManifestName);
                if (e == null) throw new InvalidDataException("アップデート ファイルの中に " + ManifestName + " がありません。");
                string text;
                using (var r = new StreamReader(e.Open(), Encoding.UTF8)) text = r.ReadToEnd();
                UpdatePackage p;
                try { p = Parse(text); }
                catch (FormatException ex) { throw new InvalidDataException("アップデート ファイルの " + ManifestName + " が読めません。\n" + ex.Message); }
                p.SourcePath = Path.GetFullPath(zipPath);
                var missing = p.Files.Where(f => zip.GetEntry(f.EntryName) == null).Select(f => f.Path).ToList();
                if (missing.Count > 0)
                    throw new InvalidDataException("アップデート ファイルが壊れています (中に無いファイル: " + string.Join(", ", missing.Take(3)) + (missing.Count > 3 ? " ほか" : "") + ")");
                return p;
            }
        }

        /// <summary>update.json の文字列を読む。形がおかしければ FormatException</summary>
        public static UpdatePackage Parse(string json)
        {
            var o = Json.ParseObj(json);
            var p = new UpdatePackage
            {
                Format = o.Int("format", 0),
                Suite = o.Str("suite") ?? "",
                Version = o.Str("version") ?? "",
                MinFromVersion = o.Str("minFromVersion") ?? "",
                Created = o.Str("created") ?? "",
                Notes = o.Str("notes") ?? "",
                Kind = o.Str("kind") ?? "",
            };
            if (p.Format <= 0) throw new FormatException("format がありません");
            if (p.Format > CurrentFormat) throw new FormatException("このアップデート ファイルは新しい形式 (format " + p.Format + ") です。TealUpdater を新しくしてから開いてください");
            if (!VersionUtil.IsValid(p.Version)) throw new FormatException("version が正しくありません (" + p.Version + ")");
            if (p.MinFromVersion.Length > 0 && !VersionUtil.IsValid(p.MinFromVersion)) throw new FormatException("minFromVersion が正しくありません (" + p.MinFromVersion + ")");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = o.Arr("files") ?? new List<object>();
            foreach (var item in files)
            {
                var f = item as JObj;
                if (f == null) throw new FormatException("files の中身が正しくありません");
                var pf = new PackageFile { Path = NormalizePath(f.Str("path")), Sha256 = (f.Str("sha256") ?? "").ToLowerInvariant(), Size = f.Long("size", -1), App = f.Str("app") ?? "" };
                var bad = CheckPath(pf.Path);
                if (bad != null) throw new FormatException(bad);
                if (pf.Sha256.Length != 64 || !pf.Sha256.All(Uri.IsHexDigit)) throw new FormatException(pf.Path + " の sha256 が正しくありません");
                if (!seen.Add(pf.Path)) throw new FormatException(pf.Path + " が 2 回書かれています");
                p.Files.Add(pf);
            }
            foreach (var d in o.Strs("delete"))
            {
                var path = NormalizePath(d);
                var bad = CheckPath(path);
                if (bad != null) throw new FormatException(bad);
                if (seen.Contains(path)) throw new FormatException(path + " が置き換えと削除の両方に書かれています");
                p.Delete.Add(path);
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in o.Arr("apps") ?? new List<object>())
            {
                var a = item as JObj;
                if (a == null || string.IsNullOrEmpty(a.Str("id"))) throw new FormatException("apps の中に id の無いアプリがあります");
                if (!ids.Add(a.Str("id"))) throw new FormatException("apps に " + a.Str("id") + " が 2 回書かれています");
                p.Apps.Add(a);
            }
            if (p.Files.Count == 0 && p.Delete.Count == 0 && p.Apps.Count == 0) throw new FormatException("置き換えるものが何もありません");
            return p;
        }

        public static string NormalizePath(string p)
        {
            if (p == null) return "";
            p = p.Replace('\\', '/').Trim();
            while (p.StartsWith("./")) p = p.Substring(2);
            return p;
        }

        /// <summary>スイートのフォルダの外や、置き換えてはいけないファイルを指していないか。問題なければ null</summary>
        public static string CheckPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "path が空のファイルがあります";
            if (p.StartsWith("/") || p.Contains(":") || p.StartsWith("~")) return "スイートの外を指すパスは使えません (" + p + ")";
            var parts = p.Split('/');
            if (parts.Any(s => s.Length == 0 || s == "." || s == "..")) return "使えないパスです (" + p + ")";
            if (parts.Any(s => s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return "使えない文字を含むパスです (" + p + ")";
            if (string.Equals(p, "apps.json", StringComparison.OrdinalIgnoreCase)) return "apps.json はファイルごと置き換えず、apps の項目でまとめ直します";
            var top = parts[0];
            if (string.Equals(top, UpdateEngine.BackupDirName, StringComparison.OrdinalIgnoreCase) || string.Equals(top, UpdateEngine.StagingDirName, StringComparison.OrdinalIgnoreCase))
                return "作業用のフォルダは置き換えられません (" + p + ")";
            return null;
        }

        public static string Sha256Of(Stream s)
        {
            using (var h = SHA256.Create())
                return string.Concat(h.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        public static string Sha256OfFile(string path)
        {
            using (var s = File.OpenRead(path)) return Sha256Of(s);
        }

        public string AppName(string id)
        {
            var a = Apps.FirstOrDefault(x => string.Equals(x.Str("id"), id, StringComparison.OrdinalIgnoreCase));
            return a != null && !string.IsNullOrEmpty(a.Str("name")) ? a.Str("name") : id;
        }
    }

    public static class VersionUtil
    {
        public static bool IsValid(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            var parts = v.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;
            int n;
            return parts.All(p => p.Length > 0 && int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out n));
        }

        /// <summary>数字ごとに比べる (1.10.0 &gt; 1.9.0)。読めない部分は 0</summary>
        public static int Compare(string a, string b)
        {
            var x = Parts(a); var y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        static int[] Parts(string v)
        {
            return (v ?? "").Split('.').Select(s => { int n; return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0; }).ToArray();
        }
    }
}
