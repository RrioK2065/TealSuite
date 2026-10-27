// Teal Clip (コピー履歴) - 履歴の管理画面 (コピー・ピン留め・削除・すべて削除)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    public class HistoryForm : Form
    {
        readonly TrayContext ctx;
        readonly HeaderBar header;
        readonly ListBox list = new ListBox();
        readonly TextBox preview = new TextBox();
        readonly FooterBar footer = new FooterBar();
        readonly FlatButton btnCopy, btnPin, btnDel, btnClear;
        bool suppress;

        public HistoryForm(TrayContext ctx)
        {
            this.ctx = ctx;
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(520, 420);
            KeyPreview = true;

            header = HeaderBar.ForApp("");
            var btnSettings = header.AddAction(new FlatButton("設定", ButtonKind.Secondary));
            btnClear = header.AddAction(new FlatButton("すべて削除", ButtonKind.Danger));
            btnClear.FitWidth(96);
            btnSettings.Click += (s, e) => ctx.ShowSettings();
            btnClear.Click += (s, e) => ClearAll();
            UpdateSubtitle();

            // 一覧
            var card = new CardPanel("最近コピーした文字 (最大 " + History.Max + " 件)") { Dock = DockStyle.Fill };
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Min(255, Theme.Body.Height + Theme.Small.Height + Theme.S(14));
            list.IntegralHeight = false;
            list.DrawItem += DrawRow;
            list.SelectedIndexChanged += (s, e) => { if (!suppress) UpdateSelection(); };
            list.DoubleClick += (s, e) => CopySelected();
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { CopySelected(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.P) { TogglePin(); e.Handled = e.SuppressKeyPress = true; }
            };

            // 下: 全文プレビュー + 操作
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(150), BackColor = Theme.Surface, Padding = Theme.P(0, 8, 0, 0) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Surface, Padding = Theme.P(0, 8, 0, 0), WrapContents = false };
            btnDel = new FlatButton("削除", ButtonKind.Secondary);
            btnPin = new FlatButton("ピン留め", ButtonKind.Secondary); btnPin.FitWidth(110);
            btnCopy = new FlatButton("コピー", ButtonKind.Primary); btnCopy.FitWidth(96);
            bar.Controls.Add(btnCopy); bar.Controls.Add(btnPin); bar.Controls.Add(btnDel);
            btnCopy.Click += (s, e) => CopySelected();
            btnPin.Click += (s, e) => TogglePin();
            btnDel.Click += (s, e) => DeleteSelected();
            preview.Multiline = true; preview.ReadOnly = true; preview.BorderStyle = BorderStyle.None; preview.BackColor = Theme.Surface;
            preview.ScrollBars = ScrollBars.Vertical; preview.Font = Theme.Body; preview.WordWrap = true;
            var pf = new InputFrame(preview, true) { Dock = DockStyle.Fill };
            bottom.Controls.Add(pf);
            bottom.Controls.Add(bar);

            card.Controls.Add(list);
            card.Controls.Add(bottom);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12) };
            body.Controls.Add(card);
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            WindowMemory.Attach(this, 680, 560, "historyWindow");
            FormClosing += (s, e) =>
            {
                // × で閉じたら隠す (常駐していれば通知領域に残る。常駐しなければ少しして終了する)
                if (e.CloseReason == CloseReason.UserClosing && !AppHost.IsExiting && !ctx.IsExited) { e.Cancel = true; Hide(); }
            };
            RefreshList();
        }

        /// <summary>見出しの下の 1 行 (テスト用にも使う)</summary>
        public string SubtitleText { get; private set; }

        public void UpdateSubtitle()
        {
            // 「閉じても常駐」の説明は起動時の通知に任せ、ヘッダーは呼び出しキーだけにする (幅が足りないため)
            SubtitleText = (ctx.Hotkey.IsEmpty ? "ショートカットなし" : "呼び出し: " + ctx.Hotkey.Display) + (ctx.Resident ? "" : " ・ 常駐オフ");
            header.SetDetail(SubtitleText);
            header.Invalidate();
        }

        public void RefreshList()
        {
            var keep = list.SelectedItem as ClipItem;
            suppress = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var i in ctx.History.Items) list.Items.Add(i);
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = keep != null && list.Items.Contains(keep) ? list.Items.IndexOf(keep) : 0;
            suppress = false;
            UpdateSelection();
        }

        ClipItem Selected { get { return list.SelectedItem as ClipItem; } }

        void UpdateSelection()
        {
            var it = Selected;
            preview.Text = it == null ? "" : it.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            btnCopy.Enabled = btnDel.Enabled = btnPin.Enabled = it != null;
            btnPin.Text = it != null && it.Pinned ? "ピンを外す" : "ピン留め";
            btnClear.Enabled = ctx.History.Items.Count > 0;
            footer.StatusText = ctx.History.Items.Count + " / " + History.Max + " 件 ・ ピン留め " + ctx.History.PinnedCount + " 件 ・ ピン留めした項目は古くなっても消えません";
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var it = (ClipItem)list.Items[e.Index];
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
            if (sel)
                using (var path = Theme.RoundRect(new RectangleF(r.X + 1, r.Y + 2, r.Width - 3, r.Height - 4), Theme.S(Theme.Radius)))
                using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, path);
            int bs = Theme.S(24);
            var br = new Rectangle(r.X + Theme.S(8), r.Y + (r.Height - bs) / 2, bs, bs);
            using (var path = Theme.RoundRect(br, Theme.S(5)))
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.SurfaceAlt)) g.FillPath(b, path);
            TextRenderer.DrawText(g, e.Index == 9 ? "0" : (e.Index + 1).ToString(), Theme.Bold, br, sel ? Color.White : Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var tf = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int x = br.Right + Theme.S(10), right = r.Right - Theme.S(10);
            int y = r.Y + (r.Height - Theme.Body.Height - Theme.Small.Height) / 2;
            var meta = When(it.Time) + "  ・  " + it.Text.Length + " 文字" + (it.Pinned ? "  ・  ★ ピン留め" : "");
            TextRenderer.DrawText(g, it.OneLine, Theme.Body, new Rectangle(x, y, right - x, Theme.Body.Height), Theme.Text, tf);
            TextRenderer.DrawText(g, meta, Theme.Small, new Rectangle(x, y + Theme.Body.Height, right - x, Theme.Small.Height + 2), it.Pinned ? Theme.Accent : Theme.SubText, tf);
        }

        static string When(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "たった今";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " 分前";
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            return t.ToString("M/d HH:mm");
        }

        void CopySelected()
        {
            var it = Selected; if (it == null) return;
            if (ctx.SetClipboardOwn(it.Text)) footer.StatusText = "クリップボードにコピーしました。貼り付けたい場所で Ctrl+V を押してください。";
        }

        void TogglePin()
        {
            var it = Selected; if (it == null) return;
            if (!it.Pinned && ctx.History.PinnedCount >= History.MaxPins) { Dialogs.Info(this, "ピン留めできるのは " + History.MaxPins + " 件までです。"); return; }
            ctx.History.SetPinned(it, !it.Pinned);
        }

        void DeleteSelected()
        {
            var it = Selected; if (it == null) return;
            int idx = list.SelectedIndex;
            ctx.History.Remove(it);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
        }

        void ClearAll()
        {
            if (ctx.History.Items.Count == 0) return;
            if (!Dialogs.Confirm(this, "履歴をすべて削除しますか？ピン留めした項目も消えます。\nこの操作は元に戻せません。")) return;
            ctx.History.Clear();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
