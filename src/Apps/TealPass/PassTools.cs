// Teal Pass (パスワード管理) - 1.8.0 の道具 (画面なし。テストできる)
//  - 以前のパスワードの記録 (編集でパスワードを変えたとき)
//  - 安全チェック (使い回し・弱い・長く変えていない)
//  - ブラウザーの CSV (Chrome / Edge / Firefox / Bitwarden など) の読み込みと、CSV の書き出し
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TealSuite.Common;

namespace TealSuite.Pass
{
    [Flags]
    public enum PassIssue { None = 0, Reused = 1, Weak = 2, Old = 4, Empty = 8 }

    public class HealthReport
    {
        public readonly Dictionary<VaultEntry, PassIssue> Issues = new Dictionary<VaultEntry, PassIssue>();
        public int Reused, Weak, Old, Empty;
        public int Total { get { return Issues.Count; } }
        public PassIssue Of(VaultEntry e) { PassIssue i; return e != null && Issues.TryGetValue(e, out i) ? i : PassIssue.None; }
    }

    public class ImportResult
    {
        public readonly List<VaultEntry> Added = new List<VaultEntry>();
        public int Duplicates, Skipped;
        public string Error;
        public string Format;
    }

    public static class PassTools
    {
        public const int HistoryMax = 5;
        /// <summary>この日数より前に変えたパスワードは「長く変えていない」</summary>
        public const int OldDays = 365;

        /// <summary>編集を確定するとき: パスワードが変わっていたら、前のものを記録に入れて変更日時を更新する</summary>
        public static void ApplyEdit(VaultEntry before, VaultEntry edited, DateTime now)
        {
            if (before == null || edited == null) return;
            if (before.Password == edited.Password) return;
            if (!string.IsNullOrEmpty(before.Password))
            {
                var list = edited.History ?? new List<PasswordHistoryItem>();
                list.RemoveAll(h => h.Password == before.Password || h.Password == edited.Password);
                list.Insert(0, new PasswordHistoryItem { Password = before.Password, Changed = now });
                if (list.Count > HistoryMax) list.RemoveRange(HistoryMax, list.Count - HistoryMax);
                edited.History = list;
            }
            edited.PasswordChanged = now;
        }

        public static string IssueText(PassIssue i)
        {
            var parts = new List<string>();
            if ((i & PassIssue.Empty) != 0) parts.Add("パスワードが空");
            if ((i & PassIssue.Reused) != 0) parts.Add("ほかのサイトと同じパスワード");
            if ((i & PassIssue.Weak) != 0) parts.Add("弱いパスワード");
            if ((i & PassIssue.Old) != 0) parts.Add("1 年以上変えていない");
            return string.Join("・", parts);
        }

        /// <summary>全部のパスワードを調べる (問題のあるものだけ Issues に入る)</summary>
        public static HealthReport Check(IEnumerable<VaultEntry> entries, DateTime now)
        {
            var r = new HealthReport();
            var all = entries.ToList();
            var counts = all.Where(e => !string.IsNullOrEmpty(e.Password)).GroupBy(e => e.Password, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var e in all)
            {
                var i = PassIssue.None;
                if (string.IsNullOrEmpty(e.Password)) i |= PassIssue.Empty;
                else
                {
                    if (counts[e.Password] > 1) i |= PassIssue.Reused;
                    if (StrengthMeter.Evaluate(e.Password).Level <= 1) i |= PassIssue.Weak;
                    if ((now - e.PasswordDate).TotalDays >= OldDays) i |= PassIssue.Old;
                }
                if (i == PassIssue.None) continue;
                r.Issues[e] = i;
                if ((i & PassIssue.Reused) != 0) r.Reused++;
                if ((i & PassIssue.Weak) != 0) r.Weak++;
                if ((i & PassIssue.Old) != 0) r.Old++;
                if ((i & PassIssue.Empty) != 0) r.Empty++;
            }
            return r;
        }

