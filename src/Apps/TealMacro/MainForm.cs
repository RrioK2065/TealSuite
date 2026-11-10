// Teal Macro - メイン画面
//  上: いまの状態の帯 (待機中 / 実行中 / 記録中) ─ どのタブでも見える
//  タブ「連打」: ① 何を押す？ ② 速さと止めかた ③ どこを押す？ ＋「くわしい設定」(開いたときだけ)  下に大きな「▶ 開始」「■ 停止」
//  タブ「マクロ」: 左にマクロの一覧、右に手順。下に大きな「▶ 再生」「■ 停止」「● 新しく記録」
//  ×・Esc では閉じずに隠れる (MacroTray が通知領域に常駐させる)。本当に閉じるのは Shutdown() のときだけ
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public class MainForm : Form
    {
        readonly MacroCore core;
        readonly HeaderBar header;
        readonly FooterBar footer = new FooterBar();
        public readonly StatusBanner Banner = new StatusBanner();
        public readonly SegmentTabs Tabs = new SegmentTabs("連打", "マクロ");
        readonly Panel[] pages = new Panel[2];

        // ---- 連打
        public readonly ChoiceChips ActionChips = new ChoiceChips("左クリック", "右クリック", "中クリック", "ダブルクリック", "キー");
        static readonly string[] ActionValues = { ClickerSettings.ActLeft, ClickerSettings.ActRight, ClickerSettings.ActMiddle, ClickerSettings.ActDouble, ClickerSettings.ActKey };
        public readonly KeyCaptureBox KeyBox = new KeyCaptureBox();
        Panel keyRow;
        public NumBox RateBox, CountBox, TimeBox, DelayBox, HoldBox, MacroDelayBox;
        public CheckBox ChkCount, ChkTime, ChkJitter;
        Label rateNote, stopNote;
        public readonly ChoiceChips TargetChips = new ChoiceChips("マウスのある場所", "決めた位置");
        public FitCard TargetCard, AdvancedCard;
        Panel pointsBox;
        public readonly RowList PointList = new RowList(2);
        public FlatButton BtnPick, BtnRemovePoint, BtnClearPoints, BtnAdvanced, BtnStart, BtnStop, BtnHotkeys;
        public readonly ToggleSwitch SwWindowRel = new ToggleSwitch("ウィンドウに合わせる (ウィンドウを動かしても同じ場所を押す)");
        public readonly ToggleSwitch SwEsc = new ToggleSwitch("実行中は Esc キーでも止める");
        Label pointsNote, startHint, keyNote;
        Panel clickerScroll;

        // ---- マクロ
        public readonly RowList MacroList = new RowList(2), StepList = new RowList(2);
        public FlatButton BtnNew, BtnMacroMenu, BtnAddStep, BtnEditStep, BtnUp, BtnDown, BtnDeleteStep, BtnPlay, BtnStopMacro, BtnRecord, BtnMacroAdvanced, BtnMacroHotkey;
        public NumBox RepeatBox;
        public CheckBox ChkForever;
        public ComboBox SpeedBox;
        public readonly ToggleSwitch SwRecordMoves = new ToggleSwitch("マウスの動きも記録する (オフなら、クリックした位置だけ)");
        public readonly ToggleSwitch SwHide = new ToggleSwitch("記録・再生のあいだ、この画面を最小化する");
        public readonly ToggleSwitch SwEsc2 = new ToggleSwitch("再生中は Esc キーでも止める");
        public readonly ContextMenuStrip MacroMenu = new ContextMenuStrip(), AddStepMenu = new ContextMenuStrip();
        Label macroTitle, macroSummary, macroHotkeyLabel, recordHint;
        Panel macroAdvanced, stepCard;
        bool loading, allowClose;

        public event Action HideRequested;
        public event Action SettingsRequested;
        public string LastMessage { get; private set; }
        public bool LastMessageIsError { get; private set; }
        public string FooterText { get { return footer.StatusText; } }

        /// <summary>テスト用: ダイアログの代わり (null ならダイアログを出す)</summary>
        public Func<MacroStep, MacroStep> EditStepHook;
        public Func<string, string> NameHook;
        public Func<string, bool> ConfirmHook;

        MacroSettings S { get { return core.Settings; } }
        ClickerSettings C { get { return core.Settings.Clicker; } }

        public MainForm(MacroCore core)
        {
            this.core = core;
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(720, 560);
            KeyPreview = true;

            header = HeaderBar.ForApp("");
            var btnSettings = header.AddAction(new FlatButton("ショートカットの設定", ButtonKind.Ghost).FitWidth());
            btnSettings.Click += (s, e) => OpenHub();
            new ToolTip().SetToolTip(btnSettings, "ショートカット・常駐・スタートアップの設定 (Teal Hub で開きます)");

            Tabs.Dock = DockStyle.Top;
            Tabs.SelectedChanged += (s, e) => { ShowPage(Tabs.SelectedIndex); if (!loading) { S.Tab = Tabs.SelectedIndex; core.SaveSettings(); } };

            var bannerBox = new Panel { Dock = DockStyle.Top, Height = Banner.Height + Theme.S(14), Padding = Theme.P(12, 12, 12, 2) };
            Banner.Dock = DockStyle.Fill;
            bannerBox.Controls.Add(Banner);

            pages[0] = BuildClickerPage();
            pages[1] = BuildMacroPage();
            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12, 10, 12, 12) };
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; body.Controls.Add(p); }

            Controls.Add(body); Controls.Add(bannerBox); Controls.Add(Tabs); Controls.Add(footer); Controls.Add(header);
            foreach (var m in new[] { MacroMenu, AddStepMenu }) { m.Font = Theme.Body; m.ShowImageMargin = false; }

            WindowMemory.Attach(this, 820, 720);
            core.Changed += OnCoreChanged;
            core.MacrosChanged += OnMacrosChanged;
            core.ClickerChanged += OnClickerChanged;
            FormClosed += (s, e) => { core.Changed -= OnCoreChanged; core.MacrosChanged -= OnMacrosChanged; core.ClickerChanged -= OnClickerChanged; };
            VisibleChanged += (s, e) => { if (Visible) RefreshAll(); };

            loading = true;
            Tabs.SelectedIndex = S.Tab;
            loading = false;
            ShowPage(S.Tab);
            RefreshAll();
        }

        void OnCoreChanged() { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke((Action)OnCoreChanged); } catch { } return; } UpdateState(); }
        void OnMacrosChanged() { if (IsDisposed) return; RefreshMacros(); UpdateState(); }
        void OnClickerChanged() { if (IsDisposed) return; LoadClicker(); }

        public void ShowPage(int i)
        {
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            if (Tabs.SelectedIndex != i) Tabs.SelectedIndex = i;
            UpdateState();
        }

        // ================================================================ 連打
        Panel BuildClickerPage()
        {
            var page = new Panel();
            clickerScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, Theme.S(4), 0) };

            // ① 何を押す？
            var actionCard = new FitCard("① 何を押す？") { Padding = Theme.P(16, 42, 16, 10) };
            ActionChips.SelectedChanged += (s, e) => { if (!loading) { C.Action = ActionValues[ActionChips.SelectedIndex]; SaveClicker(); } UpdateClickerVisibility(); };
            var actionRow = MUi.Line(ActionChips);
            KeyBox.Width = Theme.S(200);
            KeyBox.KeyChanged += (s, e) => { if (!loading) { C.Key = KeyBox.KeyValue; SaveClicker(); } };
            keyRow = MUi.Line(MUi.Text("押すキー"), new InputFrame(KeyBox) { Width = Theme.S(210) }, MUi.Text("← 欄をクリックして、キーを押します (例: Space、Enter、A)", Theme.Small, Theme.SubText));
            keyNote = (Label)Lay.Note("キーは、いちばん手前のウィンドウに送られます。「▶ 開始」のあと、送りたいウィンドウをクリックして選んでください。");
            Lay.Stack(actionCard, actionRow, keyRow, keyNote);

            // ② 速さと止めかた
            var speedCard = new FitCard("② 速さと止めかた") { Padding = Theme.P(16, 42, 16, 10) };
            RateBox = MUi.Num(ClickerSettings.MinRate, ClickerSettings.MaxRate, 10, 80);
            rateNote = MUi.Text("", Theme.Small, Theme.SubText);
            var quick = new List<Control> { MUi.Text("1 秒に"), RateBox, MUi.Text("回") };
            foreach (var v in new[] { 2, 5, 10, 20, 50 })
            {
                int vv = v;
                var b = new FlatButton(v.ToString(), ButtonKind.Secondary) { Width = Theme.S(40), Height = Theme.S(28) };
                b.Click += (s, e) => RateBox.Value = vv;
                quick.Add(b);
            }
            quick.Add(rateNote);
            RateBox.ValueChanged += (s, e) => { if (!loading) { C.PerSecond = (int)RateBox.Value; SaveClicker(); } UpdateRateNote(); };
            ChkCount = MUi.Check("回数で止める");
            CountBox = MUi.Num(1, ClickerSettings.MaxCount, 100, 100);
            ChkTime = MUi.Check("時間で止める");
            TimeBox = MUi.Num(1, ClickerSettings.MaxSeconds, 10, 90);
            stopNote = (Label)Lay.Note("");
            ChkCount.CheckedChanged += (s, e) => { if (!loading) { C.StopByCount = ChkCount.Checked; SaveClicker(); } UpdateStopNote(); };
            ChkTime.CheckedChanged += (s, e) => { if (!loading) { C.StopByTime = ChkTime.Checked; SaveClicker(); } UpdateStopNote(); };
            CountBox.ValueChanged += (s, e) => { if (!loading) { C.Count = (int)CountBox.Value; if (!ChkCount.Checked) ChkCount.Checked = true; SaveClicker(); } UpdateStopNote(); };
            TimeBox.ValueChanged += (s, e) => { if (!loading) { C.Seconds = (int)TimeBox.Value; if (!ChkTime.Checked) ChkTime.Checked = true; SaveClicker(); } UpdateStopNote(); };
            Lay.Stack(speedCard,
                MUi.Line(quick.ToArray()),
                Ui.Spacer(4),
                MUi.Line(ChkCount, CountBox, MUi.Text("回"), Ui.Spacer(16, DockStyle.None), ChkTime, TimeBox, MUi.Text("秒")),
                stopNote);

            // ③ どこを押す？
            TargetCard = new FitCard("③ どこを押す？") { Padding = Theme.P(16, 42, 16, 10) };
            TargetChips.SelectedChanged += (s, e) => { if (!loading) { C.Target = TargetChips.SelectedIndex == 1 ? ClickerSettings.TargetPoints : ClickerSettings.TargetCursor; SaveClicker(); } UpdateClickerVisibility(); };
            pointsBox = new Panel { BackColor = Color.Transparent };
            PointList.ItemHeight = Theme.S(36);
            PointList.PaintRow = PaintPointRow;
            PointList.EmptyText = "まだ位置がありません。「＋ 画面から選ぶ」を押して、クリックしたい場所をクリックします。";
            PointList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { RemovePoint(); e.Handled = true; } };
            PointList.SelectedIndexChanged += (s, e) => UpdateButtons();
            var listFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border };
            PointList.Dock = DockStyle.Fill;
            listFrame.Controls.Add(PointList);
            BtnPick = new FlatButton("＋ 画面から選ぶ", ButtonKind.Primary).FitWidth(130);
            BtnRemovePoint = new FlatButton("削除", ButtonKind.Secondary).FitWidth(64);
            BtnClearPoints = new FlatButton("すべて削除", ButtonKind.Ghost).FitWidth(80);
            BtnPick.Click += (s, e) => PickPoint();
            BtnRemovePoint.Click += (s, e) => RemovePoint();
            BtnClearPoints.Click += (s, e) => ClearPoints();
            pointsNote = (Label)Lay.Note("");
            var pbar = MUi.Line(BtnPick, BtnRemovePoint, BtnClearPoints);
            pbar.Dock = DockStyle.Bottom;
            var noteBox = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(4) };
            SwWindowRel.AutoSize = false; SwWindowRel.Height = Theme.S(30); SwWindowRel.Dock = DockStyle.Bottom;
            SwWindowRel.CheckedChanged += (s, e) => { if (!loading) { C.WindowRelative = SwWindowRel.Checked; SaveClicker(); RefreshPoints(); } };
            pointsBox.Controls.Add(listFrame);
            pointsBox.Controls.Add(noteBox);
            pointsBox.Controls.Add(pbar);
            pointsBox.Controls.Add(SwWindowRel);
            pointsBox.Height = Theme.S(250);
            Lay.Stack(TargetCard, MUi.Line(TargetChips), Ui.Spacer(4), pointsBox, pointsNote);

            // くわしい設定 (開いたときだけ)
            BtnAdvanced = new FlatButton("▸ くわしい設定", ButtonKind.Ghost).FitWidth();
            BtnAdvanced.Click += (s, e) => { S.ShowAdvanced = !S.ShowAdvanced; core.SaveSettings(); UpdateClickerVisibility(); };
            var advRow = MUi.Line(BtnAdvanced);
            AdvancedCard = new FitCard(null) { Padding = Theme.P(16, 10, 16, 10) };
            DelayBox = MUi.Num(0, 10, 3, 70);
            HoldBox = MUi.Num(1, 200, 10, 70);
            ChkJitter = MUi.Check("間隔を少しばらつかせる (±20%)");
            SwEsc.AutoSize = false; SwEsc.Height = Theme.S(30);
            BtnHotkeys = new FlatButton("ショートカットを変える (Teal Hub)…", ButtonKind.Secondary).FitWidth();
            BtnHotkeys.Click += (s, e) => OpenHub();
            DelayBox.ValueChanged += (s, e) => { if (!loading) { C.StartDelaySec = (int)DelayBox.Value; SaveClicker(); UpdateStartHint(); } };
            HoldBox.ValueChanged += (s, e) => { if (!loading) { C.HoldMs = (int)HoldBox.Value; SaveClicker(); } };
            ChkJitter.CheckedChanged += (s, e) => { if (!loading) { C.Jitter = ChkJitter.Checked; SaveClicker(); } };
            SwEsc.CheckedChanged += (s, e) => { if (!loading) { S.EscStops = SwEsc.Checked; core.SaveSettings(); LoadMacroOptions(); } };
            Lay.Stack(AdvancedCard,
                MUi.Line(MUi.Text("「▶ 開始」を押してから"), DelayBox, MUi.Text("秒待って始める"), MUi.Text("(マウスを動かす時間。ショートカットならすぐ始まります)", Theme.Small, Theme.SubText)),
                MUi.Line(MUi.Text("1 回押している時間"), HoldBox, MUi.Text("ミリ秒"), MUi.Text("(反応しないゲームなどは長めに)", Theme.Small, Theme.SubText)),
                MUi.Line(ChkJitter),
                SwEsc,
                Ui.Spacer(6),
                MUi.Line(BtnHotkeys));

            Lay.Stack(clickerScroll, actionCard, Ui.Spacer(10), speedCard, Ui.Spacer(10), TargetCard, Ui.Spacer(6), advRow, AdvancedCard, Ui.Spacer(6));
            foreach (Control c in clickerScroll.Controls) c.Dock = DockStyle.Top;

            // 下の大きなボタン
            BtnStart = MUi.Big("▶ 開始", ButtonKind.Primary, 170);
            BtnStop = MUi.Big("■ 停止", ButtonKind.Danger, 150);
            BtnStart.Click += (s, e) => core.StartClicker(true);
            BtnStop.Click += (s, e) => core.StopAll(RunJob.ReasonUser);
            startHint = MUi.Text("", Theme.Body, Theme.SubText);
            startHint.Padding = new Padding(0, Theme.S(14), 0, 0);
            var bar = BottomBar(BtnStart, BtnStop, startHint);
            page.Controls.Add(clickerScroll);
            page.Controls.Add(bar);
            return page;
        }

        static Panel BottomBar(params Control[] items)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(46 + 14), Padding = Theme.P(0, 12, 0, 0) };
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
            foreach (var c in items) { c.Margin = new Padding(0, 0, Theme.S(10), 0); f.Controls.Add(c); }
            bar.Controls.Add(f);
            return bar;
        }

        void SaveClicker() { C.Normalize(); core.SaveSettings(); UpdateState(); }

        public void LoadClicker()
        {
            loading = true;
            try
            {
                ActionChips.SelectedIndex = Math.Max(0, Array.IndexOf(ActionValues, C.Action));
                KeyBox.KeyValue = C.Key;
                RateBox.Value = Math.Max(RateBox.Minimum, Math.Min(RateBox.Maximum, C.PerSecond));
                ChkCount.Checked = C.StopByCount;
                CountBox.Value = Math.Max(CountBox.Minimum, Math.Min(CountBox.Maximum, C.Count));
                ChkTime.Checked = C.StopByTime;
                TimeBox.Value = Math.Max(TimeBox.Minimum, Math.Min(TimeBox.Maximum, C.Seconds));
                TargetChips.SelectedIndex = C.Target == ClickerSettings.TargetPoints ? 1 : 0;
                SwWindowRel.Checked = C.WindowRelative;
                DelayBox.Value = Math.Max(DelayBox.Minimum, Math.Min(DelayBox.Maximum, C.StartDelaySec));
                HoldBox.Value = Math.Max(HoldBox.Minimum, Math.Min(HoldBox.Maximum, C.HoldMs));
                ChkJitter.Checked = C.Jitter;
                SwEsc.Checked = S.EscStops;
            }
            finally { loading = false; }
            RefreshPoints();
            UpdateRateNote(); UpdateStopNote(); UpdateClickerVisibility(); UpdateStartHint();
        }

        void UpdateRateNote()
        {
            var ms = 1000.0 / (double)RateBox.Value;
            rateNote.Text = "(" + (ms >= 1000 ? (ms / 1000).ToString("0.#") + " 秒" : Math.Round(ms) + " ミリ秒") + "ごと)";
        }

        void UpdateStopNote()
        {
            string t;
            if (ChkCount.Checked && ChkTime.Checked) t = CountBox.Value.ToString("#,0") + " 回か " + TimeBox.Value + " 秒の、先に来たほうで止まります。";
            else if (ChkCount.Checked) t = CountBox.Value.ToString("#,0") + " 回押したら止まります (約 " + Fmt_.Ms((int)Math.Min(int.MaxValue, (double)CountBox.Value * 1000 / (double)RateBox.Value)) + ")。";
            else if (ChkTime.Checked) t = TimeBox.Value + " 秒たったら止まります (約 " + ((double)TimeBox.Value * (double)RateBox.Value).ToString("#,0") + " 回)。";
            else t = "どちらもオフなら、「■ 停止」か Esc を押すまで続けます。";
            stopNote.Text = t;
        }

        void UpdateClickerVisibility()
        {
            bool isKey = ActionChips.SelectedIndex == 4;
            keyRow.Visible = isKey;
            keyNote.Visible = isKey;
            TargetCard.Visible = !isKey;
            bool pts = TargetChips.SelectedIndex == 1;
            pointsBox.Visible = pts;
            pointsNote.Text = pts
                ? "上から順にクリックして、最後まで行ったら最初に戻ります。" + (core.PickKey.IsEmpty ? "" : " " + core.PickKey.Display + " を押すと、今のマウスの位置をすぐ追加できます。")
                : "「▶ 開始」のあと、" + C.StartDelaySec + " 秒のうちにマウスを押したい場所へ動かしてください。動かせば押す場所も変わります。";
            AdvancedCard.Visible = S.ShowAdvanced;
            BtnAdvanced.Text = (S.ShowAdvanced ? "▾" : "▸") + " くわしい設定";
            BtnAdvanced.FitWidth();
            UpdateButtons();
        }

        void UpdateStartHint()
        {
            var hk = core.ToggleKey;
            startHint.Text = hk.IsEmpty ? "「▶ 開始」のあと " + C.StartDelaySec + " 秒で始まります" : "または " + hk.Display + " (どの画面からでも開始 / 停止)";
            if (pointsNote != null && !(TargetChips.SelectedIndex == 1)) UpdateClickerVisibilityNoteOnly();
        }
        void UpdateClickerVisibilityNoteOnly() { if (TargetChips.SelectedIndex != 1) pointsNote.Text = "「▶ 開始」のあと、" + C.StartDelaySec + " 秒のうちにマウスを押したい場所へ動かしてください。動かせば押す場所も変わります。"; }

        void PaintPointRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var p = o as TargetPoint;
            if (p == null) return;
            int i = C.Points.IndexOf(p);
            int pad = Theme.S(12), d = Theme.S(22);
            var circle = new Rectangle(r.X + pad, r.Y + (r.Height - d) / 2, d, d);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, circle);
            TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, circle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int x = circle.Right + Theme.S(10);
            Draw.Text(g, p.Describe(C.WindowRelative), Theme.Body, Theme.Text, new Rectangle(x, r.Y, r.Right - x - Theme.S(10), r.Height));
        }

        public void RefreshPoints()
        {
            var cur = PointList.Selected;
            PointList.SetItems(C.Points.ToList(), o => o == cur);
            if (cur == null && PointList.Items.Count > 0) PointList.SelectedIndex = PointList.Items.Count - 1;
            UpdateButtons();
        }

        public void PickPoint()
        {
            if (core.PickOnScreen == null) { core.AddPointFromCursor(); return; }
            core.PickOnScreen(t => { if (t != null) { core.AddPoint(t); if (PointList.Items.Count > 0) PointList.SelectedIndex = PointList.Items.Count - 1; } });
        }

        public void RemovePoint()
        {
            var p = PointList.Selected as TargetPoint;
            if (p == null) return;
            int i = PointList.SelectedIndex;
            C.Points.Remove(p);
            SaveClicker();
            RefreshPoints();
            if (PointList.Items.Count > 0) PointList.SelectedIndex = Math.Min(i, PointList.Items.Count - 1);
            Flash("位置を削除しました", false);
        }

        public void ClearPoints()
        {
            if (C.Points.Count == 0) return;
            if (!Ask("決めた位置 (" + C.Points.Count + " か所) をすべて削除しますか？")) return;
            C.Points.Clear();
            SaveClicker();
            RefreshPoints();
        }

        // ================================================================ マクロ
        Panel BuildMacroPage()
        {
            var page = new Panel();
            // 左: マクロの一覧
            var listCard = new CardPanel("マクロ") { Dock = DockStyle.Left, Width = Theme.S(250), Padding = new Padding(1, Theme.S(42), 1, Theme.S(8)) };
            MacroList.Dock = DockStyle.Fill;
            MacroList.ItemHeight = Theme.S(50);
            MacroList.PaintRow = PaintMacroRow;
            MacroList.EmptyText = "まだありません";
            MacroList.SelectedIndexChanged += (s, e) => { if (loading) return; var m = SelectedMacro; S.SelectedMacro = m == null ? null : m.Id; core.SaveSettings(); RefreshSteps(); };
            MacroList.KeyDown += (s, e) => { if (e.KeyCode == Keys.F2) { RenameMacro(); e.Handled = true; } else if (e.KeyCode == Keys.Delete) { DeleteMacro(); e.Handled = true; } else if (e.KeyCode == Keys.Enter) { core.PlayMacro(SelectedId, true); e.Handled = true; } };
            BtnNew = new FlatButton("＋ 新規", ButtonKind.Secondary).FitWidth(70);
            BtnMacroMenu = new FlatButton("その他 ▾", ButtonKind.Ghost).FitWidth(70);
            BtnNew.Click += (s, e) => NewMacro();
            BtnMacroMenu.Click += (s, e) => { BuildMacroMenu(); MacroMenu.Show(BtnMacroMenu, new Point(0, BtnMacroMenu.Height)); };
            var lbar = MUi.Line(BtnNew, BtnMacroMenu);
            lbar.Dock = DockStyle.Bottom; lbar.Padding = Theme.P(10, 4, 0, 0);
            listCard.Controls.Add(MacroList);
            listCard.Controls.Add(lbar);

            // 右: 手順
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10), 0, 0, 0) };
            stepCard = new CardPanel(null) { Dock = DockStyle.Fill, Padding = new Padding(1, Theme.S(10), 1, Theme.S(8)) };
            macroTitle = new Label { Dock = DockStyle.Top, Font = Theme.Title, ForeColor = Theme.Text, Height = Theme.Title.Height + Theme.S(6), Padding = Theme.P(13, 0, 13, 0), UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent };
            macroSummary = new Label { Dock = DockStyle.Top, Font = Theme.Small, ForeColor = Theme.SubText, Height = Theme.Small.Height + Theme.S(8), Padding = Theme.P(14, 0, 14, 0), UseMnemonic = false, AutoEllipsis = true, BackColor = Color.Transparent };
            StepList.Dock = DockStyle.Fill;
            StepList.ItemHeight = Theme.S(34);
            StepList.PaintRow = PaintStepRow;
            StepList.DoubleClick += (s, e) => EditStep();
            StepList.SelectedIndexChanged += (s, e) => UpdateButtons();
            StepList.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteStep(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { EditStep(); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Up) { MoveStep(-1); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Down) { MoveStep(1); e.Handled = true; }
            };
            var sepTop = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
            BtnAddStep = new FlatButton("＋ 手順を追加 ▾", ButtonKind.Secondary).FitWidth();
            BtnEditStep = new FlatButton("編集", ButtonKind.Secondary).FitWidth(56);
            BtnUp = new FlatButton("↑", ButtonKind.Ghost) { Width = Theme.S(36) };
            BtnDown = new FlatButton("↓", ButtonKind.Ghost) { Width = Theme.S(36) };
            BtnDeleteStep = new FlatButton("削除", ButtonKind.Ghost).FitWidth(56);
            new ToolTip().SetToolTip(BtnUp, "上へ (Alt + ↑)");
            new ToolTip().SetToolTip(BtnDown, "下へ (Alt + ↓)");
            BtnAddStep.Click += (s, e) => { BuildAddStepMenu(); AddStepMenu.Show(BtnAddStep, new Point(0, BtnAddStep.Height)); };
            BtnEditStep.Click += (s, e) => EditStep();
            BtnUp.Click += (s, e) => MoveStep(-1);
            BtnDown.Click += (s, e) => MoveStep(1);
            BtnDeleteStep.Click += (s, e) => DeleteStep();
            var sbar = MUi.Line(BtnAddStep, BtnEditStep, BtnUp, BtnDown, BtnDeleteStep);
            sbar.Dock = DockStyle.Bottom; sbar.Padding = Theme.P(10, 6, 0, 0);
            stepCard.Controls.Add(StepList);
            stepCard.Controls.Add(sepTop);
            stepCard.Controls.Add(macroSummary);
            stepCard.Controls.Add(macroTitle);
            stepCard.Controls.Add(sbar);

            // くり返し・速さ・ショートカット
            var optCard = new FitCard(null) { Dock = DockStyle.Bottom, Padding = Theme.P(14, 8, 14, 8) };
            RepeatBox = MUi.Num(1, MacroDef.MaxRepeat, 1, 80);
            ChkForever = MUi.Check("ずっと");
            SpeedBox = Lay.Combo(MacroDef.Speeds.Select(MacroDef.SpeedName).ToArray());
            SpeedBox.Width = Theme.S(120);
            RepeatBox.ValueChanged += (s, e) => { if (!loading) ApplyMacroOptions(); };
            ChkForever.CheckedChanged += (s, e) => { RepeatBox.Enabled = !ChkForever.Checked; if (!loading) ApplyMacroOptions(); };
            SpeedBox.SelectedIndexChanged += (s, e) => { if (!loading) ApplyMacroOptions(); };
            macroHotkeyLabel = MUi.Text("ショートカット: なし");
            BtnMacroHotkey = new FlatButton("設定する…", ButtonKind.Ghost).FitWidth();
            BtnMacroHotkey.Click += (s, e) => OpenHub();
            new ToolTip().SetToolTip(BtnMacroHotkey, "マクロごとのショートカットは Teal Hub の「Teal Macro」の設定で決めます");
            Lay.Stack(optCard,
                MUi.Line(MUi.Text("くり返す"), RepeatBox, MUi.Text("回"), ChkForever, Ui.Spacer(12, DockStyle.None), MUi.Text("速さ"), SpeedBox),
                MUi.Line(macroHotkeyLabel, BtnMacroHotkey));

            // くわしい設定
            BtnMacroAdvanced = new FlatButton("▸ くわしい設定", ButtonKind.Ghost).FitWidth();
            BtnMacroAdvanced.Click += (s, e) => { S.ShowAdvanced = !S.ShowAdvanced; core.SaveSettings(); UpdateClickerVisibility(); UpdateMacroAdvanced(); };
            var advRow = MUi.Line(BtnMacroAdvanced); advRow.Dock = DockStyle.Bottom;
            macroAdvanced = new FitCard(null) { Dock = DockStyle.Bottom, Padding = Theme.P(14, 8, 14, 8) };
            MacroDelayBox = MUi.Num(0, 10, 1, 70);
            foreach (var sw in new[] { SwRecordMoves, SwHide, SwEsc2 }) { sw.AutoSize = false; sw.Height = Theme.S(28); }
            SwRecordMoves.CheckedChanged += (s, e) => { if (!loading) { S.RecordMoves = SwRecordMoves.Checked; core.SaveSettings(); } };
            SwHide.CheckedChanged += (s, e) => { if (!loading) { S.HideWhileRunning = SwHide.Checked; core.SaveSettings(); } };
            SwEsc2.CheckedChanged += (s, e) => { if (!loading) { S.EscStops = SwEsc2.Checked; core.SaveSettings(); LoadClicker(); } };
            MacroDelayBox.ValueChanged += (s, e) => { if (!loading) { S.MacroStartDelaySec = (int)MacroDelayBox.Value; core.SaveSettings(); } };
            Lay.Stack(macroAdvanced, SwRecordMoves, SwHide, SwEsc2,
                MUi.Line(MUi.Text("「▶ 再生」を押してから"), MacroDelayBox, MUi.Text("秒待って始める")));

            right.Controls.Add(stepCard);
            right.Controls.Add(Ui.Spacer(8, DockStyle.Bottom));
            right.Controls.Add(optCard);
            right.Controls.Add(macroAdvanced);
            right.Controls.Add(advRow);

            BtnPlay = MUi.Big("▶ 再生", ButtonKind.Primary, 150);
            BtnStopMacro = MUi.Big("■ 停止", ButtonKind.Danger, 130);
            BtnRecord = MUi.Big("● 新しく記録", ButtonKind.Secondary, 190);
            BtnPlay.Click += (s, e) => core.PlayMacro(SelectedId, true);
            BtnStopMacro.Click += (s, e) => core.StopAll(RunJob.ReasonUser);
            BtnRecord.Click += (s, e) => core.ToggleRecording();
            recordHint = MUi.Text("", Theme.Small, Theme.SubText);
            recordHint.Padding = new Padding(0, Theme.S(16), 0, 0);
            var bar = BottomBar(BtnPlay, BtnStopMacro, BtnRecord, recordHint);

            page.Controls.Add(right);
            page.Controls.Add(listCard);
            page.Controls.Add(bar);
            return page;
        }

        public MacroDef SelectedMacro { get { return MacroList.Selected as MacroDef; } }
        string SelectedId { get { var m = SelectedMacro; return m == null ? null : m.Id; } }
        public MacroStep SelectedStep { get { return StepList.Selected as MacroStep; } }

        void PaintMacroRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var m = o as MacroDef;
            if (m == null) return;
            int pad = Theme.S(14);
            bool playing = core.State == CoreState.Playing && core.MacroJob != null && core.MacroJob.Macro.Id == m.Id;
            int right = r.Right - Theme.S(10);
            if (playing) right = Draw.Chip(g, "▶ 再生中", Color.White, Theme.Accent, right, r.Y + r.Height / 2, true) - Theme.S(6);
            int top = r.Y + (r.Height - Theme.Bold.Height - Theme.Small.Height - Theme.S(4)) / 2;
            Draw.Text(g, m.Name, Theme.Bold, Theme.Text, new Rectangle(r.X + pad, top, right - r.X - pad, Theme.Bold.Height));
            var hk = core.MacroKey(m);
            var sub = m.Steps.Count + " 手順" + (hk.IsEmpty ? "" : " ・ " + hk.Display);
            Draw.Text(g, sub, Theme.Small, Theme.SubText, new Rectangle(r.X + pad, top + Theme.Bold.Height + Theme.S(4), right - r.X - pad, Theme.Small.Height));
        }

        void PaintStepRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var st = o as MacroStep;
            if (st == null) return;
            var m = SelectedMacro;
            int i = m == null ? -1 : m.Steps.IndexOf(st);
            bool current = core.State == CoreState.Playing && core.MacroJob != null && m != null && core.MacroJob.Macro.Id == m.Id && core.MacroJob.StepIndex == i;
            int x = r.X + Theme.S(12);
            Draw.Text(g, (i + 1).ToString(), Theme.Small, current ? Theme.Accent : Theme.SubText, new Rectangle(x, r.Y, Theme.S(32), r.Height));
            x += Theme.S(34);
            var icon = StepIcon(st.Kind);
            Draw.Text(g, icon, Theme.Body, Theme.Accent, new Rectangle(x, r.Y, Theme.S(24), r.Height));
            x += Theme.S(26);
            int wTitle = Theme.S(190);
            Draw.Text(g, st.Title, current ? Theme.Bold : Theme.Body, Theme.Text, new Rectangle(x, r.Y, wTitle, r.Height));
            x += wTitle + Theme.S(8);
            int delayW = Theme.S(80);
            Draw.Text(g, st.Detail, Theme.Body, Theme.SubText, new Rectangle(x, r.Y, Math.Max(10, r.Right - x - delayW - Theme.S(12)), r.Height));
            if (st.Delay > 0) Draw.Text(g, Fmt_.Delay(st.Delay), Theme.Small, Theme.SubText, new Rectangle(r.Right - delayW - Theme.S(12), r.Y, delayW, r.Height), TextFormatFlags.Right);
        }

        public static string StepIcon(string kind)
        {
            switch (kind)
            {
                case MacroStep.KClick: case MacroStep.KDown: case MacroStep.KUp: return "🖱";
                case MacroStep.KMove: return "➜";
                case MacroStep.KWheel: return "⇅";
                case MacroStep.KKey: case MacroStep.KKeyDown: case MacroStep.KKeyUp: return "⌨";
                case MacroStep.KText: return "Ａ";
                case MacroStep.KWait: return "⏱";
                default: return "・";
            }
        }

        public void RefreshMacros()
        {
            loading = true;
            try
            {
                var keep = S.SelectedMacro;
                MacroList.SetItems(core.Macros.Macros.ToList(), o => ((MacroDef)o).Id == keep);
                var m = SelectedMacro;
                if (m != null && S.SelectedMacro != m.Id) S.SelectedMacro = m.Id;
            }
            finally { loading = false; }
            RefreshSteps();
        }

        public void RefreshSteps()
        {
            var m = SelectedMacro;
            int keepIndex = StepList.SelectedIndex;
            StepList.SetItems(m == null ? new List<MacroStep>() : m.Steps.ToList());
            if (m != null && keepIndex >= 0 && keepIndex < StepList.Items.Count) StepList.SelectedIndex = keepIndex;
            StepList.EmptyText = m == null
                ? "まだマクロがありません。\n\n下の「● 新しく記録」を押して、くり返したい操作をすると、そのまま記録されます。\n終わったら " + (core.RecordKey.IsEmpty ? "" : core.RecordKey.Display + " か ") + "「■ 記録を終える」を押します。\n\n「＋ 新規」で、手順を 1 つずつ作ることもできます。"
                : "手順がありません。「● 新しく記録」で記録するか、「＋ 手順を追加」で作ります。";
            macroTitle.Text = m == null ? "マクロを選んでください" : m.Name;
            macroSummary.Text = m == null ? "" : m.Summary();
            LoadMacroOptions();
            UpdateButtons();
            StepList.Invalidate();
        }

        void LoadMacroOptions()
        {
            var m = SelectedMacro;
            loading = true;
            try
            {
                RepeatBox.Value = m == null || m.Repeat == 0 ? 1 : Math.Max(1, Math.Min(MacroDef.MaxRepeat, m.Repeat));
                ChkForever.Checked = m != null && m.Repeat == 0;
                RepeatBox.Enabled = m != null && m.Repeat != 0;
                int si = m == null ? 2 : Array.IndexOf(MacroDef.Speeds, m.Speed);
                SpeedBox.SelectedIndex = si < 0 ? 2 : si;
                SwRecordMoves.Checked = S.RecordMoves;
                SwHide.Checked = S.HideWhileRunning;
                SwEsc2.Checked = S.EscStops;
                MacroDelayBox.Value = Math.Max(0, Math.Min(10, S.MacroStartDelaySec));
            }
            finally { loading = false; }
            var hk = core.MacroKey(m);
            macroHotkeyLabel.Text = m == null ? "ショートカット: ―" : "このマクロのショートカット: " + (hk.IsEmpty ? "なし" : hk.Display);
            UpdateMacroAdvanced();
        }

        void UpdateMacroAdvanced()
        {
            macroAdvanced.Visible = S.ShowAdvanced;
            BtnMacroAdvanced.Text = (S.ShowAdvanced ? "▾" : "▸") + " くわしい設定";
            BtnMacroAdvanced.FitWidth();
        }

        void ApplyMacroOptions()
        {
            var m = SelectedMacro; if (m == null) return;
            m.Repeat = ChkForever.Checked ? 0 : (int)RepeatBox.Value;
            m.Speed = MacroDef.Speeds[Math.Max(0, SpeedBox.SelectedIndex)];
            core.SaveMacros();
        }

        public MacroDef NewMacro()
        {
            var m = core.NewMacro();
            if (m == null) return null;
            RefreshMacros();
            Flash("「" + m.Name + "」を作りました。「＋ 手順を追加」で手順を足していきます", false);
            return m;
        }

        void BuildMacroMenu()
        {
            MacroMenu.Items.Clear();
            var m = SelectedMacro;
            MacroMenu.Items.Add(new ToolStripMenuItem("名前の変更… (F2)", null, (s, e) => RenameMacro()) { Enabled = m != null });
            MacroMenu.Items.Add(new ToolStripMenuItem("複製", null, (s, e) => DuplicateMacro()) { Enabled = m != null });
            MacroMenu.Items.Add(new ToolStripMenuItem("ショートカットを決める (Teal Hub)…", null, (s, e) => OpenHub()) { Enabled = m != null });
            MacroMenu.Items.Add(new ToolStripSeparator());
            MacroMenu.Items.Add(new ToolStripMenuItem("削除…", null, (s, e) => DeleteMacro()) { Enabled = m != null });
        }

        public void RenameMacro()
        {
            var m = SelectedMacro; if (m == null) return;
            var name = NameHook != null ? NameHook(m.Name) : NameForm.Ask(this, "マクロの名前", m.Name, n => core.Macros.ValidateName(n, m));
            if (name == null) return;
            var why = core.Rename(m, name);
            if (why != null) { Flash(why, true); return; }
            RefreshMacros();
        }

        public void DuplicateMacro()
        {
            var m = core.Duplicate(SelectedMacro);
            if (m != null) { RefreshMacros(); Flash("「" + m.Name + "」を作りました", false); }
        }

        public void DeleteMacro()
        {
            var m = SelectedMacro; if (m == null) return;
            if (!Ask("マクロ「" + m.Name + "」を削除しますか？ (" + m.Steps.Count + " 手順)")) return;
            core.Delete(m);
            RefreshMacros();
            Flash("「" + m.Name + "」を削除しました", false);
        }

        void BuildAddStepMenu()
        {
            AddStepMenu.Items.Clear();
            Action<string, Func<MacroStep>> add = (t, make) => AddStepMenu.Items.Add(new ToolStripMenuItem(t, null, (s, e) => AddStep(make())));
            add("🖱  クリック", () => { var p = core.Api.CursorPos(); return MacroStep.Click(p.X, p.Y, MouseBtn.Left); });
            add("⌨  キーを押す", () => MacroStep.KeyPress("Enter"));
            add("Ａ  文字を入力", () => MacroStep.TypeText(""));
            add("⏱  待つ", () => MacroStep.Wait(1000));
            add("➜  マウスを移動", () => { var p = core.Api.CursorPos(); return MacroStep.Move(p.X, p.Y); });
            add("⇅  ホイール", () => { var p = core.Api.CursorPos(); return new MacroStep { Kind = MacroStep.KWheel, X = p.X, Y = p.Y, Delta = -120 }; });
        }

        /// <summary>手順を足す (選んでいる手順の次に。編集の画面を出して、OK なら)</summary>
        public MacroStep AddStep(MacroStep st)
        {
            var m = SelectedMacro ?? NewMacro();
            if (m == null || st == null) return null;
            if (m.Steps.Count >= MacroDef.MaxSteps) { Flash("手順は " + MacroDef.MaxSteps + " 個までです", true); return null; }
            var r = EditStepHook != null ? EditStepHook(st) : StepForm.Edit(this, st, true, core);
            if (r == null) return null;
            int at = StepList.SelectedIndex >= 0 ? StepList.SelectedIndex + 1 : m.Steps.Count;
            m.Steps.Insert(Math.Min(at, m.Steps.Count), r);
            core.SaveMacros();
            RefreshSteps();
            StepList.SelectedIndex = Math.Min(at, StepList.Items.Count - 1);
            return r;
        }

        public void EditStep()
        {
            var m = SelectedMacro; var st = SelectedStep;
            if (m == null || st == null) return;
            var r = EditStepHook != null ? EditStepHook(st.Clone()) : StepForm.Edit(this, st.Clone(), false, core);
            if (r == null) return;
            int i = m.Steps.IndexOf(st);
            if (i < 0) return;
            m.Steps[i] = r;
            core.SaveMacros();
            RefreshSteps();
            StepList.SelectedIndex = i;
        }

        public void MoveStep(int dir)
        {
            var m = SelectedMacro; var st = SelectedStep;
            if (m == null || st == null) return;
            int i = m.Steps.IndexOf(st), j = i + dir;
            if (i < 0 || j < 0 || j >= m.Steps.Count) return;
            m.Steps.RemoveAt(i); m.Steps.Insert(j, st);
            core.SaveMacros();
            RefreshSteps();
            StepList.SelectedIndex = j;
        }

        public void DeleteStep()
        {
            var m = SelectedMacro;
            if (m == null || StepList.SelectedIndices.Count == 0) return;
            int i = StepList.SelectedIndex;
            m.Steps.RemoveAt(i);
            core.SaveMacros();
            RefreshSteps();
            if (StepList.Items.Count > 0) StepList.SelectedIndex = Math.Min(i, StepList.Items.Count - 1);
        }

        // ================================================================ 状態
        public void RefreshAll()
        {
            LoadClicker();
            RefreshMacros();
            UpdateState();
        }

        public void UpdateState()
        {
            if (IsDisposed || BtnStart == null) return;
            var st = core.State;
            string title = core.StateTitle, detail = core.StateDetail;
            if (st == CoreState.Idle && string.IsNullOrEmpty(detail))
            {
                if (Tabs.SelectedIndex == 0)
                {
                    var why = MacroCore.CheckClicker(C);
                    detail = why ?? (C.Summary() + (core.ToggleKey.IsEmpty ? "" : " ・ " + core.ToggleKey.Display + " で開始"));
                }
                else
                {
                    var m = SelectedMacro;
                    detail = m == null ? "「● 新しく記録」で、くり返したい操作を記録します" : "「" + m.Name + "」 " + m.Summary();
                }
            }
            Banner.Set(st, title, detail);
            bool idle = st == CoreState.Idle;
            bool clickerRun = st == CoreState.Clicking || core.CountdownIsClick;
            BtnStart.Enabled = !clickerRun && st != CoreState.Recording;
            BtnStart.Text = "▶ 開始";
            BtnStop.Enabled = !idle;
            BtnStopMacro.Enabled = !idle;
            bool rec = st == CoreState.Recording;
            BtnRecord.Text = rec ? "■ 記録を終える" : "● 新しく記録";
            BtnRecord.Kind = rec ? ButtonKind.Danger : ButtonKind.Secondary;
            BtnRecord.Enabled = st == CoreState.Idle || rec;
            recordHint.Text = rec ? "" : (core.RecordKey.IsEmpty ? "" : core.RecordKey.Display + " でも記録の開始 / 終了") + (core.StopKey.IsEmpty ? "" : "\n" + core.StopKey.Display + " ですべて停止");
            header.SetDetail(idle ? (C.Points.Count > 0 ? "決めた位置 " + C.Points.Count + " か所 ・ " : "") + "マクロ " + core.Macros.Macros.Count + " 個" : title);
            UpdateStartHint();
            UpdateButtons();
            if (st == CoreState.Playing) { StepList.Invalidate(); MacroList.Invalidate(); }
        }

        void UpdateButtons()
        {
            if (BtnRemovePoint == null || BtnEditStep == null || BtnPlay == null) return;
            BtnRemovePoint.Enabled = PointList.Selected != null;
            BtnClearPoints.Enabled = C.Points.Count > 0;
            bool editable = core.State != CoreState.Playing && core.State != CoreState.Recording;
            var m = SelectedMacro;
            bool hasStep = SelectedStep != null;
            BtnAddStep.Enabled = editable;
            BtnEditStep.Enabled = editable && hasStep;
            BtnDeleteStep.Enabled = editable && hasStep;
            BtnUp.Enabled = editable && hasStep && StepList.SelectedIndex > 0;
            BtnDown.Enabled = editable && hasStep && StepList.SelectedIndex < StepList.Items.Count - 1;
            BtnMacroMenu.Enabled = m != null && editable;
            RepeatBox.Enabled = m != null && !ChkForever.Checked;
            ChkForever.Enabled = SpeedBox.Enabled = m != null;
            BtnMacroHotkey.Enabled = m != null;
            var st = core.State;
            BtnPlay.Enabled = (st == CoreState.Idle || st == CoreState.Clicking) && m != null && m.Steps.Count > 0;
        }

        public void Flash(string msg, bool error)
        {
            LastMessage = msg;
            LastMessageIsError = error;
            footer.StatusText = msg;
        }

        bool Ask(string msg)
        {
            if (ConfirmHook != null) return ConfirmHook(msg);
            return Dialogs.Confirm(this, msg);
        }

        void OpenHub() { if (SettingsRequested != null) SettingsRequested(); }

        public void Shutdown()
        {
            allowClose = true;
            if (Visible) Close();
            if (!IsDisposed) Dispose();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || allowClose || AppHost.IsExiting) return;
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (HideRequested != null) HideRequested(); else Hide();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (core.IsBusy) { core.StopAll(RunJob.ReasonEsc); return true; }
                if (KeyBox.Focused) return base.ProcessCmdKey(ref msg, keyData);
                Close(); return true;
            }
            if (keyData == (Keys.Control | Keys.Tab)) { ShowPage((Tabs.SelectedIndex + 1) % 2); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
