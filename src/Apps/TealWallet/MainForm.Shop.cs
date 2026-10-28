// Teal Wallet (サブスク＆買い物リスト) - 1.9.0: 買い物リスト・欲しいもののタブ
//  買い物リスト (Ctrl+5):
//    入力欄に「牛乳 2 198円」「卵、パン、トマト3個」と書いて Enter で追加 (#日用品 で売り場を決める)
//    Space / □ をクリック: カゴに入れる (チェック。下に移る)  Enter: 編集  Delete: 削除
//    Ctrl+Enter: 買い物を終える (レシートの合計を記録)  Ctrl+Delete: チェック済みを消す
//    Ctrl+H: 履歴から追加  Ctrl+Shift+C: テキストでコピー  Ctrl+PageUp / PageDown: リストを切り替える
//  欲しいもの (Ctrl+6): Ctrl+N 追加 / Enter 編集 / Delete 削除 / Ctrl+B 買った
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Wallet
{
    public partial class MainForm
    {
        public ShopData Shop;
        // 買い物リスト
        public readonly ComboBox ListPicker = Lay.Combo();
        public readonly TextBox QuickAdd = new TextBox();
        public readonly RowList ShopRows = new RowList(1) { Dock = DockStyle.Fill };
        readonly FlowLayoutPanel chips = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        readonly Label totalBig = new Label { AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent };
        readonly WrapLabel totalNote = new WrapLabel(), monthNote = new WrapLabel();
        readonly WrapLabel dashTotal = new WrapLabel();
        bool fillingPicker;
        // 欲しいもの
        public readonly RowList WantList = new RowList(2) { Dock = DockStyle.Fill };
        readonly ComboBox wantFilter = Lay.Combo("まだ買っていない", "買ったもの", "すべて");
        readonly WrapLabel wantSummary = new WrapLabel();

        /// <summary>売り場の見出しの行</summary>
        public class AisleRow
        {
            public string Title; public int Count; public bool Cart;
            public override string ToString() { return Title; }
        }

        public ShopList CurrentList
        {
            get { return Shop.FindList(Shop.Settings.CurrentList) ?? Shop.Lists[0]; }
        }

        public void ShopSave()
        {
            if (!DataStore.TrySave(ShopData.FileName, Shop)) footer.StatusText = "買い物リストを保存できませんでした (データフォルダを確かめてください)";
        }

        void ShopChanged(ShopItem select = null)
        {
            ShopSave();
            RefreshShop(select);
            RefreshDashboard();
        }

        // ================================================================ 買い物リスト: 画面

        void BuildShop(Panel v)
        {
            // 上: リストの切り替え + 入力欄
            ListPicker.Width = Theme.S(200);
            ListPicker.SelectedIndexChanged += (s, e) =>
            {
                if (fillingPicker || ListPicker.SelectedIndex < 0 || ListPicker.SelectedIndex >= Shop.Lists.Count) return;
                Shop.Settings.CurrentList = Shop.Lists[ListPicker.SelectedIndex].Id;
                ShopSave(); RefreshShop();
            };
            tips.SetToolTip(ListPicker, "リストを切り替える (Ctrl+PageUp / PageDown)");
            var bList = new FlatButton("リスト ▾", ButtonKind.Secondary).FitWidth(80);
            var menu = new ContextMenuStrip();
            menu.Items.Add("新しいリスト…", null, (s, e) => NewList());
            menu.Items.Add("名前を変える…", null, (s, e) => RenameList());
            menu.Items.Add("このリストを削除…", null, (s, e) => DeleteList());
            bList.Click += (s, e) => menu.Show(bList, new Point(0, bList.Height));

            QuickAdd.Font = Theme.Body; QuickAdd.BorderStyle = BorderStyle.None;
            Ui.SetCue(QuickAdd, "買うものを入力して Enter  (例: 牛乳 2 198円、卵、トマト3個 #野菜・果物)");
            QuickAdd.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None) { AddShopText(QuickAdd.Text); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape && QuickAdd.Text.Length > 0) { QuickAdd.Text = ""; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { ShopRows.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            var bAdd = new FlatButton("追加", ButtonKind.Primary).FitWidth(64);
            bAdd.Click += (s, e) => AddShopText(QuickAdd.Text);
            var bHist = new FlatButton("履歴から…", ButtonKind.Secondary).FitWidth(80);
            bHist.Click += (s, e) => AddFromHistory();
            tips.SetToolTip(bHist, "前に買ったものから選んで追加 (Ctrl+H)");
            var input = Lay.Row(new InputFrame(QuickAdd), bHist, bAdd);
            // 左にリストの切り替え、右に入力欄
            var top = new Panel { Height = input.Height, BackColor = Color.Transparent };
            ListPicker.Dock = DockStyle.Left; bList.Dock = DockStyle.Left; input.Dock = DockStyle.Fill;
            top.Controls.Add(input);
            top.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.S(10), BackColor = Color.Transparent });
            top.Controls.Add(bList);
            top.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.S(6), BackColor = Color.Transparent });
            top.Controls.Add(ListPicker);
            top.Dock = DockStyle.Top;
            chips.Height = Theme.S(Theme.ButtonHeight) + Theme.S(6);
            chips.Padding = new Padding(0, Theme.S(6), 0, 0);

            // 左: 品のリスト
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(6, 8, 6, 8), Margin = new Padding(0, 0, Theme.S(8), 0) };
            ShopRows.PaintRow = PaintShopRow;
            ShopRows.EmptyText = "まだ何もありません。\n上の入力欄に「牛乳 2 198円」のように書いて Enter で追加できます。\n「、」や改行で区切ると、まとめて追加できます。";
            ShopRows.MouseDown += (s, e) =>
            {
                int i = ShopRows.IndexFromPoint(e.Location);
                if (i < 0 || e.Button != MouseButtons.Left) return;
                var it = ShopRows.Items[i] as ShopItem;
                if (it != null && e.X < Theme.S(40)) ToggleItem(it);
            };
            ShopRows.DoubleClick += (s, e) => { var it = ShopRows.SelectedItem as ShopItem; if (it != null) EditItem(it); };
            ShopRows.KeyDown += (s, e) =>
            {
                var it = ShopRows.SelectedItem as ShopItem;
                if (e.Modifiers != Keys.None) return;
                if (e.KeyCode == Keys.Space && it != null) { ToggleItem(it); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter && it != null) { EditItem(it); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete && it != null) { DeleteItem(it); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && ShopRows.SelectedIndex <= 0) { QuickAdd.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            ShopRows.KeyPress += (s, e) => { if (e.KeyChar == ' ') e.Handled = true; };
            card.Controls.Add(ShopRows);

            // 右: 合計と操作
            var side = new CardPanel("合計") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            totalBig.Font = StatTile.ValueFont; totalBig.ForeColor = Theme.Text; totalBig.Height = StatTile.ValueFont.Height + Theme.S(4);
            totalNote.Font = Theme.Small; totalNote.ForeColor = Theme.SubText;
            monthNote.Font = Theme.Small; monthNote.ForeColor = Theme.SubText;
            var bFinish = new FlatButton("買い物を終える…", ButtonKind.Primary) { Dock = DockStyle.Top };
            var bClear = new FlatButton("チェック済みを消す", ButtonKind.Secondary) { Dock = DockStyle.Top };
            var bCopy = new FlatButton("テキストでコピー", ButtonKind.Secondary) { Dock = DockStyle.Top };
            bFinish.Click += (s, e) => FinishShopping();
            bClear.Click += (s, e) => ClearChecked(true);
            bCopy.Click += (s, e) => CopyListText();
            tips.SetToolTip(bFinish, "カゴに入れたものとレシートの合計を記録して、リストから消す (Ctrl+Enter)");
            tips.SetToolTip(bClear, "カゴに入れたものを、記録せずに消す (Ctrl+Delete)");
            tips.SetToolTip(bCopy, "LINE やメモに貼れる形でコピー (Ctrl+Shift+C)");
            Lay.Stack(side, Lay.SectionLabel("予想の合計"), totalBig, totalNote, Ui.Spacer(10), bFinish, Ui.Spacer(6), bClear, Ui.Spacer(6), bCopy, Ui.Spacer(12),
                Lay.SectionLabel("今月の買い物"), monthNote);
            side.Resize += (s, e) => { foreach (Control c in side.Controls) if (c is WrapLabel) c.Width = side.ClientSize.Width - side.Padding.Horizontal; };

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background, Padding = Theme.P(0, 8, 0, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(250)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.Controls.Add(card, 0, 0);
            grid.Controls.Add(side, 1, 0);

            v.Controls.Add(grid);
            v.Controls.Add(chips);
            v.Controls.Add(top);
        }

        public void RefreshShop(ShopItem select = null)
        {
            var list = CurrentList;
            // リストの切り替え
            fillingPicker = true;
            ListPicker.BeginUpdate();
            ListPicker.Items.Clear();
            foreach (var l in Shop.Lists) ListPicker.Items.Add(l.Name + "  (" + l.Items.Count(i => !i.Checked) + ")");
            ListPicker.SelectedIndex = Shop.Lists.IndexOf(list);
            ListPicker.EndUpdate();
            fillingPicker = false;

            // 品 (売り場ごと → カゴの中)
            var rows = new List<object>();
            var ordered = ShopLogic.Ordered(list);
            string cur = null;
            foreach (var it in ordered.Where(i => !i.Checked))
            {
                if (it.Category != cur) { cur = it.Category; rows.Add(new AisleRow { Title = cur, Count = ordered.Count(x => !x.Checked && x.Category == cur) }); }
                rows.Add(it);
            }
            var cart = ordered.Where(i => i.Checked).ToList();
            if (cart.Count > 0)
            {
                rows.Add(new AisleRow { Title = "カゴに入れた", Count = cart.Count, Cart = true });
                rows.AddRange(cart);
            }
            var keep = select ?? ShopRows.SelectedItem as ShopItem;
            ShopRows.SetItems(rows, o => o == keep);
            if (keep == null && ShopRows.Items.Count > 1 && ShopRows.SelectedItem is AisleRow) ShopRows.SelectedIndex = 1;
            // 先頭の品を選んだときは、売り場の見出しが隠れないように一番上から見せる
            if (ShopRows.SelectedIndex >= 0 && ShopRows.SelectedIndex <= 1) try { ShopRows.TopIndex = 0; } catch { }

            // 合計
            var t = ShopLogic.Totals(list);
            totalBig.Text = Fmt.Yen(t.Estimate);
            var note = "まだ " + (t.Items - t.Checked) + " 品  " + Fmt.Yen(t.Remaining) + "\nカゴ " + t.Checked + " 品  " + Fmt.Yen(t.CartPaid)
                + (t.CartPaid != t.CartEstimate ? " (予想 " + Fmt.Yen(t.CartEstimate) + ")" : "");
            if (t.Unknown > 0) note += "\n値段のわからないもの " + t.Unknown + " 品 (合計に入っていません)";
            totalNote.Text = note;
            monthNote.Text = MonthShopText();
            monthNote.ForeColor = (ShopLogic.BudgetLeft(Shop, Today) ?? 0) < 0 ? Theme.Danger : Theme.SubText;

            // よく買うもの
            RefreshChips(list);
        }

        string MonthShopText()
        {
            var today = Today;
            long spend = ShopLogic.MonthSpend(Shop, today), diff = ShopLogic.MonthDiff(Shop, today);
            var left = ShopLogic.BudgetLeft(Shop, today);
            var s = Fmt.Yen(spend) + " (" + Shop.History.Count(h => h.Day.HasValue && h.Day.Value.Year == today.Year && h.Day.Value.Month == today.Month) + " 回)";
            if (left.HasValue) s += left.Value >= 0 ? "\n予算 " + Fmt.Yen(Shop.Settings.MonthlyBudget) + " まで あと " + Fmt.Yen(left.Value) : "\n予算 " + Fmt.Yen(Shop.Settings.MonthlyBudget) + " を " + Fmt.Yen(-left.Value) + " 超えています";
            else s += "\n(設定で買い物の予算を決められます)";
            if (diff != 0) s += "\n予想より " + Fmt.Yen(Math.Abs(diff)) + (diff < 0 ? " 安く済みました" : " 多くかかりました");
            return s;
        }

        void RefreshChips(ShopList list)
        {
            chips.SuspendLayout();
            foreach (Control c in chips.Controls.Cast<Control>().ToList()) { chips.Controls.Remove(c); c.Dispose(); }
            var names = list.Items.Where(i => !i.Checked).Select(i => i.Name);
            var freq = ShopLogic.FrequentItems(Shop, names, 8, list.Name).Where(f => f.Category != "欲しいもの").ToList();
            if (freq.Count < 4)
                foreach (var f in ShopLogic.FrequentItems(Shop, names, 12).Where(f => f.Category != "欲しいもの"))
                    if (freq.Count < 8 && !freq.Any(x => x.Name == f.Name)) freq.Add(f);
            if (freq.Count > 0)
            {
                var cap = new Label { Text = "よく買うもの:", AutoSize = true, Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false, Padding = new Padding(0, Theme.S(6), Theme.S(4), 0), BackColor = Color.Transparent };
                chips.Controls.Add(cap);
                foreach (var f in freq)
                {
                    var ff = f;
                    var b = new FlatButton("＋ " + f.Name, ButtonKind.Ghost) { Font = Theme.Small };
                    b.FitWidth(40);
                    b.Margin = new Padding(0, 0, Theme.S(4), 0);
                    b.Click += (s, e) => AddFrequent(ff);
                    tips.SetToolTip(b, f.Count + " 回買いました" + (f.LastPrice > 0 ? "  (前は " + Fmt.Yen(f.LastPrice) + ")" : ""));
                    chips.Controls.Add(b);
                }
            }
            else
            {
                chips.Controls.Add(new Label { Text = "「買い物を終える」で記録すると、よく買うものがここに出て、1 クリックで追加できます。", AutoSize = true, Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false, Padding = new Padding(0, Theme.S(6), 0, 0), BackColor = Color.Transparent });
            }
            chips.ResumeLayout();
        }

        void PaintShopRow(Graphics g, Rectangle r, object o, bool sel)
        {
            int pad = Theme.S(12);
            var head = o as AisleRow;
            if (head != null)
            {
                if (!sel) using (var b = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(b, r);
                var title = head.Title + "  " + head.Count;
                Draw.Text(g, head.Cart ? "✓ " + title : title, Theme.Bold, head.Cart ? Theme.Success : Theme.Accent, new Rectangle(r.X + pad, r.Y, r.Width - pad * 2, r.Height));
                return;
            }
            var it = (ShopItem)o;
            // チェックボックス
            int box = Theme.S(16), bx = r.X + pad, by = r.Y + (r.Height - box) / 2;
            var br = new Rectangle(bx, by, box, box);
            if (it.Checked)
            {
                using (var b = new SolidBrush(Theme.Success)) g.FillRectangle(b, br);
                using (var p = new Pen(Color.White, Theme.S(2)))
                    g.DrawLines(p, new[] { new Point(bx + Theme.S(3), by + box / 2), new Point(bx + box / 2 - Theme.S(1), by + box - Theme.S(4)), new Point(bx + box - Theme.S(3), by + Theme.S(4)) });
            }
            else
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, br);
                using (var p = new Pen(Theme.BorderStrong, Theme.S(1))) g.DrawRectangle(p, br);
            }
            // 右: 金額
            string amt = it.Actual.HasValue ? Fmt.Yen(it.Actual.Value) : it.Price > 0 ? Fmt.Yen(it.Estimate) : "—";
            int aw = Draw.Width(g, amt, Theme.Body) + Theme.S(4);
            var ac = it.Checked ? Theme.SubText : it.Price > 0 || it.Actual.HasValue ? Theme.Text : Theme.Disabled;
            Draw.Text(g, amt, Theme.Body, ac, new Rectangle(r.Right - pad - aw, r.Y, aw, r.Height), TextFormatFlags.Right);
            int right = r.Right - pad - aw - Theme.S(8);
            if (it.Actual.HasValue && it.Actual.Value != it.Estimate && it.Price > 0)
            {
                var ex = "予想 " + Fmt.Yen(it.Estimate);
                int ew = Draw.Width(g, ex, Theme.Small) + Theme.S(4);
                Draw.Text(g, ex, Theme.Small, Theme.SubText, new Rectangle(right - ew, r.Y, ew, r.Height), TextFormatFlags.Right);
                right -= ew + Theme.S(6);
            }
            else if (it.Price > 0 && it.Qty > 1)
            {
                var ex = "@" + Fmt.Yen(it.Price);
                int ew = Draw.Width(g, ex, Theme.Small) + Theme.S(4);
                Draw.Text(g, ex, Theme.Small, Theme.SubText, new Rectangle(right - ew, r.Y, ew, r.Height), TextFormatFlags.Right);
                right -= ew + Theme.S(6);
            }
            if (it.Checked) right = Draw.Chip(g, it.Category, Theme.SubText, Theme.SurfaceAlt, right, r.Y + r.Height / 2, true) - Theme.S(6);
            // 中: 名前・数・メモ
            int x = bx + box + Theme.S(10);
            var font = it.Checked ? (strike ?? (strike = new Font(Theme.Body, FontStyle.Strikeout))) : Theme.Body;
            int nw = Math.Min(Draw.Width(g, it.Name, font) + Theme.S(2), Math.Max(0, right - x));
            Draw.Text(g, it.Name, font, it.Checked ? Theme.SubText : Theme.Text, new Rectangle(x, r.Y, nw, r.Height));
            x += nw + Theme.S(6);
            var q = ShopLogic.QtyText(it);
            if (q.Length > 0 && x < right)
            {
                int qw = Math.Min(Draw.Width(g, q, Theme.Bold) + Theme.S(2), right - x);
                Draw.Text(g, q, Theme.Bold, it.Checked ? Theme.SubText : Theme.Accent, new Rectangle(x, r.Y, qw, r.Height));
                x += qw + Theme.S(8);
            }
            if (!string.IsNullOrEmpty(it.Memo) && x < right)
                Draw.Text(g, it.Memo, Theme.Small, Theme.SubText, new Rectangle(x, r.Y, right - x, r.Height));
        }
        static Font strike;

        // ================================================================ 買い物リスト: 操作

        /// <summary>入力欄の文を追加 (「、」や改行で区切ると複数)。足した品の数</summary>
        public int AddShopText(string text)
        {
            var list = CurrentList;
            var added = ShopLogic.Add(Shop, list, text ?? "", Today);
            if (added.Count == 0) { QuickAdd.Focus(); return 0; }
            QuickAdd.Text = "";
            ShopChanged(added[added.Count - 1]);
            footer.StatusText = string.Join("、", added.Take(5).Select(i => i.Name + (ShopLogic.QtyText(i).Length > 0 ? " " + ShopLogic.QtyText(i) : ""))) + (added.Count > 5 ? " ほか" : "")
                + " を「" + list.Name + "」に追加しました  (売り場: " + string.Join("・", added.Select(i => i.Category).Distinct().Take(3)) + ")";
            QuickAdd.Focus();
            return added.Count;
        }

        public void AddFrequent(Frequent f)
        {
            var it = ShopLogic.AddFrequent(CurrentList, f, Today);
            ShopChanged(it);
            footer.StatusText = "「" + it.Name + "」を追加しました";
        }

        public void ToggleItem(ShopItem it)
        {
            ShopLogic.SetChecked(it, !it.Checked);
            ShopChanged(it);
            var t = ShopLogic.Totals(CurrentList);
            footer.StatusText = (it.Checked ? "「" + it.Name + "」をカゴに入れました" : "「" + it.Name + "」をカゴから戻しました") + "  ・  あと " + (t.Items - t.Checked) + " 品";
        }

        public void EditItem(ShopItem it)
        {
            var copy = it.Clone();
            using (var f = new ShopItemForm(copy, false))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            it.Name = copy.Name; it.Qty = copy.Qty; it.Unit = copy.Unit; it.Price = copy.Price; it.Category = copy.Category; it.Actual = copy.Actual; it.Memo = copy.Memo;
            ShopChanged(it);
        }

        public void DeleteItem(ShopItem it)
        {
            var list = CurrentList;
            int idx = ShopRows.SelectedIndex;
            list.Items.Remove(it);
            ShopSave(); RefreshShop(); RefreshDashboard();
            if (ShopRows.Items.Count > 0) ShopRows.SelectedIndex = Math.Max(0, Math.Min(ShopRows.Items.Count - 1, idx));
            footer.StatusText = "「" + it.Name + "」を消しました";
        }

        /// <summary>買い物を終える。total が null ならレシートの合計を聞く (テストでは金額を渡す)</summary>
        public Purchase FinishShopping(long? total = null)
        {
            var list = CurrentList;
            var t = ShopLogic.Totals(list);
            if (t.Checked == 0)
            {
                if (total == null) Dialogs.Info(this, "カゴに入れた (チェックした) ものがありません。\n買ったものの □ をクリックするか、選んで Space を押してください。");
                return null;
            }
            if (total == null)
            {
                using (var f = new AmountForm("買い物を終える", "カゴに入れた " + t.Checked + " 品を記録して、「" + list.Name + "」から消します。\nレシートの合計を入れてください (予想は " + Fmt.Yen(t.CartPaid) + ")。\nまだの品 (" + (t.Items - t.Checked) + " 品) はリストに残ります。",
                    "払った合計", t.CartPaid, "記録して終える"))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return null;
                    total = f.Amount;
                }
            }
            var p = ShopLogic.Finish(Shop, list, total, Today);
            ShopChanged();
            long diff = p.Total - p.Estimate;
            footer.StatusText = "記録しました: " + Fmt.Yen(p.Total) + (p.Estimate > 0 && diff != 0 ? "  (予想より " + Fmt.Yen(Math.Abs(diff)) + (diff < 0 ? " 安い)" : " 高い)") : "")
                + "  ・  今月の買い物 " + Fmt.Yen(ShopLogic.MonthSpend(Shop, Today));
            return p;
        }

        public int ClearChecked(bool ask)
        {
            var list = CurrentList;
            int n = list.Items.Count(i => i.Checked);
            if (n == 0) { if (ask) Dialogs.Info(this, "チェックしたものはありません。"); return 0; }
            if (ask && !Dialogs.Confirm(this, "カゴに入れた " + n + " 品を、記録せずにリストから消します。よろしいですか?\n(支出として記録したいときは「買い物を終える」を使ってください)", "チェック済みを消す")) return 0;
            ShopLogic.ClearChecked(list);
            ShopChanged();
            footer.StatusText = n + " 品を消しました";
            return n;
        }

        public string ListText() { return ShopLogic.ToText(CurrentList, Today); }

        public void CopyListText()
        {
            try { Clipboard.SetText(ListText()); footer.StatusText = "「" + CurrentList.Name + "」をテキストでコピーしました (LINE やメモに貼り付けられます)"; }
            catch (Exception ex) { footer.StatusText = "コピーできませんでした: " + ex.Message; }
        }

        public void AddFromHistory()
        {
            var list = CurrentList;
            var all = ShopLogic.FrequentItems(Shop, list.Items.Where(i => !i.Checked).Select(i => i.Name), 300).Where(f => f.Category != "欲しいもの").ToList();
            if (all.Count == 0) { Dialogs.Info(this, "まだ履歴がありません。\n「買い物を終える」で記録すると、前に買ったものから選んで追加できるようになります。"); return; }
            List<Frequent> picked;
            using (var f = new HistoryAddForm(all, list.Name))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                picked = f.Picked;
            }
            if (picked.Count == 0) return;
            ShopItem last = null;
            foreach (var p in picked) last = ShopLogic.AddFrequent(list, p, Today);
            ShopChanged(last);
            footer.StatusText = "履歴から " + picked.Count + " 品を追加しました";
        }

        public void SwitchList(int delta)
        {
            int i = Shop.Lists.IndexOf(CurrentList);
            int n = (i + delta + Shop.Lists.Count) % Shop.Lists.Count;
            Shop.Settings.CurrentList = Shop.Lists[n].Id;
            ShopSave(); RefreshShop();
            footer.StatusText = "「" + Shop.Lists[n].Name + "」を表示しています";
        }

        public ShopList AddList(string name)
        {
            var l = new ShopList { Name = name };
            Shop.Lists.Add(l);
            Shop.Settings.CurrentList = l.Id;
            ShopSave(); RefreshShop();
            return l;
        }

        void NewList()
        {
            using (var f = new NameForm("新しいリスト", "リストの名前", "", "例: ホームセンター、まとめ買い"))
                if (f.ShowDialog(this) == DialogResult.OK) { AddList(f.Value); footer.StatusText = "「" + f.Value + "」を作りました"; }
        }

        void RenameList()
        {
            var l = CurrentList;
            using (var f = new NameForm("リストの名前を変える", "リストの名前", l.Name, ""))
                if (f.ShowDialog(this) == DialogResult.OK) { l.Name = f.Value; ShopSave(); RefreshShop(); }
        }

        void DeleteList()
        {
            var l = CurrentList;
            if (Shop.Lists.Count <= 1) { Dialogs.Info(this, "リストが 1 つしかないので削除できません。名前を変えて使ってください。"); return; }
            if (!Dialogs.Confirm(this, "「" + l.Name + "」(" + l.Items.Count + " 品) を削除しますか?\n(これまでの記録・支出は残ります)", "リストを削除")) return;
            Shop.Lists.Remove(l);
            Shop.Settings.CurrentList = Shop.Lists[0].Id;
            ShopSave(); RefreshShop();
        }

        /// <summary>買い物リスト・欲しいもののタブだけのキー</summary>
        bool ShopKey(Keys k)
        {
            if (Tabs.SelectedIndex == 4)
            {
                switch (k)
                {
                    case Keys.Control | Keys.Enter: FinishShopping(); return true;
                    case Keys.Control | Keys.Delete: if (QuickAdd.Focused) return false; ClearChecked(true); return true;
                    case Keys.Control | Keys.H: AddFromHistory(); return true;
                    case Keys.Control | Keys.Shift | Keys.C: CopyListText(); return true;
                    case Keys.Control | Keys.PageUp: SwitchList(-1); return true;
                    case Keys.Control | Keys.PageDown: SwitchList(1); return true;
                    case Keys.Control | Keys.L: QuickAdd.Focus(); QuickAdd.SelectAll(); return true;
                    case Keys.Control | Keys.F: QuickAdd.Focus(); QuickAdd.SelectAll(); return true;
                }
            }
            if (Tabs.SelectedIndex == 5)
            {
                var w = WantList.SelectedItem as WantItem;
                switch (k)
                {
                    case Keys.Control | Keys.B: if (w != null && !w.Bought) BuyWant(w); return true;
                    case Keys.Control | Keys.F: return true;
                }
            }
            return false;
        }

        // ================================================================ ダッシュボードのタイル

        void RefreshShopTile()
        {
            var today = Today;
            long spend = ShopLogic.MonthSpend(Shop, today), diff = ShopLogic.MonthDiff(Shop, today);
            var left = ShopLogic.BudgetLeft(Shop, today);
            string note = left.HasValue ? (left.Value >= 0 ? "予算まで あと " + Fmt.Yen(left.Value) : "予算を " + Fmt.Yen(-left.Value) + " 超えています")
                : diff != 0 ? "予想より " + Fmt.Yen(Math.Abs(diff)) + (diff < 0 ? " 安く済みました" : " 多くかかりました") : "「買い物を終える」で記録した合計";
            TileShop.Set(today.Month + " 月の買い物", Fmt.Yen(spend), note, left.HasValue && left.Value < 0 ? Theme.Danger : Theme.Text);
            decimal m = SubLogic.MonthlyTotal(Data.Subs);
            long budget = Data.Settings.MonthlyBudget + Shop.Settings.MonthlyBudget;
            decimal sum = m + spend;
            dashTotal.Text = "今月の支出の目安: サブスク (月あたり) " + Fmt.Yen(m) + " ＋ 買い物 " + Fmt.Yen(spend) + " ＝ " + Fmt.Yen(sum)
                + (Data.Settings.MonthlyBudget > 0 && Shop.Settings.MonthlyBudget > 0 ? "  (予算の合計 " + Fmt.Yen(budget) + (sum > budget ? " を " + Fmt.Yen(sum - budget) + " 超えています)" : " まで あと " + Fmt.Yen(budget - sum) + ")") : "");
            dashTotal.ForeColor = Data.Settings.MonthlyBudget > 0 && Shop.Settings.MonthlyBudget > 0 && sum > budget ? Theme.Danger : Theme.Text;
        }

        // ================================================================ 欲しいもの

        void BuildWants(Panel v)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = Theme.P(10) };
            wantSummary.Font = Theme.Body; wantSummary.ForeColor = Theme.Text; wantSummary.Dock = DockStyle.Top;
            wantFilter.Width = Theme.S(170);
            wantFilter.SelectedIndexChanged += (s, e) => RefreshWants();
            var bNew = new FlatButton("＋ 欲しいもの", ButtonKind.Primary).FitWidth(110);
            bNew.Click += (s, e) => AddWant();
            tips.SetToolTip(bNew, "欲しいものを追加 (Ctrl+N)");
            var top = Lay.Row(wantSummary, wantFilter, bNew);
            top.Dock = DockStyle.Top;

            WantList.PaintRow = PaintWantRow;
            WantList.EmptyText = "欲しいけれど、すぐには買わないものを入れておく場所です。\n目標の値段を入れておくと、いまの値段が目標以下になったものが上に「買いどき」と出ます。\n「＋ 欲しいもの」(Ctrl+N) から追加してみましょう。";
            WantList.DoubleClick += (s, e) => { var w = WantList.SelectedItem as WantItem; if (w != null) EditWant(w); };
            WantList.KeyDown += (s, e) =>
            {
                var w = WantList.SelectedItem as WantItem;
                if (w == null || e.Modifiers != Keys.None) return;
                if (e.KeyCode == Keys.Enter) { EditWant(w); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteWant(w, true); e.Handled = e.SuppressKeyPress = true; }
            };

            var bEdit = new FlatButton("編集", ButtonKind.Secondary).FitWidth(72);
            var bBuy = new FlatButton("買った…", ButtonKind.Secondary).FitWidth(80);
            var bToList = new FlatButton("買い物リストに入れる", ButtonKind.Secondary).FitWidth(120);
            var bDel = new FlatButton("削除", ButtonKind.Danger).FitWidth(72);
            bEdit.Click += (s, e) => { var w = WantList.SelectedItem as WantItem; if (w != null) EditWant(w); };
            bBuy.Click += (s, e) => { var w = WantList.SelectedItem as WantItem; if (w != null && !w.Bought) BuyWant(w); };
            bToList.Click += (s, e) => { var w = WantList.SelectedItem as WantItem; if (w != null) WantToList(w); };
            bDel.Click += (s, e) => { var w = WantList.SelectedItem as WantItem; if (w != null) DeleteWant(w, true); };
            tips.SetToolTip(bEdit, "編集 (Enter / ダブルクリック)");
            tips.SetToolTip(bBuy, "買った金額を記録する (Ctrl+B)。今月の買い物に入ります");
            tips.SetToolTip(bToList, "いま開いている買い物リストに追加する");
            tips.SetToolTip(bDel, "削除 (Delete)");
            var buttons = Lay.Flow(bEdit, bBuy, bToList, bDel);
            buttons.Dock = DockStyle.Bottom;

            card.Controls.Add(WantList);
            card.Controls.Add(Ui.Spacer(8));
            card.Controls.Add(top);
            var sp = Ui.Spacer(8); sp.Dock = DockStyle.Bottom;
            card.Controls.Add(sp);
            card.Controls.Add(buttons);
            v.Controls.Add(card);
        }

        public void RefreshWants(WantItem select = null)
        {
            var all = ShopLogic.OrderedWants(Shop);
            int f = wantFilter.SelectedIndex;
            var shown = all.Where(w => f == 2 || (f == 0 ? !w.Bought : w.Bought)).ToList();
            var keep = select ?? WantList.SelectedItem as WantItem;
            WantList.SetItems(shown, o => o == keep);
            int open = Shop.Wants.Count(w => !w.Bought), at = Shop.Wants.Count(w => !w.Bought && w.AtTarget);
            wantSummary.Text = open == 0 ? "まだ買っていない欲しいものはありません" : "まだ買っていないもの " + open + " 件  ・  合計 " + Fmt.Yen(ShopLogic.WantTotal(Shop)) + (at > 0 ? "  ・  買いどき " + at + " 件" : "");
            wantSummary.ForeColor = at > 0 ? Theme.Success : Theme.Text;
        }

        void PaintWantRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var w = (WantItem)o;
            int pad = Theme.S(12), lh = Theme.Body.Height + Theme.S(2), y1 = r.Y + Theme.S(5), y2 = y1 + lh;
            // 右: 値段
            string amt = w.Bought ? Fmt.Yen(w.Paid) : w.Price > 0 ? Fmt.Yen(w.Price) : "値段 —";
            int aw = Draw.Width(g, amt, Theme.Bold) + Theme.S(4);
            Draw.Text(g, amt, Theme.Bold, w.Bought ? Theme.SubText : w.AtTarget ? Theme.Success : Theme.Text, new Rectangle(r.Right - pad - aw, y1, aw, lh), TextFormatFlags.Right);
            var gap = ShopLogic.ToTarget(w);
            string under = w.Bought ? "買った値段" : gap.HasValue ? (gap.Value == 0 ? "目標以下" : "目標まで " + Fmt.Yen(gap.Value)) : "";
            int uw = Draw.Width(g, under, Theme.Small) + Theme.S(4);
            Draw.Text(g, under, Theme.Small, gap.HasValue && gap.Value == 0 && !w.Bought ? Theme.Success : Theme.SubText, new Rectangle(r.Right - pad - uw, y2, uw, lh), TextFormatFlags.Right);
            int right = r.Right - pad - Math.Max(aw, uw) - Theme.S(10);
            // 左: 優先度
            int x = r.X + pad;
            Color pf, pb;
            switch (w.Priority) { case WantPriority.High: pf = Theme.Danger; pb = Theme.DangerSoft; break; case WantPriority.Low: pf = Theme.SubText; pb = Theme.SurfaceAlt; break; default: pf = Theme.Accent; pb = Theme.AccentSoft; break; }
            x = Draw.Chip(g, "優先度 " + ShopLogic.PriorityLabels[(int)w.Priority], w.Bought ? Theme.SubText : pf, w.Bought ? Theme.SurfaceAlt : pb, x, y1 + lh / 2, false) + Theme.S(8);
            int nw = Math.Min(Draw.Width(g, w.Name, Theme.Body) + Theme.S(2), Math.Max(0, right - x));
            Draw.Text(g, w.Name, Theme.Body, w.Bought ? Theme.SubText : Theme.Text, new Rectangle(x, y1, nw, lh));
            int cx = x + nw + Theme.S(8);
            if (!w.Bought && w.AtTarget && cx < right) Draw.Chip(g, "買いどき", Theme.Success, Theme.SuccessSoft, cx, y1 + lh / 2, false);
            if (w.Bought && cx < right) Draw.Chip(g, "購入済み", Theme.SubText, Theme.SurfaceAlt, cx, y1 + lh / 2, false);
            // 2 行目
            var parts = new List<string>();
            if (w.Bought) { var d = Fmt.ParseIso(w.BoughtOn); parts.Add((d.HasValue ? Fmt.MonthDay(d.Value) : w.BoughtOn) + " に買いました"); }
            if (w.Target > 0) parts.Add("目標 " + Fmt.Yen(w.Target));
            if (!string.IsNullOrEmpty(w.Store)) parts.Add(w.Store);
            if (!string.IsNullOrEmpty(w.Memo)) parts.Add(w.Memo);
            if (parts.Count == 0) parts.Add("目標の値段を入れると、買いどきがわかります");
            Draw.Text(g, string.Join("  ・  ", parts), Theme.Small, Theme.SubText, new Rectangle(r.X + pad, y2, Math.Max(0, right - r.X - pad), lh));
        }

        public void AddWant()
        {
            var w = new WantItem { Added = Fmt.Iso(Today) };
            using (var f = new WantForm(w, true))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            AddWantItem(w);
        }

        public void AddWantItem(WantItem w)
        {
            if (w.Added == null) w.Added = Fmt.Iso(Today);
            Shop.Wants.Add(w);
            if (wantFilter.SelectedIndex == 1) wantFilter.SelectedIndex = 0;
            ShopSave(); RefreshWants(w);
            footer.StatusText = "「" + w.Name + "」を欲しいものに追加しました";
        }

        public void EditWant(WantItem w)
        {
            var copy = w.Clone();
            using (var f = new WantForm(copy, false))
                if (f.ShowDialog(this) != DialogResult.OK) return;
            w.Name = copy.Name; w.Target = copy.Target; w.Price = copy.Price; w.PriorityText = copy.PriorityText; w.Store = copy.Store; w.Url = copy.Url; w.Memo = copy.Memo;
            ShopSave(); RefreshWants(w);
        }

        /// <summary>買った: 金額を記録して今月の買い物に入れる。paid が null なら聞く</summary>
        public Purchase BuyWant(WantItem w, long? paid = null)
        {
            if (paid == null)
            {
                using (var f = new AmountForm("買った", "「" + w.Name + "」を買ったことにして、払った金額を今月の買い物に記録します。", "払った金額", w.Price > 0 ? w.Price : w.Target, "記録する"))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return null;
                    paid = f.Amount;
                }
            }
            var p = ShopLogic.BuyWant(Shop, w, paid.Value, Today);
            ShopSave(); RefreshWants(); RefreshShop(); RefreshDashboard();
            footer.StatusText = "「" + w.Name + "」を " + Fmt.Yen(p.Total) + " で買ったと記録しました" + (w.Target > 0 && p.Total <= w.Target ? " (目標以下!)" : "");
            return p;
        }

        public ShopItem WantToList(WantItem w)
        {
            var list = CurrentList;
            var it = list.Items.FirstOrDefault(i => !i.Checked && i.Name == w.Name);
            if (it == null)
            {
                it = new ShopItem { Name = w.Name, Price = w.Price > 0 ? w.Price : w.Target, Category = ShopLogic.GuessAisle(w.Name), Memo = w.Store, Added = Fmt.Iso(Today) };
                list.Items.Add(it);
            }
            ShopSave(); RefreshShop(it); RefreshDashboard();
            footer.StatusText = "「" + w.Name + "」を「" + list.Name + "」に入れました (買ったら、ここで「買った」を押すと記録できます)";
            return it;
        }

        public void DeleteWant(WantItem w, bool ask)
        {
            if (ask && !Dialogs.Confirm(this, "「" + w.Name + "」を欲しいものから削除しますか?" + (w.Bought ? "\n(支出の記録は残ります)" : ""), "削除")) return;
            Shop.Wants.Remove(w);
            ShopSave(); RefreshWants();
        }
    }
}
