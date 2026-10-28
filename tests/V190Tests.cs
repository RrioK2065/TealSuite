// 1.9.0 のテスト: Teal Sub → Teal Wallet の名前変更 (引き継ぎ) と、買い物リスト・欲しいもの
extern alias AM;
extern alias SUB;
extern alias UP;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using H = AM::TealSuite.Hub;
using S = SUB::TealSuite.Wallet;
using U = UP::TealSuite.Updater;
using SC = SUB::TealSuite.Common;
using HC = AM::TealSuite.Common;

public static class V190Tests
{
    static DateTime D(int y, int m, int d) { return new DateTime(y, m, d); }

    // ---------------------------------------------------------------- 入力の読み取り
    public static void RunParse(Action<bool, string> Check)
    {
        SC.AppInfo.Init("TealWallet", "Teal Wallet", "");
        Func<string, S.ParsedItem> P = S.ShopLogic.ParseOne;
        var a = P("牛乳 2 198円");
        Check(a.Name == "牛乳" && a.Qty == 2 && a.Price == 198 && a.HasQty && a.HasPrice, "「牛乳 2 198円」→ 牛乳・2 つ・198 円");
        var b = P("牛乳 ×2 ¥198");
        Check(b.Name == "牛乳" && b.Qty == 2 && b.Price == 198, "「牛乳 ×2 ¥198」も読める");
        var c = P("卵 1パック 228");
        Check(c.Name == "卵" && c.Qty == 1 && c.Unit == "パック" && c.Price == 228, "「卵 1パック 228」→ 単位つきの数と値段");
        var d = P("トマト3個");
        Check(d.Name == "トマト" && d.Qty == 3 && d.Unit == "個" && !d.HasPrice, "「トマト3個」名前にくっついた数");
        var e = P("ティッシュ #日用品");
        Check(e.Name == "ティッシュ" && e.Category == "日用品", "#日用品 で売り場を決める");
        Check(P("キャベツ #野菜").Category == "野菜・果物", "#野菜 は「野菜・果物」に寄せる");
        var f = P("牛乳 2 198");
        Check(f.Qty == 2 && f.Price == 198, "「円」がなくても 2 つ目の数字は値段");
        Check(P("しょうゆ 298").Price == 298 && P("しょうゆ 298").Qty == 1, "数字 1 つで 100 以上は値段");
        Check(P("みかん 5").Qty == 5 && P("みかん 5").Price == 0, "数字 1 つで 100 未満は数");
        Check(P("ビール ６缶 １，２８０円").Qty == 6 && P("ビール ６缶 １，２８０円").Unit == "缶" && P("ビール ６缶 １，２８０円").Price == 1280, "全角の数字・カンマつきの値段");
        Check(P("お米 5kg 2,480円").Qty == 5 && P("お米 5kg 2,480円").Unit == "kg" && P("お米 5kg 2,480円").Price == 2480, "5kg と 2,480円");
        Check(P("鶏もも肉 @398 x2").Price == 398 && P("鶏もも肉 @398 x2").Qty == 2, "@値段 と x数");
        Check(P("ペン 3x").Qty == 3, "3x も数");
        var many = S.ShopLogic.Parse("卵、パン, トマト3個\nビール 1,280円;豆腐");
        Check(many.Count == 5 && many[0].Name == "卵" && many[2].Qty == 3 && many[3].Price == 1280 && many[4].Name == "豆腐", "「、」「,」改行「;」で区切ってまとめて追加 (1,280円 のカンマは区切らない)");
        Check(S.ShopLogic.Parse("  、 , ").Count == 0 && S.ShopLogic.Parse(null).Count == 0, "空の入力は何も足さない");
        Check(S.ShopLogic.Parse("#日用品").Count == 0, "名前のないものは足さない");
        Check(S.ShopLogic.GuessAisle("低脂肪牛乳") == "乳製品・卵" && S.ShopLogic.GuessAisle("鶏もも肉") == "肉・魚" && S.ShopLogic.GuessAisle("トイレットペーパー") == "日用品"
            && S.ShopLogic.GuessAisle("シャンプー") == "化粧品" && S.ShopLogic.GuessAisle("謎のもの") == "その他", "名前から売り場を推測 (長い言葉を優先)");
        Check(S.ShopLogic.GuessAisle("豆乳") == "飲み物", "「豆乳」は飲み物 (「乳」より長い言葉)");
        Check(S.ShopLogic.MatchAisle("おもちゃ") == "おもちゃ" && S.ShopLogic.MatchAisle("") == "その他", "知らない売り場はそのまま・空は「その他」");
    }

