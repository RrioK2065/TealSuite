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
        public const int Max = 10;
        public const int MaxPins = Max - 1;
        public const int MaxTextLength = 200000;
        readonly List<ClipItem> items = new List<ClipItem>();
        public event Action Changed;

        public IList<ClipItem> Items { get { return items.AsReadOnly(); } }
        public int PinnedCount { get { return items.Count(i => i.Pinned); } }

        public History() { }
        public History(IEnumerable<ClipItem> initial)
        {
            foreach (var i in initial ?? new ClipItem[0])
                if (i != null && !string.IsNullOrEmpty(i.Text) && !items.Any(x => x.Text == i.Text)) items.Add(i);
            Normalize();
        }

        /// <summary>追加 (空白だけ・長すぎる文字は無視)。履歴が変わったら true。</summary>
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
            if (pinned && PinnedCount >= MaxPins) return false;
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
            while (sorted.Count > Max)
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

        public static History Load()
        {
            try
            {
                var bytes = DataStore.ReadBytes(FileName);
                if (bytes == null) return new History();
                var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                var data = DataStore.FromJson<HistoryData>(plain);
                Array.Clear(plain, 0, plain.Length);
                return new History(data == null ? null : data.Items);
            }
            catch (Exception ex)
            {
                DataStore.Log("history load: " + ex.Message);
                try { var p = DataStore.PathOf(FileName); File.Move(p, p + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss")); } catch { }
                return new History();
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
    }
}
