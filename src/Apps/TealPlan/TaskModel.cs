// Teal Plan - データの形と保存 (%APPDATA%\TealSuite\TealPlan\tasks.json。1.9.x までは TealTasks)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public enum Repeat { None, Daily, Weekly, Monthly, Weekdays, Yearly }

    [DataContract]
    public class SubTask
    {
        [DataMember(Name = "text", Order = 1)] public string Text = "";
        [DataMember(Name = "done", Order = 2)] public bool Done;
    }

    [DataContract]
    public class TaskItem
    {
        public const int Low = 0, Mid = 1, High = 2;

        [DataMember(Name = "id", Order = 1)] public string Id = Guid.NewGuid().ToString("N");
        [DataMember(Name = "title", Order = 2)] public string Title = "";
        /// <summary>期限。日付だけなら yyyy-MM-dd、時刻つきなら yyyy-MM-ddTHH:mm。なしは null</summary>
        [DataMember(Name = "due", Order = 3, EmitDefaultValue = false)] public string Due;
        /// <summary>0 = 低 / 1 = 中 / 2 = 高</summary>
        [DataMember(Name = "priority", Order = 4)] public int Priority = Mid;
        /// <summary>かかる時間 (分)。0 = 決めていない</summary>
        [DataMember(Name = "minutes", Order = 5, EmitDefaultValue = false)] public int DurationMin;
        [DataMember(Name = "tags", Order = 6)] public List<string> Tags = new List<string>();
        [DataMember(Name = "memo", Order = 7, EmitDefaultValue = false)] public string Memo;
        /// <summary>none / daily / weekly / monthly / weekdays (1.8.0) / yearly (1.8.0)</summary>
        [DataMember(Name = "repeat", Order = 8)] public string RepeatText = "none";
        [DataMember(Name = "done", Order = 9)] public bool Done;
        [DataMember(Name = "doneAt", Order = 10, EmitDefaultValue = false)] public string DoneAt;
        [DataMember(Name = "created", Order = 11, EmitDefaultValue = false)] public string Created;
        [DataMember(Name = "subtasks", Order = 12)] public List<SubTask> Subtasks = new List<SubTask>();
        /// <summary>繰り返しで完了したときに作った次の回の id (完了を取り消したら消すため)</summary>
        [DataMember(Name = "next", Order = 13, EmitDefaultValue = false)] public string NextId;

        [IgnoreDataMember]
        public Repeat Repeat
        {
            get { switch (RepeatText) { case "daily": return Repeat.Daily; case "weekly": return Repeat.Weekly; case "monthly": return Repeat.Monthly; case "weekdays": return Repeat.Weekdays; case "yearly": return Repeat.Yearly; default: return Repeat.None; } }
            set { RepeatText = value == Repeat.Daily ? "daily" : value == Repeat.Weekly ? "weekly" : value == Repeat.Monthly ? "monthly" : value == Repeat.Weekdays ? "weekdays" : value == Repeat.Yearly ? "yearly" : "none"; }
        }
        [IgnoreDataMember] public bool HasDue { get { return DueAt.HasValue; } }
        /// <summary>時刻まで決まっているか</summary>
        [IgnoreDataMember] public bool HasTime { get { return HasDue && Due.Length > 10; } }
        /// <summary>期限 (日付だけのときは 0:00)</summary>
        [IgnoreDataMember] public DateTime? DueAt { get { return Fmt.ParseIso(Due); } }
        [IgnoreDataMember] public DateTime? DueDate { get { var d = DueAt; return d.HasValue ? d.Value.Date : (DateTime?)null; } }
        [IgnoreDataMember] public DateTime? DoneTime { get { return Fmt.ParseIso(DoneAt); } }

        public void SetDue(DateTime? day, bool withTime)
        {
            if (!day.HasValue) { Due = null; return; }
            Due = withTime ? Fmt.IsoTime(day.Value) : Fmt.Iso(day.Value);
        }
        public TaskItem Clone() { return DataStore.FromJson<TaskItem>(DataStore.ToJson(this)); }
    }

    [DataContract]
    public class TaskSettings
    {
        [DataMember(Name = "remind", Order = 1)] public bool Remind = true;
        /// <summary>時刻つきの期限の何分前にお知らせするか</summary>
        [DataMember(Name = "remindMinutes", Order = 2)] public int RemindMinutes = 15;
        /// <summary>その日最初に「今日のタスク N 件」をお知らせする</summary>
        [DataMember(Name = "morning", Order = 3)] public bool Morning = true;
        /// <summary>1.10.0: 一覧で見るもの (0 タスク / 1 予定 / 2 すべて)</summary>
        [DataMember(Name = "listKind", Order = 4, EmitDefaultValue = false)] public int ListKind;
        /// <summary>1.10.0: 予定の一覧の範囲 (MainForm.AgendaRanges の番号)</summary>
        [DataMember(Name = "agendaRange", Order = 5, EmitDefaultValue = false)] public int AgendaRange;
        /// <summary>1.10.0: カレンダーに出すもの (0 すべて / 1 タスクだけ / 2 予定だけ)</summary>
        [DataMember(Name = "calShow", Order = 6, EmitDefaultValue = false)] public int CalShow;
        /// <summary>1.10.0: 新しい予定のお知らせ (何分前。-1 = しない)</summary>
        [DataMember(Name = "eventRemind", Order = 7)] public int EventRemind = 10;
        /// <summary>1.10.0: Teal Wallet のサブスクの支払い日をカレンダーに出す</summary>
        [DataMember(Name = "showWallet", Order = 8)] public bool ShowWallet = true;
        [OnDeserializing] void Defaults(StreamingContext c) { Remind = true; RemindMinutes = 15; Morning = true; EventRemind = 10; ShowWallet = true; }
    }

    [DataContract]
    public class TaskData
    {
        public const string FileName = "tasks.json";
        [DataMember(Name = "version", Order = 1)] public int Version = CurrentVersion;
        /// <summary>2 = 1.10.0 (予定と、小タスクを開いているタスクを追加)</summary>
        public const int CurrentVersion = 2;
        [DataMember(Name = "tasks", Order = 2)] public List<TaskItem> Tasks = new List<TaskItem>();
        [DataMember(Name = "settings", Order = 3)] public TaskSettings Settings = new TaskSettings();
        /// <summary>お知らせ済みの印。同じお知らせを何度も出さない</summary>
        [DataMember(Name = "notified", Order = 4)] public List<string> Notified = new List<string>();
        /// <summary>1.8.0: よく使うタスクのひな形</summary>
        [DataMember(Name = "templates", Order = 5, EmitDefaultValue = false)] public List<TaskTemplate> Templates;

        /// <summary>1.10.0: 予定 (タスクとは別)</summary>
        [DataMember(Name = "events", Order = 6, EmitDefaultValue = false)] public List<EventItem> Events = new List<EventItem>();
        /// <summary>1.10.0: 一覧で小タスクを開いているタスクの id (元に戻すの対象外)</summary>
        [DataMember(Name = "expanded", Order = 7, EmitDefaultValue = false)] public List<string> Expanded = new List<string>();

        public TaskItem Find(string id) { return Tasks.FirstOrDefault(t => t.Id == id); }
        public EventItem FindEvent(string id) { return Events.FirstOrDefault(e => e.Id == id); }
        public bool IsExpanded(TaskItem t) { return t != null && Expanded.Contains(t.Id); }
        public void SetExpanded(TaskItem t, bool on)
        {
            if (t == null) return;
            if (on) { if (!Expanded.Contains(t.Id)) Expanded.Add(t.Id); }
            else Expanded.Remove(t.Id);
        }

        public void Normalize()
        {
            if (Tasks == null) Tasks = new List<TaskItem>();
            Tasks.RemoveAll(t => t == null);
            if (Settings == null) Settings = new TaskSettings();
            if (Notified == null) Notified = new List<string>();
            Settings.RemindMinutes = Math.Max(0, Math.Min(24 * 60, Settings.RemindMinutes));
            Settings.ListKind = Math.Max(0, Math.Min(2, Settings.ListKind));
            Settings.CalShow = Math.Max(0, Math.Min(2, Settings.CalShow));
            Settings.EventRemind = Math.Max(-1, Math.Min(24 * 60, Settings.EventRemind));
            if (Events == null) Events = new List<EventItem>();
            Events.RemoveAll(e => e == null || !Fmt.ParseIso(e.Start).HasValue);
            foreach (var e in Events)
            {
                if (string.IsNullOrEmpty(e.Id)) e.Id = Guid.NewGuid().ToString("N");
                if (e.Title == null) e.Title = "";
                if (e.Tags == null) e.Tags = new List<string>();
                e.Tags = e.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
                if (Array.IndexOf(EventLogic.ColorKeys, e.Color ?? "") < 0) e.Color = "teal";
                if (e.RemindMin < -1) e.RemindMin = -1;
                if (e.Skip != null && e.Skip.Count == 0) e.Skip = null;
                // 終わりが無い・始まりより前なら直す (終日はその日だけ、時刻つきは 1 時間)
                var st = e.StartAt;
                if (e.AllDay) { var en = Fmt.ParseIso(e.End); e.Start = Fmt.Iso(st.Date); e.End = Fmt.Iso(en.HasValue && en.Value.Date >= st.Date ? en.Value.Date : st.Date); }
                else { var en = Fmt.ParseIso(e.End); if (!en.HasValue || en.Value <= st || e.End.Length <= 10) e.End = Fmt.IsoTime(st.AddHours(1)); if (e.Start.Length <= 10) e.Start = Fmt.IsoTime(st); }
            }
            if (Expanded == null) Expanded = new List<string>();
            Expanded = Expanded.Where(id => id != null).Distinct().ToList();
            if (Version < CurrentVersion) Version = CurrentVersion;
            if (Templates != null)
            {
                Templates.RemoveAll(x => x == null || x.Task == null || string.IsNullOrWhiteSpace(x.Name));
                foreach (var x in Templates) { if (x.Task.Tags == null) x.Task.Tags = new List<string>(); if (x.Task.Subtasks == null) x.Task.Subtasks = new List<SubTask>(); }
                if (Templates.Count == 0) Templates = null;
            }
            foreach (var t in Tasks)
            {
                if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                if (t.Title == null) t.Title = "";
                if (t.Tags == null) t.Tags = new List<string>();
                t.Tags = t.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
                if (t.Subtasks == null) t.Subtasks = new List<SubTask>();
                t.Subtasks.RemoveAll(s => s == null || string.IsNullOrWhiteSpace(s.Text));
                t.Priority = Math.Max(TaskItem.Low, Math.Min(TaskItem.High, t.Priority));
                if (t.DurationMin < 0) t.DurationMin = 0;
                if (t.Due != null && !t.DueAt.HasValue) t.Due = null;
            }
            var ids = new HashSet<string>(Tasks.Select(t => t.Id));
            Expanded.RemoveAll(id => !ids.Contains(id));
        }
    }
}
