// Teal Tasks - 計算 (おすすめ順の点数・繰り返し・お知らせ・集計)。画面から切り離してテストできるようにしている
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public class TaskScore
    {
        public int Score;
        public List<string> Reasons = new List<string>();
    }

    public class Notice
    {
        public string Key, Title, Text;
        public TaskItem Task;
    }

    public static class TaskLogic
    {
        public static readonly string[] PriorityLabels = { "低", "中", "高" };
        public static readonly string[] RepeatLabels = { "くり返さない", "毎日", "毎週", "毎月" };
        /// <summary>かかる時間の選択肢 (分)</summary>
        public static readonly int[] DurationChoices = { 0, 15, 30, 45, 60, 90, 120, 180, 240 };

        public static string PriorityLabel(int p) { return PriorityLabels[Math.Max(0, Math.Min(2, p))]; }
        public static string RepeatLabel(Repeat r) { return RepeatLabels[(int)r]; }

        /// <summary>週の始まり (月曜) の日付</summary>
        public static DateTime WeekStart(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7; // 月=0 … 日=6
            return d.Date.AddDays(-diff);
        }

        // ------------------------------------------------------------ おすすめ順の点数

        /// <summary>今やるべき度合い。期限の近さ・優先度・すぐ終わるか、から計算する (理由も返す)</summary>
        public static TaskScore Score(TaskItem t, DateTime now)
        {
            var r = new TaskScore();
            if (t.Done) return r;
            var due = t.DueAt;
            if (due.HasValue)
            {
                int days = (int)(due.Value.Date - now.Date).TotalDays;
                if (days < 0 || (t.HasTime && due.Value < now))
                {
                    int late = Math.Max(0, -days);
                    r.Score += 100 + Math.Min(late * 5, 30);
                    r.Reasons.Add(late == 0 ? "期限の時刻を過ぎています" : "期限を " + late + " 日過ぎています");
                }
                else if (days == 0)
                {
                    r.Score += 60;
                    if (t.HasTime && (due.Value - now).TotalHours <= 3) { r.Score += 15; r.Reasons.Add("あと " + HoursText(due.Value - now) + "で期限です"); }
                    else r.Reasons.Add("今日が期限です");
                }
                else if (days == 1) { r.Score += 35; r.Reasons.Add("明日が期限です"); }
                else if (days <= 3) { r.Score += 20; r.Reasons.Add(days + " 日後が期限です"); }
                else if (days <= 7) { r.Score += 10; r.Reasons.Add("1 週間以内に期限です"); }
            }
            if (t.Priority == TaskItem.High) { r.Score += 30; r.Reasons.Add("優先度「高」"); }
            else if (t.Priority == TaskItem.Mid) r.Score += 15;
            if (t.DurationMin > 0 && t.DurationMin <= 15) { r.Score += 10; r.Reasons.Add("15 分以内で終わります"); }
            else if (t.DurationMin > 0 && t.DurationMin <= 30) { r.Score += 5; r.Reasons.Add("30 分以内で終わります"); }
            return r;
        }

        static string HoursText(TimeSpan s)
        {
            int m = Math.Max(1, (int)Math.Ceiling(s.TotalMinutes));
            return Fmt.Minutes(m);
        }

        /// <summary>期限切れ (日付だけの期限は、その日のうちは期限切れにしない)</summary>
        public static bool IsOverdue(TaskItem t, DateTime now)
        {
            if (t.Done || !t.HasDue) return false;
            if (t.HasTime) return t.DueAt.Value < now;
            return t.DueDate.Value < now.Date;
        }

        /// <summary>今日の画面: 期限切れ + 今日が期限 (未完了) をおすすめ順に</summary>
        public static List<TaskItem> TodayList(IEnumerable<TaskItem> tasks, DateTime now)
        {
            return tasks.Where(t => !t.Done && t.HasDue && t.DueDate.Value <= now.Date)
                .OrderByDescending(t => Score(t, now).Score)
                .ThenBy(t => t.DueAt.Value)
                .ThenBy(t => t.Title, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>今日終わらせたもの</summary>
        public static List<TaskItem> DoneToday(IEnumerable<TaskItem> tasks, DateTime now)
        {
            return tasks.Where(t => t.Done && t.DoneTime.HasValue && t.DoneTime.Value.Date == now.Date).OrderByDescending(t => t.DoneAt).ToList();
        }

        /// <summary>その日に表示するタスク (時刻つきは時刻順、日付だけのものはその前)</summary>
        public static List<TaskItem> OnDay(IEnumerable<TaskItem> tasks, DateTime day)
        {
            return tasks.Where(t => t.HasDue && t.DueDate.Value == day.Date)
                .OrderBy(t => t.HasTime ? 1 : 0).ThenBy(t => t.DueAt.Value).ThenByDescending(t => t.Priority).ThenBy(t => t.Title, StringComparer.CurrentCulture)
                .ToList();
        }

        // ------------------------------------------------------------ 完了と繰り返し

        /// <summary>次の回の期限。今日より後になるまで進める (たまっていた分をまとめて作らない)</summary>
        public static DateTime NextDue(DateTime due, Repeat r, DateTime today)
        {
            var d = due;
            int guard = 0;
            do
            {
                if (r == Repeat.Daily) d = d.AddDays(1);
                else if (r == Repeat.Weekly) d = d.AddDays(7);
                else if (r == Repeat.Monthly)
                {
                    // 31 日などは月末に寄せるが、元の日を覚えておく (1/31 → 2/28 → 3/31)
                    int n = 0; var x = due;
                    while (x <= d) { n++; x = AddMonthsKeep(due, n); }
                    d = x;
                }
                else return due;
            } while (d.Date <= today.Date && ++guard < 5000);
            return d;
        }

        static DateTime AddMonthsKeep(DateTime start, int months)
        {
            var m = new DateTime(start.Year, start.Month, 1).AddMonths(months);
            int day = Math.Min(start.Day, DateTime.DaysInMonth(m.Year, m.Month));
            return new DateTime(m.Year, m.Month, day, start.Hour, start.Minute, 0);
        }

        /// <summary>完了にする。繰り返しなら次の回を作って返す (作らなければ null)</summary>
        public static TaskItem Complete(TaskData data, TaskItem t, DateTime now)
        {
            if (t.Done) return null;
            t.Done = true;
            t.DoneAt = Fmt.IsoTime(now);
            if (t.Repeat == Repeat.None || !t.HasDue) return null;
            var next = t.Clone();
            next.Id = Guid.NewGuid().ToString("N");
            next.Done = false; next.DoneAt = null; next.NextId = null;
            next.Created = Fmt.IsoTime(now);
            foreach (var s in next.Subtasks) s.Done = false;
            next.SetDue(NextDue(t.DueAt.Value, t.Repeat, now), t.HasTime);
            t.NextId = next.Id;
            data.Tasks.Add(next);
            return next;
        }

        /// <summary>完了を取り消す。そのとき作った次の回が手つかずなら消す</summary>
        public static void Uncomplete(TaskData data, TaskItem t)
        {
            if (!t.Done) return;
            t.Done = false; t.DoneAt = null;
            if (t.NextId != null)
            {
                var next = data.Find(t.NextId);
                if (next != null && !next.Done && !next.Subtasks.Any(s => s.Done)) data.Tasks.Remove(next);
                t.NextId = null;
            }
        }

        public static void Toggle(TaskData data, TaskItem t, DateTime now)
        {
            if (t.Done) Uncomplete(data, t); else Complete(data, t, now);
        }

        // ------------------------------------------------------------ お知らせ

        public static List<Notice> DueNotices(TaskData data, DateTime now)
        {
            var list = new List<Notice>();
            var st = data.Settings;
            if (!st.Remind) return list;
            if (st.Morning && now.Hour >= 5)
            {
                var today = TodayList(data.Tasks, now);
                string key = "morning|" + Fmt.Iso(now);
                if (today.Count > 0 && !data.Notified.Contains(key))
                {
                    int over = today.Count(t => IsOverdue(t, now) && t.DueDate.Value < now.Date);
                    var sb = new StringBuilder();
                    foreach (var t in today.Take(3)) sb.AppendLine("・" + (t.HasTime ? Fmt.Time(t.DueAt.Value) + " " : "") + t.Title);
                    if (today.Count > 3) sb.AppendLine("ほか " + (today.Count - 3) + " 件");
                    list.Add(new Notice { Key = key, Title = "今日のタスク " + today.Count + " 件" + (over > 0 ? " (期限切れ " + over + " 件)" : ""), Text = sb.ToString().TrimEnd() });
                }
            }
            foreach (var t in data.Tasks)
            {
                if (t.Done || !t.HasTime) continue;
                var due = t.DueAt.Value;
                string key = t.Id + "|" + t.Due;
                if (data.Notified.Contains(key)) continue;
                // 何分前〜期限の 1 時間後まで (スリープ明けで少し遅れても出す)
                if (now >= due.AddMinutes(-st.RemindMinutes) && now <= due.AddHours(1))
                {
                    string when = now < due ? "あと " + HoursText(due - now) + " (" + Fmt.Time(due) + ")" : Fmt.Time(due) + " になりました";
                    list.Add(new Notice { Key = key, Task = t, Title = t.Title, Text = "期限: " + when + (t.DurationMin > 0 ? "  ・  " + Fmt.Minutes(t.DurationMin) : "") });
                }
            }
            return list;
        }

        /// <summary>古いお知らせの印を消す (1 か月より前・もう無いタスク)</summary>
        public static void PruneNotified(TaskData data, DateTime now)
        {
            var keep = new List<string>();
            foreach (var k in data.Notified)
            {
                var parts = k.Split('|');
                if (parts.Length != 2) continue;
                if (parts[0] == "morning") { var d = Fmt.ParseIso(parts[1]); if (d.HasValue && d.Value > now.AddDays(-7)) keep.Add(k); continue; }
                var t = data.Find(parts[0]);
                if (t == null || t.Done || t.Due != parts[1]) continue;
                keep.Add(k);
            }
            data.Notified = keep;
        }

        // ------------------------------------------------------------ 集計

        public class Stats
        {
            public int Open, Overdue, DoneToday, DoneWeek, DoneMonth;
            /// <summary>今週が期限のもののうち終わった割合 (0〜1。今週のものが無ければ -1)</summary>
            public double WeekRate = -1;
            public int WeekTotal, WeekDone;
            public List<KeyValuePair<DateTime, int>> Daily = new List<KeyValuePair<DateTime, int>>();
            /// <summary>タグごとの (完了, 全部)</summary>
            public List<Tuple<string, int, int>> ByTag = new List<Tuple<string, int, int>>();
        }

        public static Stats Compute(IEnumerable<TaskItem> all, DateTime now, int days = 14)
        {
            var tasks = all.ToList();
            var st = new Stats();
            var today = now.Date;
            var week = WeekStart(today);
            var month = new DateTime(today.Year, today.Month, 1);
            st.Open = tasks.Count(t => !t.Done);
            st.Overdue = tasks.Count(t => IsOverdue(t, now));
            Func<TaskItem, DateTime?> doneDay = t => t.Done && t.DoneTime.HasValue ? t.DoneTime.Value.Date : (DateTime?)null;
            st.DoneToday = tasks.Count(t => doneDay(t) == today);
            st.DoneWeek = tasks.Count(t => doneDay(t) >= week && doneDay(t) <= today);
            st.DoneMonth = tasks.Count(t => doneDay(t) >= month && doneDay(t) <= today);
            var inWeek = tasks.Where(t => t.HasDue && t.DueDate.Value >= week && t.DueDate.Value < week.AddDays(7)).ToList();
            st.WeekTotal = inWeek.Count;
            st.WeekDone = inWeek.Count(t => t.Done);
            if (st.WeekTotal > 0) st.WeekRate = (double)st.WeekDone / st.WeekTotal;
            for (int i = days - 1; i >= 0; i--)
            {
                var d = today.AddDays(-i);
                st.Daily.Add(new KeyValuePair<DateTime, int>(d, tasks.Count(t => doneDay(t) == d)));
            }
            var tags = tasks.SelectMany(t => t.Tags).Distinct().ToList();
            foreach (var tag in tags)
            {
                var with = tasks.Where(t => t.Tags.Contains(tag)).ToList();
                st.ByTag.Add(Tuple.Create(tag, with.Count(t => t.Done), with.Count));
            }
            st.ByTag = st.ByTag.OrderByDescending(x => x.Item3).ThenBy(x => x.Item1, StringComparer.CurrentCulture).ToList();
            return st;
        }

        public static List<string> AllTags(IEnumerable<TaskItem> tasks)
        {
            return tasks.SelectMany(t => t.Tags).Distinct().OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>"仕事, #家  買い物" → [仕事, 家, 買い物]</summary>
        public static List<string> ParseTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text.Split(new[] { ' ', '　', ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.TrimStart('#', '＃').Trim()).Where(x => x.Length > 0).Distinct().ToList();
        }

        /// <summary>期限の表示 ("今日 15:00" / "10/30(金)" / "期限なし")</summary>
        public static string DueText(TaskItem t, DateTime now)
        {
            if (!t.HasDue) return "期限なし";
            var d = t.DueAt.Value;
            int n = (int)(d.Date - now.Date).TotalDays;
            string day = n >= -1 && n <= 2 ? Fmt.Relative(d, now) + " " + Fmt.MonthDay(d) : (d.Year != now.Year ? Fmt.Date(d) : Fmt.MonthDay(d));
            return day + (t.HasTime ? " " + Fmt.Time(d) : "");
        }

        // ------------------------------------------------------------ CSV と見本

        public static string ToCsv(IEnumerable<TaskItem> tasks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("タイトル,期限,時刻,優先度,かかる時間(分),タグ,くり返し,完了,完了日時,小タスク,メモ");
            foreach (var t in tasks.OrderBy(x => x.Done).ThenBy(x => x.Due ?? "9999"))
            {
                sb.AppendLine(string.Join(",", new[] {
                    Csv(t.Title),
                    t.HasDue ? Fmt.Iso(t.DueAt.Value) : "",
                    t.HasTime ? Fmt.Time(t.DueAt.Value) : "",
                    PriorityLabel(t.Priority),
                    t.DurationMin > 0 ? t.DurationMin.ToString(CultureInfo.InvariantCulture) : "",
                    Csv(string.Join(" ", t.Tags)),
                    RepeatLabel(t.Repeat),
                    t.Done ? "済" : "",
                    t.DoneTime.HasValue ? t.DoneTime.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
                    Csv(string.Join(" / ", t.Subtasks.Select(s => (s.Done ? "[済] " : "") + s.Text))),
                    Csv(t.Memo ?? "") }));
            }
            return sb.ToString();
        }

        static string Csv(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>見本データ (今日を基準に、期限切れ・今日・今週・来週・完了済みが混ざるように)</summary>
        public static List<TaskItem> SampleData(DateTime now)
        {
            var today = now.Date;
            var list = new List<TaskItem>();
            Func<string, DateTime?, bool, int, int, string, TaskItem> add = (title, due, time, pri, min, tags) =>
            {
                var t = new TaskItem { Title = title, Priority = pri, DurationMin = min, Tags = ParseTags(tags), Created = Fmt.IsoTime(now.AddDays(-7)) };
                t.SetDue(due, time);
                list.Add(t);
                return t;
            };
            var wk = WeekStart(today);
            add("請求書の支払い", today.AddDays(-1), false, TaskItem.High, 15, "家");
            add("企画書を仕上げる", today.AddHours(17), true, TaskItem.High, 120, "仕事").Subtasks.AddRange(new[] {
                new SubTask { Text = "目次を決める", Done = true }, new SubTask { Text = "数字を入れる" }, new SubTask { Text = "見直し" } });
            add("歯医者", today.AddHours(19), true, TaskItem.Mid, 60, "病院");
            add("牛乳と卵を買う", today, false, TaskItem.Low, 15, "買い物");
            var mail = add("メールの返信", today.AddHours(10), true, TaskItem.Mid, 30, "仕事");
            mail.Repeat = Repeat.Daily;
            add("ジムに行く", today.AddDays(1).AddHours(7), true, TaskItem.Low, 60, "健康").Repeat = Repeat.Weekly;
            add("週次ミーティングの資料", wk.AddDays(7 + 1).AddHours(14), true, TaskItem.Mid, 45, "仕事");
            add("部屋の掃除", today.AddDays(2), false, TaskItem.Low, 30, "家");
            add("友だちの誕生日プレゼント", today.AddDays(4), false, TaskItem.High, 0, "買い物");
            add("確定申告の書類をそろえる", today.AddDays(9), false, TaskItem.Mid, 90, "お金").Memo = "源泉徴収票・医療費の領収書";
            add("本を返す", today.AddDays(3).AddHours(18).AddMinutes(30), true, TaskItem.Low, 15, "");
            add("読みたい本のリストを作る", null, false, TaskItem.Low, 0, "");
            // 完了済み (統計用)
            string[] doneTitles = { "ゴミ出し", "洗濯", "見積書を送る", "日報", "クリーニングを受け取る", "電気代を払う", "ストレッチ", "日報", "資料の印刷", "ランニング" };
            string[] doneTags = { "家", "家", "仕事", "仕事", "家", "お金", "健康", "仕事", "仕事", "健康" };
            int[] doneAgo = { 0, 1, 1, 2, 4, 4, 5, 7, 8, 10 };
            for (int i = 0; i < doneTitles.Length; i++)
            {
                var day = today.AddDays(-doneAgo[i]);
                var t = add(doneTitles[i], day, false, i % 3, 15 * (1 + i % 3), doneTags[i]);
                t.Done = true; t.DoneAt = Fmt.IsoTime(day.AddHours(9 + i % 10));
            }
            return list;
        }
    }
}
