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
        /// <summary>1.8.0: ピン留め (一覧のいちばん上に出す)</summary>
        [DataMember(Name = "pinned", Order = 5, EmitDefaultValue = false)] public bool Pinned;
        /// <summary>1.8.0: ゴミ箱に入れた日時 (UTC, ISO 8601)。null なら通常のメモ</summary>
        [DataMember(Name = "deleted", Order = 6, EmitDefaultValue = false)] public string Deleted;

        [IgnoreDataMember] public bool InTrash { get { return !string.IsNullOrEmpty(Deleted); } }
        [IgnoreDataMember]
        public DateTime DeletedUtc
        {
            get
            {
                DateTime d;
                if (DateTime.TryParse(Deleted, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return d.ToUniversalTime();
                return DateTime.UtcNow;
            }
        }
        /// <summary>本文の #タグ (重複なし、出てきた順)</summary>
        [IgnoreDataMember] public List<string> Tags { get { return NoteStore.TagsOf(Text); } }

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

    /// <summary>1.8.0: Teal Memo の表示設定 (settings.json)</summary>
    [DataContract]
    public class MemoSettings
    {
        public const string FileName = "settings.json";
        public static readonly string[] SizeLabels = { "小", "中 (標準)", "大", "特大" };
        public static readonly float[] SizeScale = { 0.85f, 1f, 1.25f, 1.55f };
        /// <summary>文字の大きさ 0〜3 (1 = 標準)</summary>
        [DataMember(Name = "fontSize", Order = 1)] public int FontSize = 1;
        [OnDeserializing] void Defaults(StreamingContext c) { FontSize = 1; }
        public int SafeSize { get { return Math.Max(0, Math.Min(SizeScale.Length - 1, FontSize)); } }
    }

    public static class NoteStore
    {
        public const string FileName = "notes.json";
        /// <summary>ゴミ箱に残す日数 (過ぎたら自動で消す)</summary>
        public const int TrashDays = 30;
        static readonly System.Text.RegularExpressions.Regex TagRe = new System.Text.RegularExpressions.Regex(@"(?<![\w#＃&])[#＃]([^\s#＃、。，．,.!！?？()（）「」『』\[\]【】<>＜＞""'”’:：;；/／]{1,30})");

        /// <summary>本文から #タグ を取り出す (数字だけのもの #1 などは除く)。全角の ＃ も使える</summary>
        public static List<string> TagsOf(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (System.Text.RegularExpressions.Match m in TagRe.Matches(text))
            {
                var t = m.Groups[1].Value;
                if (t.All(char.IsDigit)) continue;
                if (!list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) list.Add(t);
            }
            return list;
        }

        /// <summary>ゴミ箱以外のメモにあるタグ (多い順、同じなら名前順)</summary>
        public static List<string> AllTags(IEnumerable<Note> notes)
        {
            return notes.Where(n => !n.InTrash).SelectMany(n => n.Tags)
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCulture)
                .Select(g => g.First()).ToList();
        }

        /// <summary>ゴミ箱に入れる / 戻す</summary>
        public static void Trash(Note n, DateTime utcNow) { n.Deleted = utcNow.ToString("o", CultureInfo.InvariantCulture); }
        public static void Restore(Note n) { n.Deleted = null; }

        /// <summary>ゴミ箱に入れて TrashDays 日たったメモを消す。消した数を返す</summary>
        public static int PurgeTrash(NoteFile f, DateTime utcNow)
        {
            return f.Notes.RemoveAll(n => n.InTrash && (utcNow - n.DeletedUtc).TotalDays >= TrashDays);
        }

        /// <summary>ゴミ箱にあと何日残るか</summary>
        public static int DaysLeft(Note n, DateTime utcNow)
        {
            return Math.Max(0, TrashDays - (int)Math.Floor((utcNow - n.DeletedUtc).TotalDays));
        }

        /// <summary>一覧に出すメモ。view: null = すべて / "#タグ" = そのタグ / "trash" = ゴミ箱 / "pinned" = ピン留め</summary>
        public static List<Note> View(IEnumerable<Note> notes, string query, string view)
        {
            bool trash = view == "trash";
            var src = notes.Where(n => n.InTrash == trash);
            if (view == "pinned") src = src.Where(n => n.Pinned);
            else if (view != null && view.StartsWith("#"))
            {
                var tag = view.Substring(1);
                src = src.Where(n => n.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
            }
            var list = Filter(src, query);
            if (trash) return list.OrderByDescending(n => n.Deleted, StringComparer.Ordinal).ToList();
            // ピン留めを上に (それぞれの中は更新の新しい順のまま)
            return list.Where(n => n.Pinned).Concat(list.Where(n => !n.Pinned)).ToList();
        }

        /// <summary>.txt の中身から新しいメモを作る (本文はそのまま。更新日時はファイルの日時)</summary>
        public static Note FromText(string text, DateTime fileTimeUtc)
        {
            var n = Note.Create();
            n.Text = (text ?? "").TrimEnd('\n');
            var t = fileTimeUtc.ToString("o", CultureInfo.InvariantCulture);
            n.Updated = t;
            return n;
        }

        /// <summary>書き出し用の本文 (Windows のメモ帳で開ける CRLF)</summary>
        public static string ToText(Note n) { return (n.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"); }

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
            PurgeTrash(f, DateTime.UtcNow);
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
