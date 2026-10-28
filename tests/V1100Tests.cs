// 1.10.0 のテスト: Teal Plan (小タスクを一覧で開く・予定)、Teal Tasks → Teal Plan の名前変更 (引き継ぎ)
extern alias AM;
extern alias TASKS;
extern alias UP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using H = AM::TealSuite.Hub;
using T = TASKS::TealSuite.Tasks;
using U = UP::TealSuite.Updater;
using TC = TASKS::TealSuite.Common;
using HC = AM::TealSuite.Common;

public static class V1100Tests
{
    static readonly DateTime Now = new DateTime(2026, 10, 28, 9, 0, 0); // 水曜日

    class FakeSource : T.IPlanSource
    {
        public string Name { get { return "Teal Wallet"; } }
        public bool Throw;
        public IEnumerable<T.CalItem> Items(DateTime from, DateTime to)
        {
            if (Throw) throw new InvalidOperationException("壊れた");
            return new[]
            {
                new T.CalItem { Key = "bill1", Title = "動画 990円", Start = new DateTime(2026, 10, 30), End = new DateTime(2026, 10, 31), AllDay = true },
                new T.CalItem { Key = "x:Teal Wallet:bill2", Title = "音楽", Start = new DateTime(2026, 12, 1), End = new DateTime(2026, 12, 2), AllDay = true },
            };
        }
    }

    static T.TaskItem Task(string title, params string[] subs)
    {
        var t = new T.TaskItem { Title = title };
        foreach (var s in subs) t.Subtasks.Add(new T.SubTask { Text = s });
        return t;
    }

