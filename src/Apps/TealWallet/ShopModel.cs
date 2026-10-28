// Teal Wallet (サブスク＆買い物リスト) - 1.9.0: 買い物リストと欲しいもののデータ (%APPDATA%\TealSuite\TealWallet\shopping.json)
// サブスクの subs.json とは別のファイルにする (1.8.0 の Teal Sub のデータ・バックアップはそのまま読める)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Wallet
{
    /// <summary>買い物リストの 1 品</summary>
    [DataContract]
    public class ShopItem
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "name", Order = 2)] public string Name = "";
        [DataMember(Name = "qty", Order = 3)] public int Qty = 1;
        /// <summary>個・本・パックなど (なくてもよい)</summary>
        [DataMember(Name = "unit", Order = 4, EmitDefaultValue = false)] public string Unit;
        /// <summary>1 つあたりの予想の値段 (円)。0 = わからない</summary>
        [DataMember(Name = "price", Order = 5)] public long Price;
        /// <summary>売り場 (野菜・果物 など)</summary>
        [DataMember(Name = "category", Order = 6)] public string Category = "その他";
        [DataMember(Name = "checked", Order = 7)] public bool Checked;
        /// <summary>実際に払った金額 (この品の合計。円)。null = 予想と同じとみなす</summary>
        [DataMember(Name = "actual", Order = 8, EmitDefaultValue = false)] public long? Actual;
        [DataMember(Name = "memo", Order = 9, EmitDefaultValue = false)] public string Memo;
        [DataMember(Name = "added", Order = 10, EmitDefaultValue = false)] public string Added;
        /// <summary>チェックした順 (カゴの中は入れた順に並べる)</summary>
        [DataMember(Name = "checkedAt", Order = 11, EmitDefaultValue = false)] public long CheckedAt;

        /// <summary>予想の合計 (値段 × 数)</summary>
        [IgnoreDataMember] public long Estimate { get { return Price * Math.Max(1, Qty); } }
        /// <summary>実際の金額 (入れていなければ予想)</summary>
        [IgnoreDataMember] public long Paid { get { return Actual ?? Estimate; } }
        public ShopItem Clone() { return DataStore.FromJson<ShopItem>(DataStore.ToJson(this)); }
    }

    [DataContract]
    public class ShopList
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "name", Order = 2)] public string Name = "";
        [DataMember(Name = "items", Order = 3)] public List<ShopItem> Items = new List<ShopItem>();
    }

    /// <summary>買い物を終えたときの記録 (よく買うものの候補と、月の支出に使う)</summary>
    [DataContract]
    public class Purchase
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "date", Order = 2)] public string Date;
        /// <summary>リストの名前 (欲しいものを買ったときは「欲しいもの」)</summary>
        [DataMember(Name = "list", Order = 3)] public string List = "";
        /// <summary>予想の合計</summary>
        [DataMember(Name = "estimate", Order = 4)] public long Estimate;
        /// <summary>実際に払った合計 (レシートの金額)</summary>
        [DataMember(Name = "total", Order = 5)] public long Total;
        [DataMember(Name = "lines", Order = 6)] public List<PurchaseLine> Lines = new List<PurchaseLine>();
        [IgnoreDataMember] public DateTime? Day { get { return Fmt.ParseIso(Date); } }
    }

    [DataContract]
    public class PurchaseLine
    {
        [DataMember(Name = "name", Order = 1)] public string Name = "";
        [DataMember(Name = "qty", Order = 2)] public int Qty = 1;
        [DataMember(Name = "unit", Order = 3, EmitDefaultValue = false)] public string Unit;
        /// <summary>1 つあたりの値段 (予想か実際)</summary>
        [DataMember(Name = "price", Order = 4)] public long Price;
        [DataMember(Name = "category", Order = 5)] public string Category = "その他";
    }

    public enum WantPriority { High, Normal, Low }

    /// <summary>欲しいもの (すぐには買わない。目標の値段まで下がったら買う)</summary>
    [DataContract]
    public class WantItem
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "name", Order = 2)] public string Name = "";
        /// <summary>この値段以下なら買う (円)。0 = 決めていない</summary>
        [DataMember(Name = "target", Order = 3)] public long Target;
        /// <summary>いま見かける値段 (円)。0 = わからない</summary>
        [DataMember(Name = "price", Order = 4)] public long Price;
        /// <summary>high / normal / low</summary>
        [DataMember(Name = "priority", Order = 5)] public string PriorityText = "normal";
        [DataMember(Name = "store", Order = 6, EmitDefaultValue = false)] public string Store;
        [DataMember(Name = "url", Order = 7, EmitDefaultValue = false)] public string Url;
        [DataMember(Name = "memo", Order = 8, EmitDefaultValue = false)] public string Memo;
        [DataMember(Name = "added", Order = 9, EmitDefaultValue = false)] public string Added;
        /// <summary>買った日 (yyyy-MM-dd)。null = まだ</summary>
        [DataMember(Name = "boughtOn", Order = 10, EmitDefaultValue = false)] public string BoughtOn;
        [DataMember(Name = "paid", Order = 11, EmitDefaultValue = false)] public long Paid;

        [IgnoreDataMember]
        public WantPriority Priority
        {
            get { switch (PriorityText) { case "high": return WantPriority.High; case "low": return WantPriority.Low; default: return WantPriority.Normal; } }
            set { PriorityText = value == WantPriority.High ? "high" : value == WantPriority.Low ? "low" : "normal"; }
        }
        [IgnoreDataMember] public bool Bought { get { return !string.IsNullOrEmpty(BoughtOn); } }
        /// <summary>いまの値段が目標以下 (どちらも入っているとき)</summary>
        [IgnoreDataMember] public bool AtTarget { get { return Target > 0 && Price > 0 && Price <= Target; } }
        public WantItem Clone() { return DataStore.FromJson<WantItem>(DataStore.ToJson(this)); }
    }

    [DataContract]
    public class ShopSettings
    {
        /// <summary>1 か月の買い物の予算 (円)。0 = 使わない</summary>
        [DataMember(Name = "budget", Order = 1, EmitDefaultValue = false)] public long MonthlyBudget;
        /// <summary>前に開いていたリスト</summary>
        [DataMember(Name = "current", Order = 2, EmitDefaultValue = false)] public string CurrentList;
    }

    [DataContract]
    public class ShopData
    {
        public const string FileName = "shopping.json";
        public const int MaxHistory = 400;
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "lists", Order = 2)] public List<ShopList> Lists = new List<ShopList>();
        [DataMember(Name = "wants", Order = 3)] public List<WantItem> Wants = new List<WantItem>();
        [DataMember(Name = "history", Order = 4)] public List<Purchase> History = new List<Purchase>();
        [DataMember(Name = "settings", Order = 5)] public ShopSettings Settings = new ShopSettings();

        /// <summary>はじめて使うときのリスト</summary>
        public static ShopData CreateDefault()
        {
            var d = new ShopData();
            d.Lists.Add(new ShopList { Name = "スーパー" });
            d.Lists.Add(new ShopList { Name = "ドラッグストア" });
            return d;
        }

        public void Normalize()
        {
            if (Lists == null) Lists = new List<ShopList>();
            if (Wants == null) Wants = new List<WantItem>();
            if (History == null) History = new List<Purchase>();
            if (Settings == null) Settings = new ShopSettings();
            Lists.RemoveAll(l => l == null); Wants.RemoveAll(w => w == null); History.RemoveAll(h => h == null);
            if (Lists.Count == 0) Lists.Add(new ShopList { Name = "買い物リスト" });
            foreach (var l in Lists)
            {
                if (string.IsNullOrEmpty(l.Id)) l.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(l.Name)) l.Name = "リスト";
                if (l.Items == null) l.Items = new List<ShopItem>();
                l.Items.RemoveAll(i => i == null);
                foreach (var i in l.Items)
                {
                    if (string.IsNullOrEmpty(i.Id)) i.Id = Guid.NewGuid().ToString("N");
                    if (i.Name == null) i.Name = "";
                    i.Qty = Math.Max(1, Math.Min(999, i.Qty));
                    i.Price = Math.Max(0, Math.Min(10000000, i.Price));
                    if (i.Actual.HasValue && i.Actual.Value < 0) i.Actual = null;
                    if (string.IsNullOrEmpty(i.Category)) i.Category = "その他";
                }
            }
            foreach (var w in Wants)
            {
                if (string.IsNullOrEmpty(w.Id)) w.Id = Guid.NewGuid().ToString("N");
                if (w.Name == null) w.Name = "";
                w.Target = Math.Max(0, w.Target); w.Price = Math.Max(0, w.Price); w.Paid = Math.Max(0, w.Paid);
            }
            foreach (var h in History) { if (h.Lines == null) h.Lines = new List<PurchaseLine>(); h.Lines.RemoveAll(x => x == null); }
            Settings.MonthlyBudget = Math.Max(0, Math.Min(10000000, Settings.MonthlyBudget));
            if (History.Count > MaxHistory) History = History.OrderByDescending(h => h.Date, StringComparer.Ordinal).Take(MaxHistory).ToList();
        }

        public ShopList FindList(string id) { return Lists.FirstOrDefault(l => l.Id == id); }
    }
}
