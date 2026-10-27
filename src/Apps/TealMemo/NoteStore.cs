// Teal Memo (メモ) - データ (notes.json) の読み書きと、旧メモアプリからの引き継ぎ
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Memo
{
    [DataContract]
    public class Note
    {
        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "text", Order = 2)] public string Text;
        [DataMember(Name = "created", Order = 3)] public string Created;
        [DataMember(Name = "updated", Order = 4)] public string Updated;

        public DateTime UpdatedLocal
        {
            get
            {
                DateTime d;
                if (DateTime.TryParse(Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d.ToLocalTime();
                return DateTime.MinValue;
            }
        }

        public string Title
        {
            get
            {
                foreach (string line in (Text ?? "").Split('\n'))
                {
                    string s = line.Trim();
                    if (s.Length > 0) return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
                }
                return "（無題）";
            }
        }

        public string Preview
        {
            get
            {
                var l = (Text ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Skip(1).FirstOrDefault();
                return l ?? "";
            }
        }

        public static Note Create()
        {
            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            return new Note { Id = Guid.NewGuid().ToString("N"), Text = "", Created = now, Updated = now };
        }
        public void Touch() { Updated = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
    }

    [DataContract]
    public class NoteFile
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "notes", Order = 2)] public List<Note> Notes = new List<Note>();
    }

    public static class NoteStore
    {
        public const string FileName = "notes.json";

        /// <summary>旧「メモアプリ」(単体版) のデータの場所</summary>
        public static string LegacyPath
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("MEMOAPP_DATA_DIR");
                string dir = !string.IsNullOrEmpty(over) ? over
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoApp");
                return Path.Combine(dir, "notes.json");
            }
        }

        /// <summary>
        /// 新しい保存先にデータがなく、旧メモアプリのデータがあればコピーして引き継ぐ。
        /// 旧ファイルは消さない。引き継いだら件数を返す (しなければ -1)。
        /// </summary>
        public static int MigrateLegacy()
        {
            try
            {
                if (File.Exists(DataStore.PathOf(FileName))) return -1;
                string old = LegacyPath;
                if (!File.Exists(old)) return -1;
                var f = DataStore.FromJson<NoteFile>(File.ReadAllBytes(old));   // 読めることを確認してから
                if (f == null || f.Notes == null) return -1;
                DataStore.Save(FileName, f);
                DataStore.Log("migrated " + f.Notes.Count + " notes from " + old);
                return f.Notes.Count;
            }
            catch (Exception ex) { DataStore.Log("migrate failed: " + ex.Message); return -1; }
        }

        public static NoteFile Load(out string warning)
        {
            var f = DataStore.LoadOrDefault(FileName, () => new NoteFile(), out warning);
            if (f.Notes == null) f.Notes = new List<Note>();
            f.Notes.RemoveAll(n => n == null);
            foreach (var n in f.Notes)
            {
                if (string.IsNullOrEmpty(n.Id)) n.Id = Guid.NewGuid().ToString("N");
                if (n.Text == null) n.Text = "";
                if (string.IsNullOrEmpty(n.Updated)) n.Touch();
                if (string.IsNullOrEmpty(n.Created)) n.Created = n.Updated;
            }
            return f;
        }

        public static void Save(NoteFile f) { DataStore.Save(FileName, f); }

        /// <summary>検索 (スペース区切りの AND、全角半角・かなカナ・大小文字を区別しない) + 更新日時の新しい順</summary>
        public static List<Note> Filter(IEnumerable<Note> notes, string query)
        {
            var terms = (query ?? "").Trim().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            var cmp = CultureInfo.CurrentCulture.CompareInfo;
            var opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;
            return notes.Where(n => terms.All(t => cmp.IndexOf(n.Text ?? "", t, opts) >= 0))
                        .OrderByDescending(n => n.Updated, StringComparer.Ordinal).ToList();
        }

        public static string FormatTime(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm");
            if (t.Year == now.Year) return t.ToString("M/d HH:mm");
            return t.ToString("yyyy/M/d");
        }
    }
}
