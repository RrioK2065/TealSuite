// Teal Pass (パスワード管理) - 保存データ (DPAPI で暗号化した vault.dat)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using TealSuite.Common;

namespace TealSuite.Pass
{
    [DataContract]
    public class VaultEntry
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "site", Order = 2)] public string Site = "";
        [DataMember(Name = "url", Order = 3)] public string Url = "";
        [DataMember(Name = "user", Order = 4)] public string UserId = "";
        [DataMember(Name = "password", Order = 5)] public string Password = "";
        [DataMember(Name = "memo", Order = 6)] public string Memo = "";
        [DataMember(Name = "created", Order = 7)] public DateTime Created = DateTime.Now;
        [DataMember(Name = "updated", Order = 8)] public DateTime Updated = DateTime.Now;
        /// <summary>1.8.0: ★お気に入り (一覧の上に出す)</summary>
        [DataMember(Name = "favorite", Order = 9, EmitDefaultValue = false)] public bool Favorite;
        /// <summary>1.8.0: パスワードを最後に変えた日時 (1.7 までのデータには無い → 作成日時を使う)</summary>
        [DataMember(Name = "passwordChanged", Order = 10, EmitDefaultValue = false)] public DateTime? PasswordChanged;
        /// <summary>1.8.0: 以前のパスワード (新しい順に最大 PassTools.HistoryMax 件。vault.dat ごと暗号化される)</summary>
        [DataMember(Name = "history", Order = 11, EmitDefaultValue = false)] public List<PasswordHistoryItem> History;

        /// <summary>パスワードを最後に変えた日時 (記録がなければ作成日時)</summary>
        [IgnoreDataMember] public DateTime PasswordDate { get { return PasswordChanged ?? Created; } }
        [IgnoreDataMember] public int HistoryCount { get { return History == null ? 0 : History.Count; } }

        public VaultEntry Clone()
        {
            var c = (VaultEntry)MemberwiseClone();
            if (History != null) c.History = History.Select(h => new PasswordHistoryItem { Password = h.Password, Changed = h.Changed }).ToList();
            return c;
        }
        public bool Matches(string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            var c = StringComparison.CurrentCultureIgnoreCase;
            return (Site ?? "").IndexOf(q, c) >= 0 || (Url ?? "").IndexOf(q, c) >= 0
                || (UserId ?? "").IndexOf(q, c) >= 0 || (Memo ?? "").IndexOf(q, c) >= 0;
        }
    }

    [DataContract]
    public class PasswordHistoryItem
    {
        [DataMember(Name = "password", Order = 1)] public string Password = "";
        /// <summary>このパスワードをやめた (新しいものに変えた) 日時</summary>
        [DataMember(Name = "changed", Order = 2)] public DateTime Changed = DateTime.Now;
    }

    [DataContract]
    public class VaultFile
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "entries", Order = 2)] public List<VaultEntry> Entries = new List<VaultEntry>();
    }

    /// <summary>
    /// 中身は JSON を DPAPI (ProtectedData, CurrentUser) で暗号化して保存する。
    /// 同じ Windows ユーザーでログインしているときだけ復号できる。平文はディスクに書かない。
    /// </summary>
    public static class VaultStore
    {
        public const string FileName = "vault.dat";
        public const string BackupName = "vault.bak";
        // 他のアプリが同じ DPAPI で暗号化したデータと取り違えないための追加エントロピー
        // 互換性のため変更しない (1.0 で保存した vault.dat を復号するのに必要)
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AppSuite.PasswordMaker.vault.v1");
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PMV1");

        public static byte[] Protect(byte[] plain)
        {
            var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var all = new byte[Magic.Length + enc.Length];
            Buffer.BlockCopy(Magic, 0, all, 0, Magic.Length);
            Buffer.BlockCopy(enc, 0, all, Magic.Length, enc.Length);
            return all;
        }

        public static byte[] Unprotect(byte[] data)
        {
            if (data.Length < Magic.Length || !data.Take(Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("Teal Pass (PasswordMaker) の保存ファイルではありません");
            var enc = new byte[data.Length - Magic.Length];
            Buffer.BlockCopy(data, Magic.Length, enc, 0, enc.Length);
            return ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
        }

        public static bool Exists { get { return File.Exists(DataStore.PathOf(FileName)); } }

        /// <summary>読み込み。ファイルが無ければ空。復号できなければ例外 (ファイルは消さない)。</summary>
        public static VaultFile Load()
        {
            var bytes = DataStore.ReadBytes(FileName);
            if (bytes == null) return new VaultFile();
            var plain = Unprotect(bytes);
            try
            {
                var v = DataStore.FromJson<VaultFile>(plain) ?? new VaultFile();
                if (v.Entries == null) v.Entries = new List<VaultEntry>();
                return v;
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public static void Save(VaultFile v)
        {
            var plain = DataStore.ToJson(v);
            try
            {
                var data = Protect(plain);
                // 直前の版を 1 つだけ残す (どちらも暗号化済み)
                var cur = DataStore.PathOf(FileName);
                if (File.Exists(cur)) { try { File.Copy(cur, DataStore.PathOf(BackupName), true); } catch { } }
                DataStore.WriteBytes(FileName, data);
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        /// <summary>復号できないファイルを別名に退避する (削除はしない)。退避先を返す。</summary>
        public static string SetAsideBroken()
        {
            var cur = DataStore.PathOf(FileName);
            if (!File.Exists(cur)) return null;
            var dst = DataStore.PathOf("vault.unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dat");
            File.Move(cur, dst);
            return dst;
        }
    }

    [DataContract]
    public class PmSettings
    {
        [DataMember(Name = "generator", Order = 1)] public PasswordOptions Generator = new PasswordOptions();
        [DataMember(Name = "clipboardClearSeconds", Order = 2)] public int ClipboardClearSeconds = 30;
        /// <summary>1.10.0: サイト名を Teal Hub の検索に出す (サイト名だけの索引 site-index.dat を作る)</summary>
        [DataMember(Name = "searchIndex", Order = 3)] public bool SiteIndex = true;
        [OnDeserializing] void Defaults(StreamingContext c) { ClipboardClearSeconds = 30; SiteIndex = true; }
    }

    /// <summary>
    /// 1.10.0: Teal Hub の検索のための「サイト名だけ」の索引 (site-index.dat)。
    ///  - 入れるのはサイト名だけ。ID・パスワード・メモ・URL・以前のパスワードは入れない
    ///  - vault.dat とは別の追加エントロピーと印で、同じく DPAPI (CurrentUser) で暗号化する
    ///    (vault.dat の暗号化の設定は変えない。索引から vault.dat は復号できない)
    ///  - 保存・読み込みのたびに作り直す。設定「サイト名を検索に出す」をオフにすると消す
    ///  - Teal Hub の HubSearch.cs が同じ印・エントロピーで読む (変えるときは両方を同時に)
    /// </summary>
    public static class SiteIndex
    {
        public const string FileName = "site-index.dat";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TealSuite.TealPass.siteindex.v1");
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PSI1");

        [DataContract]
        public class IndexFile
        {
            [DataMember(Name = "version", Order = 1)] public int Version = 1;
            [DataMember(Name = "sites", Order = 2)] public List<string> Sites = new List<string>();
        }

        /// <summary>索引に入れるサイト名 (空でないもの。同じ名前は 1 つ。名前順)</summary>
        public static List<string> SitesOf(VaultFile v)
        {
            if (v == null || v.Entries == null) return new List<string>();
            return v.Entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Site)).Select(e => e.Site.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        }

        public static byte[] Protect(List<string> sites)
        {
            var plain = DataStore.ToJson(new IndexFile { Sites = sites ?? new List<string>() });
            try
            {
                var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                var all = new byte[Magic.Length + enc.Length];
                Buffer.BlockCopy(Magic, 0, all, 0, Magic.Length);
                Buffer.BlockCopy(enc, 0, all, Magic.Length, enc.Length);
                return all;
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        /// <summary>索引を作り直す (on = false なら消す)。失敗してもアプリは止めない</summary>
        public static void Update(VaultFile v, bool on)
        {
            try
            {
                if (!on) { Delete(); return; }
                DataStore.WriteBytes(FileName, Protect(SitesOf(v)));
            }
            catch (Exception ex) { DataStore.Log("site index: " + ex.Message); }
        }

        public static void Delete()
        {
            try { var p = DataStore.PathOf(FileName); if (File.Exists(p)) File.Delete(p); } catch (Exception ex) { DataStore.Log("site index delete: " + ex.Message); }
        }
    }
}