    // ---------------------------------------------------------------- 小タスク (画面から切り離した部分)
    public static void RunSubtasks(Action<bool, string> Check)
    {
        TC.AppInfo.Init("TealPlan", "Teal Plan", "");
        var t = Task("引っ越し", "見積もり", "荷造り");
        Check(T.SubtaskLogic.Add(t, "  住所変更 ") == 2 && t.Subtasks[2].Text == "住所変更", "小タスクを足す (前後の空白は取る)");
        Check(T.SubtaskLogic.Add(t, "   ") == -1 && t.Subtasks.Count == 3, "空なら足さない");
        Check(T.SubtaskLogic.Add(t, "電気", 1) == 1 && t.Subtasks[1].Text == "電気", "位置を決めて足す");
        Check(T.SubtaskLogic.AddLines(t, "・ガス\r\n- 水道\n\n□ネット", 2) == 3 && string.Join(",", t.Subtasks.Select(s => s.Text)) == "見積もり,電気,ガス,水道,ネット,荷造り,住所変更", "貼り付けた複数行をまとめて足す (・ - □ は取る)");
        Check(T.SubtaskLogic.Toggle(t, 0) && t.Subtasks[0].Done && T.SubtaskLogic.Progress(t) == "1/7", "完了にして進み具合 1/7");
        Check(!T.SubtaskLogic.Toggle(t, 9), "範囲外は何もしない");
        Check(T.SubtaskLogic.Rename(t, 1, "電気の解約") && !T.SubtaskLogic.Rename(t, 1, "電気の解約") && !T.SubtaskLogic.Rename(t, 1, " "), "名前を変える (同じ・空は変えない)");
        Check(T.SubtaskLogic.MoveTo(t, 0, 2) && t.Subtasks[2].Text == "見積もり" && t.Subtasks[2].Done, "並べ替え (完了の印もいっしょ)");
        Check(T.SubtaskLogic.MoveTo(t, 6, 99) == false && T.SubtaskLogic.Move(t, 6, -6) && t.Subtasks[0].Text == "住所変更", "端を越えない・Move で上下");
        Check(T.SubtaskLogic.Delete(t, 0) && t.Subtasks.Count == 6 && !T.SubtaskLogic.Delete(t, 6), "削除");

        var data = new T.TaskData();
        var a = Task("A", "1", "2"); var b = Task("B"); var c = Task("C", "x");
        data.Tasks.AddRange(new[] { a, b, c });
        Check(T.SubtaskLogic.ExpandAll(data, data.Tasks) == 2 && data.IsExpanded(a) && !data.IsExpanded(b) && data.IsExpanded(c), "すべて開く (小タスクのあるタスクだけ)");
        Check(T.SubtaskLogic.ExpandAll(data, data.Tasks) == 0, "もう開いていれば 0");
        var rows = T.PlanRows.Tasks(data.Tasks, data.Expanded);
        Check(string.Join(",", rows.Select(r => r.Key.Split(':')[0] + (r.Kind == T.RowKind.Sub ? r.Sub.ToString() : ""))) == "t,s0,s1,a,t,t,s0,a", "開いたタスクの下に小タスクと「＋小タスク」の行");
        Check(rows[1].ToString() == "1" && rows[1].Key == "s:" + a.Id + ":0" && T.PlanRows.IndexOfKey(rows, "a:" + c.Id) == 7, "行のキー");
        Check(T.SubtaskLogic.CollapseAll(data) == 2 && T.PlanRows.Tasks(data.Tasks, data.Expanded).Count == 3, "すべて閉じる");

        // 保存と読み込み (開いている状態も残す・古いファイルも読める)
        data.SetExpanded(a, true); data.Expanded.Add(null);
        var back = TC.DataStore.FromJson<T.TaskData>(TC.DataStore.ToJson(data)); back.Normalize();
        Check(back.IsExpanded(back.Find(a.Id)) && back.Expanded.Count == 1, "開いているタスクを保存する");
        var old = TC.DataStore.FromJson<T.TaskData>(System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"tasks\":[{\"id\":\"q\",\"title\":\"古い\",\"priority\":1,\"tags\":[],\"repeat\":\"none\",\"done\":false,\"subtasks\":[{\"text\":\"a\",\"done\":true}]}],\"settings\":{\"remind\":true,\"remindMinutes\":15,\"morning\":true},\"notified\":[]}"));
        old.Normalize();
        Check(old.Version == 2 && old.Events != null && old.Events.Count == 0 && old.Expanded.Count == 0 && old.Settings.EventRemind == 10 && old.Tasks[0].Subtasks.Count == 1, "1.9 の tasks.json を読める (予定は空・予定のお知らせは 10 分前)");
    }

    // ---------------------------------------------------------------- ひとこと入力の予定
    public static void RunParse(Action<bool, string> Check)
    {
        TC.AppInfo.Init("TealPlan", "Teal Plan", "");
        var p = T.QuickParser.Parse("明日 15:00-16:00 歯医者 @駅前", Now);
        Check(p.IsEvent && p.Title == "歯医者" && p.Location == "駅前" && p.Date == Now.Date.AddDays(1) && p.Time == TimeSpan.FromHours(15) && p.EndTime == TimeSpan.FromHours(16), "「明日 15:00-16:00 歯医者 @駅前」→ 予定: " + p.Title + "/" + p.Location + "/" + p.Time + "-" + p.EndTime);
        var e = p.ToEvent(Now, 10);
        Check(e.Start == "2026-10-29T15:00" && e.End == "2026-10-29T16:00" && !e.AllDay && e.Location == "駅前" && e.RemindMin == 10, "予定にする: " + e.Start + "〜" + e.End);
        var p2 = T.QuickParser.Parse("15時〜16時半 打ち合わせ", Now);
        Check(p2.IsEvent && p2.Title == "打ち合わせ" && p2.EndTime == new TimeSpan(16, 30, 0) && p2.Date == Now.Date, "「15時〜16時半」も時刻の範囲 (日付なしは今日)");
        var p3 = T.QuickParser.Parse("11/3-11/5 出張", Now);
        Check(p3.IsEvent && p3.Date == new DateTime(2026, 11, 3) && p3.EndDate == new DateTime(2026, 11, 5), "「11/3-11/5」日付の範囲");
        var e3 = p3.ToEvent(Now);
        Check(e3.AllDay && e3.Start == "2026-11-03" && e3.End == "2026-11-05", "時刻がなければ終日で 3 日間: " + e3.Start + "〜" + e3.End);
        var p4 = T.QuickParser.Parse("10/30 終日 健康診断", Now);
        Check(p4.IsEvent && p4.AllDay && p4.Title == "健康診断" && p4.ToEvent(Now).AllDay, "「終日」");
        var p5 = T.QuickParser.Parse("明日から2日間 研修", Now);
        Check(p5.IsEvent && p5.Date == Now.Date.AddDays(1) && p5.EndDate == Now.Date.AddDays(2) && p5.Title == "研修", "「明日から2日間」");
        var p6 = T.QuickParser.Parse("予定: 読書会", Now);
        Check(p6.IsEvent && p6.Title == "読書会" && p6.Date == Now.Date, "先頭の「予定:」で予定にする");
        var p7 = T.QuickParser.Parse("明日15時 歯医者 #病院", Now);
        Check(!p7.IsEvent && p7.Title == "歯医者", "範囲・場所がなければ今までどおりタスク");
        var p8 = T.QuickParser.Parse("ランチ ＠渋谷", Now);
        Check(p8.IsEvent && p8.Location == "渋谷", "全角の ＠ も場所");
        Check(T.QuickParser.Parse("メール a@b.jp に返す", Now).Location == null, "メールアドレスは場所にしない");
        Check(p.Describe(Now).StartsWith("予定「歯医者」"), "入力欄の下の説明: " + p.Describe(Now));
    }

    // ---------------------------------------------------------------- 予定 (くり返し・集める・お知らせ)
    public static void RunEvents(Action<bool, string> Check)
    {
        TC.AppInfo.Init("TealPlan", "Teal Plan", "");
        var e = new T.EventItem { Title = "ジム" };
        e.SetTimes(new DateTime(2026, 10, 5, 19, 0, 0), new DateTime(2026, 10, 5, 20, 0, 0), false);
        e.Repeat = T.Repeat.Weekly;
        var occ = T.EventLogic.Occurrences(e, new DateTime(2026, 10, 26), new DateTime(2026, 11, 16));
        Check(occ.Count == 3 && occ[0] == new DateTime(2026, 10, 26, 19, 0, 0), "毎週: 範囲の中の回 (" + occ.Count + ")");
        e.Skip = new List<string> { "2026-11-02" };
        e.Until = "2026-11-10";
        occ = T.EventLogic.Occurrences(e, new DateTime(2026, 10, 26), new DateTime(2026, 12, 1));
        Check(occ.Count == 2 && occ[1] == new DateTime(2026, 11, 9, 19, 0, 0), "この回だけ消した日・終わりの日のあとは出さない");
        var trip = new T.EventItem { Title = "出張" };
        trip.SetTimes(new DateTime(2026, 11, 3), new DateTime(2026, 11, 5), true);
        Check(trip.EndAt == new DateTime(2026, 11, 6) && T.EventLogic.Occurrences(trip, new DateTime(2026, 11, 5), new DateTime(2026, 11, 6)).Count == 1, "複数日の終日: 最後の日にも重なる");
        var it = T.EventLogic.ToItem(trip, trip.StartAt);
        Check(it.MultiDay && it.LastDay == new DateTime(2026, 11, 5) && it.OnDay(new DateTime(2026, 11, 4)) && T.EventLogic.TimeText(it) == "11/3(火)〜11/5(木)", "複数日の表示: " + T.EventLogic.TimeText(it));
        var wd = new T.EventItem { Title = "朝会" };
        wd.SetTimes(new DateTime(2026, 10, 30, 9, 30, 0), new DateTime(2026, 10, 30, 9, 45, 0), false);
        wd.Repeat = T.Repeat.Weekdays;
        Check(T.EventLogic.Occurrences(wd, new DateTime(2026, 10, 30), new DateTime(2026, 11, 3)).Count == 2, "平日だけ (金・月)");

        var data = new T.TaskData();
        data.Events.Add(e); data.Events.Add(trip);
        var task = new T.TaskItem { Title = "資料", Due = "2026-11-04T10:00", DurationMin = 90 };
        data.Tasks.Add(task);
        data.Tasks.Add(new T.TaskItem { Title = "期限なし" });
        var from = new DateTime(2026, 11, 2); var to = new DateTime(2026, 11, 9);
        var src = new FakeSource();
        var all = T.EventLogic.Collect(data, new T.IPlanSource[] { src }, new DateTime(2026, 10, 26), to);
        Check(all.Count(x => x.Kind == T.CalKind.Event) == 2 && all.Count(x => x.Kind == T.CalKind.Task) == 1, "予定とタスクを集める (ジム 10/26・出張)");
        var ext = all.Where(x => x.Kind == T.CalKind.External).ToList();
        Check(ext.Count == 1 && ext[0].ReadOnly && ext[0].Key == "x:Teal Wallet:bill1" && ext[0].SourceName == "Teal Wallet", "ほかのアプリの項目は読むだけ (キーは x:)");
        var far = T.EventLogic.Collect(data, new T.IPlanSource[] { src }, new DateTime(2026, 11, 25), new DateTime(2026, 12, 5));
        Check(far.Any(x => x.Key == "x:Teal Wallet:bill2"), "もう x: のついたキーはそのまま");
        Check(T.EventLogic.Collect(data, new T.IPlanSource[] { src }, from, to, T.EventLogic.ShowTasks).All(x => x.Kind == T.CalKind.Task), "タスクだけ");
        Check(T.EventLogic.Collect(data, null, from, to, T.EventLogic.ShowEvents).All(x => x.Kind == T.CalKind.Event), "予定だけ");
        T.EventLogic.LastSourceError = null;
        var bad = new FakeSource { Throw = true };
        Check(T.EventLogic.Collect(data, new T.IPlanSource[] { bad }, from, to).Count == 2 && T.EventLogic.LastSourceError != null, "読めない出どころがあっても、ほかは出す");
        var tItem = all.First(x => x.Kind == T.CalKind.Task);
        Check(tItem.End == new DateTime(2026, 11, 4, 11, 30, 0), "時刻つきのタスクは「かかる時間」の長さ");

        var agenda = T.PlanRows.Agenda(T.EventLogic.Collect(data, null, from, to), from, to, from, new[] { task.Id }, data.Tasks.Where(x => !x.HasDue));
        var kinds = string.Join(",", agenda.Select(r => r.Kind.ToString()));
        Check(kinds == "Header,Event,Header,Task,AddSub,Header,Task", "予定表: 日ごとの見出し・消した回は出さない・複数日は最初の日だけ・期限なしは最後: " + kinds);
        Check(agenda[0].Text == "11月3日 (火)  明日" && agenda[2].Text.StartsWith("11月4日") && agenda[5].Text == "期限なしのタスク", "見出し: " + agenda[0].Text);

        // お知らせ
        var dn = new T.TaskData();
        var dent = new T.EventItem { Title = "歯医者", Location = "駅前", RemindMin = 10 };
        dent.SetTimes(Now.AddMinutes(8), Now.AddMinutes(38), false);
        var none = new T.EventItem { Title = "知らせない", RemindMin = -1 };
        none.SetTimes(Now.AddMinutes(5), Now.AddMinutes(20), false);
        var ad = new T.EventItem { Title = "健康診断", RemindMin = 0 };
        ad.SetTimes(Now.Date, Now.Date, true);
        dn.Events.AddRange(new[] { dent, none, ad });
        var ns = T.EventLogic.DueNotices(dn, Now);
        Check(ns.Count == 2 && ns.Any(n => n.Event == dent && n.Text.Contains("あと 8 分") && n.Text.Contains("@駅前")) && ns.Any(n => n.Event == ad && n.Text.Contains("終日")), "予定のお知らせ (10 分前・終日は 9:00): " + string.Join(" | ", ns.Select(n => n.Text)));
        Check(T.EventLogic.DueNotices(dn, Now.AddMinutes(-5)).Count == 0, "早すぎるときは出さない");
        foreach (var n in ns) dn.Notified.Add(n.Key);
        Check(T.EventLogic.DueNotices(dn, Now.AddMinutes(1)).Count == 0, "一度出したら出さない");
        dn.Settings.Remind = false;
        Check(T.EventLogic.DueNotices(dn, Now.AddMinutes(30)).Count == 0, "お知らせを切っていれば出さない");
        var csv = T.EventLogic.ToCsvLine(dent);
        Check(csv.Contains("歯医者") && csv.Contains("駅前"), "CSV の 1 行");
    }

    // ---------------------------------------------------------------- 画面 (一覧の小タスク・キー操作)
    public static void RunListUi(Action<bool, string> Check)
    {
        TC.AppInfo.Init("TealPlan", "Teal Plan", "");
        using (var f = new T.MainForm())
        {
            f.Clock = () => Now;
            f.ConfirmHook = m => true;
            f.Data = new T.TaskData();
            var a = Task("旅行の準備", "切符", "宿", "荷物");
            a.Subtasks[0].Done = true;
            var b = Task("掃除");
            f.Data.Tasks.Add(a); f.Data.Tasks.Add(b);
            f.RefreshAll();
            f.Tabs.SelectedIndex = 2;
            var L = f.List;
            Func<string> keys = () => string.Join(",", L.Items.Cast<object>().Select(o => { var r = o as T.ListRow; return r == null ? "?" : r.Kind.ToString(); }));
            Func<T.ListRow> sel = () => L.SelectedItem as T.ListRow;
            Func<string, bool> pick = k => { for (int i = 0; i < L.Items.Count; i++) { var r = L.Items[i] as T.ListRow; if (r != null && r.Key == k) { L.SelectedIndex = i; return true; } } return false; };
            Check(L.Items.Count == 2, "閉じているときはタスクだけ (" + keys() + ")");
            f.ToggleExpand(a);
            Check(f.Data.IsExpanded(a) && L.Items.Count == 6 && keys().StartsWith("Task,Sub,Sub,Sub,AddSub"), "▸ で開くと小タスクがすぐ下に並ぶ: " + keys());
            f.ToggleExpand(a);
            Check(!f.Data.IsExpanded(a) && L.Items.Count == 2, "▾ で閉じる");

            // キー操作
            pick("t:" + a.Id);
            Check(f.HandleListKey(L, Keys.Right) && f.Data.IsExpanded(a), "→ で開く");
            f.HandleListKey(L, Keys.Right);
            Check(sel() != null && sel().Kind == T.RowKind.Sub && sel().Sub == 0, "もう一度 → で最初の小タスクへ");
            f.HandleListKey(L, Keys.Down);
            Check(sel().Sub == 1, "↓ で次の小タスク");
            f.HandleListKey(L, Keys.Space);
            Check(a.Subtasks[1].Done && sel().Key == "s:" + a.Id + ":1" && T.SubtaskLogic.Progress(a) == "2/3", "Space で小タスクを完了 (選んだまま・2/3)");
            f.HandleListKey(L, Keys.Alt | Keys.Up);
            Check(a.Subtasks[0].Text == "宿" && sel().Sub == 0, "Alt+↑ で上へ");
            f.HandleListKey(L, Keys.Alt | Keys.Down);
            Check(a.Subtasks[1].Text == "宿" && sel().Sub == 1, "Alt+↓ で下へ");
            f.HandleListKey(L, Keys.Left);
            Check(sel().Kind == T.RowKind.Task && sel().Task == a, "← で親のタスクへ");
            f.HandleListKey(L, Keys.Left);
            Check(!f.Data.IsExpanded(a), "もう一度 ← で閉じる");

            // Tab で追加 (続けて入力できる)
            pick("t:" + a.Id);
            Check(f.HandleListKey(L, Keys.Tab) && f.InlineOpen && f.Data.IsExpanded(a) && sel().Kind == T.RowKind.AddSub, "Tab で小タスクの入力欄を開く");
            f.InlineEditor.Text = "パスポート";
            f.CommitInline(true);
            Check(a.Subtasks.Count == 4 && a.Subtasks[3].Text == "パスポート" && f.InlineOpen, "Enter で追加して、続けて入力できる");
            f.InlineEditor.Text = "充電器\n薬";
            f.CommitInline(true);
            Check(a.Subtasks.Count == 6 && a.Subtasks[5].Text == "薬", "貼り付けた複数行も追加");
            f.InlineEditor.Text = "";
            f.CommitInline(true);
            Check(!f.InlineOpen && a.Subtasks.Count == 6, "空で Enter なら閉じる");
            f.BeginAddSub(L, a);
            f.InlineEditor.Text = "やめる";
            f.CancelInline();
            Check(!f.InlineOpen && a.Subtasks.Count == 6, "Esc でやめる");
            // 小タスクの途中に Tab で差し込む
            pick("s:" + a.Id + ":0");
            f.HandleListKey(L, Keys.Tab);
            f.InlineEditor.Text = "地図";
            f.CommitInline(false);
            Check(a.Subtasks[1].Text == "地図" && !f.InlineOpen, "小タスクで Tab → すぐ下に差し込む");
            // F2 で名前を変える
            pick("s:" + a.Id + ":1");
            f.HandleListKey(L, Keys.F2);
            Check(f.InlineOpen && f.InlineEditor.Text == "地図", "F2 で名前の入力欄");
            f.InlineEditor.Text = "地図アプリ";
            f.CommitInline(true);
            Check(a.Subtasks[1].Text == "地図アプリ" && !f.InlineOpen && a.Subtasks.Count == 7, "決めると名前が変わる (増えない)");
            // Delete
            pick("s:" + a.Id + ":1");
            f.HandleListKey(L, Keys.Delete);
            Check(a.Subtasks.Count == 6 && !a.Subtasks.Any(s => s.Text == "地図アプリ") && sel() != null && sel().Kind == T.RowKind.Sub, "Delete で小タスクを削除 (確認なし・次を選ぶ)");
            f.Undo();
            var a2 = f.Data.Find(a.Id);
            Check(a2.Subtasks.Count == 7 && f.Data.IsExpanded(a2), "Ctrl+Z で戻す (開いたまま)");
            a = a2;

            // すべて開く / 閉じる
            b.Subtasks.Clear();
            var b2 = f.Data.Find(b.Id); b2.Subtasks.Add(new T.SubTask { Text = "床" });
            f.CollapseAll();
            Check(f.Data.Expanded.Count == 0 && L.Items.Count == 2, "すべて閉じる");
            Check(f.HandleListKey(L, Keys.Control | Keys.Shift | Keys.Right) && f.Data.Expanded.Count == 2 && L.Items.Count == 2 + 7 + 1 + 1 + 1, "Ctrl+Shift+→ ですべて開く");
            var saved = TC.DataStore.FromJson<T.TaskData>(TC.DataStore.ToJson(f.Data));
            Check(saved.Expanded.Count == 2, "開いている状態はデータに残る (次に起動しても同じ)");
            f.HandleListKey(L, Keys.Control | Keys.Shift | Keys.Left);
            Check(f.Data.Expanded.Count == 0, "Ctrl+Shift+← ですべて閉じる");

            // タスクの削除は小タスクの数も聞く
            string asked = null;
            f.ConfirmHook = m => { asked = m; return false; };
            pick("t:" + a.Id);
            f.HandleListKey(L, Keys.Delete);
            Check(asked != null && asked.Contains("小タスク 7 件") && f.Data.Tasks.Count == 2, "タスクの削除: 小タスクの数も出して確かめる (いいえなら消さない): " + asked);
            // 今日の一覧でも開ける
            a.Due = "2026-10-28"; f.RefreshAll();
            f.Tabs.SelectedIndex = 0;
            f.ToggleExpand(a);
            Check(f.TodayList.Items.Cast<object>().Count(o => o is T.ListRow && ((T.ListRow)o).Kind == T.RowKind.Sub) == 7, "今日の一覧でも小タスクを開ける");
        }
    }

    // ---------------------------------------------------------------- 画面 (予定・カレンダー)
    public static void RunEventUi(Action<bool, string> Check)
    {
        TC.AppInfo.Init("TealPlan", "Teal Plan", "");
        using (var f = new T.MainForm())
        {
            f.Clock = () => Now;
            f.ConfirmHook = m => true;
            f.Data = new T.TaskData();
            f.RefreshAll();
            f.Tabs.SelectedIndex = 1;
            var cv = f.Calendar;
            T.EventItem formSaw = null;
            f.EventFormHook = e => { formSaw = e; e.Title = "会議"; e.Location = "3F"; return true; };
            cv.Mode = T.CalMode.Week;
            cv.Day = Now.Date;
            cv.SimulateCreate(new DateTime(2026, 10, 29, 14, 0, 0), new DateTime(2026, 10, 29, 15, 30, 0), false);
            Check(f.Data.Events.Count == 1 && formSaw != null && f.Data.Events[0].Start == "2026-10-29T14:00" && f.Data.Events[0].End == "2026-10-29T15:30" && f.Data.Events[0].RemindMin == 10, "週の表でなぞって予定を作る (14:00〜15:30)");
            var ev = f.Data.Events[0];
            var item = cv.ShownItems.FirstOrDefault(x => x.Event == ev);
            Check(item != null, "作った予定がカレンダーに出る");
            cv.SimulateMove(item, new DateTime(2026, 10, 30, 10, 0, 0), true);
            Check(ev.Start == "2026-10-30T10:00" && ev.End == "2026-10-30T11:30", "ドラッグで動かす (長さはそのまま): " + ev.Start);
            item = cv.ShownItems.First(x => x.Event == ev);
            cv.SimulateResize(item, new DateTime(2026, 10, 30, 10, 5, 0));
            Check(ev.End == "2026-10-30T10:15", "下の端で長さを変える (最低 15 分): " + ev.End);
            item = cv.ShownItems.First(x => x.Event == ev);
            cv.SimulateMove(item, new DateTime(2026, 10, 31), false);
            Check(ev.AllDay && ev.Start == "2026-10-31", "終日の欄へ動かすと終日に");
            f.Undo();
            ev = f.Data.Events[0];
            Check(!ev.AllDay && ev.Start == "2026-10-30T10:00", "Ctrl+Z で戻す");

            f.EventFormHook = e => { e.Title = "旅行"; return true; };
            cv.Mode = T.CalMode.Month;
            cv.SimulateCreate(new DateTime(2026, 11, 7), new DateTime(2026, 11, 3), true);
            var trip = f.Data.Events.First(e => e.Title == "旅行");
            Check(trip.AllDay && trip.Start == "2026-11-03" && trip.End == "2026-11-07" && trip.RemindMin == -1, "月の表で日をまたいでなぞると複数日の終日予定: " + trip.Start + "〜" + trip.End);
            f.EventFormHook = e => false;
            cv.SimulateCreate(new DateTime(2026, 11, 10), new DateTime(2026, 11, 10), true);
            Check(f.Data.Events.Count == 2, "ダイアログでやめたら作らない");
            f.EventFormHook = e => { e.Title = "  "; return true; };
            Check(f.AddEventAt(new DateTime(2026, 11, 11), true) == null, "タイトルが空なら作らない");
            f.EventFormHook = e => { e.Title = "ランチ"; return true; };
            var lunch = f.AddEventAt(Now.Date, true);
            Check(lunch != null && lunch.Start == "2026-10-28T10:00", "＋予定 (今日なら次のちょうどの時刻): " + (lunch == null ? "" : lunch.Start));

            // くり返す予定の削除
            var gym = new T.EventItem { Title = "ジム", Repeat = T.Repeat.Weekly };
            gym.SetTimes(new DateTime(2026, 10, 26, 19, 0, 0), new DateTime(2026, 10, 26, 20, 0, 0), false);
            f.AddEventDirect(gym);
            gym = f.Data.Events.Last();
            f.SeriesHook = m => 0;
            Check(f.DeleteEvent(gym, new DateTime(2026, 11, 2, 19, 0, 0)) && f.Data.Events.Contains(gym) && gym.Skip.Contains("2026-11-02"), "くり返す予定: この回だけ削除");
            f.SeriesHook = m => -1;
            Check(!f.DeleteEvent(gym, new DateTime(2026, 11, 9, 19, 0, 0)) && f.Data.Events.Contains(gym), "やめたら何もしない");
            // 動かすとこの回だけ消した日もずれる
            var gItem = T.EventLogic.ToItem(gym, new DateTime(2026, 10, 26, 19, 0, 0));
            f.MoveItem(gItem, new DateTime(2026, 10, 27, 19, 0, 0), true);
            Check(gym.Start == "2026-10-27T19:00" && gym.Skip.Contains("2026-11-03"), "くり返しごと 1 日ずらす (消した回もずれる)");
            f.SeriesHook = m => 1;
            Check(f.DeleteEvent(gym, new DateTime(2026, 11, 10, 19, 0, 0)) && !f.Data.Events.Contains(gym), "すべての回を削除");

            // ひとこと入力で予定
            f.Quick.Text = "明日 15:00-16:00 歯医者 @駅前";
            var r = f.QuickAdd();
            Check(r == null && f.LastQuickEvent != null && f.LastQuickEvent.Title == "歯医者" && f.LastQuickEvent.Location == "駅前" && f.Data.Events.Contains(f.LastQuickEvent) && f.Quick.Text == "", "ひとこと入力で予定を追加 (タスクは増えない)");
            Check(f.Data.Tasks.Count == 0, "タスクは 0 件のまま");

            // ほかのアプリの項目 (読むだけ)
            f.ExternalSources.Add(new FakeSource());
            cv.Mode = T.CalMode.Week; cv.Day = new DateTime(2026, 10, 30);
            f.RefreshAll();
            var xi = cv.ShownItems.FirstOrDefault(i => i.Kind == T.CalKind.External);
            Check(xi != null && xi.ReadOnly, "ほかのアプリの項目もカレンダーに出る (読むだけ)");
            int n = f.Data.Events.Count;
            f.DeleteItem(xi);
            f.MoveItem(xi, new DateTime(2026, 11, 1), false);
            cv.SimulateMove(xi, new DateTime(2026, 11, 1), false);
            Check(f.Data.Events.Count == n && xi.Start == new DateTime(2026, 10, 30), "読むだけの項目は消せない・動かせない");
            Check(f.CalItems(new DateTime(2026, 10, 26), new DateTime(2026, 11, 2), T.EventLogic.ShowTasks).All(i => i.Kind == T.CalKind.Task), "タスクだけを表示");

            // 予定の一覧
            f.Tabs.SelectedIndex = 2;
            f.Data.Settings.ListKind = 1;
            f.RefreshList();
            var rows = f.List.Items.Cast<object>().OfType<T.ListRow>().ToList();
            Check(rows.Any(rr => rr.Kind == T.RowKind.Header) && rows.Any(rr => rr.Kind == T.RowKind.Event && rr.Item.Title == "歯医者"), "一覧の「予定」: 日ごとの予定表");

            // 見本データ
            f.Data = new T.TaskData();
            f.InsertSamples();
            Check(f.Data.Events.Count >= 5 && f.Data.Expanded.Count >= 1 && f.Data.Tasks.Count > 10, "見本データ: 予定と、開いた小タスク");
        }
    }

    // ---------------------------------------------------------------- Teal Tasks → Teal Plan (引き継ぎ)
    public static void RunRename(Action<bool, string> Check, string tmp)
    {
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var oldLegacy = Environment.GetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR");
        var root = Path.Combine(tmp, "v1100-data");
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", root);
            Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", Path.Combine(tmp, "v1100-legacy"));
            Directory.CreateDirectory(Path.Combine(root, "TealTasks"));
            File.WriteAllText(Path.Combine(root, "TealTasks", "tasks.json"), "{\"version\":1,\"tasks\":[{\"id\":\"q\",\"title\":\"古いタスク\",\"priority\":1,\"tags\":[],\"repeat\":\"none\",\"done\":false,\"subtasks\":[{\"text\":\"小\",\"done\":false}]}],\"settings\":{\"remind\":true,\"remindMinutes\":30,\"morning\":false},\"notified\":[]}");
            TC.AppInfo.Init(new TC.AppSpec { Id = "TealPlan", Name = "Teal Plan", LegacyIds = new[] { "TealTasks" } });
            var from = TC.DataStore.MigrateLegacyData();
            Check(from != null && Path.GetFileName(from) == "TealTasks" && File.Exists(Path.Combine(root, "TealPlan", "tasks.json")) && File.Exists(Path.Combine(root, "TealTasks", "tasks.json")), "TealTasks フォルダのデータを TealPlan にコピー (元は残す)");
            string w;
            var d = TC.DataStore.LoadOrDefault(T.TaskData.FileName, () => new T.TaskData(), out w);
            d.Normalize();
            Check(w == null && d.Tasks.Count == 1 && d.Tasks[0].Title == "古いタスク" && d.Settings.RemindMinutes == 30 && !d.Settings.Morning && d.Events.Count == 0, "引き継いだタスクと設定を読める");
            Check(TC.DataStore.MigrateLegacyData() == null, "2 回目は何もしない");
            Check(TC.RenameMigration.OldName("TealTasks") == "Teal Tasks" && TC.RenameMigration.RenamedIn["TealTasks"] == "1.10.0", "前の名前と変えた版");
            Check(TC.RenameMigration.CompareVersions("1.9.0", "1.10.0") < 0 && TC.RenameMigration.CompareVersions("1.10.0", "1.10.0") == 0 && TC.RenameMigration.CompareVersions("1.10.1", "1.9.9") > 0, "版の比べ方 (1.10 > 1.9)");

            var pf = new TC.AppPrefsFile();
            pf.Apps.Add(new TC.AppPref { Id = "TealTasks", Hotkey = "Ctrl+Alt+T", Resident = true });
            Check(TC.RenameMigration.MigratePrefs(pf, "TealPlan", new[] { "TealTasks" }) && pf.Find("TealTasks") == null && pf.Find("TealPlan").Hotkey == "Ctrl+Alt+T" && pf.Find("TealPlan").Resident == true, "ショートカット・常駐を TealPlan に付け替える");

            var exe = Path.Combine(tmp, "v1100-suite", "apps", "TealPlan", "TealPlan.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "exe");
            var key = new TC.MemoryRunKey();
            TC.Startup.RunKey = key;
            key.Values["TealSuite.TealTasks"] = "\"C:\\TealSuite\\apps\\TealTasks\\TealTasks.exe\" --startup";
            Check(TC.RenameMigration.MigrateStartup("TealTasks", "TealPlan", exe) && key.Get("TealSuite.TealTasks") == null && key.Get("TealSuite.TealPlan") == TC.Startup.CommandFor(exe), "スタートアップを TealPlan.exe で登録し直す");

            // Hub
            HC.AppInfo.Init("TealHub", "Teal Hub", "");
            var prefsPath = HC.AppPrefs.FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(prefsPath));
            File.WriteAllText(prefsPath, "{\"version\":1,\"apps\":[{\"id\":\"TealWallet\",\"hotkey\":\"Ctrl+Alt+W\"},{\"id\":\"TealTasks\",\"hotkey\":\"Ctrl+Alt+T\",\"resident\":true}]}");
            var hkey = new HC.MemoryRunKey();
            HC.Startup.RunKey = hkey;
            hkey.Values["TealSuite.TealTasks"] = "\"C:\\old\\TealTasks.exe\" --startup";
            var apps = new List<H.AppEntry>
            {
                new H.AppEntry { Id = "TealWallet", Name = "Teal Wallet", LegacyIds = new[] { "TealSub" } },
                new H.AppEntry { Id = "TealPlan", Name = "Teal Plan", LegacyIds = new[] { "TealTasks" }, ExePath = exe },
            };
            var done = H.HubMigration.Run(apps);
            var prefs = HC.AppPrefs.Load();
            Check(done.Count == 1 && done[0] == "Teal Tasks → Teal Plan" && prefs.Find("TealTasks") == null && prefs.Find("TealPlan").Hotkey == "Ctrl+Alt+T" && prefs.Find("TealPlan").Resident == true, "Hub: Teal Tasks の設定を Teal Plan に付け替える");
            Check(hkey.Get("TealSuite.TealPlan") != null && hkey.Get("TealSuite.TealTasks") == null, "Hub: スタートアップも付け替える");
            Check(H.HubMigration.Run(apps).Count == 0, "Hub: 2 回目は何もしない");

            // 「Teal Tasks は Teal Plan になりました」は 1 回だけ (1.9.0 の Teal Sub のお知らせは出し直さない)
            Directory.CreateDirectory(Path.Combine(root, "TealSub"));
            if (File.Exists(H.HubMigration.NoticesPath)) File.Delete(H.HubMigration.NoticesPath);
            var pend = H.HubMigration.PendingNotices(apps, new List<string>(), "1.10.0");
            Check(pend.Count == 1 && pend[0] == "Teal Tasks → Teal Plan", "1.9.x から: Teal Plan になったお知らせを 1 回出す (Teal Sub は出さない): " + string.Join(",", pend));
            Check(H.HubMigration.PendingNotices(apps, new List<string>(), "1.10.0").Count == 0, "2 回目は出さない");
            File.Delete(H.HubMigration.NoticesPath);
            Directory.Delete(Path.Combine(root, "TealTasks"), true);
            Check(H.HubMigration.PendingNotices(apps, new List<string>(), "1.10.0").Count == 0, "Teal Tasks を使っていなかった (前のフォルダがない) なら出さない");
            File.Delete(H.HubMigration.NoticesPath);
            Check(H.HubMigration.PendingNotices(apps, new List<string> { "Teal Tasks → Teal Plan" }, "1.10.0").Count == 1, "引き継ぎをしたときは出す");

            // バックアップの復元: 1.9.x のバックアップ (TealTasks/) は TealPlan/ に戻す
            var src = Path.Combine(tmp, "v1100-src");
            if (Directory.Exists(src)) Directory.Delete(src, true);
            Directory.CreateDirectory(Path.Combine(src, "TealTasks"));
            Directory.CreateDirectory(Path.Combine(src, "TealWallet"));
            File.WriteAllText(Path.Combine(src, "TealTasks", "tasks.json"), "{\"old\":1}");
            File.WriteAllText(Path.Combine(src, "TealWallet", "subs.json"), "{}");
            var zip = Path.Combine(tmp, "v1100-backup-191.zip");
            if (File.Exists(zip)) File.Delete(zip);
            H.HubBackup.Create(src, zip, "1.9.1", new DateTime(2026, 10, 20));
            var renames = new Dictionary<string, string> { { "TealSub", "TealWallet" }, { "TealTasks", "TealPlan" } };
            var map = H.HubBackup.FolderMap(H.HubBackup.Inspect(zip), renames);
            Check(map.Count == 1 && map["TealTasks"] == "TealPlan", "1.9.x のバックアップ: TealTasks → TealPlan に付け替える");
            var dst = Path.Combine(tmp, "v1100-dst");
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            int n = H.HubBackup.Restore(zip, dst, null, "1.10.0", Now, renames);
            Check(n == 2 && File.ReadAllText(Path.Combine(dst, "TealPlan", "tasks.json")) == "{\"old\":1}" && !Directory.Exists(Path.Combine(dst, "TealTasks")), "TealPlan フォルダに戻す");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
            Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", oldLegacy);
            TC.Startup.RunKey = new TC.MemoryRunKey();
            HC.Startup.RunKey = new HC.MemoryRunKey();
        }
    }

    // ---------------------------------------------------------------- アップデート (apps.json の付け替え・古い exe の削除)
    public static void RunUpdate(Action<bool, string> Check, string distDir)
    {
        var installed = "{\"manifestVersion\":1,\"suite\":\"TealSuite\",\"suiteVersion\":\"1.9.1\",\"apps\":[" +
            "{\"id\":\"TealHub\",\"name\":\"Teal Hub\",\"exe\":\"TealHub.exe\",\"version\":\"1.9.1\",\"self\":true}," +
            "{\"id\":\"TealTasks\",\"name\":\"Teal Tasks\",\"exe\":\"apps/TealTasks/TealTasks.exe\",\"version\":\"1.9.1\",\"resident\":true}," +
            "{\"id\":\"TealWallet\",\"name\":\"Teal Wallet\",\"exe\":\"apps/TealWallet/TealWallet.exe\",\"version\":\"1.9.1\",\"legacyIds\":[\"TealSub\"]}]}";
        var manifest = "{\"format\":1,\"suite\":\"TealSuite\",\"version\":\"1.10.0\",\"minFromVersion\":\"1.8.0\",\"notes\":\"t\",\"files\":[],\"delete\":[\"apps/TealTasks/TealTasks.exe\",\"apps/TealTasks/TealTasks.ico\"],\"apps\":[" +
            "{\"id\":\"TealHub\",\"name\":\"Teal Hub\",\"exe\":\"TealHub.exe\",\"version\":\"1.10.0\",\"self\":true}," +
            "{\"id\":\"TealPlan\",\"name\":\"Teal Plan\",\"exe\":\"apps/TealPlan/TealPlan.exe\",\"version\":\"1.10.0\",\"legacyIds\":[\"TealTasks\"],\"resident\":true}," +
            "{\"id\":\"TealWallet\",\"name\":\"Teal Wallet\",\"exe\":\"apps/TealWallet/TealWallet.exe\",\"version\":\"1.10.0\",\"legacyIds\":[\"TealSub\"]}]}";
        var pkg = U.UpdatePackage.Parse(manifest);
        var merged = U.UpdateEngine.MergeAppsJson(U.Json.ParseObj(installed), pkg);
        var apps = merged.Arr("apps").Cast<U.JObj>().ToList();
        Check(string.Join(",", apps.Select(a => a.Str("id"))) == "TealHub,TealPlan,TealWallet", "apps.json: Teal Tasks の場所に Teal Plan (増えない): " + string.Join(",", apps.Select(a => a.Str("id"))));
        var plan = apps.First(a => a.Str("id") == "TealPlan");
        Check(plan.Str("exe") == "apps/TealPlan/TealPlan.exe" && plan.Str("version") == "1.10.0", "Teal Plan の項目");

        // 実際の dist の apps.json とアイコン
        var aj = U.Json.ParseObj(File.ReadAllText(Path.Combine(distDir, "apps.json")));
        var dapps = aj.Arr("apps").Cast<U.JObj>().ToList();
        var dp = dapps.FirstOrDefault(a => a.Str("id") == "TealPlan");
        Check(dp != null && !dapps.Any(a => a.Str("id") == "TealTasks") && dp.Strs("legacyIds").Contains("TealTasks") && U.VersionUtil.Compare(aj.Str("suiteVersion"), "1.10.0") >= 0, "dist の apps.json: TealPlan (legacyIds に TealTasks)");
        Check(File.Exists(Path.Combine(distDir, "apps", "TealPlan", "TealPlan.exe")) && File.Exists(Path.Combine(distDir, "apps", "TealPlan", "TealPlan.ico")) && !Directory.Exists(Path.Combine(distDir, "apps", "TealTasks")), "dist: TealPlan.exe と新しいアイコン (TealTasks フォルダはない)");

        // 古い exe を消す指示 (tools/retired-files.txt)
        var dir = new DirectoryInfo(distDir);
        string retired = null;
        for (var x = dir; x != null && retired == null; x = x.Parent)
        {
            var p = Path.Combine(x.FullName, "tools", "retired-files.txt");
            if (File.Exists(p)) retired = p;
        }
        if (retired != null)
        {
            var lines = File.ReadAllLines(retired).Select(l => l.Trim()).ToList();
            Check(lines.Contains("apps/TealTasks/TealTasks.exe") && lines.Contains("apps/TealTasks/TealTasks.ico") && lines.Contains("apps/TealSub/TealSub.exe"), "retired-files.txt: TealTasks.exe・TealTasks.ico を消す (TealSub.exe も残す)");
        }
    }
}
