// Teal Wallet (サブスク＆買い物リスト) と Teal Tasks (タスク管理＆カレンダー) のロジックテスト
extern alias SUB;
extern alias TASKS;
using System;
using System.Collections.Generic;
using System.Linq;
using S = SUB::TealSuite.Wallet;
using T = TASKS::TealSuite.Tasks;

static class SubTasksTests
{
    static DateTime D(int y, int m, int d) { return new DateTime(y, m, d); }

    static S.Subscription Sub(string name, long amount, S.Cycle c, DateTime start, int months = 0)
    {
        var s = new S.Subscription { Name = name, Amount = amount, CycleMonths = months, Category = "動画" };
        s.Cycle = c; s.StartDate = start;
        return s;
    }

    // ---------------------------------------------------------------- Teal Wallet (サブスク)
    public static void RunSub(Action<bool, string> Check)
    {
        SUB::TealSuite.Common.AppInfo.Init("TealWallet", "Teal Wallet", "");
        var today = D(2026, 10, 27);
        // 更新日
        var m = Sub("月払い", 1000, S.Cycle.Monthly, D(2026, 1, 15));
        Check(S.SubLogic.NextRenewal(m, today) == D(2026, 11, 15), "毎月: 1/15 開始 → 次は 11/15");
        Check(S.SubLogic.NextRenewal(m, D(2026, 11, 15)) == D(2026, 11, 15), "毎月: 支払い日の当日はその日");
        var eom = Sub("月末", 500, S.Cycle.Monthly, D(2026, 1, 31));
        Check(S.SubLogic.NextRenewal(eom, D(2026, 2, 1)) == D(2026, 2, 28) && S.SubLogic.NextRenewal(eom, D(2026, 3, 1)) == D(2026, 3, 31), "月末の契約: 2/28 → 3/31 (日がずれない)");
        var y = Sub("年払い", 12000, S.Cycle.Yearly, D(2024, 2, 29));
        Check(S.SubLogic.NextRenewal(y, today) == D(2027, 2, 28), "毎年: うるう日 2/29 開始 → 2027/2/28");
        var w = Sub("週払い", 300, S.Cycle.Weekly, D(2026, 10, 1));
        Check(S.SubLogic.NextRenewal(w, today) == D(2026, 10, 29), "毎週: 10/1(木) 開始 → 10/29(木)");
        var n3 = Sub("3か月", 3000, S.Cycle.EveryNMonths, D(2026, 1, 10), 3);
        Check(S.SubLogic.NextRenewal(n3, today) == D(2027, 1, 10), "3 か月ごと: 1/10 → 4/10 → 7/10 → 10/10 → 次は 1/10");
        var trial = Sub("無料期間", 980, S.Cycle.Monthly, D(2026, 10, 20));
        trial.TrialEndDate = D(2026, 11, 3);
        Check(S.SubLogic.NextRenewal(trial, today) == D(2026, 11, 3) && S.SubLogic.InTrial(trial, today), "無料期間の終わりが最初の支払い日");
        var future = Sub("これから", 100, S.Cycle.Monthly, D(2026, 12, 1));
        Check(S.SubLogic.NextRenewal(future, today) == D(2026, 12, 1), "開始日が先なら開始日");
        var cancelled = Sub("解約済み", 2000, S.Cycle.Monthly, D(2025, 1, 1));
        cancelled.Status = S.SubStatus.Cancelled; cancelled.CancelledDate = D(2026, 7, 27);
        Check(S.SubLogic.NextRenewal(cancelled, today) == null, "解約済みは次の支払いなし");

        // 金額
        var list = new List<S.Subscription> { m, y, w, n3, cancelled };
        Check(S.SubLogic.MonthlyCost(y) == 1000m && S.SubLogic.MonthlyCost(w) == 1300m && S.SubLogic.MonthlyCost(n3) == 1000m, "月あたり: 年 12,000→1,000 / 週 300→1,300 / 3か月 3,000→1,000");
        Check(S.SubLogic.YearlyCost(m) == 12000m && S.SubLogic.YearlyCost(w) == 15600m && S.SubLogic.YearlyCost(n3) == 12000m, "1 年あたり: 月 1,000→12,000 / 週 300→15,600 / 3か月→12,000");
        Check(S.SubLogic.MonthlyTotal(list) == 4300m && S.SubLogic.YearlyTotal(list) == 51600m, "合計は解約済みを除く (月 ¥4,300 / 年 ¥51,600)");
        Check(Math.Abs(S.SubLogic.SavedSoFar(cancelled, today) - 6000m) < 100m, "解約して 3 か月で約 ¥6,000 浮いた (" + S.SubLogic.SavedSoFar(cancelled, today) + ")");
        var cats = S.SubLogic.ByCategory(list);
        Check(cats.Count == 1 && cats[0].Monthly == 4300m && cats[0].Count == 4, "カテゴリ別の合計");

        // 見直しの点数
        var rare = Sub("ほぼ使わない", 1990, S.Cycle.Monthly, D(2025, 5, 1));
        rare.Usage = S.Usage.Rarely; rare.LastUsedDate = today.AddDays(-100); rare.Category = "音楽";
        var daily = Sub("毎日使う", 980, S.Cycle.Monthly, D(2025, 5, 1));
        daily.Usage = S.Usage.Daily; daily.LastUsedDate = today; daily.Category = "音楽";
        var all = new List<S.Subscription> { rare, daily };
        var r1 = S.SubLogic.Review(rare, all, today);
        var r2 = S.SubLogic.Review(daily, all, today);
        Check(r1.Score >= S.SubLogic.StrongScore && r1.Reasons.Count >= 3, "ほぼ使わない・100 日未使用・高い・重複 → 強くおすすめ (" + r1.Score + " 点: " + string.Join(" / ", r1.Reasons) + ")");
        Check(r2.Score == 0 && r2.Reasons.Count == 0, "毎日使っているものは 0 点");
        Check(r1.Reasons.Any(x => x.Contains("毎日使う")), "重複は、よく使うほうにまとめる案を出す");
        Check(r1.Score <= 100, "点数は 100 まで");
        var cand = S.SubLogic.Candidates(all, today);
        Check(cand.Count == 1 && cand[0].Sub == rare && S.SubLogic.PossibleSaving(cand) == 23880m, "見直し候補は 1 件、浮く金額 ¥23,880/年");
        var t2 = Sub("無料期間がもうすぐ", 1500, S.Cycle.Monthly, D(2026, 10, 1));
        t2.TrialEndDate = today.AddDays(2); t2.Category = "その他";
        Check(S.SubLogic.Review(t2, new List<S.Subscription> { t2 }, today).Reasons.Any(x => x.Contains("無料期間")), "無料期間がまもなく終わるものは理由に出る");
        rare.Status = S.SubStatus.Cancelling;
        Check(S.SubLogic.Candidates(all, today).Count == 0, "解約予定にしたものは候補から外れる");

        // お知らせ
        var data = new S.SubData();
        var soon = Sub("もうすぐ更新", 500, S.Cycle.Monthly, D(2026, 5, 29));
        var later = Sub("まだ先", 500, S.Cycle.Monthly, D(2026, 5, 10));
        data.Subs.Add(soon); data.Subs.Add(later); data.Subs.Add(trial);
        var notes = S.SubLogic.DueNotices(data, today);
        Check(notes.Count == 1 && notes[0].SubId == soon.Id, "3 日前からお知らせ (10/29 の更新だけ)");
        foreach (var x in notes) data.Notified.Add(x.Key);
        Check(S.SubLogic.DueNotices(data, today).Count == 0, "同じお知らせは 2 回出さない");
        Check(S.SubLogic.DueNotices(data, D(2026, 10, 31)).Any(x => x.SubId == trial.Id && x.Title.Contains("無料期間")), "無料期間の終わりもお知らせ");
        data.Settings.Remind = false;
        Check(S.SubLogic.DueNotices(data, D(2026, 10, 31)).Count == 0, "お知らせオフなら出さない");

        // CSV と URL
        var csv = S.SubLogic.ToCsv(new[] { m }, today);
        Check(csv.StartsWith("名前,") && csv.Contains("月払い,1000"), "CSV に書き出す");
        Check(S.SubLogic.Csv("a,b\"c") == "\"a,b\"\"c\"", "CSV のエスケープ");
        Check(S.SubLogic.IsOpenableUrl("https://example.com/cancel") && !S.SubLogic.IsOpenableUrl("javascript:alert(1)") && !S.SubLogic.IsOpenableUrl("file:///C:/x"), "解約ページは http/https だけ開く");
        Check(S.SubLogic.SampleData(today).Count >= 8, "見本データ");
    }

