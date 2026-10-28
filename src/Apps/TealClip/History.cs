// Teal Clip (コピー履歴) - 履歴データ (最大 10 件・重複なし・新しい順・ピン留め)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using TealSuite.Common;

namespace TealSuite.Clip
{
    [DataContract]
    public class ClipItem
    {
        [DataMember(Name = "text", Order = 1)] public string Text = "";
        [DataMember(Name = "time", Order = 2)] public DateTime Time = DateTime.Now;
        [DataMember(Name = "pinned", Order = 3)] public bool Pinned;

        /// <summary>一覧用の 1 行表示 (改行・タブを記号にして詰める)</summary>
        public string OneLine
        {
            get
            {
                var s = (Text ?? "").Replace("\r\n", "\n").Trim();
                var sb = new StringBuilder();
                foreach (var c in s)
                {
                    if (sb.Length >= 200) { sb.Append("…"); break; }
                    if (c == '\n') sb.Append(" ⏎ ");
                    else if (c == '\t') sb.Append(' ');
                    else if (!char.IsControl(c)) sb.Append(c);
                }
                return sb.ToString();
            }
        }
    }

    [DataContract]
    public class HistoryData
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "items", Order = 2)] public List<ClipItem> Items = new List<ClipItem>();
    }

    /// <summary>
    /// 並び順: ピン留め → 新しい順。合計 10 件を超えたら、ピン留めしていない古いものから消す。
    /// 同じ文字列をもう一度コピーしたら、新しく追加せずに先頭へ移動する。
    /// </summary>
    public class History
    {
        /// <summary>初期設定の件数 (1.7 までは固定)。1.8.0 から Capacity で 10〜50 件を選べる</summary>
        public const int Max = 10;
        public const int MaxPins = Max - 1;
        public const int MaxTextLength = 200000;
        /// <summary>選べる件数</summary>
        public static readonly int[] Capacities = { 10, 20, 30, 50 };
        /// <summary>ポップアップ (1〜0 キー) に出す件数</summary>
        public const int PopupRows = 10;
        int capacity = Max;
        /// <summary>保存する件数 (ピン留め・定型文を含む)</summary>
        public int Capacity { get { return capacity; } }
        /// <summary>ピン留め (定型文) の上限。新しいコピーの枠を 1 件は残す</summary>
        public int PinLimit { get { return capacity - 1; } }
        readonly List<ClipItem> items = new List<ClipItem>();
        public event Action Changed;

        public IList<ClipItem> Items { get { return items.AsReadOnly(); } }
        public int PinnedCount { get { return items.Count(i => i.Pinned); } }

        public History() { }
        public History(IEnumerable<ClipItem> initial) : this(initial, Max) { }
        public History(IEnumerable<ClipItem> initial, int capacity)
        {
            this.capacity = ClampCapacity(capacity);
            foreach (var i in initial ?? new ClipItem[0])
                if (i != null && !string.IsNullOrEmpty(i.Text) && !items.Any(x => x.Text == i.Text)) items.Add(i);
            Normalize();
        }

        /// <summary>追加 (空白だけ・長すぎる文字は無視)。履歴が変わったら true。</summary>
        public static int ClampCapacity(int n) { return Math.Max(Max, Math.Min(Capacities[Capacities.Length - 1], n)); }

        /// <summary>件数を変える。減らすと、ピン留めしていない古いものから消える。消えた件数</summary>
        public int SetCapacity(int n)
        {
            n = ClampCapacity(n);
            if (n == capacity) return 0;
            int before = items.Count;
            capacity = n;
            // 減らしたときにピン留めが上限を超えたら、古いピンから外す
            foreach (var p in items.Where(i => i.Pinned).OrderBy(i => i.Time).Take(Math.Max(0, PinnedCount - PinLimit)).ToList()) p.Pinned = false;
            Normalize();
            Fire();
            return before - items.Count;
        }

        /// <summary>ピン留めしていない、days 日より前の項目を消す。消えた件数</summary>
        public int PurgeOlderThan(int days, DateTime now)
        {
            if (days <= 0) return 0;
            var limit = now.AddDays(-days);
            int n = items.RemoveAll(i => !i.Pinned && i.Time < limit);
            if (n > 0) Fire();
            return n;
        }

        /// <summary>定型文を追加 (ピン留めとして入る)。上限なら null</summary>
        public ClipItem AddSnippet(string text, DateTime? when = null)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0 || text.Length > MaxTextLength) return null;
            var ex = items.FirstOrDefault(i => i.Text == text);
            if (ex != null && ex.Pinned) return ex;
            if (PinnedCount >= PinLimit) return null;
            if (ex == null) { ex = new ClipItem { Text = text, Time = when ?? DateTime.Now }; items.Add(ex); }
            ex.Pinned = true;
            Normalize(); Fire();
            return ex;
        }

        /// <summary>項目の文字を書き換える (定型文の編集)。同じ文字がほかにあれば、そちらは消す</summary>
        public bool UpdateText(ClipItem item, string text)
        {
            if (!items.Contains(item) || string.IsNullOrEmpty(text) || text.Trim().Length == 0 || text.Length > MaxTextLength) return false;
            if (item.Text == text) return false;
            items.RemoveAll(i => i != item && i.Text == text);
            item.Text = text;
            Normalize(); Fire();
            return true;
        }

        /// <summary>検索 (大文字小文字を区別しない。空白区切りはすべて含むもの)</summary>
        public List<ClipItem> Search(string query)
        {
            var words = (query ?? "").Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            return items.Where(i => words.All(w => i.Text.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
        }

        public bool Add(string text, DateTime? when = null)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0 || text.Length > MaxTextLength) return false;
            var now = when ?? DateTime.Now;
            var ex = items.FirstOrDefault(i => i.Text == text);
            if (ex != null)
            {
                if (items.IndexOf(ex) == FirstUnpinnedIndex() && !ex.Pinned) { ex.Time = now; return false; } // すでに先頭
                ex.Time = now;
            }
            else items.Add(new ClipItem { Text = text, Time = now });
            Normalize();
            Fire();
            return true;
        }

        int FirstUnpinnedIndex() { int i = items.FindIndex(x => !x.Pinned); return i < 0 ? items.Count : i; }

        public bool SetPinned(ClipItem item, bool pinned)
        {
            if (!items.Contains(item) || item.Pinned == pinned) return false;
            if (pinned && PinnedCount >= PinLimit) return false;
            item.Pinned = pinned;
            Normalize(); Fire();
            return true;
        }

        public bool Remove(ClipItem item)
        {
            if (!items.Remove(item)) return false;
            Fire(); return true;
        }

        public void Clear() { if (items.Count == 0) return; items.Clear(); Fire(); }

        void Normalize()
        {
            var sorted = items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.Time).ToList();
            while (sorted.Count > capacity)
            {
                var victim = sorted.LastOrDefault(i => !i.Pinned) ?? sorted.Last();
                sorted.Remove(victim);
            }
            items.Clear(); items.AddRange(sorted);
        }

        void Fire() { if (Changed != null) Changed(); }

        public HistoryData ToData() { return new HistoryData { Items = items.ToList() }; }
    }

    /// <summary>履歴ファイル (history.dat)。コピーした内容にはパスワードなどが含まれうるので DPAPI で暗号化する。</summary>
    public static class HistoryStore
    {
        public const string FileName = "history.dat";
        // 互換性のため変更しない (1.0 で保存した履歴を復号するのに必要)
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AppSuite.ClipboardHistory.v1");

        public static History Load() { return Load(History.Max); }
        public static History Load(int capacity)
        {
            try
            {
                var bytes = DataStore.ReadBytes(FileName);
                if (bytes == null) return new History(null, capacity);
                var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                var data = DataStore.FromJson<HistoryData>(plain);
                Array.Clear(plain, 0, plain.Length);
                return new History(data == null ? null : data.Items, capacity);
            }
            catch (Exception ex)
            {
                DataStore.Log("history load: " + ex.Message);
                try { var p = DataStore.PathOf(FileName); File.Move(p, p + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
                return new History(null, capacity);
            }
        }

        public static void Save(History h)
        {
            try
            {
                var plain = DataStore.ToJson(h.ToData());
                var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                Array.Clear(plain, 0, plain.Length);
                DataStore.WriteBytes(FileName, enc);
            }
            catch (Exception ex) { DataStore.Log("history save: " + ex.Message); }
        }
    }

    [DataContract]
    public class ChSettings
    {
        [DataMember(Name = "hotkey", Order = 1)] public string Hotkey = TrayContext.DefaultHotkey.ToString();
        [DataMember(Name = "firstRunTipShown", Order = 2)] public bool FirstRunTipShown;
        /// <summary>1.8.0: 保存する件数 (10 / 20 / 30 / 50)</summary>
        [DataMember(Name = "historySize", Order = 3)] public int HistorySize = History.Max;
        /// <summary>1.8.0: これを含む文字は記録しない (大文字小文字を区別しない)</summary>
        [DataMember(Name = "excludeWords", Order = 4, EmitDefaultValue = false)] public List<string> ExcludeWords;
        /// <summary>1.8.0: ピン留めしていない項目を、この日数で自動で消す (0 = 消さない)</summary>
        [DataMember(Name = "autoDeleteDays", Order = 5, EmitDefaultValue = false)] public int AutoDeleteDays;
        /// <summary>1.8.0: パスワードらしい文字 (空白なし・英大小・数字・記号が混ざる 8〜64 文字) を記録しない</summary>
        [DataMember(Name = "skipPasswords", Order = 6, EmitDefaultValue = false)] public bool SkipPasswordLike;

        public static readonly int[] AutoDeleteChoices = { 0, 1, 7, 30, 90 };
        public static readonly string[] AutoDeleteLabels = { "消さない", "1 日", "7 日", "30 日", "90 日" };

        [OnDeserializing] void Defaults(StreamingContext c) { HistorySize = History.Max; }

        public List<string> Words { get { return (ExcludeWords ?? new List<string>()).Select(w => (w ?? "").Trim()).Where(w => w.Length > 0).Distinct().ToList(); } }
    }

    /// <summary>記録するかどうかの判定 (画面なし。テストできる)</summary>
    public static class ClipFilter
    {
        /// <summary>記録しない理由 (記録してよければ null)</summary>
        public static string SkipReason(string text, ChSettings s)
        {
            if (string.IsNullOrEmpty(text) || s == null) return null;
            foreach (var w in s.Words)
                if (text.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0) return "除外キーワード「" + w + "」";
            if (s.SkipPasswordLike && LooksLikePassword(text)) return "パスワードらしい文字";
            return null;
        }

        /// <summary>空白なしの 8〜64 文字で、英大文字・英小文字・数字・記号のうち 3 種類以上が混ざり、URL やメールではないもの</summary>
        public static bool LooksLikePassword(string t)
        {
            if (t == null) return false;
            t = t.Trim();
            if (t.Length < 8 || t.Length > 64) return false;
            if (t.Any(c => char.IsWhiteSpace(c) || c > 0x7E || c < 0x21)) return false;
            if (t.Contains("://") || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$")) return false;
            int kinds = (t.Any(char.IsUpper) ? 1 : 0) + (t.Any(char.IsLower) ? 1 : 0) + (t.Any(char.IsDigit) ? 1 : 0) + (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
            return kinds >= 3;
        }
    }
}
