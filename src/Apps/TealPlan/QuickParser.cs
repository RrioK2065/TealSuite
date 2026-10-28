// Teal Plan - 日本語のひとこと入力を読み取る ("明日15時 歯医者 #病院" → 期限・時刻・タグ・タイトル)
// 読み取れる言い方 (全角数字も可):
//   日付: 今日 / 本日 / 明日 / あした / 明後日 / あさって / N日後 / 今週・来週・再来週 + X曜(日) / X曜(日) / M/D / M月D日 / 今夜・今晩
//   時刻: H時 / H時半 / H時M分 / H:MM / 午前・午後・朝・夜・夕方 + 時刻   (前に何も付かない 1〜6 時は午後とみなす)
//   かかる時間: N分 / N時間 / N時間半 / N時間M分 / N.5時間
//   優先度: 重要 / 急ぎ / 至急 / 大事 → 高、 優先度高・中・低 / いつか / 余裕があれば → 低
//   くり返し: 毎日 / 毎週 / 毎月 (毎週X曜 なら最初の回は次の X 曜日)
//   タグ: #仕事 / ＃家
//   予定 (1.10.0): 時刻の範囲 15:00-16:00 / 15時〜16時 / 9時から17時まで、 場所 @駅前 / ＠駅前、
//                  日付の範囲 11/3-11/5 / 11/3〜5 / 明日から2日間、 終日、 先頭の「予定」
//                  → どれかがあれば予定 (IsEvent)。無ければ今までどおりタスク
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public class ParseResult
    {
        public string Title = "";
        public DateTime? Date;
        /// <summary>時刻 (Date に足す)。null = 日付だけ</summary>
        public TimeSpan? Time;
        public int? Priority;
        public int DurationMin;
        public Repeat Repeat = Repeat.None;
        public List<string> Tags = new List<string>();
        /// <summary>1.10.0: 終わりの時刻 (Date に足す。日をまたぐと 24 時間以上)</summary>
        public TimeSpan? EndTime;
        /// <summary>1.10.0: 終わりの日 (その日を含む)</summary>
        public DateTime? EndDate;
        public string Location;
        /// <summary>「終日」と書いてあった</summary>
        public bool AllDay;
        /// <summary>予定として入れる (時刻の範囲・場所・日付の範囲・終日・先頭の「予定」のどれかがある)</summary>
        public bool IsEvent;

        public bool HasTime { get { return Time.HasValue; } }
        public DateTime? Due { get { return Date.HasValue ? Date.Value.Date + (Time ?? TimeSpan.Zero) : (DateTime?)null; } }

        public TaskItem ToTask(DateTime now)
        {
            var t = new TaskItem { Title = Title, Priority = Priority ?? TaskItem.Mid, DurationMin = DurationMin, Tags = new List<string>(Tags), Repeat = Repeat, Created = Fmt.IsoTime(now) };
            t.SetDue(Due, HasTime);
            return t;
        }

        /// <summary>予定として作る (日付が無ければ今日。時刻の終わりが無ければ「かかる時間」か 1 時間)</summary>
        public EventItem ToEvent(DateTime now, int remindMin = 10)
        {
            var e = new EventItem { Title = Title, Location = Location, Tags = new List<string>(Tags), Repeat = Repeat, Created = Fmt.IsoTime(now) };
            var day = (Date ?? now.Date).Date;
            if (Time.HasValue && !AllDay)
            {
                var start = day + Time.Value;
                DateTime end;
                if (EndDate.HasValue) end = EndDate.Value.Date + (EndTime ?? Time.Value);
                else if (EndTime.HasValue) end = day + EndTime.Value;
                else end = start.AddMinutes(DurationMin > 0 ? DurationMin : 60);
                if (end <= start) end = start.AddHours(1);
                e.SetTimes(start, end, false);
                e.RemindMin = remindMin;
            }
            else e.SetTimes(day, EndDate ?? day, true);
            return e;
        }

        /// <summary>読み取った内容の説明 (入力欄の下に出す)</summary>
        public string Describe(DateTime now)
        {
            if (IsEvent)
            {
                var ev = ToEvent(now);
                var it = EventLogic.ToItem(ev, ev.StartAt);
                var ps = new List<string>();
                ps.Add(Fmt.Relative(ev.StartAt.Date, now) + " " + (ev.AllDay ? EventLogic.WhenText(ev) : Fmt.MonthDay(ev.StartAt) + " " + EventLogic.TimeText(it)));
                if (!string.IsNullOrEmpty(Location)) ps.Add("@" + Location);
                if (Repeat != Repeat.None) ps.Add(TaskLogic.RepeatLabel(Repeat));
                foreach (var tg in Tags) ps.Add("#" + tg);
                return "予定「" + (Title.Length > 0 ? Title : "(タイトルなし)") + "」  " + string.Join("  ・  ", ps);
            }
            var parts = new List<string>();
            if (Date.HasValue) parts.Add("期限 " + Fmt.Relative(Date.Value, now) + " " + Fmt.MonthDay(Date.Value) + (HasTime ? " " + Fmt.Time(Due.Value) : ""));
            else parts.Add("期限なし");
            if (Priority.HasValue) parts.Add("優先度 " + TaskLogic.PriorityLabel(Priority.Value));
            if (DurationMin > 0) parts.Add(Fmt.Minutes(DurationMin));
            if (Repeat != Repeat.None) parts.Add(TaskLogic.RepeatLabel(Repeat));
            foreach (var tg in Tags) parts.Add("#" + tg);
            return "「" + (Title.Length > 0 ? Title : "(タイトルなし)") + "」  " + string.Join("  ・  ", parts);
        }
    }

    public static class QuickParser
    {
        const string Dows = "日月火水木金土";
        const RegexOptions O = RegexOptions.CultureInvariant;

        public static ParseResult Parse(string input, DateTime now)
        {
            var r = new ParseResult();
            if (string.IsNullOrWhiteSpace(input)) return r;
            string s = " " + Normalize(input) + " ";
            var today = now.Date;
            Match m;

            // 予定の印 (先頭の「予定」・終日・場所)
            s = Take(s, @"^\s予定(?::|\s)", x => r.IsEvent = true);
            s = Take(s, @"終日", x => r.AllDay = true);
            s = Take(s, @"(?<=\s)@([^\s#@]+)", x =>
            {
                var place = Regex.Replace(x.Groups[1].Value, "(にて|で)$", "", O);
                if (place.Length > 0) r.Location = place;
            });

            // タグ
            s = Take(s, @"#([^\s#]+)", x => r.Tags.Add(x.Groups[1].Value));
            r.Tags = r.Tags.Distinct().ToList();

            // 優先度
            s = Take(s, @"優先度\s*[:：は]?\s*(高|中|低)", x => r.Priority = x.Groups[1].Value == "高" ? TaskItem.High : x.Groups[1].Value == "中" ? TaskItem.Mid : TaskItem.Low);
            s = Take(s, @"(重要|至急|急ぎ|大至急|大事)", x => r.Priority = TaskItem.High);
            s = Take(s, @"(いつか|余裕があれば|ひまなとき|暇なとき)", x => { if (!r.Priority.HasValue) r.Priority = TaskItem.Low; });

            // くり返し
            s = Take(s, @"(毎平日|平日は?毎日|平日)", x => r.Repeat = Repeat.Weekdays);
            s = Take(s, @"(毎年|年に一度|年1回)", x => r.Repeat = Repeat.Yearly);
            s = Take(s, @"毎日", x => r.Repeat = Repeat.Daily);
            s = Take(s, @"毎月", x => r.Repeat = Repeat.Monthly);
            s = Take(s, @"毎週", x => r.Repeat = Repeat.Weekly);

            // かかる時間 (「15時」の「時」と間違えないよう「時間」を先に)
            s = Take(s, @"(?:約|およそ|〜|~)?(\d+(?:\.\d+)?)時間(半|(\d{1,2})分)?(?:くらい|ほど|程度|かかる)?", x =>
            {
                double h = double.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture);
                int min = (int)Math.Round(h * 60);
                if (x.Groups[2].Value == "半") min += 30;
                else if (x.Groups[3].Success) min += int.Parse(x.Groups[3].Value);
                r.DurationMin = min;
            });

            // 時刻の範囲 (15:00-16:00 / 15時〜16時半 / 9時から17時まで / 15-16時)
            const string T = @"(午前|午後|朝|昼|夕方|夜)?\s*(\d{1,2})(?::(\d{2})|(時)(?:(半)|(\d{1,2})分)?)?";
            m = Regex.Match(s, @"(?<![\d/:.])" + T + @"\s*(?:-|〜|~|から)\s*" + T + @"(?:まで)?(?![\d/]|間|日)", O);
            while (m.Success)
            {
                bool firstMarked = m.Groups[3].Success || m.Groups[4].Success, secondMarked = m.Groups[9].Success || m.Groups[10].Success;
                int h1 = int.Parse(m.Groups[2].Value), h2 = int.Parse(m.Groups[8].Value);
                if ((firstMarked || secondMarked) && h1 <= 24 && h2 <= 30)
                {
                    int m1 = m.Groups[5].Success ? 30 : m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : m.Groups[6].Success ? int.Parse(m.Groups[6].Value) : 0;
                    int m2 = m.Groups[11].Success ? 30 : m.Groups[9].Success ? int.Parse(m.Groups[9].Value) : m.Groups[12].Success ? int.Parse(m.Groups[12].Value) : 0;
                    if (m1 > 59) m1 = 0;
                    if (m2 > 59) m2 = 0;
                    var start = new TimeSpan(Hour24(h1, m.Groups[1].Value), m1, 0);
                    string ap2 = m.Groups[7].Value;
                    TimeSpan end;
                    if (ap2.Length > 0) end = new TimeSpan(Hour24(h2, ap2), m2, 0);
                    else
                    {
                        // 前置きが無ければ、始まりより後になる一番近い時刻 (11時-1時 → 13:00、22:00-1:00 → 翌 1:00)
                        end = new TimeSpan(h2, m2, 0);
                        if (end <= start && h2 < 12) end = end.Add(TimeSpan.FromHours(12));
                        if (end <= start) end = new TimeSpan(h2, m2, 0).Add(TimeSpan.FromHours(24));
                    }
                    if (end <= start) end = end.Add(TimeSpan.FromHours(24));
                    r.Time = start; r.EndTime = end; r.IsEvent = true;
                    s = s.Remove(m.Index, m.Length).Insert(m.Index, Mark);
                    break;
                }
                m = m.NextMatch();
            }

            // 時刻
            int? hour = null, minute = null; string ampm = null;
            if (!r.Time.HasValue)
            {
            m = Regex.Match(s, @"(午前|午後|朝|昼|夕方|夜|今夜|今晩)?\s*(\d{1,2})[:時](半|(\d{1,2})分?)?(?:から|に|〜|~)?", O);
            while (m.Success)
            {
                // "2時間" は上で取っているので、ここに来る "時" は時刻
                int h = int.Parse(m.Groups[2].Value);
                bool colon = s.Substring(m.Groups[2].Index + m.Groups[2].Length, 1) == ":";
                if (colon && !m.Groups[4].Success) { m = m.NextMatch(); continue; } // "10:" だけは無視
                if (h <= 24)
                {
                    hour = h;
                    minute = m.Groups[3].Value == "半" ? 30 : m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
                    ampm = m.Groups[1].Value;
                    if (minute > 59) minute = 0;
                    s = s.Remove(m.Index, m.Length).Insert(m.Index, Mark);
                    if (ampm == "今夜" || ampm == "今晩") s = s.Insert(m.Index, " 今日 ");
                    break;
                }
                m = m.NextMatch();
            }
            }
            if (hour.HasValue) r.Time = new TimeSpan(Hour24(hour.Value, ampm), minute ?? 0, 0);

            // かかる時間 (分)
            s = Take(s, @"(?:約|およそ|〜|~)?(\d+)分(?:間)?(?:くらい|ほど|程度|かかる)?", x => r.DurationMin += int.Parse(x.Groups[1].Value));

            // 日付
            DateTime? date = null;
            s = Take(s, @"(今日|本日|きょう)", x => date = today);
            s = Take(s, @"(今夜|今晩)", x => { date = today; if (!r.Time.HasValue) r.Time = new TimeSpan(20, 0, 0); });
            s = Take(s, @"(明後日|あさって)", x => date = today.AddDays(2));
            s = Take(s, @"(明日|あした|あす)", x => date = today.AddDays(1));
            s = Take(s, @"(\d+)日後", x => date = today.AddDays(int.Parse(x.Groups[1].Value)));
            int days = 0;
            s = Take(s, @"(\d+)日間", x => { days = int.Parse(x.Groups[1].Value); r.IsEvent = true; });
            s = Take(s, @"(\d+)週間後", x => date = today.AddDays(7 * int.Parse(x.Groups[1].Value)));
            s = Take(s, @"(今週|来週|再来週)?\s*の?\s*([日月火水木金土])曜(?:日)?", x =>
            {
                int dow = Dows.IndexOf(x.Groups[2].Value, StringComparison.Ordinal);
                int monIndex = (dow + 6) % 7; // 月=0
                string w = x.Groups[1].Value;
                if (w.Length > 0)
                {
                    var ws = TaskLogic.WeekStart(today).AddDays(w == "来週" ? 7 : w == "再来週" ? 14 : 0);
                    date = ws.AddDays(monIndex);
                }
                else
                {
                    int diff = (dow - (int)today.DayOfWeek + 7) % 7;
                    date = today.AddDays(diff); // 今日がその曜日なら今日
                }
            });
            // 日付の範囲 (11/3-11/5 / 11/3〜5 / 11月3日から5日まで)
            s = Take(s, @"(?<![\d/])(?:(\d{4})[/年])?(\d{1,2})[/月](\d{1,2})日?(?:\([日月火水木金土]\))?\s*(?:-|〜|~|から)\s*(?:(\d{1,2})[/月])?(\d{1,2})日?(?:\([日月火水木金土]\))?(?:まで)?(?![\d/:時])", x =>
            {
                int y = x.Groups[1].Success ? int.Parse(x.Groups[1].Value) : today.Year;
                int mo = int.Parse(x.Groups[2].Value), d = int.Parse(x.Groups[3].Value);
                int mo2 = x.Groups[4].Success ? int.Parse(x.Groups[4].Value) : mo, d2 = int.Parse(x.Groups[5].Value);
                if (mo < 1 || mo > 12 || mo2 < 1 || mo2 > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return;
                var a = new DateTime(y, mo, d);
                if (!x.Groups[1].Success && a < today.AddDays(-7)) a = a.AddYears(1);
                int y2 = a.Year;
                if (d2 < 1 || d2 > DateTime.DaysInMonth(y2, mo2)) return;
                var b = new DateTime(y2, mo2, d2);
                if (b < a) { b = b.AddYears(1); if (b > a.AddDays(366)) return; }
                date = a; r.EndDate = b; r.IsEvent = true;
            });
            s = Take(s, @"(?<![\d/])(?:(\d{4})[/年])?(\d{1,2})[/月](\d{1,2})日?(?![\d/])", x =>
            {
                int mo = int.Parse(x.Groups[2].Value), d = int.Parse(x.Groups[3].Value);
                int y = x.Groups[1].Success ? int.Parse(x.Groups[1].Value) : today.Year;
                if (mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return;
                var dt = new DateTime(y, mo, d);
                if (!x.Groups[1].Success && dt < today.AddDays(-7)) dt = dt.AddYears(1); // 少し前の日付は今年のまま (期限切れとして扱う)
                date = dt;
            });
            s = Take(s, @"(月末)", x => date = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)));
            if (!date.HasValue && r.Time.HasValue)
            {
                // 時刻だけ: 今日のその時刻。もう過ぎていれば明日
                date = today + r.Time.Value <= now ? today.AddDays(1) : today;
            }
            r.Date = date;
            if (days >= 1)
            {
                if (!r.Date.HasValue) r.Date = today;
                r.EndDate = r.Date.Value.AddDays(days - 1);
            }
            if (r.Location != null || r.AllDay) r.IsEvent = true;
            if (r.AllDay) { r.Time = null; r.EndTime = null; }
            if (r.IsEvent && !r.Date.HasValue) r.Date = today;
            if (r.Repeat != Repeat.None && !r.Date.HasValue) r.Date = today;
            // 平日のくり返しで、土日になったら次の月曜から
            if (r.Repeat == Repeat.Weekdays && r.Date.HasValue)
                while (r.Date.Value.DayOfWeek == DayOfWeek.Saturday || r.Date.Value.DayOfWeek == DayOfWeek.Sunday) r.Date = r.Date.Value.AddDays(1);

            r.Title = CleanTitle(s);
            return r;
        }

        /// <summary>午前・午後などの前置きから 24 時間制に (前置きの無い 1〜6 時は午後)</summary>
        static int Hour24(int h, string ampm)
        {
            bool pm = ampm == "午後" || ampm == "夕方" || ampm == "夜" || ampm == "今夜" || ampm == "今晩";
            if (pm && h < 12) h += 12;
            else if (ampm == "昼" && h < 6) h += 12;
            else if (ampm == "午前" && h == 12) h = 0;
            else if (string.IsNullOrEmpty(ampm) && h >= 1 && h <= 6) h += 12; // 「3時」はたいてい午後
            if (h == 24) h = 0;
            return h;
        }

        /// <summary>全角の数字・記号を半角に</summary>
        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= '０' && c <= '９') sb.Append((char)('0' + (c - '０')));
                else if (c == '：') sb.Append(':');
                else if (c == '／') sb.Append('/');
                else if (c == '＃') sb.Append('#');
                else if (c == '＠') sb.Append('@');
                else if (c == '～') sb.Append('〜');
                else if (c == '－' || c == '‐' || c == '–' || c == '−') sb.Append('-');
                else if (c == '．') sb.Append('.');
                else if (c == '　' || c == '\t' || c == '\r' || c == '\n') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string Take(string s, string pattern, Action<Match> onMatch)
        {
            var m = Regex.Match(s, pattern, O);
            if (!m.Success) return s;
            onMatch(m);
            // 取り除いた場所に印を置く (前後にくっついた助詞を後で落とすため)
            return s.Remove(m.Index, m.Length).Insert(m.Index, Mark);
        }

        const string Mark = "\u0001";
        // 読み取った部分を取り除いた後に残る助詞など
        static readonly string[] Particles = { "までに", "までには", "まで", "から", "には", "に", "の", "で", "は", "を", "、", "。", ",", "・", "〜", "~", "頃", "ごろ", "くらい", "ころ" };

        static string CleanTitle(string s)
        {
            // 読み取った部分の直後・直前にくっついた助詞を落とす (「明日の会議」→「会議」、「歯医者に15時」→「歯医者」)
            s = Regex.Replace(s, Mark + "(までには|までに|まで|から|には|に|の|で|は|、|。)", Mark, O);
            s = Regex.Replace(s, "(までに|まで|から|には|に|は|で|、)" + Mark, Mark, O);
            s = s.Replace(Mark, " ");
            var words = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            // 助詞だけの単語は捨てる
            words = words.Where(w => !Particles.Contains(w)).ToList();
            return string.Join(" ", words).Trim();
        }
    }
}