    // ---------------------------------------------------------------- Teal Tasks
    static T.TaskItem Task(string title, DateTime? due, bool withTime, int pri = T.TaskItem.Mid, int min = 0)
    {
        var t = new T.TaskItem { Title = title, Priority = pri, DurationMin = min };
        t.SetDue(due, withTime);
        return t;
    }

    public static void RunTasks(Action<bool, string> Check)
    {
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "");
        var now = new DateTime(2026, 10, 27, 10, 0, 0); // 火曜 10:00

        // ひとこと入力
        Func<string, T.ParseResult> P = s => T.QuickParser.Parse(s, now);
        var a = P("明日15時 歯医者 #病院");
        Check(a.Title == "歯医者" && a.Due == new DateTime(2026, 10, 28, 15, 0, 0) && a.Tags.SequenceEqual(new[] { "病院" }), "「明日15時 歯医者 #病院」→ 歯医者 / 10/28 15:00 / #病院");
        var b = P("金曜までにレポート提出 重要 2時間");
        Check(b.Title == "レポート提出" && b.Date == new DateTime(2026, 10, 30) && !b.HasTime && b.Priority == T.TaskItem.High && b.DurationMin == 120, "「金曜までに… 重要 2時間」→ 10/30・高・2時間 (" + b.Describe(now) + ")");
        var c = P("来週月曜 9:30 定例会議 #仕事");
        Check(c.Title == "定例会議" && c.Due == new DateTime(2026, 11, 2, 9, 30, 0), "「来週月曜 9:30」→ 11/2 9:30");
        var d = P("毎週水曜 ジム 1時間半");
        Check(d.Title == "ジム" && d.Repeat == T.Repeat.Weekly && d.Date == new DateTime(2026, 10, 28) && d.DurationMin == 90, "「毎週水曜 ジム 1時間半」→ 毎週・10/28・90分");
        var e = P("3時に電話");
        Check(e.Title == "電話" && e.Due == new DateTime(2026, 10, 27, 15, 0, 0), "「3時に電話」→ 今日 15:00 (1〜6 時は午後とみなす)");
        var f = P("8時 朝会");
        Check(f.Due == new DateTime(2026, 10, 28, 8, 0, 0), "過ぎた時刻だけなら明日 (8時 → 10/28 8:00)");
        var g = P("11月3日 午後2時半 打ち合わせ");
        Check(g.Title == "打ち合わせ" && g.Due == new DateTime(2026, 11, 3, 14, 30, 0), "「11月3日 午後2時半」→ 11/3 14:30");
        var h = P("３日後 ＃家 掃除");
        Check(h.Title == "掃除" && h.Date == new DateTime(2026, 10, 30) && h.Tags.Contains("家"), "全角 (３日後 ＃家) も読める");
        var i = P("はさみを買う");
        Check(i.Title == "はさみを買う" && !i.Date.HasValue && i.Priority == null, "日付などが無ければそのまま (「はさみを買う」)");
        var j = P("10/1 健康診断");
        Check(j.Date == new DateTime(2027, 10, 1) || j.Date == new DateTime(2026, 10, 1), "M/D の日付");
        Check(P("10/1 健康診断").Date == new DateTime(2027, 10, 1), "1 週間以上前の M/D は来年 (10/1 → 2027/10/1)");
        Check(P("10/25 振り返り").Date == new DateTime(2026, 10, 25), "数日前の M/D は今年のまま (期限切れとして入る)");
        var k = P("優先度低 部屋の片付け");
        Check(k.Title == "部屋の片付け" && k.Priority == T.TaskItem.Low, "「優先度低」");
        var task = P("明日の会議の準備 30分 急ぎ").ToTask(now);
        Check(task.Title == "会議の準備" && task.Due == "2026-10-28" && task.Priority == T.TaskItem.High && task.DurationMin == 30, "ToTask で TaskItem に (期限は日付だけ)");
        Check(P("").Title == "" && P("   ").Title == "", "空の入力");

