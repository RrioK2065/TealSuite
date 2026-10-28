// 1.10.0 のテスト: アプリどうしの連携 (Teal Wallet → Teal Plan のカレンダー、Teal Hub の「今日」と検索、共通の #タグ、Teal Pass のサイト名の索引)
extern alias AM;
extern alias TASKS;
extern alias SUB;
extern alias MEMO;
extern alias PM;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using H = AM::TealSuite.Hub;
using HC = AM::TealSuite.Common;
using T = TASKS::TealSuite.Tasks;
using TC = TASKS::TealSuite.Common;
using S = SUB::TealSuite.Wallet;
using SC = SUB::TealSuite.Common;
using M = MEMO::TealSuite.Memo;
using MC = MEMO::TealSuite.Common;
using P = PM::TealSuite.Pass;
using PC = PM::TealSuite.Common;

public static class V1100IntegrationTests
{
    static readonly DateTime Now = new DateTime(2026, 10, 28, 9, 0, 0); // 水曜日
    static Action<bool, string> Check;
    static int bump;

    const string Secret = "Secret-Pw-123!", SecretUser = "taro.secret@example.com", SecretMemo = "秘密の質問は犬の名前", SecretUrl = "https://login.amazon.example/secret-path";

    static string Fresh(string tmp, string name)
    {
        var d = Path.Combine(tmp, "v1100i-" + name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        return d;
    }

    /// <summary>ファイルを書いて、更新日時をずらす (読み込みのキャッシュが前のものを返さないように)</summary>
    static void Put(string root, string appId, string file, byte[] data)
    {
        var p = Path.Combine(Path.Combine(root, appId), file);
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllBytes(p, data);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddSeconds(++bump));
    }

    static S.Subscription Sub(string name, long amount, DateTime start, DateTime? trialEnd = null, S.SubStatus st = S.SubStatus.Active, S.Cycle c = S.Cycle.Monthly)
    {
        var s = new S.Subscription { Name = name, Amount = amount };
        s.Cycle = c; s.StartDate = start; s.TrialEndDate = trialEnd; s.Status = st;
        return s;
    }

    public class Ids { public string Netflix, Trial, Today, Cancelled, Milk, Bill, Trip, Meeting, MemoPin, MemoPlain, MemoTrash, ShopItem; }

    /// <summary>Wallet・Plan・Memo・Pass・Clip のデータを root に書く</summary>
    static Ids Seed(string root, bool withPass = true)
    {
        var ids = new Ids();
        var sd = new S.SubData();
        var nf = Sub("Netflix", 1490, new DateTime(2026, 9, 30)); ids.Netflix = nf.Id;
        var tr = Sub("電子書籍読み放題", 980, new DateTime(2026, 10, 3), new DateTime(2026, 11, 2)); ids.Trial = tr.Id;
        var td = Sub("クラウド保存", 250, new DateTime(2026, 9, 28)); ids.Today = td.Id;
        var cx = Sub("ゲーム", 800, new DateTime(2026, 9, 29), null, S.SubStatus.Cancelled); ids.Cancelled = cx.Id;
        var yr = Sub("ドメイン", 1500, new DateTime(2026, 3, 1), null, S.SubStatus.Active, S.Cycle.Yearly);
        sd.Subs.AddRange(new[] { nf, tr, td, cx, yr });
        sd.Settings.MonthlyBudget = 18000;
        Put(root, "TealWallet", S.SubData.FileName, SC.DataStore.ToJson(sd));
        var shop = new S.ShopData();
        var list = new S.ShopList { Name = "スーパー" };
        var item = new S.ShopItem { Name = "トイレットペーパー" }; ids.ShopItem = item.Id;
        list.Items.Add(item);
        shop.Lists.Add(list);
        Put(root, "TealWallet", S.ShopData.FileName, SC.DataStore.ToJson(shop));

        var pd = new T.TaskData();
        var milk = new T.TaskItem { Title = "牛乳を買う" }; milk.SetDue(Now.Date, false); milk.Tags.Add("買い物");
        milk.Subtasks.Add(new T.SubTask { Text = "低脂肪", Done = true }); milk.Subtasks.Add(new T.SubTask { Text = "2本" });
        var bill = new T.TaskItem { Title = "請求書を払う", Priority = T.TaskItem.High }; bill.SetDue(Now.Date.AddDays(-1), false);
        var trip = new T.TaskItem { Title = "旅行の準備" }; trip.SetDue(new DateTime(2026, 11, 10), false); trip.Tags.Add("買い物");
        var done = new T.TaskItem { Title = "済んだこと", Done = true }; done.SetDue(Now.Date, false);
        var ev = new T.EventItem { Title = "定例会議", Location = "会議室A" }; ev.SetTimes(Now.Date.AddHours(14), Now.Date.AddHours(15), false); ev.Tags.Add("仕事");
        pd.Tasks.AddRange(new[] { milk, bill, trip, done });
        pd.Events.Add(ev);
        ids.Milk = milk.Id; ids.Bill = bill.Id; ids.Trip = trip.Id; ids.Meeting = ev.Id;
        Put(root, "TealPlan", T.TaskData.FileName, TC.DataStore.ToJson(pd));

        var nfile = new M.NoteFile();
        var n1 = M.Note.Create(); n1.Text = "買い物リスト\n#買い物 牛乳とパン"; n1.Pinned = true;
        var n2 = M.Note.Create(); n2.Text = "Wi-Fi の設定\nルーターは棚の上";
        var n3 = M.Note.Create(); n3.Text = "古いメモ #買い物"; n3.Deleted = DateTime.UtcNow.ToString("o");
        nfile.Notes.AddRange(new[] { n1, n2, n3 });
        ids.MemoPin = n1.Id; ids.MemoPlain = n2.Id; ids.MemoTrash = n3.Id;
        Put(root, "TealMemo", M.NoteStore.FileName, MC.DataStore.ToJson(nfile));

        if (withPass)
        {
            PC.AppInfo.Init("TealPass", "Teal Pass", "");
            var v = new P.VaultFile();
            v.Entries.Add(new P.VaultEntry { Site = "Amazon", Url = SecretUrl, UserId = SecretUser, Password = Secret, Memo = SecretMemo });
            v.Entries.Add(new P.VaultEntry { Site = "みどり銀行", UserId = "hanako", Password = "Bank-Pa55" });
            v.Entries.Add(new P.VaultEntry { Site = "amazon", Password = "dup" });
            v.Entries.Add(new P.VaultEntry { Site = "  ", Password = "nameless" });
            P.VaultStore.Save(v);
            P.SiteIndex.Update(v, true);
        }
        // Teal Clip の履歴らしきもの (検索に出てはいけない)
        Put(root, "TealClip", "history.json", Encoding.UTF8.GetBytes("{\"items\":[{\"text\":\"クリップの秘密 Amazon " + Secret + "\"}]}"));
        return ids;
    }

