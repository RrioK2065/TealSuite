// Teal Hub (アプリ管理) - 1.8.0: 全アプリのデータのバックアップと復元、更新内容 (CHANGELOG) の表示
//  - バックアップ: %APPDATA%\TealSuite の中身を 1 つの zip にまとめる (TealHub\backups は入れない)
//  - 復元: 先に今のデータを TealHub\backups\before-restore-*.zip に残してから、zip の中身で上書きする
//  - Teal Pass の保管庫と Teal Clip の履歴は Windows の DPAPI で暗号化されているので、
//    同じ Windows ユーザーでないと開けない (別のパソコン・別のユーザーへの引っ越しには使えない)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public class BackupInfo
    {
        public bool Valid;
        public string Error;
        public string Version;
        public DateTime Created;
        public int FileCount;
        public long Bytes;
        public List<string> Apps = new List<string>();
        /// <summary>DPAPI で暗号化したデータ (Teal Pass・Teal Clip) が入っているか</summary>
        public bool HasProtected;
    }

    public static class HubBackup
    {
        public const string Marker = "tealsuite-backup.txt";
        public const string Extension = ".zip";
        /// <summary>復元の前に残すコピーの置き場 (Teal Hub のデータの中)</summary>
        public const string SafetyDirName = "backups";
        public const int KeepSafety = 5;
        /// <summary>DPAPI で暗号化しているファイル (アプリ ID/ファイル名)</summary>
        static readonly string[] Protected = { "TealPass/vault.dat", "TealClip/history.dat" };

        static ZipArchive OpenZip(string path)
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, false, Encoding.UTF8);
        }

        public static string SafetyDir(string root) { return Path.Combine(root, "TealHub", SafetyDirName); }

        public static string DefaultName(DateTime now) { return "TealSuite-backup-" + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + Extension; }

        static bool Skip(string rel)
        {
            var r = rel.Replace('\\', '/');
            if (r.StartsWith("TealHub/" + SafetyDirName + "/", StringComparison.OrdinalIgnoreCase)) return true;
            if (r.StartsWith(".update-staging/", StringComparison.OrdinalIgnoreCase)) return true;
            if (r.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(r, Marker, StringComparison.OrdinalIgnoreCase)) return true;
            // 1.10.0: Teal Pass のサイト名の索引は、Teal Pass が起動したときに作り直すので入れない
            if (string.Equals(r, "TealPass/site-index.dat", StringComparison.OrdinalIgnoreCase)) return true;
            // 1.11.0: Teal Pin のいま固定中のウィンドウの控え (落ちたときに外すため) は、その時だけのものなので入れない (設定・ルール・プリセットは入れる)
            if (string.Equals(r, "TealPin/pinned.json", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Rel(string root, string full)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/');
            return Path.GetFullPath(full).Substring(r.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        /// <summary>root の中身を zip にまとめる。入れたファイルの数</summary>
        public static int Create(string root, string zipPath, string version, DateTime now)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("データのフォルダがありません: " + root);
            var zipFull = Path.GetFullPath(zipPath);
            string tmp = zipPath + ".tmp";
            int n = 0;
            var apps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFullPath(f), zipFull, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(tmp), StringComparison.OrdinalIgnoreCase)) continue;
                    var rel = Rel(root, f);
                    if (Skip(rel)) continue;
                    byte[] data;
                    try
                    {
                        using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var ms = new MemoryStream()) { src.CopyTo(ms); data = ms.ToArray(); }
                    }
                    catch (Exception ex) { DataStore.Log("backup skip " + rel + ": " + ex.Message); continue; }
                    var e = z.CreateEntry(rel, CompressionLevel.Optimal);
                    try { e.LastWriteTime = File.GetLastWriteTime(f); } catch { }
                    using (var w = e.Open()) w.Write(data, 0, data.Length);
                    n++;
                    if (rel.Contains("/")) apps.Add(rel.Substring(0, rel.IndexOf('/')));
                }
                var m = z.CreateEntry(Marker);
                using (var w = new StreamWriter(m.Open(), new UTF8Encoding(false)))
                {
                    w.Write("TealSuite のデータのバックアップ\r\n");
                    w.Write("version=" + (version ?? "") + "\r\n");
                    w.Write("created=" + now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\r\n");
                    w.Write("files=" + n + "\r\n");
                    w.Write("apps=" + string.Join(",", apps) + "\r\n");
                    w.Write("Teal Pass の保管庫と Teal Clip の履歴は、バックアップしたときと同じ Windows ユーザーでだけ開けます。\r\n");
                }
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
            return n;
        }

        /// <summary>zip が TealSuite のバックアップかを調べる</summary>
        public static BackupInfo Inspect(string zipPath)
        {
            var info = new BackupInfo();
            try
            {
                using (var z = OpenZip(zipPath))
                {
                    var m = z.Entries.FirstOrDefault(e => string.Equals(e.FullName, Marker, StringComparison.OrdinalIgnoreCase));
                    if (m == null) { info.Error = "TealSuite のバックアップ ファイルではありません (" + Marker + " がありません)"; return info; }
                    using (var r = new StreamReader(m.Open(), Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            int i = line.IndexOf('='); if (i <= 0) continue;
                            var k = line.Substring(0, i); var v = line.Substring(i + 1);
                            if (k == "version") info.Version = v;
                            else if (k == "created") DateTime.TryParseExact(v, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out info.Created);
                        }
                    }
                    foreach (var e in z.Entries)
                    {
                        if (e.FullName.EndsWith("/") || e == m) continue;
                        if (!SafeName(e.FullName)) { info.Error = "ファイル名が正しくない項目があります: " + e.FullName; return info; }
                        info.FileCount++; info.Bytes += e.Length;
                        var n = e.FullName.Replace('\\', '/');
                        if (n.Contains("/")) { var a = n.Substring(0, n.IndexOf('/')); if (!info.Apps.Contains(a, StringComparer.OrdinalIgnoreCase)) info.Apps.Add(a); }
                        if (Protected.Any(p => string.Equals(p, n, StringComparison.OrdinalIgnoreCase))) info.HasProtected = true;
                    }
                    info.Apps.Sort(StringComparer.OrdinalIgnoreCase);
                    info.Valid = true;
                }
            }
            catch (Exception ex) { info.Error = "zip を読めませんでした: " + ex.Message; }
            return info;
        }

        /// <summary>zip の中のパスが root の外に出ないか (.. や絶対パスを拒む)</summary>
        public static bool SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.Replace('\\', '/');
            if (n.StartsWith("/") || n.Contains(":")) return false;
            return !n.Split('/').Any(p => p == ".." || p == ".");
        }

        /// <summary>
        /// 復元: 先に今のデータを safetyZip に残し、zip の中身で上書きする (zip にないファイルは消さない)。
        /// 書き戻したファイルの数
        /// </summary>
        public static int Restore(string zipPath, string root, string safetyZip, string version, DateTime now)
        {
            return Restore(zipPath, root, safetyZip, version, now, null);
        }

        /// <summary>
        /// 1.9.0: 名前が変わったアプリ (旧ID → 新ID) のフォルダを付け替えて復元する。
        /// zip に旧ID/ があって新ID/ がない (= 名前が変わる前のバックアップ) ときだけ、旧ID/… を新ID/… に書く
        /// </summary>
        public static Dictionary<string, string> FolderMap(BackupInfo info, IDictionary<string, string> renames)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (info == null || renames == null) return map;
            foreach (var kv in renames)
                if (info.Apps.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) && !info.Apps.Contains(kv.Value, StringComparer.OrdinalIgnoreCase))
                    map[kv.Key] = kv.Value;
            return map;
        }

        static string MapName(string name, Dictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return name;
            var n = name.Replace('\\', '/');
            int i = n.IndexOf('/');
            if (i <= 0) return name;
            string to;
            return map.TryGetValue(n.Substring(0, i), out to) ? to + n.Substring(i) : name;
        }

        public static int Restore(string zipPath, string root, string safetyZip, string version, DateTime now, IDictionary<string, string> renames)
        {
            var info = Inspect(zipPath);
            if (!info.Valid) throw new InvalidDataException(info.Error);
            Directory.CreateDirectory(root);
            if (safetyZip != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(safetyZip));
                Create(root, safetyZip, version, now);
                PruneSafety(Path.GetDirectoryName(safetyZip));
            }
            var rootFull = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var map = FolderMap(info, renames);
            int n = 0;
            using (var z = OpenZip(zipPath))
            {
                foreach (var e in z.Entries)
                {
                    if (e.FullName.EndsWith("/") || string.Equals(e.FullName, Marker, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SafeName(e.FullName) || Skip(e.FullName)) continue;
                    var dest = Path.GetFullPath(Path.Combine(root, MapName(e.FullName, map).Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    var tmp = dest + ".restore.tmp";
                    using (var src = e.Open())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write)) src.CopyTo(dst);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                    try { File.SetLastWriteTime(dest, e.LastWriteTime.LocalDateTime); } catch { }
                    n++;
                }
            }
            return n;
        }

        public static string SafetyPath(string root, DateTime now)
        {
            return Path.Combine(SafetyDir(root), "before-restore-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + Extension);
        }

        static void PruneSafety(string dir)
        {
            try
            {
                foreach (var f in new DirectoryInfo(dir).GetFiles("before-restore-*" + Extension).OrderByDescending(f => f.Name).Skip(KeepSafety)) f.Delete();
            }
            catch (Exception ex) { DataStore.Log("prune backups: " + ex.Message); }
        }

        public static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }
    }

    /// <summary>CHANGELOG.md (TealHub.exe と同じフォルダ) から版ごとの節を取り出す</summary>
    public static class ChangeLog
    {
        public const string FileName = "CHANGELOG.md";
        public static string PathNext { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); } }

        /// <summary>(見出し, 本文) の一覧。新しい順 (ファイルの順)</summary>
        public static List<KeyValuePair<string, string>> Parse(string md)
        {
            var list = new List<KeyValuePair<string, string>>();
            string head = null; var body = new StringBuilder();
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## "))
                {
                    if (head != null) list.Add(new KeyValuePair<string, string>(head, body.ToString().Trim()));
                    head = raw.Substring(3).Trim(); body.Clear();
                    continue;
                }
                if (head == null) continue;
                var l = raw.TrimEnd();
                body.Append(l.StartsWith("- ") ? "・" + l.Substring(2) : l).Append('\n');
            }
            if (head != null) list.Add(new KeyValuePair<string, string>(head, body.ToString().Trim()));
            return list;
        }

        /// <summary>version の節 (見つからなければ null)</summary>
        public static string SectionFor(List<KeyValuePair<string, string>> all, string version)
        {
            foreach (var kv in all)
                if (kv.Key == version || kv.Key.StartsWith(version + " ")) return kv.Value;
            return null;
        }

        public static List<KeyValuePair<string, string>> Load()
        {
            try { if (File.Exists(PathNext)) return Parse(File.ReadAllText(PathNext, Encoding.UTF8)); }
            catch (Exception ex) { DataStore.Log("changelog: " + ex.Message); }
            return new List<KeyValuePair<string, string>>();
        }
    }

    /// <summary>「更新内容」: 版を選んで、その版で変わったことを読む</summary>
    public class WhatsNewForm : Form
    {
        public readonly ComboBox VersionBox;
        public readonly TextBox Notes = new TextBox();
        readonly List<KeyValuePair<string, string>> all;

        public WhatsNewForm(List<KeyValuePair<string, string>> sections, string current)
        {
            all = sections ?? new List<KeyValuePair<string, string>>();
            Theme.ApplyForm(this);
            Text = "TealSuite の更新内容";
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(600, 500);
            MinimumSize = Theme.S(420, 320);
            var header = new HeaderBar("更新内容", "今のバージョン: " + (current ?? AppInfo.Version), false);
            VersionBox = Lay.Combo(all.Count > 0 ? all.Select(kv => kv.Key).ToArray() : new[] { "(CHANGELOG.md が見つかりません)" });
            VersionBox.Width = Theme.S(260);
            Notes.Multiline = true; Notes.ReadOnly = true; Notes.ScrollBars = ScrollBars.Vertical; Notes.BorderStyle = BorderStyle.None;
            Notes.BackColor = Theme.Surface; Notes.Font = Theme.Body; Notes.Dock = DockStyle.Fill;
            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(Theme.ButtonHeight + 10), BackColor = Theme.Surface };
            var lbl = new Label { Text = "バージョン", Font = Theme.Body, ForeColor = Theme.SubText, AutoSize = true, BackColor = Theme.Surface, Location = new Point(0, Theme.S(6)) };
            VersionBox.Location = new Point(Theme.S(80), Theme.S(2));
            top.Controls.Add(lbl); top.Controls.Add(VersionBox);
            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(16, 12, 16, 8), BackColor = Theme.Surface };
            body.Controls.Add(Notes); body.Controls.Add(Ui.Spacer(8)); body.Controls.Add(top);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(56), BackColor = Theme.Background };
            var close = new FlatButton("閉じる", ButtonKind.Primary).FitWidth(80);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(close);
            bottom.Layout += (s, e) => close.Location = new Point(bottom.Width - Theme.S(12) - close.Width, (bottom.Height - close.Height) / 2);
            CancelButton = close; AcceptButton = close;
            Controls.Add(body); Controls.Add(bottom); Controls.Add(header);
            VersionBox.SelectedIndexChanged += (s, e) => ShowSelected();
            int idx = all.FindIndex(kv => current != null && (kv.Key == current || kv.Key.StartsWith(current + " ")));
            VersionBox.SelectedIndex = idx >= 0 ? idx : 0;
            ShowSelected();
        }

        void ShowSelected()
        {
            int i = VersionBox.SelectedIndex;
            Notes.Text = i >= 0 && i < all.Count ? all[i].Value.Replace("\n", "\r\n")
                : "更新内容のファイル (CHANGELOG.md) が見つかりませんでした。\r\nリリース ページでも確認できます。";
            Notes.SelectionStart = 0; Notes.SelectionLength = 0;
        }
    }
}
