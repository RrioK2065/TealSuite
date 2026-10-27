// Teal Sub (サブスク整理) - 計算 (画面なし。tests/SubTasksTests.cs で確かめる)
//  - 次の更新日 / 月額・年額への換算 / 合計 / カテゴリ別
//  - 見直し候補 (決まったルールで点数をつける。AI やネットは使わない)
//  - お知らせ (更新日・無料期間の終わりの N 日前) / CSV / 見本データ
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TealSuite.Common;

namespace TealSuite.Sub
{
    public class ReviewItem
    {
        public Subscription Sub;
        public int Score;                       // 0〜100 (高いほど見直したほうがよい)
        public List<string> Reasons = new List<string>();
        public decimal YearlySaving;            // 解約したら 1 年で浮く金額
        public bool Strong { get { return Score >= SubLogic.StrongScore; } }
    }

    public class Notice
    {
        public string Key, Title, Text;
        public string SubId;
    }

    public class CategoryTotal
    {
        public string Category;
        public decimal Monthly;
        public int Count;
    }

    public static class SubLogic
    {
        public const int CandidateScore = 30;   // これ以上で「見直し候補」
        public const int StrongScore = 60;      // これ以上で「見直しをおすすめ」
        public static readonly string[] Categories = { "動画", "音楽", "ゲーム", "クラウド・ストレージ", "ソフト・アプリ", "ニュース・雑誌", "学習", "ショッピング・会員", "フィットネス・健康", "生活・その他サービス", "その他" };
        public static readonly string[] UsageLabels = { "毎日", "週に数回", "月に数回", "ほぼ使わない" };
        public static readonly string[] StatusLabels = { "契約中", "解約予定", "解約済み" };
        public static readonly string[] CycleLabels = { "毎月", "毎年", "毎週", "○か月ごと" };

        public static string CycleLabel(Subscription s)
        {
            switch (s.Cycle)
            {
                case Cycle.Yearly: return "毎年";
                case Cycle.Weekly: return "毎週";
                case Cycle.EveryNMonths: return s.CycleMonths <= 1 ? "毎月" : s.CycleMonths + "か月ごと";
                default: return "毎月";
            }
        }
        public static string UsageLabel(Usage u) { return UsageLabels[(int)u]; }
        public static string StatusLabel(SubStatus s) { return StatusLabels[(int)s]; }

        // ---------------------------------------------------------------- 更新日

        /// <summary>課金の起点。無料期間があれば、その終わりの日が最初の支払い日</summary>
        public static DateTime Anchor(Subscription s)
        {
            var start = (s.StartDate ?? DateTime.Today).Date;
            var t = s.TrialEndDate;
            if (t.HasValue && t.Value.Date > start) return t.Value.Date;
            return start;
        }

        /// <summary>起点から k 回目の支払い日 (月末の契約は、短い月では月末に合わせる)</summary>
        public static DateTime Nth(Subscription s, DateTime anchor, int k)
        {
            switch (s.Cycle)
            {
                case Cycle.Yearly: return anchor.AddYears(k);
                case Cycle.Weekly: return anchor.AddDays(7 * k);
                case Cycle.EveryNMonths: return anchor.AddMonths(Math.Max(1, s.CycleMonths) * k);
                default: return anchor.AddMonths(k);
            }
        }

        /// <summary>today 以降で最初の支払い日。解約済みなら null</summary>
        public static DateTime? NextRenewal(Subscription s, DateTime today)
        {
            if (s.Status == SubStatus.Cancelled) return null;
            today = today.Date;
            var anchor = Anchor(s);
            if (anchor >= today) return anchor;
            // おおよその回数から始めて、today 以上になるまで進める
            int k;
            switch (s.Cycle)
            {
                case Cycle.Weekly: k = (int)((today - anchor).TotalDays / 7); break;
                case Cycle.Yearly: k = today.Year - anchor.Year - 1; break;
                case Cycle.EveryNMonths: k = ((today.Year - anchor.Year) * 12 + today.Month - anchor.Month) / Math.Max(1, s.CycleMonths) - 1; break;
                default: k = (today.Year - anchor.Year) * 12 + today.Month - anchor.Month - 1; break;
            }
            k = Math.Max(0, k);
            var d = Nth(s, anchor, k);
            while (d < today) { k++; d = Nth(s, anchor, k); }
            return d;
        }