        // 優先度の点数
        Func<T.TaskItem, int> Sc = t => T.TaskLogic.Score(t, now).Score;
        var over = Task("期限切れ", now.AddDays(-2), false, T.TaskItem.Low);
        var todayHigh = Task("今日・高", now.Date, false, T.TaskItem.High);
        var todaySoon = Task("2時間後", now.AddHours(2), true, T.TaskItem.Mid);
        var tomorrow = Task("明日", now.AddDays(1), false, T.TaskItem.Mid);
        var nextWeek = Task("来週", now.AddDays(6), false, T.TaskItem.Low);
        var none = Task("期限なし", null, false, T.TaskItem.Low);
        Check(Sc(over) == 110, "期限切れ 2 日 = 100 + 10 (" + Sc(over) + ")");
        Check(Sc(todayHigh) == 90, "今日・優先度高 = 60 + 30 (" + Sc(todayHigh) + ")");
        Check(Sc(todaySoon) == 90, "今日 3 時間以内・中 = 60 + 15 + 15 (" + Sc(todaySoon) + ")");
        Check(Sc(tomorrow) == 50 && Sc(nextWeek) == 10 && Sc(none) == 0, "明日・中 = 50 / 6 日後・低 = 10 / 期限なし・低 = 0");
        Check(Sc(Task("すぐ終わる", null, false, T.TaskItem.Low, 15)) == 10 && Sc(Task("30分", null, false, T.TaskItem.Low, 30)) == 5, "15 分以内 +10 / 30 分以内 +5");
        Check(Sc(Task("ずっと前", now.AddDays(-30), false, T.TaskItem.Low)) == 130, "期限切れの上乗せは +30 まで");
        Check(T.TaskLogic.Score(over, now).Reasons.Count > 0, "点数の理由を日本語で出す");
        var done = Task("完了", now.Date, false); done.Done = true; done.DoneAt = "2026-10-27T09:00";
        var all = new List<T.TaskItem> { none, nextWeek, tomorrow, todaySoon, todayHigh, over, done };
        var tl = T.TaskLogic.TodayList(all, now);
        Check(tl.Count == 3 && tl[0] == over && !tl.Contains(done) && !tl.Contains(tomorrow), "今日のリスト: 期限切れ＋今日、点数の高い順");
        Check(T.TaskLogic.DoneToday(all, now).Single() == done, "今日終わらせたもの");
        Check(T.TaskLogic.IsOverdue(over, now) && !T.TaskLogic.IsOverdue(todayHigh, now) && T.TaskLogic.IsOverdue(Task("9時", now.Date.AddHours(9), true), now), "期限切れの判定 (日付だけなら翌日から・時刻つきはその時刻から)");
        Check(T.TaskLogic.WeekStart(new DateTime(2026, 11, 1)) == new DateTime(2026, 10, 26), "週は月曜はじまり (11/1 日曜 → 10/26 月曜)");