        /// <summary>同じパスワードを使っているほかの登録</summary>
        public static List<VaultEntry> SamePassword(IEnumerable<VaultEntry> entries, VaultEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Password)) return new List<VaultEntry>();
            return entries.Where(x => x != e && x.Password == e.Password).ToList();
        }

        /// <summary>URL からサイト名を作る (https://www.example.com/login → example.com)</summary>
        public static string SiteFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            var u = url.Trim();
            if (!u.Contains("://")) u = "https://" + u;
            Uri uri;
            if (!Uri.TryCreate(u, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host)) return url.Trim();
            var h = uri.Host;
            if (h.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) h = h.Substring(4);
            return h;
        }

        /// <summary>
        /// CSV を読む。見出しの名前で列を探すので、Chrome・Edge (name,url,username,password,note)、
        /// Firefox (url,username,password,...)、Bitwarden (name,notes,login_uri,login_username,login_password)、
        /// Teal Pass の書き出しなどがそのまま読める。今の登録と同じもの (サイト・ID・パスワード) は飛ばす
        /// </summary>
        public static ImportResult ImportCsv(string text, IEnumerable<VaultEntry> existing, DateTime now)
        {
            var r = new ImportResult();
            var rows = TextKit.ParseCsv(text);
            if (rows.Count < 1) { r.Error = "CSV が空です。"; return r; }
            var h = rows[0];
            int cName = TextKit.Column(h, "name", "title", "site", "サイト名", "サイト"),
                cUrl = TextKit.Column(h, "url", "login_uri", "website", "uri", "URL"),
                cUser = TextKit.Column(h, "username", "login_username", "user", "login", "email", "ユーザー名", "ID"),
                cPass = TextKit.Column(h, "password", "login_password", "パスワード"),
                cNote = TextKit.Column(h, "note", "notes", "memo", "extra", "メモ"),
                cFav = TextKit.Column(h, "favorite");
            if (cPass < 0 || (cUrl < 0 && cName < 0)) { r.Error = "パスワードの CSV として読めませんでした。\n1 行目に「name / url / username / password」のような見出しが必要です。"; return r; }
            r.Format = cUrl >= 0 && h.Any(x => x == "login_uri") ? "Bitwarden" : cName < 0 ? "Firefox" : "Chrome / Edge など";
            var seen = new HashSet<string>(existing.Select(Key), StringComparer.Ordinal);
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                string name = TextKit.Cell(row, cName), url = TextKit.Cell(row, cUrl), user = TextKit.Cell(row, cUser), note = TextKit.Cell(row, cNote);
                string pass = cPass < row.Count ? (row[cPass] ?? "") : "";
                if (name.Length == 0) name = SiteFromUrl(url);
                if (name.Length == 0 || (pass.Length == 0 && user.Length == 0)) { r.Skipped++; continue; }
                var e = new VaultEntry { Site = name, Url = url, UserId = user, Password = pass, Memo = note.Replace("\r\n", "\n"), Created = now, Updated = now };
                e.Favorite = TextKit.Cell(row, cFav) == "1" || TextKit.Cell(row, cFav).Equals("true", StringComparison.OrdinalIgnoreCase);
                var k = Key(e);
                if (seen.Contains(k)) { r.Duplicates++; continue; }
                seen.Add(k);
                r.Added.Add(e);
            }
            return r;
        }

        static string Key(VaultEntry e)
        {
            return (e.Site ?? "").Trim().ToLowerInvariant() + "\u0001" + (e.UserId ?? "").Trim().ToLowerInvariant() + "\u0001" + (e.Password ?? "");
        }

        /// <summary>Chrome と同じ形の CSV (name,url,username,password,note)。ほかのパスワード管理にも読み込める</summary>
        public static string ToCsv(IEnumerable<VaultEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("name,url,username,password,note\r\n");
            foreach (var e in entries.OrderBy(x => x.Site, StringComparer.CurrentCultureIgnoreCase))
                sb.Append(string.Join(",", new[] { Raw(e.Site), Raw(e.Url), Raw(e.UserId), Raw(e.Password), Raw((e.Memo ?? "").Replace("\r\n", "\n")) })).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>パスワードの CSV は値をそのまま残す (= で始まるものに ' をつけない) ので、囲むだけ</summary>
        static string Raw(string v)
        {
            v = v ?? "";
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || (v.Length > 0 && (v[0] == ' ' || v[v.Length - 1] == ' '))) v = "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
