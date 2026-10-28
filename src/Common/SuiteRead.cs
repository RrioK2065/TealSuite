// TealSuite 共通 (1.10.0): ほかのアプリのデータを「読むだけ」で使う (Teal Plan のカレンダー・Teal Hub の「今日」と検索)
//  - 書き込みはしない (書き換えはそのアプリが自分でする。開くときは OpenRequest で頼む)
//  - アプリが入っていない・ファイルがない・壊れている・書き込み中で読めないときは「何もない」として扱い、例外もダイアログも出さない
//  - データの形は各アプリの JSON の一部だけ (知らない項目は読み飛ばす)。アプリの側の型には依存しない
//  - Teal Pass と Teal Clip のデータは読まない (Teal Pass はサイト名だけの索引を Teal Hub が別に読む。Teal Clip は使わない)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace TealSuite.Common
{
    // ---------------------------------------------------------------- Teal Wallet (subs.json / shopping.json)

    [DataContract]
    public class SharedSub
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "amount")] public long Amount;
        [DataMember(Name = "cycle")] public string CycleText;
        [DataMember(Name = "cycleMonths")] public int CycleMonths;
        [DataMember(Name = "start")] public string Start;
        [DataMember(Name = "category")] public string Category;
        [DataMember(Name = "payment")] public string Payment;
        [DataMember(Name = "memo")] public string Memo;
        [DataMember(Name = "status")] public string StatusText;
        [DataMember(Name = "trialEnd")] public string TrialEnd;

        public BillCycle Cycle { get { return Renewal.ParseCycle(CycleText); } }
        public bool Cancelled { get { return StatusText == "cancelled"; } }
        public bool Cancelling { get { return StatusText == "cancelling"; } }
        public DateTime? StartDate { get { return Fmt.ParseIso(Start); } }
        public DateTime? TrialEndDate { get { return Fmt.ParseIso(TrialEnd); } }
        public DateTime Anchor(DateTime today) { return Renewal.Anchor(StartDate, TrialEndDate, today); }
        public decimal Monthly { get { return Renewal.MonthlyCost(Amount, Cycle, CycleMonths); } }
    }

    [DataContract]
    public class SharedSubSettings
    {
        [DataMember(Name = "budget")] public long Budget;
    }

    [DataContract]
    public class SharedSubFile
    {
        [DataMember(Name = "subs")] public List<SharedSub> Subs;
        [DataMember(Name = "settings")] public SharedSubSettings Settings;
    }

    [DataContract]
    public class SharedShopItem
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "qty")] public int Qty;
        [DataMember(Name = "unit")] public string Unit;
        [DataMember(Name = "price")] public long Price;
        [DataMember(Name = "category")] public string Category;
        [DataMember(Name = "checked")] public bool Checked;
        [DataMember(Name = "memo")] public string Memo;
    }

    [DataContract]
    public class SharedShopList
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "items")] public List<SharedShopItem> Items;
    }

    [DataContract]
    public class SharedWant
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "target")] public long Target;
        [DataMember(Name = "price")] public long Price;
        [DataMember(Name = "store")] public string Store;
        [DataMember(Name = "memo")] public string Memo;
        [DataMember(Name = "boughtOn")] public string BoughtOn;
        public bool Bought { get { return !string.IsNullOrEmpty(BoughtOn); } }
    }

    [DataContract]
    public class SharedPurchase
    {
        [DataMember(Name = "date")] public string Date;
        [DataMember(Name = "total")] public long Total;
    }

    [DataContract]
    public class SharedShopSettings
    {
        [DataMember(Name = "budget")] public long Budget;
    }

    [DataContract]
    public class SharedShopFile
    {
        [DataMember(Name = "lists")] public List<SharedShopList> Lists;
        [DataMember(Name = "wants")] public List<SharedWant> Wants;
        [DataMember(Name = "history")] public List<SharedPurchase> History;
        [DataMember(Name = "settings")] public SharedShopSettings Settings;
    }

    /// <summary>Teal Wallet のデータ (読めなかったところは空)</summary>
    public class WalletView
    {
        public bool Found;
        public List<SharedSub> Subs = new List<SharedSub>();
        public long SubBudget;
        public List<SharedShopList> Lists = new List<SharedShopList>();
        public List<SharedWant> Wants = new List<SharedWant>();
        public List<SharedPurchase> History = new List<SharedPurchase>();
        public long ShopBudget;
    }

    /// <summary>支払い・無料期間の終わりの 1 件</summary>
    public class PayDue
    {
        public SharedSub Sub;
        public DateTime Day;
        /// <summary>true = 無料期間の終わり (この日から有料)</summary>
        public bool TrialEnd;
        public long Amount { get { return Sub.Amount; } }
        /// <summary>「Netflix 1,490円」/「Netflix 無料期間おわり」</summary>
        public string Title { get { return TrialEnd ? Sub.Name + " 無料期間おわり" : Sub.Name + " " + SuiteText.Yen(Sub.Amount); } }
        public string Badge { get { return TrialEnd ? "⚠" : "💳"; } }
    }

    public static class WalletCalc
    {
        /// <summary>
        /// [from, to) の支払い日と無料期間の終わり (解約済みは出さない・解約予定は次の 1 回まで)。
        /// 無料期間の終わりの日は最初の支払い日でもあるので、その日は「無料期間おわり」の 1 件だけにする
        /// </summary>
        public static List<PayDue> Dues(IEnumerable<SharedSub> subs, DateTime today, DateTime from, DateTime to)
        {
            var list = new List<PayDue>();
            if (subs == null) return list;
            foreach (var s in subs)
            {
                if (s == null || s.Cancelled || string.IsNullOrEmpty(s.Name)) continue;
                try
                {
                    var anchor = s.Anchor(today);
                    var trial = s.TrialEndDate;
                    bool hasTrial = trial.HasValue && trial.Value.Date > (s.StartDate ?? DateTime.MinValue).Date;
                    foreach (var d in Renewal.Payments(s.Cycle, s.CycleMonths, anchor, today, s.Cancelling, from, to))
                        list.Add(new PayDue { Sub = s, Day = d, TrialEnd = hasTrial && d == trial.Value.Date });
                    // 解約予定で、無料期間の終わりが範囲にあるのに支払いとして出なかったとき (次の回より後) は出さない
                }
                catch { }
            }
            return list.OrderBy(x => x.Day).ThenBy(x => x.TrialEnd ? 0 : 1).ThenBy(x => x.Sub.Name, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>今日から days 日のうちの支払いと無料期間の終わり</summary>
        public static List<PayDue> Upcoming(IEnumerable<SharedSub> subs, DateTime today, int days = 7)
        {
            return Dues(subs, today.Date, today.Date, today.Date.AddDays(days));
        }

        /// <summary>サブスクの予算の残り (月額の合計を引いた額。予算なしなら null)</summary>
        public static decimal? SubBudgetLeft(WalletView w)
        {
            if (w == null || w.SubBudget <= 0) return null;
            return w.SubBudget - Math.Round(w.Subs.Where(s => s != null && !s.Cancelled).Sum(s => s.Monthly), 0);
        }

        /// <summary>今月の買い物の予算の残り (予算なしなら null)</summary>
        public static long? ShopBudgetLeft(WalletView w, DateTime month)
        {
            if (w == null || w.ShopBudget <= 0) return null;
            long spent = w.History.Where(h => h != null).Sum(h => { var d = Fmt.ParseIso(h.Date); return d.HasValue && d.Value.Year == month.Year && d.Value.Month == month.Month ? h.Total : 0; });
            return w.ShopBudget - spent;
        }
    }

    // ---------------------------------------------------------------- Teal Plan (tasks.json)

    [DataContract]
    public class SharedSubTask
    {
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "done")] public bool Done;
    }

    [DataContract]
    public class SharedTask
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "due")] public string Due;
        [DataMember(Name = "priority")] public int Priority;
        [DataMember(Name = "tags")] public List<string> Tags;
        [DataMember(Name = "memo")] public string Memo;
        [DataMember(Name = "done")] public bool Done;
        [DataMember(Name = "subtasks")] public List<SharedSubTask> Subtasks;
        public DateTime? DueAt { get { return Fmt.ParseIso(Due); } }
        public bool HasTime { get { return Due != null && Due.Length > 10 && DueAt.HasValue; } }
    }

    [DataContract]
    public class SharedEvent
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "start")] public string Start;
        [DataMember(Name = "end")] public string End;
        [DataMember(Name = "allDay")] public bool AllDay;
        [DataMember(Name = "location")] public string Location;
        [DataMember(Name = "memo")] public string Memo;
        [DataMember(Name = "repeat")] public string RepeatText;
        [DataMember(Name = "until")] public string Until;
        [DataMember(Name = "tags")] public List<string> Tags;
        [DataMember(Name = "skip")] public List<string> Skip;

        public DateTime StartAt { get { return Fmt.ParseIso(Start) ?? DateTime.MinValue; } }
        /// <summary>終わり (含まない)。Teal Plan の EventItem.EndAt と同じ決め方</summary>
        public DateTime EndAt
        {
            get
            {
                var s = StartAt;
                var e = Fmt.ParseIso(End);
                if (AllDay) { var last = e.HasValue ? e.Value.Date : s.Date; if (last < s.Date) last = s.Date; return last.AddDays(1); }
                if (!e.HasValue || e.Value <= s) return s.AddHours(1);
                return e.Value;
            }
        }
        public List<DateTime> Occurrences(DateTime from, DateTime to)
        {
            var skip = Skip;
            return Renewal.Occurrences(StartAt, EndAt - StartAt, Renewal.ParseRepeat(RepeatText), Fmt.ParseIso(Until), d => skip != null && skip.Contains(Fmt.Iso(d)), from, to);
        }
    }

    [DataContract]
    public class SharedPlanFile
    {
        [DataMember(Name = "tasks")] public List<SharedTask> Tasks;
        [DataMember(Name = "events")] public List<SharedEvent> Events;
    }

    public class PlanView
    {
        public bool Found;
        public List<SharedTask> Tasks = new List<SharedTask>();
        public List<SharedEvent> Events = new List<SharedEvent>();
    }

    /// <summary>今日の予定の 1 回</summary>
    public class EventDue
    {
        public SharedEvent Event;
        public DateTime Start, End;
        public bool AllDay { get { return Event.AllDay; } }
    }

    public static class PlanCalc
    {
        /// <summary>今日やるタスク (期限が今日まで・未完了。期限切れが先、そのあと時刻の順)</summary>
        public static List<SharedTask> TodayTasks(PlanView p, DateTime now)
        {
            var end = now.Date.AddDays(1);
            return p.Tasks.Where(t => t != null && !t.Done && t.DueAt.HasValue && t.DueAt.Value < end)
                .OrderBy(t => t.DueAt.Value.Date < now.Date ? 0 : 1).ThenBy(t => t.HasTime ? 0 : 1).ThenBy(t => t.DueAt.Value)
                .ThenByDescending(t => t.Priority).ThenBy(t => t.Title, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>今日 (day) に重なる予定の回 (終日を先、そのあと始まりの順)</summary>
        public static List<EventDue> EventsOn(PlanView p, DateTime day)
        {
            var from = day.Date; var to = from.AddDays(1);
            var list = new List<EventDue>();
            foreach (var e in p.Events)
            {
                if (e == null || e.StartAt == DateTime.MinValue) continue;
                try
                {
                    var len = e.EndAt - e.StartAt;
                    foreach (var o in e.Occurrences(from, to)) list.Add(new EventDue { Event = e, Start = o, End = o + len });
                }
                catch { }
            }
            return list.OrderBy(x => x.AllDay ? 0 : 1).ThenBy(x => x.Start).ThenBy(x => x.Event.Title, StringComparer.CurrentCulture).ToList();
        }
    }

    // ---------------------------------------------------------------- Teal Memo (notes.json)

    [DataContract]
    public class SharedNote
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "updated")] public string Updated;
        [DataMember(Name = "pinned")] public bool Pinned;
        [DataMember(Name = "deleted")] public string Deleted;
        public bool InTrash { get { return !string.IsNullOrEmpty(Deleted); } }
        /// <summary>最初の空でない行 (Teal Memo の一覧と同じ)</summary>
        public string Title
        {
            get
            {
                foreach (var line in (Text ?? "").Split('\n'))
                {
                    var s = line.Trim();
                    if (s.Length > 0) return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
                }
                return "（無題）";
            }
        }
        public List<string> Tags { get { return SuiteTags.Parse(Text); } }
        public DateTime UpdatedUtc
        {
            get { DateTime d; return DateTime.TryParse(Updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? d.ToUniversalTime() : DateTime.MinValue; }
        }
    }

    [DataContract]
    public class SharedNoteFile
    {
        [DataMember(Name = "notes")] public List<SharedNote> Notes;
    }

    public class MemoView
    {
        public bool Found;
        public List<SharedNote> Notes = new List<SharedNote>();
        /// <summary>ゴミ箱以外のピン留めのメモ (新しい順)</summary>
        public List<SharedNote> Pinned { get { return Notes.Where(n => n.Pinned && !n.InTrash).OrderByDescending(n => n.UpdatedUtc).ToList(); } }
        public IEnumerable<SharedNote> Live { get { return Notes.Where(n => !n.InTrash); } }
    }

    // ---------------------------------------------------------------- 読み込み

    public static class SuiteRead
    {
        public const string WalletId = "TealWallet", PlanId = "TealPlan", MemoId = "TealMemo", PassId = "TealPass";

        /// <summary>データのファイル (新しい名前のフォルダになければ、改名前の名前のフォルダ)</summary>
        public static string FileOf(string appId, string fileName)
        {
            var p = Path.Combine(DataStore.DirFor(appId), fileName);
            if (File.Exists(p)) return p;
            string old = appId == WalletId ? "TealSub" : appId == PlanId ? "TealTasks" : null;
            if (old != null)
            {
                var q = Path.Combine(DataStore.DirFor(old), fileName);
                if (File.Exists(q) && !Directory.Exists(DataStore.DirFor(appId))) return q;
            }
            return p;
        }
        public static string WalletSubsPath { get { return FileOf(WalletId, "subs.json"); } }
        public static string WalletShopPath { get { return FileOf(WalletId, "shopping.json"); } }
        public static string PlanPath { get { return FileOf(PlanId, "tasks.json"); } }
        public static string MemoPath { get { return FileOf(MemoId, "notes.json"); } }

        /// <summary>最後に読めなかったファイルと理由 (画面には出さない。テスト・ログ用)</summary>
        public static string LastError;

        class Cached { public DateTime Time; public long Length; public object Value; }
        static readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);

        /// <summary>JSON を読む (なければ・壊れていれば null)。変わっていなければ前に読んだものを返す</summary>
        public static T Read<T>(string path) where T : class
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                lock (cache)
                {
                    Cached c;
                    if (cache.TryGetValue(path, out c) && c.Time == fi.LastWriteTimeUtc && c.Length == fi.Length && c.Value is T) return (T)c.Value;
                }
                byte[] bytes = null;
                for (int i = 0; i < 3 && bytes == null; i++)
                {
                    try { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { bytes = new byte[fs.Length]; int n = 0; while (n < bytes.Length) { int r = fs.Read(bytes, n, bytes.Length - n); if (r <= 0) break; n += r; } } }
                    catch (IOException) { if (i == 2) throw; System.Threading.Thread.Sleep(40); }
                }
                var v = DataStore.FromJson<T>(bytes);
                lock (cache) cache[path] = new Cached { Time = fi.LastWriteTimeUtc, Length = fi.Length, Value = v };
                return v;
            }
            catch (Exception ex) { LastError = Path.GetFileName(path) + ": " + ex.Message; return null; }
        }

        public static WalletView Wallet()
        {
            var w = new WalletView();
            var subs = Read<SharedSubFile>(WalletSubsPath);
            if (subs != null)
            {
                w.Found = true;
                if (subs.Subs != null) w.Subs = subs.Subs.Where(s => s != null).ToList();
                if (subs.Settings != null) w.SubBudget = Math.Max(0, subs.Settings.Budget);
            }
            var shop = Read<SharedShopFile>(WalletShopPath);
            if (shop != null)
            {
                w.Found = true;
                if (shop.Lists != null) w.Lists = shop.Lists.Where(l => l != null).ToList();
                foreach (var l in w.Lists) if (l.Items == null) l.Items = new List<SharedShopItem>(); else l.Items.RemoveAll(i => i == null);
                if (shop.Wants != null) w.Wants = shop.Wants.Where(x => x != null).ToList();
                if (shop.History != null) w.History = shop.History.Where(x => x != null).ToList();
                if (shop.Settings != null) w.ShopBudget = Math.Max(0, shop.Settings.Budget);
            }
            return w;
        }

        public static PlanView Plan()
        {
            var p = new PlanView();
            var f = Read<SharedPlanFile>(PlanPath);
            if (f == null) return p;
            p.Found = true;
            if (f.Tasks != null) p.Tasks = f.Tasks.Where(t => t != null && t.Title != null).ToList();
            if (f.Events != null) p.Events = f.Events.Where(e => e != null && e.Title != null && Fmt.ParseIso(e.Start).HasValue).ToList();
            return p;
        }

        public static MemoView Memo()
        {
            var m = new MemoView();
            var f = Read<SharedNoteFile>(MemoPath);
            if (f == null) return m;
            m.Found = true;
            if (f.Notes != null) m.Notes = f.Notes.Where(n => n != null && !string.IsNullOrEmpty(n.Id)).ToList();
            return m;
        }
    }

    public static class SuiteText
    {
        /// <summary>1490 → 「1,490円」</summary>
        public static string Yen(long v) { return v.ToString("#,0", CultureInfo.InvariantCulture) + "円"; }
        public static string Yen(decimal v) { return Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture) + "円"; }
    }
}
