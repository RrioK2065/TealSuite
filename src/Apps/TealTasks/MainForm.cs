// Teal Tasks (タスク管理＆カレンダー) - メイン画面
//  上: ひとこと入力 (「明日15時 歯医者 #病院」→ Enter で追加。Ctrl+Enter で詳しく入力)
//  タブ: 今日 / カレンダー / 一覧 / 統計 (Ctrl+1〜4)
//  Ctrl+N 追加 / Ctrl+L ひとこと入力へ / Ctrl+F 検索 / Space 完了 / Enter 編集 / Delete 削除 / Ctrl+Z 元に戻す
//  お知らせ: 起動中 (常駐中) だけ。30 秒ごとに確かめる
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public class MainForm : Form
    {
        public TaskData Data;
        readonly HeaderBar header;
        readonly FooterBar footer = new FooterBar();
        public readonly SegmentTabs Tabs = new SegmentTabs("今日", "カレンダー", "一覧", "統計");
        readonly Panel content = new Panel { Dock = DockStyle.Fill };
        readonly Panel[] views = new Panel[4];
        readonly Timer remindTimer = new Timer();
        readonly ToolTip tips = new ToolTip();
        ResidentContext resident;
        DateTime lastCheckDay = DateTime.MinValue;
        /// <summary>テストで時刻を固定するため</summary>
        public Func<DateTime> Clock = () => DateTime.Now;
        DateTime Now { get { return Clock(); } }

        // 元に戻す (1 回ぶん)
        byte[] undoData; string undoText;

        // ひとこと入力
        public readonly TextBox Quick = new TextBox();
        readonly Label quickPreview = new Label { AutoSize = false, UseMnemonic = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        // 今日
        readonly Panel todayEmpty = new CardPanel();
        readonly WrapLabel todaySummary = new WrapLabel();
        public readonly RowList TodayList = new RowList(2) { Dock = DockStyle.Fill };
        // カレンダー
        public readonly CalendarView Calendar = new CalendarView { Dock = DockStyle.Fill };
        public readonly SegmentTabs CalMode = new SegmentTabs("月", "週", "日");
        readonly Label periodLabel = new Label { AutoSize = true, UseMnemonic = false };
        // 一覧
        public readonly TextBox Search = new TextBox();
        readonly ComboBox filter = Lay.Combo("未完了", "期限切れ", "今日まで", "今週", "期限なし", "完了", "すべて");
        readonly ComboBox tagFilter = Lay.Combo("すべてのタグ");
        readonly ComboBox sort = Lay.Combo("期限が近い順", "おすすめ順", "優先度順", "追加した順");
        public readonly RowList List = new RowList(2) { Dock = DockStyle.Fill };
        // 統計
        public readonly StatTile TileToday = new StatTile(), TileWeek = new StatTile(), TileRate = new StatTile(), TileOpen = new StatTile();
        readonly ColumnChart daily = new ColumnChart { Dock = DockStyle.Fill };
        readonly TagChart tagChart = new TagChart { Dock = DockStyle.Fill };

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(820, 560);
            KeyPreview = true;

            header = HeaderBar.ForApp("このパソコンの中だけで管理します");
            var btnSettings = header.AddAction(new FlatButton("設定", ButtonKind.Secondary).FitWidth(72));
            var btnMore = header.AddAction(new FlatButton("その他 ▾", ButtonKind.Secondary).FitWidth(80));
            moreMenu.Font = Theme.Body; moreMenu.ShowImageMargin = false;
            btnMore.Click += (s, e) => { BuildMoreMenu(); moreMenu.Show(btnMore, new Point(0, btnMore.Height)); };
            tips.SetToolTip(btnMore, "テンプレート・まとめて追加・複製・完了したタスクの整理");
            var btnAdd = header.AddAction(new FlatButton("＋ 追加", ButtonKind.Primary).FitWidth(96));
            btnAdd.Click += (s, e) => AddNew(null, false);
            btnSettings.Click += (s, e) => OpenSettings();
            tips.SetToolTip(btnAdd, "詳しく入力して追加 (Ctrl+N)");
            tips.SetToolTip(btnSettings, "お知らせ・CSV・バックアップ");

            Tabs.Dock = DockStyle.Top;
            Tabs.SelectedChanged += (s, e) => ShowTab(Tabs.SelectedIndex);

            for (int i = 0; i < views.Length; i++) views[i] = new Panel { Dock = DockStyle.Fill, Visible = i == 0, BackColor = Theme.Background };
            BuildToday(views[0]);
            BuildCalendar(views[1]);
            BuildList(views[2]);
            BuildStats(views[3]);
            foreach (var v in views) content.Controls.Add(v);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12, 8, 12, 12), BackColor = Theme.Background };
            body.Controls.Add(content);
            body.Controls.Add(Ui.Spacer(8));
            body.Controls.Add(Tabs);
            body.Controls.Add(Ui.Spacer(8));
            body.Controls.Add(BuildQuick());
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            string warn;
            Data = DataStore.LoadOrDefault(TaskData.FileName, () => new TaskData(), out warn);
            Data.Normalize();
            if (warn != null) Load += (s, e) => Dialogs.Warn(this, warn);

            WindowMemory.Attach(this, 1080, 780);
            remindTimer.Interval = 5000;
            remindTimer.Tick += (s, e) => { remindTimer.Interval = 30 * 1000; if (lastCheckDay != Now.Date) RefreshAll(); CheckReminders(); };
            Load += (s, e) =>
            {
                ApplyStartArgs();
                RefreshAll();
                remindTimer.Start();
            };
            Shown += (s, e) => { if (Tabs.SelectedIndex == 0) Quick.Focus(); };
            Activated += (s, e) => { if (lastCheckDay != Now.Date) { RefreshAll(); CheckReminders(); } };
            Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
            FormClosed += (s, e) => { Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged; remindTimer.Stop(); };
        }

        void OnTimeChanged(object sender, EventArgs e) { AppHost.OnUi(() => { RefreshAll(); CheckReminders(); }); }

        /// <summary>--tab=today|calendar|list|stats (1〜4) と --cal=month|week|day (画面写真用)</summary>
        void ApplyStartArgs()
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("--tab="))
                {
                    var v = a.Substring(6); int n;
                    if (int.TryParse(v, out n) && n >= 1 && n <= 4) Tabs.SelectedIndex = n - 1;
                    else switch (v) { case "calendar": Tabs.SelectedIndex = 1; break; case "list": Tabs.SelectedIndex = 2; break; case "stats": Tabs.SelectedIndex = 3; break; }
                }
                if (a.StartsWith("--cal="))
                {
                    switch (a.Substring(6)) { case "week": CalMode.SelectedIndex = 1; break; case "day": CalMode.SelectedIndex = 2; break; }
                }
            }
        }

        public void AttachResident(ResidentContext ctx)
        {
            resident = ctx;
            ctx.BalloonClicked = () =>
            {
                ctx.Window.Show();
                if (ctx.Window.WindowState == FormWindowState.Minimized) ctx.Window.WindowState = FormWindowState.Normal;
                AppHost.BringToFront(ctx.Window);
                Tabs.SelectedIndex = 0;
            };
        }

        // ================================================================ 共通

        void ShowTab(int i)
        {
            for (int k = 0; k < views.Length; k++) views[k].Visible = k == i;
            if (i == 0) TodayList.Focus();
            if (i == 1) Calendar.Focus();
            if (i == 2) List.Focus();
            UpdatePreview();
        }

        public void Save()
        {
            if (!DataStore.TrySave(TaskData.FileName, Data)) footer.StatusText = "保存できませんでした (データフォルダを確かめてください)";
        }

        /// <summary>変更の前に呼ぶ (Ctrl+Z で 1 回だけ戻せる)</summary>
        void Remember(string what) { undoData = DataStore.ToJson(Data); undoText = what; }

        void Changed(string status)
        {
            Save(); RefreshAll();
            if (status != null) footer.StatusText = status + (undoData != null ? "  (Ctrl+Z で元に戻せます)" : "");
        }

        public void Undo()
        {
            if (undoData == null) { footer.StatusText = "元に戻せる操作はありません"; return; }
            var keepTemplates = Data.Templates; // テンプレートは元に戻す対象にしない
            Data = DataStore.FromJson<TaskData>(undoData);
            Data.Templates = keepTemplates;
            Data.Normalize();
            string what = undoText;
            undoData = null; undoText = null;
            Save(); RefreshAll();
            footer.StatusText = "元に戻しました: " + what;
        }

        public void RefreshAll()
        {
            lastCheckDay = Now.Date;
            RefreshToday();
            Calendar.Invalidate(); UpdatePeriod();
            RefreshTags();
            RefreshList();
            RefreshStats();
            UpdatePreview();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: AddNew(Tabs.SelectedIndex == 1 ? (DateTime?)Calendar.Day : null, false); return true;
                case Keys.Control | Keys.L: Quick.Focus(); Quick.SelectAll(); return true;
                case Keys.Control | Keys.Shift | Keys.N: BulkAdd(); return true;
                case Keys.Control | Keys.D: DuplicateTask(Current()); return true;
                case Keys.Control | Keys.D1: Tabs.SelectedIndex = 0; return true;
                case Keys.Control | Keys.D2: Tabs.SelectedIndex = 1; return true;
                case Keys.Control | Keys.D3: Tabs.SelectedIndex = 2; return true;
                case Keys.Control | Keys.D4: Tabs.SelectedIndex = 3; return true;
                case Keys.Control | Keys.F: Tabs.SelectedIndex = 2; Search.Focus(); Search.SelectAll(); return true;
                case Keys.Control | Keys.Z:
                    if (ActiveControl is TextBox || (ActiveControl is InputFrame)) break;
                    if (Quick.Focused || Search.Focused) break;
                    Undo(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        TaskItem Current()
        {
            if (Tabs.SelectedIndex == 0) return TodayList.SelectedItem as TaskItem;
            if (Tabs.SelectedIndex == 1) return Calendar.SelectedTask;
            if (Tabs.SelectedIndex == 2) return List.SelectedItem as TaskItem;
            return null;
        }

        // ================================================================ 操作

        /// <summary>詳しく入力して追加 (day を渡すとその日を期限に)</summary>
        public void AddNew(DateTime? day, bool withTime, TaskItem prefill = null)
        {
            var t = prefill ?? new TaskItem();
            t.Created = Fmt.IsoTime(Now);
            if (prefill == null && day.HasValue) t.SetDue(day, withTime);
            using (var f = new TaskEditForm(t, true))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Remember("「" + t.Title + "」の追加");
                Data.Tasks.Add(t);
                if (f.WantDone) TaskLogic.Complete(Data, t, Now);
            }
            Changed("「" + t.Title + "」を追加しました");
            Reveal(t);
        }

        public void Edit(TaskItem t)
        {
            if (t == null) return;
            var copy = t.Clone();
            using (var f = new TaskEditForm(copy, false))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Remember("「" + t.Title + "」の編集");
                int i = Data.Tasks.IndexOf(t);
                if (i >= 0) Data.Tasks[i] = copy; else Data.Tasks.Add(copy);
                if (f.WantDone && !copy.Done) { copy.Done = false; TaskLogic.Complete(Data, copy, Now); }
                else if (!f.WantDone && copy.Done) TaskLogic.Uncomplete(Data, copy);
            }
            Changed("「" + copy.Title + "」を保存しました");
            Reveal(copy);
        }

        public void Toggle(TaskItem t)
        {
            if (t == null) return;
            Remember(t.Done ? "「" + t.Title + "」の完了の取り消し" : "「" + t.Title + "」の完了");
            bool wasDone = t.Done;
            var next = wasDone ? null : TaskLogic.Complete(Data, t, Now);
            if (wasDone) TaskLogic.Uncomplete(Data, t);
            Changed(wasDone ? "「" + t.Title + "」を未完了に戻しました"
                : "「" + t.Title + "」を完了しました" + (next != null ? "。次の回: " + TaskLogic.DueText(next, Now) : ""));
        }

        void Delete(TaskItem t)
        {
            if (t == null) return;
            if (!Dialogs.Confirm(this, "「" + t.Title + "」を削除しますか?")) return;
            Remember("「" + t.Title + "」の削除");
            Data.Tasks.Remove(t);
            Changed("「" + t.Title + "」を削除しました");
        }

        /// <summary>明日に回す (時刻はそのまま)</summary>
        void Postpone(TaskItem t)
        {
            if (t == null || t.Done) return;
            Remember("「" + t.Title + "」の延期");
            var tomorrow = Now.Date.AddDays(1);
            if (t.HasTime) t.SetDue(tomorrow + t.DueAt.Value.TimeOfDay, true); else t.SetDue(tomorrow, false);
            Changed("「" + t.Title + "」を明日 (" + Fmt.MonthDay(tomorrow) + ") に回しました");
        }

        public void MoveTask(TaskItem t, DateTime when, bool withTime)
        {
            Remember("「" + t.Title + "」の移動");
            t.SetDue(when, withTime);
            Changed("「" + t.Title + "」を " + TaskLogic.DueText(t, Now) + " に動かしました");
        }

        void Reveal(TaskItem t)
        {
            for (int i = 0; i < TodayList.Items.Count; i++) if (TodayList.Items[i] == t) { TodayList.SelectedIndex = i; break; }
            for (int i = 0; i < List.Items.Count; i++) if (List.Items[i] == t) { List.SelectedIndex = i; break; }
            if (Tabs.SelectedIndex == 1 && t.HasDue) { Calendar.SelectedTask = t; Calendar.Day = t.DueDate.Value; }
        }

        // ================================================================ 1.8.0: その他 (テンプレート・まとめて追加・複製・整理)

        readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        public ContextMenuStrip MoreMenu { get { BuildMoreMenu(); return moreMenu; } }
        /// <summary>テスト用: 確認・名前入力の代わり</summary>
        public Func<string, bool> ConfirmHook;
        public Func<string, string> TemplateNameHook;

        bool Ask(string msg) { return ConfirmHook != null ? ConfirmHook(msg) : Dialogs.Confirm(this, msg); }

        void BuildMoreMenu()
        {
            moreMenu.Items.Clear();
            var cur = Current();
            var tpls = Data.Templates ?? new List<TaskTemplate>();
            var miUse = new ToolStripMenuItem("テンプレートから追加") { Enabled = tpls.Count > 0 };
            foreach (var t in tpls)
            {
                var tpl = t;
                miUse.DropDownItems.Add(new ToolStripMenuItem(tpl.Name + "   (" + TaskTools.TemplateSummary(tpl) + ")", null, (s, e) => AddFromTemplate(tpl)));
            }
            moreMenu.Items.Add(miUse);
            moreMenu.Items.Add(new ToolStripMenuItem(cur != null ? "選んでいるタスクをテンプレートにする…" : "選んでいるタスクをテンプレートにする (タスクを選んでください)", null, (s, e) => SaveAsTemplate(Current())) { Enabled = cur != null });
            var miDel = new ToolStripMenuItem("テンプレートを削除") { Enabled = tpls.Count > 0 };
            foreach (var t in tpls) { var tpl = t; miDel.DropDownItems.Add(new ToolStripMenuItem(tpl.Name, null, (s, e) => DeleteTemplate(tpl))); }
            moreMenu.Items.Add(miDel);
            moreMenu.Items.Add(new ToolStripSeparator());
            moreMenu.Items.Add(new ToolStripMenuItem("まとめて追加… (Ctrl+Shift+N)", null, (s, e) => BulkAdd()));
            moreMenu.Items.Add(new ToolStripMenuItem("選んでいるタスクを複製 (Ctrl+D)", null, (s, e) => DuplicateTask(Current())) { Enabled = cur != null });
            moreMenu.Items.Add(new ToolStripSeparator());
            var miClean = new ToolStripMenuItem("完了したタスクを整理");
            foreach (var d in TaskTools.CleanupDays)
            {
                int days = d;
                int n = TaskTools.OldDone(Data.Tasks, Now, days).Count;
                miClean.DropDownItems.Add(new ToolStripMenuItem((days >= 365 ? "1 年" : days + " 日") + "より前に完了したものを削除 (" + n + " 件)", null, (s, e) => CleanupDone(days)) { Enabled = n > 0 });
            }
            moreMenu.Items.Add(miClean);
        }

        public TaskItem AddFromTemplate(TaskTemplate tpl)
        {
            var t = TaskTools.Instantiate(tpl, Now);
            Remember("「" + t.Title + "」の追加");
            Data.Tasks.Add(t);
            Changed("テンプレート「" + tpl.Name + "」から追加しました (" + TaskLogic.DueText(t, Now) + ")");
            Reveal(t);
            return t;
        }

        public bool SaveAsTemplate(TaskItem t)
        {
            if (t == null) { footer.StatusText = "テンプレートにするタスクを選んでください"; return false; }
            var tpl = TaskTools.FromTask(t, t.Title, Now);
            string name;
            if (TemplateNameHook != null) name = TemplateNameHook(t.Title);
            else
                using (var f = new TemplateNameForm(t.Title, TaskTools.TemplateSummary(tpl)))
                    name = f.ShowDialog(this) == DialogResult.OK ? f.TemplateName : null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            tpl.Name = name.Trim();
            if (!TaskTools.SaveTemplate(Data, tpl)) { Dialogs.Warn(this, "テンプレートは " + TaskTools.MaxTemplates + " 個までです。使わないものを削除してください。"); return false; }
            Save();
            footer.StatusText = "テンプレート「" + tpl.Name + "」を保存しました (その他 ▾ → テンプレートから追加)";
            return true;
        }

        void DeleteTemplate(TaskTemplate tpl)
        {
            if (!Ask("テンプレート「" + tpl.Name + "」を削除しますか? (作ったタスクはそのまま残ります)")) return;
            if (Data.Templates != null) { Data.Templates.Remove(tpl); if (Data.Templates.Count == 0) Data.Templates = null; }
            Save();
            footer.StatusText = "テンプレート「" + tpl.Name + "」を削除しました";
        }

        /// <summary>まとめて追加 (1 行 1 タスク)</summary>
        public void BulkAdd()
        {
            var day = Tabs.SelectedIndex == 1 ? (DateTime?)Calendar.Day : null;
            using (var f = new BulkAddForm(Now, day))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                AddMany(f.Result);
            }
        }

        public int AddMany(List<TaskItem> list)
        {
            if (list == null || list.Count == 0) return 0;
            Remember(list.Count + " 件の追加");
            Data.Tasks.AddRange(list);
            Changed(list.Count + " 件をまとめて追加しました");
            return list.Count;
        }

        public TaskItem DuplicateTask(TaskItem t)
        {
            if (t == null) { footer.StatusText = "複製するタスクを選んでください"; return null; }
            var c = TaskTools.Duplicate(t, Now);
            Remember("「" + t.Title + "」の複製");
            Data.Tasks.Add(c);
            Changed("「" + t.Title + "」を複製しました");
            Reveal(c);
            return c;
        }

        /// <summary>days 日より前に完了したタスクを削除 (Ctrl+Z で戻せる)</summary>
        public int CleanupDone(int days)
        {
            int n = TaskTools.OldDone(Data.Tasks, Now, days).Count;
            if (n == 0) { footer.StatusText = "整理するタスクはありません"; return 0; }
            if (!Ask((days >= 365 ? "1 年" : days + " 日") + "より前に完了したタスク " + n + " 件を削除しますか?\n(統計の「これまでの完了」からも消えます。Ctrl+Z で元に戻せます)")) return 0;
            Remember("完了したタスクの整理");
            n = TaskTools.CleanupDone(Data, Now, days);
            Changed("完了したタスク " + n + " 件を整理しました");
            return n;
        }

        public void InsertSamples()
        {
            Data.Tasks.AddRange(TaskLogic.SampleData(Now));
            Changed("見本データを入れました (不要になったら一覧から削除できます)");
        }

        void OpenSettings()
        {
            bool res = resident != null && resident.IsResident;
            using (var f = new TaskSettingsForm(Data.Settings, res, ExportCsv, Backup, Restore))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            Data.Normalize();
            Save(); RefreshAll();
            CheckReminders();
        }

        void ExportCsv()
        {
            var p = FileKit.SaveText(this, TaskLogic.ToCsv(Data.Tasks), "タスク一覧-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", "CSV (*.csv)|*.csv");
            if (p != null) footer.StatusText = "CSV に書き出しました: " + p;
        }
        void Backup()
        {
            var p = FileKit.ExportJson(this, Data, "TealTasks");
            if (p != null) footer.StatusText = "バックアップしました: " + p;
        }
        void Restore()
        {
            var d = FileKit.ImportJson<TaskData>(this, TaskData.FileName, x => x.Tasks != null);
            if (d == null) return;
            d.Normalize();
            Remember("バックアップから戻す");
            Data = d;
            Save(); RefreshAll();
            Dialogs.Info(this, "バックアップから " + d.Tasks.Count + " 件を戻しました。");
        }

        // ================================================================ お知らせ

        public List<Notice> CheckReminders()
        {
            var now = Now;
            var list = TaskLogic.DueNotices(Data, now);
            if (list.Count == 0) return list;
            var ctx = resident ?? ResidentContext.Current;
            if (ctx == null) return list;
            bool shown;
            if (list.Count <= 2)
            {
                shown = true;
                foreach (var n in list) shown &= ctx.Notify(n.Title, n.Text, ToolTipIcon.Info);
            }
            else
            {
                var text = string.Join("\n", list.Take(4).Select(n => "・" + n.Title));
                if (list.Count > 4) text += "\nほか " + (list.Count - 4) + " 件";
                shown = ctx.Notify("Teal Tasks: お知らせ " + list.Count + " 件", text, ToolTipIcon.Info);
            }
            if (shown)
            {
                foreach (var n in list) Data.Notified.Add(n.Key);
                TaskLogic.PruneNotified(Data, now);
                Save();
            }
            return list;
        }

        // ================================================================ ひとこと入力

        Control BuildQuick()
        {
            var card = new CardPanel { Dock = DockStyle.Top, Padding = Theme.P(12, 10, 12, 8) };
            Quick.Font = Theme.Editor; Quick.BorderStyle = BorderStyle.None;
            Ui.SetCue(Quick, "例: 明日15時 歯医者 #病院 / 金曜までにレポート 重要 2時間 / 毎週月曜 ゴミ出し  (Ctrl+L)");
            var frame = new InputFrame(Quick);
            var add = new FlatButton("追加", ButtonKind.Primary).FitWidth(72);
            var more = new FlatButton("詳しく…", ButtonKind.Secondary).FitWidth(72);
            add.Click += (s, e) => QuickAdd();
            more.Click += (s, e) => QuickDetail();
            tips.SetToolTip(add, "Enter");
            tips.SetToolTip(more, "日時などを確かめてから追加 (Ctrl+Enter)");
            var row = Lay.Row(frame, more, add);
            row.Dock = DockStyle.Top;
            quickPreview.Font = Theme.Small; quickPreview.ForeColor = Theme.SubText;
            var prev = new Panel { Dock = DockStyle.Top, Height = Theme.Small.Height + Theme.S(10), Padding = Theme.P(4, 0, 0, 0) };
            prev.Controls.Add(quickPreview);
            Quick.TextChanged += (s, e) => UpdatePreview();
            Quick.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control) { QuickDetail(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { QuickAdd(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { Quick.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { (Tabs.SelectedIndex == 2 ? (Control)List : Tabs.SelectedIndex == 1 ? (Control)Calendar : TodayList).Focus(); e.Handled = true; }
            };
            Lay.Stack(card, row, prev);
            card.Height = card.Padding.Vertical + row.Height + prev.Height;
            return card;
        }

        /// <summary>入力欄の内容を読み取る。日付が無ければ、カレンダーで選んでいる日を使う</summary>
        public ParseResult ParseQuick()
        {
            var r = QuickParser.Parse(Quick.Text, Now);
            if (!r.Date.HasValue && Tabs.SelectedIndex == 1 && r.Title.Length > 0) r.Date = Calendar.Day;
            return r;
        }

        void UpdatePreview()
        {
            if (Quick.Text.Trim().Length == 0)
            {
                quickPreview.ForeColor = Theme.SubText;
                quickPreview.Text = "日本語で書くと、日付・時刻・かかる時間・優先度 (重要/急ぎ)・#タグ・くり返し (毎日/平日/毎週/毎月/毎年) を読み取ります。Enter で追加。";
                return;
            }
            var r = ParseQuick();
            quickPreview.ForeColor = r.Title.Length > 0 ? Theme.Accent : Theme.Warning;
            quickPreview.Text = (r.Title.Length > 0 ? "追加される内容: " : "やることの名前も書いてください: ") + r.Describe(Now);
        }

        public TaskItem QuickAdd()
        {
            if (Quick.Text.Trim().Length == 0) { Quick.Focus(); return null; }
            var r = ParseQuick();
            if (r.Title.Length == 0) { footer.StatusText = "やることの名前を書いてください (例: 明日10時 会議)"; return null; }
            var t = r.ToTask(Now);
            Remember("「" + t.Title + "」の追加");
            Data.Tasks.Add(t);
            Quick.Text = "";
            Changed("「" + t.Title + "」を追加しました (" + TaskLogic.DueText(t, Now) + ")");
            Reveal(t);
            return t;
        }

        void QuickDetail()
        {
            var r = ParseQuick();
            var t = Quick.Text.Trim().Length == 0 ? null : r.ToTask(Now);
            if (t != null && t.Title.Length == 0) t.Title = Quick.Text.Trim();
            int before = Data.Tasks.Count;
            AddNew(Tabs.SelectedIndex == 1 ? (DateTime?)Calendar.Day : null, false, t);
            if (Data.Tasks.Count > before) Quick.Text = "";
        }

        // ================================================================ 行の描き方 (今日・一覧で共通)

        Rectangle CheckRect(Rectangle r)
        {
            int d = Theme.S(18);
            return new Rectangle(r.X + Theme.S(12), r.Y + (r.Height - d) / 2, d, d);
        }

        void PaintTask(Graphics g, Rectangle r, object o, bool sel, bool showReason)
        {
            var t = (TaskItem)o;
            var now = Now;
            var cr = CheckRect(r);
            using (var path = Theme.RoundRect(cr, cr.Width / 2f))
            {
                if (t.Done) { using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path); }
                else using (var p = new Pen(t.Priority == TaskItem.High ? Theme.Danger : Theme.BorderStrong, Theme.S(2))) g.DrawPath(p, path);
            }
            if (t.Done)
                using (var p = new Pen(Color.White, Theme.S(2)))
                    g.DrawLines(p, new[] { new Point(cr.X + cr.Width / 4, cr.Y + cr.Height / 2), new Point(cr.X + cr.Width * 2 / 5, cr.Y + cr.Height * 2 / 3), new Point(cr.Right - cr.Width / 4, cr.Y + cr.Height / 3) });

            int pad = Theme.S(12), lh = Theme.Body.Height + Theme.S(2);
            int x = cr.Right + Theme.S(12);
            // 右: 期限
            string due = TaskLogic.DueText(t, now);
            bool over = TaskLogic.IsOverdue(t, now);
            bool today = t.HasDue && t.DueDate.Value == now.Date && !t.Done;
            int dw = Draw.Width(g, due, Theme.Bold) + Theme.S(4);
            Draw.Text(g, due, Theme.Bold, t.Done ? Theme.Disabled : over ? Theme.Danger : today ? Theme.Accent : t.HasDue ? Theme.Text : Theme.SubText, new Rectangle(r.Right - pad - dw, r.Y + Theme.S(5), dw, lh), TextFormatFlags.Right);
            if (over) Draw.Text(g, "期限切れ", Theme.Small, Theme.Danger, new Rectangle(r.Right - pad - dw, r.Y + Theme.S(5) + lh, dw, lh), TextFormatFlags.Right);
            else if (t.Repeat != Repeat.None) Draw.Text(g, "↻ " + TaskLogic.RepeatLabel(t.Repeat), Theme.Small, Theme.SubText, new Rectangle(r.Right - pad - dw - Theme.S(40), r.Y + Theme.S(5) + lh, dw + Theme.S(40), lh), TextFormatFlags.Right);
            // 1 行目: タイトル
            int tw = r.Right - pad - dw - Theme.S(10) - x;
            using (var f = t.Done ? new Font(Theme.Body, FontStyle.Strikeout) : null)
                Draw.Text(g, t.Title, t.Done ? f : Theme.Body, t.Done ? Theme.SubText : Theme.Text, new Rectangle(x, r.Y + Theme.S(5), tw, lh));
            // 2 行目: 優先度・時間・小タスク・タグ・理由
            int cy = r.Y + Theme.S(5) + lh + lh / 2;
            int cx = x;
            int limit = r.Right - pad - dw - Theme.S(48);
            if (!t.Done)
            {
                var pc = t.Priority == TaskItem.High ? Theme.Danger : t.Priority == TaskItem.Mid ? Theme.Accent : Theme.SubText;
                var pb = t.Priority == TaskItem.High ? Theme.DangerSoft : t.Priority == TaskItem.Mid ? Theme.AccentSoft : Theme.SurfaceAlt;
                cx = Draw.Chip(g, "優先度 " + TaskLogic.PriorityLabel(t.Priority), pc, pb, cx, cy, false) + Theme.S(4);
            }
            var bits = new List<string>();
            if (t.DurationMin > 0) bits.Add(Fmt.Minutes(t.DurationMin));
            if (t.Subtasks.Count > 0) bits.Add("小タスク " + t.Subtasks.Count(s => s.Done) + "/" + t.Subtasks.Count);
            foreach (var tg in t.Tags) bits.Add("#" + tg);
            if (showReason && !t.Done)
            {
                var sc = TaskLogic.Score(t, now);
                if (sc.Reasons.Count > 0) bits.Add(sc.Reasons[0]);
            }
            if (t.Done && t.DoneTime.HasValue) bits.Insert(0, "完了 " + Fmt.MonthDay(t.DoneTime.Value) + " " + Fmt.Time(t.DoneTime.Value));
            Draw.Text(g, string.Join("  ・  ", bits), Theme.Small, Theme.SubText, new Rectangle(cx + Theme.S(2), cy - lh / 2, Math.Max(0, limit - cx), lh));
        }

        void WireTaskList(RowList list)
        {
            list.DoubleClick += (s, e) =>
            {
                var p = list.PointToClient(Cursor.Position);
                int i = list.IndexFromPoint(p);
                if (i < 0 || CheckRect(list.GetItemRectangle(i)).Contains(p)) return;
                Edit(list.SelectedItem as TaskItem);
            };
            list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int i = list.IndexFromPoint(e.Location);
                if (i < 0) return;
                var r = CheckRect(list.GetItemRectangle(i));
                r.Inflate(Theme.S(6), Theme.S(6));
                if (r.Contains(e.Location)) Toggle(list.Items[i] as TaskItem);
            };
            list.MouseMove += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                list.Cursor = i >= 0 && CheckRect(list.GetItemRectangle(i)).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            };
            list.KeyDown += (s, e) =>
            {
                var t = list.SelectedItem as TaskItem;
                if (t == null) return;
                if (e.KeyCode == Keys.Enter) { Edit(t); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Space) { Toggle(t); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { Delete(t); e.Handled = true; }
            };
        }

        FlowLayoutPanel TaskButtons(Func<TaskItem> current, bool withPostpone)
        {
            var bDone = new FlatButton("完了 / 戻す", ButtonKind.Secondary).FitWidth(88);
            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(72);
            var bLater = new FlatButton("明日に回す", ButtonKind.Secondary).FitWidth(88);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
            bDone.Click += (s, e) => Toggle(current());
            bEdit.Click += (s, e) => Edit(current());
            bLater.Click += (s, e) => Postpone(current());
            bDel.Click += (s, e) => Delete(current());
            tips.SetToolTip(bDone, "完了にする・未完了に戻す (Space / 左の丸をクリック)");
            tips.SetToolTip(bEdit, "編集 (Enter / ダブルクリック)");
            tips.SetToolTip(bLater, "期限を明日にする (時刻はそのまま)");
            tips.SetToolTip(bDel, "削除 (Delete)");
            var row = withPostpone ? Lay.Flow(bDone, bEdit, bLater, bDel) : Lay.Flow(bDone, bEdit, bDel);
            row.Dock = DockStyle.Bottom;
            return row;
        }

        // ================================================================ 今日

        void BuildToday(Panel v)
        {
            todayEmpty.Dock = DockStyle.Top;
            todayEmpty.Padding = Theme.P(20, 16, 20, 16);
            var t1 = new WrapLabel { Text = "まだタスクがありません", Font = Theme.Title, ForeColor = Theme.Text };
            var t2 = new WrapLabel { Text = "上の入力欄に「明日15時 歯医者」「金曜までにレポート 重要」のように書いて Enter を押すと追加できます。\n今日やることは、期限の近さ・優先度・かかる時間から「おすすめ順」に並びます。どんな画面になるか見たいときは、見本データを入れて試せます (あとで削除できます)。", Font = Theme.Body, ForeColor = Theme.SubText };
            var add = new FlatButton("＋ 詳しく入力して追加", ButtonKind.Primary).FitWidth(120);
            var sample = new FlatButton("見本データを入れる", ButtonKind.Secondary).FitWidth(120);
            add.Click += (s, e) => AddNew(null, false);
            sample.Click += (s, e) => InsertSamples();
            Lay.Stack(todayEmpty, t1, Ui.Spacer(6), t2, Ui.Spacer(12), Lay.Flow(add, sample));
            todayEmpty.Resize += (s, e) => FitHeight(todayEmpty);

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            todaySummary.Dock = DockStyle.Top; todaySummary.Font = Theme.Body; todaySummary.Padding = Theme.P(4, 2, 4, 6);
            TodayList.PaintRow = (g, r, o, sel) => PaintTask(g, r, o, sel, true);
            TodayList.EmptyText = "今日が期限のタスクはありません。\n上の入力欄から追加するか、「カレンダー」「一覧」で先の予定を見られます。";
            WireTaskList(TodayList);
            card.Controls.Add(TodayList);
            card.Controls.Add(Ui.Spacer(6));
            card.Controls.Add(todaySummary);
            card.Controls.Add(TaskButtons(() => TodayList.SelectedItem as TaskItem, true));
            v.Controls.Add(card);
            v.Controls.Add(todayEmpty);
        }

        static void FitHeight(Panel p)
        {
            int h = p.Padding.Vertical;
            foreach (Control c in p.Controls) if (c.Visible) h += c.Height;
            if (p.Height != h) p.Height = h;
        }

        void RefreshToday()
        {
            var now = Now;
            bool empty = Data.Tasks.Count == 0;
            todayEmpty.Visible = empty;
            if (empty) FitHeight(todayEmpty);
            var open = TaskLogic.TodayList(Data.Tasks, now);
            var done = TaskLogic.DoneToday(Data.Tasks, now);
            int over = open.Count(t => TaskLogic.IsOverdue(t, now));
            int minutes = open.Sum(t => t.DurationMin);
            var parts = new List<string>();
            parts.Add(Fmt.Date(now));
            parts.Add("残り " + open.Count + " 件" + (over > 0 ? " (うち期限切れ " + over + " 件)" : ""));
            if (minutes > 0) parts.Add("かかる時間の合計 " + Fmt.Minutes(minutes));
            parts.Add("今日終わらせた " + done.Count + " 件");
            todaySummary.ForeColor = over > 0 ? Theme.Danger : Theme.Text;
            todaySummary.Text = string.Join("  ・  ", parts) + "\n上から順にやるのがおすすめです (期限の近さ・優先度・すぐ終わるかで並べています)";
            var items = new List<object>();
            items.AddRange(open.Cast<object>());
            items.AddRange(done.Cast<object>());
            var sel = TodayList.SelectedItem as TaskItem;
            TodayList.SetItems(items, o => sel != null && ((TaskItem)o).Id == sel.Id);
        }

        // ================================================================ カレンダー

        void BuildCalendar(Panel v)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            var prev = new FlatButton("◀", ButtonKind.Secondary).FitWidth(36);
            var next = new FlatButton("▶", ButtonKind.Secondary).FitWidth(36);
            var today = new FlatButton("今日", ButtonKind.Secondary).FitWidth(56);
            prev.Click += (s, e) => { Calendar.Navigate(-1); Calendar.Focus(); };
            next.Click += (s, e) => { Calendar.Navigate(1); Calendar.Focus(); };
            today.Click += (s, e) => { Calendar.GoToday(); Calendar.Focus(); };
            tips.SetToolTip(prev, "前へ (PageUp)"); tips.SetToolTip(next, "次へ (PageDown)"); tips.SetToolTip(today, "今日へ (Home)");
            periodLabel.Font = Theme.Header; periodLabel.ForeColor = Theme.Text; periodLabel.Padding = Theme.P(10, 2, 0, 0);
            CalMode.Width = Theme.S(170); CalMode.BackColor = Theme.Surface;
            CalMode.SelectedChanged += (s, e) => { Calendar.Mode = (TealSuite.Tasks.CalMode)CalMode.SelectedIndex; Calendar.Focus(); };
            var nav = Lay.Flow(prev, today, next, periodLabel);
            var bar = new Panel { Dock = DockStyle.Top, Height = Math.Max(nav.PreferredSize.Height, CalMode.Height) + Theme.S(4) };
            nav.Dock = DockStyle.Left; nav.AutoSize = true;
            CalMode.Dock = DockStyle.Right;
            bar.Controls.Add(nav); bar.Controls.Add(CalMode);
            var hint = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.Small.Height + Theme.S(8), Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false, TextAlign = ContentAlignment.BottomLeft,
                Text = "日の「＋」かダブルクリックで追加 ・ ドラッグで日時を変更 (週・日は 15 分きざみ) ・ ←→↑↓ 日を移動 ・ PageUp/PageDown 前後 ・ Space 完了" };

            Calendar.Source = () => Data.Tasks;
            Calendar.Clock = () => Now;
            Calendar.ViewChanged += (s, e) => { UpdatePeriod(); UpdatePreview(); };
            Calendar.AddRequested += (day, withTime) => AddNew(day, withTime);
            Calendar.TaskOpened += t => Edit(t);
            Calendar.TaskToggled += t => Toggle(t);
            Calendar.TaskMoved += (t, when, withTime) => MoveTask(t, when, withTime);
            Calendar.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && Calendar.SelectedTask != null) { Delete(Calendar.SelectedTask); e.Handled = true; } };

            card.Controls.Add(Calendar);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(bar);
            card.Controls.Add(hint);
            v.Controls.Add(card);
        }

        void UpdatePeriod() { periodLabel.Text = Calendar.PeriodText; }

        // ================================================================ 一覧

        void BuildList(Panel v)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            Search.Font = Theme.Body; Search.BorderStyle = BorderStyle.None;
            Ui.SetCue(Search, "タイトル・タグ・メモで検索…  (Ctrl+F)");
            Search.TextChanged += (s, e) => RefreshList();
            Search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Search.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { List.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            filter.Width = Theme.S(110); tagFilter.Width = Theme.S(130); sort.Width = Theme.S(140);
            tagFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            filter.SelectedIndexChanged += (s, e) => RefreshList();
            tagFilter.SelectedIndexChanged += (s, e) => RefreshList();
            sort.SelectedIndexChanged += (s, e) => RefreshList();
            tips.SetToolTip(filter, "表示するもの"); tips.SetToolTip(tagFilter, "タグで絞り込む"); tips.SetToolTip(sort, "並べ方");
            var top = Lay.Row(new InputFrame(Search), sort, tagFilter, filter);
            top.Dock = DockStyle.Top;

            List.PaintRow = (g, r, o, sel) => PaintTask(g, r, o, sel, false);
            List.EmptyText = "該当するタスクはありません";
            WireTaskList(List);

            card.Controls.Add(List);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(top);
            card.Controls.Add(TaskButtons(() => List.SelectedItem as TaskItem, true));
            v.Controls.Add(card);
        }

        void RefreshTags()
        {
            var tags = TaskLogic.AllTags(Data.Tasks);
            var cur = tagFilter.SelectedIndex > 0 ? tagFilter.SelectedItem as string : null;
            var want = new List<string> { "すべてのタグ" };
            want.AddRange(tags.Select(t => "#" + t));
            if (tagFilter.Items.Cast<string>().SequenceEqual(want)) return;
            tagFilter.BeginUpdate();
            tagFilter.Items.Clear();
            foreach (var w in want) tagFilter.Items.Add(w);
            int idx = cur == null ? 0 : want.IndexOf(cur);
            tagFilter.SelectedIndex = Math.Max(0, idx);
            tagFilter.EndUpdate();
        }

        public void RefreshList()
        {
            var now = Now;
            var today = now.Date;
            var week = TaskLogic.WeekStart(today);
            IEnumerable<TaskItem> q = Data.Tasks;
            switch (filter.SelectedIndex)
            {
                case 0: q = q.Where(t => !t.Done); break;
                case 1: q = q.Where(t => TaskLogic.IsOverdue(t, now)); break;
                case 2: q = q.Where(t => !t.Done && t.HasDue && t.DueDate.Value <= today); break;
                case 3: q = q.Where(t => t.HasDue && t.DueDate.Value >= week && t.DueDate.Value < week.AddDays(7)); break;
                case 4: q = q.Where(t => !t.Done && !t.HasDue); break;
                case 5: q = q.Where(t => t.Done); break;
            }
            if (tagFilter.SelectedIndex > 0)
            {
                var tag = ((string)tagFilter.SelectedItem).Substring(1);
                q = q.Where(t => t.Tags.Contains(tag));
            }
            var words = Search.Text.Trim().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w0 in words)
            {
                var w = w0.TrimStart('#', '＃');
                q = q.Where(t => Has(t.Title, w) || Has(t.Memo, w) || t.Tags.Any(x => Has(x, w)) || t.Subtasks.Any(x => Has(x.Text, w)));
            }
            switch (sort.SelectedIndex)
            {
                case 1: q = q.OrderBy(t => t.Done).ThenByDescending(t => TaskLogic.Score(t, now).Score).ThenBy(t => t.Due ?? "9999"); break;
                case 2: q = q.OrderBy(t => t.Done).ThenByDescending(t => t.Priority).ThenBy(t => t.Due ?? "9999"); break;
                case 3: q = q.OrderByDescending(t => t.Created ?? ""); break;
                default: q = q.OrderBy(t => t.Done).ThenBy(t => t.HasDue ? 0 : 1).ThenBy(t => t.DueAt ?? DateTime.MaxValue).ThenByDescending(t => t.Priority); break;
            }
            if (filter.SelectedIndex == 5) q = q.OrderByDescending(t => t.DoneAt ?? "");
            var sel = List.SelectedItem as TaskItem;
            List.SetItems(q.ToList(), o => sel != null && ((TaskItem)o).Id == sel.Id);
        }

        static bool Has(string s, string w) { return s != null && s.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0; }

        // ================================================================ 統計

        void BuildStats(Panel v)
        {
            var tiles = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, RowCount = 1, Height = TileToday.PreferredHeight, BackColor = Theme.Background };
            for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            foreach (var t in new[] { TileToday, TileWeek, TileRate, TileOpen }) { t.Dock = DockStyle.Fill; t.Margin = new Padding(0, 0, Theme.S(8), 0); tiles.Controls.Add(t); }
            TileOpen.Margin = Padding.Empty;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = Theme.P(0, 8, 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var c1 = new CardPanel("最近 14 日の完了数") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(8), 0) };
            c1.Controls.Add(daily);
            var c2 = new CardPanel("タグ別 (終わった数 / 全部)") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            c2.Controls.Add(tagChart);
            grid.Controls.Add(c1, 0, 0);
            grid.Controls.Add(c2, 1, 0);
            v.Controls.Add(grid);
            v.Controls.Add(tiles);
        }

        void RefreshStats()
        {
            var st = TaskLogic.Compute(Data.Tasks, Now);
            TileToday.Set("今日終わらせた", st.DoneToday + " 件", "今月は " + st.DoneMonth + " 件", Theme.Accent);
            TileWeek.Set("今週 (月曜から) 終わらせた", st.DoneWeek + " 件", "1 日あたり " + (st.DoneWeek / (double)(((int)Now.DayOfWeek + 6) % 7 + 1)).ToString("0.0") + " 件");
            TileRate.Set("今週が期限のものの達成率", st.WeekRate < 0 ? "—" : Math.Round(st.WeekRate * 100) + "%", st.WeekTotal > 0 ? st.WeekDone + " / " + st.WeekTotal + " 件" : "今週が期限のタスクはありません",
                st.WeekRate >= 0.8 ? Theme.Success : (Color?)null);
            TileOpen.Set("残っているタスク", st.Open + " 件", st.Overdue > 0 ? "うち期限切れ " + st.Overdue + " 件" : "期限切れはありません", st.Overdue > 0 ? Theme.Danger : (Color?)null);
            daily.SetData(st.Daily, Now.Date);
            tagChart.SetData(st.ByTag);
        }
    }

    /// <summary>縦棒グラフ (日ごとの完了数)</summary>
    public class ColumnChart : Control
    {
        List<KeyValuePair<DateTime, int>> items = new List<KeyValuePair<DateTime, int>>();
        DateTime today;
        public ColumnChart() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        public void SetData(List<KeyValuePair<DateTime, int>> list, DateTime today) { items = list; this.today = today; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (items.Count == 0) return;
            int max = Math.Max(1, items.Max(x => x.Value));
            int labelH = Theme.Small.Height * 2 + Theme.S(6), valueH = Theme.Small.Height + Theme.S(4);
            int top = valueH, bottom = Height - labelH;
            float cw = Width / (float)items.Count;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, bottom, Width, bottom);
            for (int i = 0; i < items.Count; i++)
            {
                var kv = items[i];
                int bh = (int)((bottom - top) * kv.Value / (double)max);
                int bw = (int)Math.Max(Theme.S(4), cw * 0.6f);
                int x = (int)(i * cw + (cw - bw) / 2);
                var color = kv.Key == today ? Theme.Accent : Theme.AccentLight;
                if (bh > 0)
                    using (var b = new SolidBrush(color))
                    using (var path = Theme.RoundRect(new RectangleF(x, bottom - bh, bw, bh), Math.Min(Theme.S(3), bh / 2f)))
                        g.FillPath(b, path);
                var cell = new Rectangle((int)(i * cw), 0, (int)cw, Height);
                if (kv.Value > 0) TextRenderer.DrawText(g, kv.Value.ToString(), Theme.Small, new Rectangle(cell.X, bottom - bh - valueH, cell.Width, valueH), Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);
                bool showLabel = cw >= Theme.S(30) || i % 2 == (items.Count - 1) % 2;
                if (showLabel)
                {
                    var c = kv.Key == today ? Theme.Accent : kv.Key.DayOfWeek == DayOfWeek.Sunday ? Theme.Danger : Theme.SubText;
                    TextRenderer.DrawText(g, kv.Key.Day.ToString(), kv.Key == today ? Theme.Bold : Theme.Small, new Rectangle(cell.X, bottom + Theme.S(3), cell.Width, Theme.Small.Height + 2), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                    TextRenderer.DrawText(g, Fmt.DayOfWeek(kv.Key), Theme.Small, new Rectangle(cell.X, bottom + Theme.S(3) + Theme.Small.Height + 1, cell.Width, Theme.Small.Height + 2), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                }
            }
            if (items.All(x => x.Value == 0))
                TextRenderer.DrawText(g, "まだ完了したタスクはありません", Theme.Body, new Rectangle(0, 0, Width, bottom), Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>タグ別の横棒 (全部の数の上に終わった数を重ねる)</summary>
    public class TagChart : Control
    {
        List<Tuple<string, int, int>> items = new List<Tuple<string, int, int>>();
        public TagChart() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        public void SetData(List<Tuple<string, int, int>> list) { items = list; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (items.Count == 0)
            {
                Draw.Text(g, "タグの付いたタスクはありません (#仕事 のように書くとタグになります)", Theme.Body, Theme.SubText, new Rectangle(0, 0, Width, Theme.S(30)));
                return;
            }
            int rowH = Theme.Body.Height + Theme.S(14);
            int maxRows = Math.Max(1, Height / rowH);
            var list = items.Take(maxRows).ToList();
            int labelW = 0;
            foreach (var it in list) labelW = Math.Max(labelW, Draw.Width(g, "#" + it.Item1, Theme.Body));
            labelW = Math.Min(labelW + Theme.S(8), Width / 3);
            int valueW = Draw.Width(g, "000 / 000", Theme.Small) + Theme.S(6);
            int barMax = Math.Max(Theme.S(20), Width - labelW - valueW - Theme.S(8));
            int max = Math.Max(1, list.Max(x => x.Item3));
            int y = 0;
            foreach (var it in list)
            {
                Draw.Text(g, "#" + it.Item1, Theme.Body, Theme.Text, new Rectangle(0, y, labelW - Theme.S(4), rowH));
                int all = (int)Math.Max(Theme.S(3), barMax * it.Item3 / (double)max);
                int done = it.Item3 == 0 ? 0 : (int)(all * it.Item2 / (double)it.Item3);
                var rr = new RectangleF(labelW, y + Theme.S(6), all, rowH - Theme.S(12));
                using (var b = new SolidBrush(Theme.SurfaceAlt)) using (var path = Theme.RoundRect(rr, Theme.S(3))) g.FillPath(b, path);
                if (done > 0) using (var b = new SolidBrush(Theme.Accent)) using (var path = Theme.RoundRect(new RectangleF(labelW, y + Theme.S(6), done, rowH - Theme.S(12)), Theme.S(3))) g.FillPath(b, path);
                Draw.Text(g, it.Item2 + " / " + it.Item3, Theme.Small, Theme.SubText, new Rectangle(labelW + all + Theme.S(6), y, valueW, rowH));
                y += rowH;
            }
        }
    }
}
