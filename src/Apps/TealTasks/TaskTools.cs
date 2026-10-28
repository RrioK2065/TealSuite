// Teal Tasks - 1.8.0 の追加: テンプレート・まとめて追加・完了したタスクの整理・複製
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    /// <summary>よく使うタスクのひな形 (期限は「追加した日から何日後」で覚える)</summary>
    [DataContract]
    public class TaskTemplate
    {
        [DataMember(Name = "name", Order = 1)] public string Name = "";
        /// <summary>中身 (id・期限・完了は使わない)</summary>
        [DataMember(Name = "task", Order = 2)] public TaskItem Task;
        /// <summary>期限: 追加した日から何日後か。-1 = 期限なし</summary>
        [DataMember(Name = "dueDays", Order = 3)] public int DueDays = -1;
        /// <summary>期限の時刻 (HH:mm)。null = 日付だけ</summary>
        [DataMember(Name = "time", Order = 4, EmitDefaultValue = false)] public string Time;
        [OnDeserializing] void Defaults(StreamingContext c) { DueDays = -1; }
    }

    public static class TaskTools
    {
        public const int MaxTemplates = 30;
        public const int MaxBulk = 200;
        public static readonly int[] CleanupDays = { 30, 90, 365 };

        /// <summary>タスクからテンプレートを作る (期限は今日からの日数に直す)</summary>
        public static TaskTemplate FromTask(TaskItem t, string name, DateTime now)
        {
            var c = t.Clone();
            var tpl = new TaskTemplate { Name = (name ?? "").Trim(), Task = c };
            if (tpl.Name.Length == 0) tpl.Name = t.Title;
            if (t.HasDue)
            {
                tpl.DueDays = Math.Max(0, (int)(t.DueDate.Value - now.Date).TotalDays);
                if (t.HasTime) tpl.Time = t.DueAt.Value.ToString("HH:mm");
            }
            c.Id = null; c.Due = null; c.Done = false; c.DoneAt = null; c.NextId = null; c.Created = null;
            foreach (var s in c.Subtasks) s.Done = false;
            return tpl;
        }

        /// <summary>テンプレートから新しいタスクを作る</summary>
        public static TaskItem Instantiate(TaskTemplate tpl, DateTime now)
        {
            var t = tpl.Task.Clone();
            t.Id = Guid.NewGuid().ToString("N");
            t.Done = false; t.DoneAt = null; t.NextId = null;
            t.Created = Fmt.IsoTime(now);
            if (t.Tags == null) t.Tags = new List<string>();
            if (t.Subtasks == null) t.Subtasks = new List<SubTask>();
            foreach (var s in t.Subtasks) s.Done = false;
            if (tpl.DueDays >= 0)
            {
                var day = now.Date.AddDays(tpl.DueDays);
                TimeSpan tm;
                if (tpl.Time != null && TimeSpan.TryParse(tpl.Time, out tm) && tm < TimeSpan.FromDays(1)) t.SetDue(day + tm, true);
                else t.SetDue(day, false);
            }
            else t.Due = null;
            if (t.Repeat != Repeat.None && !t.HasDue) t.SetDue(now.Date, false);
            return t;
        }

        /// <summary>同じ名前があれば置きかえて保存。いっぱいなら false</summary>
        public static bool SaveTemplate(TaskData data, TaskTemplate tpl)
        {
            if (data.Templates == null) data.Templates = new List<TaskTemplate>();
            int i = data.Templates.FindIndex(x => string.Equals(x.Name, tpl.Name, StringComparison.CurrentCultureIgnoreCase));
            if (i >= 0) { data.Templates[i] = tpl; return true; }
            if (data.Templates.Count >= MaxTemplates) return false;
            data.Templates.Add(tpl);
            return true;
        }

        public static string TemplateSummary(TaskTemplate tpl)
        {
            var parts = new List<string>();
            parts.Add(tpl.DueDays < 0 ? "期限なし" : tpl.DueDays == 0 ? "今日" + (tpl.Time != null ? " " + tpl.Time : "") : tpl.DueDays == 1 ? "明日" + (tpl.Time != null ? " " + tpl.Time : "") : tpl.DueDays + " 日後" + (tpl.Time != null ? " " + tpl.Time : ""));
            if (tpl.Task.Repeat != Repeat.None) parts.Add(TaskLogic.RepeatLabel(tpl.Task.Repeat));
            if (tpl.Task.Subtasks != null && tpl.Task.Subtasks.Count > 0) parts.Add("小タスク " + tpl.Task.Subtasks.Count);
            return string.Join("・", parts);
        }

        /// <summary>完了してから days 日より前のタスク (繰り返しの次の回が残っていても消してよい)</summary>
        public static List<TaskItem> OldDone(IEnumerable<TaskItem> tasks, DateTime now, int days)
        {
            var limit = now.Date.AddDays(-days);
            return tasks.Where(t => t.Done && (t.DoneTime ?? DateTime.MinValue).Date < limit).ToList();
        }

        public static int CleanupDone(TaskData data, DateTime now, int days)
        {
            var old = new HashSet<TaskItem>(OldDone(data.Tasks, now, days));
            return data.Tasks.RemoveAll(t => old.Contains(t));
        }

        /// <summary>1 行 1 タスクで読み取る (行頭の「・」「-」「□」や番号は落とす。空の行は飛ばす)</summary>
        public static List<TaskItem> ParseBulk(string text, DateTime now, DateTime? defaultDay = null)
        {
            var list = new List<TaskItem>();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"^(?:(?:[・\-\*•□■☐✓✔]|\d{1,3}[\.\)．、]|\[\s?[xX ]?\])\s*)+", "");
                if (line.Length == 0) continue;
                var r = QuickParser.Parse(line, now);
                if (r.Title.Length == 0) continue;
                if (!r.Date.HasValue && defaultDay.HasValue) r.Date = defaultDay.Value.Date;
                list.Add(r.ToTask(now));
                if (list.Count >= MaxBulk) break;
            }
            return list;
        }

        /// <summary>複製 (未完了で、小タスクも未完了に)</summary>
        public static TaskItem Duplicate(TaskItem t, DateTime now)
        {
            var c = t.Clone();
            c.Id = Guid.NewGuid().ToString("N");
            c.Done = false; c.DoneAt = null; c.NextId = null;
            c.Created = Fmt.IsoTime(now);
            foreach (var s in c.Subtasks) s.Done = false;
            return c;
        }
    }

    /// <summary>まとめて追加 (1 行に 1 つ。ひとこと入力と同じ書き方)</summary>
    public class BulkAddForm : Form
    {
        public readonly TextBox Lines = new TextBox();
        public readonly Label Preview = new Label { AutoSize = false, UseMnemonic = false };
        readonly DateTime now; readonly DateTime? day;
        public List<TaskItem> Result = new List<TaskItem>();

        public BulkAddForm(DateTime now, DateTime? defaultDay)
        {
            this.now = now; day = defaultDay;
            DialogKit.Setup(this, "まとめて追加");
            var grid = new FormGrid(1, 520);
            grid.Add(null, new WrapLabel { Text = "1 行に 1 つずつ書くと、まとめて追加します。ひとこと入力と同じように「明日15時」「重要」「#タグ」「毎週」も読み取ります。\nメモ帳やメールの箇条書き (・ - 1. □) をそのまま貼り付けても大丈夫です。" + (defaultDay.HasValue ? "\n日付を書いていない行は " + Fmt.MonthDay(defaultDay.Value) + " (カレンダーで選んでいる日) が期限になります。" : ""), Font = Theme.Small, ForeColor = Theme.SubText });
            Lines.Multiline = true; Lines.AcceptsReturn = true; Lines.ScrollBars = ScrollBars.Vertical; Lines.Font = Theme.Editor;
            Ui.SetCue(Lines, "例:\n牛乳を買う #買い物\n明日10時 会議の資料を送る 重要\n金曜までに 経費の精算");
            var frame = new InputFrame(Lines, true) { Height = Theme.S(220) };
            grid.Add("やること (1 行に 1 つ)", frame);
            Preview.Font = Theme.Small; Preview.ForeColor = Theme.Accent; Preview.Height = Theme.Small.Height * 6 + Theme.S(8); Preview.Width = Theme.S(520);
            grid.Add(null, Preview);
            var ok = new FlatButton("追加", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
            Lines.TextChanged += (s, e) => UpdatePreview();
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; } };
            Shown += (s, e) => Lines.Focus();
            UpdatePreview();
        }

        void UpdatePreview()
        {
            var list = TaskTools.ParseBulk(Lines.Text, now, day);
            if (list.Count == 0) { Preview.ForeColor = Theme.SubText; Preview.Text = "まだ何も書かれていません (Ctrl+Enter で追加)"; return; }
            Preview.ForeColor = Theme.Accent;
            Preview.Text = list.Count + " 件を追加します:\n" + string.Join("\n", list.Take(4).Select(t => "・" + t.Title + "  (" + TaskLogic.DueText(t, now) + ")")) + (list.Count > 4 ? "\n…ほか " + (list.Count - 4) + " 件" : "");
        }

        public void Commit()
        {
            Result = TaskTools.ParseBulk(Lines.Text, now, day);
            if (Result.Count == 0) { Lines.Focus(); return; }
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>テンプレートの名前を入れる</summary>
    public class TemplateNameForm : Form
    {
        public readonly TextBox NameBox = new TextBox();
        public string TemplateName { get { return NameBox.Text.Trim(); } }
        public TemplateNameForm(string initial, string summary)
        {
            DialogKit.Setup(this, "テンプレートにする");
            var grid = new FormGrid(1, 400);
            NameBox.Text = initial ?? "";
            grid.Add("テンプレートの名前", new InputFrame(NameBox));
            grid.Add(null, new WrapLabel { Text = "優先度・かかる時間・タグ・メモ・小タスク・くり返しを覚えます。期限は「" + summary + "」として、使った日から数えます。\n同じ名前のテンプレートがあれば上書きします。", Font = Theme.Small, ForeColor = Theme.SubText });
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => { if (TemplateName.Length == 0) { NameBox.Focus(); return; } DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel; AcceptButton = ok;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
            Shown += (s, e) => { NameBox.Focus(); NameBox.SelectAll(); };
        }
    }
}