        // くり返し
        Check(T.TaskLogic.NextDue(new DateTime(2026, 1, 31), T.Repeat.Monthly, new DateTime(2026, 1, 31)) == new DateTime(2026, 2, 28), "毎月 1/31 → 2/28");
        Check(T.TaskLogic.NextDue(new DateTime(2026, 1, 31), T.Repeat.Monthly, new DateTime(2026, 2, 28)) == new DateTime(2026, 3, 31), "毎月 1/31 → 3/31 (元の日に戻る)");
        Check(T.TaskLogic.NextDue(new DateTime(2026, 10, 20, 7, 0, 0), T.Repeat.Weekly, now) == new DateTime(2026, 11, 3, 7, 0, 0), "毎週: たまった分は飛ばして今日より後へ");
        var data = new T.TaskData();
        var rep = Task("ゴミ出し", now.Date.AddHours(8), true); rep.Repeat = T.Repeat.Daily;
        rep.Subtasks.Add(new T.SubTask { Text = "分別", Done = true });
        data.Tasks.Add(rep);
        var next = T.TaskLogic.Complete(data, rep, now);
        Check(rep.Done && next != null && next.Due == "2026-10-28T08:00" && !next.Done && next.Subtasks.All(s => !s.Done) && data.Tasks.Count == 2, "毎日のタスクを完了 → 明日 8:00 の回を作る (小タスクは未完了に)");
        T.TaskLogic.Uncomplete(data, rep);
        Check(!rep.Done && data.Tasks.Count == 1, "完了を取り消すと、作った次の回を消す");
        T.TaskLogic.Toggle(data, rep, now);
        Check(rep.Done && data.Tasks.Count == 2, "Toggle で完了");