        public static bool InTrial(Subscription s, DateTime today)
        {
            var t = s.TrialEndDate;
            return t.HasValue && t.Value.Date > today.Date && (s.StartDate ?? today) <= today;
        }

        // ---------------------------------------------------------------- 金額

        /// <summary>1 か月あたりの金額 (毎週は 52 週 / 12 か月で換算)</summary>
        public static decimal MonthlyCost(Subscription s)
        {
            switch (s.Cycle)
            {
                case Cycle.Yearly: return s.Amount / 12m;
                case Cycle.Weekly: return s.Amount * 52m / 12m;
                case Cycle.EveryNMonths: return s.Amount / (decimal)Math.Max(1, s.CycleMonths);
                default: return s.Amount;
            }
        }
        /// <summary>1 年あたりの金額</summary>
        public static decimal YearlyCost(Subscription s)
        {
            switch (s.Cycle)
            {
                case Cycle.Yearly: return s.Amount;
                case Cycle.Weekly: return s.Amount * 52m;
                case Cycle.EveryNMonths: return s.Amount * 12m / Math.Max(1, s.CycleMonths);
                default: return s.Amount * 12m;
            }
        }
        public static decimal MonthlyTotal(IEnumerable<Subscription> subs) { return subs.Where(s => s.Paying).Sum(s => MonthlyCost(s)); }
        public static decimal YearlyTotal(IEnumerable<Subscription> subs) { return subs.Where(s => s.Paying).Sum(s => YearlyCost(s)); }

        /// <summary>カテゴリ別の月額 (大きい順)</summary>
        public static List<CategoryTotal> ByCategory(IEnumerable<Subscription> subs)
        {
            return subs.Where(s => s.Paying)
                .GroupBy(s => string.IsNullOrEmpty(s.Category) ? "その他" : s.Category)
                .Select(g => new CategoryTotal { Category = g.Key, Monthly = g.Sum(s => MonthlyCost(s)), Count = g.Count() })
                .OrderByDescending(c => c.Monthly).ThenBy(c => c.Category, StringComparer.Ordinal).ToList();
        }

        /// <summary>days 日以内に支払いがあるもの (近い順)</summary>
        public static List<KeyValuePair<Subscription, DateTime>> Upcoming(IEnumerable<Subscription> subs, DateTime today, int days)
        {
            var list = new List<KeyValuePair<Subscription, DateTime>>();
            foreach (var s in subs)
            {
                var r = NextRenewal(s, today);
                if (r.HasValue && (r.Value - today.Date).TotalDays <= days) list.Add(new KeyValuePair<Subscription, DateTime>(s, r.Value));
            }
            return list.OrderBy(p => p.Value).ThenBy(p => p.Key.Name, StringComparer.CurrentCulture).ToList();
        }

        // ---------------------------------------------------------------- 見直し候補

        /// <summary>使う頻度から見た 1 か月の利用回数の目安</summary>
        public static decimal UsesPerMonth(Usage u)
        {
            switch (u) { case Usage.Daily: return 30m; case Usage.Weekly: return 12m; case Usage.Monthly: return 3m; default: return 0.5m; }
        }

