// Teal Wallet (サブスク＆買い物リスト) - 1.9.0: 買い物リストの計算 (画面なし。tests/V190Tests.cs で確かめる)
//  ・すばやく追加: 「牛乳 2 198円」→ 牛乳 × 2、1 つ 198 円。「、」で区切ると何品でも
//  ・売り場 (カテゴリ) を名前から推測、前に買ったときの売り場・値段を優先
//  ・チェックしたものは下へ。予想の合計と実際の金額
//  ・よく買うもの・履歴から追加、テキストでコピー、月の支出 (ダッシュボードと予算)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TealSuite.Common;

namespace TealSuite.Wallet
{
    public class ParsedItem
    {
        public string Name = "";
        public int Qty = 1;
        public string Unit;
        public long Price;
        public string Category;   // null = 推測する
        public bool HasQty, HasPrice;
    }

    /// <summary>よく買うもの (履歴から)</summary>
    public class Frequent
    {
        public string Name, Category, Unit;
        public int Count, Qty = 1;
        public long LastPrice;
        public DateTime? Last;
    }

    /// <summary>リストの合計</summary>
    public class ShopTotals
    {
        public int Items, Checked;
        /// <summary>全部の予想の合計 / まだカゴに入れていないものの予想 / カゴの中 (予想) / カゴの中 (実際)</summary>
        public long Estimate, Remaining, CartEstimate, CartPaid;
        /// <summary>値段がわからない品の数</summary>
        public int Unknown;
    }

    public static class ShopLogic
    {
        /// <summary>売り場 (お店の中を回る順)</summary>
        public static readonly string[] Aisles =
        {
            "野菜・果物", "肉・魚", "乳製品・卵", "豆腐・練り物", "パン・シリアル", "米・麺", "冷凍食品", "調味料・乾物", "お惣菜",
            "飲み物", "お酒", "お菓子", "日用品", "掃除・洗濯", "薬・衛生用品", "化粧品", "ベビー・ペット", "その他",
        };