    // ---------------------------------------------------------------- 買い物リスト
    public static void RunShop(Action<bool, string> Check)
    {
        SC.AppInfo.Init("TealWallet", "Teal Wallet", "");
        var today = D(2026, 10, 28);
        var d = S.ShopData.CreateDefault();
        d.Normalize();
        Check(d.Lists.Count == 2 && d.Lists[0].Name == "スーパー" && d.Lists[1].Name == "ドラッグストア", "はじめはスーパーとドラッグストアのリスト");
        var sp = d.Lists[0];
        var added = S.ShopLogic.Add(d, sp, "牛乳 2 198円、にんじん 3本 128円、ティッシュ #日用品、謎のもの", today);
        Check(added.Count == 4 && sp.Items.Count == 4 && sp.Items[0].Category == "乳製品・卵" && sp.Items[1].Category == "野菜・果物" && sp.Items[3].Category == "その他", "まとめて追加して売り場を推測");
        var again = S.ShopLogic.Add(d, sp, "牛乳", today);
        Check(again.Count == 1 && sp.Items.Count == 4 && sp.Items[0].Qty == 3, "同じ名前 (まだカゴに入れていない) は数を足してまとめる");
        var ord = S.ShopLogic.Ordered(sp);
        Check(ord.Select(i => i.Name).SequenceEqual(new[] { "にんじん", "牛乳", "ティッシュ", "謎のもの" }), "売り場の順に並ぶ (野菜 → 乳製品 → 日用品 → その他)");
        var milk = sp.Items.First(i => i.Name == "牛乳");
        var carrot = sp.Items.First(i => i.Name == "にんじん");
        S.ShopLogic.SetChecked(milk, true);
        System.Threading.Thread.Sleep(2);
        S.ShopLogic.SetChecked(carrot, true);
        ord = S.ShopLogic.Ordered(sp);
        Check(ord.Select(i => i.Name).SequenceEqual(new[] { "ティッシュ", "謎のもの", "牛乳", "にんじん" }), "チェックしたものは下へ (入れた順)");
        var t = S.ShopLogic.Totals(sp);
        Check(t.Items == 4 && t.Checked == 2 && t.Estimate == 198 * 3 + 128 * 3 && t.CartEstimate == 198 * 3 + 128 * 3 && t.Remaining == 0 && t.Unknown == 2, "予想の合計・カゴ・残り・値段のわからないもの (" + t.Estimate + ")");
        milk.Actual = 500;
        t = S.ShopLogic.Totals(sp);
        Check(t.CartPaid == 500 + 384 && t.CartEstimate == 594 + 384, "実際の金額を入れると、カゴの金額は実際の金額で");
        S.ShopLogic.SetChecked(milk, false);
        Check(!milk.Checked && milk.Actual == null && milk.CheckedAt == 0, "チェックを外すと実際の金額も消える");
        S.ShopLogic.SetChecked(milk, true);
        var text = S.ShopLogic.ToText(sp, today);
        Check(text.StartsWith("【スーパー】 10/28(水)\r\n") && text.Contains("■ 日用品\r\n□ ティッシュ\r\n") && text.Contains("■ カゴに入れた\r\n✓ にんじん 3本  ¥384\r\n") && text.Contains("予想の合計 ¥978 (値段のわからないもの 2 品を除く)"),
            "テキストでコピー (売り場ごと・カゴは ✓)\n" + text);
        Check(S.ShopLogic.Finish(d, d.Lists[1], 100, today) == null && d.History.Count == 0, "カゴが空なら記録しない");
        var p = S.ShopLogic.Finish(d, sp, 950, today);
        Check(p != null && p.Total == 950 && p.Estimate == 978 && p.Lines.Count == 2 && p.List == "スーパー" && p.Date == "2026-10-28" && sp.Items.Count == 2 && d.History.Count == 1, "買い物を終える: カゴの中を記録してリストから消す (まだのものは残る)");
        Check(p.Lines.First(l => l.Name == "牛乳").Price == 198 && p.Lines.First(l => l.Name == "にんじん").Unit == "本", "記録の 1 つの値段・単位");
        var p2 = S.ShopLogic.Finish(d, sp, null, today);
        Check(p2 == null, "2 回目はカゴが空");
        S.ShopLogic.SetChecked(sp.Items[0], true);
        Check(S.ShopLogic.ClearChecked(sp) == 1 && sp.Items.Count == 1 && d.History.Count == 1, "チェック済みを消す (記録はしない)");

        // 前に買ったときの値段・売り場を使う
        var milk2 = S.ShopLogic.Add(d, sp, "牛乳", today)[0];
        Check(milk2.Price == 198 && milk2.Category == "乳製品・卵" && milk2.Qty == 1, "前に買ったときの値段・売り場で追加");
        var tissue = S.ShopLogic.Add(d, sp, "にんじん", today)[0];
        Check(tissue.Unit == "本" && tissue.Price == 128, "前の単位も使う");
        // よく買うもの
        var sample = S.ShopLogic.Sample(today);
        sample.Normalize();
        var freq = S.ShopLogic.FrequentItems(sample, new[] { "牛乳" }, 3, "スーパー");
        Check(freq.Count == 3 && freq[0].Count == 4 && !freq.Any(x => x.Name == "牛乳") && freq.All(x => x.Count >= 3), "よく買うもの (回数の多い順・いまリストにあるものは除く): " + string.Join(",", freq.Select(x => x.Name + x.Count)));
        var drugFreq = S.ShopLogic.FrequentItems(sample, null, 10, "ドラッグストア");
        Check(drugFreq.Count == 2 && drugFreq.Any(x => x.Name == "ティッシュ"), "リストごとのよく買うもの");
        var yog = S.ShopLogic.FrequentItems(sample, null, 20).First(x => x.Name == "ヨーグルト");
        var added2 = S.ShopLogic.AddFrequent(sample.Lists[0], yog, today);
        Check(added2.Name == "ヨーグルト" && added2.Price == 138 && added2.Category == "乳製品・卵" && sample.Lists[0].Items.Contains(added2), "よく買うものを 1 クリックで追加");
        Check(S.ShopLogic.AddFrequent(sample.Lists[0], yog, today) == added2 && sample.Lists[0].Items.Count(i => i.Name == "ヨーグルト") == 1, "同じものは 2 回足さない");

        // 月の支出と予算
        var md = new S.ShopData(); md.Normalize();
        md.History.Add(new S.Purchase { Date = "2026-10-03", Estimate = 3000, Total = 2800 });
        md.History.Add(new S.Purchase { Date = "2026-10-20", Estimate = 1000, Total = 1200 });
        md.History.Add(new S.Purchase { Date = "2026-09-30", Estimate = 5000, Total = 5000 });
        Check(S.ShopLogic.MonthSpend(md, today) == 4000 && S.ShopLogic.MonthSpend(md, D(2026, 9, 1)) == 5000, "月ごとの買い物の合計");
        Check(S.ShopLogic.MonthDiff(md, today) == 0, "予想との差 (-200 + 200 = 0)");
        Check(S.ShopLogic.BudgetLeft(md, today) == null, "予算 0 は使わない");
        md.Settings.MonthlyBudget = 3500;
        Check(S.ShopLogic.BudgetLeft(md, today) == -500, "予算を超えたらマイナス");

        // 欲しいもの
        var w1 = new S.WantItem { Name = "イヤホン", Target = 12000, Price = 14800, PriorityText = "high" };
        var w2 = new S.WantItem { Name = "ケトル", Target = 4000, Price = 3980 };
        var w3 = new S.WantItem { Name = "毛布", PriorityText = "low" };
        var w4 = new S.WantItem { Name = "本棚", Target = 9000, Price = 9500, PriorityText = "normal" };
        md.Wants.AddRange(new[] { w3, w4, w1, w2 });
        var ow = S.ShopLogic.OrderedWants(md);
        Check(ow.Select(x => x.Name).SequenceEqual(new[] { "ケトル", "イヤホン", "本棚", "毛布" }), "買いどき (目標以下) → 優先度の順: " + string.Join(",", ow.Select(x => x.Name)));
        Check(S.ShopLogic.ToTarget(w1) == 2800 && S.ShopLogic.ToTarget(w2) == 0 && S.ShopLogic.ToTarget(w3) == null && w2.AtTarget && !w1.AtTarget, "目標まであといくら");
        Check(S.ShopLogic.WantTotal(md) == 14800 + 3980 + 9500, "まだ買っていないものの合計");
        Check(w1.Priority == S.WantPriority.High && w3.Priority == S.WantPriority.Low && new S.WantItem { PriorityText = "??" }.Priority == S.WantPriority.Normal, "優先度の読み取り");
        var bp = S.ShopLogic.BuyWant(md, w2, 3900, today);
        Check(w2.Bought && w2.Paid == 3900 && w2.BoughtOn == "2026-10-28" && bp.List == "欲しいもの" && S.ShopLogic.MonthSpend(md, today) == 7900, "欲しいものを買った: 今月の買い物に入る");
        Check(S.ShopLogic.OrderedWants(md).Last() == w2 && S.ShopLogic.WantTotal(md) == 14800 + 9500, "買ったものは最後・合計から外す");

        // 保存と読み込み (既定値の補い)
        var json = Encoding.UTF8.GetString(SC.DataStore.ToJson(sample));
        var back = SC.DataStore.FromJson<S.ShopData>(Encoding.UTF8.GetBytes(json));
        back.Normalize();
        Check(back.Lists.Count == 2 && back.Lists[0].Items.Count == sample.Lists[0].Items.Count && back.Wants.Count == 3 && back.History.Count == 5 && back.Settings.MonthlyBudget == 40000, "shopping.json に保存して読み戻す");
        var odd = SC.DataStore.FromJson<S.ShopData>(Encoding.UTF8.GetBytes("{\"lists\":[{\"name\":\"\",\"items\":[{\"name\":\"x\",\"qty\":0,\"price\":-5},null]}],\"wants\":null}"));
        odd.Normalize();
        Check(odd.Lists[0].Name == "リスト" && odd.Lists[0].Items.Count == 1 && odd.Lists[0].Items[0].Qty == 1 && odd.Lists[0].Items[0].Price == 0 && odd.Lists[0].Items[0].Category == "その他" && odd.Wants != null && odd.History != null && !string.IsNullOrEmpty(odd.Lists[0].Id),
            "壊れた・足りない値を補う");
        var empty = SC.DataStore.FromJson<S.ShopData>(Encoding.UTF8.GetBytes("{}"));
        empty.Normalize();
        Check(empty.Lists.Count == 1, "リストが 1 つもなければ 1 つ作る");
        var big = new S.ShopData();
        for (int i = 0; i < S.ShopData.MaxHistory + 30; i++) big.History.Add(new S.Purchase { Date = D(2020, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), Total = 1 });
        big.Normalize();
        Check(big.History.Count == S.ShopData.MaxHistory && big.History.All(h => string.CompareOrdinal(h.Date, "2020-01-31") >= 0), "履歴は新しい " + S.ShopData.MaxHistory + " 回ぶん");
        // サブスクのバックアップに買い物リストを入れる (1.8.0 のバックアップもそのまま読める)
        var sd = new S.SubData { Shopping = sample };
        var sdBack = SC.DataStore.FromJson<S.SubData>(SC.DataStore.ToJson(sd));
        Check(sdBack.Shopping != null && sdBack.Shopping.Wants.Count == 3, "バックアップ (JSON) に買い物リストも入る");
        var v18 = SC.DataStore.FromJson<S.SubData>(Encoding.UTF8.GetBytes("{\"version\":1,\"subs\":[],\"settings\":{},\"notified\":[]}"));
        Check(v18 != null && v18.Subs != null && v18.Shopping == null, "1.8.0 のバックアップ (買い物リストなし) も読める");
        Check(!Encoding.UTF8.GetString(SC.DataStore.ToJson(new S.SubData())).Contains("shopping"), "ふだんの subs.json には買い物リストを書かない");
    }