        /// <summary>見直しの点数と理由 (契約中のものだけ。解約予定・解約済みは対象外)</summary>
        public static ReviewItem Review(Subscription s, IList<Subscription> all, DateTime today)
        {
            today = today.Date;
            var r = new ReviewItem { Sub = s, YearlySaving = YearlyCost(s) };
            if (s.Status != SubStatus.Active) return r;
            int score = 0;
            // 1) 使う頻度
            if (s.Usage == Usage.Rarely) { score += 40; r.Reasons.Add("使う頻度が「ほぼ使わない」になっています"); }
            else if (s.Usage == Usage.Monthly) { score += 20; r.Reasons.Add("使うのは月に数回です"); }
            // 2) 最後に使った日
            var last = s.LastUsedDate;
            if (last.HasValue)
            {
                int days = (int)(today - last.Value.Date).TotalDays;
                if (days >= 90) { score += 35; r.Reasons.Add("最後に使ってから " + days + " 日たっています (3か月以上)"); }
                else if (days >= 45) { score += 25; r.Reasons.Add("最後に使ってから " + days + " 日たっています"); }
                else if (days >= 30) { score += 15; r.Reasons.Add("最後に使ってから 1 か月以上たっています (" + days + " 日)"); }
            }
            // 3) 使う回数のわりに高い (1 回あたりの金額)
            decimal monthly = MonthlyCost(s);
            if (monthly > 0)
            {
                decimal perUse = monthly / UsesPerMonth(s.Usage);
                if (perUse >= 1000m) { score += 25; r.Reasons.Add("1 回あたり約 " + Fmt.Yen(perUse) + " で、使う回数のわりに高めです"); }
                else if (perUse >= 400m) { score += 12; r.Reasons.Add("1 回あたり約 " + Fmt.Yen(perUse) + " かかっています"); }
            }
            // 4) 同じカテゴリが重なっている (いちばんよく使うもの以外に点数)
            if (!string.IsNullOrEmpty(s.Category) && s.Category != "その他")
            {
                var same = all.Where(o => o.Status == SubStatus.Active && o.Category == s.Category).ToList();
                if (same.Count >= 2)
                {
                    var keep = same.OrderBy(o => (int)o.Usage).ThenByDescending(o => o.LastUsedDate ?? DateTime.MinValue).ThenBy(o => MonthlyCost(o)).First();
                    if (keep.Id != s.Id)
                    {
                        score += 15;
                        r.Reasons.Add("同じカテゴリ「" + s.Category + "」に " + same.Count + " 件あります (よく使う「" + keep.Name + "」にまとめられるかも)");
                    }
                }
            }
            // 5) 無料期間がもうすぐ終わる
            var t = s.TrialEndDate;
            if (t.HasValue)
            {
                int left = (int)(t.Value.Date - today).TotalDays;
                if (left >= 0 && left <= 7)
                {
                    score += 25;
                    r.Reasons.Add("無料期間が" + (left == 0 ? "今日" : " " + left + " 日後に") + "終わり、そのあと " + Fmt.Yen(s.Amount) + " (" + CycleLabel(s) + ") かかります");
                }
            }
            r.Score = Math.Min(100, score);
            return r;
        }

        /// <summary>見直し候補 (点数の高い順)</summary>
        public static List<ReviewItem> Candidates(IList<Subscription> all, DateTime today)
        {
            return all.Where(s => s.Status == SubStatus.Active)
                .Select(s => Review(s, all, today))
                .Where(r => r.Score >= CandidateScore)
                .OrderByDescending(r => r.Score).ThenByDescending(r => r.YearlySaving).ToList();
        }
        public static decimal PossibleSaving(IEnumerable<ReviewItem> items) { return items.Sum(i => i.YearlySaving); }

        // ---------------------------------------------------------------- 解約済み

        /// <summary>解約してから今日までに浮いた金額の目安 (月額 × 経過月数)</summary>
        public static decimal SavedSoFar(Subscription s, DateTime today)
        {
            if (s.Status != SubStatus.Cancelled || !s.CancelledDate.HasValue) return 0;
            double days = Math.Max(0, (today.Date - s.CancelledDate.Value.Date).TotalDays);
            return Math.Round(MonthlyCost(s) * (decimal)(days / 30.4375), 0);
        }

