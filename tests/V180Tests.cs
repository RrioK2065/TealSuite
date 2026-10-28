// 1.8.0 で追加した機能のテスト (メモ・パスワード・クリップボード・音量・Hub・サブスク・タスク)
extern alias PM;
extern alias AM;
extern alias MEMO;
extern alias CH;
extern alias VM;
extern alias SUB;
extern alias TASKS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mm = MEMO::TealSuite.Memo;
using Pp = PM::TealSuite.Pass;
using Cc = CH::TealSuite.Clip;
using V = VM::TealSuite.Volume;
using H = AM::TealSuite.Hub;
using S = SUB::TealSuite.Wallet;
using T = TASKS::TealSuite.Tasks;

static class V180Tests
{
    static DateTime D(int y, int m, int d) { return new DateTime(y, m, d); }

    // ---------------------------------------------------------------- 共通・メモ
    public static void RunMemo(Action<bool, string> Check)
    {
        MEMO::TealSuite.Common.AppInfo.Init("TealMemo", "Teal Memo", "");
        var csv = MEMO::TealSuite.Common.TextKit.ParseCsv("a,b,c\r\n\"x,1\",\"he said \"\"hi\"\"\",\"line1\nline2\"\r\n");
        Check(csv.Count == 2 && csv[1][0] == "x,1" && csv[1][1] == "he said \"hi\"" && csv[1][2] == "line1\nline2", "CSV: カンマ・引用符・改行を含むセル");
        Check(MEMO::TealSuite.Common.TextKit.Column(csv[0], "B") == 1 && MEMO::TealSuite.Common.TextKit.Column(csv[0], "zz") < 0, "CSV: 見出しを大文字小文字を区別せず探す");
        var sjis = Encoding.GetEncoding(932).GetBytes("日本語のメモ");
        Check(MEMO::TealSuite.Common.TextKit.Decode(sjis) == "日本語のメモ", "Shift_JIS のファイルも読める");
        Check(MEMO::TealSuite.Common.TextKit.Decode(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("BOMつき")).ToArray()) == "BOMつき", "UTF-8 (BOM つき) も読める");