        static readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>
        {
            { "野菜・果物", new[] { "キャベツ", "にんじん", "人参", "玉ねぎ", "たまねぎ", "玉葱", "じゃがいも", "ジャガイモ", "トマト", "きゅうり", "レタス", "ねぎ", "ネギ", "白菜", "大根", "ほうれん草", "ブロッコリー", "なす", "ピーマン", "もやし", "しめじ", "えのき", "きのこ", "しいたけ", "ごぼう", "かぼちゃ", "さつまいも", "生姜", "しょうが", "にんにく", "バナナ", "りんご", "リンゴ", "みかん", "いちご", "ぶどう", "レモン", "キウイ", "アボカド", "果物", "野菜", "サラダ" } },
            { "肉・魚", new[] { "豚", "牛肉", "鶏", "とり", "ひき肉", "挽肉", "ベーコン", "ハム", "ウインナー", "ソーセージ", "肉", "鮭", "さけ", "サーモン", "まぐろ", "マグロ", "さば", "サバ", "あじ", "いか", "えび", "エビ", "たこ", "刺身", "魚", "しらす", "ちくわ" } },
            { "乳製品・卵", new[] { "牛乳", "ミルク", "ヨーグルト", "チーズ", "バター", "マーガリン", "生クリーム", "卵", "たまご", "玉子" } },
            { "豆腐・練り物", new[] { "豆腐", "とうふ", "納豆", "油揚げ", "厚揚げ", "こんにゃく", "かまぼこ", "はんぺん" } },
            { "パン・シリアル", new[] { "パン", "食パン", "ロール", "シリアル", "グラノーラ", "ベーグル" } },
            { "米・麺", new[] { "米", "ごはん", "パックご飯", "うどん", "そば", "そうめん", "パスタ", "スパゲッティ", "ラーメン", "焼きそば", "麺" } },
            { "冷凍食品", new[] { "冷凍", "アイス", "餃子", "ぎょうざ" } },
            { "調味料・乾物", new[] { "醤油", "しょうゆ", "味噌", "みそ", "塩", "砂糖", "酢", "みりん", "料理酒", "油", "オリーブオイル", "マヨネーズ", "ケチャップ", "ソース", "ドレッシング", "だし", "コンソメ", "カレー", "ルー", "小麦粉", "片栗粉", "胡椒", "こしょう", "わかめ", "のり", "海苔", "ふりかけ", "缶詰", "ツナ" } },
            { "お惣菜", new[] { "惣菜", "弁当", "おにぎり", "コロッケ", "唐揚げ", "からあげ", "寿司" } },
            { "飲み物", new[] { "お茶", "麦茶", "緑茶", "水", "ジュース", "コーヒー", "紅茶", "炭酸", "豆乳", "スポーツドリンク" } },
            { "お酒", new[] { "ビール", "ワイン", "日本酒", "焼酎", "チューハイ", "ハイボール" } },
            { "お菓子", new[] { "お菓子", "チョコ", "クッキー", "ポテチ", "ポテトチップス", "せんべい", "ガム", "飴", "グミ" } },
            { "日用品", new[] { "ティッシュ", "トイレットペーパー", "キッチンペーパー", "ラップ", "アルミホイル", "ゴミ袋", "電池", "スポンジ", "保存袋" } },
            { "掃除・洗濯", new[] { "洗剤", "柔軟剤", "漂白剤", "クリーナー", "掃除", "食器用" } },
            { "薬・衛生用品", new[] { "薬", "かぜ", "風邪", "目薬", "湿布", "絆創膏", "ばんそうこう", "マスク", "歯ブラシ", "歯磨き", "ハミガキ", "綿棒", "消毒", "ビタミン", "サプリ" } },
            { "化粧品", new[] { "シャンプー", "リンス", "コンディショナー", "ボディソープ", "石けん", "石鹸", "化粧水", "乳液", "日焼け止め", "洗顔", "ハンドクリーム", "リップ" } },
            { "ベビー・ペット", new[] { "おむつ", "オムツ", "おしりふき", "ミルク缶", "ペット", "猫砂", "ドッグフード", "キャットフード" } },
        };

        static readonly string[] Units = { "個", "本", "袋", "パック", "枚", "缶", "箱", "玉", "束", "株", "房", "切れ", "尾", "丁", "瓶", "kg", "g", "L", "ml", "セット", "ロール", "つ" };

        /// <summary>全角の数字・記号を半角に</summary>
        public static string Narrow(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= '０' && c <= '９') sb.Append((char)('0' + (c - '０')));
                else if (c == '\u3000') sb.Append(' ');
                else if (c == '，') sb.Append(',');
                else if (c == '＃') sb.Append('#');
                else if (c == '＠') sb.Append('@');
                else if (c == '＊' || c == '×' || c == 'Ｘ' || c == 'ｘ') sb.Append('x');
                else if (c == '￥') sb.Append('¥');
                else if (c == 'ｇ') sb.Append('g');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static readonly Regex RxPrice = new Regex(@"^(?:[¥\\@](?<n>\d[\d,]*)円?|(?<n>\d[\d,]*)円)$");
        static readonly Regex RxQtyX = new Regex(@"^(?:x(?<n>\d{1,3})|(?<n>\d{1,3})x)$", RegexOptions.IgnoreCase);
        static readonly Regex RxQtyUnit = new Regex(@"^(?<n>\d{1,3})(?<u>" + string.Join("|", Units.Select(Regex.Escape)) + ")$");
        static readonly Regex RxNum = new Regex(@"^\d[\d,]*$");
        static readonly Regex RxGlued = new Regex(@"^(?<name>.*?[^\d\s,])(?<n>\d{1,3})(?<u>" + string.Join("|", Units.Select(Regex.Escape)) + ")$");

