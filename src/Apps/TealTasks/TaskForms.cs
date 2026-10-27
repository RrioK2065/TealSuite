// Teal Tasks - ダイアログ: 追加・編集 / 設定
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    /// <summary>タスクの追加・編集</summary>
    public class TaskEditForm : Form
    {
        public readonly TaskItem Task;
        public readonly TextBox tbTitle = new TextBox(), tbTags = new TextBox(), tbMemo = new TextBox(), tbSub = new TextBox();
        public readonly CheckBox chkDue = FormGrid.Check("期限あり"), chkTime = FormGrid.Check("時刻も決める"), chkDone = FormGrid.Check("完了した");
        public readonly DateTimePicker dtDate = FormGrid.DatePicker(), dtTime = new DateTimePicker();
        public readonly ComboBox cbPriority = Lay.Combo(TaskLogic.PriorityLabels.Reverse().ToArray()), cbRepeat = Lay.Combo(TaskLogic.RepeatLabels), cbDuration;
        public readonly CheckedListBox subList = new CheckedListBox();
        readonly WrapLabel error = new WrapLabel();
        /// <summary>保存したときに「完了」にチェックが入っていたか (完了・繰り返しの処理は呼び出し側で)</summary>
        public bool WantDone;

        public TaskEditForm(TaskItem task, bool isNew)
        {
            Task = task;
            DialogKit.Setup(this, isNew ? "タスクを追加" : "タスクを編集");
            var grid = new FormGrid(2, 250);

            Ui.SetCue(tbTitle, "例: 企画書を仕上げる");
            grid.Add("やること (必須)", new InputFrame(tbTitle), 2);

            grid.Add("期限の日", FormGrid.Inline(chkDue, dtDate));
            dtTime.Format = DateTimePickerFormat.Custom; dtTime.CustomFormat = "H:mm"; dtTime.ShowUpDown = true; dtTime.Font = Theme.Body; dtTime.Width = Theme.S(80);
            grid.Add("時刻", FormGrid.Inline(chkTime, dtTime));

            var bToday = new FlatButton("今日", ButtonKind.Ghost).FitWidth(48);
            var bTomorrow = new FlatButton("明日", ButtonKind.Ghost).FitWidth(48);
            var bWeekend = new FlatButton("今週末", ButtonKind.Ghost).FitWidth(48);
            var bNextWeek = new FlatButton("来週の月曜", ButtonKind.Ghost).FitWidth(48);
            var bNone = new FlatButton("期限なし", ButtonKind.Ghost).FitWidth(48);
            var today = DateTime.Today;
            bToday.Click += (s, e) => SetDay(today);
            bTomorrow.Click += (s, e) => SetDay(today.AddDays(1));
            bWeekend.Click += (s, e) => SetDay(TaskLogic.WeekStart(today).AddDays(5) < today ? today : TaskLogic.WeekStart(today).AddDays(5));
            bNextWeek.Click += (s, e) => SetDay(TaskLogic.WeekStart(today).AddDays(7));
            bNone.Click += (s, e) => { chkDue.Checked = false; chkTime.Checked = false; };
            grid.Add("すぐ選ぶ", FormGrid.Inline(bToday, bTomorrow, bWeekend, bNextWeek, bNone), 2);

            grid.Add("優先度", cbPriority);
            cbDuration = Lay.Combo(TaskLogic.DurationChoices.Select(m => m == 0 ? "決めない" : Fmt.Minutes(m)).ToArray());
            grid.Add("かかる時間", cbDuration);

            Ui.SetCue(tbTags, "例: 仕事 家 (空白で区切る)");
            grid.Add("タグ", new InputFrame(tbTags));
            grid.Add("くり返し (完了すると次の回ができます)", cbRepeat);

            // 小タスク
            subList.Font = Theme.Body; subList.CheckOnClick = true; subList.BorderStyle = BorderStyle.FixedSingle; subList.IntegralHeight = false;
            subList.Height = Theme.Body.Height * 4 + Theme.S(8);
            grid.Add("小タスク (チェックで完了)", subList, 2);
            Ui.SetCue(tbSub, "小タスクを入力して Enter");
            var subFrame = new InputFrame(tbSub) { Width = Theme.S(330) };
            var bSubAdd = new FlatButton("追加", ButtonKind.Secondary).FitWidth(56);
            var bSubDel = new FlatButton("選んだものを消す", ButtonKind.Ghost).FitWidth(56);
            bSubAdd.Click += (s, e) => AddSub();
            bSubDel.Click += (s, e) => { if (subList.SelectedIndex >= 0) subList.Items.RemoveAt(subList.SelectedIndex); };
            tbSub.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Control) { AddSub(); e.Handled = e.SuppressKeyPress = true; } };
            subList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && subList.SelectedIndex >= 0) { subList.Items.RemoveAt(subList.SelectedIndex); e.Handled = true; } };
            grid.AddRaw(FormGrid.Inline(subFrame, bSubAdd, bSubDel), 2);

            tbMemo.Multiline = true; tbMemo.AcceptsReturn = true; tbMemo.ScrollBars = ScrollBars.Vertical;
            grid.Add("メモ", new InputFrame(tbMemo, true) { Height = Theme.S(60) }, 2);
            grid.AddRaw(chkDone, 2);

            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error, 2);

            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 2);
            Controls.Add(grid);

            // 値を入れる
            tbTitle.Text = task.Title;
            chkDue.Checked = task.HasDue;
            var due = task.DueAt ?? today.AddHours(DateTime.Now.Hour + 1);
            dtDate.Value = due.Date;
            chkTime.Checked = task.HasTime;
            dtTime.Value = today + (task.HasTime ? due.TimeOfDay : new TimeSpan(Math.Min(23, DateTime.Now.Hour + 1), 0, 0));
            cbPriority.SelectedIndex = 2 - task.Priority;
            int di = Array.IndexOf(TaskLogic.DurationChoices, task.DurationMin);
            if (di < 0 && task.DurationMin > 0) { cbDuration.Items.Add(Fmt.Minutes(task.DurationMin)); di = cbDuration.Items.Count - 1; }
            cbDuration.SelectedIndex = Math.Max(0, di);
            tbTags.Text = string.Join(" ", task.Tags);
            cbRepeat.SelectedIndex = (int)task.Repeat;
            foreach (var st in task.Subtasks) subList.Items.Add(st.Text, st.Done);
            tbMemo.Text = (task.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            chkDone.Checked = task.Done;

            EventHandler upd = (s, e) => UpdateEnabled();
            chkDue.CheckedChanged += upd; chkTime.CheckedChanged += upd;
            UpdateEnabled();
            Shown += (s, e) => { tbTitle.Focus(); tbTitle.SelectAll(); };
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; } };
        }

        void SetDay(DateTime d) { chkDue.Checked = true; dtDate.Value = d; }

        void AddSub()
        {
            var t = tbSub.Text.Trim();
            if (t.Length == 0) return;
            subList.Items.Add(t, false);
            tbSub.Clear(); tbSub.Focus();
        }

        void UpdateEnabled()
        {
            dtDate.Enabled = chkDue.Checked;
            chkTime.Enabled = chkDue.Checked;
            dtTime.Enabled = chkDue.Checked && chkTime.Checked;
        }

        public void Commit()
        {
            var title = tbTitle.Text.Trim();
            if (title.Length == 0) { error.Text = "やることを入力してください"; tbTitle.Focus(); return; }
            if (cbRepeat.SelectedIndex > 0 && !chkDue.Checked) { error.Text = "くり返すときは期限の日を決めてください (次の回の日付をそこから決めます)"; chkDue.Focus(); return; }
            if (tbSub.Text.Trim().Length > 0) AddSub();
            var t = Task;
            t.Title = title;
            if (!chkDue.Checked) t.SetDue(null, false);
            else if (chkTime.Checked) t.SetDue(dtDate.Value.Date + new TimeSpan(dtTime.Value.Hour, dtTime.Value.Minute, 0), true);
            else t.SetDue(dtDate.Value.Date, false);
            t.Priority = 2 - Math.Max(0, cbPriority.SelectedIndex);
            int di = cbDuration.SelectedIndex;
            if (di >= 0 && di < TaskLogic.DurationChoices.Length) t.DurationMin = TaskLogic.DurationChoices[di];
            t.Tags = TaskLogic.ParseTags(tbTags.Text);
            t.Repeat = (Repeat)Math.Max(0, cbRepeat.SelectedIndex);
            t.Subtasks.Clear();
            for (int i = 0; i < subList.Items.Count; i++) t.Subtasks.Add(new SubTask { Text = subList.Items[i].ToString(), Done = subList.GetItemChecked(i) });
            t.Memo = tbMemo.Text.Trim().Length == 0 ? null : tbMemo.Text;
            WantDone = chkDone.Checked;
            DialogResult = DialogResult.OK;
        }
    }

    public class TaskSettingsForm : Form
    {
        public readonly ToggleSwitch swRemind = new ToggleSwitch("期限が近づいたらお知らせする");
        public readonly ToggleSwitch swMorning = new ToggleSwitch("その日最初に「今日のタスク」をまとめてお知らせする");
        public readonly ComboBox cbMinutes;
        static readonly int[] MinuteChoices = { 0, 5, 10, 15, 30, 60, 120 };

        public TaskSettingsForm(TaskSettings st, bool resident, Action exportCsv, Action backup, Action restore)
        {
            DialogKit.Setup(this, AppInfo.TitleFor("設定"));
            var grid = new FormGrid(1, 460);
            grid.Add(null, new WrapLabel { Text = "お知らせ (リマインダー)", Font = Theme.Title });
            grid.AddRaw(swRemind, 1);
            cbMinutes = Lay.Combo(MinuteChoices.Select(m => m == 0 ? "期限の時刻ちょうど" : Fmt.Minutes(m) + "前").ToArray());
            cbMinutes.Width = Theme.S(180);
            grid.Add("時刻つきのタスクをいつお知らせするか", cbMinutes);
            grid.AddRaw(swMorning, 1);
            var note = new WrapLabel
            {
                Text = "お知らせは、Teal Tasks が起動している間だけ出ます (画面右下の通知)。\n" +
                       (resident ? "いまは常駐オンなので、×で閉じても通知領域に残ってお知らせします。パソコンを起動したときから働かせるには、Teal Hub の設定で「スタートアップ」をオンにしてください。"
                                 : "いまは常駐オフなので、閉じている間はお知らせできません。Teal Hub の設定で「常駐」をオンにしてください。"),
                Font = Theme.Small, ForeColor = resident ? Theme.SubText : Theme.Warning
            };
            grid.Add(null, note);
            var hub = new FlatButton("常駐・スタートアップ・ショートカット (Teal Hub)…", ButtonKind.Secondary).FitWidth(120);
            hub.Click += (s, e) => HubLink.OpenSettings(this, AppInfo.Id);
            grid.AddRaw(FormGrid.Inline(hub), 1);

            grid.Add(null, new WrapLabel { Text = "データ", Font = Theme.Title });
            var csv = new FlatButton("CSV に書き出す…", ButtonKind.Secondary).FitWidth(100);
            var bak = new FlatButton("バックアップ (JSON)…", ButtonKind.Secondary).FitWidth(100);
            var res = new FlatButton("バックアップから戻す…", ButtonKind.Secondary).FitWidth(100);
            csv.Click += (s, e) => exportCsv(); bak.Click += (s, e) => backup(); res.Click += (s, e) => restore();
            grid.AddRaw(FormGrid.Inline(csv, bak, res), 1);
            grid.Add(null, new WrapLabel { Text = "データはこのパソコンの中 (" + DataStore.AppDir + ") だけに保存されます。インターネットには送りません。週は月曜はじまりです。", Font = Theme.Small, ForeColor = Theme.SubText });

            var ok = new FlatButton("OK", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) =>
            {
                st.Remind = swRemind.Checked; st.Morning = swMorning.Checked;
                int mi = cbMinutes.SelectedIndex;
                if (mi >= 0 && mi < MinuteChoices.Length) st.RemindMinutes = MinuteChoices[mi];
                DialogResult = DialogResult.OK;
            };
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);

            swRemind.Checked = st.Remind; swMorning.Checked = st.Morning;
            int idx = Array.IndexOf(MinuteChoices, st.RemindMinutes);
            if (idx < 0) { cbMinutes.Items.Add(Fmt.Minutes(st.RemindMinutes) + "前"); idx = cbMinutes.Items.Count - 1; }
            cbMinutes.SelectedIndex = idx;
            EventHandler upd = (s, e) => { cbMinutes.Enabled = swMorning.Enabled = swRemind.Checked; };
            swRemind.CheckedChanged += upd; upd(null, null);
        }
    }
}