    static bool Contains(byte[] hay, string s)
    {
        foreach (var enc in new[] { Encoding.UTF8, Encoding.Unicode })
        {
            var n = enc.GetBytes(s);
            for (int i = 0; i + n.Length <= hay.Length; i++)
            {
                int j = 0; while (j < n.Length && hay[i + j] == n[j]) j++;
                if (j == n.Length) return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- 読み込み (Wallet → Plan、壊れたファイル)
    public static void RunRead(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        try
        {
            var root = Fresh(tmp, "read");
            var ids = Seed(root, false);
            var w = TC.SuiteRead.Wallet();
            Check(w.Found && w.Subs.Count == 5 && w.SubBudget == 18000 && w.Lists.Count == 1, "Teal Plan から Teal Wallet のデータを読める");
            var dues = TC.WalletCalc.Dues(w.Subs, Now.Date, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1));
            Check(dues.Any(d => d.Sub.Id == ids.Netflix && d.Day == new DateTime(2026, 10, 30) && d.Title == "Netflix 1,490円" && d.Badge == "💳"), "支払い日「Netflix 1,490円」(10/30): " + string.Join(", ", dues.Select(d => d.Title + "@" + d.Day.ToString("MM/dd"))));
            Check(dues.Any(d => d.Sub.Id == ids.Trial && d.TrialEnd && d.Day == new DateTime(2026, 11, 2) && d.Title == "電子書籍読み放題 無料期間おわり" && d.Badge == "⚠"), "無料期間の終わり「○○ 無料期間おわり」");
            Check(!dues.Any(d => d.Sub.Id == ids.Trial && !d.TrialEnd && d.Day < new DateTime(2026, 11, 2)), "無料期間のうちは支払いを出さない");
            Check(!dues.Any(d => d.Sub.Id == ids.Cancelled), "解約済みは出さない");
            Check(!dues.Any(d => d.Day < new DateTime(2026, 10, 1) || d.Day >= new DateTime(2026, 12, 1)), "範囲の外は出さない");

            // 解約予定: 次の支払い日までは出す
            var cancelling = Sub("解約予定のサブスク", 500, new DateTime(2026, 9, 5), null, S.SubStatus.Cancelling);
            var cd = TC.DataStore.FromJson<TC.SharedSubFile>(SC.DataStore.ToJson(new S.SubData { Subs = { cancelling } }));
            var cdues = TC.WalletCalc.Dues(cd.Subs, Now.Date, Now.Date, Now.Date.AddMonths(4));
            Check(cdues.Count <= 1, "解約予定は (多くても) 次の 1 回だけ: " + cdues.Count);

            // Teal Plan のカレンダー
            var src = new T.WalletPlanSource(() => Now);
            var items = src.Items(new DateTime(2026, 10, 26), new DateTime(2026, 11, 9)).ToList();
            var nfx = items.FirstOrDefault(x => T.WalletPlanSource.SubIdOf(x) == ids.Netflix);
            Check(nfx != null && nfx.AllDay && nfx.Start == new DateTime(2026, 10, 30) && nfx.Badge == "💳" && nfx.Title == "Netflix 1,490円" && nfx.SourceName == "Teal Wallet", "カレンダーに 💳「Netflix 1,490円」");
            Check(items.Any(x => x.Badge == "⚠" && x.Title.EndsWith("無料期間おわり")), "カレンダーに ⚠「無料期間おわり」");
            Check(T.WalletPlanSource.SubIdOf(new T.CalItem { Title = "x" }) == null && T.WalletPlanSource.SubIdOf(null) == null, "Wallet の項目でなければ id は null");

            using (var f = new T.MainForm())
            {
                f.Clock = () => Now;
                f.ConfirmHook = m => true;
                f.Data = new T.TaskData();
                f.ApplyWalletSource();
                f.CreateControl();
                f.Tabs.SelectedIndex = 1;
                f.CalMode.SelectedIndex = 0;
                f.RefreshAll();
                var shown = f.Calendar.ShownItems.Where(x => T.WalletPlanSource.SubIdOf(x) != null).ToList();
                Check(shown.Any(x => x.Title == "Netflix 1,490円"), "Teal Plan の月のカレンダーに Wallet の支払い: " + shown.Count);
                Check(shown.All(x => x.ReadOnly), "Wallet の項目は読むだけ (動かせない・消せない)");
                var opened = new List<string>();
                f.OpenRequestHook = (a, k, key) => opened.Add(a + "/" + k + "/" + key);
                f.OpenItem(shown.First(x => x.Title == "Netflix 1,490円"));
                Check(opened.Count == 1 && opened[0] == "TealWallet/sub/" + ids.Netflix, "クリックで Teal Wallet のそのサブスクを開く: " + string.Join(",", opened));
                int before = f.Data.Tasks.Count + f.Data.Events.Count;
                f.DeleteItem(shown[0]);
                Check(f.Data.Tasks.Count + f.Data.Events.Count == before, "Wallet の項目を消そうとしても何も消えない");

                f.Data.Settings.ShowWallet = false;
                f.ApplyWalletSource();
                f.RefreshAll();
                Check(!f.Calendar.ShownItems.Any(x => T.WalletPlanSource.SubIdOf(x) != null) && !f.ExternalSources.Any(), "設定でオフにするとカレンダーに出さない");
                f.Data.Settings.ShowWallet = true;
                f.ApplyWalletSource();
                Check(f.ExternalSources.Count(x => x is T.WalletPlanSource) == 1, "オンに戻すと 1 つだけ足す");
                f.ApplyWalletSource();
                Check(f.ExternalSources.Count(x => x is T.WalletPlanSource) == 1, "何度呼んでも重ならない");
            }
            var oldSt = TC.DataStore.FromJson<T.TaskSettings>(Encoding.UTF8.GetBytes("{\"remind\":true}"));
            Check(oldSt.ShowWallet, "1.9 までの設定ファイル → Wallet の支払いを出す (既定はオン)");

            // 壊れた・無いファイル → 何も出さない (例外にしない)
            Put(root, "TealWallet", S.SubData.FileName, Encoding.UTF8.GetBytes("{ これは壊れた JSON"));
            Put(root, "TealWallet", S.ShopData.FileName, new byte[] { 0xff, 0xfe, 0x00 });
            var bad = TC.SuiteRead.Wallet();
            Check(!bad.Found && bad.Subs.Count == 0 && TC.SuiteRead.LastError != null, "壊れた subs.json → 空 (エラーは記録だけ)");
            Check(!src.Items(new DateTime(2026, 10, 1), new DateTime(2026, 12, 1)).Any(), "壊れたデータ → カレンダーに何も出さない");
            Put(root, "TealPlan", T.TaskData.FileName, Encoding.UTF8.GetBytes("[1,2,"));
            Put(root, "TealMemo", M.NoteStore.FileName, Encoding.UTF8.GetBytes("null"));
            int[] counts;
            var rows = HC.SuiteRead.Plan() == null ? null : H.HubToday.Build(Now, null, out counts);
            Check(rows != null && rows.All(r => r.Header || r.Empty || r.Kind == "budget") && rows.Any(r => r.Empty), "壊れたデータ → 「今日」は見出しと「ありません」だけ");
            Check(H.HubSearch.Run("牛乳", Now, null).Count == 0, "壊れたデータ → 検索は 0 件 (例外にしない)");
            Fresh(tmp, "read-empty");
            Check(!TC.SuiteRead.Wallet().Found && !new T.WalletPlanSource(() => Now).Items(Now, Now.AddDays(30)).Any(), "Teal Wallet が無い → 何も出さない");
            rows = H.HubToday.Build(Now, null, out counts);
            Check(rows.Any(r => r.Empty && r.Title.Contains("Teal Wallet にサブスクを入れると")) && counts.Sum() == 0, "データが無いときは案内の行");
            Check(H.HubToday.TraySummary(Now, null) == null || H.HubToday.TraySummary(Now, null).Contains("0件"), "データが無いときの通知領域の件数: " + H.HubToday.TraySummary(Now, null));
        }
        finally { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData); }
    }

    // ---------------------------------------------------------------- 「開いて」の頼みごと
    public static void RunOpen(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        try
        {
            var root = Fresh(tmp, "open");
            HC.AppInfo.Init("TealHub", "Teal Hub", "");
            Check(HC.OpenRequest.Write("TealPlan", "task", "abc"), "頼みごとを書ける");
            Check(File.Exists(HC.OpenRequest.PathFor("TealPlan")) && HC.OpenRequest.PathFor("TealPlan").StartsWith(Path.Combine(root, "TealPlan")), "TealPlan\\open-request.json");
            var r = TC.OpenRequest.Take("TealPlan");
            Check(r != null && r.Kind == "task" && r.Key == "abc", "アプリが読める (別のアセンブリでも同じ形)");
            Check(TC.OpenRequest.Take("TealPlan") == null && !File.Exists(HC.OpenRequest.PathFor("TealPlan")), "1 回読んだら消える");
            HC.OpenRequest.Write("TealPlan", "task", "old");
            var p = HC.OpenRequest.PathFor("TealPlan");
            File.WriteAllText(p, "{\"kind\":\"task\",\"key\":\"old\",\"at\":" + DateTime.UtcNow.AddSeconds(-HC.OpenRequest.MaxAgeSeconds - 10).Ticks + "}");
            Check(TC.OpenRequest.Take("TealPlan") == null, "古い頼みごと (5 分より前) は無視");
            File.WriteAllText(p, "壊れた");
            Check(TC.OpenRequest.Take("TealPlan") == null, "壊れた頼みごとは無視");
            Check(HC.OpenRequest.Send("TealNothing", "task", "x", Path.Combine(tmp, "no-such.exe")) == "missing" && !File.Exists(HC.OpenRequest.PathFor("TealNothing")), "アプリが無い → missing (頼みごとは残さない)");

            var ids = Seed(root);
            // Teal Plan
            TC.AppInfo.Init("TealPlan", "Teal Plan", "");
            using (var f = new T.MainForm())
            {
                f.Clock = () => Now; f.ConfirmHook = m => true;
                f.Data = TC.DataStore.FromJson<T.TaskData>(File.ReadAllBytes(Path.Combine(root, "TealPlan", T.TaskData.FileName)));
                f.Data.Normalize();
                f.CreateControl(); f.RefreshAll();
                f.Tabs.SelectedIndex = 3;
                Check(f.OpenFromRequest(new TC.OpenRequest { Kind = "task", Key = ids.Milk }) && f.Tabs.SelectedIndex == 0, "Plan: 今日のタスクは「今日」で開く");
                Check(f.OpenFromRequest(new TC.OpenRequest { Kind = "task", Key = ids.Trip }) && f.Tabs.SelectedIndex == 2, "Plan: 先のタスクは「一覧」で開く");
                Check(f.OpenFromRequest(new TC.OpenRequest { Kind = "event", Key = ids.Meeting }) && f.Tabs.SelectedIndex == 1, "Plan: 予定はカレンダーで開く");
                Check(!f.OpenFromRequest(new TC.OpenRequest { Kind = "task", Key = "nope" }) && !f.OpenFromRequest(new TC.OpenRequest { Kind = "sub", Key = ids.Milk }) && !f.OpenFromRequest(null), "Plan: 見つからない・ちがう種類は false");
            }
            // Teal Wallet
            SC.AppInfo.Init("TealWallet", "Teal Wallet", "");
            using (var f = new S.MainForm())
            {
                f.Data = SC.DataStore.FromJson<S.SubData>(File.ReadAllBytes(Path.Combine(root, "TealWallet", S.SubData.FileName)));
                f.Data.Normalize();
                f.Shop = SC.DataStore.FromJson<S.ShopData>(File.ReadAllBytes(Path.Combine(root, "TealWallet", S.ShopData.FileName)));
                f.Shop.Normalize();
                f.CreateControl(); f.RefreshAll();
                Check(f.OpenFromRequest(new SC.OpenRequest { Kind = "sub", Key = ids.Netflix }) && f.Tabs.SelectedIndex == 1 && f.List.SelectedItem is S.Subscription && ((S.Subscription)f.List.SelectedItem).Id == ids.Netflix, "Wallet: サブスクを開いて選ぶ");
                Check(f.OpenFromRequest(new SC.OpenRequest { Kind = "sub", Key = ids.Cancelled }) && f.Tabs.SelectedIndex == 3, "Wallet: 解約済みは「解約済み」で開く");
                Check(f.OpenFromRequest(new SC.OpenRequest { Kind = "shop", Key = ids.ShopItem }) && f.Tabs.SelectedIndex == 4, "Wallet: 買い物リストの品を開く");
                Check(!f.OpenFromRequest(new SC.OpenRequest { Kind = "sub", Key = "nope" }), "Wallet: 見つからなければ false");
            }
            // Teal Memo (データのフォルダから読む)
            MC.AppInfo.Init("TealMemo", "Teal Memo", "");
            using (var f = new M.MainForm())
            {
                f.CreateControl();
                Check(f.OpenFromRequest(new MC.OpenRequest { Kind = "note", Key = ids.MemoPlain }) && f.Current != null && f.Current.Id == ids.MemoPlain, "Memo: メモを開く");
                Check(f.OpenFromRequest(new MC.OpenRequest { Kind = "note", Key = ids.MemoTrash }) && f.Current.Id == ids.MemoTrash, "Memo: ゴミ箱のメモも開ける");
                Check(!f.OpenFromRequest(new MC.OpenRequest { Kind = "note", Key = "nope" }) && !f.OpenFromRequest(new MC.OpenRequest { Kind = "task", Key = ids.MemoPin }), "Memo: 見つからない・ちがう種類は false");
            }
            // Teal Pass: 保存データを読み終わるまで待つ
            PC.AppInfo.Init("TealPass", "Teal Pass", "");
            File.Delete(Path.Combine(root, "TealPass", P.SiteIndex.FileName));
            var f2 = new P.MainForm();
            try
            {
                Check(!f2.OpenFromRequest(new PC.OpenRequest { Kind = "site", Key = "みどり銀行" }), "Pass: 読み込む前は待つ (false)");
                f2.Show();
                for (int i = 0; i < 20; i++) Application.DoEvents();
                var la = (string)typeof(P.MainForm).GetField("lastAction", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f2);
                Check(la != null && la.Contains("みどり銀行") && la.Contains("開きました"), "Pass: 読み込んだあとで、待っていたサイトを開く: " + la);
                Check(File.Exists(Path.Combine(root, "TealPass", P.SiteIndex.FileName)), "Pass: 起動するとサイト名の索引を作り直す");
                Check(f2.OpenFromRequest(new PC.OpenRequest { Kind = "site", Key = "AMAZON" }), "Pass: サイト名は大文字・小文字を区別しない");
                Check(!f2.OpenFromRequest(new PC.OpenRequest { Kind = "site", Key = "無いサイト" }) && !f2.OpenFromRequest(new PC.OpenRequest { Kind = "note", Key = "Amazon" }), "Pass: 見つからない・ちがう種類は false");
            }
            finally { f2.Close(); f2.Dispose(); }
        }
        finally { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData); }
    }

