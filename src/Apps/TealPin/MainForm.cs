// Teal Pin (最前面に固定) - メイン画面
//  固定中 / 自動で固定 / 位置とサイズ / 動作 の 4 つのタブ。
//  ×・Esc では閉じずに隠れる (PinTray が通知領域に常駐させる)。本当に閉じるのは Shutdown() のときだけ
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pin
{
    public class MainForm : Form
    {
        readonly PinManager mgr;
        readonly HeaderBar header;
        readonly FooterBar footer = new FooterBar();
        public readonly SegmentTabs Tabs = new SegmentTabs("固定中", "自動で固定", "位置とサイズ", "動作");
        readonly Panel[] pages = new Panel[4];
        public readonly RowList PinList = new RowList(2), RuleList = new RowList(2), PresetList = new RowList(2);
        public FlatButton BtnUnpinAll, BtnFocus, BtnPresetFor, BtnUnpin, BtnPinOther;
        public FlatButton BtnAddRule, BtnEditRule, BtnToggleRule, BtnDeleteRule, BtnScanRules;
        public FlatButton BtnSavePreset, BtnApplyPreset, BtnRenamePreset, BtnDeletePreset, BtnHubSettings;
        public ToggleSwitch SwTitleBar = new ToggleSwitch("タイトルバーの右クリックに「📌 最前面に固定」を出す");
        public ToggleSwitch SwBorder = new ToggleSwitch("固定したウィンドウをティールの枠線で囲む");
        public ToggleSwitch SwToast = new ToggleSwitch("固定した・外したときに小さなお知らせを出す");
        public ToggleSwitch SwKeep = new ToggleSwitch("アプリが最前面をやめても、固定し直す");
        public ToggleSwitch SwRulesOnStart = new ToggleSwitch("起動したとき、開いているウィンドウにもルールを使う");
        public ComboBox BorderWidthBox;
        Label hotkeyNote;
        readonly Dictionary<string, Image> icons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        Image genericIcon;
        bool allowClose, loading;
        string hotkeyText;

        public event Action HideRequested;
        public event Action SettingsRequested;
        /// <summary>設定を保存する処理 (常駐部分)</summary>
        public Action SaveSettings;
        /// <summary>タイトルバーのメニュー・枠線などの設定を変えた (常駐部分で反映する)</summary>
        public Action SettingsChanged;
        public string LastMessage { get; private set; }
        public string FooterText { get { return footer.StatusText; } }
        /// <summary>テスト用: ダイアログの代わり (null ならダイアログを出す)</summary>
        public Func<PinRule, PinRule> EditRuleHook;
        public Func<string, string> PresetNameHook;
        public Func<string, bool> ConfirmHook;
        public readonly ContextMenuStrip PresetMenu = new ContextMenuStrip(), PinOtherMenu = new ContextMenuStrip(), CaptureMenu = new ContextMenuStrip();

        PinSettings S { get { return mgr.Settings; } }

        public string HotkeyText
        {
            get { return hotkeyText; }
            set { hotkeyText = value; UpdateHeader(); UpdateHotkeyNote(); }
        }

        public MainForm(PinManager manager)
        {
            mgr = manager;
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(720, 460);
            KeyPreview = true;
            genericIcon = Glyph("?", Theme.SubText);

            header = HeaderBar.ForApp("");
            var btnSettings = header.AddAction(new FlatButton("設定", ButtonKind.Ghost));
            BtnUnpinAll = header.AddAction(new FlatButton("すべての固定を外す", ButtonKind.Secondary));
            btnSettings.Click += (s, e) => OpenHub();
            new ToolTip().SetToolTip(btnSettings, "ショートカット・常駐・スタートアップの設定 (Teal Hub で開きます)");
            BtnUnpinAll.Click += (s, e) => UnpinAll();

            Tabs.Dock = DockStyle.Top;
            Tabs.SelectedChanged += (s, e) => ShowPage(Tabs.SelectedIndex);

            pages[0] = BuildPinsPage();
            pages[1] = BuildRulesPage();
            pages[2] = BuildPresetsPage();
            pages[3] = BuildBehaviorPage();
            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12) };
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; body.Controls.Add(p); }
            pages[0].Visible = true;

            Controls.Add(body); Controls.Add(Tabs); Controls.Add(footer); Controls.Add(header);
            foreach (var m in new[] { PresetMenu, PinOtherMenu, CaptureMenu }) { m.Font = Theme.Body; m.ShowImageMargin = true; }

            WindowMemory.Attach(this, 760, 620);
            mgr.Changed += OnChanged;
            VisibleChanged += (s, e) => { if (Visible) RefreshAll(); };
            FormClosed += (s, e) => mgr.Changed -= OnChanged;
            RefreshAll();
        }

        void OnChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)OnChanged); } catch { } return; }
            RefreshPins();
        }

        public void ShowPage(int i)
        {
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            if (Tabs.SelectedIndex != i) Tabs.SelectedIndex = i;
            UpdateFooter();
        }

        // ================================================================ 固定中
        Panel BuildPinsPage()
        {
            var page = new Panel();
            var card = new CardPanel("固定中のウィンドウ") { Dock = DockStyle.Fill, Padding = new Padding(1, Theme.S(42), 1, Theme.S(8)) };
            PinList.Dock = DockStyle.Fill;
            PinList.EmptyText = "固定しているウィンドウはありません。\n\n固定したいウィンドウを前面にして、ショートカット (初期設定: Ctrl + Alt + T) を押すか、"
                + "タイトルバーを右クリックして「📌 最前面に固定」を選びます。\n下の「ほかのウィンドウを固定」からも選べます。";
            PinList.ItemHeight = Theme.S(52);
            PinList.PaintRow = PaintPinRow;
            PinList.DoubleClick += (s, e) => FocusSelected();
            PinList.SelectedIndexChanged += (s, e) => UpdateButtons();
            PinList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { UnpinSelected(); e.Handled = true; } else if (e.KeyCode == Keys.Enter) { FocusSelected(); e.Handled = true; } };
            card.Controls.Add(PinList);

            BtnFocus = new FlatButton("前面に出す", ButtonKind.Secondary).FitWidth();
            BtnPresetFor = new FlatButton("位置とサイズ ▾", ButtonKind.Secondary).FitWidth();
            BtnUnpin = new FlatButton("固定を外す", ButtonKind.Primary).FitWidth();
            BtnPinOther = new FlatButton("ほかのウィンドウを固定 ▾", ButtonKind.Ghost).FitWidth();
            BtnFocus.Click += (s, e) => FocusSelected();
            BtnUnpin.Click += (s, e) => UnpinSelected();
            BtnPresetFor.Click += (s, e) =>
            {
                var pw = SelectedPin; if (pw == null) return;
                BuildPresetMenu(PresetMenu, pw.Handle);
                PresetMenu.Show(BtnPresetFor, new Point(0, BtnPresetFor.Height));
            };
            BtnPinOther.Click += (s, e) => { BuildPinOtherMenu(); PinOtherMenu.Show(BtnPinOther, new Point(0, BtnPinOther.Height)); };

            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 10), Padding = Theme.P(0, 10, 0, 0) };
            var right = Lay.Flow(BtnFocus, BtnPresetFor, BtnUnpin);
            right.Dock = DockStyle.Right; right.Width = BtnFocus.Width + BtnPresetFor.Width + BtnUnpin.Width + Theme.S(20);
            BtnPinOther.Dock = DockStyle.Left;
            bar.Controls.Add(right); bar.Controls.Add(BtnPinOther);
            page.Controls.Add(card); page.Controls.Add(bar);
            return page;
        }

        public PinnedWindow SelectedPin { get { return PinList.Selected as PinnedWindow; } }

        void PaintPinRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var pw = o as PinnedWindow;
            if (pw == null) return;
            int pad = Theme.S(14), ic = Theme.S(28);
            var img = IconFor(pw.ExePath, pw.AppName);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, new Rectangle(r.X + pad, r.Y + (r.Height - ic) / 2, ic, ic));
            int x = r.X + pad + ic + Theme.S(12);
            int chipLeft = Draw.Chip(g, "📌 固定中", Theme.Accent, Theme.AccentSoft, r.Right - Theme.S(14), r.Y + r.Height / 2, true);
            int w = chipLeft - x - Theme.S(10);
            int top = r.Y + (r.Height - Theme.Bold.Height - Theme.Small.Height - Theme.S(4)) / 2;
            Draw.Text(g, pw.Title, Theme.Bold, Theme.Text, new Rectangle(x, top, w, Theme.Bold.Height));
            Draw.Text(g, pw.Subtitle, Theme.Small, (pw.Minimized || pw.Hidden || pw.Cloaked) ? Theme.Warning : Theme.SubText, new Rectangle(x, top + Theme.Bold.Height + Theme.S(4), w, Theme.Small.Height));
        }

        public void RefreshPins()
        {
            var cur = SelectedPin;
            IntPtr keep = cur == null ? IntPtr.Zero : cur.Handle;
            PinList.SetItems(mgr.Pins.ToList(), o => ((PinnedWindow)o).Handle == keep);
            UpdateButtons();
            UpdateHeader();
            if (Tabs.SelectedIndex == 0) UpdateFooter();
        }

        public void FocusSelected()
        {
            var pw = SelectedPin; if (pw == null) return;
            if (!mgr.Activate(pw.Handle)) Flash(PinManager.MsgGone);
        }

        public void UnpinSelected()
        {
            var pw = SelectedPin; if (pw == null) return;
            var r = mgr.Unpin(pw.Handle);
            Flash(r.Message);
        }

        public void UnpinAll()
        {
            int n = mgr.UnpinAll();
            Flash(n == 0 ? "固定しているウィンドウはありません" : n + " 個のウィンドウの固定を外しました");
        }

        void BuildPinOtherMenu()
        {
            PinOtherMenu.Items.Clear();
            var list = mgr.Candidates().Where(h => !mgr.IsPinned(h)).ToList();
            foreach (var h in list.Take(30))
            {
                var hh = h;
                var exe = mgr.Api.ExePath(mgr.Api.ProcessId(h));
                var title = mgr.Api.Title(h);
                if (title.Length > 60) title = title.Substring(0, 60) + "…";
                var mi = new ToolStripMenuItem(title, IconFor(exe, null), (s, e) => { var r = mgr.Pin(hh, PinManager.HowList, null); Flash(r.Message); });
                PinOtherMenu.Items.Add(mi);
            }
            if (list.Count == 0) PinOtherMenu.Items.Add(new ToolStripMenuItem("(固定できるウィンドウがありません)") { Enabled = false });
        }

        /// <summary>このウィンドウに使うプリセットのメニュー</summary>
        public void BuildPresetMenu(ContextMenuStrip m, IntPtr h)
        {
            m.Items.Clear();
            bool userStarted = false;
            foreach (var p in S.AllPresets())
            {
                if (!p.BuiltIn && !userStarted) { userStarted = true; m.Items.Add(new ToolStripSeparator()); }
                var pp = p;
                m.Items.Add(new ToolStripMenuItem(p.Name, null, (s, e) => { var r = mgr.ApplyPreset(h, pp); Flash(r.Message); }) { ToolTipText = p.Summary });
            }
            m.ShowItemToolTips = true;
        }

        // ================================================================ 自動で固定
        Panel BuildRulesPage()
        {
            var page = new Panel();
            var card = new CardPanel("自動で固定するルール") { Dock = DockStyle.Fill, Padding = new Padding(1, Theme.S(42), 1, Theme.S(8)) };
            RuleList.Dock = DockStyle.Fill;
            RuleList.EmptyText = "ルールはまだありません。\n\n「追加」で、たとえば「vlc.exe」や「Teams.exe でタイトルに『会議』を含む」ウィンドウを、開いたときに自動で最前面に固定できます。";
            RuleList.ItemHeight = Theme.S(52);
            RuleList.PaintRow = PaintRuleRow;
            RuleList.DoubleClick += (s, e) => EditSelectedRule();
            RuleList.SelectedIndexChanged += (s, e) => UpdateButtons();
            card.Controls.Add(RuleList);

            var note = Lay.Note("ルールは、Teal Pin が起動している (常駐している) 間だけ働きます。ウィンドウが開いたときと、前面に来たときに確かめます。管理者として実行中のアプリはとばします。");
            note.Dock = DockStyle.Bottom;
            note.Padding = Theme.P(2, 6, 2, 0);

            BtnAddRule = new FlatButton("追加…", ButtonKind.Primary).FitWidth();
            BtnEditRule = new FlatButton("編集…", ButtonKind.Secondary).FitWidth();
            BtnToggleRule = new FlatButton("使わない", ButtonKind.Secondary).FitWidth(96);
            BtnDeleteRule = new FlatButton("削除", ButtonKind.Danger).FitWidth();
            BtnScanRules = new FlatButton("今すぐ使う", ButtonKind.Ghost).FitWidth();
            new ToolTip().SetToolTip(BtnScanRules, "今開いているウィンドウにルールを使います");
            BtnAddRule.Click += (s, e) => AddRule();
            BtnEditRule.Click += (s, e) => EditSelectedRule();
            BtnToggleRule.Click += (s, e) => ToggleSelectedRule();
            BtnDeleteRule.Click += (s, e) => DeleteSelectedRule();
            BtnScanRules.Click += (s, e) => { mgr.ForgetRuleHistory(); int n = mgr.ScanRules(); Flash(n == 0 ? "ルールに合う (まだ固定していない) ウィンドウはありませんでした" : n + " 個のウィンドウを固定しました"); };

            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 10), Padding = Theme.P(0, 10, 0, 0) };
            var right = Lay.Flow(BtnAddRule, BtnEditRule, BtnToggleRule, BtnDeleteRule);
            right.Dock = DockStyle.Right; right.Width = BtnAddRule.Width + BtnEditRule.Width + BtnToggleRule.Width + BtnDeleteRule.Width + Theme.S(28);
            BtnScanRules.Dock = DockStyle.Left;
            bar.Controls.Add(right); bar.Controls.Add(BtnScanRules);
            page.Controls.Add(card); page.Controls.Add(bar); page.Controls.Add(note);
            return page;
        }

        public PinRule SelectedRule { get { return RuleList.Selected as PinRule; } }

        void PaintRuleRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var rule = o as PinRule; if (rule == null) return;
            int x = r.X + Theme.S(16);
            int chipLeft = Draw.Chip(g, rule.Enabled ? "使う" : "使わない", rule.Enabled ? Theme.Accent : Theme.SubText, rule.Enabled ? Theme.AccentSoft : Theme.SurfaceAlt, r.Right - Theme.S(14), r.Y + r.Height / 2, true);
            int w = chipLeft - x - Theme.S(10);
            int top = r.Y + (r.Height - Theme.Bold.Height - Theme.Small.Height - Theme.S(4)) / 2;
            Draw.Text(g, rule.Describe(), Theme.Bold, rule.Enabled ? Theme.Text : Theme.SubText, new Rectangle(x, top, w, Theme.Bold.Height));
            var p = S.FindPreset(rule.PresetId);
            var sub = "位置とサイズ: " + (p == null ? (string.IsNullOrEmpty(rule.PresetId) ? "変えない" : "(削除されたプリセット)") : p.Name);
            Draw.Text(g, sub, Theme.Small, Theme.SubText, new Rectangle(x, top + Theme.Bold.Height + Theme.S(4), w, Theme.Small.Height));
        }

        List<RunningApp> Running()
        {
            var list = new List<RunningApp>();
            try
            {
                foreach (var h in mgr.Candidates())
                {
                    var exe = mgr.Api.ExePath(mgr.Api.ProcessId(h));
                    if (string.IsNullOrEmpty(exe)) continue;
                    string name;
                    try { name = mgr.Api.AppName(exe); } catch { name = null; }
                    list.Add(new RunningApp { Exe = RuleMatcher.NormalizeExe(exe), AppName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name.Trim(), Title = mgr.Api.Title(h) });
                }
            }
            catch (Exception ex) { DataStore.Log("running apps: " + ex.Message); }
            return list;
        }

        PinRule EditRule(PinRule r)
        {
            if (EditRuleHook != null) return EditRuleHook(r);
            using (var f = new RuleForm(r, S.AllPresets(), Running()))
                return f.ShowDialog(this) == DialogResult.OK ? f.Result : null;
        }

        public void AddRule()
        {
            if (S.Rules.Count >= PinSettings.MaxRules) { Flash("ルールは " + PinSettings.MaxRules + " 個までです"); return; }
            var r = EditRule(null);
            if (r == null) return;
            S.Rules.Add(r);
            SaveAndRefresh("ルールを追加しました: " + r.Describe());
            RuleList.SetItems(S.Rules.ToList(), o => o == r);
            mgr.ScanRules();
        }

        public void EditSelectedRule()
        {
            var cur = SelectedRule; if (cur == null) return;
            var r = EditRule(cur);
            if (r == null) return;
            int i = S.Rules.IndexOf(cur);
            if (i < 0) return;
            S.Rules[i] = r;
            mgr.ForgetRuleHistory();
            SaveAndRefresh("ルールを保存しました: " + r.Describe());
            RuleList.SetItems(S.Rules.ToList(), o => o == r);
            mgr.ScanRules();
        }

        public void ToggleSelectedRule()
        {
            var cur = SelectedRule; if (cur == null) return;
            cur.Enabled = !cur.Enabled;
            if (cur.Enabled) { mgr.ForgetRuleHistory(); }
            SaveAndRefresh(cur.Enabled ? "ルールを使うようにしました" : "ルールを使わないようにしました");
            if (cur.Enabled) mgr.ScanRules();
        }

        public void DeleteSelectedRule()
        {
            var cur = SelectedRule; if (cur == null) return;
            if (!Confirm("ルール「" + cur.Describe() + "」を削除しますか?\n(今固定しているウィンドウは、固定したままです)")) return;
            S.Rules.Remove(cur);
            SaveAndRefresh("ルールを削除しました");
        }

        // ================================================================ 位置とサイズ
        Panel BuildPresetsPage()
        {
            var page = new Panel();
            var card = new CardPanel("位置とサイズのプリセット") { Dock = DockStyle.Fill, Padding = new Padding(1, Theme.S(42), 1, Theme.S(8)) };
            PresetList.Dock = DockStyle.Fill;
            PresetList.ItemHeight = Theme.S(52);
            PresetList.PaintRow = PaintPresetRow;
            PresetList.SelectedIndexChanged += (s, e) => UpdateButtons();
            card.Controls.Add(PresetList);
            var note = Lay.Note("ウィンドウがあるモニターの作業領域 (タスクバーを除いた範囲) に合わせて計算します。固定中の一覧・通知領域のメニュー・自動で固定するルールから使えます。");
            note.Dock = DockStyle.Bottom; note.Padding = Theme.P(2, 6, 2, 0);

            BtnApplyPreset = new FlatButton("ウィンドウに使う ▾", ButtonKind.Primary).FitWidth();
            BtnRenamePreset = new FlatButton("名前を変える…", ButtonKind.Secondary).FitWidth();
            BtnDeletePreset = new FlatButton("削除", ButtonKind.Danger).FitWidth();
            BtnSavePreset = new FlatButton("ウィンドウの今の位置を保存 ▾", ButtonKind.Ghost).FitWidth();
            BtnApplyPreset.Click += (s, e) =>
            {
                var p = SelectedPreset; if (p == null) return;
                BuildWindowMenu(PresetMenu, h => { var r = mgr.ApplyPreset(h, p); Flash(r.Message); }, true);
                PresetMenu.Show(BtnApplyPreset, new Point(0, BtnApplyPreset.Height));
            };
            BtnSavePreset.Click += (s, e) =>
            {
                BuildWindowMenu(CaptureMenu, h => SavePresetFrom(h), false);
                CaptureMenu.Show(BtnSavePreset, new Point(0, BtnSavePreset.Height));
            };
            BtnRenamePreset.Click += (s, e) => RenameSelectedPreset();
            BtnDeletePreset.Click += (s, e) => DeleteSelectedPreset();

            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 10), Padding = Theme.P(0, 10, 0, 0) };
            var right = Lay.Flow(BtnApplyPreset, BtnRenamePreset, BtnDeletePreset);
            right.Dock = DockStyle.Right; right.Width = BtnApplyPreset.Width + BtnRenamePreset.Width + BtnDeletePreset.Width + Theme.S(20);
            BtnSavePreset.Dock = DockStyle.Left;
            bar.Controls.Add(right); bar.Controls.Add(BtnSavePreset);
            page.Controls.Add(card); page.Controls.Add(bar); page.Controls.Add(note);
            return page;
        }

        public PinPreset SelectedPreset { get { return PresetList.Selected as PinPreset; } }

        void PaintPresetRow(Graphics g, Rectangle r, object o, bool sel)
        {
            var p = o as PinPreset; if (p == null) return;
            int pad = Theme.S(14);
            // 画面の中のどこか、の小さな図
            var mon = new Rectangle(r.X + pad, r.Y + (r.Height - Theme.S(30)) / 2, Theme.S(48), Theme.S(30));
            using (var b = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(b, mon);
            using (var pen = new Pen(Theme.BorderStrong)) g.DrawRectangle(pen, mon);
            var inner = PresetMath.Compute(p, new Rectangle(0, 0, 1920, 1032), 1.0);
            if (!inner.IsEmpty)
            {
                var ir = new Rectangle(mon.X + 1 + inner.X * (mon.Width - 2) / 1920, mon.Y + 1 + inner.Y * (mon.Height - 2) / 1032,
                    Math.Max(3, inner.Width * (mon.Width - 2) / 1920), Math.Max(3, inner.Height * (mon.Height - 2) / 1032));
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, ir);
            }
            int x = mon.Right + Theme.S(14);
            int chipLeft = p.BuiltIn ? Draw.Chip(g, "組み込み", Theme.SubText, Theme.SurfaceAlt, r.Right - Theme.S(14), r.Y + r.Height / 2, true) : r.Right - Theme.S(14);
            int w = chipLeft - x - Theme.S(10);
            int top = r.Y + (r.Height - Theme.Bold.Height - Theme.Small.Height - Theme.S(4)) / 2;
            Draw.Text(g, p.Name, Theme.Bold, Theme.Text, new Rectangle(x, top, w, Theme.Bold.Height));
            Draw.Text(g, p.Summary, Theme.Small, Theme.SubText, new Rectangle(x, top + Theme.Bold.Height + Theme.S(4), w, Theme.Small.Height));
        }

        /// <summary>ウィンドウを選ぶメニュー (固定中のものを先に)</summary>
        void BuildWindowMenu(ContextMenuStrip m, Action<IntPtr> pick, bool pinnedFirst)
        {
            m.Items.Clear();
            var pinned = mgr.Pins.Select(p => p.Handle).ToList();
            var others = mgr.Candidates().Where(h => !mgr.IsPinned(h)).Take(25).ToList();
            foreach (var h in pinned.Concat(others))
            {
                var hh = h;
                var title = mgr.Api.Title(h);
                if (title.Length > 60) title = title.Substring(0, 60) + "…";
                var exe = mgr.Api.ExePath(mgr.Api.ProcessId(h));
                m.Items.Add(new ToolStripMenuItem((mgr.IsPinned(h) ? "📌 " : "") + title, IconFor(exe, null), (s, e) => pick(hh)));
            }
            if (m.Items.Count == 0) m.Items.Add(new ToolStripMenuItem("(選べるウィンドウがありません)") { Enabled = false });
        }

        string AskName(string title, string initial, string note, PinPreset self)
        {
            Func<string, string> validate = n => PresetMath.ValidateName(n, S.AllPresets(), self);
            if (PresetNameHook != null)
            {
                var n = PresetNameHook(initial);
                if (n == null) return null;
                var err = validate(n);
                if (err != null) { Flash(err); return null; }
                return n.Trim();
            }
            using (var f = new PresetNameForm(title, initial, note, validate))
                return f.ShowDialog(this) == DialogResult.OK ? f.PresetName : null;
        }

        public void SavePresetFrom(IntPtr h)
        {
            if (S.Presets.Count >= PinSettings.MaxPresets) { Flash("プリセットは " + PinSettings.MaxPresets + " 個までです"); return; }
            var name = AskName("今の位置とサイズをプリセットに保存", "", "「" + mgr.Api.Title(h) + "」の今の位置と大きさを、画面に対する割合で覚えます。", null);
            if (name == null) return;
            var p = mgr.CapturePreset(h, name);
            if (p == null) { Flash(PinManager.MsgGone); return; }
            S.Presets.Add(p);
            SaveAndRefresh("プリセット「" + p.Name + "」を保存しました");
            PresetList.SetItems(S.AllPresets(), o => ((PinPreset)o).Id == p.Id);
        }

        public void RenameSelectedPreset()
        {
            var p = SelectedPreset; if (p == null || p.BuiltIn) return;
            var name = AskName("プリセットの名前を変える", p.Name, "", p);
            if (name == null) return;
            p.Name = name;
            SaveAndRefresh("名前を変えました");
        }

        public void DeleteSelectedPreset()
        {
            var p = SelectedPreset; if (p == null || p.BuiltIn) return;
            int used = S.Rules.Count(r => r.PresetId == p.Id);
            if (!Confirm("プリセット「" + p.Name + "」を削除しますか?" + (used > 0 ? "\n(使っているルール " + used + " 個は「位置とサイズを変えない」になります)" : ""))) return;
            S.Presets.Remove(p);
            foreach (var r in S.Rules) if (r.PresetId == p.Id) r.PresetId = null;
            SaveAndRefresh("プリセットを削除しました");
        }

        // ================================================================ 動作
        Panel BuildBehaviorPage()
        {
            var page = new Panel { AutoScroll = true };
            var card = new CardPanel("動作") { Dock = DockStyle.Fill, Padding = Theme.P(16, 44, 16, 12) };
            BorderWidthBox = Lay.Combo("1 px", "2 px", "3 px (おすすめ)", "4 px", "5 px", "6 px", "7 px", "8 px");
            BorderWidthBox.Width = Theme.S(160);
            var widthRow = new Panel { Height = BorderWidthBox.Height + Theme.S(4), BackColor = Color.Transparent };
            var widthLabel = new Label { Text = "枠線の太さ", AutoSize = false, Width = Theme.S(120), Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Body, ForeColor = Theme.Text, Padding = Theme.P(46, 0, 0, 0) };
            BorderWidthBox.Dock = DockStyle.Left;
            widthLabel.Width = Theme.S(46 + 90);
            widthRow.Controls.Add(BorderWidthBox); widthRow.Controls.Add(widthLabel);

            hotkeyNote = (Label)Lay.Note("");
            BtnHubSettings = new FlatButton("ショートカット・常駐の設定 (Teal Hub)…", ButtonKind.Secondary).FitWidth();
            BtnHubSettings.Click += (s, e) => OpenHub();
            var hubRow = Lay.Flow(BtnHubSettings);

            foreach (var sw in new[] { SwTitleBar, SwBorder, SwToast, SwKeep, SwRulesOnStart })
            {
                sw.AutoSize = false; sw.Height = Theme.S(30);
                sw.CheckedChanged += (s, e) => ApplyToggles();
            }
            BorderWidthBox.SelectedIndexChanged += (s, e) => ApplyToggles();

            Lay.Stack(card,
                Lay.SectionLabel("固定のしかた"),
                SwTitleBar,
                Lay.Note("タイトルバーを右クリックすると、開いたメニューのすぐ上に Teal Pin のボタンが出ます (ほかのアプリには何も組み込みません)。"),
                Ui.Spacer(8),
                hotkeyNote,
                Ui.Spacer(4),
                hubRow,
                Ui.Spacer(12),
                Lay.SectionLabel("見た目"),
                SwBorder,
                widthRow,
                SwToast,
                Ui.Spacer(12),
                Lay.SectionLabel("そのほか"),
                SwKeep,
                SwRulesOnStart,
                Ui.Spacer(10),
                Lay.Note("管理者として実行中のアプリのウィンドウは、Windows のしくみ上、ふつうに起動した Teal Pin からは固定できません (固定しようとすると、そのことをお知らせします)。"
                    + "Teal Pin を終了すると、固定はすべて外れます。"));
            // 画面が低いときはスクロールできるように、カードの高さを中身に合わせる (Fill だと下が切れる)
            card.Dock = DockStyle.Top;
            bool sizing = false;
            EventHandler fit = (s, e) =>
            {
                if (sizing) return;
                sizing = true;
                try
                {
                    int need = 0;
                    foreach (Control c in card.Controls) if (c.Visible) need = Math.Max(need, c.Bottom);
                    need += card.Padding.Bottom;
                    int h = Math.Max(page.ClientSize.Height, need);
                    if (card.Height != h) card.Height = h;
                }
                finally { sizing = false; }
            };
            page.Resize += fit;
            card.Layout += (s, e) => fit(s, EventArgs.Empty);
            page.Controls.Add(card);
            return page;
        }

        void LoadToggles()
        {
            loading = true;
            SwTitleBar.Checked = S.TitleBarMenu;
            SwBorder.Checked = S.ShowBorder;
            SwToast.Checked = S.Toast;
            SwKeep.Checked = S.KeepTopmost;
            SwRulesOnStart.Checked = S.RulesOnStart;
            BorderWidthBox.SelectedIndex = Math.Max(0, Math.Min(7, S.BorderWidth - 1));
            BorderWidthBox.Enabled = S.ShowBorder;
            loading = false;
        }

        void ApplyToggles()
        {
            if (loading) return;
            S.TitleBarMenu = SwTitleBar.Checked;
            S.ShowBorder = SwBorder.Checked;
            S.Toast = SwToast.Checked;
            S.KeepTopmost = SwKeep.Checked;
            S.RulesOnStart = SwRulesOnStart.Checked;
            S.BorderWidth = BorderWidthBox.SelectedIndex + 1;
            BorderWidthBox.Enabled = S.ShowBorder;
            mgr.RefreshBorders();
            Save();
            if (SettingsChanged != null) SettingsChanged();
            Flash("設定を保存しました");
        }

        /// <summary>常駐部分 (通知領域のメニュー) で設定を変えたとき</summary>
        public void ReloadSettings() { LoadToggles(); RefreshAll(); }

        void UpdateHotkeyNote()
        {
            if (hotkeyNote == null) return;
            hotkeyNote.Text = string.IsNullOrEmpty(hotkeyText)
                ? "ショートカットはオフです。Teal Hub の設定でキーを決めると、前面のウィンドウをキーだけで固定 / 解除できます。"
                : "ショートカット: " + hotkeyText + " … いま前面にあるウィンドウを固定します (もう一度押すと外します)。キーは Teal Hub の設定で変えたり、オフにしたりできます。";
        }

        // ================================================================ 共通
        void OpenHub() { if (SettingsRequested != null) SettingsRequested(); }

        void Save() { if (SaveSettings != null) SaveSettings(); }

        void SaveAndRefresh(string msg)
        {
            Save();
            RefreshAll();
            Flash(msg);
        }

        bool Confirm(string msg) { return ConfirmHook != null ? ConfirmHook(msg) : Dialogs.Confirm(this, msg); }

        public void Flash(string msg)
        {
            LastMessage = msg;
            footer.StatusText = msg;
        }

        public void RefreshAll()
        {
            RefreshPins();
            var r = SelectedRule;
            RuleList.SetItems(S.Rules.ToList(), o => o == r);
            var p = SelectedPreset;
            string pid = p == null ? null : p.Id;
            PresetList.SetItems(S.AllPresets(), o => ((PinPreset)o).Id == pid);
            LoadToggles();
            UpdateHotkeyNote();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool pin = SelectedPin != null;
            BtnFocus.Enabled = BtnPresetFor.Enabled = BtnUnpin.Enabled = pin;
            BtnUnpinAll.Enabled = mgr.Count > 0;
            var rule = SelectedRule;
            BtnEditRule.Enabled = BtnToggleRule.Enabled = BtnDeleteRule.Enabled = rule != null;
            BtnToggleRule.Text = rule != null && !rule.Enabled ? "使う" : "使わない";
            BtnAddRule.Enabled = S.Rules.Count < PinSettings.MaxRules;
            BtnScanRules.Enabled = S.Rules.Any(x => x.Enabled);
            var ps = SelectedPreset;
            BtnApplyPreset.Enabled = ps != null;
            BtnRenamePreset.Enabled = BtnDeletePreset.Enabled = ps != null && !ps.BuiltIn;
        }

        void UpdateHeader()
        {
            if (header == null) return;
            var parts = new List<string>();
            parts.Add(mgr.Count == 0 ? "固定なし" : mgr.Count + " 個を固定中");
            if (!string.IsNullOrEmpty(hotkeyText)) parts.Add(hotkeyText + " で固定 / 解除");
            header.SetDetail(string.Join("   ・   ", parts));
        }

        void UpdateFooter()
        {
            int i = Tabs.SelectedIndex;
            string t;
            if (i == 0) t = mgr.Count == 0 ? "ダブルクリックで前面に出す ・ Delete で固定を外す" : mgr.Count + " 個を固定中 ・ ダブルクリックで前面に出す ・ Delete で固定を外す";
            else if (i == 1) t = S.Rules.Count + " 個のルール (使う: " + S.Rules.Count(r => r.Enabled) + " 個)";
            else if (i == 2) t = PresetMath.BuiltIns().Count + " 個の組み込み + " + S.Presets.Count + " 個の自分のプリセット";
            else t = "設定は変えるとすぐに保存されます";
            footer.StatusText = t;
        }

        // ---------------------------------------------------------------- アイコン
        public Image IconFor(string exe, string appName)
        {
            if (string.IsNullOrEmpty(exe)) return genericIcon;
            Image img;
            if (icons.TryGetValue(exe, out img)) return img;
            img = null;
            try
            {
                if (AppInfo.IsWindows && File.Exists(exe))
                    using (var ico = Icon.ExtractAssociatedIcon(exe))
                        if (ico != null) using (var big = new Icon(ico, 32, 32)) img = big.ToBitmap();
            }
            catch { img = null; }
            if (img == null)
            {
                var n = string.IsNullOrEmpty(appName) ? Path.GetFileNameWithoutExtension(exe) : appName;
                img = Glyph(string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(), Theme.Accent);
            }
            if (icons.Count > 200) icons.Clear();
            icons[exe] = img;
            return img;
        }

        static Image Glyph(string letter, Color c)
        {
            int px = Theme.S(32);
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.RoundRect(new RectangleF(0, 0, px - 1, px - 1), px * 0.26f))
                using (var b = new SolidBrush(Theme.Blend(c, Color.White, 0.82f))) g.FillPath(b, path);
                TextRenderer.DrawText(g, letter, Theme.Bold, new Rectangle(0, 0, px, px), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            return bmp;
        }

        // ---------------------------------------------------------------- 閉じる
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
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.F5) { RefreshAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