        // ---------------------------------------------------------------- お知らせ

        /// <summary>今日出すべきお知らせ (まだ出していないもの)。出したら Notified に Key を足す</summary>
        public static List<Notice> DueNotices(SubData data, DateTime today)
        {
            var list = new List<Notice>();
            if (!data.Settings.Remind) return list;
            today = today.Date;
            int n = data.Settings.RemindDays;
            var done = new HashSet<string>(data.Notified ?? new List<string>());
            foreach (var s in data.Subs.Where(x => x.Paying))
            {
                var trial = s.TrialEndDate;
                bool trialSoon = false;
                if (trial.HasValue && s.Status == SubStatus.Active)
                {
                    int left = (int)(trial.Value.Date - today).TotalDays;
                    if (left >= 0 && left <= n)
                    {
                        trialSoon = true;
                        var key = s.Id + "|trial|" + Fmt.Iso(trial.Value);
                        if (!done.Contains(key))
                            list.Add(new Notice { Key = key, SubId = s.Id, Title = "無料期間がもうすぐ終わります: " + s.Name,
                                Text = Fmt.MonthDay(trial.Value) + " (" + Fmt.Relative(trial.Value, today) + ") から " + Fmt.Yen(s.Amount) + " (" + CycleLabel(s) + ") がかかります。続けないなら、その前に解約を。" });
                    }
                }
                var r = NextRenewal(s, today);
                if (!r.HasValue) continue;
                int d = (int)(r.Value - today).TotalDays;
                if (d < 0 || d > n) continue;
                if (trialSoon && trial.HasValue && trial.Value.Date == r.Value) continue;
                var k2 = s.Id + "|renew|" + Fmt.Iso(r.Value);
                if (done.Contains(k2)) continue;
                if (s.Status == SubStatus.Cancelling)
                    list.Add(new Notice { Key = k2, SubId = s.Id, Title = "解約予定のサブスクが更新されます: " + s.Name,
                        Text = Fmt.MonthDay(r.Value) + " (" + Fmt.Relative(r.Value, today) + ") に " + Fmt.Yen(s.Amount) + " の支払いがあります。解約を忘れずに。" });
                else
                    list.Add(new Notice { Key = k2, SubId = s.Id, Title = "まもなく更新: " + s.Name,
                        Text = Fmt.MonthDay(r.Value) + " (" + Fmt.Relative(r.Value, today) + ") に " + Fmt.Yen(s.Amount) + " の支払いがあります。" });
            }
            return list;
        }

        /// <summary>古いお知らせ済みの印を消す (60 日より前の日付のもの)</summary>
        public static void PruneNotified(SubData data, DateTime today)
        {
            data.Notified.RemoveAll(k =>
            {
                var p = k.Split('|');
                var d = p.Length == 3 ? Fmt.ParseIso(p[2]) : null;
                return !d.HasValue || d.Value < today.Date.AddDays(-60);
            });
        }

        // ---------------------------------------------------------------- CSV

        public static string ToCsv(IEnumerable<Subscription> subs, DateTime today)
        {
            var sb = new StringBuilder();
            sb.Append("名前,金額(円),周期,月額換算(円),年額換算(円),次の支払い日,カテゴリ,支払い方法,使う頻度,最後に使った日,状態,無料期間の終わり,解約日,解約ページ,メモ\r\n");
            foreach (var s in subs)
            {
                var r = NextRenewal(s, today);
                var cells = new[] {
                    s.Name, s.Amount.ToString(CultureInfo.InvariantCulture), CycleLabel(s),
                    Math.Round(MonthlyCost(s), 0).ToString(CultureInfo.InvariantCulture), Math.Round(YearlyCost(s), 0).ToString(CultureInfo.InvariantCulture),
                    r.HasValue ? Fmt.Iso(r.Value) : "", s.Category, s.Payment, UsageLabel(s.Usage), s.LastUsed, StatusLabel(s.Status),
                    s.TrialEnd, s.CancelledOn, s.CancelUrl, s.Memo };
                sb.Append(string.Join(",", cells.Select(Csv))).Append("\r\n");
            }
            return sb.ToString();
        }
        public static string Csv(string v)
        {
            v = v ?? "";
            // 表計算ソフトで式として読まれないように (=,+,-,@ で始まる文字列)
            if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0 && !IsNumber(v)) v = "'" + v;
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
        static bool IsNumber(string v) { decimal d; return decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d); }