        Check(Mm.NoteStore.TagsOf("買い物 #家 #仕事 と #家").SequenceEqual(new[] { "家", "仕事" }), "本文の #タグ を重複なしで拾う");
        var a = Mm.Note.Create(); a.Text = "A #家"; a.Updated = "2026-10-01T00:00:00.0000000Z";
        var b = Mm.Note.Create(); b.Text = "B #仕事"; b.Pinned = true; b.Updated = "2026-09-01T00:00:00.0000000Z";
        var c = Mm.Note.Create(); c.Text = "C"; 
        var now = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
        Mm.NoteStore.Trash(c, now.AddDays(-31));
        var notes = new List<Mm.Note> { a, b, c };
        var all = Mm.NoteStore.View(notes, "", "all");
        Check(all.Count == 2 && all[0] == b, "ピン留めは一番上、ゴミ箱のメモは出さない");
        Check(Mm.NoteStore.View(notes, "", "pinned").Single() == b && Mm.NoteStore.View(notes, "", "#家").Single() == a, "ピン留めだけ・タグで絞り込み");
        Check(Mm.NoteStore.View(notes, "", "trash").Single() == c, "ゴミ箱の表示");
        Check(Mm.NoteStore.AllTags(notes.Where(n => !n.InTrash)).Count == 2, "タグの一覧");
        var f = new Mm.NoteFile(); f.Notes.AddRange(notes);
        Check(Mm.NoteStore.PurgeTrash(f, now) == 1 && f.Notes.Count == 2, "ゴミ箱で 30 日たったメモは消える");
        Mm.NoteStore.Trash(a, now.AddDays(-10));
        Check(Mm.NoteStore.DaysLeft(a, now) == 20, "ゴミ箱: あと何日で消えるか");
        Mm.NoteStore.Restore(a);
        Check(!a.InTrash, "ゴミ箱から戻す");
        var imp = Mm.NoteStore.FromText("一行目\r\n二行目\n", now);
        Check(imp.Text.StartsWith("一行目") && Mm.NoteStore.ToText(imp).Contains("\r\n"), ".txt を読み込み・書き出し (メモ帳で開ける CRLF)");
        var old = MEMO::TealSuite.Common.DataStore.FromJson<Mm.Note>(Encoding.UTF8.GetBytes("{\"id\":\"x\",\"text\":\"t\",\"created\":\"c\",\"updated\":\"u\"}"));
        Check(old != null && !old.Pinned && !old.InTrash, "1.7.0 のメモをそのまま読める");
        var st = MEMO::TealSuite.Common.DataStore.FromJson<Mm.MemoSettings>(Encoding.UTF8.GetBytes("{}"));
        Check(st != null && st.SafeSize >= 0 && st.SafeSize < Mm.MemoSettings.SizeScale.Length, "文字の大きさの設定 (無ければ標準)");
    }

    // ---------------------------------------------------------------- パスワード
    public static void RunPass(Action<bool, string> Check)
    {
        PM::TealSuite.Common.AppInfo.Init("TealPass", "Teal Pass", "");
        var now = new DateTime(2026, 10, 28, 12, 0, 0);
        var before = new Pp.VaultEntry { Site = "Example", Password = "old-Pass1!", Created = now.AddDays(-400) };
        var edited = before.Clone(); edited.Password = "New-Pass-2026!x";
        Pp.PassTools.ApplyEdit(before, edited, now);
        Check(edited.HistoryCount == 1 && edited.History[0].Password == "old-Pass1!" && edited.PasswordChanged == now, "パスワードを変えると前のものを記録 (変更日も更新)");
        for (int i = 0; i < 8; i++) { var b2 = edited.Clone(); edited.Password = "P" + i + "-aaaaaaaaaa!"; Pp.PassTools.ApplyEdit(b2, edited, now); }
        Check(edited.HistoryCount == Pp.PassTools.HistoryMax, "記録は 5 件まで");
        var same1 = new Pp.VaultEntry { Site = "A", Password = "Same-Pass-123!", Created = now };
        var same2 = new Pp.VaultEntry { Site = "B", Password = "Same-Pass-123!", Created = now };
        var weak = new Pp.VaultEntry { Site = "C", Password = "1234", Created = now };
        var old = new Pp.VaultEntry { Site = "D", Password = "Very-Strong-Pass-99!", Created = now.AddDays(-500) };
        var good = new Pp.VaultEntry { Site = "E", Password = "Another-Strong-7#x", Created = now };
        var rep = Pp.PassTools.Check(new[] { same1, same2, weak, old, good }, now);
        Check(rep.Reused == 2 && rep.Weak >= 1 && rep.Old == 1 && rep.Of(good) == Pp.PassIssue.None, "パスワードの点検: 使い回し・弱い・古い");
        Check(Pp.PassTools.SamePassword(new[] { same1, same2, good }, same1).Single() == same2, "同じパスワードを使っている登録");
        Check(Pp.PassTools.SiteFromUrl("https://www.example.com/login") == "example.com", "URL からサイト名");
        var chrome = "name,url,username,password,note\r\nGitHub,https://github.com/,me,pw1,\r\n,https://www.x.com/,u,pw2,メモ\r\nGitHub,https://github.com/,me,pw1,\r\n";
        var r = Pp.PassTools.ImportCsv(chrome, new Pp.VaultEntry[0], now);
        Check(r.Error == null && r.Added.Count == 2 && r.Duplicates == 1 && r.Added[1].Site == "x.com", "Chrome の CSV を読み込み (同じものは 1 回だけ)");
        var bw = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\r\n,1,login,Bank,,,0,https://bank.example,me,secret,\r\n";
        var r2 = Pp.PassTools.ImportCsv(bw, r.Added, now);
        Check(r2.Format == "Bitwarden" && r2.Added.Single().Favorite && r2.Added[0].Password == "secret", "Bitwarden の CSV (お気に入りも)");
        Check(Pp.PassTools.ImportCsv("a,b\r\n1,2\r\n", new Pp.VaultEntry[0], now).Error != null, "パスワードの CSV でなければエラー");
        var back = Pp.PassTools.ImportCsv(Pp.PassTools.ToCsv(r.Added), new Pp.VaultEntry[0], now);
        Check(back.Added.Count == 2 && back.Added.Any(e => e.Site == "GitHub" && e.Password == "pw1"), "書き出した CSV をそのまま読み戻せる");
        var oldJson = PM::TealSuite.Common.DataStore.FromJson<Pp.VaultEntry>(Encoding.UTF8.GetBytes("{\"id\":\"1\",\"site\":\"s\",\"password\":\"p\"}"));
        Check(oldJson != null && !oldJson.Favorite && oldJson.HistoryCount == 0, "1.7.0 の登録をそのまま読める");
    }

    // ---------------------------------------------------------------- クリップボード
    public static void RunClip(Action<bool, string> Check)
    {
        CH::TealSuite.Common.AppInfo.Init("TealClip", "Teal Clip", "");
        var now = new DateTime(2026, 10, 28, 12, 0, 0);
        var h = new Cc.History(new Cc.ClipItem[0], 30);
        Check(h.Capacity == 30 && h.PinLimit == 29, "履歴の数を 30 件に");
        for (int i = 0; i < 35; i++) h.Add("item" + i, now.AddMinutes(-i));
        Check(h.Items.Count == 30, "30 件を超えたら古いものから消える");
        Check(Cc.History.ClampCapacity(5) == 10 && Cc.History.ClampCapacity(999) == 50, "履歴の数は 10〜50");
        h.SetCapacity(10);
        Check(h.Items.Count == 10, "履歴の数を減らす");
        var old = h.Items.Last();
        var removed = h.PurgeOlderThan(1, now.AddDays(2));
        Check(removed > 0 && !h.Items.Contains(old), "古い履歴を自動で消す");
        var sn = h.AddSnippet("いつもお世話になっております。");
        Check(sn != null && sn.Pinned && h.Items.Contains(sn), "定型文を追加 (ピン留め)");
        Check(h.UpdateText(sn, "お世話になっております。") && sn.Text == "お世話になっております。", "定型文の編集");
        h.Add("Hello World"); h.Add("hello teal");
        Check(h.Search("HELLO").Count == 2 && h.Search("hello world").Count == 1, "検索 (大文字小文字を区別しない・空白区切りは全部含む)");
        var st = new Cc.ChSettings { ExcludeWords = new List<string> { "秘密", " ", "秘密" }, SkipPasswordLike = true };
        Check(st.Words.Count == 1, "除外キーワードは空白・重複を除く");
        Check(Cc.ClipFilter.SkipReason("これは秘密です", st) != null && Cc.ClipFilter.SkipReason("ふつうの文", st) == null, "除外キーワードを含むものは記録しない");
        Check(Cc.ClipFilter.LooksLikePassword("Abc123!xyz") && !Cc.ClipFilter.LooksLikePassword("hello world") && !Cc.ClipFilter.LooksLikePassword("https://example.com/Ab1") && !Cc.ClipFilter.LooksLikePassword("Me.Name1@example.com"), "パスワードらしい文字の判定 (URL・メール・文章は除く)");
        Check(Cc.ClipFilter.SkipReason("Abc123!xyz", st) != null && Cc.ClipFilter.SkipReason("Abc123!xyz", new Cc.ChSettings()) == null, "パスワードらしい文字はオンのときだけ飛ばす");
        var oldSt = CH::TealSuite.Common.DataStore.FromJson<Cc.ChSettings>(Encoding.UTF8.GetBytes("{\"hotkey\":\"x\",\"firstRunTipShown\":true}"));
        Check(oldSt != null && oldSt.Words.Count == 0 && !oldSt.SkipPasswordLike && oldSt.AutoDeleteDays == 0, "1.7.0 の設定をそのまま読める");
    }

    // ---------------------------------------------------------------- 音量
    public static void RunVolume(Action<bool, string> Check)
    {
        var demo = new V.DemoBackend();
        var apps = demo.Refresh();
        var chrome = apps.First(a => a.Key == "chrome");
        demo.SetVolume(chrome, 0.30f);
        var p = V.VolumeLogic.Capture("作業中", demo, apps, true);
        Check(p.Name == "作業中" && p.Apps.Any(x => x.Key == "chrome" && x.Volume == 30) && (p.Master >= 0 || !demo.HasMaster), "今の音量をプリセットに保存");
        demo.SetVolume(chrome, 0.90f); demo.SetMute(chrome, true);
        int n = V.VolumeLogic.Apply(p, demo, apps);
        Check(n >= 1 && Math.Abs(chrome.Volume - 0.30f) < 0.01f && !chrome.Muted, "プリセットを当てると元の音量に戻る");
        var list = new List<V.VolumePreset>();
        for (int i = 0; i < V.VolumeLogic.MaxPresets; i++) V.VolumeLogic.Save(list, new V.VolumePreset { Name = "p" + i });
        Check(!V.VolumeLogic.Save(list, new V.VolumePreset { Name = "あふれ" }) && V.VolumeLogic.Save(list, new V.VolumePreset { Name = "P0" }) && list.Count == V.VolumeLogic.MaxPresets, "プリセットは 12 個まで (同じ名前は上書き)");
        Check(!V.VolumeLogic.IsStable("pid:123") && V.VolumeLogic.IsStable("chrome"), "プロセス番号だけのアプリは保存しない");
        Check(V.VolumeLogic.ToggleMuteAll(demo, apps) && V.VolumeLogic.AllMuted(apps), "すべてミュート");
        Check(!V.VolumeLogic.ToggleMuteAll(demo, apps) && apps.All(a => !a.Muted), "もう一度で全部解除");
        var pinned = new List<string> { "spotify" }; var hidden = new List<string> { "chrome" };
        var arr = V.VolumeLogic.Arrange(apps, pinned, hidden, false);
        Check(arr[0].Key == "spotify" && !arr.Any(a => a.Key == "chrome"), "固定したアプリは上、隠したアプリは出さない");
        Check(V.VolumeLogic.Arrange(apps, pinned, hidden, true).Any(a => a.Key == "chrome"), "「隠したアプリも表示」");
        Check(V.VolumeLogic.Toggle(pinned, "Chrome") && !V.VolumeLogic.Toggle(pinned, "CHROME") && pinned.Count == 1, "固定の付け外し");
        var oldSt = VM::TealSuite.Common.DataStore.FromJson<V.VmSettings>(Encoding.UTF8.GetBytes("{\"hotkey\":\"x\",\"trayTipShown\":true}"));
        Check(oldSt != null && oldSt.Presets == null && oldSt.Pinned == null, "1.7.0 の設定をそのまま読める");
    }

    // ---------------------------------------------------------------- Hub
    public static void RunHub(Action<bool, string> Check, string tmp)
    {
        AM::TealSuite.Common.AppInfo.Init("TealHub", "Teal Hub", "");
        var root = Path.Combine(tmp, "v180-data");
        var other = Path.Combine(tmp, "v180-restore");
        Directory.CreateDirectory(Path.Combine(root, "TealMemo"));
        Directory.CreateDirectory(Path.Combine(root, "TealPass"));
        Directory.CreateDirectory(Path.Combine(root, "TealHub", "backups"));
        File.WriteAllText(Path.Combine(root, "TealMemo", "notes.json"), "{\"memo\":1}");
        File.WriteAllBytes(Path.Combine(root, "TealPass", "vault.dat"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(root, "TealHub", "backups", "before-restore-x.zip"), "skip");
        File.WriteAllText(Path.Combine(root, "TealMemo", "x.tmp"), "skip");
        var zip = Path.Combine(tmp, H.HubBackup.DefaultName(new DateTime(2026, 10, 28, 9, 5, 0)));
        Check(Path.GetFileName(zip) == "TealSuite-backup-20261028-0905.zip", "バックアップのファイル名");
        int n = H.HubBackup.Create(root, zip, "1.8.0", new DateTime(2026, 10, 28, 9, 5, 0));
        Check(n == 2, "すべてのアプリのデータを zip に (一時ファイル・前のバックアップは入れない) (" + n + ")");
        var info = H.HubBackup.Inspect(zip);
        Check(info.Valid && info.Version == "1.8.0" && info.FileCount == 2 && info.HasProtected && info.Apps.SequenceEqual(new[] { "TealMemo", "TealPass" }), "バックアップの中身を確かめる (バージョン・アプリ・暗号化ファイル)");
        var notZip = Path.Combine(tmp, "v180-not.zip");
        File.WriteAllText(notZip, "not a zip");
        Check(!H.HubBackup.Inspect(notZip).Valid, "zip でないファイルは断る");
        Check(H.HubBackup.SafeName("TealMemo/notes.json") && !H.HubBackup.SafeName("../evil.txt") && !H.HubBackup.SafeName("/etc/x") && !H.HubBackup.SafeName("C:/x") && !H.HubBackup.SafeName("a/../../b"), "zip の中の危ないパスは使わない");
        Directory.CreateDirectory(Path.Combine(other, "TealMemo"));
        File.WriteAllText(Path.Combine(other, "TealMemo", "notes.json"), "{\"memo\":2}");
        File.WriteAllText(Path.Combine(other, "TealMemo", "keep.txt"), "keep");
        var safety = H.HubBackup.SafetyPath(other, new DateTime(2026, 10, 28, 9, 6, 0));
        int m = H.HubBackup.Restore(zip, other, safety, "1.8.0", new DateTime(2026, 10, 28, 9, 6, 0));
        Check(m == 2 && File.ReadAllText(Path.Combine(other, "TealMemo", "notes.json")) == "{\"memo\":1}" && File.Exists(Path.Combine(other, "TealPass", "vault.dat")), "バックアップから復元");
        Check(File.Exists(Path.Combine(other, "TealMemo", "keep.txt")), "zip に無いファイルは消さない");
        Check(File.Exists(safety) && H.HubBackup.Inspect(safety).Valid && H.HubBackup.Inspect(safety).FileCount >= 2, "復元の前に今のデータを残す");
        Check(H.HubBackup.SizeText(500) == "500 B" && H.HubBackup.SizeText(2048).EndsWith("KB"), "サイズの表示");

        var md = "# 変更履歴\n\n## 1.8.0 (2026-10-28)\n\n- 新しい機能\n- もう一つ\n\n## 1.7.0\n\n- 前の版\n";
        var cl = H.ChangeLog.Parse(md);
        Check(cl.Count == 2 && cl[0].Key.StartsWith("1.8.0") && cl[0].Value == "・新しい機能\n・もう一つ", "CHANGELOG.md を節ごとに読む");
        Check(H.ChangeLog.SectionFor(cl, "1.8.0") == "・新しい機能\n・もう一つ" && H.ChangeLog.SectionFor(cl, "1.7.0") == "・前の版" && H.ChangeLog.SectionFor(cl, "9.9.9") == null, "バージョンの節を探す");
        var real = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "CHANGELOG.md");
        if (File.Exists(real))
            Check(H.ChangeLog.SectionFor(H.ChangeLog.Parse(File.ReadAllText(real)), "1.8.0") != null, "同梱の CHANGELOG.md に 1.8.0 の節がある");
        var hs = AM::TealSuite.Common.DataStore.FromJson<H.HubSettings>(Encoding.UTF8.GetBytes("{}"));
        Check(hs != null && string.IsNullOrEmpty(hs.LastSeenVersion), "1.7.0 の Hub 設定をそのまま読める (更新内容はまだ見ていない扱い)");
    }

    // ---------------------------------------------------------------- サブスク
    public static void RunSub(Action<bool, string> Check)
    {
        SUB::TealSuite.Common.AppInfo.Init("TealWallet", "Teal Wallet", "");
        var today = D(2026, 10, 28);
        var a = new S.Subscription { Name = "動画", Amount = 1490, Category = "動画", Payment = "カード A" };
        a.Cycle = S.Cycle.Monthly; a.StartDate = D(2025, 1, 5);
        Check(!S.SubPlan.RecordPrice(1490, a, today), "金額が同じなら記録しない");
        a.Amount = 1790;
        Check(S.SubPlan.RecordPrice(1490, a, today) && S.SubPlan.Latest(a).From == 1490 && S.SubPlan.Latest(a).Increase, "値上げを記録");
        Check(S.SubPlan.RecentIncrease(a, today) != null && S.SubPlan.RecentIncrease(a, today.AddDays(120)) == null, "90 日以内の値上げだけ目立たせる");
        Check(S.SubPlan.HistoryText(a).Contains("1,490") || S.SubPlan.HistoryText(a).Contains("1490"), "料金の変更の説明");
        var y = new S.Subscription { Name = "年払い", Amount = 12000, Category = "その他" };
        y.Cycle = S.Cycle.Yearly; y.StartDate = D(2025, 3, 1);
        var cz = new S.Subscription { Name = "解約予定", Amount = 500, Category = "その他", Payment = "カード A" };
        cz.Cycle = S.Cycle.Monthly; cz.StartDate = D(2026, 1, 10); cz.Status = S.SubStatus.Cancelling;
        var subs = new List<S.Subscription> { a, y, cz };
        Check(S.SubPlan.BudgetLeft(subs, 0) == null, "予算 0 は「決めていない」");
        var left = S.SubPlan.BudgetLeft(subs, 3000);
        Check(left.HasValue && left.Value == 3000m - S.SubLogic.MonthlyTotal(subs), "予算の残り (" + left + ")");
        var plan = S.SubPlan.Forecast(subs, today, 12);
        Check(plan.Count == 12 && plan[0].Month == D(2026, 10, 1), "12 か月分の支払い予定");
        Check(plan.Count(p => p.Items.Any(i => i.Key == y)) == 1 && plan[5].Items.Any(i => i.Key == y && i.Value == D(2027, 3, 1)), "年払いは 3 月にだけ出る");
        Check(plan.Sum(p => p.Items.Count(i => i.Key == cz)) == 1, "解約予定のものは次の 1 回だけ");
        Check(plan[1].Total == 2290m && plan[2].Total == 1790m, "11 月 ¥2,290 (解約予定の最後の 1 回を含む)・12 月 ¥1,790 (" + plan[1].Total + " / " + plan[2].Total + ")");
        var pay = S.SubPlan.ByPayment(subs);
        Check(pay.Any(p => p.Payment == "カード A") && pay.Any(p => p.Payment == "未設定"), "支払い方法ごとの合計 (空は「未設定」)");
        S.Cycle cy; int mo;
        Check(S.SubPlan.ParseCycle("毎月", out cy, out mo) && cy == S.Cycle.Monthly, "「毎月」");
        Check(S.SubPlan.ParseCycle("3 か月ごと", out cy, out mo) && cy == S.Cycle.EveryNMonths && mo == 3, "「3 か月ごと」");
        Check(S.SubPlan.ParseCycle("12 か月ごと", out cy, out mo) && cy == S.Cycle.Yearly, "「12 か月ごと」は毎年");
        Check(!S.SubPlan.ParseCycle("ときどき", out cy, out mo), "読めない周期");
        var csv = S.SubLogic.ToCsv(subs, today);
        var imp = S.SubPlan.ImportCsv(csv, new S.Subscription[0], today);
        Check(imp.Error == null && imp.Items.Count == 3 && imp.Items.Any(s => s.Name == "年払い" && s.Cycle == S.Cycle.Yearly && s.Amount == 12000), "書き出した CSV を読み込める (" + imp.Items.Count + ")");
        var again = S.SubPlan.ImportCsv(csv, subs, today);
        Check(again.Items.Count == 0 && again.Duplicates == 3, "同じものは読み込まない");
        Check(S.SubPlan.ImportCsv("a,b\n1,2\n", subs, today).Error != null, "名前・金額の列がなければエラー");
        var simple = S.SubPlan.ImportCsv("名前,金額\n音楽,980\n,100\n", subs, today);
        Check(simple.Items.Count == 1 && simple.Items[0].Cycle == S.Cycle.Monthly && simple.Skipped == 1, "最低限 (名前・金額) の CSV も読める");
        var oldJson = SUB::TealSuite.Common.DataStore.FromJson<S.SubData>(Encoding.UTF8.GetBytes("{\"version\":1,\"subs\":[{\"id\":\"1\",\"name\":\"x\",\"amount\":100}],\"settings\":{\"remind\":true,\"remindDays\":3}}"));
        Check(oldJson != null && oldJson.Settings.MonthlyBudget == 0 && oldJson.Subs[0].PriceHistory == null, "1.7.0 のデータをそのまま読める");
        using (var f = new S.ForecastForm(subs, today, 3000))
            Check(f.Plans.Count == 12, "支払い予定の画面");
    }

    // ---------------------------------------------------------------- タスク
    public static void RunTasks(Action<bool, string> Check)
    {
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "");
        var now = new DateTime(2026, 10, 30, 10, 0, 0); // 金曜
        Check(T.TaskLogic.NextDue(D(2026, 10, 30), T.Repeat.Weekdays, now.Date) == D(2026, 11, 2), "平日: 金曜の次は月曜");
        Check(T.TaskLogic.NextDue(D(2026, 10, 27), T.Repeat.Weekdays, now.Date) == D(2026, 11, 2), "平日: たまった分は飛ばす");
        Check(T.TaskLogic.NextDue(D(2026, 4, 1), T.Repeat.Yearly, now.Date) == D(2027, 4, 1), "毎年: 4/1 → 来年の 4/1");
        Check(T.TaskLogic.NextDue(D(2024, 2, 29), T.Repeat.Yearly, D(2024, 2, 29)) == D(2025, 2, 28) && T.TaskLogic.NextDue(D(2024, 2, 29), T.Repeat.Yearly, D(2027, 3, 1)) == D(2028, 2, 29), "毎年: 2/29 は 2/28、うるう年は 2/29");
        var p = T.QuickParser.Parse("平日 9時 朝会", now);
        Check(p.Title == "朝会" && p.Repeat == T.Repeat.Weekdays, "「平日 9時 朝会」→ 平日のくり返し (" + p.Describe(now) + ")");
        var sat = new DateTime(2026, 10, 31, 10, 0, 0);
        var ps = T.QuickParser.Parse("毎平日 日報", sat);
        Check(ps.Repeat == T.Repeat.Weekdays && ps.Date == D(2026, 11, 2), "土曜に「毎平日」→ 月曜から");
        var py = T.QuickParser.Parse("毎年 12/1 年賀状の準備", now);
        Check(py.Title == "年賀状の準備" && py.Repeat == T.Repeat.Yearly && py.Date == D(2026, 12, 1), "「毎年 12/1」");
        var item = py.ToTask(now);
        var back = TASKS::TealSuite.Common.DataStore.FromJson<T.TaskItem>(TASKS::TealSuite.Common.DataStore.ToJson(item));
        Check(back.Repeat == T.Repeat.Yearly && T.TaskLogic.RepeatLabel(T.Repeat.Weekdays).StartsWith("平日"), "新しいくり返しを保存・読み込み");
        var data = new T.TaskData();
        var w = item.Clone(); w.Repeat = T.Repeat.Weekdays; w.SetDue(now.Date, false); data.Tasks.Add(w);
        var next = T.TaskLogic.Complete(data, w, now);
        Check(next != null && next.DueDate == D(2026, 11, 2), "平日のタスクを金曜に完了 → 次は月曜");

        // テンプレート
        var src = new T.TaskItem { Title = "週報を書く", Priority = T.TaskItem.High, DurationMin = 30, Tags = new List<string> { "仕事" } };
        src.SetDue(now.Date.AddDays(2) + new TimeSpan(17, 0, 0), true);
        src.Subtasks.Add(new T.SubTask { Text = "数字をまとめる", Done = true });
        var tpl = T.TaskTools.FromTask(src, "週報", now);
        Check(tpl.Name == "週報" && tpl.DueDays == 2 && tpl.Time == "17:00" && tpl.Task.Subtasks.All(s => !s.Done), "タスクをテンプレートに (期限は 2 日後 17:00 として覚える)");
        var later = now.AddDays(10);
        var made = T.TaskTools.Instantiate(tpl, later);
        Check(made.Title == "週報を書く" && made.Due == Fmt(later.Date.AddDays(2)) + "T17:00" && made.Priority == T.TaskItem.High && made.Tags.Contains("仕事") && !made.Done && made.Id != src.Id, "テンプレートから追加 (使った日から 2 日後)");
        var made2 = T.TaskTools.Instantiate(tpl, later);
        Check(made2.Id != made.Id && made2.Subtasks != made.Subtasks, "毎回べつのタスクになる");
        var td = new T.TaskData();
        Check(T.TaskTools.SaveTemplate(td, tpl) && T.TaskTools.SaveTemplate(td, T.TaskTools.FromTask(src, "週報", now)) && td.Templates.Count == 1, "同じ名前のテンプレートは上書き");
        var json = TASKS::TealSuite.Common.DataStore.ToJson(td);
        var td2 = TASKS::TealSuite.Common.DataStore.FromJson<T.TaskData>(json); td2.Normalize();
        Check(td2.Templates.Count == 1 && td2.Templates[0].Time == "17:00", "テンプレートを保存・読み込み");
        var oldData = TASKS::TealSuite.Common.DataStore.FromJson<T.TaskData>(Encoding.UTF8.GetBytes("{\"version\":1,\"tasks\":[{\"id\":\"a\",\"title\":\"x\",\"repeat\":\"weekly\"}]}"));
        oldData.Normalize();
        Check(oldData.Templates == null && oldData.Tasks[0].Repeat == T.Repeat.Weekly, "1.7.0 のデータをそのまま読める");
        Check(!Encoding.UTF8.GetString(TASKS::TealSuite.Common.DataStore.ToJson(new T.TaskData())).Contains("templates"), "テンプレートが無ければファイルに書かない");

        // まとめて追加
        var bulk = T.TaskTools.ParseBulk("・牛乳を買う #買い物\r\n\r\n1. 明日10時 会議の資料 重要\n- [ ] 掃除\n□ 洗濯\n   \n", now);
        Check(bulk.Count == 4 && bulk[0].Title == "牛乳を買う" && bulk[0].Tags.Contains("買い物") && bulk[1].Title == "会議の資料" && bulk[1].Priority == T.TaskItem.High && bulk[2].Title == "掃除" && bulk[3].Title == "洗濯", "まとめて追加: 箇条書きの記号を落として 1 行 1 件 (" + string.Join("/", bulk.Select(x => x.Title)) + ")");
        var bd = T.TaskTools.ParseBulk("買い物\n明日 歯医者", now, D(2026, 11, 5));
        Check(bd[0].DueDate == D(2026, 11, 5) && bd[1].DueDate == D(2026, 10, 31), "日付の無い行はカレンダーで選んでいる日");

        // 整理・複製
        var cd = new T.TaskData();
        var d1 = new T.TaskItem { Title = "昔の" }; d1.Done = true; d1.DoneAt = "2026-06-01T10:00";
        var d2 = new T.TaskItem { Title = "最近の" }; d2.Done = true; d2.DoneAt = "2026-10-25T10:00";
        var o1 = new T.TaskItem { Title = "未完了" };
        cd.Tasks.AddRange(new[] { d1, d2, o1 });
        Check(T.TaskTools.OldDone(cd.Tasks, now, 30).Single() == d1, "30 日より前に完了したもの");
        Check(T.TaskTools.CleanupDone(cd, now, 30) == 1 && cd.Tasks.Count == 2 && cd.Tasks.Contains(o1), "完了したタスクを整理 (未完了は残す)");
        var dup = T.TaskTools.Duplicate(d2, now);
        Check(dup.Id != d2.Id && !dup.Done && dup.Title == d2.Title, "複製は未完了の新しいタスク");
    }

    static string Fmt(DateTime d) { return d.ToString("yyyy-MM-dd"); }

    public static void RunTasksUi(Action<bool, string> Check)
    {
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "");
        var now = new DateTime(2026, 10, 27, 10, 0, 0);
        using (var f = new T.MainForm())
        {
            f.Clock = () => now;
            f.Data = new T.TaskData();
            f.ConfirmHook = s => true;
            f.TemplateNameHook = s => "テンプレA";
            f.RefreshAll();
            f.Quick.Text = "明日15時 歯医者 #病院";
            var t = f.QuickAdd();
            f.TodayList.Focus();
            Check(f.SaveAsTemplate(t) && f.Data.Templates.Count == 1 && f.Data.Templates[0].Name == "テンプレA", "画面: 選んだタスクをテンプレートに");
            var m = f.AddFromTemplate(f.Data.Templates[0]);
            Check(f.Data.Tasks.Count == 2 && m.Title == "歯医者" && m.Id != t.Id, "画面: テンプレートから追加");
            f.Undo();
            Check(f.Data.Tasks.Count == 1 && f.Data.Templates != null && f.Data.Templates.Count == 1, "元に戻してもテンプレートは残る");
            Check(f.AddMany(T.TaskTools.ParseBulk("a\nb\nc", now)) == 3 && f.Data.Tasks.Count == 4, "画面: まとめて追加");
            f.Undo();
            Check(f.Data.Tasks.Count == 1, "まとめて追加も 1 回で元に戻せる");
            var d = f.DuplicateTask(f.Data.Tasks[0]);
            Check(d != null && f.Data.Tasks.Count == 2, "画面: 複製");
            var old = new T.TaskItem { Title = "昔" }; old.Done = true; old.DoneAt = "2026-01-01T10:00";
            f.Data.Tasks.Add(old);
            Check(f.CleanupDone(90) == 1 && !f.Data.Tasks.Contains(old), "画面: 完了したタスクを整理");
            var menu = f.MoreMenu;
            Check(menu.Items.Count >= 7, "画面: 「その他」メニュー");
        }
    }
}
