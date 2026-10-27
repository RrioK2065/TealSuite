// Teal Tasks - データの形と保存 (%APPDATA%\TealSuite\TealTasks\tasks.json)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public enum Repeat { None, Daily, Weekly, Monthly }

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
        /// <summary>none / daily / weekly / monthly</summary>
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
            get { switch (RepeatText) { case "daily": return Repeat.Daily; case "weekly": return Repeat.Weekly; case "monthly": return Repeat.Monthly; default: return Repeat.None; } }
            set { RepeatText = value == Repeat.Daily ? "daily" : value == Repeat.Weekly ? "weekly" : value == Repeat.Monthly ? "monthly" : "none"; }
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
        [OnDeserializing] void Defaults(StreamingContext c) { Remind = true; RemindMinutes = 15; Morning = true; }
    }

    [DataContract]
    public class TaskData
    {
        public const string FileName = "tasks.json";
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "tasks", Order = 2)] public List<TaskItem> Tasks = new List<TaskItem>();
        [DataMember(Name = "settings", Order = 3)] public TaskSettings Settings = new TaskSettings();
        /// <summary>お知らせ済みの印。同じお知らせを何度も出さない</summary>
        [DataMember(Name = "notified", Order = 4)] public List<string> Notified = new List<string>();

        public TaskItem Find(string id) { return Tasks.FirstOrDefault(t => t.Id == id); }

        public void Normalize()
        {
            if (Tasks == null) Tasks = new List<TaskItem>();
            Tasks.RemoveAll(t => t == null);
            if (Settings == null) Settings = new TaskSettings();
            if (Notified == null) Notified = new List<string>();
            Settings.RemindMinutes = Math.Max(0, Math.Min(24 * 60, Settings.RemindMinutes));
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
        }
    }
}