        /// <summary>解約ページの URL として開いてよいか (http/https のみ)</summary>
        public static bool IsOpenableUrl(string url)
        {
            Uri u;
            return !string.IsNullOrEmpty(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        // ---------------------------------------------------------------- 見本データ

        public static List<Subscription> SampleData(DateTime today)
        {
            today = today.Date;
            Func<string, long, Cycle, int, string, Usage, int?, int, Subscription> mk = (name, amount, cycle, startDaysAgo, cat, usage, lastUsedAgo, months) =>
            {
                var s = new Subscription { Name = name, Amount = amount, Category = cat, Created = Fmt.Iso(today) };
                s.Cycle = cycle; s.CycleMonths = months; s.Usage = usage;
                s.StartDate = today.AddDays(-startDaysAgo);
                if (lastUsedAgo.HasValue) s.LastUsedDate = today.AddDays(-lastUsedAgo.Value);
                return s;
            };
            var list = new List<Subscription> {
                mk("動画見放題 スタンダード", 1490, Cycle.Monthly, 400 - 2, "動画", Usage.Daily, 0, 0),
                mk("映画専門チャンネル", 990, Cycle.Monthly, 200, "動画", Usage.Rarely, 75, 0),
                mk("音楽聴き放題", 1080, Cycle.Monthly, 700 + 5, "音楽", Usage.Daily, 1, 0),
                mk("クラウド保存 200GB", 4500, Cycle.Yearly, 300, "クラウド・ストレージ", Usage.Weekly, 3, 0),
                mk("フィットネスジム", 7700, Cycle.Monthly, 150 + 20, "フィットネス・健康", Usage.Monthly, 40, 0),
                mk("ニュース読み放題", 1980, Cycle.Monthly, 90 + 12, "ニュース・雑誌", Usage.Monthly, 20, 0),
                mk("語学学習アプリ", 2400, Cycle.EveryNMonths, 120, "学習", Usage.Weekly, 6, 3),
                mk("写真編集ソフト", 2380, Cycle.Monthly, 25, "ソフト・アプリ", Usage.Weekly, 2, 0),
            };
            list[0].Payment = "クレジットカード (メイン)"; list[1].Payment = "クレジットカード (メイン)"; list[2].Payment = "スマホのアプリストア";
            list[3].Payment = "クレジットカード (メイン)"; list[4].Payment = "口座振替"; list[5].Payment = "クレジットカード (サブ)";
            list[6].Payment = "スマホのアプリストア"; list[7].Payment = "クレジットカード (メイン)";
            list[4].Memo = "入会金なし。休会もできるか確認する";
            // 無料期間中のもの (5 日後から有料)
            var trial = mk("電子書籍読み放題", 980, Cycle.Monthly, 25, "ニュース・雑誌", Usage.Monthly, 9, 0);
            trial.TrialEndDate = today.AddDays(5); trial.Payment = "スマホのアプリストア";
            list.Add(trial);
            // 解約済み
            var gone = mk("ゲーム オンラインプラス", 850, Cycle.Monthly, 500, "ゲーム", Usage.Rarely, 120, 0);
            gone.Status = SubStatus.Cancelled; gone.CancelledDate = today.AddDays(-95);
            list.Add(gone);
            return list;
        }
    }
}
