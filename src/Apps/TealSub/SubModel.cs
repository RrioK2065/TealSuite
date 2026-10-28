// Teal Sub (サブスク整理) - データの形と保存 (%APPDATA%\TealSuite\TealSub\subs.json)
// カード番号などの秘密情報は扱わない (支払い方法は「楽天カード」「PayPay」のような名前だけ)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Sub
{
    public enum Cycle { Monthly, Yearly, Weekly, EveryNMonths }
    public enum Usage { Daily, Weekly, Monthly, Rarely }
    public enum SubStatus { Active, Cancelling, Cancelled }

    [DataContract]
    public class Subscription
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "name", Order = 2)] public string Name = "";
        /// <summary>1 回の支払い額 (円)</summary>
        [DataMember(Name = "amount", Order = 3)] public long Amount;
        /// <summary>monthly / yearly / weekly / months</summary>
        [DataMember(Name = "cycle", Order = 4)] public string CycleText = "monthly";
        /// <summary>cycle = months のときの月数 (例: 3 = 3か月ごと)</summary>
        [DataMember(Name = "cycleMonths", Order = 5, EmitDefaultValue = false)] public int CycleMonths;
        /// <summary>契約を始めた日 (yyyy-MM-dd)。更新日はここから計算する</summary>
        [DataMember(Name = "start", Order = 6)] public string Start;
        [DataMember(Name = "category", Order = 7)] public string Category = "その他";
        [DataMember(Name = "payment", Order = 8, EmitDefaultValue = false)] public string Payment;
        /// <summary>daily / weekly / monthly / rarely</summary>
        [DataMember(Name = "usage", Order = 9)] public string UsageText = "weekly";
        [DataMember(Name = "lastUsed", Order = 10, EmitDefaultValue = false)] public string LastUsed;
        [DataMember(Name = "memo", Order = 11, EmitDefaultValue = false)] public string Memo;
        [DataMember(Name = "cancelUrl", Order = 12, EmitDefaultValue = false)] public string CancelUrl;
        /// <summary>active / cancelling / cancelled</summary>
        [DataMember(Name = "status", Order = 13)] public string StatusText = "active";
        /// <summary>無料期間の終わる日 (この日から有料。yyyy-MM-dd)</summary>
        [DataMember(Name = "trialEnd", Order = 14, EmitDefaultValue = false)] public string TrialEnd;
        [DataMember(Name = "cancelledOn", Order = 15, EmitDefaultValue = false)] public string CancelledOn;
        [DataMember(Name = "created", Order = 16, EmitDefaultValue = false)] public string Created;
        /// <summary>1.8.0: 料金の変更履歴 (古い順)</summary>
        [DataMember(Name = "priceHistory", Order = 17, EmitDefaultValue = false)] public List<PriceChange> PriceHistory;

        [IgnoreDataMember]
        public Cycle Cycle
        {
            get
            {
                switch (CycleText) { case "yearly": return Cycle.Yearly; case "weekly": return Cycle.Weekly; case "months": return Cycle.EveryNMonths; default: return Cycle.Monthly; }
            }
            set
            {
                CycleText = value == Cycle.Yearly ? "yearly" : value == Cycle.Weekly ? "weekly" : value == Cycle.EveryNMonths ? "months" : "monthly";
                if (value != Cycle.EveryNMonths) CycleMonths = 0;
            }
        }
        [IgnoreDataMember]
        public Usage Usage
        {
            get { switch (UsageText) { case "daily": return Usage.Daily; case "monthly": return Usage.Monthly; case "rarely": return Usage.Rarely; default: return Usage.Weekly; } }
            set { UsageText = value == Usage.Daily ? "daily" : value == Usage.Monthly ? "monthly" : value == Usage.Rarely ? "rarely" : "weekly"; }
        }
        [IgnoreDataMember]
        public SubStatus Status
        {
            get { switch (StatusText) { case "cancelling": return SubStatus.Cancelling; case "cancelled": return SubStatus.Cancelled; default: return SubStatus.Active; } }
            set { StatusText = value == SubStatus.Cancelling ? "cancelling" : value == SubStatus.Cancelled ? "cancelled" : "active"; }
        }
        [IgnoreDataMember] public DateTime? StartDate { get { return Fmt.ParseIso(Start); } set { Start = value.HasValue ? Fmt.Iso(value.Value) : null; } }
        [IgnoreDataMember] public DateTime? TrialEndDate { get { return Fmt.ParseIso(TrialEnd); } set { TrialEnd = value.HasValue ? Fmt.Iso(value.Value) : null; } }
        [IgnoreDataMember] public DateTime? LastUsedDate { get { return Fmt.ParseIso(LastUsed); } set { LastUsed = value.HasValue ? Fmt.Iso(value.Value) : null; } }
        [IgnoreDataMember] public DateTime? CancelledDate { get { return Fmt.ParseIso(CancelledOn); } set { CancelledOn = value.HasValue ? Fmt.Iso(value.Value) : null; } }
        /// <summary>まだお金がかかっている (契約中か解約予定)</summary>
        [IgnoreDataMember] public bool Paying { get { return Status != SubStatus.Cancelled; } }

        public Subscription Clone() { return DataStore.FromJson<Subscription>(DataStore.ToJson(this)); }
    }

    [DataContract]
    public class SubSettings
    {
        [DataMember(Name = "remind", Order = 1)] public bool Remind = true;
        /// <summary>更新日・無料期間の終わりの何日前にお知らせするか</summary>
        [DataMember(Name = "remindDays", Order = 2)] public int RemindDays = 3;
        /// <summary>1.8.0: 1 か月の予算 (円)。0 = 使わない</summary>
        [DataMember(Name = "budget", Order = 3, EmitDefaultValue = false)] public long MonthlyBudget;
        [OnDeserializing] void Defaults(StreamingContext c) { Remind = true; RemindDays = 3; MonthlyBudget = 0; }
    }

    [DataContract]
    public class SubData
    {
        public const string FileName = "subs.json";
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "subs", Order = 2)] public List<Subscription> Subs = new List<Subscription>();
        [DataMember(Name = "settings", Order = 3)] public SubSettings Settings = new SubSettings();
        /// <summary>お知らせ済みの印 ("id|renew|2026-11-01" など)。同じお知らせを何度も出さない</summary>
        [DataMember(Name = "notified", Order = 4)] public List<string> Notified = new List<string>();

        public void Normalize()
        {
            if (Subs == null) Subs = new List<Subscription>();
            Subs.RemoveAll(s => s == null);
            if (Settings == null) Settings = new SubSettings();
            if (Notified == null) Notified = new List<string>();
            Settings.RemindDays = Math.Max(0, Math.Min(30, Settings.RemindDays));
            Settings.MonthlyBudget = Math.Max(0, Math.Min(10000000, Settings.MonthlyBudget));
            foreach (var s in Subs)
            {
                if (string.IsNullOrEmpty(s.Id)) s.Id = Guid.NewGuid().ToString("N");
                if (s.Name == null) s.Name = "";
                if (string.IsNullOrEmpty(s.Category)) s.Category = "その他";
                if (s.Amount < 0) s.Amount = 0;
                if (s.Cycle == Cycle.EveryNMonths && s.CycleMonths < 1) s.CycleMonths = 1;
                if (!s.StartDate.HasValue) s.StartDate = DateTime.Today;
                if (s.PriceHistory != null) { s.PriceHistory.RemoveAll(p => p == null); if (s.PriceHistory.Count == 0) s.PriceHistory = null; }
            }
        }
    }
}
