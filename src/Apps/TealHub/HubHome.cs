// Teal Hub (アプリ管理) - 1.10.0: 「今日」の画面と、全アプリの検索
//  - 各アプリのデータ (JSON) を読むだけ。書き換えない。ファイルが無い・壊れているときは何も出さない (エラーにしない)
//  - Teal Pass は、Teal Pass が作る「サイト名だけ」の索引 (site-index.dat) だけを読む。vault.dat は開かない
//  - Teal Clip (クリップボードの履歴) は検索しない
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>「今日」・検索の 1 行 (Header = 見出しの行、Empty = 「ありません」の行)</summary>
    public class HomeItem
    {
        public string AppId, Kind, Key;
        public string Title = "", Detail = "", Badge = "";
        public Color DetailColor = Theme.SubText;
        public bool Header, Empty;
        public override string ToString() { return (Header ? "# " : "") + Title + (Detail.Length > 0 ? " | " + Detail : ""); }
        /// <summary>開ける行か (見出し・空の行・アプリを開くだけの行でない)</summary>
        public bool Openable { get { return !Header && !Empty && AppId != null; } }

        public static HomeItem Head(string title, string appId) { return new HomeItem { Header = true, Title = title, AppId = appId }; }
        public static HomeItem None(string text) { return new HomeItem { Empty = true, Title = text }; }
    }

    public static class HubToday
    {
        public const int PayDays = 7;
        public const string PlanName = "Teal Plan", WalletName = "Teal Wallet", MemoName = "Teal Memo", PassName = "Teal Pass";

        /// <summary>
        /// 「今日」の行。has(appId) が false のアプリ (apps.json に無い) は出さない。
        /// counts: [今日のタスク, 今日の予定, 7 日以内の支払い, ピン留め]
        /// </summary>
        public static List<HomeItem> Build(DateTime now, Func<string, bool> has, out int[] counts)
        {
            counts = new int[4];
            var rows = new List<HomeItem>();
            var today = now.Date;
            if (has == null) has = id => true;

            if (has(SuiteRead.PlanId))
            {
                var p = SafePlan();
                var tasks = PlanCalc.TodayTasks(p, now);
                counts[0] = tasks.Count;
                rows.Add(HomeItem.Head("今日のタスク" + (tasks.Count > 0 ? "  " + tasks.Count + " 件" : ""), SuiteRead.PlanId));
                foreach (var t in tasks.Take(30))
                {
                    var due = t.DueAt.Value;
                    bool over = t.HasTime ? due < now : due.Date < today;
                    string when = due.Date < today ? "期限切れ (" + Fmt.MonthDay(due) + (t.HasTime ? " " + Fmt.Time(due) : "") + ")" : t.HasTime ? Fmt.Time(due) + " まで" : "今日まで";
                    int subs = t.Subtasks == null ? 0 : t.Subtasks.Count, done = subs == 0 ? 0 : t.Subtasks.Count(x => x != null && x.Done);
                    rows.Add(new HomeItem
                    {
                        AppId = SuiteRead.PlanId, Kind = "task", Key = t.Id, Badge = t.Priority >= 2 ? "❗" : "☐",
                        Title = t.Title ?? "",
                        Detail = when + (subs > 0 ? " ・ 小タスク " + done + "/" + subs : "") + Tags(t.Tags),
                        DetailColor = over ? Theme.Danger : Theme.SubText
                    });
                }
                if (tasks.Count == 0) rows.Add(HomeItem.None(p.Found ? "今日が期限のタスクはありません" : "Teal Plan にタスクを入れると、ここに出ます"));
                if (tasks.Count > 30) rows.Add(HomeItem.None("ほか " + (tasks.Count - 30) + " 件 (Teal Plan で見られます)"));

                var evs = PlanCalc.EventsOn(p, today);
                counts[1] = evs.Count;
                rows.Add(HomeItem.Head("今日の予定" + (evs.Count > 0 ? "  " + evs.Count + " 件" : ""), SuiteRead.PlanId));
                foreach (var e in evs.Take(30))
                {
                    string when = e.AllDay ? "終日" : (e.Start.Date < today ? Fmt.MonthDay(e.Start) + " " : "") + Fmt.Time(e.Start) + "〜" + (e.End.Date > today ? Fmt.MonthDay(e.End) + " " : "") + Fmt.Time(e.End);
                    rows.Add(new HomeItem
                    {
                        AppId = SuiteRead.PlanId, Kind = "event", Key = e.Event.Id, Badge = "📅", Title = e.Event.Title ?? "",
                        Detail = when + (string.IsNullOrEmpty(e.Event.Location) ? "" : " ・ " + e.Event.Location) + Tags(e.Event.Tags),
                        DetailColor = !e.AllDay && e.End <= now ? Theme.Disabled : Theme.SubText
                    });
                }
                if (evs.Count == 0) rows.Add(HomeItem.None("今日の予定はありません"));
            }

            if (has(SuiteRead.WalletId))
            {
                var w = SafeWallet();
                var dues = WalletCalc.Upcoming(w.Subs, today, PayDays);
                counts[2] = dues.Count(d => !d.TrialEnd && d.Day == today);
                rows.Add(HomeItem.Head("お金 (これから " + PayDays + " 日の支払い)", SuiteRead.WalletId));
                foreach (var d in dues)
                {
                    rows.Add(new HomeItem
                    {
                        AppId = SuiteRead.WalletId, Kind = "sub", Key = d.Sub.Id, Badge = d.Badge, Title = d.Title,
                        Detail = DayText(d.Day, today) + (d.TrialEnd ? " ・ この日から " + SuiteText.Yen(d.Sub.Amount) + " かかります" : (string.IsNullOrEmpty(d.Sub.Payment) ? "" : " ・ " + d.Sub.Payment)) + (d.Sub.Cancelling ? " ・ 解約予定" : ""),
                        DetailColor = d.TrialEnd || d.Day == today ? Theme.Warning : Theme.SubText
                    });
                }
                if (dues.Count == 0) rows.Add(HomeItem.None(w.Found ? PayDays + " 日以内の支払いはありません" : "Teal Wallet にサブスクを入れると、支払い日がここに出ます"));
                var subLeft = WalletCalc.SubBudgetLeft(w);
                var shopLeft = WalletCalc.ShopBudgetLeft(w, today);
                if (subLeft.HasValue || shopLeft.HasValue)
                {
                    var parts = new List<string>();
                    if (subLeft.HasValue) parts.Add("サブスク " + Left(subLeft.Value));
                    if (shopLeft.HasValue) parts.Add("今月の買い物 " + Left(shopLeft.Value));
                    rows.Add(new HomeItem
                    {
                        AppId = SuiteRead.WalletId, Kind = "budget", Key = "", Badge = "💰", Title = "予算の残り",
                        Detail = string.Join(" ・ ", parts),
                        DetailColor = (subLeft ?? 0) < 0 || (shopLeft ?? 0) < 0 ? Theme.Danger : Theme.SubText
                    });
                }
            }

            if (has(SuiteRead.MemoId))
            {
                var m = SafeMemo();
                var pins = m.Pinned;
                counts[3] = pins.Count;
                rows.Add(HomeItem.Head("ピン留めのメモ", SuiteRead.MemoId));
                foreach (var n in pins.Take(20))
                    rows.Add(new HomeItem { AppId = SuiteRead.MemoId, Kind = "note", Key = n.Id, Badge = "📌", Title = n.Title, Detail = Snippet(n.Text, null) });
                if (pins.Count == 0) rows.Add(HomeItem.None("Teal Memo でピン留めしたメモがここに出ます"));
            }
            return rows;
        }

        /// <summary>通知領域に出す 1 行 (例: 「今日のタスク 3件 / 支払い 1件」)。Plan も Wallet も無ければ null</summary>
        public static string TraySummary(DateTime now, Func<string, bool> has)
        {
            if (has == null) has = id => true;
            var parts = new List<string>();
            try { if (has(SuiteRead.PlanId)) parts.Add("今日のタスク " + PlanCalc.TodayTasks(SafePlan(), now).Count + "件"); } catch { }
            try { if (has(SuiteRead.WalletId)) parts.Add("支払い " + WalletCalc.Dues(SafeWallet().Subs, now.Date, now.Date, now.Date.AddDays(1)).Count(d => !d.TrialEnd) + "件"); } catch { }
            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        static string Left(decimal v) { return v >= 0 ? "あと " + SuiteText.Yen((long)v) : SuiteText.Yen((long)-v) + " オーバー"; }

        public static string DayText(DateTime d, DateTime today)
        {
            int n = (int)(d.Date - today.Date).TotalDays;
            return n == 0 ? "今日" : n == 1 ? "明日" : n == 2 ? "あさって" : Fmt.MonthDay(d);
        }

        internal static string Tags(List<string> tags)
        {
            if (tags == null) return "";
            var t = tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => "#" + SuiteTags.Clean(x)).ToList();
            return t.Count == 0 ? "" : " ・ " + string.Join(" ", t);
        }

        /// <summary>メモの 2 行目以降の始め (q があれば q のまわり)</summary>
        internal static string Snippet(string text, string q)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return "";
            string s;
            if (!string.IsNullOrEmpty(q))
            {
                var hit = lines.FirstOrDefault(l => l.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) ?? lines[0];
                int i = Math.Max(0, hit.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) - 12);
                s = (i > 0 ? "…" : "") + hit.Substring(i);
            }
            else s = lines.Count > 1 ? string.Join("  ", lines.Skip(1).Take(3)) : "";
            return s.Length > 80 ? s.Substring(0, 80) + "…" : s;
        }

        internal static PlanView SafePlan() { try { return SuiteRead.Plan(); } catch { return new PlanView(); } }
        internal static WalletView SafeWallet() { try { return SuiteRead.Wallet(); } catch { return new WalletView(); } }
        internal static MemoView SafeMemo() { try { return SuiteRead.Memo(); } catch { return new MemoView(); } }
    }

    /// <summary>
    /// Teal Pass が作るサイト名の索引 (site-index.dat) を読む。
    /// 印・エントロピーは Teal Pass の Vault.cs (SiteIndex) と同じ (変えるときは両方を同時に)
    /// </summary>
    public static class PassSites
    {
        public const string FileName = "site-index.dat";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TealSuite.TealPass.siteindex.v1");
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PSI1");

        [DataContract]
        class IndexFile
        {
            [DataMember(Name = "version", Order = 1)] public int Version = 1;
            [DataMember(Name = "sites", Order = 2)] public List<string> Sites = new List<string>();
        }

        public static string PathOf { get { return Path.Combine(DataStore.DirFor(SuiteRead.PassId), FileName); } }

        static DateTime cacheTime; static long cacheLen = -1; static List<string> cache;

        /// <summary>サイト名の一覧 (索引が無い・読めない・別の Windows ユーザーのもの → 空)</summary>
        public static List<string> Read()
        {
            try
            {
                var fi = new FileInfo(PathOf);
                if (!fi.Exists) return new List<string>();
                if (cache != null && fi.LastWriteTimeUtc == cacheTime && fi.Length == cacheLen) return cache.ToList();
                var data = File.ReadAllBytes(fi.FullName);
                if (data.Length <= Magic.Length || !data.Take(Magic.Length).SequenceEqual(Magic)) return new List<string>();
                var enc = new byte[data.Length - Magic.Length];
                Buffer.BlockCopy(data, Magic.Length, enc, 0, enc.Length);
                var plain = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                List<string> sites;
                try { var f = DataStore.FromJson<IndexFile>(plain); sites = f != null && f.Sites != null ? f.Sites.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>(); }
                finally { Array.Clear(plain, 0, plain.Length); }
                cache = sites; cacheTime = fi.LastWriteTimeUtc; cacheLen = fi.Length;
                return sites.ToList();
            }
            catch (Exception ex) { DataStore.Log("pass site index: " + ex.GetType().Name); return new List<string>(); }
        }
    }

    /// <summary>全アプリの検索 (Teal Memo・Teal Plan・Teal Wallet・Teal Pass のサイト名)。「#タグ」なら Memo と Plan のタグ</summary>
    public static class HubSearch
    {
        public const int MaxPerApp = 50;

        public static bool IsTag(string q) { q = (q ?? "").Trim(); return q.Length > 1 && (q[0] == '#' || q[0] == '＃'); }

        static bool Hit(string s, string q) { return !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0; }

        /// <summary>見出し (アプリごと) つきの結果。何も見つからなければ空</summary>
        public static List<HomeItem> Run(string query, DateTime now, Func<string, bool> has)
        {
            var rows = new List<HomeItem>();
            var q = (query ?? "").Trim();
            if (q.Length == 0) return rows;
            if (has == null) has = id => true;
            bool tag = IsTag(q);
            string t = tag ? SuiteTags.Clean(q) : q;
            if (t.Length == 0) return rows;
            var today = now.Date;

            if (has(SuiteRead.MemoId))
            {
                var hits = new List<HomeItem>();
                foreach (var n in HubToday.SafeMemo().Live.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedUtc))
                {
                    if (tag ? !SuiteTags.Has(n.Tags, t) : !Hit(n.Text, t)) continue;
                    hits.Add(new HomeItem { AppId = SuiteRead.MemoId, Kind = "note", Key = n.Id, Badge = n.Pinned ? "📌" : "📝", Title = n.Title, Detail = HubToday.Snippet(n.Text, tag ? "#" + t : t) });
                }
                Add(rows, HubToday.MemoName, SuiteRead.MemoId, hits);
            }

            if (has(SuiteRead.PlanId))
            {
                var p = HubToday.SafePlan();
                var hits = new List<HomeItem>();
                foreach (var x in p.Tasks.OrderBy(x => x.Done ? 1 : 0).ThenBy(x => x.DueAt ?? DateTime.MaxValue))
                {
                    bool ok = tag ? SuiteTags.Has(x.Tags, t)
                        : Hit(x.Title, t) || Hit(x.Memo, t) || (x.Tags != null && x.Tags.Any(g => Hit(g, t))) || (x.Subtasks != null && x.Subtasks.Any(s => s != null && Hit(s.Text, t)));
                    if (!ok) continue;
                    var due = x.DueAt;
                    hits.Add(new HomeItem
                    {
                        AppId = SuiteRead.PlanId, Kind = "task", Key = x.Id, Badge = x.Done ? "✔" : "☐", Title = x.Title ?? "",
                        Detail = "タスク" + (x.Done ? " ・ 完了" : "") + (due.HasValue ? " ・ 期限 " + Fmt.MonthDay(due.Value) + (x.HasTime ? " " + Fmt.Time(due.Value) : "") : "") + HubToday.Tags(x.Tags),
                        DetailColor = !x.Done && due.HasValue && due.Value.Date < today ? Theme.Danger : Theme.SubText
                    });
                }
                foreach (var e in p.Events)
                {
                    bool ok = tag ? SuiteTags.Has(e.Tags, t) : Hit(e.Title, t) || Hit(e.Location, t) || Hit(e.Memo, t) || (e.Tags != null && e.Tags.Any(g => Hit(g, t)));
                    if (!ok) continue;
                    DateTime when = e.StartAt;
                    try { var o = e.Occurrences(today, today.AddYears(2)); if (o.Count > 0) when = o[0]; } catch { }
                    hits.Add(new HomeItem
                    {
                        AppId = SuiteRead.PlanId, Kind = "event", Key = e.Id, Badge = "📅", Title = e.Title ?? "",
                        Detail = "予定 ・ " + (when == DateTime.MinValue ? "" : Fmt.MonthDay(when) + (e.AllDay ? " 終日" : " " + Fmt.Time(when))) + (string.IsNullOrEmpty(e.Location) ? "" : " ・ " + e.Location) + HubToday.Tags(e.Tags)
                    });
                }
                Add(rows, HubToday.PlanName, SuiteRead.PlanId, hits);
            }

            if (has(SuiteRead.WalletId) && !tag)
            {
                var w = HubToday.SafeWallet();
                var hits = new List<HomeItem>();
                foreach (var s in w.Subs.OrderBy(s => s.Cancelled ? 1 : 0))
                {
                    if (!(Hit(s.Name, t) || Hit(s.Category, t) || Hit(s.Memo, t) || Hit(s.Payment, t))) continue;
                    string next = "";
                    try { var d = WalletCalc.Dues(new[] { s }, today, today, today.AddYears(2)).FirstOrDefault(); if (d != null) next = " ・ 次は " + Fmt.MonthDay(d.Day); } catch { }
                    hits.Add(new HomeItem
                    {
                        AppId = SuiteRead.WalletId, Kind = "sub", Key = s.Id, Badge = "💳", Title = s.Name ?? "",
                        Detail = "サブスク ・ " + SuiteText.Yen(s.Amount) + (s.Cancelled ? " ・ 解約済み" : s.Cancelling ? " ・ 解約予定" + next : next)
                    });
                }
                foreach (var l in w.Lists)
                    foreach (var i in l.Items)
                    {
                        if (!(Hit(i.Name, t) || Hit(i.Memo, t) || Hit(i.Category, t))) continue;
                        hits.Add(new HomeItem
                        {
                            AppId = SuiteRead.WalletId, Kind = "shop", Key = i.Id, Badge = "🛒", Title = i.Name ?? "",
                            Detail = "買い物リスト「" + (l.Name ?? "") + "」" + (i.Checked ? " ・ 買った" : "") + (i.Qty > 1 ? " ・ " + i.Qty + (i.Unit ?? "") : "")
                        });
                    }
                foreach (var x in w.Wants)
                {
                    if (!(Hit(x.Name, t) || Hit(x.Store, t) || Hit(x.Memo, t))) continue;
                    hits.Add(new HomeItem
                    {
                        AppId = SuiteRead.WalletId, Kind = "want", Key = x.Id, Badge = "⭐", Title = x.Name ?? "",
                        Detail = "ほしいもの" + (x.Bought ? " ・ 買った" : "") + (x.Price > 0 ? " ・ " + SuiteText.Yen(x.Price) : "") + (string.IsNullOrEmpty(x.Store) ? "" : " ・ " + x.Store)
                    });
                }
                Add(rows, HubToday.WalletName, SuiteRead.WalletId, hits);
            }

            if (has(SuiteRead.PassId) && !tag)
            {
                var hits = PassSites.Read().Where(s => Hit(s, t)).OrderBy(s => s, StringComparer.CurrentCulture)
                    .Select(s => new HomeItem { AppId = SuiteRead.PassId, Kind = "site", Key = s, Badge = "🔑", Title = s, Detail = "パスワード ・ ダブルクリックで Teal Pass を開きます" }).ToList();
                Add(rows, HubToday.PassName + " (サイト名だけ)", SuiteRead.PassId, hits);
            }
            return rows;
        }

        static void Add(List<HomeItem> rows, string name, string appId, List<HomeItem> hits)
        {
            if (hits.Count == 0) return;
            rows.Add(HomeItem.Head(name + "  " + hits.Count + " 件", appId));
            rows.AddRange(hits.Take(MaxPerApp));
            if (hits.Count > MaxPerApp) rows.Add(HomeItem.None("ほか " + (hits.Count - MaxPerApp) + " 件 (" + name + " で探してください)"));
        }
    }

    /// <summary>「今日」・検索の一覧 (見出しの行と、2 行の項目)</summary>
    public class HomeList : RowList
    {
        public HomeList() : base(2)
        {
            RowHeight = o => { var it = o as HomeItem; return it == null ? ItemHeight : it.Header ? Theme.Title.Height + Theme.S(20) : it.Empty ? Theme.Body.Height + Theme.S(12) : Theme.Bold.Height + Theme.Small.Height + Theme.S(16); };
            PlainRow = o => { var it = o as HomeItem; return it == null || it.Header || it.Empty; };
            PaintRow = PaintItem;
        }

        public HomeItem Current { get { var it = SelectedItem as HomeItem; return it != null && !it.Header && !it.Empty ? it : null; } }

        /// <summary>見出しを飛ばして、開ける最初の行を選ぶ</summary>
        public void SelectFirst()
        {
            for (int i = 0; i < Items.Count; i++) { var it = Items[i] as HomeItem; if (it != null && !it.Header && !it.Empty) { SelectedIndex = i; return; } }
            SelectedIndex = -1;
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            // 見出し・空の行は選ばない (次の行へ)
            var it = SelectedItem as HomeItem;
            if (it != null && (it.Header || it.Empty))
            {
                for (int i = SelectedIndex + 1; i < Items.Count; i++) { var n = Items[i] as HomeItem; if (n != null && !n.Header && !n.Empty) { SelectedIndex = i; return; } }
            }
            base.OnSelectedIndexChanged(e);
        }

        void PaintItem(Graphics g, Rectangle b, object o, bool sel)
        {
            var it = o as HomeItem;
            if (it == null) return;
            int pad = Theme.S(14);
            if (it.Header)
            {
                var r = new Rectangle(b.X + Theme.S(10), b.Y + Theme.S(10), b.Width - Theme.S(20), b.Height - Theme.S(10));
                Draw.Text(g, it.Title, Theme.Title, Theme.Accent, r);
                using (var p = new Pen(Theme.Border)) g.DrawLine(p, b.X + Theme.S(10), b.Bottom - 1, b.Right - Theme.S(10), b.Bottom - 1);
                return;
            }
            if (it.Empty)
            {
                Draw.Text(g, it.Title, Theme.Body, Theme.SubText, new Rectangle(b.X + pad + Theme.S(28), b.Y, b.Width - pad * 2, b.Height));
                return;
            }
            int bw = Theme.S(28);
            Draw.Text(g, it.Badge, Theme.Body, Theme.Text, new Rectangle(b.X + pad, b.Y, bw, b.Height));
            int x = b.X + pad + bw, w = b.Width - (x - b.X) - pad;
            int y = b.Y + Theme.S(7);
            Draw.Text(g, it.Title, Theme.Bold, Theme.Text, new Rectangle(x, y, w, Theme.Bold.Height + 2));
            Draw.Text(g, it.Detail, Theme.Small, it.DetailColor, new Rectangle(x, y + Theme.Bold.Height + Theme.S(2), w, Theme.Small.Height + 2));
        }
    }
}
