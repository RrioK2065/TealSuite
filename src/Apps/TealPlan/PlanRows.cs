// Teal Plan - 一覧の行 (タスク・小タスク・予定・日付の見出し) と、小タスクの操作 (1.10.0)
//  画面から切り離しているのでテストから直接呼べる
using System;
using System.Collections.Generic;
using System.Linq;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public enum RowKind { Task, Sub, AddSub, Event, Header }

    /// <summary>一覧の 1 行。小タスクは親のタスクと番号で指す</summary>
    public class ListRow
    {
        public RowKind Kind;
        public TaskItem Task;
        public int Sub = -1;
        public CalItem Item;
        public string Text;
        public DateTime Day;

        public SubTask SubTask { get { return Kind == RowKind.Sub && Task != null && Sub >= 0 && Sub < Task.Subtasks.Count ? Task.Subtasks[Sub] : null; } }
        public string Key
        {
            get
            {
                switch (Kind)
                {
                    case RowKind.Task: return "t:" + Task.Id;
                    case RowKind.Sub: return "s:" + Task.Id + ":" + Sub;
                    case RowKind.AddSub: return "a:" + Task.Id;
                    case RowKind.Event: return Item.Key;
                    default: return "h:" + Fmt.Iso(Day);
                }
            }
        }
        public bool Selectable { get { return Kind != RowKind.Header; } }
        public override string ToString() { return Kind == RowKind.Sub ? SubTask.Text : Kind == RowKind.Task ? Task.Title : Kind == RowKind.Event ? Item.Title : Text ?? ""; }
    }

    public static class PlanRows
    {
        /// <summary>タスクの行と、開いているタスクの小タスクの行 (最後に「＋小タスク」の行)</summary>
        public static List<ListRow> Tasks(IEnumerable<TaskItem> tasks, ICollection<string> expanded)
        {
            var rows = new List<ListRow>();
            foreach (var t in tasks) AddTask(rows, t, expanded);
            return rows;
        }

        static void AddTask(List<ListRow> rows, TaskItem t, ICollection<string> expanded)
        {
            rows.Add(new ListRow { Kind = RowKind.Task, Task = t });
            if (expanded == null || !expanded.Contains(t.Id)) return;
            for (int i = 0; i < t.Subtasks.Count; i++) rows.Add(new ListRow { Kind = RowKind.Sub, Task = t, Sub = i });
            rows.Add(new ListRow { Kind = RowKind.AddSub, Task = t });
        }

        /// <summary>日ごとの見出しをはさんだ予定表 (items は予定・タスクの CalItem。undated は最後に並べる期限なしのタスク)</summary>
        public static List<ListRow> Agenda(IEnumerable<CalItem> items, DateTime from, DateTime to, DateTime today, ICollection<string> expanded, IEnumerable<TaskItem> undated = null)
        {
            var rows = new List<ListRow>();
            var all = items.ToList();
            for (var d = from.Date; d < to; d = d.AddDays(1))
            {
                var day = EventLogic.Sort(all.Where(x => x.OnDay(d)));
                if (day.Count == 0) continue;
                rows.Add(new ListRow { Kind = RowKind.Header, Day = d, Text = DayTitle(d, today) });
                foreach (var x in day)
                {
                    // 複数日の予定は始まった日 (範囲の最初の日) にだけ並べる
                    if (x.MultiDay && x.Start.Date < d && d != from.Date) continue;
                    if (x.Kind == CalKind.Task) AddTask(rows, x.Task, expanded);
                    else rows.Add(new ListRow { Kind = RowKind.Event, Item = x, Day = d });
                }
                if (rows[rows.Count - 1].Kind == RowKind.Header) rows.RemoveAt(rows.Count - 1);
            }
            var rest = undated == null ? new List<TaskItem>() : undated.ToList();
            if (rest.Count > 0)
            {
                rows.Add(new ListRow { Kind = RowKind.Header, Text = "期限なしのタスク", Day = DateTime.MaxValue.Date });
                foreach (var t in rest) AddTask(rows, t, expanded);
            }
            return rows;
        }

        public static string DayTitle(DateTime d, DateTime today)
        {
            string rel = d == today ? "今日" : d == today.AddDays(1) ? "明日" : d == today.AddDays(-1) ? "昨日" : "";
            return (d.Year != today.Year ? d.Year + "年" : "") + d.Month + "月" + d.Day + "日 (" + Fmt.DayOfWeek(d) + ")" + (rel != "" ? "  " + rel : "");
        }

        public static int IndexOfKey(IList<ListRow> rows, string key)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Key == key) return i;
            return -1;
        }
    }

    /// <summary>小タスクの操作 (どれも成功したら true)</summary>
    public static class SubtaskLogic
    {
        public static int DoneCount(TaskItem t) { return t.Subtasks.Count(s => s.Done); }
        public static string Progress(TaskItem t) { return DoneCount(t) + "/" + t.Subtasks.Count; }
        public static float Ratio(TaskItem t) { return t.Subtasks.Count == 0 ? 0f : DoneCount(t) / (float)t.Subtasks.Count; }

        /// <summary>小タスクを足す (at が -1 なら最後)。足した番号を返す。空なら -1</summary>
        public static int Add(TaskItem t, string text, int at = -1)
        {
            text = (text ?? "").Trim();
            if (text == "" || t == null) return -1;
            if (at < 0 || at > t.Subtasks.Count) at = t.Subtasks.Count;
            t.Subtasks.Insert(at, new SubTask { Text = text });
            return at;
        }
        /// <summary>改行で区切った複数行をまとめて足す (貼り付け用)。足した数を返す</summary>
        public static int AddLines(TaskItem t, string text, int at = -1)
        {
            int n = 0;
            foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var s = line.Trim().TrimStart('・', '-', '*', '□', '☐').Trim();
                if (s == "") continue;
                int i = Add(t, s, at < 0 ? -1 : at + n);
                if (i >= 0) n++;
            }
            return n;
        }
        public static bool Rename(TaskItem t, int i, string text)
        {
            text = (text ?? "").Trim();
            if (!Valid(t, i) || text == "" || t.Subtasks[i].Text == text) return false;
            t.Subtasks[i].Text = text;
            return true;
        }
        public static bool Toggle(TaskItem t, int i)
        {
            if (!Valid(t, i)) return false;
            t.Subtasks[i].Done = !t.Subtasks[i].Done;
            return true;
        }
        public static bool Delete(TaskItem t, int i)
        {
            if (!Valid(t, i)) return false;
            t.Subtasks.RemoveAt(i);
            return true;
        }
        /// <summary>from の小タスクを to の位置へ (同じタスクの中だけ)</summary>
        public static bool MoveTo(TaskItem t, int from, int to)
        {
            if (!Valid(t, from)) return false;
            to = Math.Max(0, Math.Min(t.Subtasks.Count - 1, to));
            if (to == from) return false;
            var s = t.Subtasks[from];
            t.Subtasks.RemoveAt(from);
            t.Subtasks.Insert(to, s);
            return true;
        }
        public static bool Move(TaskItem t, int i, int delta) { return MoveTo(t, i, i + delta); }
        static bool Valid(TaskItem t, int i) { return t != null && i >= 0 && i < t.Subtasks.Count; }

        /// <summary>小タスクのあるタスクをすべて開く。開いた数を返す</summary>
        public static int ExpandAll(TaskData data, IEnumerable<TaskItem> tasks)
        {
            int n = 0;
            foreach (var t in tasks) if (t.Subtasks.Count > 0 && !data.IsExpanded(t)) { data.SetExpanded(t, true); n++; }
            return n;
        }
        public static int CollapseAll(TaskData data)
        {
            int n = data.Expanded.Count;
            data.Expanded.Clear();
            return n;
        }
    }
}