    // ---------------------------------------------------------------- 画面
    public static void RunShopUi(Action<bool, string> Check)
    {
        SC.AppInfo.Init("TealWallet", "Teal Wallet", "");
        var today = DateTime.Today;
        using (var f = new S.MainForm())
        {
            f.Shop = S.ShopLogic.Sample(today);
            f.Shop.Normalize();
            f.Data = new S.SubData();
            f.Data.Subs.AddRange(S.SubLogic.SampleData(today));
            f.CreateControl();
            f.RefreshAll();
            Check(f.Tabs.Count == 6 && f.Tabs[4] == "買い物リスト" && f.Tabs[5] == "欲しいもの" && f.Tabs[1] == "サブスク", "タブ: サブスク・買い物リスト・欲しいもの");
            f.Tabs.SelectedIndex = 4;
            Check(f.CurrentList.Name == "スーパー" && f.ShopRows.Items.Count > 8 && f.ShopRows.Items[0] is S.MainForm.AisleRow, "買い物リスト: 売り場の見出しつきで並ぶ");
            var heads = f.ShopRows.Items.OfType<S.MainForm.AisleRow>().ToList();
            Check(heads.Last().Cart && heads.Last().Count == 2, "最後に「カゴに入れた」(2 品)");
            int before = f.CurrentList.Items.Count;
            f.QuickAdd.Text = "卵 1パック 228、バナナ";
            Check(f.AddShopText(f.QuickAdd.Text) == 2 && f.CurrentList.Items.Count == before + 2 && f.QuickAdd.Text == "", "入力欄から 2 品追加して入力欄を空に");
            var egg = f.CurrentList.Items.First(i => i.Name == "卵");
            Check(f.ShopRows.SelectedItem == f.CurrentList.Items.First(i => i.Name == "バナナ"), "足した品を選ぶ");
            f.ToggleItem(egg);
            Check(egg.Checked && f.ShopRows.Items.IndexOf(egg) > f.ShopRows.Items.IndexOf(heads.Last().Title) , "チェックするとカゴへ");
            var cartIdx = f.ShopRows.Items.Cast<object>().ToList().FindIndex(o => o is S.MainForm.AisleRow && ((S.MainForm.AisleRow)o).Cart);
            Check(f.ShopRows.Items.IndexOf(egg) > cartIdx, "カゴの見出しより下に移る");
            var tot = S.ShopLogic.Totals(f.CurrentList);
            var p = f.FinishShopping(1000);
            Check(p != null && p.Total == 1000 && f.CurrentList.Items.All(i => !i.Checked) && f.Shop.History.Contains(p), "買い物を終える (金額を記録)");
            Check(f.TileShop.ValueText == SC.Fmt.Yen(S.ShopLogic.MonthSpend(f.Shop, today)), "ダッシュボードの「今月の買い物」に反映 (" + f.TileShop.ValueText + ")");
            Check(f.FooterText().Contains("今月の買い物"), "下の帯にも今月の買い物");
            Check(f.FinishShopping(1) == null, "カゴが空なら何もしない");
            f.SwitchList(1);
            Check(f.CurrentList.Name == "ドラッグストア" && f.ListPicker.SelectedIndex == 1, "Ctrl+PageDown でリストを切り替え");
            f.SwitchList(1);
            Check(f.CurrentList.Name == "スーパー", "最後の次は最初へ");
            var l3 = f.AddList("ホームセンター");
            Check(f.CurrentList == l3 && f.ListPicker.Items.Count == 3 && f.ShopRows.Items.Count == 0, "新しいリスト (空)");
            Check(f.ListText().StartsWith("【ホームセンター】") && f.ListText().Contains("(何もありません)"), "空のリストのテキスト");
            f.AddShopText("電池 #日用品");
            S.ShopLogic.SetChecked(f.CurrentList.Items[0], true);
            Check(f.ClearChecked(false) == 1 && f.CurrentList.Items.Count == 0, "チェック済みを消す");
            // 欲しいもの
            f.Tabs.SelectedIndex = 5;
            Check(f.WantList.Items.Count == 3 && ((S.WantItem)f.WantList.Items[0]).Name == "電気ケトル", "欲しいもの: 買いどきが先頭");
            var kettle = (S.WantItem)f.WantList.Items[0];
            long spendBefore = S.ShopLogic.MonthSpend(f.Shop, today);
            f.BuyWant(kettle, 3980);
            Check(kettle.Bought && f.WantList.Items.Count == 2 && S.ShopLogic.MonthSpend(f.Shop, today) == spendBefore + 3980, "買った: 一覧から外れて今月の買い物に入る");
            var ear = f.Shop.Wants.First(w => w.Name == "ワイヤレスイヤホン");
            var it = f.WantToList(ear);
            Check(f.CurrentList.Items.Contains(it) && it.Price == 14800, "欲しいものを買い物リストに入れる");
            f.AddWantItem(new S.WantItem { Name = "テスト" });
            Check(f.Shop.Wants.Count == 4 && f.WantList.SelectedItem is S.WantItem && ((S.WantItem)f.WantList.SelectedItem).Name == "テスト", "欲しいものを追加");
            f.DeleteWant(f.Shop.Wants.Last(), false);
            Check(f.Shop.Wants.Count == 3, "欲しいものを削除");
        }
        // 保存したファイルを読み直す
        string warn;
        var saved = SC.DataStore.LoadOrDefault(S.ShopData.FileName, S.ShopData.CreateDefault, out warn);
        Check(warn == null && saved.Lists.Count == 3 && saved.Wants.Count == 3 && saved.History.Count >= 7, "shopping.json に保存されている");
        // ダイアログ
        var item = new S.ShopItem { Name = "牛乳", Qty = 2, Price = 198, Category = "乳製品・卵" };
        using (var ed = new S.ShopItemForm(item, false))
        {
            ed.tbName.Text = "低脂肪牛乳"; ed.numQty.Value = 3; ed.cbAisle.Text = "乳製品"; ed.chkActual.Checked = true; ed.numActual.Value = 550;
            Check(ed.Commit() && item.Name == "低脂肪牛乳" && item.Qty == 3 && item.Category == "乳製品・卵" && item.Actual == 550, "品の編集ダイアログ");
            ed.tbName.Text = " ";
            Check(!ed.Commit(), "名前が空なら保存しない");
        }
        var want = new S.WantItem();
        using (var wf = new S.WantForm(want, true))
        {
            wf.tbName.Text = "カメラ"; wf.numTarget.Value = 50000; wf.numPrice.Value = 60000; wf.cbPriority.SelectedIndex = 0; wf.tbUrl.Text = "ftp://x";
            Check(!wf.Commit(), "URL は http(s) だけ");
            wf.tbUrl.Text = "https://example.com/camera";
            Check(wf.Commit() && want.Name == "カメラ" && want.Priority == S.WantPriority.High && want.Target == 50000 && want.Url == "https://example.com/camera", "欲しいものの編集ダイアログ");
        }
        var hist = S.ShopLogic.FrequentItems(S.ShopLogic.Sample(today), null, 50);
        using (var hf = new S.HistoryAddForm(hist, "スーパー"))
        {
            hf.Pick("牛乳"); hf.Pick("ティッシュ");
            hf.tbSearch.Text = "牛";
            Check(hf.List.Items.Count == 1 && hf.Picked.Count == 2, "履歴から追加: 絞り込んでも選んだものは残る");
        }
    }

