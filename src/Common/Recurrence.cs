// TealSuite 共通 (1.10.0): くり返しの日付の計算 (画面なし)
//  - サブスクの支払い日 (Teal Wallet。Teal Plan のカレンダーと Teal Hub の「今日」でも同じ計算を使う)
//  - 予定のくり返し (Teal Plan。Teal Hub の「今日」でも同じ計算を使う)
// どのアプリにも入るので、アプリごとのデータの形には依存しない (日付と種類だけを受け取る)
using System;
using System.Collections.Generic;

namespace TealSuite.Common
{
    /// <summary>支払いの周期 (Teal Wallet の Cycle と同じ並び)</summary>
    public enum BillCycle { Monthly, Yearly, Weekly, EveryNMonths }

    /// <summary>予定・タスクのくり返し (Teal Plan の Repeat と同じ並び)</summary>
    public enum RepeatKind { None, Daily, Weekly, Monthly, Weekdays, Yearly }

    public static class Renewal
    {
        /// <summary>subs.json の cycle の文字 (monthly / yearly / weekly / months)</summary>
        public static BillCycle ParseCycle(string text)
        {
            switch (text) { case "yearly": return BillCycle.Yearly; case "weekly": return BillCycle.Weekly; case "months": return BillCycle.EveryNMonths; default: return BillCycle.Monthly; }
        }

        /// <summary>課金の起点。無料期間があれば、その終わりの日 (この日から有料) が最初の支払い日</summary>
        public static DateTime Anchor(DateTime? start, DateTime? trialEnd, DateTime today)
        {
            var s = (start ?? today).Date;
            if (trialEnd.HasValue && trialEnd.Value.Date > s) return trialEnd.Value.Date;
            return s;
        }

        /// <summary>起点から k 回目の支払い日 (月末の契約は、短い月では月末に合わせる)</summary>
        public static DateTime Nth(BillCycle c, int cycleMonths, DateTime anchor, int k)
        {
            switch (c)
            {
                case BillCycle.Yearly: return anchor.AddYears(k);
                case BillCycle.Weekly: return anchor.AddDays(7 * k);
                case BillCycle.EveryNMonths: return anchor.AddMonths(Math.Max(1, cycleMonths) * k);
                default: return anchor.AddMonths(k);
            }
        }

        /// <summary>today 以降で最初の支払い日が何回目か</summary>
        public static int NextIndex(BillCycle c, int cycleMonths, DateTime anchor, DateTime today)
        {
            today = today.Date;
            if (anchor >= today) return 0;
            // おおよその回数から始めて、today 以上になるまで進める
            int k;
            switch (c)
            {
                case BillCycle.Weekly: k = (int)((today - anchor).TotalDays / 7); break;
                case BillCycle.Yearly: k = today.Year - anchor.Year - 1; break;
                case BillCycle.EveryNMonths: k = ((today.Year - anchor.Year) * 12 + today.Month - anchor.Month) / Math.Max(1, cycleMonths) - 1; break;
                default: k = (today.Year - anchor.Year) * 12 + today.Month - anchor.Month - 1; break;
            }
            k = Math.Max(0, k);
            while (Nth(c, cycleMonths, anchor, k) < today && k < 1000000) k++;
            return k;
        }

        /// <summary>today 以降で最初の支払い日</summary>
        public static DateTime Next(BillCycle c, int cycleMonths, DateTime anchor, DateTime today)
        {
            return Nth(c, cycleMonths, anchor, NextIndex(c, cycleMonths, anchor, today));
        }

        /// <summary>
        /// [from, to) の支払い日。onlyUntilNext = true (解約予定) なら、today 以降の次の 1 回までで終わる。
        /// 過ぎた日 (today より前) の支払いも、範囲に入っていれば返す
        /// </summary>
        public static List<DateTime> Payments(BillCycle c, int cycleMonths, DateTime anchor, DateTime today, bool onlyUntilNext, DateTime from, DateTime to, int max = 400)
        {
            var list = new List<DateTime>();
            if (to <= from) return list;
            DateTime last = onlyUntilNext ? Next(c, cycleMonths, anchor, today) : DateTime.MaxValue;
            int k = from.Date > anchor ? Math.Max(0, NextIndex(c, cycleMonths, anchor, from.Date) - 1) : 0;
            for (int guard = 0; guard < 5000 && list.Count < max; guard++, k++)
            {
                var d = Nth(c, cycleMonths, anchor, k);
                if (d >= to || d > last) break;
                if (d >= from.Date) list.Add(d);
            }
            return list;
        }

