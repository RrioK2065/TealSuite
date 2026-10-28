// Teal Wallet (サブスク＆買い物リスト) - 1.9.0: 買い物リスト・欲しいもののダイアログ
//  品の編集 / 金額の入力 (買い物を終える・欲しいものを買った) / 名前の入力 (リスト) / 履歴から追加 / 欲しいものの編集
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Wallet
{
    static class ShopUi
    {
        public static NumericUpDown Yen(long max = 10000000, int width = 120)
        {
            return new NumericUpDown { Minimum = 0, Maximum = max, ThousandsSeparator = true, Increment = 10, Font = Theme.Body, Width = Theme.S(width), TextAlign = HorizontalAlignment.Right };
        }
        public static Label Unit(string text) { return new Label { Text = text, AutoSize = true, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) }; }
        public static ComboBox AisleCombo()
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.Body };
            c.Items.AddRange(ShopLogic.Aisles);
            return c;
        }
        public static void Value(NumericUpDown n, long v) { n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }
    }

    /// <summary>買い物リストの品の追加・編集</summary>
    public class ShopItemForm : Form
    {
        public readonly ShopItem Item;
        public readonly TextBox tbName = new TextBox(), tbUnit = new TextBox(), tbMemo = new TextBox();
        public readonly NumericUpDown numQty = new NumericUpDown { Minimum = 1, Maximum = 999 }, numPrice = ShopUi.Yen(), numActual = ShopUi.Yen();
        public readonly ComboBox cbAisle = ShopUi.AisleCombo();
        public readonly CheckBox chkActual = FormGrid.Check("入れる");
        readonly WrapLabel error = new WrapLabel();

        public ShopItemForm(ShopItem item, bool isNew)
        {
            Item = item;
            DialogKit.Setup(this, isNew ? "買うものを追加" : "買うものを編集");
            var grid = new FormGrid(2, 220);
            Ui.SetCue(tbName, "例: 牛乳");
            grid.Add("名前 (必須)", new InputFrame(tbName), 2);
            numQty.Font = Theme.Body; numQty.Width = Theme.S(64);
            tbUnit.Width = Theme.S(70);
            Ui.SetCue(tbUnit, "個・本…");
            var unitFrame = new InputFrame(tbUnit) { Width = Theme.S(80) };
            grid.Add("数", FormGrid.Inline(numQty, unitFrame));
            grid.Add("1 つの値段 (予想)", FormGrid.Inline(numPrice, ShopUi.Unit("円 (0 = わからない)")));
            grid.Add("売り場", cbAisle);
            grid.Add("実際の金額 (この品の合計)", FormGrid.Inline(chkActual, numActual, ShopUi.Unit("円")));
            Ui.SetCue(tbMemo, "例: 低脂肪のもの、特売なら 2 つ");
            grid.Add("メモ", new InputFrame(tbMemo), 2);
            grid.Add(null, new WrapLabel { Text = "実際の金額は、カゴに入れた (チェックした) あとに値札やレシートの金額がわかったときに入れます。入れないときは予想の値段で計算します。", Font = Theme.Small, ForeColor = Theme.SubText }, 2);
            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error, 2);

            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            AcceptButton = ok;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 2);
            Controls.Add(grid);

            tbName.Text = item.Name; numQty.Value = Math.Max(1, Math.Min(999, item.Qty)); tbUnit.Text = item.Unit ?? "";
            ShopUi.Value(numPrice, item.Price); cbAisle.Text = item.Category ?? "その他";
            chkActual.Checked = item.Actual.HasValue; ShopUi.Value(numActual, item.Actual ?? item.Estimate);
            numActual.Enabled = chkActual.Checked;
            chkActual.CheckedChanged += (s, e) => { numActual.Enabled = chkActual.Checked; if (chkActual.Checked && numActual.Value == 0) ShopUi.Value(numActual, (long)numPrice.Value * (long)numQty.Value); };
            tbMemo.Text = item.Memo ?? "";
        }

        public bool Commit()
        {
            var name = tbName.Text.Trim();
            if (name.Length == 0) { error.Text = "名前を入れてください"; tbName.Focus(); return false; }
            Item.Name = name;
            Item.Qty = (int)numQty.Value;
            Item.Unit = string.IsNullOrWhiteSpace(tbUnit.Text) ? null : tbUnit.Text.Trim();
            Item.Price = (long)numPrice.Value;
            Item.Category = ShopLogic.MatchAisle(cbAisle.Text);
            Item.Actual = chkActual.Checked ? (long?)numActual.Value : null;
            Item.Memo = string.IsNullOrWhiteSpace(tbMemo.Text) ? null : tbMemo.Text.Trim();
            DialogResult = DialogResult.OK;
            return true;
        }
    }

    /// <summary>金額を 1 つ入れる (買い物を終える・欲しいものを買った)</summary>
    public class AmountForm : Form
    {
        public readonly NumericUpDown numAmount = ShopUi.Yen(10000000, 140);
        public long Amount { get { return (long)numAmount.Value; } }

        public AmountForm(string title, string message, string label, long initial, string okText)
        {
            DialogKit.Setup(this, title);
            var grid = new FormGrid(1, 380);
            grid.Add(null, new WrapLabel { Text = message, Font = Theme.Body, ForeColor = Theme.Text });
            ShopUi.Value(numAmount, initial);
            grid.Add(label, FormGrid.Inline(numAmount, ShopUi.Unit("円")));
            var ok = new FlatButton(okText, ButtonKind.Primary).FitWidth(110);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.DialogResult = DialogResult.OK; AcceptButton = ok;
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
            Shown += (s, e) => { numAmount.Focus(); numAmount.Select(0, numAmount.Text.Length); };
        }
    }

    /// <summary>名前を 1 つ入れる (リストの追加・名前の変更)</summary>
    public class NameForm : Form
    {
        public readonly TextBox tbName = new TextBox();
        readonly WrapLabel error = new WrapLabel();
        public string Value { get { return tbName.Text.Trim(); } }

        public NameForm(string title, string label, string initial, string cue)
        {
            DialogKit.Setup(this, title);
            var grid = new FormGrid(1, 320);
            Ui.SetCue(tbName, cue);
            tbName.Text = initial ?? "";
            grid.Add(label, new InputFrame(tbName));
            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error);
            var ok = new FlatButton("OK", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => { if (Value.Length == 0) { error.Text = "名前を入れてください"; return; } DialogResult = DialogResult.OK; };
            AcceptButton = ok;
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
        }
    }

    /// <summary>前に買ったものから選んで追加</summary>
    public class HistoryAddForm : Form
    {
        public readonly TextBox tbSearch = new TextBox();
        public readonly CheckedListBox List = new CheckedListBox();
        readonly List<Frequent> all;
        readonly HashSet<Frequent> picked = new HashSet<Frequent>();
        public List<Frequent> Picked { get { return all.Where(picked.Contains).ToList(); } }

        class Row
        {
            public Frequent F;
            public override string ToString()
            {
                var q = ShopLogic.QtyText(F.Qty, F.Unit);
                return F.Name + (q.Length > 0 ? " " + q : "") + (F.LastPrice > 0 ? "  " + Fmt.Yen(F.LastPrice) : "") + "   (" + F.Count + " 回" + (F.Last.HasValue ? "・最後 " + F.Last.Value.Month + "/" + F.Last.Value.Day : "") + ")";
            }
        }

        public HistoryAddForm(List<Frequent> items, string listName)
        {
            all = items;
            DialogKit.Setup(this, "履歴から追加 (" + listName + ")");
            var grid = new FormGrid(1, 420);
            Ui.SetCue(tbSearch, "名前で絞り込む…");
            grid.Add(null, new InputFrame(tbSearch));
            List.Font = Theme.Body; List.CheckOnClick = true; List.BorderStyle = BorderStyle.FixedSingle; List.IntegralHeight = false;
            List.Height = Theme.S(300);
            grid.Add("前に買ったもの (よく買う順)。チェックしたものを、前と同じ数・値段で追加します", List);
            List.ItemCheck += (s, e) => { var r = List.Items[e.Index] as Row; if (r == null) return; if (e.NewValue == CheckState.Checked) picked.Add(r.F); else picked.Remove(r.F); };
            tbSearch.TextChanged += (s, e) => Fill();
            tbSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down) { List.Focus(); e.Handled = e.SuppressKeyPress = true; } };
            var ok = new FlatButton("追加", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.DialogResult = DialogResult.OK; AcceptButton = ok;
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
            Fill();
        }

        void Fill()
        {
            var q = tbSearch.Text.Trim();
            List.BeginUpdate();
            List.Items.Clear();
            foreach (var f in all.Where(f => q.Length == 0 || f.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                List.Items.Add(new Row { F = f }, picked.Contains(f));
            List.EndUpdate();
        }

        /// <summary>名前で選ぶ (テスト用)</summary>
        public void Pick(string name)
        {
            var f = all.FirstOrDefault(x => x.Name == name);
            if (f != null) picked.Add(f);
            Fill();
        }
    }

    /// <summary>欲しいものの追加・編集</summary>
    public class WantForm : Form
    {
        public readonly WantItem Item;
        public readonly TextBox tbName = new TextBox(), tbStore = new TextBox(), tbUrl = new TextBox(), tbMemo = new TextBox();
        public readonly NumericUpDown numTarget = ShopUi.Yen(), numPrice = ShopUi.Yen();
        public readonly ComboBox cbPriority = Lay.Combo("高 (早めに買いたい)", "中", "低 (いつか)");
        readonly WrapLabel error = new WrapLabel(), status = new WrapLabel();

        public WantForm(WantItem item, bool isNew)
        {
            Item = item;
            DialogKit.Setup(this, isNew ? "欲しいものを追加" : "欲しいものを編集");
            var grid = new FormGrid(2, 220);
            Ui.SetCue(tbName, "例: ワイヤレスイヤホン");
            grid.Add("名前 (必須)", new InputFrame(tbName), 2);
            grid.Add("目標の値段 (この値段以下なら買う)", FormGrid.Inline(numTarget, ShopUi.Unit("円")));
            grid.Add("いま見かける値段", FormGrid.Inline(numPrice, ShopUi.Unit("円")));
            grid.Add("優先度", cbPriority);
            Ui.SetCue(tbStore, "例: 家電量販店、ネット通販");
            grid.Add("お店 (任意)", new InputFrame(tbStore));
            Ui.SetCue(tbUrl, "https:// (商品ページ。任意)");
            grid.Add("URL (任意)", new InputFrame(tbUrl), 2);
            grid.Add("メモ", new InputFrame(tbMemo), 2);
            status.Font = Theme.Body; status.ForeColor = Theme.Accent;
            grid.Add(null, status, 2);
            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error, 2);
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 2);
            Controls.Add(grid);

            tbName.Text = item.Name; ShopUi.Value(numTarget, item.Target); ShopUi.Value(numPrice, item.Price);
            cbPriority.SelectedIndex = (int)item.Priority;
            tbStore.Text = item.Store ?? ""; tbUrl.Text = item.Url ?? ""; tbMemo.Text = item.Memo ?? "";
            numTarget.ValueChanged += (s, e) => Preview(); numPrice.ValueChanged += (s, e) => Preview();
            Preview();
        }

        void Preview()
        {
            long t = (long)numTarget.Value, p = (long)numPrice.Value;
            if (t <= 0 || p <= 0) { status.Text = "目標の値段といまの値段を入れると、あといくら下がれば買いどきかがわかります。"; status.ForeColor = Theme.SubText; return; }
            if (p <= t) { status.Text = "目標の値段以下です。買いどき!"; status.ForeColor = Theme.Success; }
            else { status.Text = "目標まで あと " + Fmt.Yen(p - t) + " (" + (int)Math.Round((p - t) * 100.0 / p) + "% 下がれば)"; status.ForeColor = Theme.Accent; }
        }

        public bool Commit()
        {
            var name = tbName.Text.Trim();
            if (name.Length == 0) { error.Text = "名前を入れてください"; tbName.Focus(); return false; }
            var url = tbUrl.Text.Trim();
            if (url.Length > 0 && !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            { error.Text = "URL は http:// か https:// で始めてください"; tbUrl.Focus(); return false; }
            Item.Name = name; Item.Target = (long)numTarget.Value; Item.Price = (long)numPrice.Value;
            Item.Priority = (WantPriority)Math.Max(0, cbPriority.SelectedIndex);
            Item.Store = tbStore.Text.Trim().Length == 0 ? null : tbStore.Text.Trim();
            Item.Url = url.Length == 0 ? null : url;
            Item.Memo = tbMemo.Text.Trim().Length == 0 ? null : tbMemo.Text.Trim();
            DialogResult = DialogResult.OK;
            return true;
        }
    }
}
