// Teal Macro - ダイアログ (手順の編集・名前) と、画面から位置を選ぶ全画面の重ね表示、実行中の小さな表示
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    /// <summary>手順を 1 つ編集する。種類を変えると、その種類に要る欄だけが出る</summary>
    public class StepForm : Form
    {
        static readonly string[] Kinds = { MacroStep.KClick, MacroStep.KKey, MacroStep.KText, MacroStep.KWait, MacroStep.KMove, MacroStep.KWheel, MacroStep.KDown, MacroStep.KUp, MacroStep.KKeyDown, MacroStep.KKeyUp };
        static readonly string[] KindNames = { "クリック", "キーを押す", "文字を入力", "待つ", "マウスを移動", "ホイール", "マウスのボタンを押したままにする", "マウスのボタンを離す", "キーを押したままにする", "キーを離す" };
        public readonly ComboBox KindBox, ButtonBox, ClicksBox, WheelDirBox;
        public readonly NumBox XBox, YBox, MsBox, DelayBox, WheelBox;
        public readonly KeyCaptureBox KeyBox = new KeyCaptureBox();
        public readonly TextBox TextBox_ = new TextBox();
        public readonly FlatButton OkButton, PickButton;
        public readonly Label ErrorLabel;
        Panel posRow, btnRow, keyRow, textRow, waitRow, wheelRow;
        readonly MacroStep src;
        readonly MacroCore core;
        public MacroStep Result { get; private set; }

        public StepForm(MacroStep step, bool isNew, MacroCore core)
        {
            src = step ?? MacroStep.Wait(1000);
            this.core = core;
            DialogKit.Setup(this, isNew ? "手順を追加" : "手順を編集");
            AutoSize = false;
            ClientSize = Theme.S(500, 400);
            Padding = Theme.P(16, 12, 16, 12);

            KindBox = Lay.Combo(KindNames);
            KindBox.Width = Theme.S(300);
            KindBox.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, src.Kind));
            KindBox.SelectedIndexChanged += (s, e) => UpdateRows();

            XBox = MUi.Num(-100000, 100000, src.X, 90);
            YBox = MUi.Num(-100000, 100000, src.Y, 90);
            PickButton = new FlatButton("📍 画面から選ぶ", ButtonKind.Secondary).FitWidth();
            PickButton.Click += (s, e) => Pick();
            posRow = MUi.Line(MUi.Text("位置  X"), XBox, MUi.Text("Y"), YBox, PickButton);

            ButtonBox = Lay.Combo("左ボタン", "右ボタン", "中ボタン (ホイール)");
            ButtonBox.Width = Theme.S(170);
            ButtonBox.SelectedIndex = (int)src.Btn;
            ClicksBox = Lay.Combo("1 回", "ダブルクリック");
            ClicksBox.Width = Theme.S(140);
            ClicksBox.SelectedIndex = src.Clicks >= 2 ? 1 : 0;
            btnRow = MUi.Line(MUi.Text("ボタン"), ButtonBox, ClicksBox);

            KeyBox.KeyValue = src.Key ?? "";
            keyRow = MUi.Line(MUi.Text("キー"), new InputFrame(KeyBox) { Width = Theme.S(200) }, MUi.Text("欄をクリックしてキーを押す", Theme.Small, Theme.SubText));

            TextBox_.Multiline = true; TextBox_.Font = Theme.Body; TextBox_.ScrollBars = ScrollBars.Vertical; TextBox_.AcceptsReturn = true;
            TextBox_.MaxLength = MacroDef.MaxText;
            TextBox_.Text = (src.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            var tf = new InputFrame(TextBox_, true) { Dock = DockStyle.Fill };
            textRow = new Panel { Height = Theme.S(96), BackColor = Color.Transparent };
            textRow.Controls.Add(tf);

            MsBox = MUi.Num(0, MacroDef.MaxWaitMs, src.Kind == MacroStep.KWait ? src.Ms : 1000, 110);
            MsBox.Increment = 100;
            waitRow = MUi.Line(MUi.Text("待つ時間"), MsBox, MUi.Text("ミリ秒 (1000 = 1 秒)", Theme.Small, Theme.SubText));

            WheelDirBox = Lay.Combo("下へ", "上へ");
            WheelDirBox.Width = Theme.S(90);
            WheelDirBox.SelectedIndex = src.Delta > 0 ? 1 : 0;
            WheelBox = MUi.Num(1, 50, Math.Max(1, Math.Abs(src.Delta) / 120), 70);
            wheelRow = MUi.Line(MUi.Text("ホイール"), WheelDirBox, WheelBox, MUi.Text("目もり"));

            DelayBox = MUi.Num(0, MacroDef.MaxWaitMs, src.Delay, 110);
            DelayBox.Increment = 100;
            var delayRow = MUi.Line(MUi.Text("この手順の前に"), DelayBox, MUi.Text("ミリ秒待つ", Theme.Small, Theme.SubText));

            ErrorLabel = (Label)Lay.Note("", Theme.Danger);
            OkButton = new FlatButton(isNew ? "追加" : "保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            OkButton.Click += (s, e) => Accept();
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(OkButton, cancel));

            Lay.Stack(this,
                Lay.SectionLabel("何をする？"),
                MUi.Line(KindBox),
                Ui.Spacer(6),
                posRow, btnRow, keyRow, textRow, waitRow, wheelRow,
                Ui.Spacer(6),
                delayRow,
                ErrorLabel);
            Controls.Add(bar);
            UpdateRows();
        }

        public string Kind { get { return Kinds[Math.Max(0, KindBox.SelectedIndex)]; } }

        static bool UsesPos(string k) { return k == MacroStep.KClick || k == MacroStep.KMove || k == MacroStep.KDown || k == MacroStep.KUp || k == MacroStep.KWheel; }
        static bool UsesButton(string k) { return k == MacroStep.KClick || k == MacroStep.KDown || k == MacroStep.KUp; }
        static bool UsesKey(string k) { return k == MacroStep.KKey || k == MacroStep.KKeyDown || k == MacroStep.KKeyUp; }

        void UpdateRows()
        {
            var k = Kind;
            posRow.Visible = UsesPos(k);
            btnRow.Visible = UsesButton(k);
            ClicksBox.Visible = k == MacroStep.KClick;
            keyRow.Visible = UsesKey(k);
            textRow.Visible = k == MacroStep.KText;
            waitRow.Visible = k == MacroStep.KWait;
            wheelRow.Visible = k == MacroStep.KWheel;
            ErrorLabel.Text = "";
        }

        void Pick()
        {
            if (core == null || core.PickOnScreen == null) { var p = core == null ? Cursor.Position : core.Api.CursorPos(); XBox.Value = p.X; YBox.Value = p.Y; return; }
            Hide();
            core.PickOnScreen(t =>
            {
                if (t != null) { XBox.Value = Math.Max(XBox.Minimum, Math.Min(XBox.Maximum, t.X)); YBox.Value = Math.Max(YBox.Minimum, Math.Min(YBox.Maximum, t.Y)); }
                Show();
                Activate();
            });
        }

        /// <summary>入力を手順にする (だめなら理由を出して null)</summary>
        public MacroStep Build()
        {
            var k = Kind;
            var r = src.Clone();
            r.Kind = k;
            r.Delay = (int)DelayBox.Value;
            if (UsesPos(k))
            {
                int nx = (int)XBox.Value, ny = (int)YBox.Value;
                if (k == MacroStep.KMove && (nx != src.X || ny != src.Y || src.Kind != k)) r.Path = null;   // 位置を変えたら、記録した動きは使わない
                r.X = nx; r.Y = ny;
            }
            if (k != MacroStep.KMove) r.Path = null;
            r.Button = UsesButton(k) ? MacroStep.BtnName((MouseBtn)Math.Max(0, ButtonBox.SelectedIndex)) : null;
            r.Clicks = k == MacroStep.KClick ? (ClicksBox.SelectedIndex == 1 ? 2 : 1) : 0;
            r.Key = UsesKey(k) ? KeyBox.KeyValue : null;
            r.Text = k == MacroStep.KText ? TextBox_.Text.Replace("\r\n", "\n") : null;
            r.Ms = k == MacroStep.KWait ? (int)MsBox.Value : 0;
            r.Delta = k == MacroStep.KWheel ? (WheelDirBox.SelectedIndex == 1 ? 1 : -1) * 120 * (int)WheelBox.Value : 0;
            if (UsesKey(k) && string.IsNullOrEmpty(r.Key)) { ErrorLabel.Text = "キーを決めてください (欄をクリックして、キーを押します)"; return null; }
            if (k == MacroStep.KText && string.IsNullOrEmpty(r.Text)) { ErrorLabel.Text = "入力する文字を書いてください"; return null; }
            r.Normalize();
            if (!r.IsValid) { ErrorLabel.Text = "この手順は使えません"; return null; }
            return r;
        }

        void Accept()
        {
            var r = Build();
            if (r == null) return;
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && !TextBox_.Focused && !KeyBox.Focused) { Accept(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static MacroStep Edit(IWin32Window owner, MacroStep step, bool isNew, MacroCore core)
        {
            using (var f = new StepForm(step, isNew, core))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }
    }

    /// <summary>名前を入れる小さなダイアログ</summary>
    public class NameForm : Form
    {
        public readonly TextBox NameBox = new TextBox();
        public readonly Label ErrorLabel;
        readonly Func<string, string> validate;
        public string Result { get; private set; }

        public NameForm(string title, string value, Func<string, string> validate)
        {
            this.validate = validate;
            DialogKit.Setup(this, title);
            AutoSize = false;
            ClientSize = Theme.S(380, 150);
            Padding = Theme.P(16, 12, 16, 12);
            NameBox.Font = Theme.Body; NameBox.MaxLength = MacroDef.MaxNameLength; NameBox.Text = value ?? "";
            ErrorLabel = (Label)Lay.Note("", Theme.Danger);
            var ok = new FlatButton("OK", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => Accept();
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(ok, cancel));
            Lay.Stack(this, Lay.SectionLabel("名前"), new InputFrame(NameBox), ErrorLabel);
            Controls.Add(bar);
            Shown += (s, e) => { NameBox.SelectAll(); NameBox.Focus(); };
        }

        void Accept()
        {
            var why = validate == null ? null : validate(NameBox.Text);
            if (why != null) { ErrorLabel.Text = why; return; }
            Result = NameBox.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        public static string Ask(IWin32Window owner, string title, string value, Func<string, string> validate)
        {
            using (var f = new NameForm(title, value, validate))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Result : null;
        }
    }

    /// <summary>
    /// 画面から位置を選ぶ: 画面全体を薄く暗くして、クリックした場所を記録する (Esc・右クリックで取り消し)。
    /// 位置は物理 px (IInputApi.CursorPos) で取り、重ね表示を消してからその場所のウィンドウを調べる
    /// </summary>
    public class PickOverlay : Form
    {
        readonly IInputApi api;
        readonly Action<TargetPoint> done;
        bool finished;
        readonly Label tip;

        public PickOverlay(IInputApi api, Action<TargetPoint> done)
        {
            this.api = api; this.done = done;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.Black;
            Opacity = 0.28;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            tip = new Label
            {
                AutoSize = true, Font = Theme.Header, ForeColor = Color.White, BackColor = Theme.Accent, Padding = Theme.P(16, 10, 16, 10),
                Text = "クリックしたい場所をクリックしてください   (Esc・右クリックで取り消し)", UseMnemonic = false
            };
            Controls.Add(tip);
            Shown += (s, e) =>
            {
                var scr = Screen.FromPoint(Cursor.Position).Bounds;
                tip.Location = new Point(scr.X - Bounds.X + (scr.Width - tip.Width) / 2, scr.Y - Bounds.Y + Theme.S(40));
                Activate();
            };
            tip.MouseDown += (s, e) => OnPick(e.Button);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); OnPick(e.Button); }

        void OnPick(MouseButtons b)
        {
            if (finished) return;
            if (b != MouseButtons.Left) { Finish(null); return; }
            var p = api.CursorPos();
            Hide();
            Application.DoEvents();
            TargetPoint t;
            try { t = WindowMatch.Make(p, api.WindowAt(p)); }
            catch { t = new TargetPoint { X = p.X, Y = p.Y }; }
            Finish(t);
        }

        void Finish(TargetPoint t)
        {
            if (finished) return;
            finished = true;
            try { Close(); } catch { }
            if (done != null) done(t);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Finish(null); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); }
    }

    /// <summary>
    /// 実行中・記録中・カウントダウン中に画面の右下に出る小さな表示 (いつも手前・フォーカスを取らない)。
    /// メインの画面を最小化していても、いまの状態と「■ 停止」がここで見える
    /// </summary>
    public class RunBar : Form
    {
        public readonly StatusBanner Banner = new StatusBanner();
        public readonly FlatButton BtnStop;
        public event Action StopClicked;

        public RunBar()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Background;
            Font = Theme.Body;
            Size = Theme.S(580, 64);
            Padding = Theme.P(6);
            BtnStop = new FlatButton("■ 停止", ButtonKind.Danger) { Dock = DockStyle.Right, Width = Theme.S(90) };
            BtnStop.Click += (s, e) => { if (StopClicked != null) StopClicked(); };
            Banner.Dock = DockStyle.Fill;
            Controls.Add(Banner);
            Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(6), BackColor = Color.Transparent });
            Controls.Add(BtnStop);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (AppInfo.IsWindows) cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
                return cp;
            }
        }

        public void SetState(CoreState st, string title, string detail)
        {
            Banner.Set(st, title, detail);
            BtnStop.Text = st == CoreState.Recording ? "■ 終える" : "■ 停止";
        }

        /// <summary>マウスのある画面の作業領域の右下に置く</summary>
        public void PlaceBottomRight()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - Theme.S(16), wa.Bottom - Height - Theme.S(16));
        }
    }
}
