// Teal Plan (タスク＆予定。1.9 までの Teal Tasks) - メイン画面
//  上: ひとこと入力 (「明日15時 歯医者 #病院」→ タスク、「明日 15:00-16:00 歯医者 @駅前」→ 予定。Ctrl+Enter で詳しく入力)
//  タブ: 今日 (今日の予定の時間割 + タスク) / カレンダー / 一覧 (タスク・予定表・すべて) / 統計 (Ctrl+1〜4)
//  Ctrl+N タスクを追加 / Ctrl+E 予定を追加 / Ctrl+L ひとこと入力へ / Ctrl+F 検索 / Space 完了 / Enter 編集 / Delete 削除 / Ctrl+Z 元に戻す
//  一覧の小タスク: ▸/▾ (→/←) で開く・閉じる、Tab で追加、F2 で名前の変更、Alt+↑↓ で並べ替え、Ctrl+Shift+→/← ですべて開く・閉じる
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
        public readonly CalendarView TodayCal = new CalendarView { Dock = DockStyle.Fill };
        readonly Label todayCalTitle = new Label { AutoSize = false, UseMnemonic = false };
        // カレンダー
        public readonly CalendarView Calendar = new CalendarView { Dock = DockStyle.Fill };
        public readonly SegmentTabs CalMode = new SegmentTabs("月", "週", "日");
        readonly Label periodLabel = new Label { AutoSize = true, UseMnemonic = false };
        public readonly ComboBox CalShow = Lay.Combo("すべて表示", "タスクだけ", "予定だけ");
        // 一覧
        public readonly TextBox Search = new TextBox();
        readonly ComboBox filter = Lay.Combo("未完了", "期限切れ", "今日まで", "今週", "期限なし", "完了", "すべて");
        readonly ComboBox tagFilter = Lay.Combo("すべてのタグ");
        readonly ComboBox sort = Lay.Combo("期限が近い順", "おすすめ順", "優先度順", "追加した順");
        public readonly RowList List = new RowList(2) { Dock = DockStyle.Fill };
        public readonly SegmentTabs ListKind = new SegmentTabs("タスク", "予定", "すべて");
        public readonly ComboBox AgendaRange = Lay.Combo("これから 30 日", "これから 90 日", "今週", "今月", "過去 30 日");
        bool loading;
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
            var btnEvent = header.AddAction(new FlatButton("＋ 予定", ButtonKind.Secondary).FitWidth(80));
            btnEvent.Click += (s, e) => AddEventAt(Tabs.SelectedIndex == 1 ? Calendar.Day : Now.Date, true);
            tips.SetToolTip(btnEvent, "予定を追加 (Ctrl+E)");
            var btnAdd = header.AddAction(new FlatButton("＋ タスク", ButtonKind.Primary).FitWidth(96));
            btnAdd.Click += (s, e) => AddNew(null, false);
            btnSettings.Click += (s, e) => OpenSettings();
            tips.SetToolTip(btnAdd, "タスクを詳しく入力して追加 (Ctrl+N)");
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
            PlanTagSource.Get = () => SuiteTags.Collect(SuiteRead.PlanId, Data.Tasks.SelectMany(t => t.Tags).Concat(Data.Events.SelectMany(ev => ev.Tags ?? new List<string>())));
            ApplyWalletSource();
            AppHost.OpenRequested = r => OpenFromRequest(r);
            InlineEditor.KeyDown += (s, e) => { if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { CommitInline(false); e.Handled = true; } };
            InlineEditor.LostFocus += (s, e) => { if (inlineOpen) CommitInline(false); };

            WindowMemory.Attach(this, 1080, 780);
            remindTimer.Interval = 5000;
            remindTimer.Tick += (s, e) => { remindTimer.Interval = 30 * 1000; if (lastCheckDay != Now.Date) RefreshAll(); CheckReminders(); };
            Load += (s, e) =>
            {
                ApplyViewSettings();
                ApplyStartArgs();
                RefreshAll();
                remindTimer.Start();
            };
            Shown += (s, e) => { if (Tabs.SelectedIndex == 0) Quick.Focus(); };
            Activated += (s, e) =>
            {
                if (lastCheckDay != Now.Date) { RefreshAll(); CheckReminders(); }
                else if (WalletChanged()) { Calendar.Reload(); RefreshList(); RefreshToday(); }   // Teal Wallet でサブスクを変えたあと
            };
            Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
            FormClosed += (s, e) => { Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged; remindTimer.Stop(); };
        }

        void OnTimeChanged(object sender, EventArgs e) { AppHost.OnUi(() => { RefreshAll(); CheckReminders(); }); }

        /// <summary>覚えている表示 (一覧の種類・予定表の期間・カレンダーに出すもの) を画面に</summary>
        public void ApplyViewSettings()
        {
            loading = true;
            try
            {
                ListKind.SelectedIndex = Math.Max(0, Math.Min(2, Data.Settings.ListKind));
                AgendaRange.SelectedIndex = Math.Max(0, Math.Min(AgendaRange.Items.Count - 1, Data.Settings.AgendaRange));
                CalShow.SelectedIndex = Math.Max(0, Math.Min(2, Data.Settings.CalShow));
            }
            finally { loading = false; }
        }

        /// <summary>--tab=today|calendar|list|stats (1〜4)、--cal=month|week|day、--list=tasks|events|all (画面写真用)</summary>
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
                if (a.StartsWith("--list="))
                {
                    switch (a.Substring(7)) { case "tasks": Data.Settings.ListKind = 0; break; case "events": Data.Settings.ListKind = 1; break; case "all": Data.Settings.ListKind = 2; break; }
                    ApplyViewSettings();
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
            CommitInline(false);
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
            CancelInline();
            var keepTemplates = Data.Templates; // テンプレート・開いている小タスク・表示の設定は元に戻す対象にしない
            var keepExpanded = Data.Expanded;
            var keepSt = Data.Settings;
            Data = DataStore.FromJson<TaskData>(undoData);
            Data.Templates = keepTemplates;
            Data.Expanded = keepExpanded;
            Data.Settings.ListKind = keepSt.ListKind; Data.Settings.AgendaRange = keepSt.AgendaRange; Data.Settings.CalShow = keepSt.CalShow;
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
            Calendar.Reload(); UpdatePeriod();
            RefreshTags();
            RefreshList();
            RefreshStats();
            UpdatePreview();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (inlineOpen && InlineEditor.Focused)
            {
                switch (keyData)
                {
                    case Keys.Enter: CommitInline(true); return true;
                    case Keys.Tab: CommitInline(true); return true;
                    case Keys.Escape: CancelInline(); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (TodayList.Focused && HandleListKey(TodayList, keyData)) return true;
            if (List.Focused && HandleListKey(List, keyData)) return true;
            if ((Calendar.Focused || TodayCal.Focused) && keyData == Keys.Delete)
            {
                var cv = Calendar.Focused ? Calendar : TodayCal;
                if (cv.SelectedItem != null) { DeleteItem(cv.SelectedItem); return true; }
            }
            switch (keyData)
            {
                case Keys.Control | Keys.E: AddEventAt(Tabs.SelectedIndex == 1 ? Calendar.Day : Now.Date, true); return true;
                case Keys.Control | Keys.Shift | Keys.Right: if (ActiveControl is TextBox) break; ExpandAll(); return true;
                case Keys.Control | Keys.Shift | Keys.Left: if (ActiveControl is TextBox) break; CollapseAll(); return true;
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

        /// <summary>選んでいるタスク (小タスクの行なら親のタスク)</summary>
        TaskItem Current()
        {
            if (Tabs.SelectedIndex == 0) { var r = RowOf(TodayList); return r != null ? r.Task : TodayCal.SelectedTask; }
            if (Tabs.SelectedIndex == 1) return Calendar.SelectedTask;
            if (Tabs.SelectedIndex == 2) { var r = RowOf(List); return r != null ? r.Task : null; }
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
            if (!Ask("「" + t.Title + "」を削除しますか?" + (t.Subtasks.Count > 0 ? " (小タスク " + t.Subtasks.Count + " 件も消えます)" : ""))) return;
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
            SelectKey(TodayList, "t:" + t.Id);
            SelectKey(List, "t:" + t.Id);
            if (Tabs.SelectedIndex == 1 && t.HasDue) { Calendar.SelectedTask = t; Calendar.Day = t.DueDate.Value; }
        }

        // ================================================================ 1.10.0: 予定

        /// <summary>テスト用: 予定のダイアログの代わり (編集してから true で保存)。くり返しの選択の代わり (0 この回だけ / 1 すべて / -1 やめる)</summary>
        public Func<EventItem, bool> EventFormHook;
        public Func<string, int> SeriesHook;

        /// <summary>予定を追加 (ダイアログで確かめる)。end は時刻つきなら終わりの時刻、終日なら最後の日</summary>
        public EventItem AddEvent(DateTime start, DateTime end, bool allDay, EventItem prefill = null)
        {
            var e = prefill ?? new EventItem();
            if (prefill == null)
            {
                if (!allDay && end <= start) end = start.AddHours(1);
                if (allDay && end < start) end = start;
                e.SetTimes(start, end, allDay);
                e.RemindMin = allDay ? -1 : Data.Settings.EventRemind;
            }
            e.Created = Fmt.IsoTime(Now);
            bool ok;
            if (EventFormHook != null) ok = EventFormHook(e);
            else using (var f = new EventEditForm(e, true)) ok = f.ShowDialog(this) == DialogResult.OK;
            if (!ok || string.IsNullOrWhiteSpace(e.Title)) return null;
            return AddEventDirect(e);
        }

        /// <summary>その日に予定を追加 (今日なら次のちょうどの時刻から 1 時間、ほかの日は 10:00)</summary>
        public EventItem AddEventAt(DateTime day, bool pickTime)
        {
            var d = day.Date;
            DateTime s;
            if (!pickTime && day.TimeOfDay != TimeSpan.Zero) s = day;
            else if (d == Now.Date) s = d.AddHours(Math.Min(23, Now.Hour + 1));
            else s = d.AddHours(10);
            return AddEvent(s, s.AddHours(1), false);
        }

        public EventItem AddEventDirect(EventItem e)
        {
            if (e.Created == null) e.Created = Fmt.IsoTime(Now);
            Remember("予定「" + e.Title + "」の追加");
            Data.Events.Add(e);
            Changed("予定「" + e.Title + "」を追加しました (" + EventLogic.WhenText(e) + ")");
            RevealItem("e:" + e.Id + ":" + Fmt.IsoTime(e.StartAt), e.StartAt);
            return e;
        }

        /// <summary>予定を編集 (くり返しのときはすべての回が変わる)</summary>
        public bool EditEvent(EventItem e)
        {
            if (e == null) return false;
            var copy = e.Clone();
            DialogResult res;
            if (EventFormHook != null) res = EventFormHook(copy) ? DialogResult.OK : DialogResult.Cancel;
            else using (var f = new EventEditForm(copy, false)) res = f.ShowDialog(this);
            if (res == DialogResult.Abort) return DeleteEvent(e, null);
            if (res != DialogResult.OK) return false;
            Remember("予定「" + e.Title + "」の編集");
            int i = Data.Events.IndexOf(e);
            if (i >= 0) Data.Events[i] = copy; else Data.Events.Add(copy);
            Changed("予定「" + copy.Title + "」を保存しました (" + EventLogic.WhenText(copy) + ")");
            return true;
        }

        int AskSeries(string what, string title)
        {
            var msg = "「" + title + "」はくり返す予定です。どの回を" + what + "か選んでください。";
            if (SeriesHook != null) return SeriesHook(msg);
            using (var f = new SeriesChoiceForm("くり返す予定", msg)) return f.ShowDialog(this) == DialogResult.OK ? f.Choice : -1;
        }

        /// <summary>予定を削除。くり返す予定で occ を渡すと「この回だけ / すべて」を選ぶ</summary>
        public bool DeleteEvent(EventItem e, DateTime? occ)
        {
            if (e == null) return false;
            if (e.Repeat != Repeat.None && occ.HasValue)
            {
                int c = AskSeries("削除する", e.Title);
                if (c < 0) return false;
                if (c == 0)
                {
                    Remember("予定「" + e.Title + "」(" + Fmt.MonthDay(occ.Value) + ") の削除");
                    if (e.Skip == null) e.Skip = new List<string>();
                    var k = Fmt.Iso(occ.Value.Date);
                    if (!e.Skip.Contains(k)) e.Skip.Add(k);
                    Changed("予定「" + e.Title + "」の " + Fmt.MonthDay(occ.Value) + " の回だけ削除しました");
                    return true;
                }
            }
            else if (!Ask("予定「" + e.Title + "」を削除しますか?")) return false;
            Remember("予定「" + e.Title + "」の削除");
            Data.Events.Remove(e);
            Changed("予定「" + e.Title + "」を削除しました");
            return true;
        }

        public void OpenItem(CalItem x)
        {
            if (x == null) return;
            if (x.Task != null) Edit(x.Task);
            else if (x.Event != null) EditEvent(x.Event);
            else if (WalletPlanSource.SubIdOf(x) != null) OpenInWallet(x);
            else footer.StatusText = (x.SourceName ?? "ほかのアプリ") + " の項目です (ここでは読むだけです): " + x.Title;
        }

        // ================================================================ 1.10.0: ほかのアプリとの連携

        public TagSuggest QuickTags { get; private set; }
        /// <summary>テスト用: Teal Wallet に頼む代わり (appId, kind, key)</summary>
        public Action<string, string, string> OpenRequestHook;
        DateTime walletStamp;

        /// <summary>設定に合わせて、Teal Wallet の支払い日をカレンダーに出す・出さない</summary>
        public void ApplyWalletSource()
        {
            ExternalSources.RemoveAll(x => x is WalletPlanSource);
            if (Data.Settings.ShowWallet) ExternalSources.Add(new WalletPlanSource(() => Now));
            walletStamp = WalletStamp();
        }

        static DateTime WalletStamp() { try { return System.IO.File.GetLastWriteTimeUtc(SuiteRead.WalletSubsPath); } catch { return DateTime.MinValue; } }

        bool WalletChanged()
        {
            if (!Data.Settings.ShowWallet) return false;
            var t = WalletStamp();
            if (t == walletStamp) return false;
            walletStamp = t;
            return true;
        }

        void OpenInWallet(CalItem x)
        {
            var id = WalletPlanSource.SubIdOf(x);
            if (OpenRequestHook != null) { OpenRequestHook(SuiteRead.WalletId, "sub", id); return; }
            var r = OpenRequest.Send(SuiteRead.WalletId, "sub", id);
            footer.StatusText = r == "missing" ? "Teal Wallet が見つかりません (Teal Hub から入れてください)"
                : r == "error" ? "Teal Wallet を開けませんでした" : "Teal Wallet で「" + x.Title + "」を開きます";
        }

        /// <summary>ほかのアプリ (Teal Hub の「今日」・検索) から「この項目を開いて」と頼まれた。見つかったら true</summary>
        public bool OpenFromRequest(OpenRequest r)
        {
            if (r == null || string.IsNullOrEmpty(r.Key)) return false;
            if (r.Kind == "task")
            {
                var t = Data.Tasks.FirstOrDefault(x => x.Id == r.Key);
                if (t == null) { footer.StatusText = "タスクが見つかりませんでした (消されたかもしれません)"; return false; }
                bool today = TaskLogic.TodayList(Data.Tasks, Now).Contains(t) || TaskLogic.DoneToday(Data.Tasks, Now).Contains(t);
                Tabs.SelectedIndex = today ? 0 : 2;
                if (!today && ListKind.SelectedIndex == 1) ListKind.SelectedIndex = 0;
                RefreshAll();
                Reveal(t);
                footer.StatusText = "「" + t.Title + "」を開きました";
                return true;
            }
            if (r.Kind == "event")
            {
                var e = Data.Events.FirstOrDefault(x => x.Id == r.Key);
                if (e == null) { footer.StatusText = "予定が見つかりませんでした (消されたかもしれません)"; return false; }
                var occ = EventLogic.Occurrences(e, Now.Date, Now.Date.AddYears(2), 1);
                var start = occ.Count > 0 ? occ[0] : e.StartAt;
                Tabs.SelectedIndex = 1;
                RevealItem("e:" + e.Id + ":" + Fmt.IsoTime(start), start);
                footer.StatusText = "予定「" + e.Title + "」を開きました";
                return true;
            }
            return false;
        }

        public void DeleteItem(CalItem x)
        {
            if (x == null) return;
            if (x.ReadOnly) { footer.StatusText = "ほかのアプリの項目は消せません (" + (x.SourceName ?? "") + " で変えてください)"; return; }
            if (x.Task != null) Delete(x.Task);
            else if (x.Event != null) DeleteEvent(x.Event, x.Start);
        }

        /// <summary>カレンダーで動かした (予定はくり返しごと同じだけずらす)</summary>
        public void MoveItem(CalItem x, DateTime when, bool withTime)
        {
            if (x == null || x.ReadOnly) return;
            if (x.Task != null) { MoveTask(x.Task, when, withTime); return; }
            var e = x.Event;
            if (e == null) return;
            var occ = x.Start;
            Remember("予定「" + e.Title + "」の移動");
            int dayShift = (when.Date - occ.Date).Days;
            if (withTime && !e.AllDay) e.SetTimes(e.StartAt + (when - occ), e.EndAt + (when - occ), false);
            else if (!withTime && e.AllDay)
            {
                var last = Fmt.ParseIso(e.End) ?? e.StartAt;
                e.SetTimes(e.StartAt.AddDays(dayShift), last.AddDays(dayShift), true);
            }
            else if (withTime && e.AllDay)
            {
                var s = e.StartAt.Date.AddDays(dayShift) + when.TimeOfDay;
                e.SetTimes(s, s.AddHours(1), false);
            }
            else
            {
                int span = (x.LastDay - x.Start.Date).Days;
                var s = e.StartAt.Date.AddDays(dayShift);
                e.SetTimes(s, s.AddDays(span), true);
            }
            if (e.Skip != null && dayShift != 0)
                e.Skip = e.Skip.Select(k => Fmt.ParseIso(k)).Where(d => d.HasValue).Select(d => Fmt.Iso(d.Value.AddDays(dayShift))).ToList();
            Changed("予定「" + e.Title + "」を " + EventLogic.WhenText(e) + " に動かしました" + (e.Repeat != Repeat.None ? " (くり返しの回もすべて)" : ""));
        }

        /// <summary>カレンダーで長さを変えた (予定は終わり、タスクはかかる時間)</summary>
        public void ResizeItem(CalItem x, DateTime end)
        {
            if (x == null || x.ReadOnly) return;
            if (x.Task != null)
            {
                var t = x.Task;
                int min = Math.Max(15, (int)Math.Round((end - x.Start).TotalMinutes));
                Remember("「" + t.Title + "」のかかる時間の変更");
                t.DurationMin = min;
                Changed("「" + t.Title + "」のかかる時間を " + Fmt.Minutes(min) + " にしました");
                return;
            }
            var e = x.Event;
            if (e == null || e.AllDay) return;
            var len = end - x.Start;
            if (len < TimeSpan.FromMinutes(15)) len = TimeSpan.FromMinutes(15);
            Remember("予定「" + e.Title + "」の長さの変更");
            e.SetTimes(e.StartAt, e.StartAt + len, false);
            Changed("予定「" + e.Title + "」を " + EventLogic.WhenText(e) + " にしました");
        }

        void RevealItem(string key, DateTime day)
        {
            if (Tabs.SelectedIndex == 1) { Calendar.Day = day.Date; Calendar.SelectedKey = key; Calendar.Invalidate(); }
            SelectKey(List, key);
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
            var tasks = TaskLogic.SampleData(Now);
            Data.Tasks.AddRange(tasks);
            Data.Events.AddRange(EventLogic.SampleData(Now));
            // 小タスクの見せ方がわかるように、小タスクのある最初のタスクを開いておく
            var withSubs = tasks.FirstOrDefault(t => t.Subtasks.Count > 0 && !t.Done);
            if (withSubs != null) Data.SetExpanded(withSubs, true);
            Changed("見本データ (タスクと予定) を入れました (不要になったら一覧から削除できます)");
        }

        void OpenSettings()
        {
            bool res = resident != null && resident.IsResident;
            using (var f = new TaskSettingsForm(Data.Settings, res, ExportCsv, Backup, Restore))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            Data.Normalize();
            ApplyWalletSource();
            Save(); RefreshAll();
            CheckReminders();
        }

        void ExportCsv()
        {
            var csv = TaskLogic.ToCsv(Data.Tasks);
            if (Data.Events.Count > 0) csv += "\r\n" + string.Join("\r\n", new[] { "予定,始まり,終わり,終日,場所,くり返し,タグ,メモ" }.Concat(Data.Events.Select(EventLogic.ToCsvLine))) + "\r\n";
            var p = FileKit.SaveText(this, csv, "タスクと予定-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", "CSV (*.csv)|*.csv");
            if (p != null) footer.StatusText = "CSV に書き出しました: " + p;
        }
        void Backup()
        {
            var p = FileKit.ExportJson(this, Data, "TealPlan");
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
            Dialogs.Info(this, "バックアップから タスク " + d.Tasks.Count + " 件・予定 " + d.Events.Count + " 件を戻しました。");
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
                shown = ctx.Notify("Teal Plan: お知らせ " + list.Count + " 件", text, ToolTipIcon.Info);
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
            Ui.SetCue(Quick, "例: 金曜までにレポート 重要 / 毎週月曜 ゴミ出し / 明日 15:00-16:00 歯医者 @駅前 (予定)  (Ctrl+L)");
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
            // 1.10.0: 「#」のあとに Teal Memo と Teal Plan のタグの候補を出す (Enter・Tab で入れる)
            QuickTags = TagSuggest.Attach(Quick, PlanTagSource.Get, true);
            Quick.KeyDown += (s, e) =>
            {
                if (e.Handled) return;
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
                quickPreview.Text = "日本語で書くと、日付・時刻・かかる時間・優先度 (重要/急ぎ)・#タグ・くり返しを読み取ります。「15:00-16:00」「@場所」「終日」「予定:」があると予定になります。Enter で追加。";
                return;
            }
            var r = ParseQuick();
            quickPreview.ForeColor = r.Title.Length > 0 ? Theme.Accent : Theme.Warning;
            quickPreview.Text = (r.Title.Length > 0 ? "追加される内容: " : (r.IsEvent ? "予定の名前も書いてください: " : "やることの名前も書いてください: ")) + r.Describe(Now);
        }

        public TaskItem QuickAdd()
        {
            if (Quick.Text.Trim().Length == 0) { Quick.Focus(); return null; }
            var r = ParseQuick();
            if (r.Title.Length == 0) { footer.StatusText = r.IsEvent ? "予定の名前を書いてください (例: 明日 15:00-16:00 歯医者)" : "やることの名前を書いてください (例: 明日10時 会議)"; return null; }
            if (r.IsEvent)
            {
                LastQuickEvent = AddEventDirect(r.ToEvent(Now, Data.Settings.EventRemind));
                Quick.Text = "";
                return null;
            }
            var t = r.ToTask(Now);
            Remember("「" + t.Title + "」の追加");
            Data.Tasks.Add(t);
            Quick.Text = "";
            Changed("「" + t.Title + "」を追加しました (" + TaskLogic.DueText(t, Now) + ")");
            Reveal(t);
            return t;
        }

        /// <summary>ひとこと入力で最後に追加した予定 (QuickAdd は予定のとき null を返す)</summary>
        public EventItem LastQuickEvent;

        void QuickDetail()
        {
            var r = ParseQuick();
            if (Quick.Text.Trim().Length > 0 && r.IsEvent)
            {
                var ev = r.ToEvent(Now, Data.Settings.EventRemind);
                if (ev.Title.Length == 0) ev.Title = Quick.Text.Trim();
                if (AddEvent(ev.StartAt, ev.EndAt, ev.AllDay, ev) != null) Quick.Text = "";
                return;
            }
            var t = Quick.Text.Trim().Length == 0 ? null : r.ToTask(Now);
            if (t != null && t.Title.Length == 0) t.Title = Quick.Text.Trim();
            int before = Data.Tasks.Count;
            AddNew(Tabs.SelectedIndex == 1 ? (DateTime?)Calendar.Day : null, false, t);
            if (Data.Tasks.Count > before) Quick.Text = "";
        }

        // ================================================================ 行の描き方 (今日・一覧で共通)

        static int TaskRowH { get { return Math.Min(255, Theme.Body.Height * 2 + Theme.S(14)); } }
        static int SubRowH { get { return Theme.Body.Height + Theme.S(12); } }
        static int HeaderRowH { get { return Theme.Small.Height + Theme.S(18); } }

        static int RowHeightOf(object o)
        {
            var row = o as ListRow;
            if (row == null) return TaskRowH;
            switch (row.Kind)
            {
                case RowKind.Sub: case RowKind.AddSub: return SubRowH;
                case RowKind.Header: return HeaderRowH;
                default: return TaskRowH;
            }
        }

        /// <summary>開く・閉じる (▸/▾) の場所</summary>
        Rectangle ArrowRect(Rectangle r)
        {
            int d = Theme.S(18);
            return new Rectangle(r.X + Theme.S(6), r.Y + (r.Height - d) / 2, d, d);
        }

        Rectangle CheckRect(Rectangle r)
        {
            int d = Theme.S(18);
            return new Rectangle(r.X + Theme.S(28), r.Y + (r.Height - d) / 2, d, d);
        }

        int GuideX(Rectangle r) { return r.X + Theme.S(28) + Theme.S(9); }

        Rectangle SubCheckRect(Rectangle r)
        {
            int d = Theme.S(14);
            return new Rectangle(r.X + Theme.S(54), r.Y + (r.Height - d) / 2, d, d);
        }

        Rectangle SubTextRect(Rectangle r)
        {
            int x = r.X + Theme.S(76);
            return new Rectangle(x, r.Y + Theme.S(2), Math.Max(Theme.S(40), r.Right - Theme.S(76) - x), r.Height - Theme.S(4));
        }

        Rectangle SubDeleteRect(Rectangle r)
        {
            int d = Theme.S(22);
            return new Rectangle(r.Right - Theme.S(12) - d, r.Y + (r.Height - d) / 2, d, d);
        }

        class ListState { public int Hover = -1; public ListRow Drag; public Point Press; public bool Dragging; public int Drop = -1; }
        readonly Dictionary<RowList, ListState> listStates = new Dictionary<RowList, ListState>();
        ListState StateOf(RowList l) { ListState s; if (!listStates.TryGetValue(l, out s)) listStates[l] = s = new ListState(); return s; }

        void PaintRow(RowList list, Graphics g, Rectangle r, object o, bool sel, bool showReason)
        {
            var row = o as ListRow;
            if (row == null) { var t0 = o as TaskItem; if (t0 != null) PaintTask(g, r, t0, sel, showReason); return; }
            int index = list.Items.IndexOf(o);
            var st = StateOf(list);
            switch (row.Kind)
            {
                case RowKind.Task: PaintTask(g, r, row.Task, sel, showReason); break;
                case RowKind.Sub: PaintSub(g, r, row, sel, st.Hover == index, st.Dragging && st.Drop == index, list, index); break;
                case RowKind.AddSub: PaintAddSub(g, r, row, sel, st.Hover == index, list, index); break;
                case RowKind.Event: PaintEvent(g, r, row.Item, sel); break;
                case RowKind.Header: PaintHeader(g, r, row); break;
            }
        }

        void PaintTask(Graphics g, Rectangle r, TaskItem t, bool sel, bool showReason)
        {
            var now = Now;
            // ▸/▾ (小タスクがあるときだけ)
            bool open = Data.IsExpanded(t);
            var ar = ArrowRect(r);
            if (t.Subtasks.Count > 0 || open)
            {
                var c = open ? Theme.Accent : Theme.SubText;
                using (var b = new SolidBrush(c))
                {
                    float ax = ar.X + ar.Width / 2f, ay = ar.Y + ar.Height / 2f, h = Theme.S(4);
                    var pts = open
                        ? new[] { new PointF(ax - h, ay - h / 2), new PointF(ax + h, ay - h / 2), new PointF(ax, ay + h * 0.8f) }
                        : new[] { new PointF(ax - h / 2, ay - h), new PointF(ax - h / 2, ay + h), new PointF(ax + h * 0.8f, ay) };
                    g.FillPolygon(b, pts);
                }
            }
            if (open)
                using (var p = new Pen(Theme.Border, Math.Max(1, Theme.S(2)))) g.DrawLine(p, GuideX(r), CheckRect(r).Bottom + Theme.S(3), GuideX(r), r.Bottom);
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
            // 2 行目: 小タスクの進み具合・優先度・時間・タグ・理由
            int cy = r.Y + Theme.S(5) + lh + lh / 2;
            int cx = x;
            int limit = r.Right - pad - dw - Theme.S(48);
            if (t.Subtasks.Count > 0)
            {
                int done = SubtaskLogic.DoneCount(t);
                bool all = done == t.Subtasks.Count;
                cx = Draw.Chip(g, "☑ " + SubtaskLogic.Progress(t), all ? Theme.Success : Theme.Accent, all ? Theme.SuccessSoft : Theme.AccentSoft, cx, cy, false) + Theme.S(4);
                int bw = Theme.S(36), bh = Theme.S(4);
                var bar = new Rectangle(cx, cy - bh / 2, bw, bh);
                using (var b = new SolidBrush(Theme.SurfaceAlt)) using (var path = Theme.RoundRect(bar, bh / 2f)) g.FillPath(b, path);
                int fw = (int)(bw * SubtaskLogic.Ratio(t));
                if (fw > 0) using (var b = new SolidBrush(all ? Theme.Success : Theme.Accent)) using (var path = Theme.RoundRect(new Rectangle(bar.X, bar.Y, fw, bh), bh / 2f)) g.FillPath(b, path);
                cx += bw + Theme.S(8);
            }
            if (!t.Done)
            {
                var pc = t.Priority == TaskItem.High ? Theme.Danger : t.Priority == TaskItem.Mid ? Theme.Accent : Theme.SubText;
                var pb = t.Priority == TaskItem.High ? Theme.DangerSoft : t.Priority == TaskItem.Mid ? Theme.AccentSoft : Theme.SurfaceAlt;
                cx = Draw.Chip(g, "優先度 " + TaskLogic.PriorityLabel(t.Priority), pc, pb, cx, cy, false) + Theme.S(4);
            }
            var bits = new List<string>();
            if (t.DurationMin > 0) bits.Add(Fmt.Minutes(t.DurationMin));
            foreach (var tg in t.Tags) bits.Add("#" + tg);
            if (showReason && !t.Done)
            {
                var sc = TaskLogic.Score(t, now);
                if (sc.Reasons.Count > 0) bits.Add(sc.Reasons[0]);
            }
            if (t.Done && t.DoneTime.HasValue) bits.Insert(0, "完了 " + Fmt.MonthDay(t.DoneTime.Value) + " " + Fmt.Time(t.DoneTime.Value));
            Draw.Text(g, string.Join("  ・  ", bits), Theme.Small, Theme.SubText, new Rectangle(cx + Theme.S(2), cy - lh / 2, Math.Max(0, limit - cx), lh));
        }

        void PaintGuide(Graphics g, Rectangle r, bool last)
        {
            int gx = GuideX(r);
            using (var p = new Pen(Theme.Border, Math.Max(1, Theme.S(2))))
            {
                g.DrawLine(p, gx, r.Y, gx, last ? r.Y + r.Height / 2 : r.Bottom);
                g.DrawLine(p, gx, r.Y + r.Height / 2, gx + Theme.S(10), r.Y + r.Height / 2);
            }
        }

        void PaintSub(Graphics g, Rectangle r, ListRow row, bool sel, bool hover, bool dropHere, RowList list, int index)
        {
            var s = row.SubTask;
            if (s == null) return;
            PaintGuide(g, r, false);
            var cr = SubCheckRect(r);
            using (var path = Theme.RoundRect(cr, Theme.S(3)))
            {
                if (s.Done) { using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path); }
                else using (var p = new Pen(Theme.BorderStrong, Math.Max(1, Theme.S(2) - 1))) g.DrawPath(p, path);
            }
            if (s.Done)
                using (var p = new Pen(Color.White, Theme.S(2)))
                    g.DrawLines(p, new[] { new Point(cr.X + cr.Width / 4, cr.Y + cr.Height / 2), new Point(cr.X + cr.Width * 2 / 5, cr.Y + cr.Height * 2 / 3), new Point(cr.Right - cr.Width / 4, cr.Y + cr.Height / 3) });
            bool editing = inlineList == list && inlineTask == row.Task && inlineSub == row.Sub && inlineOpen;
            if (!editing)
            {
                var tr = SubTextRect(r);
                if (sel || hover) tr.Width -= Theme.S(56);
                using (var f = s.Done ? new Font(Theme.Body, FontStyle.Strikeout) : null)
                    Draw.Text(g, s.Text, s.Done ? f : Theme.Body, s.Done ? Theme.SubText : Theme.Text, tr);
            }
            if ((sel || hover) && !editing)
            {
                var dr = SubDeleteRect(r);
                Draw.Text(g, "≡", Theme.Body, Theme.Disabled, new Rectangle(dr.X - Theme.S(26), dr.Y, Theme.S(22), dr.Height), TextFormatFlags.HorizontalCenter);
                Draw.Text(g, "×", Theme.Bold, hover ? Theme.Danger : Theme.SubText, dr, TextFormatFlags.HorizontalCenter);
            }
            if (dropHere)
                using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawLine(p, r.X + Theme.S(50), r.Y + 1, r.Right - Theme.S(12), r.Y + 1);
        }

        void PaintAddSub(Graphics g, Rectangle r, ListRow row, bool sel, bool hover, RowList list, int index)
        {
            PaintGuide(g, r, true);
            bool editing = inlineList == list && inlineTask == row.Task && inlineSub < 0 && inlineOpen;
            if (editing) return;
            var c = sel || hover ? Theme.Accent : Theme.SubText;
            Draw.Text(g, "＋ 小タスクを追加", sel ? Theme.Bold : Theme.Body, c, new Rectangle(r.X + Theme.S(52), r.Y, r.Width - Theme.S(64), r.Height));
            if (sel) Draw.Text(g, "Enter で入力 ・ 続けて Enter で次の小タスク", Theme.Small, Theme.SubText, new Rectangle(r.X + Theme.S(52), r.Y, r.Width - Theme.S(64), r.Height), TextFormatFlags.Right);
        }

        void PaintHeader(Graphics g, Rectangle r, ListRow row)
        {
            using (var b = new SolidBrush(Theme.Background)) g.FillRectangle(b, r);
            bool today = row.Day == Now.Date;
            Draw.Text(g, row.Text ?? "", Theme.Bold, today ? Theme.Accent : row.Day.DayOfWeek == DayOfWeek.Sunday ? Theme.Danger : row.Day.DayOfWeek == DayOfWeek.Saturday ? Theme.Blend(Theme.Accent, Color.Blue, 0.5f) : Theme.Text,
                new Rectangle(r.X + Theme.S(12), r.Y + Theme.S(4), r.Width - Theme.S(24), r.Height - Theme.S(4)));
            using (var p = new Pen(today ? Theme.Accent : Theme.Border)) g.DrawLine(p, r.X + Theme.S(10), r.Bottom - 1, r.Right - Theme.S(10), r.Bottom - 1);
        }

        void PaintEvent(Graphics g, Rectangle r, CalItem x, bool sel)
        {
            int pad = Theme.S(12), lh = Theme.Body.Height + Theme.S(2);
            var bar = new Rectangle(r.X + Theme.S(14), r.Y + Theme.S(8), Theme.S(4), r.Height - Theme.S(16));
            using (var b = new SolidBrush(x.Color)) using (var path = Theme.RoundRect(bar, Theme.S(2))) g.FillPath(b, path);
            if (x.Kind == CalKind.External)
                using (var p = new Pen(x.Color) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(p, bar.X - 1, bar.Y - 1, bar.Width + 1, bar.Height + 1);
            int tx = r.X + Theme.S(28), tw = Theme.S(96);
            string t1, t2 = null;
            if (x.AllDay) { t1 = "終日"; if (x.MultiDay) t2 = "〜" + Fmt.MonthDay(x.LastDay); }
            else if (x.MultiDay) { t1 = Fmt.Time(x.Start); t2 = "〜" + Fmt.MonthDay(x.LastDay) + " " + Fmt.Time(x.End); }
            else { t1 = Fmt.Time(x.Start); t2 = "〜" + Fmt.Time(x.End); }
            bool past = x.End <= Now;
            Draw.Text(g, t1, Theme.Bold, past ? Theme.SubText : Theme.Text, new Rectangle(tx, r.Y + Theme.S(5), tw, lh));
            if (t2 != null) Draw.Text(g, t2, Theme.Small, Theme.SubText, new Rectangle(tx, r.Y + Theme.S(5) + lh, tw, lh));
            int x0 = tx + tw + Theme.S(4);
            int right = r.Right - pad;
            if (x.Kind == CalKind.External)
            {
                string badge = (x.Badge ?? x.SourceName ?? "ほかのアプリ") + " (読むだけ)";
                int bw = Draw.Width(g, badge, Theme.Small) + Theme.S(16);
                Draw.Chip(g, badge, Theme.SubText, Theme.SurfaceAlt, right - bw, r.Y + Theme.S(5) + lh / 2, false);
                right -= bw + Theme.S(6);
            }
            Draw.Text(g, x.Title, Theme.Body, past ? Theme.SubText : Theme.Text, new Rectangle(x0, r.Y + Theme.S(5), Math.Max(0, right - x0), lh));
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(x.Location)) bits.Add("@" + x.Location);
            var e = x.Event;
            if (e != null)
            {
                if (e.Repeat != Repeat.None) bits.Add("↻ " + TaskLogic.RepeatLabel(e.Repeat));
                if (e.RemindMin >= 0) bits.Add("🔔 " + EventLogic.RemindLabel(e.RemindMin));
                foreach (var tg in e.Tags) bits.Add("#" + tg);
            }
            if (bits.Count > 0) Draw.Text(g, string.Join("  ・  ", bits), Theme.Small, Theme.SubText, new Rectangle(x0, r.Y + Theme.S(5) + lh, Math.Max(0, r.Right - pad - x0), lh));
        }

        // ================================================================ 一覧の操作 (小タスク・開く/閉じる・キー)

        /// <summary>小タスクをその場で入力・編集する欄 (一覧の子)</summary>
        bool inlineOpen;
        /// <summary>小タスクの入力欄を開いているか</summary>
        public bool InlineOpen { get { return inlineOpen; } }
        public readonly TextBox InlineEditor = new TextBox { Visible = false, BorderStyle = BorderStyle.FixedSingle };
        RowList inlineList; TaskItem inlineTask; int inlineSub = -1, inlineAt = -1;
        bool inlineBusy;

        static ListRow RowOf(RowList l) { return l.SelectedItem as ListRow; }

        void WireTaskList(RowList list, bool showReason)
        {
            list.RowHeight = RowHeightOf;
            list.PlainRow = o => { var r = o as ListRow; return r != null && r.Kind == RowKind.Header; };
            list.PaintRow = (g, r, o, sel) => PaintRow(list, g, r, o, sel, showReason);
            var st = StateOf(list);
            list.DoubleClick += (s, e) =>
            {
                var p = list.PointToClient(Cursor.Position);
                int i = list.IndexFromPoint(p);
                if (i < 0 || i >= list.Items.Count) return;
                var row = list.Items[i] as ListRow;
                if (row == null) return;
                var rr = list.GetItemRectangle(i);
                switch (row.Kind)
                {
                    case RowKind.Task: if (!CheckRect(rr).Contains(p) && !ArrowRect(rr).Contains(p)) Edit(row.Task); break;
                    case RowKind.Sub: if (!SubCheckRect(rr).Contains(p) && !SubDeleteRect(rr).Contains(p)) BeginEditSub(list, row.Task, row.Sub); break;
                    case RowKind.Event: OpenItem(row.Item); break;
                }
            };
            int lastSel = -1;
            list.SelectedIndexChanged += (s, e) =>
            {
                // 見出しの行は選ばない (動いた向きの次の行へ)
                var row = RowOf(list);
                if (row != null && row.Kind == RowKind.Header)
                {
                    int dir = list.SelectedIndex < lastSel ? -1 : 1;
                    int j = NextSelectable(list, list.SelectedIndex, dir);
                    if (j < 0) j = NextSelectable(list, list.SelectedIndex, -dir);
                    if (j >= 0) { list.SelectedIndex = j; return; }
                }
                lastSel = list.SelectedIndex;
                list.Invalidate();
            };
            list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int i = list.IndexFromPoint(e.Location);
                if (i < 0 || i >= list.Items.Count) return;
                var row = list.Items[i] as ListRow;
                if (row == null) return;
                var rr = list.GetItemRectangle(i);
                if (!rr.Contains(e.Location)) return;
                var big = new Func<Rectangle, Rectangle>(x => { x.Inflate(Theme.S(5), Theme.S(5)); return x; });
                switch (row.Kind)
                {
                    case RowKind.Task:
                        if ((row.Task.Subtasks.Count > 0 || Data.IsExpanded(row.Task)) && big(ArrowRect(rr)).Contains(e.Location)) ToggleExpand(row.Task);
                        else if (big(CheckRect(rr)).Contains(e.Location)) Toggle(row.Task);
                        break;
                    case RowKind.Sub:
                        if (big(SubCheckRect(rr)).Contains(e.Location)) ToggleSub(row.Task, row.Sub);
                        else if (SubDeleteRect(rr).Contains(e.Location)) DeleteSub(row.Task, row.Sub);
                        else { st.Drag = row; st.Press = e.Location; st.Dragging = false; st.Drop = -1; }
                        break;
                    case RowKind.AddSub:
                        BeginAddSub(list, row.Task);
                        break;
                }
            };
            list.MouseMove += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                if (i >= list.Items.Count || (i >= 0 && !list.GetItemRectangle(i).Contains(e.Location))) i = -1;
                if (st.Hover != i) { st.Hover = i; list.Invalidate(); }
                var row = i >= 0 ? list.Items[i] as ListRow : null;
                if (st.Drag != null && e.Button == MouseButtons.Left)
                {
                    if (!st.Dragging && Math.Abs(e.Y - st.Press.Y) > Theme.S(5)) st.Dragging = true;
                    if (st.Dragging)
                    {
                        int drop = -1;
                        if (row != null && row.Task == st.Drag.Task && (row.Kind == RowKind.Sub || row.Kind == RowKind.AddSub)) drop = i;
                        if (drop != st.Drop) { st.Drop = drop; list.Invalidate(); }
                        list.Cursor = drop >= 0 ? Cursors.SizeNS : Cursors.No;
                        return;
                    }
                }
                Cursor c = Cursors.Default;
                if (row != null)
                {
                    var rr = list.GetItemRectangle(i);
                    if (row.Kind == RowKind.Task && (CheckRect(rr).Contains(e.Location) || ((row.Task.Subtasks.Count > 0 || Data.IsExpanded(row.Task)) && ArrowRect(rr).Contains(e.Location)))) c = Cursors.Hand;
                    if (row.Kind == RowKind.Sub && (SubCheckRect(rr).Contains(e.Location) || SubDeleteRect(rr).Contains(e.Location))) c = Cursors.Hand;
                    if (row.Kind == RowKind.AddSub) c = Cursors.IBeam;
                }
                list.Cursor = c;
            };
            list.MouseUp += (s, e) =>
            {
                if (st.Dragging && st.Drag != null && st.Drop >= 0)
                {
                    var target = list.Items[st.Drop] as ListRow;
                    int to = target.Kind == RowKind.AddSub ? target.Task.Subtasks.Count - 1 : target.Sub;
                    if (to > st.Drag.Sub && target.Kind == RowKind.Sub) to--;
                    MoveSub(st.Drag.Task, st.Drag.Sub, Math.Max(0, to));
                }
                st.Drag = null; st.Dragging = false; st.Drop = -1;
                list.Cursor = Cursors.Default;
                list.Invalidate();
            };
            list.MouseLeave += (s, e) => { if (st.Hover != -1) { st.Hover = -1; list.Invalidate(); } };
            list.Resize += (s, e) => PlaceInline();
            list.MouseWheel += (s, e) => { if (inlineOpen && inlineList == list) CommitInline(false); };
        }

        static int NextSelectable(RowList list, int from, int dir)
        {
            for (int j = from + dir; j >= 0 && j < list.Items.Count; j += dir)
            {
                var r = list.Items[j] as ListRow;
                if (r == null || r.Selectable) return j;
            }
            return -1;
        }

        bool SelectKey(RowList list, string key)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                var r = list.Items[i] as ListRow;
                if (r != null && r.Key == key) { list.SelectedIndex = i; return true; }
            }
            return false;
        }

        /// <summary>一覧でのキー操作 (テストからも呼ぶ)。扱ったら true</summary>
        public bool HandleListKey(RowList list, Keys k)
        {
            var row = RowOf(list);
            switch (k)
            {
                case Keys.Control | Keys.Shift | Keys.Right: ExpandAll(); return true;
                case Keys.Control | Keys.Shift | Keys.Left: CollapseAll(); return true;
                case Keys.Up: case Keys.Down:
                {
                    int j = NextSelectable(list, list.SelectedIndex, k == Keys.Up ? -1 : 1);
                    if (j >= 0) list.SelectedIndex = j;
                    return true;
                }
            }
            if (row == null) return false;
            switch (k)
            {
                case Keys.Enter:
                    if (row.Kind == RowKind.Task) Edit(row.Task);
                    else if (row.Kind == RowKind.Sub) BeginEditSub(list, row.Task, row.Sub);
                    else if (row.Kind == RowKind.AddSub) BeginAddSub(list, row.Task);
                    else if (row.Kind == RowKind.Event) OpenItem(row.Item);
                    return true;
                case Keys.F2:
                    if (row.Kind == RowKind.Sub) BeginEditSub(list, row.Task, row.Sub);
                    else if (row.Kind == RowKind.Task) Edit(row.Task);
                    else if (row.Kind == RowKind.Event) OpenItem(row.Item);
                    return true;
                case Keys.Space:
                    if (row.Kind == RowKind.Task) Toggle(row.Task);
                    else if (row.Kind == RowKind.Sub) ToggleSub(row.Task, row.Sub);
                    else if (row.Kind == RowKind.AddSub) BeginAddSub(list, row.Task);
                    return true;
                case Keys.Delete:
                    if (row.Kind == RowKind.Task) Delete(row.Task);
                    else if (row.Kind == RowKind.Sub) DeleteSub(row.Task, row.Sub);
                    else if (row.Kind == RowKind.Event) DeleteItem(row.Item);
                    return true;
                case Keys.Tab:
                    if (row.Kind == RowKind.Task) { BeginAddSub(list, row.Task); return true; }
                    if (row.Kind == RowKind.Sub) { BeginAddSub(list, row.Task, row.Sub + 1); return true; }
                    if (row.Kind == RowKind.AddSub) { BeginAddSub(list, row.Task); return true; }
                    return false;
                case Keys.Right:
                    if (row.Kind == RowKind.Task)
                    {
                        if (!Data.IsExpanded(row.Task)) ToggleExpand(row.Task);
                        else SelectKey(list, row.Task.Subtasks.Count > 0 ? "s:" + row.Task.Id + ":0" : "a:" + row.Task.Id);
                    }
                    return true;
                case Keys.Left:
                    if (row.Kind == RowKind.Sub || row.Kind == RowKind.AddSub) SelectKey(list, "t:" + row.Task.Id);
                    else if (row.Kind == RowKind.Task && Data.IsExpanded(row.Task)) ToggleExpand(row.Task);
                    return true;
                case Keys.Alt | Keys.Up:
                case Keys.Alt | Keys.Down:
                    if (row.Kind == RowKind.Sub)
                    {
                        int to = row.Sub + (k == (Keys.Alt | Keys.Up) ? -1 : 1);
                        if (to >= 0 && to < row.Task.Subtasks.Count) { MoveSub(row.Task, row.Sub, to); SelectKey(list, "s:" + row.Task.Id + ":" + to); }
                        return true;
                    }
                    return false;
            }
            return false;
        }

        void RefreshLists() { RefreshToday(); RefreshList(); }

        public void ToggleExpand(TaskItem t)
        {
            if (t == null) return;
            CommitInline(false);
            bool open = !Data.IsExpanded(t);
            Data.SetExpanded(t, open);
            Save(); RefreshLists();
            foreach (var l in new[] { TodayList, List }) if (l.Focused || l == ActiveList()) SelectKey(l, "t:" + t.Id);
            footer.StatusText = open ? "「" + t.Title + "」の小タスクを開きました (Tab で追加 ・ ← で閉じる)" : "「" + t.Title + "」を閉じました";
        }

        RowList ActiveList() { return Tabs.SelectedIndex == 0 ? TodayList : Tabs.SelectedIndex == 2 ? List : null; }

        public int ExpandAll()
        {
            CommitInline(false);
            int n = SubtaskLogic.ExpandAll(Data, Data.Tasks);
            Save(); RefreshLists();
            footer.StatusText = n > 0 ? "小タスクのあるタスク " + n + " 件を開きました" : "開くものはありません";
            return n;
        }

        public int CollapseAll()
        {
            CommitInline(false);
            int n = SubtaskLogic.CollapseAll(Data);
            Save(); RefreshLists();
            footer.StatusText = n > 0 ? "すべて閉じました" : "閉じるものはありません";
            return n;
        }

        public int AddSubtask(TaskItem t, string text, int at = -1)
        {
            if (t == null || string.IsNullOrWhiteSpace(text)) return 0;
            Remember("「" + t.Title + "」の小タスクの追加");
            int n = SubtaskLogic.AddLines(t, text, at);
            if (!Data.IsExpanded(t)) Data.SetExpanded(t, true);
            Changed(n > 1 ? "小タスクを " + n + " 件追加しました" : "小タスク「" + text.Trim() + "」を追加しました (" + SubtaskLogic.Progress(t) + ")");
            return n;
        }

        public bool ToggleSub(TaskItem t, int i)
        {
            if (t == null || i < 0 || i >= t.Subtasks.Count) return false;
            var s = t.Subtasks[i];
            Remember("小タスク「" + s.Text + "」の" + (s.Done ? "完了の取り消し" : "完了"));
            SubtaskLogic.Toggle(t, i);
            bool allDone = t.Subtasks.All(x => x.Done);
            Changed("小タスク「" + s.Text + "」を" + (s.Done ? "完了しました" : "未完了に戻しました") + " (" + SubtaskLogic.Progress(t) + ")" + (allDone && !t.Done ? "。すべて終わりました — タスクも完了にするなら Space" : ""));
            KeepSelection("s:" + t.Id + ":" + i);
            return true;
        }

        public bool RenameSub(TaskItem t, int i, string text)
        {
            if (t == null || i < 0 || i >= t.Subtasks.Count || string.IsNullOrWhiteSpace(text) || t.Subtasks[i].Text == text.Trim()) return false;
            Remember("小タスクの名前の変更");
            SubtaskLogic.Rename(t, i, text);
            Changed("小タスクを「" + text.Trim() + "」にしました");
            return true;
        }

        public bool DeleteSub(TaskItem t, int i)
        {
            if (t == null || i < 0 || i >= t.Subtasks.Count) return false;
            CommitInline(false);
            var s = t.Subtasks[i];
            Remember("小タスク「" + s.Text + "」の削除");
            SubtaskLogic.Delete(t, i);
            Changed("小タスク「" + s.Text + "」を削除しました");
            KeepSelection(t.Subtasks.Count == 0 ? "a:" + t.Id : "s:" + t.Id + ":" + Math.Min(i, t.Subtasks.Count - 1));
            return true;
        }

        public bool MoveSub(TaskItem t, int from, int to)
        {
            if (t == null || from == to || from < 0 || from >= t.Subtasks.Count || to < 0 || to >= t.Subtasks.Count) return false;
            Remember("小タスクの並べ替え");
            SubtaskLogic.MoveTo(t, from, to);
            Changed("小タスク「" + t.Subtasks[to].Text + "」を " + (to + 1) + " 番目にしました");
            KeepSelection("s:" + t.Id + ":" + to);
            return true;
        }

        void KeepSelection(string key)
        {
            var l = ActiveList();
            if (l != null) SelectKey(l, key);
        }

        /// <summary>小タスクの追加欄を開く (at: 入れる位置。-1 で最後)</summary>
        public void BeginAddSub(RowList list, TaskItem t, int at = -1)
        {
            if (t == null) return;
            CommitInline(false);
            if (!Data.IsExpanded(t)) { Data.SetExpanded(t, true); Save(); RefreshLists(); }
            SelectKey(list, "a:" + t.Id);
            inlineList = list; inlineTask = t; inlineSub = -1; inlineAt = at;
            ShowInline("");
        }

        public void BeginEditSub(RowList list, TaskItem t, int i)
        {
            if (t == null || i < 0 || i >= t.Subtasks.Count) return;
            CommitInline(false);
            if (!Data.IsExpanded(t)) { Data.SetExpanded(t, true); Save(); RefreshLists(); }
            SelectKey(list, "s:" + t.Id + ":" + i);
            inlineList = list; inlineTask = t; inlineSub = i; inlineAt = -1;
            ShowInline(t.Subtasks[i].Text);
        }

        void ShowInline(string text)
        {
            if (InlineEditor.Parent != inlineList) { if (InlineEditor.Parent != null) InlineEditor.Parent.Controls.Remove(InlineEditor); inlineList.Controls.Add(InlineEditor); }
            InlineEditor.Font = Theme.Body;
            Ui.SetCue(InlineEditor, inlineSub < 0 ? "小タスクを入力して Enter (続けて入力できます ・ Esc でやめる)" : "Enter で決定 ・ Esc でやめる");
            InlineEditor.Text = text;
            InlineEditor.Visible = true; inlineOpen = true;
            PlaceInline();
            InlineEditor.BringToFront();
            InlineEditor.Focus();
            InlineEditor.SelectAll();
            inlineList.Invalidate();
        }

        void PlaceInline()
        {
            if (!inlineOpen || inlineList == null) return;
            string key = inlineSub < 0 ? "a:" + inlineTask.Id : "s:" + inlineTask.Id + ":" + inlineSub;
            int idx = -1;
            for (int i = 0; i < inlineList.Items.Count; i++) { var r = inlineList.Items[i] as ListRow; if (r != null && r.Key == key) { idx = i; break; } }
            if (idx < 0) { CancelInline(); return; }
            var rr = inlineList.GetItemRectangle(idx);
            var tr = SubTextRect(rr);
            if (inlineSub < 0) tr.X = rr.X + Theme.S(52);
            int h = InlineEditor.PreferredHeight;
            InlineEditor.SetBounds(tr.X, rr.Y + (rr.Height - h) / 2, Math.Max(Theme.S(120), rr.Right - Theme.S(16) - tr.X), h);
        }

        /// <summary>入力欄の内容で決める。keepAdding = 追加のとき、続けて次の小タスクを入力する</summary>
        public void CommitInline(bool keepAdding)
        {
            if (inlineBusy || !inlineOpen || inlineTask == null) return;
            inlineBusy = true;
            try
            {
                var list = inlineList; var t = inlineTask; int sub = inlineSub, at = inlineAt;
                var text = InlineEditor.Text.Trim();
                HideInline();
                if (!Data.Tasks.Contains(t)) return;
                if (sub >= 0)
                {
                    if (text.Length > 0) RenameSub(t, sub, text);
                    SelectKey(list, "s:" + t.Id + ":" + sub);
                    list.Focus();
                    return;
                }
                if (text.Length == 0) { SelectKey(list, "a:" + t.Id); list.Focus(); return; }
                int n = AddSubtask(t, text, at);
                inlineBusy = false;
                if (keepAdding) BeginAddSub(list, t, at < 0 ? -1 : at + n);
                else { SelectKey(list, "a:" + t.Id); list.Focus(); }
            }
            finally { inlineBusy = false; }
        }

        public void CancelInline()
        {
            if (!inlineOpen) return;
            var list = inlineList; var t = inlineTask; int sub = inlineSub;
            HideInline();
            if (list != null && t != null) { SelectKey(list, sub >= 0 ? "s:" + t.Id + ":" + sub : "a:" + t.Id); list.Focus(); }
        }

        void HideInline()
        {
            InlineEditor.Visible = false; inlineOpen = false;
            var l = inlineList;
            inlineList = null; inlineTask = null; inlineSub = -1; inlineAt = -1;
            if (l != null) l.Invalidate();
        }

        Control[] listTaskButtons = new Control[0], listEventButtons = new Control[0], listExpandButtons = new Control[0];
        /// <summary>一覧の下のボタンのうち、いま見えているものの文字 (テスト用)</summary>
        public List<string> ListButtonLabels()
        {
            var all = listTaskButtons.Concat(listEventButtons).Where(c => c.Parent != null).Select(c => c.Parent).FirstOrDefault();
            if (all == null) return new List<string>();
            return all.Controls.Cast<Control>().Concat(listExpandButtons).Where(c => !hiddenButtons.Contains(c)).Select(c => c.Text).ToList();
        }
        readonly HashSet<Control> hiddenButtons = new HashSet<Control>();
        void ShowButton(Control c, bool on) { c.Visible = on; if (on) hiddenButtons.Remove(c); else hiddenButtons.Add(c); }

        FlowLayoutPanel TaskButtons(RowList list, bool withPostpone)
        {
            var bDone = new FlatButton("完了 / 戻す", ButtonKind.Secondary).FitWidth(88);
            var bSub = new FlatButton("＋小タスク", ButtonKind.Secondary).FitWidth(88);
            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(72);
            var bLater = new FlatButton("明日に回す", ButtonKind.Secondary).FitWidth(88);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
            bDone.Click += (s, e) => { var r = RowOf(list); if (r == null) return; if (r.Kind == RowKind.Sub) ToggleSub(r.Task, r.Sub); else if (r.Task != null) Toggle(r.Task); };
            bSub.Click += (s, e) => { var r = RowOf(list); if (r != null && r.Task != null) BeginAddSub(list, r.Task, r.Kind == RowKind.Sub ? r.Sub + 1 : -1); else footer.StatusText = "小タスクを足すタスクを選んでください"; };
            bEdit.Click += (s, e) => { var r = RowOf(list); if (r == null) return; if (r.Kind == RowKind.Sub) BeginEditSub(list, r.Task, r.Sub); else if (r.Kind == RowKind.Event) OpenItem(r.Item); else if (r.Task != null) Edit(r.Task); };
            bLater.Click += (s, e) => { var r = RowOf(list); if (r != null && r.Task != null) Postpone(r.Task); };
            bDel.Click += (s, e) => { var r = RowOf(list); if (r == null) return; if (r.Kind == RowKind.Sub) DeleteSub(r.Task, r.Sub); else if (r.Kind == RowKind.Event) DeleteItem(r.Item); else if (r.Task != null) Delete(r.Task); };
            tips.SetToolTip(bDone, "完了にする・未完了に戻す (Space / 左の丸をクリック)");
            tips.SetToolTip(bSub, "選んでいるタスクに小タスクを足す (Tab)");
            tips.SetToolTip(bEdit, "編集 (Enter / ダブルクリック。小タスクは F2)");
            tips.SetToolTip(bLater, "期限を明日にする (時刻はそのまま)");
            tips.SetToolTip(bDel, "削除 (Delete)");
            FlowLayoutPanel row;
            if (withPostpone)
            {
                // 一覧: 「予定」のときはタスク用のボタン (完了・小タスク・明日に回す) を隠し、「＋ 予定を追加」を出す
                var bAddEv = new FlatButton("＋ 予定を追加", ButtonKind.Secondary).FitWidth(104);
                bAddEv.Click += (s, e) => AddEventAt(Now.Date, true);
                tips.SetToolTip(bAddEv, "予定を追加 (Ctrl+E)");
                row = Lay.Flow(bAddEv, bDone, bSub, bEdit, bLater, bDel);
                listTaskButtons = new Control[] { bDone, bSub, bLater };
                listEventButtons = new Control[] { bAddEv };
            }
            else row = Lay.Flow(bDone, bSub, bEdit, bDel);
            row.Dock = DockStyle.Bottom;
            return row;
        }

        // ================================================================ 今日

        void BuildToday(Panel v)
        {
            todayEmpty.Dock = DockStyle.Top;
            todayEmpty.Padding = Theme.P(20, 16, 20, 16);
            var t1 = new WrapLabel { Text = "まだタスクも予定もありません", Font = Theme.Title, ForeColor = Theme.Text };
            var t2 = new WrapLabel { Text = "上の入力欄に「明日15時 歯医者」「金曜までにレポート 重要」のように書いて Enter を押すとタスクに、「明日 15:00-16:00 歯医者 @駅前」のように終わりの時刻か @場所 を書くと予定になります。\n今日やることは、期限の近さ・優先度・かかる時間から「おすすめ順」に並びます。どんな画面になるか見たいときは、見本データを入れて試せます (あとで削除できます)。", Font = Theme.Body, ForeColor = Theme.SubText };
            var add = new FlatButton("＋ タスクを追加", ButtonKind.Primary).FitWidth(120);
            var addEv = new FlatButton("＋ 予定を追加", ButtonKind.Secondary).FitWidth(120);
            var sample = new FlatButton("見本データを入れる", ButtonKind.Secondary).FitWidth(120);
            add.Click += (s, e) => AddNew(null, false);
            addEv.Click += (s, e) => AddEventAt(Now.Date, true);
            sample.Click += (s, e) => InsertSamples();
            Lay.Stack(todayEmpty, t1, Ui.Spacer(6), t2, Ui.Spacer(12), Lay.Flow(add, addEv, sample));
            todayEmpty.Resize += (s, e) => FitHeight(todayEmpty);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var left = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10), Margin = new Padding(0, 0, Theme.S(8), 0) };
            todayCalTitle.Font = Theme.Header; todayCalTitle.ForeColor = Theme.Text; todayCalTitle.Dock = DockStyle.Top; todayCalTitle.Height = Theme.Header.Height + Theme.S(8);
            todayCalTitle.TextAlign = ContentAlignment.MiddleLeft;
            var calHint = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.Small.Height + Theme.S(8), Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false, TextAlign = ContentAlignment.BottomLeft,
                Text = "空いた時間をドラッグかダブルクリックで予定を追加 (Ctrl+E)" };
            TodayCal.Mode = TealSuite.Tasks.CalMode.Day;
            TodayCal.Fixed = true;
            WireCalendar(TodayCal, () => EventLogic.ShowAll);
            left.Controls.Add(TodayCal);
            left.Controls.Add(Ui.Spacer(4));
            left.Controls.Add(todayCalTitle);
            left.Controls.Add(calHint);

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10), Margin = Padding.Empty };
            todaySummary.Dock = DockStyle.Top; todaySummary.Font = Theme.Body; todaySummary.Padding = Theme.P(4, 2, 4, 6);
            TodayList.EmptyText = "今日が期限のタスクはありません。\n上の入力欄から追加するか、「カレンダー」「一覧」で先の予定を見られます。";
            WireTaskList(TodayList, true);
            card.Controls.Add(TodayList);
            card.Controls.Add(Ui.Spacer(6));
            card.Controls.Add(todaySummary);
            card.Controls.Add(TaskButtons(TodayList, true));

            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(card, 1, 0);
            v.Controls.Add(grid);
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
            bool empty = Data.Tasks.Count == 0 && Data.Events.Count == 0;
            todayEmpty.Visible = empty;
            if (empty) FitHeight(todayEmpty);
            var open = TaskLogic.TodayList(Data.Tasks, now);
            var done = TaskLogic.DoneToday(Data.Tasks, now);
            int over = open.Count(t => TaskLogic.IsOverdue(t, now));
            int minutes = open.Sum(t => t.DurationMin);
            var evs = CalItems(now.Date, now.Date.AddDays(1), EventLogic.ShowEvents);
            var next = evs.FirstOrDefault(x => !x.AllDay && x.End > now);
            var parts = new List<string>();
            parts.Add(Fmt.Date(now));
            parts.Add("予定 " + evs.Count + " 件" + (next != null ? " (次: " + Fmt.Time(next.Start) + " " + next.Title + ")" : ""));
            parts.Add("タスク残り " + open.Count + " 件" + (over > 0 ? " (うち期限切れ " + over + " 件)" : ""));
            if (minutes > 0) parts.Add("かかる時間の合計 " + Fmt.Minutes(minutes));
            parts.Add("今日終わらせた " + done.Count + " 件");
            todaySummary.ForeColor = over > 0 ? Theme.Danger : Theme.Text;
            todaySummary.Text = string.Join("  ・  ", parts) + "\n上から順にやるのがおすすめです。▸ で小タスクを開き、Tab で足せます";
            todayCalTitle.Text = "今日の予定  " + Fmt.MonthDay(now);
            var items = new List<TaskItem>();
            items.AddRange(open);
            items.AddRange(done);
            var key = RowOf(TodayList) != null ? RowOf(TodayList).Key : null;
            TodayList.SetItems(PlanRows.Tasks(items, Data.Expanded), o => key != null && ((ListRow)o).Key == key);
            TodayCal.Day = now.Date;
            PlaceInline();
        }

        // ================================================================ カレンダー

        /// <summary>ほかのアプリの読むだけの項目 (1.10.0: Teal Wallet の支払い日 = WalletPlanSource)。ここに足すだけでカレンダー・予定表に出る</summary>
        public readonly List<IPlanSource> ExternalSources = new List<IPlanSource>();

        public List<CalItem> CalItems(DateTime from, DateTime to, int show)
        {
            return EventLogic.Collect(Data, ExternalSources, from, to, show);
        }

        void WireCalendar(CalendarView cv, Func<int> show)
        {
            cv.Items = (a, b) => CalItems(a, b, show());
            cv.Clock = () => Now;
            cv.AddRequested += (day, withTime) => AddNew(day, withTime);
            cv.EventRequested += (a, b, allDay) => AddEvent(a, b, allDay);
            cv.AddMenuRequested += (day, p) => ShowAddMenu(cv, day, p);
            cv.ItemOpened += x => OpenItem(x);
            cv.ItemToggled += x => { if (x.Task != null) Toggle(x.Task); };
            cv.ItemDeleteRequested += x => DeleteItem(x);
            cv.ItemMoved += (x, when, withTime) => MoveItem(x, when, withTime);
            cv.ItemResized += (x, end) => ResizeItem(x, end);
        }

        void ShowAddMenu(CalendarView cv, DateTime day, Point p)
        {
            var m = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            bool withTime = day.TimeOfDay != TimeSpan.Zero;
            m.Items.Add(new ToolStripMenuItem("予定を追加" + (withTime ? " (" + Fmt.Time(day) + "〜)" : "") + "  Ctrl+E", null, (s, e) => AddEventAt(day, !withTime)));
            m.Items.Add(new ToolStripMenuItem("タスクを追加  Ctrl+N", null, (s, e) => AddNew(day, withTime)));
            m.Show(cv, p);
        }

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
            CalShow.Width = Theme.S(120);
            CalShow.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                Data.Settings.CalShow = Math.Max(0, CalShow.SelectedIndex);
                Save(); Calendar.Reload();
            };
            tips.SetToolTip(CalShow, "カレンダーに出すもの");
            var nav = Lay.Flow(prev, today, next, periodLabel);
            var bar = new Panel { Dock = DockStyle.Top, Height = Math.Max(nav.PreferredSize.Height, CalMode.Height) + Theme.S(4) };
            nav.Dock = DockStyle.Left; nav.AutoSize = true;
            CalMode.Dock = DockStyle.Right;
            var showBox = new Panel { Dock = DockStyle.Right, Width = CalShow.Width + Theme.S(10), Padding = Theme.P(0, 2, 10, 0) };
            CalShow.Dock = DockStyle.Top;
            showBox.Controls.Add(CalShow);
            bar.Controls.Add(nav); bar.Controls.Add(showBox); bar.Controls.Add(CalMode);
            var hint = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.Small.Height + Theme.S(8), Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false, TextAlign = ContentAlignment.BottomLeft,
                Text = "空いた所をドラッグかダブルクリックで予定を追加 ・ 「＋」でタスクか予定 ・ ドラッグで移動、下の端で長さを変える (15 分きざみ) ・ PageUp/PageDown 前後 ・ Space 完了" };

            WireCalendar(Calendar, () => Data.Settings.CalShow);
            Calendar.ViewChanged += (s, e) => { UpdatePeriod(); UpdatePreview(); };

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
            Ui.SetCue(Search, "タイトル・タグ・メモ・場所・小タスクで検索…  (Ctrl+F)");
            Search.TextChanged += (s, e) => RefreshList();
            Search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Search.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { List.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            filter.Width = Theme.S(110); tagFilter.Width = Theme.S(130); sort.Width = Theme.S(140); AgendaRange.Width = Theme.S(140);
            tagFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            filter.SelectedIndexChanged += (s, e) => RefreshList();
            tagFilter.SelectedIndexChanged += (s, e) => RefreshList();
            sort.SelectedIndexChanged += (s, e) => RefreshList();
            AgendaRange.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                Data.Settings.AgendaRange = Math.Max(0, AgendaRange.SelectedIndex);
                Save(); RefreshList();
            };
            tips.SetToolTip(filter, "表示するもの"); tips.SetToolTip(tagFilter, "タグで絞り込む"); tips.SetToolTip(sort, "並べ方"); tips.SetToolTip(AgendaRange, "予定表に出す期間");
            var top = Lay.Row(new InputFrame(Search), sort, AgendaRange, tagFilter, filter);
            top.Dock = DockStyle.Top;

            ListKind.Width = Theme.S(220); ListKind.BackColor = Theme.Surface;
            ListKind.SelectedChanged += (s, e) =>
            {
                if (loading) return;
                Data.Settings.ListKind = ListKind.SelectedIndex;
                Save(); RefreshList(); List.Focus();
            };
            var bExpand = new FlatButton("すべて開く", ButtonKind.Ghost).FitWidth(80);
            var bCollapse = new FlatButton("すべて閉じる", ButtonKind.Ghost).FitWidth(80);
            bExpand.Click += (s, e) => { ExpandAll(); List.Focus(); };
            bCollapse.Click += (s, e) => { CollapseAll(); List.Focus(); };
            tips.SetToolTip(bExpand, "小タスクのあるタスクをすべて開く (Ctrl+Shift+→)");
            tips.SetToolTip(bCollapse, "すべて閉じる (Ctrl+Shift+←)");
            tips.SetToolTip(ListKind, "タスク / 予定 (日ごとの予定表) / すべて");
            var kindBar = new Panel { Dock = DockStyle.Top, Height = ListKind.Height + Theme.S(2) };
            ListKind.Dock = DockStyle.Left;
            listExpandButtons = new Control[] { bExpand, bCollapse };
            var right = Lay.Flow(bExpand, bCollapse);
            right.Dock = DockStyle.Right; right.AutoSize = true;
            kindBar.Controls.Add(ListKind); kindBar.Controls.Add(right);

            List.EmptyText = "該当するものはありません";
            WireTaskList(List, false);

            card.Controls.Add(List);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(top);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(kindBar);
            card.Controls.Add(TaskButtons(List, true));
            v.Controls.Add(card);
        }

        void RefreshTags()
        {
            var tags = TaskLogic.AllTags(Data.Tasks).Union(Data.Events.SelectMany(e => e.Tags ?? new List<string>())).Distinct().ToList();
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

        /// <summary>予定表の期間 [from, to)</summary>
        public void AgendaSpan(out DateTime from, out DateTime to)
        {
            var today = Now.Date;
            switch (Data.Settings.AgendaRange)
            {
                case 1: from = today; to = today.AddDays(90); break;
                case 2: from = TaskLogic.WeekStart(today); to = from.AddDays(7); break;
                case 3: from = new DateTime(today.Year, today.Month, 1); to = from.AddMonths(1); break;
                case 4: from = today.AddDays(-30); to = today.AddDays(1); break;
                default: from = today; to = today.AddDays(30); break;
            }
        }

        public void RefreshList()
        {
            var now = Now;
            var today = now.Date;
            var week = TaskLogic.WeekStart(today);
            int kind = Data.Settings.ListKind;
            filter.Visible = sort.Visible = kind == 0;
            AgendaRange.Visible = kind != 0;
            foreach (var c in listTaskButtons) ShowButton(c, kind != 1);
            foreach (var c in listExpandButtons) ShowButton(c, kind != 1);
            foreach (var c in listEventButtons) ShowButton(c, kind != 0);
            string tag = tagFilter.SelectedIndex > 0 ? ((string)tagFilter.SelectedItem).Substring(1) : null;
            var words = Search.Text.Trim().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.TrimStart('#', '＃')).Where(w => w.Length > 0).ToList();
            Func<TaskItem, bool> taskOk = t => (tag == null || t.Tags.Contains(tag)) && words.All(w => Has(t.Title, w) || Has(t.Memo, w) || t.Tags.Any(x => Has(x, w)) || t.Subtasks.Any(x => Has(x.Text, w)));
            Func<CalItem, bool> itemOk = x =>
            {
                if (x.Task != null) return taskOk(x.Task);
                var e = x.Event;
                var tags = e != null ? e.Tags ?? new List<string>() : new List<string>();
                if (tag != null && !tags.Contains(tag)) return false;
                return words.All(w => Has(x.Title, w) || Has(x.Location, w) || (e != null && Has(e.Memo, w)) || tags.Any(t => Has(t, w)) || Has(x.SourceName, w));
            };
            var key = RowOf(List) != null ? RowOf(List).Key : null;
            List<ListRow> rows;
            if (kind == 0)
            {
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
                q = q.Where(taskOk);
                switch (sort.SelectedIndex)
                {
                    case 1: q = q.OrderBy(t => t.Done).ThenByDescending(t => TaskLogic.Score(t, now).Score).ThenBy(t => t.Due ?? "9999"); break;
                    case 2: q = q.OrderBy(t => t.Done).ThenByDescending(t => t.Priority).ThenBy(t => t.Due ?? "9999"); break;
                    case 3: q = q.OrderByDescending(t => t.Created ?? ""); break;
                    default: q = q.OrderBy(t => t.Done).ThenBy(t => t.HasDue ? 0 : 1).ThenBy(t => t.DueAt ?? DateTime.MaxValue).ThenByDescending(t => t.Priority); break;
                }
                if (filter.SelectedIndex == 5) q = q.OrderByDescending(t => t.DoneAt ?? "");
                rows = PlanRows.Tasks(q.ToList(), Data.Expanded);
                List.EmptyText = "該当するタスクはありません";
            }
            else
            {
                DateTime from, to;
                AgendaSpan(out from, out to);
                var items = CalItems(from, to, kind == 1 ? EventLogic.ShowEvents : EventLogic.ShowAll).Where(itemOk).ToList();
                var undated = kind == 2 ? Data.Tasks.Where(t => !t.HasDue && !t.Done).Where(taskOk).OrderByDescending(t => t.Priority).ToList() : null;
                rows = PlanRows.Agenda(items, from, to, today, Data.Expanded, undated);
                List.EmptyText = kind == 1 ? "この期間の予定はありません。\n上の入力欄に「明日 15:00-16:00 歯医者 @駅前」のように書くか、カレンダーの空いた所をダブルクリックして追加できます。" : "この期間の予定・タスクはありません";
            }
            List.SetItems(rows, o => key != null && ((ListRow)o).Key == key);
            var cur = RowOf(List);
            if (cur != null && cur.Kind == RowKind.Header) { int j = NextSelectable(List, List.SelectedIndex, 1); if (j >= 0) List.SelectedIndex = j; }
            PlaceInline();
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
