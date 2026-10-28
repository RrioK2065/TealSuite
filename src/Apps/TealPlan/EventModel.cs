// Teal Plan - 予定 (1.10.0) とカレンダーに並べる項目
//  予定 (EventItem) はタスクとは別のもの。始まりと終わりがあり、終日・複数日・くり返し・お知らせを持てる。
//  カレンダーと一覧は TaskItem / EventItem を直接ではなく CalItem に直してから描く。
//  ほかのアプリの項目 (例: Teal Wallet の支払日) も IPlanSource から CalItem で渡せば、読むだけの項目として同じように並ぶ。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    [DataContract]
    public class EventItem
    {
        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "title", Order = 2)] public string Title = "";
        /// <summary>始まり。終日なら yyyy-MM-dd、時刻つきなら yyyy-MM-ddTHH:mm</summary>
        [DataMember(Name = "start", Order = 3)] public string Start;
        /// <summary>終わり。終日なら最後の日 (その日を含む)、時刻つきなら終わる時刻</summary>
        [DataMember(Name = "end", Order = 4)] public string End;
        [DataMember(Name = "allDay", Order = 5)] public bool AllDay;
        [DataMember(Name = "location", Order = 6, EmitDefaultValue = false)] public string Location;
        [DataMember(Name = "memo", Order = 7, EmitDefaultValue = false)] public string Memo;
        /// <summary>色の名前 (EventLogic.ColorKeys)</summary>
        [DataMember(Name = "color", Order = 8)] public string Color = "teal";
        /// <summary>none / daily / weekly / monthly / weekdays / yearly (タスクと同じ)</summary>
        [DataMember(Name = "repeat", Order = 9)] public string RepeatText = "none";
        /// <summary>くり返しの最後の日 (yyyy-MM-dd)。なしは null</summary>
        [DataMember(Name = "until", Order = 10, EmitDefaultValue = false)] public string Until;
        /// <summary>何分前にお知らせするか。-1 = お知らせしない</summary>
        [DataMember(Name = "remind", Order = 11)] public int RemindMin = -1;
        [DataMember(Name = "tags", Order = 12)] public List<string> Tags = new List<string>();
        /// <summary>くり返しの中で消した回 (その回の始まりの日 yyyy-MM-dd)</summary>
        [DataMember(Name = "skip", Order = 13, EmitDefaultValue = false)] public List<string> Skip;
        [DataMember(Name = "created", Order = 14, EmitDefaultValue = false)] public string Created;

        [OnDeserializing] void Defaults(StreamingContext c) { RemindMin = -1; Color = "teal"; RepeatText = "none"; }

        [IgnoreDataMember]
        public Repeat Repeat
        {
            get { var t = new TaskItem { RepeatText = RepeatText }; return t.Repeat; }
            set { var t = new TaskItem { Repeat = value }; RepeatText = t.RepeatText; }
        }
        [IgnoreDataMember] public DateTime StartAt { get { return Fmt.ParseIso(Start) ?? DateTime.MinValue; } }
        /// <summary>終わり (この時刻を含まない)。終日なら最後の日の翌日 0:00</summary>
        [IgnoreDataMember]
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
        [IgnoreDataMember] public TimeSpan Length { get { return EndAt - StartAt; } }

        /// <summary>始まりと終わりを決める (終日なら日付だけ。lastDay はその日を含む最後の日)</summary>
        public void SetTimes(DateTime start, DateTime endOrLastDay, bool allDay)
        {
            AllDay = allDay;
            if (allDay)
            {
                Start = Fmt.Iso(start.Date);
                End = Fmt.Iso(endOrLastDay.Date < start.Date ? start.Date : endOrLastDay.Date);
            }
            else
            {
                if (endOrLastDay <= start) endOrLastDay = start.AddHours(1);
                Start = Fmt.IsoTime(start);
                End = Fmt.IsoTime(endOrLastDay);
            }
        }
        public EventItem Clone() { return DataStore.FromJson<EventItem>(DataStore.ToJson(this)); }
    }

    public enum CalKind { Task, Event, External }

    /// <summary>カレンダー・一覧に並べる 1 件 (タスク・予定・ほかのアプリの読むだけの項目)</summary>
    public class CalItem
    {
        public CalKind Kind;
        /// <summary>選択を覚えるための鍵 (t:id / e:id:始まり / x:出どころ:id)</summary>
        public string Key = "";
        public string Title = "";
        public DateTime Start;
        /// <summary>終わり (含まない)。終日なら最後の日の翌日 0:00</summary>
        public DateTime End;
        public bool AllDay;
        public Color Color = Theme.Accent;
        /// <summary>読むだけ (動かせない・編集できない)。ほかのアプリの項目は必ず true</summary>
        public bool ReadOnly;
        public bool Done;
        public bool Repeats;
        public string Location;
        /// <summary>出どころの名前 (例: "Teal Wallet")。自分の項目は null</summary>
        public string SourceName;
        /// <summary>札の頭に付ける短い印 (例: "¥")</summary>
        public string Badge;
        public int Priority = TaskItem.Mid;
        public TaskItem Task;
        public EventItem Event;
        /// <summary>ほかのアプリの項目の元データ (出どころが自由に使う)</summary>
        public object Tag;

        public bool HasTime { get { return !AllDay; } }
        public DateTime LastDay { get { var e = End; if (e > Start && e.TimeOfDay == TimeSpan.Zero) e = e.AddDays(-1); return (e < Start ? Start : e).Date; } }
        public bool MultiDay { get { return LastDay > Start.Date; } }
        public bool OnDay(DateTime d) { return Start.Date <= d.Date && d.Date <= LastDay; }
        public bool Overlaps(DateTime from, DateTime to) { return Start < to && (End > from || (End <= Start && Start >= from)); }
    }

    /// <summary>ほかのアプリからカレンダーに出す項目 (読むだけ)。例外を投げてもカレンダーは止めない</summary>
    public interface IPlanSource
    {
        string Name { get; }
        IEnumerable<CalItem> Items(DateTime from, DateTime to);
    }

    public static class EventLogic
    {
        public static readonly string[] ColorKeys = { "teal", "blue", "green", "orange", "red", "purple", "gray" };
        public static readonly string[] ColorLabels = { "ティール", "青", "緑", "オレンジ", "赤", "紫", "グレー" };
        static readonly Color[] ColorValues =
        {
            Theme.Accent, Color.FromArgb(0x2F, 0x6F, 0xC8), Color.FromArgb(0x2E, 0x8B, 0x4E), Color.FromArgb(0xE0, 0x7A, 0x1F),
            Color.FromArgb(0xD6, 0x40, 0x45), Color.FromArgb(0x7E, 0x57, 0xC2), Color.FromArgb(0x78, 0x86, 0x86),
        };
        public static readonly int[] RemindChoices = { -1, 0, 5, 10, 15, 30, 60, 120, 24 * 60 };
        public static string RemindLabel(int m)
        {
            if (m < 0) return "お知らせしない";
            if (m == 0) return "始まる時刻ちょうど";
            if (m == 24 * 60) return "1 日前";
            return Fmt.Minutes(m) + "前";
        }
        public static Color ColorOf(string key)
        {
            int i = Array.IndexOf(ColorKeys, key ?? "");
            return ColorValues[i < 0 ? 0 : i];
        }
        public static Color ColorAt(int i) { return ColorValues[Math.Max(0, Math.Min(ColorValues.Length - 1, i))]; }

        /// <summary>[from, to) に重なる回の始まり (くり返さない予定は 1 回だけ)。1.10.0: 計算は共通の Renewal.Occurrences (Teal Hub の「今日」と同じ)</summary>
        public static List<DateTime> Occurrences(EventItem e, DateTime from, DateTime to, int max = 400)
        {
            var skip = e.Skip;
            return Renewal.Occurrences(e.StartAt, e.Length, (RepeatKind)(int)e.Repeat, Fmt.ParseIso(e.Until), d => skip != null && skip.Contains(Fmt.Iso(d)), from, to, max);
        }

        /// <summary>予定の 1 回を CalItem に</summary>
        public static CalItem ToItem(EventItem e, DateTime occ)
        {
            return new CalItem
            {
                Kind = CalKind.Event, Key = "e:" + e.Id + ":" + Fmt.IsoTime(occ), Title = e.Title, Start = occ, End = occ + e.Length,
                AllDay = e.AllDay, Color = ColorOf(e.Color), Location = e.Location, Repeats = e.Repeat != Repeat.None, Event = e,
            };
        }

        /// <summary>期限のあるタスクを CalItem に (時刻つきは「かかる時間」ぶんの長さ、最低 30 分)</summary>
        public static CalItem ToItem(TaskItem t)
        {
            var s = t.DueAt.Value;
            return new CalItem
            {
                Kind = CalKind.Task, Key = "t:" + t.Id, Title = t.Title, Start = s, AllDay = !t.HasTime,
                End = t.HasTime ? s.AddMinutes(Math.Max(30, t.DurationMin)) : s.Date.AddDays(1),
                Done = t.Done, Priority = t.Priority, Task = t, Repeats = t.Repeat != Repeat.None,
                Color = t.Done ? Theme.Disabled : t.Priority == TaskItem.High ? Theme.Danger : t.Priority == TaskItem.Mid ? Theme.Accent : Theme.BorderStrong,
            };
        }

        /// <summary>読めなかった出どころの最後のエラー (画面には出さない。テスト用)</summary>
        public static string LastSourceError;
        public const int ShowAll = 0, ShowTasks = 1, ShowEvents = 2;

        /// <summary>[from, to) に重なる項目をすべて集める (show: 0 すべて / 1 タスクだけ / 2 予定だけ (ほかのアプリの項目も))</summary>
        public static List<CalItem> Collect(TaskData data, IEnumerable<IPlanSource> sources, DateTime from, DateTime to, int show = ShowAll)
        {
            var list = new List<CalItem>();
            if (show != ShowEvents)
                foreach (var t in data.Tasks)
                    if (t.HasDue) { var it = ToItem(t); if (it.Start < to && it.End > from) list.Add(it); }
            if (show != ShowTasks)
            {
                foreach (var e in data.Events)
                    foreach (var occ in Occurrences(e, from, to)) list.Add(ToItem(e, occ));
                if (sources != null)
                    foreach (var src in sources)
                    {
                        try
                        {
                            foreach (var x in src.Items(from, to) ?? new CalItem[0])
                            {
                                if (x == null || !x.Overlaps(from, to)) continue;
                                x.Kind = CalKind.External; x.ReadOnly = true;
                                if (x.SourceName == null) x.SourceName = src.Name;
                                if (!x.Key.StartsWith("x:")) x.Key = "x:" + src.Name + ":" + x.Key;
                                list.Add(x);
                            }
                        }
                        catch (Exception ex) { LastSourceError = src.Name + ": " + ex.Message; }
                    }
            }
            return Sort(list);
        }

        /// <summary>並べ方: 終日 (複数日を先) → 時刻の早い順 → 予定 → タスク</summary>
        public static List<CalItem> Sort(IEnumerable<CalItem> items)
        {
            return items.OrderBy(x => x.AllDay ? 0 : 1).ThenByDescending(x => x.AllDay && x.MultiDay)
                .ThenBy(x => x.Start).ThenBy(x => x.Kind == CalKind.Task ? 2 : x.Kind == CalKind.External ? 1 : 0)
                .ThenBy(x => x.Done).ThenByDescending(x => x.Priority).ThenBy(x => x.Title, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>時刻の書き方 (終日 / 15:00〜16:00 / 10/28 9:00〜10/29 18:00)</summary>
        public static string TimeText(CalItem x)
        {
            if (x.AllDay) return x.MultiDay ? Fmt.MonthDay(x.Start) + "〜" + Fmt.MonthDay(x.LastDay) : "終日";
            if (x.Kind == CalKind.Task) return Fmt.Time(x.Start);
            if (x.MultiDay) return Fmt.MonthDay(x.Start) + " " + Fmt.Time(x.Start) + "〜" + Fmt.MonthDay(x.LastDay) + " " + Fmt.Time(x.End);
            return Fmt.Time(x.Start) + "〜" + Fmt.Time(x.End);
        }

        public static string WhenText(EventItem e)
        {
            var it = ToItem(e, e.StartAt);
            if (e.AllDay) return Fmt.MonthDay(e.StartAt) + (it.MultiDay ? "〜" + Fmt.MonthDay(it.LastDay) : "") + " 終日";
            return Fmt.MonthDay(e.StartAt) + " " + TimeText(it);
        }

        /// <summary>その日の 1 回のお知らせ (お知らせ時刻〜始まりの 1 時間後まで)</summary>
        public static List<Notice> DueNotices(TaskData data, DateTime now)
        {
            var list = new List<Notice>();
            if (!data.Settings.Remind) return list;
            foreach (var e in data.Events)
            {
                if (e.RemindMin < 0) continue;
                foreach (var occ in Occurrences(e, now.AddHours(-1).AddMinutes(-1), now.AddMinutes(e.RemindMin + 1)))
                {
                    // 終日の予定は前の日の夜にならないよう、その日の 9:00 を始まりとみなす
                    var at = e.AllDay ? occ.Date.AddHours(9) : occ;
                    string key = "ev:" + e.Id + "|" + Fmt.IsoTime(occ);
                    if (data.Notified.Contains(key)) continue;
                    if (now < at.AddMinutes(-e.RemindMin) || now > at.AddHours(1)) continue;
                    var it = ToItem(e, occ);
                    string when = e.AllDay ? "今日 " + TimeText(it) : now < occ ? "あと " + Span(occ - now) + " (" + TimeText(it) + ")" : TimeText(it) + " (始まりました)";
                    list.Add(new Notice { Key = key, Event = e, Title = e.Title, Text = "予定: " + when + (string.IsNullOrEmpty(e.Location) ? "" : "  ・  @" + e.Location) });
                }
            }
            return list;
        }

        static string Span(TimeSpan t)
        {
            int m = (int)Math.Ceiling(t.TotalMinutes);
            if (m >= 24 * 60) return (m / (24 * 60)) + " 日";
            return m >= 60 ? Fmt.Minutes(m) : m + " 分";
        }

        /// <summary>予定の印を残すか (もう無い予定・2 日より前の回は消す)</summary>
        public static bool KeepNotified(TaskData data, string id, string occ, DateTime now)
        {
            var e = data.Events.FirstOrDefault(x => x.Id == id);
            var d = Fmt.ParseIso(occ);
            return e != null && d.HasValue && d.Value > now.AddDays(-2);
        }

        public static List<EventItem> SampleData(DateTime now)
        {
            var today = now.Date;
            var ws = TaskLogic.WeekStart(today);
            var list = new List<EventItem>();
            Func<string, DateTime, int, string, string, EventItem> Add = (title, start, minutes, color, place) =>
            {
                var e = new EventItem { Title = title, Color = color, Location = place, RemindMin = 10, Created = Fmt.IsoTime(now) };
                e.SetTimes(start, start.AddMinutes(minutes), false);
                list.Add(e);
                return e;
            };
            Add("眼科の検査", today.AddDays(1).AddHours(15), 60, "teal", "駅前クリニック");
            Add("チーム定例", ws.AddHours(10), 60, "blue", "会議室 A").Repeat = Repeat.Weekly;
            Add("ランチ (佐藤さん)", today.AddHours(12), 60, "orange", "カフェ ミモザ");
            Add("ヨガ", ws.AddDays(2).AddHours(19), 90, "green", "スタジオ");
            Add("企画レビュー", ws.AddDays(3).AddHours(14), 90, "purple", null);
            Add("美容院", ws.AddDays(5).AddHours(11), 90, "red", null);
            var trip = new EventItem { Title = "出張 (大阪)", Color = "gray", Created = Fmt.IsoTime(now) };
            trip.SetTimes(ws.AddDays(10), ws.AddDays(11), true);
            list.Add(trip);
            var bd = new EventItem { Title = "母の誕生日", Color = "red", RemindMin = 24 * 60, Created = Fmt.IsoTime(now) };
            bd.SetTimes(today.AddDays(9), today.AddDays(9), true);
            bd.Repeat = Repeat.Yearly;
            list.Add(bd);
            return list;
        }

        public static string ToCsvLine(EventItem e)
        {
            Func<string, string> q = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            return string.Join(",", new[] { q("予定"), q(e.Title), q(e.Start), q(e.End), q(e.AllDay ? "終日" : ""), q(e.Location), q(string.Join(" ", e.Tags)), q(TaskLogic.RepeatLabel(e.Repeat)), q(e.Memo) });
        }
    }
}
