// Teal Hub (アプリ管理) - アプリごとの設定ページ (1.6.0)
//  一覧の歯車ボタンで、同じウィンドウの中がこのページに切り替わる (「← 戻る」で一覧へ)
//   - ショートカット … キーの組み合わせを押して「登録」/「解除」。Teal Hub が登録する
//   - 常駐           … オン: ×で閉じても通知領域に残る / オフ: ×で終了 (ショートカットを押すと Teal Hub が起動し直す)
//   - スタートアップ … 常駐がオンのときだけ選べる (Windows のサインイン時に通知領域で起動)
//  Teal Hub 自身のページは「常に常駐」で、ショートカットとスタートアップだけ変えられる
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>ティール色の歯車 (記号の文字はフォントによって出ないので描く)</summary>
    public static class GearIcon
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(int px, Color color)
        {
            var key = px + "|" + color.ToArgb();
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = Draw(px, color);
            cache[key] = b;
            return b;
        }

        public static Bitmap Draw(int px, Color color)
        {
            var b = new Bitmap(px, px);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float c = px / 2f;
                float ringR = px * 0.30f, ringW = px * 0.17f;
                using (var br = new SolidBrush(color))
                {
                    // 歯 (8 本)
                    float tw = px * 0.17f, th = px * 0.2f;
                    for (int i = 0; i < 8; i++)
                    {
                        var st = g.Save();
                        g.TranslateTransform(c, c);
                        g.RotateTransform(i * 45f);
                        g.FillRectangle(br, -tw / 2, -c + px * 0.04f, tw, th + px * 0.06f);
                        g.Restore(st);
                    }
                }
                using (var pen = new Pen(color, ringW))
                    g.DrawEllipse(pen, c - ringR, c - ringR, ringR * 2, ringR * 2);
            }
            return b;
        }
    }

    /// <summary>行を縦に積むカード。文の行は幅に合わせて折り返し、カードの高さも中身に合わせる</summary>
    public class StackCard : CardPanel
    {
        readonly List<Control> rows = new List<Control>();
        readonly Dictionary<Control, int> gaps = new Dictionary<Control, int>();
        readonly HashSet<Control> natural = new HashSet<Control>();
        bool layingOut;

        public StackCard(string title) : base(title) { }

        /// <param name="naturalWidth">幅を広げない (トグル・ボタン)</param>
        public T Add<T>(T c, int gapBefore = 0, bool naturalWidth = false) where T : Control
        {
            rows.Add(c);
            gaps[c] = Theme.S(gapBefore);
            if (naturalWidth) natural.Add(c);
            Controls.Add(c);
            c.VisibleChanged += (s, e) => PerformLayout();
            c.TextChanged += (s, e) => { if (c is Label) PerformLayout(); };
            return c;
        }

        public static Label Text_(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, Font = font ?? Theme.Small, ForeColor = color ?? Theme.SubText, AutoSize = false, UseMnemonic = false, BackColor = Theme.Surface };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (layingOut) return;
            layingOut = true;
            try
            {
                int x = Padding.Left, w = Math.Max(Theme.S(120), Width - Padding.Horizontal), y = Padding.Top;
                foreach (var r in rows)
                {
                    if (!r.Visible && IsHandleCreated && Visible) continue;
                    if (!r.Visible && !Explicit(r)) continue;
                    y += gaps[r];
                    int h;
                    var lbl = r as Label;
                    if (lbl != null && !lbl.AutoSize)
                        h = string.IsNullOrEmpty(lbl.Text) ? 0 : TextRenderer.MeasureText(lbl.Text, lbl.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + Theme.S(2);
                    else if (r is ToggleSwitch) h = r.GetPreferredSize(Size.Empty).Height;
                    else h = r.Height;
                    int rw = natural.Contains(r) ? Math.Min(w, r is ToggleSwitch ? r.GetPreferredSize(Size.Empty).Width : r.Width) : w;
                    r.SetBounds(x, y, rw, h);
                    y += h;
                }
                int want = y + Padding.Bottom;
                if (Height != want) Height = want;
            }
            finally { layingOut = false; }
        }

        // 画面に出す前 (テスト・スクリーンショット) は Visible が親に引きずられて false になるので、自分で隠したものだけ除く
        readonly HashSet<Control> hidden = new HashSet<Control>();
        bool Explicit(Control c) { return !hidden.Contains(c); }
        public void SetShown(Control c, bool shown)
        {
            if (shown) hidden.Remove(c); else hidden.Add(c);
            c.Visible = shown;
            PerformLayout();
        }
    }

    /// <summary>設定ページ (アプリ 1 つぶん。app が null なら Teal Hub 自身)</summary>
    public class AppSettingsPage : Panel
    {
        readonly MainForm owner;
        readonly HubShortcuts shortcuts;
        readonly Panel top = new Panel();
        public readonly FlatButton BtnBack = new FlatButton("← 戻る", ButtonKind.Ghost);
        readonly PictureBox icon = new PictureBox();
        readonly Label title = new Label(), sub = new Label();
        readonly FlowLayoutPanel body = new FlowLayoutPanel();
        readonly ToolTip tip = new ToolTip();

        AppEntry app;
        ShortcutSlot slot;
        StackCard keyCard, residentCard, startupCard;
        public HotkeyBox Box { get; private set; }
        public FlatButton BtnRegister { get; private set; }
        public FlatButton BtnClear { get; private set; }
        public LinkLabel ResetLink { get; private set; }
        public ToggleSwitch ResidentSwitch { get; private set; }
        public ToggleSwitch StartupSwitch { get; private set; }
        public FlatButton StartupFix { get; private set; }
        Label keyStatus, keyMessage, residentNote, clipNote, startupNote;
        bool updating;
        string startupKey;

        /// <summary>表示中のアプリ (Teal Hub 自身なら "TealHub")</summary>
        public string AppId { get { return app == null ? Program.Id : app.Id; } }
        public AppEntry App { get { return app; } }
        public bool IsSelfPage { get { return app == null; } }
        public string MessageText { get { return keyMessage == null ? "" : keyMessage.Text; } }
        public string StatusText { get { return keyStatus == null ? "" : keyStatus.Text; } }
        public string StartupNoteText { get { return startupNote == null ? "" : startupNote.Text; } }
        public string ResidentNoteText { get { return residentNote == null ? "" : residentNote.Text + (clipNote != null && clipNote.Text.Length > 0 ? "\n" + clipNote.Text : ""); } }
        public string TitleText { get { return title.Text; } }
        public string SubText { get { return sub.Text; } }
        public event Action BackRequested;
        public bool Box_Focused() { return Box != null && !Box.IsDisposed && Box.Focused; }

        public AppSettingsPage(MainForm owner, HubShortcuts shortcuts)
        {
            this.owner = owner;
            this.shortcuts = shortcuts;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Visible = false;

            top.Dock = DockStyle.Top; top.BackColor = Theme.Surface; top.Height = Theme.S(64);
            top.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1); };
            BtnBack.FitWidth(80);
            BtnBack.Click += (s, e) => { if (BackRequested != null) BackRequested(); };
            tip.SetToolTip(BtnBack, "アプリの一覧に戻ります (Esc)");
            icon.SizeMode = PictureBoxSizeMode.Zoom; icon.Size = new Size(Theme.S(36), Theme.S(36)); icon.BackColor = Theme.Surface;
            title.Font = Theme.Header; title.ForeColor = Theme.Text; title.AutoSize = true; title.UseMnemonic = false; title.BackColor = Theme.Surface;
            sub.Font = Theme.Small; sub.ForeColor = Theme.SubText; sub.AutoSize = false; sub.AutoEllipsis = true; sub.UseMnemonic = false; sub.BackColor = Theme.Surface;
            top.Controls.AddRange(new Control[] { BtnBack, icon, title, sub });
            top.Layout += (s, e) => LayoutTop();

            body.Dock = DockStyle.Fill; body.FlowDirection = FlowDirection.TopDown; body.WrapContents = false; body.AutoScroll = true;
            body.Padding = Theme.P(Theme.Pad + 4, Theme.Pad, Theme.Pad + 4, Theme.Pad); body.BackColor = Theme.Background;
            body.Resize += (s, e) => FitCards();
            Controls.Add(body);
            Controls.Add(top);
        }

        void LayoutTop()
        {
            int pad = Theme.S(10);
            BtnBack.Location = new Point(pad, (top.Height - BtnBack.Height) / 2);
            icon.Location = new Point(BtnBack.Right + Theme.S(14), (top.Height - icon.Height) / 2);
            int x = icon.Right + Theme.S(12);
            int th = title.PreferredHeight, sh = sub.Font.Height + 2;
            int y = (top.Height - th - sh) / 2;
            title.Location = new Point(x, y);
            sub.Bounds = new Rectangle(x, title.Bottom, Math.Max(10, top.Width - x - pad), sh);
        }

        void FitCards()
        {
            int w = body.ClientSize.Width - body.Padding.Horizontal;
            int total = body.Padding.Vertical + body.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
            if (total > body.ClientSize.Height && !body.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
            w = Math.Max(Theme.S(320), Math.Min(Theme.S(760), w));
            foreach (Control c in body.Controls) c.Width = w;
        }

        /// <summary>appEntry のページを作る (null なら Teal Hub 自身)</summary>
        public void ShowFor(AppEntry appEntry)
        {
            if (Box_Focused()) { BtnBack.Focus(); shortcuts.ResumeAll(); }
            app = appEntry;
            slot = app == null ? shortcuts.Self : shortcuts.For(app.Id);
            startupKey = null;
            body.SuspendLayout();
            foreach (Control c in body.Controls.Cast<Control>().ToList()) c.Dispose();
            body.Controls.Clear();

            // ---- 見出し ----
            if (app == null)
            {
                title.Text = "Teal Hub の設定";
                sub.Text = "v" + AppInfo.Version + " ・ アプリ管理 ・ 全アプリのショートカットを受け付けます";
                icon.Image = Theme.AppIconBitmap(Theme.S(36));
            }
            else
            {
                title.Text = app.Name + " の設定";
                sub.Text = (string.IsNullOrEmpty(app.Version) ? "" : "v" + app.Version + " ・ ") + (app.Role ?? "") + (string.IsNullOrEmpty(app.Description) ? "" : " ・ " + app.Description.Split('\n')[0]);
                icon.Image = AppCard.LoadIcon(app, Theme.S(36));
            }
            tip.SetToolTip(sub, app == null ? AppInfo.Description : (app.Description ?? ""));
            LayoutTop();

            BuildKeyCard();
            BuildResidentCard();
            BuildStartupCard();
            foreach (var c in new Control[] { keyCard, residentCard, startupCard }) { c.Margin = new Padding(0, 0, 0, Theme.S(12)); body.Controls.Add(c); }
            FitCards();
            body.ResumeLayout();
            RefreshState();
        }

        // ---------- ショートカット ----------

        void BuildKeyCard()
        {
            keyCard = new StackCard("ショートカット");
            keyCard.Add(StackCard.Text_(KeyActionText(), Theme.Body, Theme.Text));
            keyStatus = keyCard.Add(StackCard.Text_(""), 6);

            Box = new HotkeyBox { Font = Theme.Title };
            var frame = new InputFrame(Box);
            var row = new Panel { BackColor = Theme.Surface, Height = Math.Max(frame.Height, Theme.S(Theme.ButtonHeight)) };
            BtnRegister = new FlatButton("登録", ButtonKind.Primary).FitWidth(72);
            BtnClear = new FlatButton("解除", ButtonKind.Secondary).FitWidth(72);
            row.Controls.AddRange(new Control[] { frame, BtnRegister, BtnClear });
            row.Layout += (s, e) =>
            {
                int gap = Theme.S(8);
                int fw = Math.Max(Theme.S(160), Math.Min(Theme.S(260), row.Width - BtnRegister.Width - BtnClear.Width - gap * 2));
                frame.SetBounds(0, (row.Height - frame.Height) / 2, fw, frame.Height);
                BtnRegister.Location = new Point(frame.Right + gap, (row.Height - BtnRegister.Height) / 2);
                BtnClear.Location = new Point(BtnRegister.Right + gap, (row.Height - BtnClear.Height) / 2);
            };
            keyCard.Add(row, 8);

            var hint = keyCard.Add(StackCard.Text_("上の欄をクリックしてから、使いたいキーの組み合わせを押して「登録」。\nCtrl・Alt・Win のどれかと、文字・数字・F キーを組み合わせます (例: Alt + V)。"), 8);
            ResetLink = new LinkLabel { AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentPressed, Font = Theme.Small, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Theme.Surface, UseMnemonic = false };
            ResetLink.Text = "初期設定に戻す (" + slot.Default.DisplayOrNone + ")";
            keyCard.Add(ResetLink, 4, true);
            keyMessage = keyCard.Add(StackCard.Text_(""), 6);
            var note = keyCard.Add(StackCard.Text_(app == null
                ? "ショートカットは Teal Hub が起動している間だけ使えます (Teal Hub は通知領域に常駐します)。"
                : "ショートカットは Teal Hub が受け付けます。" + app.Name + " が起動していなければ、Teal Hub が起動します。"), 4);
            hint.ForeColor = note.ForeColor = Theme.SubText;

            Box.SetValue(slot.Current);
            Box.Enter += (s, e) => { shortcuts.SuspendAll(); SetMessage("キーを押してください… (Esc で取り消し)", Theme.SubText); };
            Box.Leave += (s, e) => { shortcuts.ResumeAll(); if (keyMessage.Text.StartsWith("キーを押して")) SetMessage("", Theme.SubText); RefreshState(); };
            Box.Captured += (s, e) =>
            {
                var v = Box.Value;
                var why = HotkeySpec.Validate(v);
                if (why == null && !v.SameAs(slot.Current)) why = DupText(v);
                if (why != null) { SetMessage(why, Theme.Danger); UpdateButtons(); return; }
                var caution = HotkeySpec.Caution(v);
                SetMessage(v.SameAs(slot.Current) && slot.Error == null && slot.Active
                    ? v.Display + " は登録済みです。"
                    : v.Display + " にします。「登録」を押すと使えるようになります。" + (caution != null ? "\n" + caution : ""), v.SameAs(slot.Current) ? Theme.SubText : Theme.Accent);
                UpdateButtons();
            };
            BtnRegister.Click += (s, e) => Register();
            BtnClear.Click += (s, e) => Clear(true);
            ResetLink.LinkClicked += (s, e) => ResetToDefault();
        }

        string DupText(HotkeySpec v)
        {
            var dup = shortcuts.DuplicateOf(slot, v);
            return dup == null ? null : v.Display + " は " + dup.Describe() + " で使っています。先にそちらを解除するか、別の組み合わせを選んでください。";
        }

        string KeyActionText()
        {
            if (app == null) return "押すと Teal Hub の画面を開きます (前面にあるときは隠します)。";
            switch (app.Id)
            {
                case "TealClip": return "押すと、コピーの履歴をその場に呼び出します (選ぶと貼り付け)。";
                case "TealVolume": return "押すと、音量ミキサーを開きます (前面にあるときは隠します)。";
                default: return "押すと、" + app.Name + " の画面を前面に出します (前面にあるときは隠します)。";
            }
        }

        void SetMessage(string text, Color color) { keyMessage.ForeColor = color; keyMessage.Text = text; }

        void UpdateButtons()
        {
            var v = Box.Value;
            BtnRegister.Enabled = !v.IsEmpty && (!v.SameAs(slot.Current) || slot.Error != null || (!slot.Active && !shortcuts.Suspended));
            BtnClear.Enabled = !slot.Current.IsEmpty;
            ResetLink.Visible = !slot.Default.SameAs(slot.Current);
        }

        /// <summary>入力欄の組み合わせを登録する。成功したら true</summary>
        public bool Register()
        {
            var v = Box.Value;
            if (v.IsEmpty) { SetMessage("先に、上の欄でキーの組み合わせを押してください。", Theme.Danger); return false; }
            var err = slot.Apply(v);
            if (err != null) { SetMessage(err, Theme.Danger); RefreshState(); return false; }
            SetMessage(v.Display + " を登録しました。" + (app == null ? "" : "押すと " + app.Name + " が開きます。"), Theme.Success);
            owner.SetStatus(slot.AppName + " のショートカットを " + v.Display + " にしました");
            RefreshState();
            return true;
        }

        /// <summary>解除する (なしにする)。confirm なら Teal Hub 自身のときだけ確認する</summary>
        public bool Clear(bool confirm)
        {
            if (slot.Current.IsEmpty) return true;
            if (confirm && slot.IsSelf && !Dialogs.Confirm(owner, slot.Current.Display + " で Teal Hub を開けなくなります。解除しますか？\n(通知領域のアイコンからは、いつでも開けます)", "ショートカットを解除")) return false;
            var old = slot.Current;
            var err = slot.Apply(HotkeySpec.None);
            if (err != null) { SetMessage(err, Theme.Danger); return false; }
            Box.SetValue(HotkeySpec.None);
            SetMessage(old.Display + " を解除しました (ショートカットなし)。", Theme.SubText);
            owner.SetStatus(slot.AppName + " のショートカットを解除しました");
            RefreshState();
            return true;
        }

        public bool ResetToDefault()
        {
            if (slot.Default.IsEmpty) return Clear(false);
            Box.SetValue(slot.Default);
            return Register();
        }

        /// <summary>入力中のキーを取り消して、今の値に戻す (Esc)</summary>
        public void CancelCapture()
        {
            Box.SetValue(slot.Current);
            SetMessage("", Theme.SubText);
            UpdateButtons();
            BtnBack.Focus();
        }

        // ---------- 常駐 ----------

        void BuildResidentCard()
        {
            residentCard = new StackCard("常駐");
            ResidentSwitch = residentCard.Add(new ToggleSwitch("常駐する (×で閉じても通知領域に残す)") { AutoSize = false, BackColor = Theme.Surface }, 0, true);
            residentNote = residentCard.Add(StackCard.Text_(""), 4);
            clipNote = residentCard.Add(StackCard.Text_(""), 6);
            if (app == null)
            {
                ResidentSwitch.Checked = true;
                ResidentSwitch.Enabled = false;
                ResidentSwitch.Text = "常に常駐します";
            }
            ResidentSwitch.CheckedChanged += (s, e) => { if (!updating && app != null) owner.SetResident(app, ResidentSwitch.Checked); };
        }

        // ---------- スタートアップ ----------

        void BuildStartupCard()
        {
            startupCard = new StackCard("スタートアップ");
            StartupSwitch = startupCard.Add(new ToggleSwitch("Windows の起動時に自動で起動する") { AutoSize = false, BackColor = Theme.Surface }, 0, true);
            startupNote = startupCard.Add(StackCard.Text_(""), 4);
            StartupFix = startupCard.Add(new FlatButton("この場所で登録し直す", ButtonKind.Secondary).FitWidth(120), 8, true);
            startupCard.SetShown(StartupFix, false);
            StartupSwitch.CheckedChanged += (s, e) =>
            {
                if (updating) return;
                if (app == null) owner.SetSelfStartup(StartupSwitch.Checked);
                else owner.SetAppStartup(app, StartupSwitch.Checked);
            };
            StartupFix.Click += (s, e) => { if (app == null) owner.SetSelfStartup(true); else owner.SetAppStartup(app, true); };
        }

        /// <summary>今の状態 (ショートカット・常駐・スタートアップ) を表示に反映する</summary>
        public void RefreshState()
        {
            if (slot == null || keyCard == null || IsDisposed) return;
            // スロットは Load (再読み込み) で作り直されるので、取り直す
            var fresh = app == null ? shortcuts.Self : shortcuts.For(app.Id);
            if (fresh != null) slot = fresh;
            updating = true;
            try
            {
                // ショートカット
                if (slot.Current.IsEmpty) { keyStatus.ForeColor = Theme.SubText; keyStatus.Text = "● 今のショートカット: なし"; }
                else if (slot.Error != null) { keyStatus.ForeColor = Theme.Danger; keyStatus.Text = "● " + slot.Current.Display + " は使えません: " + slot.Error; }
                else if (slot.Active || shortcuts.Suspended) { keyStatus.ForeColor = Theme.Success; keyStatus.Text = "● 今のショートカット: " + slot.Current.Display + " (有効)"; }
                else { keyStatus.ForeColor = Theme.Danger; keyStatus.Text = "● " + slot.Current.Display + " は登録できていません (ほかのアプリが使用中など)"; }
                if (!Box.Focused && (Box.Value.SameAs(slot.Current) || keyMessage.Text.Length == 0)) Box.SetValue(slot.Current);
                ResetLink.Text = "初期設定に戻す (" + slot.Default.DisplayOrNone + ")";
                UpdateButtons();

                // 常駐
                bool resident = HubShortcuts.IsResident(app);
                ResidentSwitch.Checked = resident;
                if (app == null)
                    residentNote.Text = "Teal Hub は常に通知領域に常駐します (全アプリのショートカットを受け付けるため)。\n×で閉じても隠れるだけです。終了は通知領域のアイコンを右クリック →「終了」。";
                else
                    residentNote.Text = resident
                        ? "×で閉じても終了せず、通知領域 (画面右下) に残ります。終了は通知領域のアイコンを右クリック →「終了」。"
                        : "×で閉じると終了します。ショートカットを押すと、Teal Hub がもう一度起動します。";
                if (app != null && app.Id == "TealClip")
                {
                    clipNote.Text = resident
                        ? "Teal Clip は起動している間だけコピーを記録します。常駐していれば、画面を閉じても記録を続けます。"
                        : "注意: Teal Clip は起動している間だけコピーを記録します。常駐をオフにすると、閉じている間のコピーは履歴に残りません。";
                    clipNote.ForeColor = resident ? Theme.SubText : Theme.Warning;
                }
                else clipNote.Text = "";

                // スタートアップ
                var st = app == null ? Startup.Get(AppInfo.Id, Startup.CurrentExe) : Startup.Get(app.Id, app.ExePath);
                bool missing = app != null && !File.Exists(app.ExePath);
                string key = st.Status + "|" + st.Command + "|" + resident + "|" + missing + "|" + owner.AnyAppStartupOn;
                if (key != startupKey)
                {
                    startupKey = key;
                    StartupSwitch.Checked = st.Registered;
                    bool fix = false;
                    if (!resident)
                    {
                        StartupSwitch.Enabled = st.Registered;   // 常駐オフで登録が残っていれば、オフにはできる
                        startupNote.ForeColor = Theme.SubText;
                        startupNote.Text = "スタートアップは「常駐」がオンのときだけ使えます。上の「常駐する」をオンにすると選べるようになります。";
                    }
                    else if (missing && !st.Registered)
                    {
                        StartupSwitch.Enabled = false;
                        startupNote.ForeColor = Theme.Danger;
                        startupNote.Text = app.Name + " の exe が見つからないため、登録できません。\n" + app.ExePath;
                    }
                    else
                    {
                        StartupSwitch.Enabled = true;
                        switch (st.Status)
                        {
                            case StartupStatus.PathMismatch:
                                startupNote.ForeColor = Theme.Danger;
                                startupNote.Text = "登録先が違います: " + st.RegisteredPath;
                                StartupFix.Text = "この場所で登録し直す"; fix = !missing;
                                break;
                            case StartupStatus.DisabledByUser:
                                startupNote.ForeColor = Theme.Warning;
                                startupNote.Text = "タスク マネージャーで無効になっています。";
                                StartupFix.Text = "有効に戻す"; fix = !missing;
                                break;
                            default:
                                startupNote.ForeColor = Theme.SubText;
                                if (app == null)
                                    startupNote.Text = st.Registered
                                        ? "Windows にサインインすると Teal Hub が通知領域で起動し、ショートカットがすぐ使えます。"
                                        : "オフのままだと、Windows を起動したあと Teal Hub を開くまで、ショートカットは使えません。";
                                else
                                    startupNote.Text = st.Registered
                                        ? "Windows にサインインすると、画面を出さずに通知領域で起動します。"
                                        : "Windows の起動時には起動しません。";
                                break;
                        }
                        if (app == null && !st.Registered && owner.AnyAppStartupOn) startupNote.ForeColor = Theme.Warning;
                    }
                    StartupFix.FitWidth(120);
                    startupCard.SetShown(StartupFix, fix);
                    tip.SetToolTip(StartupSwitch, st.Command == null ? "" : "登録内容: " + st.Command);
                }
            }
            finally { updating = false; }
            keyCard.PerformLayout(); residentCard.PerformLayout(); startupCard.PerformLayout();
        }
    }
}