        /// <summary>
        /// 1 品ぶんの文字を読む。例:
        ///   「牛乳 2 198円」「牛乳 ×2 ¥198」「卵 1パック 228」「トマト3個」「ティッシュ #日用品」
        /// 数字だけのときは 100 未満を数、100 以上を値段とみなす (「円」「¥」をつけると確実)。
        /// </summary>
        public static ParsedItem ParseOne(string text)
        {
            var p = new ParsedItem();
            var tokens = Narrow(text).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var name = new List<string>();
            var plain = new List<long>();
            foreach (var raw in tokens)
            {
                var t = raw.Trim();
                Match m;
                if (t.StartsWith("#") && t.Length > 1) { p.Category = MatchAisle(t.Substring(1)); continue; }
                if ((m = RxPrice.Match(t)).Success) { long v; if (ParseLong(m.Groups["n"].Value, out v)) { p.Price = v; p.HasPrice = true; } continue; }
                if ((m = RxQtyX.Match(t)).Success) { p.Qty = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture); p.HasQty = true; continue; }
                if ((m = RxQtyUnit.Match(t)).Success) { p.Qty = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture); p.Unit = m.Groups["u"].Value; p.HasQty = true; continue; }
                if (RxNum.IsMatch(t) && name.Count > 0) { long v; if (ParseLong(t, out v)) plain.Add(v); continue; }
                // 「トマト3個」のように名前にくっついた数
                if (!p.HasQty && (m = RxGlued.Match(t)).Success)
                {
                    name.Add(m.Groups["name"].Value);
                    p.Qty = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture); p.Unit = m.Groups["u"].Value; p.HasQty = true;
                    continue;
                }
                name.Add(t);
            }
            // 残った数字: 2 つ以上なら「数 値段」の順、1 つなら 100 未満を数・100 以上を値段とみなす
            for (int k = 0; k < plain.Count; k++)
            {
                long v = plain[k];
                bool asQty = !p.HasQty && v >= 1 && v < 1000 && (plain.Count >= 2 ? k == 0 : (v < 100 || p.HasPrice));
                if (asQty) { p.Qty = (int)v; p.HasQty = true; continue; }
                if (!p.HasPrice) { p.Price = v; p.HasPrice = true; continue; }
                name.Add(v.ToString(CultureInfo.InvariantCulture));
            }
            p.Name = string.Join(" ", name).Trim();
            p.Qty = Math.Max(1, Math.Min(999, p.Qty));
            p.Price = Math.Max(0, Math.Min(10000000, p.Price));
            return p;
        }

        static bool ParseLong(string s, out long v) { return long.TryParse(s.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out v); }

        /// <summary>「、」「,」改行で区切った何品かを読む (名前のないものは捨てる)</summary>
        public static List<ParsedItem> Parse(string text)
        {
            var list = new List<ParsedItem>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            // 「1,280円」のカンマは区切りにしない (数字にはさまれたカンマは残す)
            var s = Regex.Replace(Narrow(text), @"(?<!\d),|,(?!\d)", "\n");
            foreach (var part in s.Split(new[] { '、', '\n', '\r', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = ParseOne(part);
                if (p.Name.Length > 0) list.Add(p);
            }
            return list;
        }

        /// <summary>入力された売り場の名前を、決まった売り場に寄せる (「野菜」→「野菜・果物」)。合わなければそのまま</summary>
        public static string MatchAisle(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "その他";
            var hit = Aisles.FirstOrDefault(a => a == s) ?? Aisles.FirstOrDefault(a => a.Split('・').Any(x => x == s)) ?? Aisles.FirstOrDefault(a => a.StartsWith(s, StringComparison.Ordinal));
            return hit ?? s;
        }

        /// <summary>名前から売り場を推測する (長い言葉を優先)</summary>
        public static string GuessAisle(string name)
        {
            if (string.IsNullOrEmpty(name)) return "その他";
            string best = null; int bestLen = 0;
            foreach (var kv in Words)
                foreach (var w in kv.Value)
                    if (w.Length > bestLen && name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { best = kv.Key; bestLen = w.Length; }
            return best ?? "その他";
        }

        static string Key(string name) { return Narrow(name ?? "").Trim().ToLowerInvariant(); }

        /// <summary>前に買ったときの記録 (新しい順で最初に見つかったもの)</summary>
        public static PurchaseLine LastBought(ShopData d, string name)
        {
            var k = Key(name);
            foreach (var h in d.History.OrderByDescending(h => h.Date, StringComparer.Ordinal))
            {
                var l = h.Lines.FirstOrDefault(x => Key(x.Name) == k);
                if (l != null) return l;
            }
            return null;
        }

        /// <summary>読んだ品をリストの品にする (売り場・値段・単位がなければ、前に買ったときか推測で埋める)</summary>
        public static ShopItem ToItem(ShopData d, ParsedItem p, DateTime today)
        {
            var last = LastBought(d, p.Name);
            var it = new ShopItem { Name = p.Name, Qty = p.Qty, Unit = p.Unit, Price = p.Price, Added = Fmt.Iso(today) };
            it.Category = p.Category ?? (last != null && !string.IsNullOrEmpty(last.Category) ? last.Category : GuessAisle(p.Name));
            if (!p.HasPrice && last != null) it.Price = last.Price;
            if (it.Unit == null && last != null && !p.HasQty) it.Unit = last.Unit;
            return it;
        }

        /// <summary>
        /// リストに足す。同じ名前でまだカゴに入れていない品があれば、数を足してまとめる。
        /// 足した (またはまとめた) 品を返す
        /// </summary>
        public static List<ShopItem> Add(ShopData d, ShopList list, string text, DateTime today)
        {
            var added = new List<ShopItem>();
            foreach (var p in Parse(text))
            {
                var it = ToItem(d, p, today);
                var same = list.Items.FirstOrDefault(x => !x.Checked && Key(x.Name) == Key(it.Name));
                if (same != null)
                {
                    same.Qty = Math.Min(999, same.Qty + (p.HasQty ? it.Qty : 1));
                    if (p.HasPrice) same.Price = it.Price;
                    if (p.Category != null) same.Category = it.Category;
                    added.Add(same);
                }
                else { list.Items.Add(it); added.Add(it); }
            }
            return added;
        }

        public static int AisleOrder(string c)
        {
            int i = Array.IndexOf(Aisles, c);
            return i < 0 ? Aisles.Length - 1 : i;   // 知らない売り場は「その他」の前後 (名前順)
        }

        /// <summary>まだの品 (売り場の順 → 足した順) と、カゴの中 (入れた順)</summary>
        public static List<ShopItem> Ordered(ShopList list)
        {
            var open = list.Items.Where(i => !i.Checked).Select((it, n) => new { it, n })
                .OrderBy(x => AisleOrder(x.it.Category)).ThenBy(x => x.it.Category, StringComparer.Ordinal).ThenBy(x => x.n).Select(x => x.it);
            var done = list.Items.Where(i => i.Checked).Select((it, n) => new { it, n }).OrderBy(x => x.it.CheckedAt).ThenBy(x => x.n).Select(x => x.it);
            return open.Concat(done).ToList();
        }

        public static ShopTotals Totals(ShopList list)
        {
            var t = new ShopTotals();
            foreach (var i in list.Items)
            {
                t.Items++;
                t.Estimate += i.Estimate;
                if (i.Price == 0 && !i.Actual.HasValue) t.Unknown++;
                if (i.Checked) { t.Checked++; t.CartEstimate += i.Estimate; t.CartPaid += i.Paid; }
                else t.Remaining += i.Estimate;
            }
            return t;
        }

        static long stamp;
        public static void SetChecked(ShopItem it, bool on)
        {
            it.Checked = on;
            it.CheckedAt = on ? Math.Max(DateTime.UtcNow.Ticks, ++stamp) : 0;
            if (on) stamp = Math.Max(stamp, it.CheckedAt);
            if (!on) it.Actual = null;
        }

        /// <summary>
        /// 買い物を終える: カゴの中の品を履歴に残してリストから消す。
        /// actualTotal はレシートの合計 (null ならカゴの中の金額)。記録したものを返す (カゴが空なら null)
        /// </summary>
        public static Purchase Finish(ShopData d, ShopList list, long? actualTotal, DateTime today)
        {
            var done = list.Items.Where(i => i.Checked).ToList();
            if (done.Count == 0) return null;
            var t = Totals(list);
            var p = new Purchase { Date = Fmt.Iso(today), List = list.Name, Estimate = t.CartEstimate, Total = Math.Max(0, actualTotal ?? t.CartPaid) };
            foreach (var i in done)
                p.Lines.Add(new PurchaseLine { Name = i.Name, Qty = i.Qty, Unit = i.Unit, Category = i.Category, Price = i.Actual.HasValue ? i.Actual.Value / Math.Max(1, i.Qty) : i.Price });
            d.History.Add(p);
            list.Items.RemoveAll(i => i.Checked);
            if (d.History.Count > ShopData.MaxHistory) d.History = d.History.OrderByDescending(h => h.Date, StringComparer.Ordinal).Take(ShopData.MaxHistory).ToList();
            return p;
        }

        /// <summary>カゴの中の品を消すだけ (履歴・支出には残さない)。消した数</summary>
        public static int ClearChecked(ShopList list) { return list.Items.RemoveAll(i => i.Checked); }

        /// <summary>よく買うもの (回数の多い順 → 最近の順)。exclude にある名前 (いまリストにあるもの) は除く</summary>
        public static List<Frequent> FrequentItems(ShopData d, IEnumerable<string> exclude, int max, string listName = null)
        {
            var ex = new HashSet<string>((exclude ?? new string[0]).Select(Key));
            var map = new Dictionary<string, Frequent>();
            foreach (var h in d.History.OrderBy(h => h.Date, StringComparer.Ordinal))
            {
                if (listName != null && h.List != listName) continue;
                foreach (var l in h.Lines)
                {
                    var k = Key(l.Name);
                    if (k.Length == 0) continue;
                    Frequent f;
                    if (!map.TryGetValue(k, out f)) map[k] = f = new Frequent { Name = l.Name };
                    f.Count++; f.LastPrice = l.Price; f.Category = l.Category; f.Unit = l.Unit; f.Qty = Math.Max(1, l.Qty); f.Last = h.Day;
                }
            }
            return map.Where(kv => !ex.Contains(kv.Key)).Select(kv => kv.Value)
                .OrderByDescending(f => f.Count).ThenByDescending(f => f.Last ?? DateTime.MinValue).ThenBy(f => f.Name, StringComparer.Ordinal)
                .Take(max).ToList();
        }

        /// <summary>よく買うもの・履歴の品をリストに足す (前の数・値段・売り場で)</summary>
        public static ShopItem AddFrequent(ShopList list, Frequent f, DateTime today)
        {
            var same = list.Items.FirstOrDefault(x => !x.Checked && Key(x.Name) == Key(f.Name));
            if (same != null) return same;
            var it = new ShopItem { Name = f.Name, Qty = f.Qty, Unit = f.Unit, Price = f.LastPrice, Category = string.IsNullOrEmpty(f.Category) ? GuessAisle(f.Name) : f.Category, Added = Fmt.Iso(today) };
            list.Items.Add(it);
            return it;
        }

        /// <summary>その月 (today の月) に買い物で使った金額 (履歴の実際の合計)</summary>
        public static long MonthSpend(ShopData d, DateTime month)
        {
            return d.History.Where(h => h.Day.HasValue && h.Day.Value.Year == month.Year && h.Day.Value.Month == month.Month).Sum(h => h.Total);
        }

        /// <summary>その月の予想との差 (実際 − 予想。マイナスなら予想より安く済んだ)</summary>
        public static long MonthDiff(ShopData d, DateTime month)
        {
            return d.History.Where(h => h.Day.HasValue && h.Day.Value.Year == month.Year && h.Day.Value.Month == month.Month && h.Estimate > 0).Sum(h => h.Total - h.Estimate);
        }

        /// <summary>予算の残り (予算なしなら null)</summary>
        public static long? BudgetLeft(ShopData d, DateTime month)
        {
            if (d.Settings.MonthlyBudget <= 0) return null;
            return d.Settings.MonthlyBudget - MonthSpend(d, month);
        }

        public static string QtyText(ShopItem i) { return QtyText(i.Qty, i.Unit); }
        public static string QtyText(int qty, string unit)
        {
            if (!string.IsNullOrEmpty(unit)) return qty + unit;
            return qty > 1 ? "×" + qty : "";
        }

        /// <summary>LINE やメモに貼れるテキスト (売り場ごと。カゴの中は ✓)</summary>
        public static string ToText(ShopList list, DateTime today, bool includeChecked = true)
        {
            var sb = new StringBuilder();
            sb.Append("【" + list.Name + "】 " + today.ToString("M/d", CultureInfo.InvariantCulture) + "(" + Fmt.DayOfWeek(today) + ")\r\n");
            var open = Ordered(list).Where(i => !i.Checked).ToList();
            string cur = null;
            foreach (var i in open)
            {
                if (i.Category != cur) { cur = i.Category; sb.Append("■ " + cur + "\r\n"); }
                sb.Append("□ " + Line(i) + "\r\n");
            }
            var done = list.Items.Where(i => i.Checked).ToList();
            if (includeChecked && done.Count > 0)
            {
                sb.Append("■ カゴに入れた\r\n");
                foreach (var i in Ordered(list).Where(i => i.Checked)) sb.Append("✓ " + Line(i) + "\r\n");
            }
            if (open.Count == 0 && done.Count == 0) sb.Append("(何もありません)\r\n");
            var t = Totals(list);
            if (t.Estimate > 0) sb.Append("予想の合計 " + Fmt.Yen(t.Estimate) + (t.Unknown > 0 ? " (値段のわからないもの " + t.Unknown + " 品を除く)" : "") + "\r\n");
            return sb.ToString();
        }

        static string Line(ShopItem i)
        {
            var q = QtyText(i);
            var s = i.Name + (q.Length > 0 ? " " + q : "");
            if (i.Price > 0) s += "  " + Fmt.Yen(i.Estimate);
            if (!string.IsNullOrEmpty(i.Memo)) s += "  (" + i.Memo + ")";
            return s;
        }

        // ---------------------------------------------------------------- 欲しいもの

        public static readonly string[] PriorityLabels = { "高", "中", "低" };

        /// <summary>まだ買っていないもの: 目標の値段になったもの → 優先度 → 足した順。買ったものは最後 (新しい順)</summary>
        public static List<WantItem> OrderedWants(ShopData d)
        {
            var open = d.Wants.Where(w => !w.Bought).Select((w, n) => new { w, n })
                .OrderByDescending(x => x.w.AtTarget).ThenBy(x => (int)x.w.Priority).ThenBy(x => x.n).Select(x => x.w);
            var bought = d.Wants.Where(w => w.Bought).OrderByDescending(w => w.BoughtOn, StringComparer.Ordinal);
            return open.Concat(bought).ToList();
        }

        /// <summary>目標まであといくら (目標以下なら 0、わからなければ null)</summary>
        public static long? ToTarget(WantItem w)
        {
            if (w.Target <= 0 || w.Price <= 0) return null;
            return Math.Max(0, w.Price - w.Target);
        }

        /// <summary>欲しいものを買った: 印をつけて、支出に残す</summary>
        public static Purchase BuyWant(ShopData d, WantItem w, long paid, DateTime today)
        {
            w.BoughtOn = Fmt.Iso(today);
            w.Paid = Math.Max(0, paid);
            var p = new Purchase { Date = Fmt.Iso(today), List = "欲しいもの", Estimate = w.Price > 0 ? w.Price : w.Target, Total = w.Paid };
            p.Lines.Add(new PurchaseLine { Name = w.Name, Qty = 1, Price = w.Paid, Category = "欲しいもの" });
            d.History.Add(p);
            return p;
        }

        /// <summary>まだ買っていない欲しいものの合計 (いまの値段、なければ目標)</summary>
        public static long WantTotal(ShopData d)
        {
            return d.Wants.Where(w => !w.Bought).Sum(w => w.Price > 0 ? w.Price : w.Target);
        }

        /// <summary>見本データ (画面写真・試し用)</summary>
        public static ShopData Sample(DateTime today)
        {
            var d = ShopData.CreateDefault();
            var super = d.Lists[0]; var drug = d.Lists[1];
            Func<int, string> ago = n => Fmt.Iso(today.AddDays(-n));
            // 履歴 (よく買うもの)
            for (int w = 1; w <= 4; w++)
            {
                var p = new Purchase { Date = ago(w * 7 - 2), List = "スーパー", Estimate = 2380, Total = 2380 + (w % 2 == 0 ? 120 : -60) };
                p.Lines.Add(new PurchaseLine { Name = "牛乳", Qty = 2, Price = 198, Category = "乳製品・卵" });
                p.Lines.Add(new PurchaseLine { Name = "卵", Qty = 1, Unit = "パック", Price = 248, Category = "乳製品・卵" });
                p.Lines.Add(new PurchaseLine { Name = "食パン", Qty = 1, Price = 168, Category = "パン・シリアル" });
                if (w % 2 == 1) p.Lines.Add(new PurchaseLine { Name = "納豆", Qty = 1, Unit = "パック", Price = 98, Category = "豆腐・練り物" });
                if (w <= 3) p.Lines.Add(new PurchaseLine { Name = "バナナ", Qty = 1, Unit = "房", Price = 158, Category = "野菜・果物" });
                if (w >= 2) p.Lines.Add(new PurchaseLine { Name = "ヨーグルト", Qty = 1, Price = 138, Category = "乳製品・卵" });
                d.History.Add(p);
            }
            var dp = new Purchase { Date = ago(9), List = "ドラッグストア", Estimate = 1480, Total = 1398 };
            dp.Lines.Add(new PurchaseLine { Name = "ティッシュ", Qty = 1, Unit = "箱", Price = 398, Category = "日用品" });
            dp.Lines.Add(new PurchaseLine { Name = "歯みがき粉", Qty = 1, Price = 298, Category = "薬・衛生用品" });
            d.History.Add(dp);

            foreach (var line in new[] { "にんじん 3本 128円", "玉ねぎ 1袋 198円", "鶏もも肉 2 398円", "牛乳 2 198円", "豆腐 2 68円", "しょうゆ 298円", "お茶 2本 138円", "食パン 168円" })
                Add(d, super, line, today);
            foreach (var it in super.Items.Where(i => i.Name == "牛乳" || i.Name == "食パン")) SetChecked(it, true);
            super.Items.First(i => i.Name == "牛乳").Actual = 376;
            foreach (var line in new[] { "シャンプー 詰め替え 598円", "マスク 1箱 498円", "トイレットペーパー 12ロール 458円" })
                Add(d, drug, line, today);

            d.Wants.Add(new WantItem { Name = "ワイヤレスイヤホン", Target = 12000, Price = 14800, PriorityText = "high", Store = "家電量販店", Added = ago(20) });
            d.Wants.Add(new WantItem { Name = "電気ケトル", Target = 4000, Price = 3980, PriorityText = "normal", Store = "ネット通販", Added = ago(12) });
            d.Wants.Add(new WantItem { Name = "冬用ブランケット", Target = 2500, Price = 0, PriorityText = "low", Added = ago(5), Memo = "セールを待つ" });
            d.Settings.MonthlyBudget = 40000;
            return d;
        }
    }
}