    // ---------------------------------------------------------------- 名前の変更 (Teal Sub → Teal Wallet)
    public static void RunRename(Action<bool, string> Check, string tmp)
    {
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var oldLegacy = Environment.GetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR");
        var root = Path.Combine(tmp, "v190-data");
        try
        {
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", root);
            Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", Path.Combine(tmp, "v190-legacy"));
            // データ: %APPDATA%\TealSuite\TealSub\ → TealWallet\
            Directory.CreateDirectory(Path.Combine(root, "TealSub"));
            File.WriteAllText(Path.Combine(root, "TealSub", "subs.json"), "{\"version\":1,\"subs\":[{\"id\":\"a\",\"name\":\"動画\",\"amount\":990}],\"settings\":{\"budget\":18000}}");
            SC.AppInfo.Init(new SC.AppSpec { Id = "TealWallet", Name = "Teal Wallet", LegacyIds = new[] { "TealSub" } });
            var from = SC.DataStore.MigrateLegacyData();
            Check(from != null && Path.GetFileName(from) == "TealSub" && File.Exists(Path.Combine(root, "TealWallet", "subs.json")), "1.8.0 の TealSub フォルダのデータを TealWallet にコピー");
            Check(File.Exists(Path.Combine(root, "TealSub", "subs.json")), "元のフォルダは消さない (1.8.0 に戻しても使える)");
            string w;
            var sd = SC.DataStore.LoadOrDefault(S.SubData.FileName, () => new S.SubData(), out w);
            Check(w == null && sd.Subs.Count == 1 && sd.Subs[0].Name == "動画", "引き継いだサブスクを読める");
            File.WriteAllText(Path.Combine(root, "TealSub", "subs.json"), "{\"changed\":1}");
            Check(SC.DataStore.MigrateLegacyData() == null && !File.ReadAllText(Path.Combine(root, "TealWallet", "subs.json")).Contains("changed"), "2 回目は何もしない (新しいデータを上書きしない)");

            // Hub の設定 (ショートカット・常駐)
            var pf = new SC.AppPrefsFile();
            pf.Apps.Add(new SC.AppPref { Id = "TealMemo", Hotkey = "Ctrl+Alt+M" });
            pf.Apps.Add(new SC.AppPref { Id = "TealSub", Hotkey = "Ctrl+Alt+S", Resident = false });
            Check(SC.RenameMigration.MigratePrefs(pf, "TealWallet", new[] { "TealSub" }) && pf.Find("TealSub") == null && pf.Find("TealWallet").Hotkey == "Ctrl+Alt+S" && pf.Find("TealWallet").Resident == false && pf.Apps.Count == 2,
                "ショートカット・常駐の設定を TealWallet に付け替える");
            Check(!SC.RenameMigration.MigratePrefs(pf, "TealWallet", new[] { "TealSub" }), "2 回目は何もしない");
            var pf2 = new SC.AppPrefsFile();
            pf2.Apps.Add(new SC.AppPref { Id = "TealWallet", Resident = true });
            pf2.Apps.Add(new SC.AppPref { Id = "TealSub", Hotkey = "Ctrl+Alt+S", Resident = false });
            Check(SC.RenameMigration.MigratePrefs(pf2, "TealWallet", new[] { "TealSub" }) && pf2.Apps.Count == 1 && pf2.Find("TealWallet").Hotkey == "Ctrl+Alt+S" && pf2.Find("TealWallet").Resident == true,
                "新しい項目があれば、決まっていない値だけ補う (常駐は新しい方)");
            // アプリから見た設定 (Hub がまだ付け替えていないとき)
            var prefsPath = SC.AppPrefs.FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(prefsPath));
            File.WriteAllText(prefsPath, "{\"version\":1,\"apps\":[{\"id\":\"TealSub\",\"hotkey\":\"Ctrl+Alt+W\",\"resident\":false}]}");
            Check(SC.AppPrefs.Get("TealWallet") != null && SC.AppPrefs.HotkeyOf("TealWallet") == "Ctrl+Alt+W" && !SC.AppPrefs.IsResident("TealWallet", true), "Hub が付け替える前でも、アプリは旧 ID の設定を使う");

            // スタートアップ (HKCU\…\Run)
            var exe = Path.Combine(tmp, "v190-suite", "apps", "TealWallet", "TealWallet.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, "exe");
            var key = new SC.MemoryRunKey();
            SC.Startup.RunKey = key;
            key.Values["TealSuite.TealSub"] = "\"C:\\TealSuite\\apps\\TealSub\\TealSub.exe\" --startup";
            Check(SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe) && key.Get("TealSuite.TealSub") == null && key.Get("TealSuite.TealWallet") == SC.Startup.CommandFor(exe), "スタートアップを TealWallet.exe で登録し直す");
            Check(!SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe) && key.Get("TealSuite.TealWallet") != null, "2 回目は何もしない");
            key.Values.Clear();
            key.Values["TealSuite.TealSub"] = "\"C:\\old\\TealSub.exe\" --startup"; key.Disabled.Add("TealSuite.TealSub");
            Check(SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe) && key.Values.Count == 0 && key.Disabled.Count == 0, "タスク マネージャーで無効にしていたら、登録し直さない (オフのまま)");
            key.Values["TealSuite.TealSub"] = "\"C:\\old\\TealSub.exe\" --startup";
            Check(!SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe + ".missing") && key.Get("TealSuite.TealSub") != null, "新しい exe がまだないときは触らない");
            Check(!SC.RenameMigration.MigrateStartup("TealWallet", "TealWallet", exe), "同じ ID は何もしない");
            key.Values.Clear();
            Check(!SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe) && key.Values.Count == 0, "スタートアップに入っていなければ何もしない");
            var fileKey = Path.Combine(tmp, "v190-runkey.txt");
            File.WriteAllText(fileKey, "TealSuite.TealSub\t\"C:\\old\\TealSub.exe\" --startup\n");
            SC.Startup.RunKey = new SC.FileRunKey(fileKey);
            Check(SC.RenameMigration.MigrateStartup("TealSub", "TealWallet", exe) && File.ReadAllText(fileKey).Contains("TealSuite.TealWallet") && !File.ReadAllText(fileKey).Contains("TealSuite.TealSub"), "ファイルの Run キーでも同じ");
            SC.Startup.RunKey = key;
            key.Values["TealSuite.TealSub"] = "\"C:\\old\\TealSub.exe\" --startup";
            SC.AppInfo.Init(new SC.AppSpec { Id = "TealWallet", Name = "Teal Wallet", LegacyIds = new[] { "TealSub" } });
            SC.RenameMigration.RunForSelf();
            Check(key.Get("TealSuite.TealSub") == null, "アプリ自身の起動時にも直す (exe = 自分)");

            // Teal Hub: apps.json の legacyIds から引き継ぐ
            HC.AppInfo.Init("TealHub", "Teal Hub", "");
            File.WriteAllText(prefsPath, "{\"version\":1,\"apps\":[{\"id\":\"TealMemo\",\"hotkey\":\"Ctrl+Alt+M\"},{\"id\":\"TealSub\",\"hotkey\":\"Ctrl+Alt+S\",\"resident\":true}]}");
            var hkey = new HC.MemoryRunKey();
            HC.Startup.RunKey = hkey;
            hkey.Values["TealSuite.TealSub"] = "\"C:\\old\\TealSub.exe\" --startup";
            var apps = new List<H.AppEntry>
            {
                new H.AppEntry { Id = "TealMemo", Name = "Teal Memo" },
                new H.AppEntry { Id = "TealWallet", Name = "Teal Wallet", LegacyIds = new[] { "TealSub" }, ExePath = exe },
            };
            var done = H.HubMigration.Run(apps);
            var prefs = HC.AppPrefs.Load();
            Check(done.Count == 1 && done[0] == "Teal Sub → Teal Wallet" && prefs.Find("TealSub") == null && prefs.Find("TealWallet").Hotkey == "Ctrl+Alt+S" && prefs.Find("TealMemo").Hotkey == "Ctrl+Alt+M", "Hub: 設定ファイルを付け替えて保存");
            Check(hkey.Get("TealSuite.TealWallet") != null && hkey.Get("TealSuite.TealSub") == null, "Hub: スタートアップも付け替える");
            Check(H.HubMigration.Notice(done) == "Teal Sub を Teal Wallet に引き継ぎました (ショートカット・常駐・スタートアップ)" && H.HubMigration.Notice(new List<string>()) == null, "Hub の下の帯に出すお知らせ");
            Check(H.HubMigration.Run(apps).Count == 0, "Hub: 2 回目は何もしない");
            File.Delete(prefsPath);
            hkey.Values["TealSuite.TealSub"] = "x";
            Check(H.HubMigration.Run(apps).Count == 1 && !File.Exists(prefsPath), "設定ファイルがなければ作らない (スタートアップだけ直す)");

            // バックアップの復元: 1.8.0 のバックアップ (TealSub/) は TealWallet/ に戻す
            var src = Path.Combine(tmp, "v190-src");
            Directory.CreateDirectory(Path.Combine(src, "TealSub"));
            Directory.CreateDirectory(Path.Combine(src, "TealMemo"));
            File.WriteAllText(Path.Combine(src, "TealSub", "subs.json"), "{\"old\":1}");
            File.WriteAllText(Path.Combine(src, "TealMemo", "notes.json"), "{}");
            var zip = Path.Combine(tmp, "v190-backup-180.zip");
            H.HubBackup.Create(src, zip, "1.8.0", D(2026, 10, 1));
            var info = H.HubBackup.Inspect(zip);
            var renames = new Dictionary<string, string> { { "TealSub", "TealWallet" } };
            var map = H.HubBackup.FolderMap(info, renames);
            Check(map.Count == 1 && map["TealSub"] == "TealWallet", "1.8.0 のバックアップ: TealSub → TealWallet に付け替える");
            var dst = Path.Combine(tmp, "v190-dst");
            Directory.CreateDirectory(Path.Combine(dst, "TealWallet"));
            File.WriteAllText(Path.Combine(dst, "TealWallet", "shopping.json"), "{\"keep\":1}");
            int n = H.HubBackup.Restore(zip, dst, null, "1.9.0", D(2026, 10, 28), renames);
            Check(n == 2 && File.ReadAllText(Path.Combine(dst, "TealWallet", "subs.json")) == "{\"old\":1}" && !Directory.Exists(Path.Combine(dst, "TealSub")), "TealWallet フォルダに戻す (TealSub フォルダは作らない)");
            Check(File.ReadAllText(Path.Combine(dst, "TealWallet", "shopping.json")) == "{\"keep\":1}", "買い物リスト (バックアップにないファイル) は消さない");
            Directory.CreateDirectory(Path.Combine(src, "TealWallet"));
            File.WriteAllText(Path.Combine(src, "TealWallet", "subs.json"), "{\"new\":1}");
            var zip2 = Path.Combine(tmp, "v190-backup-both.zip");
            H.HubBackup.Create(src, zip2, "1.9.0", D(2026, 10, 28));
            Check(H.HubBackup.FolderMap(H.HubBackup.Inspect(zip2), renames).Count == 0, "TealWallet/ があるバックアップは付け替えない");
            Check(H.HubBackup.FolderMap(info, null).Count == 0, "付け替え表がなければ何もしない");
            int n2 = H.HubBackup.Restore(zip, Path.Combine(tmp, "v190-dst2"), null, "1.9.0", D(2026, 10, 28));
            Check(n2 == 2 && File.Exists(Path.Combine(tmp, "v190-dst2", "TealSub", "subs.json")), "前の書き方 (付け替えなし) もそのまま使える");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
            Environment.SetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR", oldLegacy);
            SC.Startup.RunKey = new SC.MemoryRunKey();
            HC.Startup.RunKey = new HC.MemoryRunKey();
        }
    }

    // ---------------------------------------------------------------- アップデート (apps.json の付け替え・古い exe の削除)
    public static void RunUpdate(Action<bool, string> Check, string distDir)
    {
        var installed = "{\"manifestVersion\":1,\"suite\":\"TealSuite\",\"suiteVersion\":\"1.8.0\",\"apps\":[" +
            "{\"id\":\"TealHub\",\"name\":\"Teal Hub\",\"exe\":\"TealHub.exe\",\"version\":\"1.8.0\",\"self\":true}," +
            "{\"id\":\"TealSub\",\"name\":\"Teal Sub\",\"exe\":\"apps/TealSub/TealSub.exe\",\"version\":\"1.8.0\",\"resident\":true,\"myNote\":\"利用者のメモ\"}," +
            "{\"id\":\"TealTasks\",\"name\":\"Teal Tasks\",\"exe\":\"apps/TealTasks/TealTasks.exe\",\"version\":\"1.8.0\"}]}";
        var manifest = "{\"format\":1,\"suite\":\"TealSuite\",\"version\":\"1.9.0\",\"minFromVersion\":\"1.8.0\",\"notes\":\"t\",\"files\":[],\"delete\":[\"apps/TealSub/TealSub.exe\",\"apps/TealSub/TealSub.ico\"],\"apps\":[" +
            "{\"id\":\"TealHub\",\"name\":\"Teal Hub\",\"exe\":\"TealHub.exe\",\"version\":\"1.9.0\",\"self\":true}," +
            "{\"id\":\"TealWallet\",\"name\":\"Teal Wallet\",\"exe\":\"apps/TealWallet/TealWallet.exe\",\"version\":\"1.9.0\",\"legacyIds\":[\"TealSub\"],\"resident\":true}," +
            "{\"id\":\"TealTasks\",\"name\":\"Teal Tasks\",\"exe\":\"apps/TealTasks/TealTasks.exe\",\"version\":\"1.9.0\"}]}";
        var pkg = U.UpdatePackage.Parse(manifest);
        var merged = U.UpdateEngine.MergeAppsJson(U.Json.ParseObj(installed), pkg);
        var apps = merged.Arr("apps").Cast<U.JObj>().ToList();
        Check(string.Join(",", apps.Select(a => a.Str("id"))) == "TealHub,TealWallet,TealTasks", "apps.json: Teal Sub の場所に Teal Wallet (増えない・順番そのまま): " + string.Join(",", apps.Select(a => a.Str("id"))));
        var wal = apps.First(a => a.Str("id") == "TealWallet");
        Check(wal.Str("exe") == "apps/TealWallet/TealWallet.exe" && wal.Str("name") == "Teal Wallet" && wal.Str("version") == "1.9.0", "Teal Wallet の項目");
        Check(pkg.Delete.Contains("apps/TealSub/TealSub.exe"), "古い TealSub.exe は削除するファイルに入る");

        // 実際に作ったアップデート ファイル (あれば)
        var real = Path.Combine(distDir, "..", "TealSuite-1.9.0.tealupdate");
        if (File.Exists(real))
        {
            var rp = U.UpdatePackage.Read(real);
            Check(rp.Version == "1.9.0" && rp.Delete.Any(x => x == "apps/TealSub/TealSub.exe") && rp.Files.Any(x => x.Path == "apps/TealWallet/TealWallet.exe")
                && rp.Apps.Any(a => a.Str("id") == "TealWallet" && a.Strs("legacyIds").Contains("TealSub")) && !rp.Apps.Any(a => a.Str("id") == "TealSub"), "TealSuite-1.9.0.tealupdate: TealWallet を入れて TealSub.exe を消す");
            // 1.8.0 の形のフォルダに実際に当てる (TealSub.exe と空になった apps/TealSub が消え、apps.json は Teal Wallet 1 件)
            var suite = Path.Combine(Path.GetTempPath(), "tealsuite-v190-apply-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            try { if (Directory.Exists(suite)) Directory.Delete(suite, true); } catch { }
            Directory.CreateDirectory(Path.Combine(suite, "apps", "TealSub"));
            File.WriteAllText(Path.Combine(suite, "apps", "TealSub", "TealSub.exe"), "old");
            File.WriteAllText(Path.Combine(suite, "apps", "TealSub", "TealSub.ico"), "old");
            File.WriteAllText(Path.Combine(suite, "apps.json"), installed);
            var res = new U.UpdateEngine(suite).Apply(rp);
            var after = File.ReadAllText(Path.Combine(suite, "apps.json"));
            var afterIds = U.Json.ParseObj(after).Arr("apps").Cast<U.JObj>().Select(x => x.Str("id")).ToList();
            Check(res.Ok && File.Exists(Path.Combine(suite, "apps", "TealWallet", "TealWallet.exe")) && !Directory.Exists(Path.Combine(suite, "apps", "TealSub"))
                && afterIds.Count(x => x == "TealWallet") == 1 && !afterIds.Contains("TealSub") && afterIds.Count == 7 && afterIds[1] == "TealWallet" && after.Contains("利用者のメモ"),
                "1.8.0 のフォルダに当てる: TealWallet.exe が入り apps/TealSub は消える" + (res.Ok ? "" : " (" + res.Error + ")"));
            try { Directory.Delete(suite, true); } catch { }
        }
        // 同梱の apps.json と CHANGELOG
        var aj = File.ReadAllText(Path.Combine(distDir, "apps.json"));
        Check(aj.Contains("\"id\": \"TealWallet\"") && aj.Contains("\"legacyIds\": [\"TealSub\"]") && !aj.Contains("\"id\": \"TealSub\"") && !aj.Contains("Teal Sub\""), "apps.json: Teal Wallet (旧 ID TealSub)");
        Check(File.Exists(Path.Combine(distDir, "apps", "TealWallet", "TealWallet.exe")) && !Directory.Exists(Path.Combine(distDir, "apps", "TealSub")), "TealWallet.exe があり、TealSub フォルダはない");
        var cl = Path.Combine(distDir, "CHANGELOG.md");
        if (File.Exists(cl))
            Check(H.ChangeLog.SectionFor(H.ChangeLog.Parse(File.ReadAllText(cl)), "1.9.0") != null, "同梱の CHANGELOG.md に 1.9.0 の節がある");
    }
}