        // お知らせ
        var nd = new T.TaskData();
        var meet = Task("会議", now.AddMinutes(10), true);
        var later = Task("あとで", now.AddHours(3), true);
        nd.Tasks.Add(meet); nd.Tasks.Add(later); nd.Tasks.Add(Task("今日やる", now.Date, false));
        var ns = T.TaskLogic.DueNotices(nd, now);
        Check(ns.Any(x => x.Key.StartsWith("morning|")) && ns.Any(x => x.Task == meet) && !ns.Any(x => x.Task == later), "朝のまとめ＋15 分前のお知らせ (3 時間後はまだ)");
        foreach (var x in ns) nd.Notified.Add(x.Key);
        Check(T.TaskLogic.DueNotices(nd, now.AddMinutes(1)).Count == 0, "同じお知らせは 2 回出さない");
        meet.SetDue(now.AddMinutes(12), true);
        Check(T.TaskLogic.DueNotices(nd, now.AddMinutes(1)).Count == 1, "期限を変えたらもう一度お知らせ");
        T.TaskLogic.PruneNotified(nd, now);
        Check(nd.Notified.Count == 1 && nd.Notified[0].StartsWith("morning|"), "古い印 (変わった期限) は消す");
        nd.Settings.Remind = false;
        Check(T.TaskLogic.DueNotices(nd, now.AddHours(3)).Count == 0, "お知らせオフなら出さない");

        // 統計
        var st = T.TaskLogic.Compute(all, now);
        Check(st.Open == 6 && st.Overdue == 1 && st.DoneToday == 1 && st.Daily.Count == 14 && st.Daily.Last().Value == 1, "統計: 残り 6 / 期限切れ 1 / 今日 1 件 / 14 日分");
        var tags = T.TaskLogic.ParseTags("#仕事 家, ＃仕事  お金");
        Check(tags.SequenceEqual(new[] { "仕事", "家", "お金" }), "タグの読み取り (重複・全角 # をまとめる)");
        Check(T.TaskLogic.ToCsv(all).Length > 0, "CSV");
        var sample = T.TaskLogic.SampleData(now);
        Check(sample.Count >= 15 && sample.Any(x => x.Repeat != T.Repeat.None) && sample.Any(x => x.Done), "見本データ");