        /// <summary>無料期間中か (始まりから無料期間の終わりの前日まで)</summary>
        public static bool InTrial(DateTime? start, DateTime? trialEnd, DateTime today)
        {
            return trialEnd.HasValue && trialEnd.Value.Date > today.Date && (start ?? today) <= today;
        }

        /// <summary>1 か月あたりの金額 (毎週は 52 週 / 12 か月で換算)</summary>
        public static decimal MonthlyCost(long amount, BillCycle c, int cycleMonths)
        {
            switch (c)
            {
                case BillCycle.Yearly: return amount / 12m;
                case BillCycle.Weekly: return amount * 52m / 12m;
                case BillCycle.EveryNMonths: return amount / (decimal)Math.Max(1, cycleMonths);
                default: return amount;
            }
        }

        // ---------------------------------------------------------------- 予定のくり返し

        public static RepeatKind ParseRepeat(string text)
        {
            switch (text) { case "daily": return RepeatKind.Daily; case "weekly": return RepeatKind.Weekly; case "monthly": return RepeatKind.Monthly; case "weekdays": return RepeatKind.Weekdays; case "yearly": return RepeatKind.Yearly; default: return RepeatKind.None; }
        }

        /// <summary>くり返しの k 回目の始まり (平日は別に数える)</summary>
        static DateTime NthRepeat(DateTime s, RepeatKind r, int k)
        {
            switch (r)
            {
                case RepeatKind.Daily: return s.AddDays(k);
                case RepeatKind.Weekly: return s.AddDays(7 * k);
                case RepeatKind.Monthly: return s.AddMonths(k); // 元の日から数えるので 1/31 → 2/28 → 3/31
                case RepeatKind.Yearly: return s.AddYears(k);
                default: return s;
            }
        }

        /// <summary>
        /// [from, to) に重なる回の始まり (くり返さない予定は 1 回だけ)。
        /// s = 最初の回の始まり、len = 1 回の長さ、until = くり返しの最後の日 (含む)、skip = 消した回 (その日の始まり) なら true
        /// </summary>
        public static List<DateTime> Occurrences(DateTime s, TimeSpan len, RepeatKind r, DateTime? until, Func<DateTime, bool> skip, DateTime from, DateTime to, int max = 400)
        {
            var list = new List<DateTime>();
            if (s == DateTime.MinValue) return list;
            if (skip == null) skip = d => false;
            DateTime limit = until.HasValue ? until.Value.Date.AddDays(1) : DateTime.MaxValue;
            if (r == RepeatKind.None)
            {
                if (s < to && s + len > from && !skip(s)) list.Add(s);
                else if (len <= TimeSpan.Zero && s >= from && s < to && !skip(s)) list.Add(s);
                return list;
            }
            if (r == RepeatKind.Weekdays)
            {
                // 始まりの日から 1 日ずつ。範囲の少し前から数える
                var d = s;
                if (from - len > s) d = s.AddDays(Math.Max(0, (int)((from - len - s).TotalDays) - 1));
                for (int guard = 0; d < to && d < limit && guard < 5000 && list.Count < max; d = d.AddDays(1), guard++)
                {
                    if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
                    if (d + len > from && !skip(d)) list.Add(d);
                }
                return list;
            }
            int k = 0;
            // 範囲の手前まで一気に飛ばす
            double back = (from - len - s).TotalDays;
            if (back > 0)
            {
                if (r == RepeatKind.Daily) k = Math.Max(0, (int)back - 1);
                else if (r == RepeatKind.Weekly) k = Math.Max(0, (int)(back / 7) - 1);
                else if (r == RepeatKind.Monthly) k = Math.Max(0, (int)(back / 31) - 1);
                else if (r == RepeatKind.Yearly) k = Math.Max(0, (int)(back / 366) - 1);
            }
            for (int guard = 0; guard < 5000 && list.Count < max; guard++, k++)
            {
                var d = NthRepeat(s, r, k);
                if (d >= to || d >= limit) break;
                if (d + len > from && !skip(d)) list.Add(d);
            }
            return list;
        }
    }
}
