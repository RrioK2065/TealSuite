// Teal Tasks - 日本語のひとこと入力を読み取る ("明日15時 歯医者 #病院" → 期限・時刻・タグ・タイトル)
// 読み取れる言い方 (全角数字も可):
//   日付: 今日 / 本日 / 明日 / あした / 明後日 / あさって / N日後 / 今週・来週・再来週 + X曜(日) / X曜(日) / M/D / M月D日 / 今夜・今晩
//   時刻: H時 / H時半 / H時M分 / H:MM / 午前・午後・朝・夜・夕方 + 時刻   (前に何も付かない 1〜6 時は午後とみなす)
//   かかる時間: N分 / N時間 / N時間半 / N時間M分 / N.5時間
//   優先度: 重要 / 急ぎ / 至急 / 大事 → 高、 優先度高・中・低 / いつか / 余裕があれば → 低
//   くり返し: 毎日 / 毎週 / 毎月 (毎週X曜 なら最初の回は次の X 曜日)
//   タグ: #仕事 / ＃家
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

        public bool HasTime { get { return Time.HasValue; } }
        public DateTime? Due { get { return Date.HasValue ? Date.Value.Date + (Time ?? TimeSpan.Zero) : (DateTime?)null; } }

        public TaskItem ToTask(DateTime now)
        {
            var t = new TaskItem { Title = Title, Priority = Priority ?? TaskItem.Mid, DurationMin = DurationMin, Tags = new List<string>(Tags), Repeat = Repeat, Created = Fmt.IsoTime(now) };
            t.SetDue(Due, HasTime);
            return t;
        }

        /// <summary>読み取った内容の説明 (入力欄の下に出す)</summary>
        public string Describe(DateTime now)
        {
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

            // タグ
            s = Take(s, @"#([^\s#]+)", x => r.Tags.Add(x.Groups[1].Value));
            r.Tags = r.Tags.Distinct().ToList();

            // 優先度
            s = Take(s, @"優先度\s*[:：は]?\s*(高|中|低)", x => r.Priority = x.Groups[1].Value == "高" ? TaskItem.High : x.Groups[1].Value == "中" ? TaskItem.Mid : TaskItem.Low);
            s = Take(s, @"(重要|至急|急ぎ|大至急|大事)", x => r.Priority = TaskItem.High);
            s = Take(s, @"(いつか|余裕があれば|ひまなとき|暇なとき)", x => { if (!r.Priority.HasValue) r.Priority = TaskItem.Low; });

            // くり返し
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

            // 時刻
            int? hour = null, minute = null; string ampm = null;
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
            if (hour.HasValue)
            {
                int h = hour.Value;
                bool pm = ampm == "午後" || ampm == "夕方" || ampm == "夜" || ampm == "今夜" || ampm == "今晩";
                if (pm && h < 12) h += 12;
                else if (ampm == "昼" && h < 6) h += 12;
                else if (ampm == "午前" && h == 12) h = 0;
                else if (string.IsNullOrEmpty(ampm) && h >= 1 && h <= 6) h += 12; // 「3時」はたいてい午後
                if (h == 24) h = 0;
                r.Time = new TimeSpan(h, minute ?? 0, 0);
            }

            // かかる時間 (分)
            s = Take(s, @"(?:約|およそ|〜|~)?(\d+)分(?:間)?(?:くらい|ほど|程度|かかる)?", x => r.DurationMin += int.Parse(x.Groups[1].Value));

            // 日付
            DateTime? date = null;
            s = Take(s, @"(今日|本日|きょう)", x => date = today);
            s = Take(s, @"(今夜|今晩)", x => { date = today; if (!r.Time.HasValue) r.Time = new TimeSpan(20, 0, 0); });
            s = Take(s, @"(明後日|あさって)", x => date = today.AddDays(2));
            s = Take(s, @"(明日|あした|あす)", x => date = today.AddDays(1));
            s = Take(s, @"(\d+)日後", x => date = today.AddDays(int.Parse(x.Groups[1].Value)));
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
            if (r.Repeat != Repeat.None && !r.Date.HasValue) r.Date = today;

            r.Title = CleanTitle(s);
            return r;
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