        // データの保存と読み込み
        TASKS::TealSuite.Common.DataStore.Save(T.TaskData.FileName, data);
        string warn;
        var back = TASKS::TealSuite.Common.DataStore.LoadOrDefault(T.TaskData.FileName, () => new T.TaskData(), out warn);
        back.Normalize();
        Check(warn == null && back.Tasks.Count == 2 && back.Tasks[0].Repeat == T.Repeat.Daily && back.Tasks[0].Subtasks.Count == 1, "tasks.json に保存して読み戻す");
    }

    /// <summary>画面を作ってひとこと入力から追加・完了・元に戻す (Xvfb 上)</summary>
    public static void RunTasksUi(Action<bool, string> Check)
    {
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "");
        var now = new DateTime(2026, 10, 27, 10, 0, 0);
        using (var f = new T.MainForm())
        {
            f.Clock = () => now;
            f.Data = new T.TaskData();
            f.RefreshAll();
            f.Quick.Text = "明日15時 歯医者 #病院 重要";
            var t = f.QuickAdd();
            Check(t != null && f.Data.Tasks.Count == 1 && t.Due == "2026-10-28T15:00" && t.Priority == T.TaskItem.High && f.Quick.Text == "", "ひとこと入力で追加して入力欄を空に");
            f.Quick.Text = "#だけ";
            Check(f.QuickAdd() == null || f.Data.Tasks.Count == 2, "名前だけでないときも落ちない");
            f.Data.Tasks.RemoveAll(x => x != t);
            f.Toggle(t);
            Check(t.Done, "完了にする");
            f.Undo();
            Check(f.Data.Tasks.Count == 1 && !f.Data.Tasks[0].Done, "Ctrl+Z (元に戻す) で完了前に戻る");
            f.InsertSamples();
            Check(f.Data.Tasks.Count > 10 && f.TodayList.Items.Count > 0 && f.List.Items.Count > 0, "見本データで今日・一覧に表示");
            f.Tabs.SelectedIndex = 1;
            f.Quick.Text = "資料作り";
            var p = f.ParseQuick();
            Check(p.Date == f.Calendar.Day, "カレンダー表示中は、選んでいる日を期限にする");
        }
        using (var s = new S.MainForm())
        {
            SUB::TealSuite.Common.AppInfo.Init("TealWallet", "Teal Wallet", "");
            s.Data = new S.SubData();
            s.InsertSamples();
            Check(s.Data.Subs.Count >= 8 && s.List.Items.Count > 0, "Teal Wallet: 見本データを入れて一覧に表示");
        }
    }

    public static void Seed(string dir)
    {
        SUB::TealSuite.Common.AppInfo.Init("TealWallet", "Teal Wallet", "");
        var sd = new S.SubData();
        sd.Subs.AddRange(S.SubLogic.SampleData(DateTime.Today));
        sd.Settings.MonthlyBudget = 18000; // 1.8.0: 月の予算 (支払い予定の画面に出る)
        SUB::TealSuite.Common.DataStore.Save(S.SubData.FileName, sd);
        // 1.9.0: 買い物リスト・欲しいもの (月の予算 40,000 円)
        SUB::TealSuite.Common.DataStore.Save(S.ShopData.FileName, S.ShopLogic.Sample(DateTime.Today));
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "");
        var td = new T.TaskData();
        td.Tasks.AddRange(T.TaskLogic.SampleData(DateTime.Now));
        // 1.10.0: 予定の見本と、小タスクのあるタスクを開いた状態
        td.Events.AddRange(T.EventLogic.SampleData(DateTime.Now));
        foreach (var t in td.Tasks.Where(x => x.Subtasks.Count > 0 && !x.Done).Take(2)) td.SetExpanded(t, true);
        // 1.8.0: テンプレートの見本
        T.TaskTools.SaveTemplate(td, new T.TaskTemplate { Name = "週次レポート", Task = new T.TaskItem { Title = "週次レポートを送る", Priority = T.TaskItem.High, DurationMin = 30 }, DueDays = 4, Time = "17:00" });
        T.TaskTools.SaveTemplate(td, new T.TaskTemplate { Name = "買い出し", Task = new T.TaskItem { Title = "スーパーで買い出し", DurationMin = 45 }, DueDays = 0 });
        TASKS::TealSuite.Common.DataStore.Save(T.TaskData.FileName, td);
    }
}
