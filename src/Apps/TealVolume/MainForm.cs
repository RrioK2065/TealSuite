// Teal Volume (音量操作) - メイン画面 (上: 全体の音量 / 下: アプリごとの音量)
// ×・Esc では閉じずに隠れる (VolumeTray が通知領域に常駐させる)。本当に閉じるのは Shutdown() のときだけ
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Volume
{
    /// <summary>スピーカーの絵のミュートボタン</summary>
    public class SpeakerButton : Control
    {
        bool muted, hover;
        public bool Muted { get { return muted; } set { muted = value; Invalidate(); } }
        public int Level { get; set; } // 0-100 (波の数を変える)
        public SpeakerButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = Theme.S(36, 32); Cursor = Cursors.Hand; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(rr, Theme.S(Theme.Radius)))
            {
                var back = muted ? Theme.DangerSoft : hover ? Theme.SurfaceAlt : Theme.Surface;
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var p = new Pen(muted ? Theme.Danger : (Focused ? Theme.Accent : Theme.Border))) g.DrawPath(p, path);
            }
            Color c = muted ? Theme.Danger : Theme.Text;
            float s = Theme.ScaleFactor, cx = Width / 2f - 5 * s, cy = Height / 2f;
            var body = new[] {
                new PointF(cx - 6 * s, cy - 3 * s), new PointF(cx - 2 * s, cy - 3 * s), new PointF(cx + 3 * s, cy - 7 * s),
                new PointF(cx + 3 * s, cy + 7 * s), new PointF(cx - 2 * s, cy + 3 * s), new PointF(cx - 6 * s, cy + 3 * s) };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, body);
            using (var p = new Pen(c, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (muted)
                {
                    g.DrawLine(p, cx + 7 * s, cy - 4 * s, cx + 13 * s, cy + 4 * s);
                    g.DrawLine(p, cx + 13 * s, cy - 4 * s, cx + 7 * s, cy + 4 * s);
                }
                else
                {
                    if (Level > 0) g.DrawArc(p, cx + 1 * s, cy - 4 * s, 8 * s, 8 * s, -50, 100);
                    if (Level > 33) g.DrawArc(p, cx - 1 * s, cy - 7.5f * s, 15 * s, 15 * s, -50, 100);
                    if (Level > 66) g.DrawArc(p, cx - 3 * s, cy - 11 * s, 22 * s, 22 * s, -50, 100);
                }
            }
        }
    }

    /// <summary>1 アプリぶんの行: アイコン / 名前 / スライダー / % / ミュート</summary>
    public class VolumeRow : Panel
    {
        public readonly FlatSlider Slider = new FlatSlider();
        public readonly SpeakerButton Mute = new SpeakerButton();
        readonly Label percent = new Label();
        readonly Label name = new Label();
        readonly Label sub = new Label();
        readonly PictureBox pic = new PictureBox();
        public AudioApp App;
        public bool Divider = true;

        public VolumeRow(Image icon, string title)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(58); Dock = DockStyle.Top; BackColor = Theme.Surface;
            Padding = Theme.P(14, 0, 12, 0);
            pic.Size = Theme.S(28, 28); pic.SizeMode = PictureBoxSizeMode.Zoom; pic.Image = icon; pic.BackColor = Theme.Surface;
            var picHost = new Panel { Dock = DockStyle.Left, Width = Theme.S(40), BackColor = Theme.Surface };
            picHost.Controls.Add(pic);
            picHost.Resize += (s, e) => pic.Location = new Point(0, (picHost.Height - pic.Height) / 2);

            var names = new Panel { Dock = DockStyle.Left, Width = Theme.S(190), BackColor = Theme.Surface };
            name.Text = title; name.Font = Theme.Bold; name.ForeColor = Theme.Text; name.AutoEllipsis = true; name.UseMnemonic = false;
            name.Dock = DockStyle.Top; name.Height = Theme.Bold.Height + Theme.S(2);
            sub.Font = Theme.Small; sub.ForeColor = Theme.SubText; sub.Dock = DockStyle.Top; sub.Height = Theme.Small.Height + Theme.S(2); sub.UseMnemonic = false;
            names.Controls.Add(sub); names.Controls.Add(name);
            names.Resize += (s, e) => names.Padding = new Padding(0, Math.Max(0, (names.Height - name.Height - sub.Height) / 2), 0, 0);

            Slider.Dock = DockStyle.Fill; Slider.Minimum = 0; Slider.Maximum = 100; Slider.SmallChange = 1; Slider.LargeChange = 10; Slider.ShiftStep = 5;
            var sliderHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            sliderHost.Controls.Add(Slider);
            sliderHost.Resize += (s, e) => sliderHost.Padding = new Padding(0, Math.Max(0, (sliderHost.Height - Theme.S(28)) / 2), 0, Math.Max(0, (sliderHost.Height - Theme.S(28)) / 2));

            percent.Dock = DockStyle.Right; percent.Width = Theme.S(52); percent.Font = Theme.Title; percent.ForeColor = Theme.Text;
            percent.TextAlign = ContentAlignment.MiddleRight; percent.UseMnemonic = false;
            var muteHost = new Panel { Dock = DockStyle.Right, Width = Theme.S(48), BackColor = Theme.Surface };
            muteHost.Controls.Add(Mute);
            muteHost.Resize += (s, e) => Mute.Location = new Point(muteHost.Width - Mute.Width, (muteHost.Height - Mute.Height) / 2);

            Controls.Add(sliderHost); Controls.Add(percent); Controls.Add(muteHost); Controls.Add(names); Controls.Add(picHost);
            Slider.ValueChanged += (s, e) => UpdatePercent();
        }

        public string Title { get { return name.Text; } set { name.Text = value; } }
        public string SubText { get { return sub.Text; } set { sub.Text = value; } }
        public void SetIcon(Image img) { pic.Image = img; }

        public void Show(int value, bool muted)
        {
            if (!Slider.IsDragging && Slider.Value != value) Slider.Value = value;
            Mute.Muted = muted; Slider.Dimmed = muted;
            UpdatePercent(); Slider.Invalidate();
        }

        void UpdatePercent()
        {
            percent.Text = Slider.Value + "%";
            percent.ForeColor = Mute.Muted ? Theme.SubText : Theme.Text;
            Mute.Level = Slider.Value; Mute.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Divider) using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, Theme.S(14), Height - 1, Width - Theme.S(14), Height - 1);
        }
    }

    public class MainForm : Form
    {
        readonly IAudioBackend audio;
        readonly bool demo;
        readonly HeaderBar header;
        readonly FooterBar footer = new FooterBar();
        readonly VolumeRow masterRow;
        readonly Panel appList = new Panel();
        readonly Label empty = new Label();
        readonly Dictionary<string, VolumeRow> rows = new Dictionary<string, VolumeRow>();
        readonly Dictionary<string, Image> icons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly Timer timer = new Timer();
        readonly Image genericIcon, systemIcon, speakerIcon;
        string loadError, hotkeyText;
        bool allowClose, audioReleased;

        /// <summary>×・Esc が押された (隠してほしい)</summary>
        public event Action HideRequested;
        /// <summary>「設定」ボタン</summary>
        public event Action SettingsRequested;

        /// <summary>ヘッダーに出すショートカットの説明 (例: "Alt + V で開閉")。null なら出さない</summary>
        public string FooterText { get { return footer.StatusText; } }
        public string HotkeyText { get { return hotkeyText; } set { hotkeyText = value; UpdateDetail(); UpdateFooter(); } }

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = AppInfo.WindowTitle;
            MinimumSize = Theme.S(520, 380);
            KeyPreview = true;

            demo = !AppInfo.IsWindows || Environment.GetEnvironmentVariable("TEALSUITE_DEMO") == "1";
            try { audio = demo ? (IAudioBackend)new DemoBackend() : new CoreAudioBackend(); }
            catch (Exception ex) { loadError = ex.Message; DataStore.Log("audio init: " + ex); audio = null; }

            genericIcon = Glyph(Theme.SubText, false); systemIcon = Glyph(Theme.Accent, true); speakerIcon = Glyph(Theme.Accent, false);

            header = HeaderBar.ForApp("");
            var btnRefresh = header.AddAction(new FlatButton("更新", ButtonKind.Secondary));
            var btnWin = header.AddAction(new FlatButton("Windows の設定", ButtonKind.Ghost));
            var btnSettings = header.AddAction(new FlatButton("設定", ButtonKind.Ghost));
            btnSettings.Click += (s, e) => { if (SettingsRequested != null) SettingsRequested(); };
            new ToolTip().SetToolTip(btnSettings, "ショートカット・常駐・スタートアップの設定 (Teal Hub で開きます)");
            btnRefresh.Click += (s, e) => RefreshAll(true);
            btnWin.Click += (s, e) => { try { Process.Start("ms-settings:apps-volume"); } catch { try { Process.Start("sndvol.exe"); } catch { } } };
            new ToolTip().SetToolTip(btnRefresh, "最新の状態に更新 (F5)");
            new ToolTip().SetToolTip(btnWin, "Windows の「音量ミキサー」設定を開く");

            // 全体
            var masterCard = new CardPanel("全体の音量") { Dock = DockStyle.Top, Height = Theme.S(40 + 58 + 10), Padding = new Padding(1, Theme.S(40), 1, Theme.S(6)) };
            masterRow = new VolumeRow(speakerIcon, "スピーカー") { Divider = false };
            masterRow.Slider.ValueChanged += (s, e) => { if (masterRow.Slider.IsUserChange && audio != null) audio.SetMasterVolume(AudioNames.FromPercent(masterRow.Slider.Value)); };
            masterRow.Mute.Click += (s, e) => { if (audio == null) return; audio.SetMasterMute(!audio.MasterMuted); masterRow.Show(masterRow.Slider.Value, audio.MasterMuted); };
            masterCard.Controls.Add(masterRow);

            // アプリごと
            var appsCard = new CardPanel("アプリごとの音量") { Dock = DockStyle.Fill, Padding = new Padding(1, Theme.S(40), 1, Theme.S(8)) };
            appList.Dock = DockStyle.Fill; appList.AutoScroll = true; appList.BackColor = Theme.Surface;
            empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = Theme.SubText; empty.Font = Theme.Body;
            empty.Text = "音を出しているアプリはありません。\nアプリで音を再生すると、ここに表示されます。"; empty.Visible = false;
            appsCard.Controls.Add(appList); appsCard.Controls.Add(empty);

            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(12) };
            body.Controls.Add(appsCard);
            body.Controls.Add(Ui.Spacer(10));
            body.Controls.Add(masterCard);
            Controls.Add(body); Controls.Add(footer); Controls.Add(header);

            WindowMemory.Attach(this, 720, 620);
            timer.Interval = 1500;
            timer.Tick += (s, e) => RefreshAll(false);
            // 表示されたら最新の状態に更新して監視を始める。隠れている間は止める
            VisibleChanged += (s, e) => { if (Visible) { RefreshAll(true); timer.Start(); } else timer.Stop(); };
            FormClosed += (s, e) => ReleaseAudio();
        }

        /// <summary>アプリのアイコンが取れないとき用の小さな絵 (スピーカー / 歯車風)</summary>
        static Image Glyph(Color c, bool system)
        {
            int px = Theme.S(28);
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.RoundRect(new RectangleF(0, 0, px - 1, px - 1), px * 0.26f))
                using (var b = new SolidBrush(Theme.Blend(c, Color.White, 0.85f))) g.FillPath(b, path);
                float s = px / 28f, cx = px / 2f - 3 * s, cy = px / 2f;
                using (var b = new SolidBrush(c))
                {
                    if (system)
                    {
                        g.FillEllipse(b, px / 2f - 7 * s, cy - 7 * s, 14 * s, 14 * s);
                        using (var w = new SolidBrush(Theme.Blend(c, Color.White, 0.85f))) g.FillEllipse(w, px / 2f - 3 * s, cy - 3 * s, 6 * s, 6 * s);
                        for (int i = 0; i < 8; i++)
                        {
                            var st = g.Save();
                            g.TranslateTransform(px / 2f, cy); g.RotateTransform(i * 45);
                            g.FillRectangle(b, -1.6f * s, -9.5f * s, 3.2f * s, 4 * s);
                            g.Restore(st);
                        }
                    }
                    else
                    {
                        g.FillPolygon(b, new[] { new PointF(cx - 6 * s, cy - 3 * s), new PointF(cx - 2 * s, cy - 3 * s), new PointF(cx + 3 * s, cy - 7 * s),
                            new PointF(cx + 3 * s, cy + 7 * s), new PointF(cx - 2 * s, cy + 3 * s), new PointF(cx - 6 * s, cy + 3 * s) });
                        using (var p = new Pen(c, 1.6f * s)) { g.DrawArc(p, cx + 1 * s, cy - 4 * s, 8 * s, 8 * s, -50, 100); g.DrawArc(p, cx - 1 * s, cy - 7.5f * s, 15 * s, 15 * s, -50, 100); }
                    }
                }
            }
            return bmp;
        }

        Image IconFor(AudioApp a)
        {
            if (a.IsSystem) return systemIcon;
            if (string.IsNullOrEmpty(a.ExePath)) return genericIcon;
            Image img;
            if (icons.TryGetValue(a.ExePath, out img)) return img;
            img = genericIcon;
            try
            {
                using (var ico = Icon.ExtractAssociatedIcon(a.ExePath))
                    if (ico != null) { using (var big = new Icon(ico, 32, 32)) img = big.ToBitmap(); }
            }
            catch { }
            icons[a.ExePath] = img;
            return img;
        }

        void RefreshAll(bool force)
        {
            if (audio == null)
            {
                header.SetDetail("音声デバイスに接続できませんでした");
                empty.Text = "Windows の音声機能を利用できませんでした。\n" + (loadError ?? "");
                empty.Visible = true; appList.Visible = false; masterRow.Enabled = false;
                footer.StatusText = "エラー: " + (loadError ?? "不明");
                return;
            }
            // ドラッグ中は行の入れ替えをしない (つかんでいる行が消えないように)
            if (!force && (masterRow.Slider.IsDragging || rows.Values.Any(r => r.Slider.IsDragging))) return;
            List<AudioApp> apps;
            try { apps = audio.Refresh(); }
            catch (Exception ex) { DataStore.Log("refresh: " + ex); footer.StatusText = "更新に失敗しました: " + ex.Message; return; }

            UpdateDetail();
            masterRow.Title = audio.DeviceName != null ? ShortDevice(audio.DeviceName) : "スピーカー";
            masterRow.SubText = audio.HasMaster ? (audio.MasterMuted ? "ミュート中" : "すべてのアプリに影響します") : "利用できません";
            masterRow.Enabled = audio.HasMaster;
            masterRow.Show(AudioNames.ToPercent(audio.MasterVolume), audio.MasterMuted);

            var keys = apps.Select(a => a.Key).ToList();
            bool layoutChanged = !keys.SequenceEqual(rows.Keys.ToList()) || rows.Count != apps.Count;
            if (layoutChanged)
            {
                appList.SuspendLayout();
                foreach (var k in rows.Keys.Except(keys).ToList()) { var r = rows[k]; appList.Controls.Remove(r); r.Dispose(); rows.Remove(k); }
                foreach (var a in apps)
                {
                    if (rows.ContainsKey(a.Key)) continue;
                    var row = new VolumeRow(IconFor(a), a.Name);
                    var key = a.Key;
                    row.Slider.ValueChanged += (s, e) => { if (row.Slider.IsUserChange && row.App != null) audio.SetVolume(row.App, AudioNames.FromPercent(row.Slider.Value)); };
                    row.Mute.Click += (s, e) => { if (row.App == null) return; audio.SetMute(row.App, !row.App.Muted); row.Show(row.Slider.Value, row.App.Muted); UpdateSub(row); };
                    rows[key] = row;
                    appList.Controls.Add(row);
                }
                // Dock=Top は後から前面に来たものほど上 → 表示順に並べ直す
                var ordered = new Dictionary<string, VolumeRow>();
                for (int i = 0; i < apps.Count; i++) { var r = rows[apps[i].Key]; appList.Controls.SetChildIndex(r, apps.Count - 1 - i); r.Divider = i < apps.Count - 1; ordered[apps[i].Key] = r; }
                rows.Clear(); foreach (var kv in ordered) rows[kv.Key] = kv.Value;
                appList.ResumeLayout();
            }
            foreach (var a in apps)
            {
                var row = rows[a.Key];
                row.App = a;
                row.Title = a.Name;
                row.Show(AudioNames.ToPercent(a.Volume), a.Muted);
                UpdateSub(row);
            }
            empty.Visible = apps.Count == 0; appList.Visible = apps.Count > 0;
            appCount = apps.Count;
            UpdateFooter();
        }

        int appCount = -1;
        // 下の帯: ショートカット (Alt + V で開閉) とスライダーの操作説明
        void UpdateFooter()
        {
            if (appCount < 0) return;
            footer.StatusText = (hotkeyText != null ? hotkeyText + " ・ " : "") + appCount + " 個のアプリ ・ ドラッグで 1% ずつ、Shift を押しながらで 5% ずつ";
        }

        static string ShortDevice(string n) { return n; }

        void UpdateDetail()
        {
            if (audio == null) return;
            var parts = new System.Collections.Generic.List<string>();
            // ショートカットと Shift + ドラッグの説明は下の帯に出す (ヘッダーは幅が足りないと役割だけになる)
            if (audio.DeviceName == null) parts.Add("出力デバイスがありません");
            if (demo) parts.Add("デモ表示");
            header.SetDetail(string.Join(" ・ ", parts));
        }

        void ReleaseAudio()
        {
            timer.Stop();
            if (audioReleased) return;
            audioReleased = true;
            try { if (audio != null) audio.Dispose(); } catch { }
        }

        /// <summary>アプリの終了時: 本当に閉じる</summary>
        public void Shutdown()
        {
            allowClose = true;
            if (Visible) Close();
            ReleaseAudio();
            if (!IsDisposed) Dispose();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);   // 先に位置を保存させる (WindowMemory)
            if (e.Cancel || allowClose || AppHost.IsExiting) return;
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (HideRequested != null) HideRequested(); else Hide();
            }
        }

        static void UpdateSub(VolumeRow row)
        {
            var a = row.App; if (a == null) return;
            row.SubText = a.Muted ? "ミュート中" : a.IsSystem ? "通知音など" : a.Active ? "再生中" : "待機中";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5) { RefreshAll(true); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }   // 隠れる (OnFormClosing)
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
