// Teal Wallet (サブスク＆買い物リスト) - ダイアログ: 追加・編集 / 解約ガイド / 設定
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Wallet
{
    /// <summary>サブスクの追加・編集</summary>
    public class SubEditForm : Form
    {
        public readonly Subscription Sub;
        public readonly TextBox tbName = new TextBox(), tbPayment = new TextBox(), tbUrl = new TextBox(), tbMemo = new TextBox();
        public readonly NumericUpDown numAmount = new NumericUpDown(), numMonths = new NumericUpDown();
        public readonly ComboBox cbCycle = Lay.Combo(SubLogic.CycleLabels), cbUsage = Lay.Combo(SubLogic.UsageLabels), cbStatus = Lay.Combo(SubLogic.StatusLabels);
        public readonly ComboBox cbCategory = new ComboBox();
        public readonly DateTimePicker dtStart = FormGrid.DatePicker(), dtTrial = FormGrid.DatePicker(), dtLast = FormGrid.DatePicker();
        public readonly CheckBox chkTrial = FormGrid.Check("あり"), chkLast = FormGrid.Check("記録する");
        readonly WrapLabel preview = new WrapLabel(), error = new WrapLabel();
        readonly Label monthsUnit = new Label { Text = "か月ごと", AutoSize = true, UseMnemonic = false };

        public SubEditForm(Subscription sub, bool isNew)
        {
            Sub = sub;
            DialogKit.Setup(this, isNew ? "サブスクを追加" : "サブスクを編集");
            var grid = new FormGrid(2, 270);

            Ui.SetCue(tbName, "例: 動画見放題、音楽聴き放題");
            grid.Add("サービス名 (必須)", new InputFrame(tbName), 2);

            numAmount.Maximum = 10000000; numAmount.ThousandsSeparator = true; numAmount.Font = Theme.Body; numAmount.Width = Theme.S(140);
            numAmount.TextAlign = HorizontalAlignment.Right;
            var yen = new Label { Text = "円", AutoSize = true, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) };
            grid.Add("1 回の支払い額", FormGrid.Inline(numAmount, yen));

            numMonths.Minimum = 1; numMonths.Maximum = 60; numMonths.Value = 3; numMonths.Width = Theme.S(56); numMonths.Font = Theme.Body;
            cbCycle.Width = Theme.S(120);
            monthsUnit.Padding = new Padding(0, Theme.S(4), 0, 0);
            grid.Add("支払いの周期", FormGrid.Inline(cbCycle, numMonths, monthsUnit));

            grid.Add("契約を始めた日", dtStart);
            grid.Add("無料期間 (終わる日から有料)", FormGrid.Inline(chkTrial, dtTrial));

            cbCategory.DropDownStyle = ComboBoxStyle.DropDown; cbCategory.Font = Theme.Body;
            cbCategory.Items.AddRange(SubLogic.Categories);
            grid.Add("カテゴリ", cbCategory);
            Ui.SetCue(tbPayment, "例: クレジットカード (メイン)");
            grid.Add("支払い方法 (名前だけ。カード番号は入れない)", new InputFrame(tbPayment));

            grid.Add("使う頻度", cbUsage);
            var btnToday = new FlatButton("今日", ButtonKind.Ghost).FitWidth(48);
            btnToday.Click += (s, e) => { chkLast.Checked = true; dtLast.Value = DateTime.Today; };
            grid.Add("最後に使った日", FormGrid.Inline(chkLast, dtLast, btnToday));

            grid.Add("状態", cbStatus);
            Ui.SetCue(tbUrl, "https:// (自分で調べた解約ページ)");
            grid.Add("解約ページの URL (任意)", new InputFrame(tbUrl));

            tbMemo.Multiline = true; tbMemo.AcceptsReturn = true; tbMemo.ScrollBars = ScrollBars.Vertical;
            var memo = new InputFrame(tbMemo, true) { Height = Theme.S(64) };
            grid.Add("メモ", memo, 2);

            preview.Font = Theme.Body; preview.ForeColor = Theme.Accent;
            grid.Add(null, preview, 2);
            error.Font = Theme.Small; error.ForeColor = Theme.Danger;
            grid.Add(null, error, 2);

            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(96);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(96);
            ok.Click += (s, e) => Commit();
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 2);
            Controls.Add(grid);

            // 値を入れる
            tbName.Text = sub.Name;
            numAmount.Value = Math.Min(numAmount.Maximum, Math.Max(0, sub.Amount));
            cbCycle.SelectedIndex = (int)sub.Cycle;
            if (sub.CycleMonths > 0) numMonths.Value = Math.Min(60, sub.CycleMonths);
            dtStart.Value = (sub.StartDate ?? DateTime.Today);
            chkTrial.Checked = sub.TrialEndDate.HasValue; dtTrial.Value = sub.TrialEndDate ?? DateTime.Today.AddDays(30);
            cbCategory.Text = string.IsNullOrEmpty(sub.Category) ? "その他" : sub.Category;
            tbPayment.Text = sub.Payment;
            cbUsage.SelectedIndex = (int)sub.Usage;
            chkLast.Checked = sub.LastUsedDate.HasValue; dtLast.Value = sub.LastUsedDate ?? DateTime.Today;
            cbStatus.SelectedIndex = (int)sub.Status;
            tbUrl.Text = sub.CancelUrl;
            tbMemo.Text = (sub.Memo ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

            EventHandler upd = (s, e) => UpdatePreview();
            numAmount.ValueChanged += upd; cbCycle.SelectedIndexChanged += upd; numMonths.ValueChanged += upd;
            dtStart.ValueChanged += upd; chkTrial.CheckedChanged += upd; dtTrial.ValueChanged += upd; cbStatus.SelectedIndexChanged += upd;
            chkLast.CheckedChanged += upd;
            UpdatePreview();
            Shown += (s, e) => tbName.Focus();
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; } };
        }

        /// <summary>入力中の内容を一時的なコピーに入れる (プレビュー・保存の両方で使う)</summary>
        Subscription Read(Subscription target)
        {
            target.Name = tbName.Text.Trim();
            target.Amount = (long)numAmount.Value;
            target.Cycle = (Cycle)Math.Max(0, cbCycle.SelectedIndex);
            target.CycleMonths = target.Cycle == Cycle.EveryNMonths ? (int)numMonths.Value : 0;
            target.StartDate = dtStart.Value.Date;
            target.TrialEndDate = chkTrial.Checked ? (DateTime?)dtTrial.Value.Date : null;
            var cat = cbCategory.Text.Trim();
            target.Category = cat.Length == 0 ? "その他" : cat;
            target.Payment = tbPayment.Text.Trim();
            target.Usage = (Usage)Math.Max(0, cbUsage.SelectedIndex);
            target.LastUsedDate = chkLast.Checked ? (DateTime?)dtLast.Value.Date : null;
            var newStatus = (SubStatus)Math.Max(0, cbStatus.SelectedIndex);
            if (newStatus == SubStatus.Cancelled && target.Status != SubStatus.Cancelled) target.CancelledDate = DateTime.Today;
            if (newStatus != SubStatus.Cancelled) target.CancelledOn = null;
            target.Status = newStatus;
            target.CancelUrl = tbUrl.Text.Trim();
            target.Memo = tbMemo.Text;
            return target;
        }

        void UpdatePreview()
        {
            bool months = cbCycle.SelectedIndex == (int)Cycle.EveryNMonths;
            numMonths.Visible = monthsUnit.Visible = months;
            dtTrial.Enabled = chkTrial.Checked; dtLast.Enabled = chkLast.Checked;
            var s = Read(Sub.Clone());
            var today = DateTime.Today;
            var next = SubLogic.NextRenewal(s, today);
            string text = next.HasValue
                ? "次の支払い: " + Fmt.Date(next.Value) + " (" + Fmt.Relative(next.Value, today) + ")"
                : "解約済み (支払いはありません)";
            text += "  ・  月あたり " + Fmt.Yen(SubLogic.MonthlyCost(s)) + "  ・  年 " + Fmt.Yen(SubLogic.YearlyCost(s));
            if (SubLogic.InTrial(s, today)) text += "\n無料期間中です (" + Fmt.MonthDay(s.TrialEndDate.Value) + " から有料)";
            if (s.Amount != Sub.Amount && Sub.Amount > 0) text += "\n保存すると料金の変更 (" + Fmt.Yen(Sub.Amount) + " → " + Fmt.Yen(s.Amount) + ") を記録します";
            else { var last = SubPlan.Latest(Sub); if (last != null && last.DateValue.HasValue) text += "\n前回の料金変更: " + Fmt.Date(last.DateValue.Value) + "  " + Fmt.Yen(last.From) + " → " + Fmt.Yen(last.To); }
            preview.Text = text;
        }

        public void Commit()
        {
            if (tbName.Text.Trim().Length == 0) { error.Text = "サービス名を入力してください。"; tbName.Focus(); return; }
            var url = tbUrl.Text.Trim();
            if (url.Length > 0 && !SubLogic.IsOpenableUrl(url)) { error.Text = "解約ページの URL は http:// か https:// で始まるものを入れてください。"; tbUrl.Focus(); return; }
            Read(Sub);
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>解約ガイド (一般的な手順 + 自分で入れた解約ページを開く)。公式の URL をこちらで決めつけない</summary>
    public class CancelGuideForm : Form
    {
        public enum Result { None, MarkCancelling, MarkCancelled }
        public Result Choice = Result.None;

        public static string[] Steps(Subscription s, DateTime today)
        {
            var steps = new System.Collections.Generic.List<string>();
            var next = SubLogic.NextRenewal(s, today);
            if (next.HasValue)
                steps.Add("次の支払いは " + Fmt.Date(next.Value) + " (" + Fmt.Relative(next.Value, today) + ") です。多くのサービスは、更新日の前日までに解約すれば次の請求はありません (サービスの規約で確かめてください)。");
            if (SubLogic.InTrial(s, today))
                steps.Add("いまは無料期間中です。無料期間中に解約しても、終わる日まで使えることが多いです。");
            string pay = (s.Payment ?? "").ToLowerInvariant();
            if (pay.Contains("app store") || pay.Contains("apple") || pay.Contains("iphone") || pay.Contains("ipad"))
                steps.Add("iPhone / iPad の App Store で払っている場合: 「設定」アプリ → 自分の名前 → 「サブスクリプション」から止めます (アプリを消しただけでは止まりません)。");
            else if (pay.Contains("google") || pay.Contains("android") || pay.Contains("play"))
                steps.Add("Android の Google Play で払っている場合: Play ストア → プロフィールのアイコン → 「お支払いと定期購入」→「定期購入」から止めます。");
            else if (pay.Contains("アプリストア") || pay.Contains("スマホ"))
                steps.Add("スマホのアプリストアで払っている場合は、iPhone なら「設定」→ 自分の名前 →「サブスクリプション」、Android なら Play ストア →「お支払いと定期購入」から止めます。");
            else if (pay.Contains("キャリア") || pay.Contains("携帯") || pay.Contains("docomo") || pay.Contains("au") || pay.Contains("softbank") || pay.Contains("ソフトバンク") || pay.Contains("ドコモ"))
                steps.Add("携帯電話の料金と一緒に払っている場合は、携帯会社の会員ページ (契約内容の確認・変更) からも止められることがあります。");
            steps.Add("サービスのサイトかアプリにログインし、「アカウント」「設定」「契約・プラン」「メンバーシップ」などの画面を探します。");
            steps.Add("「解約」「退会」「自動更新をオフ」「プランをキャンセル」などのボタンを押し、最後の確認画面まで進みます (途中で引き止めの案内が出ることがあります)。");
            steps.Add("解約完了のメールや画面を保存しておきます (請求が続いたときの証拠になります)。");
            steps.Add("次の請求日のあとに、カードや口座の明細で請求が止まったか確かめます。");
            return steps.ToArray();
        }

        public CancelGuideForm(Subscription s)
        {
            DialogKit.Setup(this, "解約ガイド - " + s.Name);
            var grid = new FormGrid(1, 520);
            var title = new WrapLabel { Text = s.Name + " を解約する手順", Font = Theme.Title, ForeColor = Theme.Text };
            grid.Add(null, title);
            var lead = new WrapLabel { Text = Fmt.Yen(s.Amount) + " (" + SubLogic.CycleLabel(s) + ")  ・  年 " + Fmt.Yen(SubLogic.YearlyCost(s)) + "  ・  支払い方法: " + (string.IsNullOrEmpty(s.Payment) ? "未入力" : s.Payment), Font = Theme.Body, ForeColor = Theme.SubText };
            grid.Add(null, lead);
            int i = 1;
            foreach (var st in Steps(s, DateTime.Today))
                grid.Add(null, new WrapLabel { Text = (i++) + ".  " + st, Font = Theme.Body, ForeColor = Theme.Text });
            var note = new WrapLabel { Text = "※ 手順はサービスによって違います。正しい手順はサービスの公式のヘルプで確かめてください。このアプリはインターネットに接続せず、解約の操作もしません。", Font = Theme.Small, ForeColor = Theme.SubText };
            grid.Add(null, note);

            bool hasUrl = SubLogic.IsOpenableUrl(s.CancelUrl);
            var open = new FlatButton(hasUrl ? "解約ページを開く" : "解約ページの URL は未登録", ButtonKind.Secondary).FitWidth(140);
            open.Enabled = hasUrl;
            open.Click += (sender, e) => AppHost.OpenUrl(s.CancelUrl.Trim());
            var urlNote = new WrapLabel { Text = hasUrl ? "登録した URL: " + s.CancelUrl : "「編集」で、自分で調べた解約ページの URL を登録しておくと、ここから開けます。", Font = Theme.Small, ForeColor = Theme.SubText };
            grid.AddRaw(FormGrid.Inline(open), 1);
            grid.Add(null, urlNote);

            var done = new FlatButton("解約した (解約済みにする)", ButtonKind.Primary).FitWidth(120);
            var later = new FlatButton("解約予定にする", ButtonKind.Secondary).FitWidth(96);
            var close = new FlatButton("閉じる", ButtonKind.Secondary).FitWidth(80);
            done.Click += (sender, e) => { Choice = Result.MarkCancelled; DialogResult = DialogResult.OK; };
            later.Click += (sender, e) => { Choice = Result.MarkCancelling; DialogResult = DialogResult.OK; };
            later.Enabled = s.Status == SubStatus.Active;
            done.Enabled = s.Status != SubStatus.Cancelled;
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            grid.AddRaw(DialogKit.Buttons(close, done, later), 1);
            Controls.Add(grid);
        }
    }

    /// <summary>Teal Wallet の設定 (お知らせ) とデータの書き出し</summary>
    public class SubSettingsForm : Form
    {
        public readonly ToggleSwitch swRemind = new ToggleSwitch("更新日・無料期間の終わりをお知らせする");
        public readonly NumericUpDown numDays = new NumericUpDown();
        public readonly NumericUpDown numBudget = new NumericUpDown();
        public readonly NumericUpDown numShopBudget = new NumericUpDown();
        public SubSettingsForm(SubSettings st, bool resident, Action exportCsv, Action backup, Action restore, Action importCsv = null, ShopSettings shop = null)
        {
            DialogKit.Setup(this, AppInfo.TitleFor("設定"));
            var grid = new FormGrid(1, 460);
            grid.Add(null, new WrapLabel { Text = "お知らせ (リマインダー)", Font = Theme.Title });
            grid.AddRaw(swRemind, 1);
            numDays.Minimum = 0; numDays.Maximum = 30; numDays.Width = Theme.S(60); numDays.Font = Theme.Body;
            var unit = new Label { Text = "日前から (0 = 当日だけ)", AutoSize = true, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) };
            grid.Add("いつからお知らせするか", FormGrid.Inline(numDays, unit));
            var note = new WrapLabel
            {
                Text = "お知らせは、Teal Wallet が起動している間だけ出ます (画面右下の通知)。\n" +
                       (resident ? "いまは常駐オンなので、×で閉じても通知領域に残ってお知らせします。パソコンを起動したときから働かせるには、Teal Hub の設定で「スタートアップ」をオンにしてください。"
                                 : "いまは常駐オフなので、閉じている間はお知らせできません。Teal Hub の設定で「常駐」をオンにしてください。"),
                Font = Theme.Small, ForeColor = resident ? Theme.SubText : Theme.Warning
            };
            grid.Add(null, note);
            var hub = new FlatButton("常駐・スタートアップ・ショートカット (Teal Hub)…", ButtonKind.Secondary).FitWidth(120);
            hub.Click += (s, e) => HubLink.OpenSettings(this, AppInfo.Id);
            grid.AddRaw(FormGrid.Inline(hub), 1);

            grid.Add(null, new WrapLabel { Text = "月の予算", Font = Theme.Title });
            numBudget.Minimum = 0; numBudget.Maximum = 10000000; numBudget.Increment = 500; numBudget.ThousandsSeparator = true;
            numBudget.Width = Theme.S(120); numBudget.Font = Theme.Body; numBudget.TextAlign = HorizontalAlignment.Right;
            var yen = new Label { Text = "円 / 月 (0 = 使わない)", AutoSize = true, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) };
            grid.Add("サブスクに使ってよい金額 (月あたり)", FormGrid.Inline(numBudget, yen));
            grid.Add(null, new WrapLabel { Text = "ダッシュボードと支払い予定に、予算まであといくらか (超えていないか) を出します。", Font = Theme.Small, ForeColor = Theme.SubText });
            if (shop != null)
            {
                numShopBudget.Minimum = 0; numShopBudget.Maximum = 10000000; numShopBudget.Increment = 1000; numShopBudget.ThousandsSeparator = true;
                numShopBudget.Width = Theme.S(120); numShopBudget.Font = Theme.Body; numShopBudget.TextAlign = HorizontalAlignment.Right;
                var yen2 = new Label { Text = "円 / 月 (0 = 使わない)", AutoSize = true, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) };
                grid.Add("買い物に使ってよい金額 (1 か月)", FormGrid.Inline(numShopBudget, yen2));
                grid.Add(null, new WrapLabel { Text = "買い物リストで「買い物を終える」と記録した金額を月ごとに合計し、ダッシュボードと買い物リストに予算の残りを出します。", Font = Theme.Small, ForeColor = Theme.SubText });
                numShopBudget.Value = Math.Max(0, Math.Min(numShopBudget.Maximum, shop.MonthlyBudget));
            }

            grid.Add(null, new WrapLabel { Text = "データ", Font = Theme.Title });
            var csvIn = new FlatButton("CSV から読み込む…", ButtonKind.Secondary).FitWidth(100);
            csvIn.Visible = importCsv != null;
            csvIn.Click += (s, e) => importCsv();
            var csv = new FlatButton("CSV に書き出す…", ButtonKind.Secondary).FitWidth(100);
            var bak = new FlatButton(shop != null ? "バックアップ (JSON。買い物リストも)…" : "バックアップ (JSON)…", ButtonKind.Secondary).FitWidth(100);
            var res = new FlatButton("バックアップから戻す…", ButtonKind.Secondary).FitWidth(100);
            csv.Click += (s, e) => exportCsv(); bak.Click += (s, e) => backup(); res.Click += (s, e) => restore();
            grid.AddRaw(FormGrid.Inline(csv, csvIn), 1);
            grid.AddRaw(FormGrid.Inline(bak, res), 1);
            grid.Add(null, new WrapLabel { Text = "データはこのパソコンの中 (" + DataStore.AppDir + ") だけに保存されます。インターネットには送りません。", Font = Theme.Small, ForeColor = Theme.SubText });

            var ok = new FlatButton("OK", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => { st.Remind = swRemind.Checked; st.RemindDays = (int)numDays.Value; st.MonthlyBudget = (long)numBudget.Value; if (shop != null) shop.MonthlyBudget = (long)numShopBudget.Value; DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            grid.AddRaw(DialogKit.Buttons(ok, cancel), 1);
            Controls.Add(grid);
            swRemind.Checked = st.Remind; numDays.Value = Math.Max(0, Math.Min(30, st.RemindDays));
            numBudget.Value = Math.Max(0, Math.Min(numBudget.Maximum, st.MonthlyBudget));
            swRemind.CheckedChanged += (s, e) => numDays.Enabled = swRemind.Checked;
            numDays.Enabled = st.Remind;
        }
    }
}
