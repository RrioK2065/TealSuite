// Teal Hub (アプリ管理) - オンラインのアップデート確認 (GitHub Releases)
//  - 公開場所はここ (UpdateSource) の 1 か所だけ。リポジトリを変えるときはここを直す
//  - 起動時 (1 日 1 回まで) と「更新を確認」で releases/latest を読み、新しいバージョンがあればお知らせを出す
//  - ダウンロードは %TEMP%\TealSuite-download\ へ。sha256 (.sha256 ファイルかリリースの説明文) が一致したものだけ
//    TealUpdater に渡す (--expect-version で中身のバージョンも確かめる)
//  - 通信できない・回数制限・壊れた応答などは、自動確認ではログに残すだけで何も出さない
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>アップデートの公開場所 (ここだけ変えればよい)</summary>
    public static class UpdateSource
    {
        public const string Owner = "RrioK2065";
        public const string Repo = "TealSuite";
        public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases";
        const string LatestApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        /// <summary>開発・テスト用: TEALSUITE_UPDATE_API に別の URL か、見本の JSON ファイルのパスを入れられる</summary>
        public static string ApiUrl
        {
            get
            {
                var e = Environment.GetEnvironmentVariable("TEALSUITE_UPDATE_API");
                return string.IsNullOrEmpty(e) ? LatestApi : e;
            }
        }

        /// <summary>ダウンロードしてよい場所 (https で、GitHub のもの)</summary>
        public static readonly string[] AllowedHosts = { "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };

        public static bool IsAllowed(Uri u)
        {
            if (u == null || !u.IsAbsoluteUri) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;
            var h = u.Host.ToLowerInvariant();
            return AllowedHosts.Contains(h) || h.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        }

        public static string DownloadDir
        {
            get
            {
                var e = Environment.GetEnvironmentVariable("TEALSUITE_DOWNLOAD_DIR");
                return string.IsNullOrEmpty(e) ? Path.Combine(Path.GetTempPath(), "TealSuite-download") : e;
            }
        }
    }

    // ---------------------------------------------------------------- GitHub の JSON

    [DataContract]
    public class GhAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "browser_download_url")] public string Url;
        [DataMember(Name = "size")] public long Size;
    }

    [DataContract]
    public class GhRelease
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "body")] public string Body;
        [DataMember(Name = "html_url")] public string HtmlUrl;
        [DataMember(Name = "draft")] public bool Draft;
        [DataMember(Name = "prerelease")] public bool Prerelease;
        [DataMember(Name = "published_at")] public string PublishedAt;
        [DataMember(Name = "assets")] public List<GhAsset> Assets;
    }

    /// <summary>読み取った公開中のバージョン</summary>
    public class ReleaseInfo
    {
        public string Version, Tag, Title, Notes, PageUrl, Published;
        public GhAsset Package;      // .tealupdate
        public GhAsset Checksum;     // .tealupdate.sha256 (なければ null)
        public List<GhAsset> Assets = new List<GhAsset>();

        /// <summary>お知らせに出す 1 行 (説明文の最初の中身のある行)</summary>
        public string Summary
        {
            get
            {
                foreach (var raw in (Notes ?? "").Replace("\r", "").Split('\n'))
                {
                    var l = raw.Trim().TrimStart('#', '-', '*', ' ').Trim();
                    if (l.Length == 0 || l.StartsWith("sha256", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(l, "^[0-9a-fA-F]{64}\\b")) continue;
                    return l.Length > 80 ? l.Substring(0, 79) + "…" : l;
                }
                return "";
            }
        }
    }

    public enum CheckOutcome { UpToDate, NewVersion, NoRelease, Offline, RateLimited, Error }

    public class CheckResult
    {
        public CheckOutcome Outcome;
        public ReleaseInfo Release;
        public string Message;   // 手動で確認したときに出す説明
        public string Installed;
        public bool IsNew { get { return Outcome == CheckOutcome.NewVersion; } }
    }

    // ---------------------------------------------------------------- 通信 (テストでは差し替える)

    public class FetchResult
    {
        public int Status;          // 200 など。通信できなければ 0
        public byte[] Body;
        public Uri FinalUri;        // リダイレクト後
        public string RateRemaining;
        public string Error;
    }

    public class WebFetcher
    {
        static WebFetcher()
        {
            // .NET Framework 4.x の既定は TLS 1.0 のことがあるので、GitHub が求める TLS 1.2 を足す
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static string UserAgent { get { return "TealSuite-Hub/" + AppInfo.Version + " (+" + UpdateSource.ReleasesPage + ")"; } }

        /// <summary>url を読む。maxBytes を超えたら打ち切る。progress(読んだ量, 全体 or -1)</summary>
        public virtual FetchResult Fetch(string url, bool binary, long maxBytes, Action<long, long> progress)
        {
            var r = new FetchResult();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Accept = binary ? "application/octet-stream" : "application/vnd.github+json";
                if (!binary) req.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                req.Timeout = 20000; req.ReadWriteTimeout = 60000;
                req.AllowAutoRedirect = true; req.MaximumAutomaticRedirections = 5;
                using (var resp = (HttpWebResponse)req.GetResponse()) Read(resp, r, maxBytes, progress);
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null) using (resp) { r.Status = (int)resp.StatusCode; r.RateRemaining = resp.Headers["X-RateLimit-Remaining"]; r.FinalUri = resp.ResponseUri; }
                r.Error = ex.Message;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
        }

        static void Read(HttpWebResponse resp, FetchResult r, long maxBytes, Action<long, long> progress)
        {
            r.Status = (int)resp.StatusCode; r.FinalUri = resp.ResponseUri; r.RateRemaining = resp.Headers["X-RateLimit-Remaining"];
            long total = resp.ContentLength;
            if (total > maxBytes) throw new IOException("ファイルが大きすぎます (" + total + " バイト)");
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920]; int n; long got = 0;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n); got += n;
                    if (got > maxBytes) throw new IOException("ファイルが大きすぎます");
                    if (progress != null) progress(got, total);
                }
                r.Body = ms.ToArray();
            }
        }
    }

    // ---------------------------------------------------------------- 確認とダウンロード

    public class OnlineUpdate
    {
        public const long MaxApiBytes = 2 * 1024 * 1024;
        public const long MaxPackageBytes = 200L * 1024 * 1024;
        public readonly WebFetcher Http;

        public OnlineUpdate(WebFetcher http) { Http = http ?? new WebFetcher(); }

        // ---- 部品 (テストしやすいように static) ----

        /// <summary>"v1.5.0" / "1.5.0" → "1.5.0"。バージョンの形でなければ null</summary>
        public static string VersionFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
            return Regex.IsMatch(t, "^\\d+(\\.\\d+){1,3}$") ? t : null;
        }

        /// <summary>数字ごとに比べる (1.10.0 &gt; 1.9.0、1.5 = 1.5.0)</summary>
        public static int CompareVersions(string a, string b)
        {
            var x = (a ?? "").Split('.'); var y = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = 0, q = 0;
                if (i < x.Length) int.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out p);
                if (i < y.Length) int.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out q);
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        /// <summary>リリースの JSON を読む。下書き・プレリリース・バージョンでないタグは null</summary>
        public static ReleaseInfo ParseRelease(byte[] json)
        {
            var g = DataStore.FromJson<GhRelease>(json);
            if (g == null || g.Draft || g.Prerelease) return null;
            var v = VersionFromTag(g.Tag);
            if (v == null) return null;
            var info = new ReleaseInfo
            {
                Version = v, Tag = g.Tag, Title = string.IsNullOrEmpty(g.Name) ? g.Tag : g.Name,
                Notes = g.Body ?? "", PageUrl = g.HtmlUrl, Published = g.PublishedAt,
                Assets = (g.Assets ?? new List<GhAsset>()).Where(x => x != null && !string.IsNullOrEmpty(x.Name)).ToList(),
            };
            info.Package = SelectPackage(info.Assets, v);
            if (info.Package != null)
                info.Checksum = info.Assets.FirstOrDefault(x => string.Equals(x.Name, info.Package.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            return info;
        }

        /// <summary>アップデート ファイルを選ぶ: TealSuite-update-&lt;ver&gt;.tealupdate → 名前にバージョンを含む .tealupdate → ほかの .tealupdate</summary>
        public static GhAsset SelectPackage(IEnumerable<GhAsset> assets, string version)
        {
            var list = (assets ?? new GhAsset[0]).Where(a => a != null && a.Name != null && a.Name.EndsWith(UpdaterLauncher.Extension, StringComparison.OrdinalIgnoreCase)).ToList();
            return list.FirstOrDefault(a => string.Equals(a.Name, "TealSuite-update-" + version + UpdaterLauncher.Extension, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => a.Name.Contains("-" + version + ".") || a.Name.Contains("-" + version + "-"))
                ?? list.FirstOrDefault();
        }

        /// <summary>".sha256" ファイルの中身 ("&lt;64 桁&gt;  ファイル名" か 64 桁だけ) から値を取り出す</summary>
        public static string ParseShaFile(string text, string fileName)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), "^([0-9a-fA-F]{64})(?:\\s+\\*?(.+))?$");
                if (!m.Success) continue;
                var name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
                if (name == null || fileName == null || string.Equals(Path.GetFileName(name), fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>リリースの説明文に書かれた sha256 ("&lt;64 桁&gt;  TealSuite-update-x.y.z.tealupdate" の行)</summary>
        public static string ShaFromNotes(string notes, string fileName)
        {
            if (string.IsNullOrEmpty(notes) || string.IsNullOrEmpty(fileName)) return null;
            foreach (var raw in notes.Replace("\r", "").Split('\n'))
            {
                var l = raw.Trim().Trim('`', '|', ' ').Trim();
                var m = Regex.Match(l, "([0-9a-fA-F]{64})[`|*\\s]+([^\\s`|*]+)");
                if (m.Success && string.Equals(m.Groups[2].Value, fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        /// <summary>前回から 24 時間たっていれば true (自動確認がオフなら false)</summary>
        public static bool IsDue(HubSettings s, DateTime nowUtc)
        {
            if (s == null || s.UpdateCheckOff) return false;
            DateTime last;
            if (string.IsNullOrEmpty(s.LastUpdateCheck) || !DateTime.TryParse(s.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out last)) return true;
            return last > nowUtc.AddMinutes(5) || nowUtc - last >= TimeSpan.FromHours(24);
        }

        // ---- 確認 ----

        public CheckResult Check(string installed)
        {
            var res = new CheckResult { Installed = installed };
            var url = UpdateSource.ApiUrl;
            FetchResult f;
            if (File.Exists(url)) f = new FetchResult { Status = 200, Body = File.ReadAllBytes(url) };   // 開発用の見本
            else
            {
                Uri u;
                if (!Uri.TryCreate(url, UriKind.Absolute, out u) || !UpdateSource.IsAllowed(u)) return Fail(res, CheckOutcome.Error, "確認先の URL が正しくありません: " + url);
                f = Http.Fetch(url, false, MaxApiBytes, null);
            }
            if (f.Status == 0) return Fail(res, CheckOutcome.Offline, "インターネットにつながらないため、確認できませんでした。" + (f.Error != null ? "\n(" + f.Error + ")" : ""));
            if (f.Status == 404) return Fail(res, CheckOutcome.NoRelease, "まだ公開されているバージョンがありません。");
            if (f.Status == 403 || f.Status == 429) return Fail(res, CheckOutcome.RateLimited, "GitHub の確認回数の上限に達しました。しばらくしてからもう一度お試しください。");
            if (f.Status != 200 || f.Body == null) return Fail(res, CheckOutcome.Error, "確認できませんでした (HTTP " + f.Status + ")。");
            if (f.FinalUri != null && !UpdateSource.IsAllowed(f.FinalUri)) return Fail(res, CheckOutcome.Error, "想定外の場所に転送されました: " + f.FinalUri.Host);
            ReleaseInfo r;
            try { r = ParseRelease(f.Body); }
            catch (Exception ex) { return Fail(res, CheckOutcome.Error, "応答を読めませんでした。(" + ex.Message + ")"); }
            if (r == null) return Fail(res, CheckOutcome.NoRelease, "公開されているバージョンが見つかりませんでした。");
            res.Release = r;
            if (CompareVersions(r.Version, installed) <= 0) { res.Outcome = CheckOutcome.UpToDate; res.Message = "最新のバージョンです (" + installed + ")。"; return res; }
            if (r.Package == null) return Fail(res, CheckOutcome.Error, r.Version + " は公開されていますが、アップデート ファイル (" + UpdaterLauncher.Extension + ") がありません。\n" + UpdateSource.ReleasesPage);
            res.Outcome = CheckOutcome.NewVersion;
            res.Message = "新しいバージョン " + r.Version + " があります (今は " + installed + ")。";
            return res;
        }

        static CheckResult Fail(CheckResult r, CheckOutcome o, string msg) { r.Outcome = o; r.Message = msg; DataStore.Log("update check: " + o + " " + msg.Replace("\n", " ")); return r; }

        // ---- ダウンロード ----

        /// <summary>ダウンロードして sha256 を確かめ、保存したパスを返す。だめなら例外 (利用者向けの文)</summary>
        public string Download(ReleaseInfo r, Action<int> progress)
        {
            if (r == null || r.Package == null) throw new InvalidOperationException("アップデート ファイルがありません。");
            var name = Path.GetFileName(r.Package.Name);
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(UpdaterLauncher.Extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("アップデート ファイルの名前が正しくありません: " + r.Package.Name);

            // 1) 確認用の sha256 (.sha256 ファイル → なければリリースの説明文)
            string expected = null;
            if (r.Checksum != null)
            {
                var c = Get(r.Checksum.Url, 64 * 1024, null);
                expected = ParseShaFile(Encoding.UTF8.GetString(c), name);
            }
            if (expected == null) expected = ShaFromNotes(r.Notes, name);
            if (expected == null) throw new InvalidOperationException("確認用の sha256 が公開されていないため、ダウンロードしたファイルを確かめられません。\nリリース ページから手動でダウンロードしてください。\n" + (r.PageUrl ?? UpdateSource.ReleasesPage));

            // 2) 本体
            var data = Get(r.Package.Url, MaxPackageBytes, (got, total) =>
            {
                if (progress == null) return;
                long all = total > 0 ? total : r.Package.Size;
                if (all > 0) progress((int)Math.Min(100, got * 100 / all));
            });
            if (r.Package.Size > 0 && data.Length != r.Package.Size) throw new InvalidOperationException("ダウンロードが途中で切れました (" + data.Length + " / " + r.Package.Size + " バイト)。もう一度お試しください。");
            var actual = Sha256Hex(data);
            if (actual != expected) throw new InvalidOperationException("ダウンロードしたファイルが公開されているものと一致しません (sha256 が違います)。\nファイルは使わずに消しました。");
            if (data.Length < 4 || data[0] != 'P' || data[1] != 'K') throw new InvalidOperationException("ダウンロードしたファイルはアップデート ファイルではありません。");

            // 3) 保存 (古いものは消す)
            var dir = UpdateSource.DownloadDir;
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*" + UpdaterLauncher.Extension)) { try { File.Delete(old); } catch { } }
            var path = Path.Combine(dir, name);
            DataStore.WriteFileAtomic(path, data);
            DataStore.Log("update downloaded: " + path + " sha256=" + actual);
            return path;
        }

        byte[] Get(string url, long max, Action<long, long> progress)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u) || !UpdateSource.IsAllowed(u)) throw new InvalidOperationException("GitHub 以外の場所からはダウンロードしません: " + url);
            var f = Http.Fetch(url, true, max, progress);
            if (f.Status == 0) throw new InvalidOperationException("ダウンロードできませんでした。インターネットの接続を確かめてください。" + (f.Error != null ? "\n(" + f.Error + ")" : ""));
            if (f.Status != 200 || f.Body == null) throw new InvalidOperationException("ダウンロードできませんでした (HTTP " + f.Status + ")。");
            if (f.FinalUri != null && !UpdateSource.IsAllowed(f.FinalUri)) throw new InvalidOperationException("想定外の場所に転送されました: " + f.FinalUri.Host);
            return f.Body;
        }
    }
}
