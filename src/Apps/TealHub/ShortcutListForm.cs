// Teal Hub (アプリ管理) - ショートカット一覧 (すべてのアプリのショートカットを 1 か所で確認・変更・解除)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>一覧の 1 行: 名前 / キー / 補足 / 変更・解除</summary>
    public class ShortcutRow : Panel
    {
        public readonly ShortcutSlot Slot;
        readonly Label name = new Label();
        public readonly KeyCap Cap = new KeyCap();
        readonly Label note = new Label();
        public readonly FlatButton Edit = new FlatButton("変更", ButtonKind.Secondary);
        public readonly FlatButton Clear = new FlatButton("解除", ButtonKind.Ghost);
        public ShortcutRow(ShortcutSlot slot)
        {
            Slot = slot;
            BackColor = Theme.Surface;
            Height = Theme.S(44);
            name.Text = slot.Label; name.Font = Theme.Body; name.ForeColor = Theme.Text; name.AutoSize = false; name.AutoEllipsis = true; name.UseMnemonic = false; name.TextAlign = ContentAlignment.MiddleLeft;
            note.Font = Theme.Small; note.ForeColor = Theme.SubText; note.AutoSize = false; note.AutoEllipsis = true; note.UseMnemonic = false; note.TextAlign = ContentAlignment.MiddleLeft;
            Edit.FitWidth(64); Clear.FitWidth(56);
            Controls.AddRange(new Control[] { name, Cap, note, Edit, Clear });
        }
        public string NoteText { get { return note.Text; } }

        public void Refresh_(HubShortcuts mgr, ToolTip tip)
        {
            Cap.Set(Slot.Current.DisplayOrNone, KeyCap.StateOf(mgr, Slot));
            Edit.Text = Slot.Current.IsEmpty ? "登録" : "変更";
            Clear.Enabled = !Slot.Current.IsEmpty;
            var dup = mgr.DuplicateOf(Slot);
            if (Slot.Current.IsEmpty) { note.Text = Slot.Default.IsEmpty ? "未登録 (必要なら「登録」)" : "解除中 (初期設定は " + Slot.Default.Display + ")"; note.ForeColor = Theme.SubText; }
            else if (dup != null) { note.Text = "⚠ " + dup.Describe() + " と重なっています"; note.ForeColor = Theme.Danger; }
            else if (Slot.Error != null) { note.Text = "⚠ " + Slot.Error; note.ForeColor = Theme.Warning; }
            else if (Slot.HubOwned) { note.Text = Slot.IsSelf ? "Teal Hub が通知領域にいる間使えます" : "Teal Hub が開きます (起動していなければ起動)"; note.ForeColor = Theme.SubText; }
            else { note.Text = Slot.AppName + " の起動中に使えます"; note.ForeColor = Theme.SubText; }
            if (tip != null) { tip.SetToolTip(note, note.Text); tip.SetToolTip(Cap, KeyCap.TipOf(mgr, Slot)); }
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Clear == null) return;
            int gap = Theme.S(8), right = Width;
            Clear.Location = new Point(right - Clear.Width, (Height - Clear.Height) / 2); right = Clear.Left - Theme.S(4);
            Edit.Location = new Point(right - Edit.Width, (Height - Edit.Height) / 2); right = Edit.Left - Theme.S(12);
            int nameW = Theme.S(150), capX = nameW + gap, capW = Theme.S(150);
            name.Bounds = new Rectangle(0, 0, nameW, Height);
            Cap.Location = new Point(capX, (Height - Cap.Height) / 2);
            int noteX = capX + Math.Max(capW, Cap.Width) + gap;
            note.Bounds = new Rectangle(noteX, 0, Math.Max(10, right - noteX), Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
        }
    }

    public class ShortcutListForm : Form
    {
        readonly MainForm main;
        readonly HubShortcuts mgr;
        readonly FlowLayoutPanel list = new FlowLayoutPanel();
        readonly FlowLayoutPanel top = new FlowLayoutPanel();
        readonly ToolTip tip = new ToolTip();
        readonly FooterBar footer = new FooterBar();
        public readonly List<ShortcutRow> Rows = new List<ShortcutRow>();
        readonly List<CardPanel> groups = new List<CardPanel>();
        NoticeBar startupWarning;
        string warnKey;

        public ShortcutListForm(MainForm main, HubShortcuts mgr)
        {
            this.main = main; this.mgr = mgr;
            Theme.ApplyForm(this);
            Text = AppInfo.TitleFor("ショートカット一覧");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = Theme.S(600, 420);
            ShowInTaskbar = false;

            var header = new HeaderBar("ショートカット一覧", "すべてのアプリの全体ショートカット。どれも「解除」でなしにできます", false);
            var about = new Label
            {
                Text = "・Teal Hub が開くもの … Teal Hub が登録します。押すと、起動していなければ起動し、起動中なら前面に出します。\n"
                     + "・アプリ自身のもの (Teal Clip・Teal Volume) … そのアプリが起動している間に使えます。ここで変えると、すぐにそのアプリに反映されます。\n"
                     + "・同じ組み合わせは 2 つに割り当てられません。",
                Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = false, UseMnemonic = false, BackColor = Theme.Background,
                Margin = new Padding(0, 0, 0, Theme.S(8)), Height = Theme.S(56)
            };
            top.Dock = DockStyle.Top; top.FlowDirection = FlowDirection.TopDown; top.WrapContents = false; top.AutoSize = true; top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.Padding = Theme.P(Theme.Pad, Theme.Pad, Theme.Pad, 0); top.BackColor = Theme.Background;
            top.Controls.Add(about);

            list.Dock = DockStyle.Fill; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AutoScroll = true;
            list.Padding = Theme.P(Theme.Pad, Theme.S(4), Theme.Pad, Theme.Pad); list.BackColor = Theme.Background;
            list.Resize += (s, e) => Fit();
            Resize += (s, e) => Fit();

            Controls.Add(list); Controls.Add(top); Controls.Add(footer); Controls.Add(header);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            WindowMemory.Attach(this, 760, 600, "shortcuts");
            Activated += (s, e) => { mgr.RefreshFromDisk(); RefreshValues(); };
            Rebuild();
        }

        /// <summary>アプリの一覧が変わったとき (apps.json の読み直し)</summary>
        public void Rebuild()
        {
            list.SuspendLayout();
            foreach (var g in groups) g.Dispose();
            groups.Clear(); Rows.Clear(); list.Controls.Clear();
            foreach (var grp in mgr.Slots.GroupBy(x => x.AppId, StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var card = new CardPanel(first.AppName + (first.App != null && !string.IsNullOrEmpty(first.App.Role) ? "  (" + first.App.Role + ")" : first.IsSelf ? "  (アプリ管理)" : ""))
                { Margin = new Padding(0, 0, 0, Theme.S(10)), Padding = Theme.P(16, 40, 16, 6) };
                var rows = grp.Select(sl => new ShortcutRow(sl)).ToList();
                // Dock=Top は後から追加したものが上 → 逆順に追加
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    var r = rows[i];
                    r.Dock = DockStyle.Top;
                    r.Edit.Click += (s, e) => main.EditShortcut(r.Slot);
                    r.Clear.Click += (s, e) => main.ClearShortcut(r.Slot, true);
                    card.Controls.Add(r);
                }
                Rows.AddRange(rows);
                card.Height = Theme.S(40 + 6) + rows.Sum(r => r.Height) + 2;
                groups.Add(card); list.Controls.Add(card);
            }
            list.ResumeLayout();
            RefreshValues();
            Fit();
        }

        public void RefreshValues()
        {
            foreach (var r in Rows) r.Refresh_(mgr, tip);
            int dups = mgr.DuplicateCount;
            var used = mgr.Slots.Count(x => !x.Current.IsEmpty);
            footer.StatusText = used + " 個のショートカットを使用中" + (dups > 0 ? " ・ ⚠ 重なっている組み合わせが " + dups + " 個あります" : "");
            UpdateStartupWarning();
        }

        /// <summary>Teal Hub がスタートアップにないと、サインイン直後は Teal Hub のショートカットが使えない</summary>
        void UpdateStartupWarning()
        {
            bool hubKeys = mgr.Slots.Any(x => x.HubOwned && !x.Current.IsEmpty);
            bool startupOn = main.SelfStartup.Checked;
            string key = hubKeys && !startupOn ? "warn" : "";
            if (key == warnKey) return;
            warnKey = key;
            if (startupWarning != null) { startupWarning.Dispose(); startupWarning = null; }
            if (key.Length > 0)
            {
                startupWarning = new NoticeBar("Teal Hub がスタートアップに入っていないため、Windows にサインインした直後は Teal Hub のショートカットが使えません (Teal Hub を開くまで)。", "スタートアップをオンにする", false);
                startupWarning.Margin = new Padding(0, 0, 0, Theme.S(8));
                startupWarning.Action.Click += (s, e) => { main.SetSelfStartup(true); warnKey = null; UpdateStartupWarning(); };
                top.Controls.Add(startupWarning);
            }
            Fit();
        }
        public bool ShowsStartupWarning { get { return startupWarning != null; } }

        void Fit()
        {
            int w = list.ClientSize.Width - list.Padding.Horizontal;
            int total = list.Padding.Vertical + groups.Sum(c => c.Height + c.Margin.Vertical);
            if (total > list.ClientSize.Height && !list.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
            foreach (var g in groups) g.Width = Math.Max(Theme.S(400), w);
            foreach (Control c in top.Controls) c.Width = Math.Max(Theme.S(400), ClientSize.Width - top.Padding.Horizontal);
        }
    }
}
