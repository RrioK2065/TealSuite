// TealSuite 共通: 全体ショートカットとスタートアップの設定画面 (Teal Clip・Teal Volume・Teal Hub)
//  - ショートカットは IHotkeyTarget (HotkeyController など) で登録し直す (失敗したら元に戻して理由を表示)
//  - 「解除」でショートカットなしにできる (保存値 "none")
//  - startupOnNote が null ならスタートアップの欄は出さない (Teal Hub からほかのアプリのショートカットを変えるとき)
//  - スタートアップは Teal Hub のカードのスイッチと同じ Run キーの値を読み書きする (Startup.cs)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TealSuite.Common
{
    /// <summary>押したキーの組み合わせをそのまま表示する入力欄</summary>
    public class HotkeyBox : TextBox
    {
        HotkeySpec value;
        public HotkeySpec Value { get { return value; } }
        public event EventHandler Captured;
        public HotkeyBox() { ReadOnly = true; ShortcutsEnabled = false; BackColor = Theme.Surface; Cursor = Cursors.Hand; TextAlign = HorizontalAlignment.Center; }

        public void SetValue(HotkeySpec v) { value = v; Text = EmptyText(); }
        string EmptyText() { return value.IsEmpty ? "なし" : value.Display; }

        static bool WinDown() { return GlobalHotkey.WinKeyDown(); }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift)) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            var spec = HotkeySpec.FromKeyEvent(keyData, WinDown());
            if (spec.IsEmpty)
            {
                // 修飾キーだけ押している途中: 途中経過を表示
                Text = spec.Mods == HotMods.None ? "" : new HotkeySpec { Mods = spec.Mods, Key = Keys.A }.Display.Replace(" + A", " + …");
                return true;
            }
            SetValue(spec);
            if (Captured != null) Captured(this, EventArgs.Empty);
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (Value.IsEmpty || Text.EndsWith("…") || Text.Length == 0) Text = EmptyText();
        }
    }

    public class HotkeySettingsForm : Form
    {
        readonly IHotkeyTarget ctx;
        readonly bool showStartup;
        readonly HotkeyBox box = new HotkeyBox();
        readonly Label message = new Label();
        readonly Label status = new Label();
        readonly ToggleSwitch startup = new ToggleSwitch("Windows の起動時に自動で起動する (スタートアップ)");
        readonly Label startupNote = new Label();
        StartupState startupState;

        readonly string startupOnNote;

        /// <param name="hotkeyTitle">例: 「履歴を呼び出すショートカット」</param>
        /// <param name="startupOnNote">スタートアップがオンのときの説明 (例: 「Windows にサインインすると、通知領域に静かに常駐します。」)</param>
        public HotkeySettingsForm(IHotkeyTarget ctx, string hotkeyTitle, string startupOnNote)
        {
            this.ctx = ctx;
            this.startupOnNote = startupOnNote;
            showStartup = startupOnNote != null;
            Theme.ApplyForm(this);
            Text = AppInfo.TitleFor("設定");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = showStartup ? Theme.S(470, 430) : Theme.S(470, 330);
            TopMost = true;

            var card = new CardPanel(hotkeyTitle) { Dock = DockStyle.Fill };
            var outer = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            outer.Controls.Add(card);

            var hint = new Label
            {
                Text = "下の欄をクリックしてから、使いたいキーの組み合わせを押してください。\nCtrl・Alt・Win のどれかと、文字・数字・F キーを組み合わせます (例: Alt + V)。",
                Font = Theme.Small, ForeColor = Theme.SubText, Height = Theme.S(40), Dock = DockStyle.Top, UseMnemonic = false
            };
            box.Font = Theme.Title;
            box.SetValue(ctx.Current);
            var frame = new InputFrame(box) { Dock = DockStyle.Top };
            box.Enter += (s, e) => { ctx.Suspend(); message.ForeColor = Theme.SubText; message.Text = "キーを押してください… (Esc で取り消し)"; };
            box.Leave += (s, e) => { ctx.Resume(); if (message.Text.StartsWith("キーを押して")) message.Text = ""; };
            box.Captured += (s, e) =>
            {
                var why = HotkeySpec.Validate(box.Value);
                message.ForeColor = why == null ? Theme.Success : Theme.Danger;
                var caution = why == null ? HotkeySpec.Caution(box.Value) : null;
                message.Text = why ?? box.Value.Display + " に変更します。「保存」を押すと反映されます。" + (caution != null ? "\n" + caution : "");
            };

            // 「解除」(ショートカットなし) と「初期設定に戻す」
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(Theme.ButtonHeight + 8), Padding = Theme.P(0, 6, 0, 0), BackColor = Theme.Surface, WrapContents = false };
            clear.Text = "解除 (なし)";
            clear.FitWidth(96);
            clear.Margin = Theme.P(0, 0, 10, 0);
            clear.Click += (s, e) => ClearValue();
            var reset = new LinkLabel { Text = "初期設定に戻す (" + ctx.Default.DisplayOrNone + ")", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentPressed, Font = Theme.Small, LinkBehavior = LinkBehavior.HoverUnderline, Margin = Theme.P(0, 8, 0, 0) };
            reset.LinkClicked += (s, e) => { box.SetValue(ctx.Default); message.ForeColor = Theme.SubText; message.Text = (ctx.Default.IsEmpty ? "ショートカットなしに戻します。" : "") + "「保存」を押すと反映されます。"; };
            row.Controls.Add(clear); row.Controls.Add(reset);

            message.Dock = DockStyle.Top; message.Height = Theme.S(50); message.Font = Theme.Small; message.UseMnemonic = false;
            status.Dock = DockStyle.Top; status.Height = Theme.S(22); status.Font = Theme.Small; status.UseMnemonic = false;
            UpdateStatus();

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(Theme.ButtonHeight + 12), Dock = DockStyle.Bottom, Padding = Theme.P(0, 10, 0, 0), BackColor = Theme.Surface };
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => Save();
            cancel.Click += (s, e) => Close();
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            AcceptButton = ok;

            // スタートアップ (Teal Hub のカードのトグルと同じレジストリの値を読み書きする)
            var startupHead = new Label { Text = "スタートアップ", Font = Theme.Bold, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = Theme.S(24), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            startup.Dock = DockStyle.Top; startup.AutoSize = false; startup.Height = Theme.S(28);
            startupNote.Dock = DockStyle.Top; startupNote.Height = Theme.S(34); startupNote.Font = Theme.Small; startupNote.ForeColor = Theme.SubText; startupNote.UseMnemonic = false;
            if (showStartup) LoadStartup();
            startup.CheckedChanged += (s, e) => UpdateStartupNote();

            // Dock=Top は後から追加したものが上に来る → 下から順に追加
            if (showStartup)
            {
                card.Controls.Add(startupNote);
                card.Controls.Add(startup);
                card.Controls.Add(startupHead);
            }
            card.Controls.Add(message);
            card.Controls.Add(row);
            card.Controls.Add(Ui.Spacer(6));
            card.Controls.Add(frame);
            card.Controls.Add(Ui.Spacer(4));
            card.Controls.Add(status);
            card.Controls.Add(hint);
            card.Controls.Add(buttons);
            Controls.Add(outer);
            FormClosed += (s, e) => ctx.Resume();
            Shown += (s, e) => ok.Focus();
        }

        // ---- テスト用 ----
        public HotkeyBox HotkeyInput { get { return box; } }
        public ToggleSwitch StartupSwitch { get { return startup; } }
        public string MessageText { get { return message.Text; } }
        public string StatusText { get { return status.Text; } }
        public void SaveForTest() { Save(); }
        public void ClearForTest() { ClearValue(); }
        public bool HasStartupSection { get { return showStartup; } }
        readonly FlatButton clear = new FlatButton("解除 (なし)", ButtonKind.Secondary);

        void ClearValue()
        {
            box.SetValue(HotkeySpec.None);
            message.ForeColor = Theme.SubText;
            message.Text = "ショートカットを解除します (なし)。「保存」を押すと反映されます。";
        }

        void LoadStartup()
        {
            startupState = Startup.Get(AppInfo.Id, Startup.CurrentExe);
            startup.Checked = startupState.Registered;
            UpdateStartupNote();
        }

        void UpdateStartupNote()
        {
            startupNote.ForeColor = Theme.SubText;
            if (startup.Checked && startupState.Status == StartupStatus.PathMismatch)
            {
                startupNote.ForeColor = Theme.Danger;
                startupNote.Text = "登録先が違います: " + startupState.RegisteredPath + "\n「保存」でこの場所に登録し直します。";
            }
            else if (startupState.Status == StartupStatus.DisabledByUser)
                startupNote.Text = startup.Checked ? "タスク マネージャーで無効になっています。「保存」で有効に戻します。" : "タスク マネージャーで無効になっています。";
            else
                startupNote.Text = startup.Checked ? startupOnNote : "Windows の起動時には起動しません。";
        }

        /// <summary>スタートアップの変更を反映する。失敗したら false。</summary>
        bool SaveStartup()
        {
            if (!showStartup) return true;
            bool want = startup.Checked;
            bool needed = want ? startupState.Status != StartupStatus.On : startupState.Command != null;
            if (!needed) return true;
            var err = Startup.TrySet(AppInfo.Id, Startup.CurrentExe, want);
            if (err != null)
            {
                startupNote.ForeColor = Theme.Danger;
                startupNote.Text = "スタートアップを変更できませんでした: " + err;
                return false;
            }
            LoadStartup();
            return true;
        }

        void UpdateStatus()
        {
            if (ctx.Current.IsEmpty) { status.ForeColor = Theme.SubText; status.Text = "● 現在: ショートカットなし"; }
            else if (ctx.Error == null) { status.ForeColor = Theme.Success; status.Text = "● 現在のショートカット: " + ctx.Current.Display + " (有効)"; }
            else { status.ForeColor = Theme.Danger; status.Text = "● " + ctx.Current.Display + " は登録できていません (ほかのアプリが使用中など)"; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (box.Focused) { box.SetValue(ctx.Current); ActiveControl = null; message.Text = ""; return true; }
                Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Save()
        {
            if (!SaveStartup()) return;
            var spec = box.Value;
            if (spec.SameAs(ctx.Current) && ctx.Error == null) { Close(); return; }
            var err = ctx.Apply(spec);
            if (err != null)
            {
                message.ForeColor = Theme.Danger; message.Text = err;
                UpdateStatus();
                return;
            }
            UpdateStatus();
            Close();
        }
    }
}