    // ---------------------------------------------------------------- Teal Pass のサイト名の索引
    public static void RunPassIndex(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        try
        {
            var root = Fresh(tmp, "pass");
            Seed(root);
            var path = Path.Combine(root, "TealPass", P.SiteIndex.FileName);
            Check(path == H.PassSites.PathOf, "Teal Hub が読む場所と Teal Pass が書く場所が同じ");
            var raw = File.ReadAllBytes(path);
            Check(raw.Length > 4 && Encoding.ASCII.GetString(raw, 0, 4) == "PSI1", "索引の先頭は PSI1");
            Check(Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(root, "TealPass", "vault.dat")), 0, 4) == "PMV1", "保存データ (vault.dat) は今までどおり PMV1");
            var enc = raw.Skip(4).ToArray();
            var plain = ProtectedData.Unprotect(enc, Encoding.UTF8.GetBytes("TealSuite.TealPass.siteindex.v1"), DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            Check(json.Contains("Amazon") && json.Contains("みどり銀行"), "索引の中身はサイト名: " + json);
            foreach (var s in new[] { Secret, SecretUser, SecretMemo, "login.amazon.example", "Bank-Pa55", "hanako", "nameless", "dup", "\"id\"", "password", "url", "memo" })
                Check(!json.Contains(s), "索引に入れない: " + s);
            foreach (var s in new[] { Secret, SecretUser, SecretMemo, "Amazon", "みどり銀行" })
                Check(!Contains(raw, s), "索引のファイルは暗号化されている (平文で「" + s + "」が無い)");
            bool wrongKey = false;
            try { ProtectedData.Unprotect(enc, Encoding.UTF8.GetBytes("AppSuite.PasswordMaker.vault.v1"), DataProtectionScope.CurrentUser); } catch (CryptographicException) { wrongKey = true; }
            Check(wrongKey, "保存データの鍵 (entropy) では索引を開けない (別の鍵)");
            bool vaultWithIndexKey = false;
            try { ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(root, "TealPass", "vault.dat")).Skip(4).ToArray(), Encoding.UTF8.GetBytes("TealSuite.TealPass.siteindex.v1"), DataProtectionScope.CurrentUser); } catch (CryptographicException) { vaultWithIndexKey = true; }
            Check(vaultWithIndexKey, "索引の鍵では保存データ (パスワード) を開けない");

            var sites = H.PassSites.Read();
            Check(sites.Count == 2 && sites.Contains("Amazon") && sites.Contains("みどり銀行"), "Teal Hub が読める (同じ名前は 1 つ、空の名前は入れない): " + string.Join(",", sites));
            var hits = H.HubSearch.Run("amaz", Now, null);
            Check(hits.Any(h => h.AppId == "TealPass" && h.Kind == "site" && h.Key == "Amazon") && hits.Where(h => h.AppId == "TealPass").All(h => !h.Detail.Contains(SecretUser) && !h.Detail.Contains("http")), "検索でサイト名が出る (ID・URL は出さない)");
            foreach (var s in new[] { "Secret-Pw", "taro.secret", "犬の名前", "login.amazon", "hanako", "Bank-Pa55" })
                Check(!H.HubSearch.Run(s, Now, null).Any(h => h.AppId == "TealPass"), "パスワード・ID・メモ・URL では見つからない: " + s);

            // 索引が壊れている・別のもの
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PSI1こわれた"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(++bump));
            Check(H.PassSites.Read().Count == 0 && H.HubSearch.Run("Amazon", Now, null).All(h => h.AppId != "TealPass"), "壊れた索引 → サイト名は出さない");
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("XXXX1234567890"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(++bump));
            Check(H.PassSites.Read().Count == 0, "ちがう形のファイル → 空");

            // 設定「サイト名を検索に出す」
            Check(new P.PmSettings().SiteIndex, "「サイト名を検索に出す」の既定はオン");
            var old = PC.DataStore.FromJson<P.PmSettings>(Encoding.UTF8.GetBytes("{\"clipboardClearSeconds\":30}"));
            Check(old.SiteIndex, "1.9 までの設定ファイル → オン");
            var v = P.VaultStore.Load();
            P.SiteIndex.Update(v, true);
            Check(H.PassSites.Read().Count == 2, "作り直すと読める");
            P.SiteIndex.Update(v, false);
            Check(!File.Exists(path) && H.PassSites.Read().Count == 0, "オフ → 索引を消す");

            var f = new P.MainForm();
            try
            {
                f.Show();
                for (int i = 0; i < 20; i++) Application.DoEvents();
                Check(File.Exists(path), "Teal Pass の起動で索引を作る (オン)");
                bool seen = false;
                f.SettingsHook = s => { seen = s.SiteIndex; s.SiteIndex = false; return true; };
                f.OpenSettings();
                Check(seen && !f.Settings.SiteIndex && !File.Exists(path), "設定でオフにすると索引をすぐ消す");
                var saved = PC.DataStore.FromJson<P.PmSettings>(File.ReadAllBytes(Path.Combine(root, "TealPass", "settings.json")));
                Check(saved != null && !saved.SiteIndex, "オフは保存される");
                Check(H.HubSearch.Run("Amazon", Now, null).All(h => h.AppId != "TealPass"), "オフ → Teal Hub の検索にサイト名が出ない");
                f.Vault.Entries.Add(new P.VaultEntry { Site = "新しいサイト", Password = "zzz" });
                P.VaultStore.Save(f.Vault);
                f.SettingsHook = s => true;
                f.OpenSettings();
                Check(!File.Exists(path), "オフのままなら作らない");
                f.SettingsHook = s => { s.SiteIndex = true; return true; };
                f.OpenSettings();
                Check(File.Exists(path) && H.PassSites.Read().Contains("新しいサイト"), "オンに戻すとすぐ作る");
                f.SettingsHook = s => { s.SiteIndex = false; return false; };
                f.OpenSettings();
                Check(f.Settings.SiteIndex && File.Exists(path), "キャンセルなら変えない");
            }
            finally { f.Close(); f.Dispose(); }

            // バックアップに索引は入れない
            var zip = Path.Combine(tmp, "v1100i-backup.zip");
            if (File.Exists(zip)) File.Delete(zip);
            H.HubBackup.Create(root, zip, "1.10.0", Now);
            using (var z = ZipFile.OpenRead(zip))
            {
                var names = z.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
                Check(names.Contains("TealPass/vault.dat") && !names.Contains("TealPass/site-index.dat"), "バックアップ: vault.dat は入れて、索引は入れない");
            }
        }
        finally { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData); }
    }

    // ---------------------------------------------------------------- 「今日」・検索・#タグ
    public static void RunHome(Action<bool, string> check, string tmp)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        try
        {
            var root = Fresh(tmp, "home");
            var ids = Seed(root);
            int[] counts;
            var rows = H.HubToday.Build(Now, null, out counts);
            Func<string, H.HomeItem> row = key => rows.FirstOrDefault(r => r.Key == key);
            Check(counts[0] == 2 && row(ids.Milk) != null && row(ids.Bill) != null && row(ids.Trip) == null, "今日のタスク: 今日まで + 期限切れ (先のもの・完了は出さない): " + counts[0]);
            Check(row(ids.Milk).Detail.Contains("小タスク 1/2") && row(ids.Milk).Detail.Contains("#買い物") && row(ids.Milk).Kind == "task" && row(ids.Milk).AppId == "TealPlan", "タスクの行: 小タスクの数とタグ");
            Check(row(ids.Bill).Detail.Contains("期限切れ") && row(ids.Bill).Badge == "❗", "期限切れ・重要");
            Check(counts[1] == 1 && row(ids.Meeting) != null && row(ids.Meeting).Detail.Contains("14:00〜15:00") && row(ids.Meeting).Detail.Contains("会議室A"), "今日の予定");
            Check(row(ids.Netflix) != null && row(ids.Netflix).Title == "Netflix 1,490円" && row(ids.Netflix).Detail.StartsWith("あさって"), "7 日以内の支払い: " + (row(ids.Netflix) == null ? "" : row(ids.Netflix).Detail));
            var trial = rows.FirstOrDefault(r => r.Key == ids.Trial && r.Badge == "⚠");
            Check(trial != null && trial.Detail.Contains("980円 かかります"), "無料期間の終わり (この日から有料)");
            Check(row(ids.Today) != null && row(ids.Today).Detail.StartsWith("今日") && counts[2] == 1, "今日の支払い");
            Check(row(ids.Cancelled) == null, "解約済みは出さない");
            var budget = rows.FirstOrDefault(r => r.Kind == "budget");
            Check(budget != null && budget.Title == "予算の残り" && budget.Detail.Contains("サブスク あと 15,155円"), "予算の残り (18,000円 - 毎月の合計): " + (budget == null ? "" : budget.Detail));
            Check(row(ids.MemoPin) != null && row(ids.MemoPlain) == null && row(ids.MemoTrash) == null && counts[3] == 1, "ピン留めのメモだけ (ゴミ箱は出さない)");
            Check(rows.All(r => r.AppId != "TealPass" && r.AppId != "TealClip"), "「今日」に Teal Pass・Teal Clip は出ない");
            var heads = rows.Where(r => r.Header).Select(r => r.AppId).Distinct().ToList();
            Check(heads.SequenceEqual(new[] { "TealPlan", "TealWallet", "TealMemo" }), "見出しはアプリごと (Plan → Wallet → Memo)");
            var onlyMemo = H.HubToday.Build(Now, id => id == "TealMemo", out counts);
            Check(onlyMemo.All(r => r.AppId == null || r.AppId == "TealMemo"), "入っていないアプリは出さない");
            Check(H.HubToday.TraySummary(Now, null) == "今日のタスク 2件 / 支払い 1件", "通知領域の 1 行: " + H.HubToday.TraySummary(Now, null));
            Check(H.HubToday.TraySummary(Now, id => false) == null, "アプリが無いときは出さない");

            // 検索
            Check(H.HubSearch.IsTag("#買い物") && H.HubSearch.IsTag("＃買い物") && !H.HubSearch.IsTag("#") && !H.HubSearch.IsTag("買い物"), "#タグの検索か");
            var tag = H.HubSearch.Run("#買い物", Now, null);
            Check(tag.Any(r => r.Key == ids.MemoPin) && tag.Any(r => r.Key == ids.Milk) && tag.Any(r => r.Key == ids.Trip), "#買い物 → Memo と Plan の両方");
            Check(!tag.Any(r => r.Key == ids.MemoTrash) && !tag.Any(r => r.AppId == "TealWallet" || r.AppId == "TealPass"), "#タグは Memo・Plan だけ (ゴミ箱は出さない)");
            Check(tag.Where(r => r.Header).Select(r => r.AppId).SequenceEqual(new[] { "TealMemo", "TealPlan" }) && tag.First().Header && tag.First().Title.StartsWith("Teal Memo  1 件"), "アプリごとにまとめる: " + string.Join(" / ", tag.Where(r => r.Header).Select(r => r.Title)));
            Check(H.HubSearch.Run("＃買い物", Now, null).Count == tag.Count && H.HubSearch.Run("#仕事", Now, null).Any(r => r.Key == ids.Meeting), "全角の＃・予定のタグ");
            var word = H.HubSearch.Run("牛乳", Now, null);
            Check(word.Any(r => r.Key == ids.MemoPin) && word.Any(r => r.Key == ids.Milk), "ことばの検索 (メモの本文・タスク)");
            Check(H.HubSearch.Run("低脂肪", Now, null).Any(r => r.Key == ids.Milk), "小タスクの文字でも見つかる");
            Check(H.HubSearch.Run("netflix", Now, null).Any(r => r.AppId == "TealWallet" && r.Kind == "sub" && r.Key == ids.Netflix), "サブスク (大文字・小文字を区別しない)");
            Check(H.HubSearch.Run("トイレット", Now, null).Any(r => r.Kind == "shop" && r.Key == ids.ShopItem), "買い物リストの品");
            Check(H.HubSearch.Run("会議室", Now, null).Any(r => r.Kind == "event"), "予定の場所");
            Check(H.HubSearch.Run("Amazon", Now, null).Any(r => r.AppId == "TealPass"), "Teal Pass のサイト名 (Teal Pass が起動していなくても)");
            Check(H.HubSearch.Run("クリップの秘密", Now, null).Count == 0, "Teal Clip の履歴は検索しない");
            Check(H.HubSearch.Run("Amazon", Now, null).All(r => r.AppId != "TealClip") && H.HubSearch.Run("   ", Now, null).Count == 0, "空の検索は 0 件");
            Check(H.HubSearch.Run("牛乳", Now, id => id == "TealPlan").All(r => r.AppId == "TealPlan"), "入っていないアプリは検索しない");

            // 共通のタグ
            var memoSide = MC.SuiteTags.Collect("TealMemo", new[] { "メモだけ" });
            Check(memoSide.Contains("メモだけ") && memoSide.Contains("仕事") && memoSide.Contains("買い物"), "Teal Memo の候補に Teal Plan のタグも出る: " + string.Join(",", memoSide));
            var planSide = TC.SuiteTags.Collect("TealPlan", new[] { "仕事" });
            Check(planSide.Contains("買い物") && planSide.Count(x => x == "買い物") == 1, "Teal Plan の候補に Teal Memo のタグ (重ならない)");
            Check(HC.SuiteTags.Has(new[] { "買い物" }, "かいもの") == false && HC.SuiteTags.Has(new[] { "Work" }, "work"), "タグは大文字・小文字を区別しない");
            int st;
            Check(TC.TagSuggest.Token("牛乳 #買", 5, true, out st) == "買" && st == 3, "入力中のタグ");
            Check(TC.TagSuggest.Token("牛乳 買", 4, true, out st) == null && TC.TagSuggest.Token("牛乳 買", 4, false, out st) == "買", "# が要るとき・要らないとき");
            Check(TC.TagSuggest.Token("#買い物です", 2, true, out st) == null, "言葉の途中では出さない");
            var cands = TC.TagSuggest.Candidates("い", new[] { "いぬ", "買い物", "仕事", "い" });
            Check(cands.SequenceEqual(new[] { "いぬ", "買い物" }), "候補: 始まるもの → 含むもの (同じものは出さない): " + string.Join(",", cands));
        }
        finally { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData); }
    }

    class FakeAssoc : H.IAssocStore
    {
        public string Command; public int Count;
        public string GetCommand() { return Command; }
        public void Register(string command, string icon, string description) { Command = command; Count++; }
    }

    // ---------------------------------------------------------------- Teal Hub の画面と通知領域
    public static void RunHubUi(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var root = Fresh(tmp, "hubui");
        var ids = Seed(root);
        Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
        HC.AppInfo.Init(new HC.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        HC.Startup.RunKey = new HC.MemoryRunKey();
        HC.Startup.Folder = new HC.StartupFolder(Path.Combine(tmp, "v1100i-startup"), p => null);
        HC.GlobalHotkey.FakeRegister = s => null;
        H.FileAssoc.Store = new FakeAssoc();
        H.UpdaterLauncher.StartProcess = (exe, a) => { };
        try
        {
            Check(H.MainForm.IsWatchedFile(Path.Combine(root, "TealPlan", "tasks.json")) && H.MainForm.IsWatchedFile(Path.Combine(root, "TealPass", "site-index.dat")), "見張るファイル: 各アプリの .json と索引");
            Check(!H.MainForm.IsWatchedFile(Path.Combine(root, "TealPass", "vault.dat")) && !H.MainForm.IsWatchedFile(Path.Combine(root, "TealClip", "history.json"))
                && !H.MainForm.IsWatchedFile(Path.Combine(root, "TealHub", "hub.json")) && !H.MainForm.IsWatchedFile(Path.Combine(root, "TealPlan", "open-request.json"))
                && !H.MainForm.IsWatchedFile(Path.Combine(root, "TealPlan", "tasks.json.tmp")) && !H.MainForm.IsWatchedFile(null), "見張らない: vault.dat・Teal Clip・Teal Hub 自身・頼みごと・一時ファイル");

            var oldHs = HC.DataStore.FromJson<H.HubSettings>(Encoding.UTF8.GetBytes("{\"version\":1}"));
            Check(oldHs != null && oldHs.TraySummary, "1.9 までの Teal Hub の設定 → 件数を通知領域に出す (既定はオン)");

            var tray = new H.HubTray(new string[0], true);
            tray.ExitAction = () => { };
            tray.Shortcuts.SignalReload = id => true;
            var f = tray.Window;
            f.Now = () => Now;
            var opened = new List<string>();
            f.OpenItemHook = (a, k, key) => { opened.Add(a + "/" + (k ?? "-") + "/" + (key ?? "-")); return "open"; };
            f.Show();
            Application.DoEvents();
            f.RefreshHome();
            Check(f.HasHomeApps && f.ViewTabs.SelectedIndex == 0 && f.IsHomeShown, "はじめは「今日」のタブ");
            Check(f.HomeTitle == "10月28日 (水) の今日", "見出し: " + f.HomeTitle);
            var items = f.HomeList_.Items.Cast<H.HomeItem>().ToList();
            Check(items.Any(x => x.Key == ids.Milk) && items.Any(x => x.Key == ids.Netflix) && items.Any(x => x.Key == ids.MemoPin), "「今日」にタスク・支払い・ピン留め");
            Check(f.OpenHomeItem(items.First(x => x.Key == ids.Milk)) == "open" && opened.Last() == "TealPlan/task/" + ids.Milk, "タスクの行 → Teal Plan でそのタスク");
            Check(f.OpenHomeItem(items.First(x => x.Key == ids.Netflix)) == "open" && opened.Last() == "TealWallet/sub/" + ids.Netflix, "支払いの行 → Teal Wallet でそのサブスク");
            Check(f.OpenHomeItem(items.First(x => x.Key == ids.MemoPin)) == "open" && opened.Last() == "TealMemo/note/" + ids.MemoPin, "メモの行 → Teal Memo でそのメモ");
            Check(f.OpenHomeItem(items.First(x => x.Kind == "budget")) == "open" && opened.Last() == "TealWallet/-/-", "予算の行・見出し → アプリを開くだけ");
            Check(f.OpenHomeItem(H.HomeItem.None("なし")) == "none", "空の行は開かない");
            Check(f.OpenHomeItem(null) == "none", "何も選んでいなければ何もしない");

            // 検索
            f.SearchBox.Text = "#買い物";
            f.ApplyView();
            Check(f.SearchActive && f.IsHomeShown && f.HomeTitle.StartsWith("「#買い物」") && f.HomeTitle.EndsWith("3 件"), "検索の見出し: " + f.HomeTitle);
            var res = f.HomeList_.Items.Cast<H.HomeItem>().ToList();
            Check(res.Any(x => x.AppId == "TealMemo" && !x.Header) && res.Any(x => x.AppId == "TealPlan" && !x.Header) && res.All(x => x.AppId != "TealClip" && x.AppId != "TealPass"), "#買い物 → Memo と Plan");
            Check(f.OpenHomeItem(res.First(x => x.Key == ids.Trip)) == "open" && opened.Last() == "TealPlan/task/" + ids.Trip, "検索の結果を開く");
            f.SearchBox.Text = "amazon";
            f.ApplyView();
            res = f.HomeList_.Items.Cast<H.HomeItem>().ToList();
            Check(res.Any(x => x.AppId == "TealPass" && x.Key == "Amazon"), "サイト名を検索");
            Check(f.OpenHomeItem(res.First(x => x.AppId == "TealPass" && !x.Header)) == "open" && opened.Last() == "TealPass/site/Amazon", "サイト名 → Teal Pass でそのサイト (サイト名だけを渡す)");
            Check(res.All(x => !x.Title.Contains(Secret) && !x.Detail.Contains(Secret) && !x.Detail.Contains(SecretUser)), "検索の結果にパスワード・ID は出ない");
            f.SearchBox.Text = "見つからない言葉";
            f.ApplyView();
            Check(f.HomeList_.Items.Count == 0 && f.HomeTitle == "「見つからない言葉」", "見つからない");
            // #タグの候補
            f.SearchBox.Text = "#買";
            f.SearchBox.SelectionStart = f.SearchBox.TextLength;
            f.SearchTags.Update();
            Check(f.SearchTags.Visible && f.SearchTags.Items.Contains("買い物"), "検索欄で #タグの候補: " + string.Join(",", f.SearchTags.Items));
            f.SearchTags.Accept();
            Check(f.SearchBox.Text == "#買い物 ", "候補を選ぶと入る: [" + f.SearchBox.Text + "]");
            f.ViewTabs.SelectedIndex = 1;
            Check(!f.SearchActive && !f.IsHomeShown, "「アプリ」のタブに切り替えると検索を消す");
            f.ViewTabs.SelectedIndex = 0;
            Check(f.IsHomeShown && f.HomeTitle.EndsWith("の今日"), "「今日」に戻る");

            // 新しいデータ → 読み直す
            var pd = TC.DataStore.FromJson<T.TaskData>(File.ReadAllBytes(Path.Combine(root, "TealPlan", "tasks.json")));
            var added = new T.TaskItem { Title = "あとから足したタスク" }; added.SetDue(Now.Date, true);
            pd.Tasks.Add(added);
            Put(root, "TealPlan", "tasks.json", TC.DataStore.ToJson(pd));
            f.RefreshHome();
            Check(f.HomeList_.Items.Cast<H.HomeItem>().Any(x => x.Key == added.Id), "読み直すと新しいタスクが出る");
            Check(f.Watching, "データのフォルダを見張っている");

            // 通知領域
            Check(tray.TraySummaryOn && tray.Summary() == "今日のタスク 3件 / 支払い 1件", "通知領域の件数: " + tray.Summary());
            tray.SetTraySummary(true);
            Check(tray.TrayText.Contains("今日のタスク 3件") && tray.TrayText.Length <= 63, "通知領域のアイコンの文字 (63 文字まで): " + tray.TrayText);
            tray.SetTraySummary(false);
            Check(!tray.TraySummaryOn && tray.Summary() == null && !tray.TrayText.Contains("今日のタスク"), "オフにすると出さない");
            var hs = HC.DataStore.FromJson<H.HubSettings>(File.ReadAllBytes(HC.DataStore.PathOf(H.HubTray.SettingsFile)));
            Check(hs != null && !hs.TraySummary, "オフは保存される");
            tray.SetTraySummary(true);
            Check(tray.TrayText.Contains("今日のタスク"), "オンに戻す");

            tray.ExitApp();
            Check(!f.Watching, "終了すると見張りをやめる");
        }
        finally
        {
            HC.GlobalHotkey.FakeRegister = null;
            H.FileAssoc.Store = null;
            H.UpdaterLauncher.StartProcess = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DEMO", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    // ---------------------------------------------------------------- Teal Plan: 一覧の「予定」ではタスク用のボタンを隠す
    public static void RunPlanButtons(Action<bool, string> check)
    {
        Check = check;
        using (var f = new T.MainForm())
        {
            f.Clock = () => Now; f.ConfirmHook = m => true;
            f.Data = new T.TaskData();
            f.CreateControl();
            f.Tabs.SelectedIndex = 2;
            var taskOnly = new[] { "完了 / 戻す", "＋小タスク", "明日に回す", "すべて開く", "すべて閉じる" };
            f.ListKind.SelectedIndex = 0; f.RefreshAll();
            var b0 = f.ListButtonLabels();
            Check(taskOnly.All(b0.Contains) && b0.Contains("編集") && b0.Contains("削除") && !b0.Contains("＋ 予定を追加"), "一覧「タスク」: タスク用のボタン: " + string.Join(",", b0));
            f.ListKind.SelectedIndex = 1; f.RefreshAll();
            var b1 = f.ListButtonLabels();
            Check(!taskOnly.Any(b1.Contains) && b1.Contains("＋ 予定を追加") && b1.Contains("編集") && b1.Contains("削除"), "一覧「予定」: 完了・小タスク・明日に回す・開く/閉じるを隠す: " + string.Join(",", b1));
            f.ListKind.SelectedIndex = 2; f.RefreshAll();
            var b2 = f.ListButtonLabels();
            Check(taskOnly.All(b2.Contains) && b2.Contains("＋ 予定を追加"), "一覧「すべて」: 両方: " + string.Join(",", b2));
        }
    }
}
