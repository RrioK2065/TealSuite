// Teal Hub (アプリ管理) - オンラインのアップデート確認の流れ (いつ確認するか・お知らせ・ダウンロード → TealUpdater)
//  - 起動から少したってから 1 回、その後は 1 時間ごとに「前回から 24 時間たったか」を見て確認する
//  - 自動の確認では、新しいバージョンがあったときだけ画面のお知らせと「アップデート ▾」の印を出す
//    (通知領域のお知らせはバージョンごとに 1 回だけ)。失敗はログに残すだけ
//  - 「更新を確認」(手動) では結果を必ず表示する
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public class HubUpdates : IDisposable
    {
        public readonly HubSettings Settings;
        readonly Action save;
        public OnlineUpdate Online;
        public readonly MainForm Window;
        Timer timer;

        /// <summary>最後に分かった結果 (新しいバージョン / 最新)。通信の失敗では変えない</summary>
        public CheckResult Last { get; private set; }
        public ReleaseInfo Available { get { return Last != null && Last.IsNew ? Last.Release : null; } }
        public bool Checking { get; private set; }
        public bool Downloading { get; private set; }
        /// <summary>ダウンロード中の進み具合 (0〜100)。していなければ -1</summary>
        public int DownloadPercent { get; private set; }
        public int CheckCount { get; private set; }
        public string LastError { get; private set; }

        /// <summary>テスト用: 別スレッドを使わずにその場で実行する</summary>
        public bool Synchronous;
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>通知領域のお知らせ (タイトル, 本文)</summary>
        public Action<string, string> Balloon;
        /// <summary>結果の表示 (テストで差し替え)</summary>
        public Action<string> Message;
        /// <summary>手動の確認で新しいバージョンが見つかったときに内容を見せる (テストで差し替え)</summary>
        public Action<ReleaseInfo> ShowNotes;
        /// <summary>ダウンロードしたファイルを TealUpdater に渡す (テストで差し替え)</summary>
        public Func<string, string, bool> OpenPackage;
        public event Action Changed;

        public HubUpdates(MainForm window, HubSettings settings, Action save, WebFetcher http = null)
        {
            Window = window; Settings = settings ?? new HubSettings(); this.save = save ?? (() => { });
            Online = new OnlineUpdate(http);
            DownloadPercent = -1;
            Message = m => Dialogs.Info(Window, m);
            ShowNotes = r => { using (var f = new ReleaseNotesForm(this, r)) f.ShowDialog(Window); };
            OpenPackage = (path, ver) => UpdaterLauncher.OpenDownloaded(Window, path, ver);
        }

        /// <summary>設定を保存する (1.8.0: 更新内容のお知らせを見たとき)</summary>
        public void SaveSettings() { save(); }

        public bool AutoCheckEnabled { get { return !Settings.UpdateCheckOff; } }
        public string Installed { get { return Window.SuiteVersion ?? AppInfo.Version; } }

        public void SetAutoCheck(bool on)
        {
            Settings.UpdateCheckOff = !on;
            save();
            Fire();
        }

        /// <summary>自動の確認を始める (最初は firstDelayMs 後、その後 1 時間ごとに期限を見る)</summary>
        public void Start(int firstDelayMs)
        {
            if (timer != null) return;
            timer = new Timer { Interval = Math.Max(1, firstDelayMs) };
            timer.Tick += (s, e) => { timer.Interval = 60 * 60 * 1000; AutoCheck(); };
            timer.Start();
        }

        /// <summary>期限が来ていれば確認する。確認を始めたら true</summary>
        public bool AutoCheck()
        {
            if (Checking || Downloading || !OnlineUpdate.IsDue(Settings, UtcNow())) return false;
            Run(false);
            return true;
        }

        /// <summary>「更新を確認」</summary>
        public void CheckNow() { if (!Checking && !Downloading) Run(true); }

        void Run(bool manual)
        {
            Checking = true; Fire();
            var installed = Installed;
            if (Synchronous) { Done(SafeCheck(installed), manual); return; }
            Task.Factory.StartNew(() => SafeCheck(installed)).ContinueWith(t => OnUi(() => Done(t.Result, manual)));
        }

        CheckResult SafeCheck(string installed)
        {
            try { return Online.Check(installed); }
            catch (Exception ex) { return new CheckResult { Outcome = CheckOutcome.Error, Installed = installed, Message = "確認できませんでした。(" + ex.Message + ")" }; }
        }

        void Done(CheckResult r, bool manual)
        {
            Checking = false; CheckCount++;
            // つながらなかったときは記録しない (次の起動か 1 時間後にもう一度)
            if (r.Outcome != CheckOutcome.Offline)
            {
                Settings.LastUpdateCheck = UtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            }
            LastError = r.Outcome == CheckOutcome.NewVersion || r.Outcome == CheckOutcome.UpToDate || r.Outcome == CheckOutcome.NoRelease ? null : r.Message;
            if (r.Outcome == CheckOutcome.NewVersion || r.Outcome == CheckOutcome.UpToDate || r.Outcome == CheckOutcome.NoRelease) Last = r;
            var rel = Available;
            if (!manual && rel != null && Settings.NotifiedVersion != rel.Version && Balloon != null)
            {
                Settings.NotifiedVersion = rel.Version;
                Balloon("TealSuite " + rel.Version + " があります", "Teal Hub の画面の「今すぐアップデート」で新しいバージョンにできます。" + (rel.Summary.Length > 0 ? "\n" + rel.Summary : ""));
            }
            save();
            Fire();
            if (manual)
            {
                if (r.IsNew) ShowNotes(r.Release);
                else Message(r.Message);
            }
        }

        /// <summary>「今すぐアップデート」: ダウンロード → 確かめる → TealUpdater の確認画面へ</summary>
        public void StartUpdate()
        {
            var rel = Available;
            if (rel == null || Downloading) return;
            Downloading = true; DownloadPercent = 0; Fire();
            Action<int> progress = p => OnUi(() => { if (Downloading && p != DownloadPercent) { DownloadPercent = p; Fire(); } });
            if (Synchronous) { DownloadDone(rel, SafeDownload(rel, progress)); return; }
            Task.Factory.StartNew(() => SafeDownload(rel, progress)).ContinueWith(t => OnUi(() => DownloadDone(rel, t.Result)));
        }

        Tuple<string, string> SafeDownload(ReleaseInfo rel, Action<int> progress)
        {
            try { return Tuple.Create(Online.Download(rel, progress), (string)null); }
            catch (InvalidOperationException ex) { return Tuple.Create((string)null, ex.Message); }
            catch (Exception ex) { DataStore.Log("update download: " + ex); return Tuple.Create((string)null, "ダウンロードできませんでした。\n(" + ex.Message + ")"); }
        }

        void DownloadDone(ReleaseInfo rel, Tuple<string, string> r)
        {
            Downloading = false; DownloadPercent = -1; Fire();
            if (r.Item1 == null) { LastError = r.Item2; Message(r.Item2); return; }
            OpenPackage(r.Item1, rel.Version);
        }

        public static void OpenReleasePage(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u) || !UpdateSource.IsAllowed(u)) url = UpdateSource.ReleasesPage;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { DataStore.Log("open release page: " + ex.Message); }
        }

        void OnUi(Action a)
        {
            if (Synchronous || Window == null || Window.IsDisposed || !Window.IsHandleCreated) { try { a(); } catch (Exception ex) { DataStore.Log("update ui: " + ex.Message); } return; }
            try { Window.BeginInvoke(a); } catch (Exception ex) { DataStore.Log("update ui: " + ex.Message); }
        }

        void Fire() { var c = Changed; if (c != null) c(); }

        public void Dispose() { if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; } }
    }

    /// <summary>新しいバージョンの内容 (リリースの説明文) と「今すぐアップデート」</summary>
    public class ReleaseNotesForm : Form
    {
        public readonly TextBox Notes = new TextBox();
        public readonly FlatButton BtnUpdate = new FlatButton("今すぐアップデート", ButtonKind.Primary);
        public readonly FlatButton BtnClose = new FlatButton("閉じる", ButtonKind.Secondary);
        public readonly LinkLabel Page = new LinkLabel();

        public ReleaseNotesForm(HubUpdates updates, ReleaseInfo r)
        {
            Theme.ApplyForm(this);
            Text = "TealSuite " + r.Version;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.S(520, 420);
            var header = new HeaderBar("TealSuite " + r.Version + " があります", "今は " + updates.Installed + " ・ 公開日 " + ShortDate(r.Published), false);
            Notes.Multiline = true; Notes.ReadOnly = true; Notes.ScrollBars = ScrollBars.Vertical; Notes.BorderStyle = BorderStyle.None;
            Notes.BackColor = Theme.Surface; Notes.Font = Theme.Body; Notes.Dock = DockStyle.Fill;
            Notes.Text = CleanNotes(r.Notes).Replace("\n", "\r\n");
            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(16, 12, 16, 8), BackColor = Theme.Surface };
            body.Controls.Add(Notes);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(56), BackColor = Theme.Background };
            BtnUpdate.FitWidth(120); BtnClose.FitWidth(72);
            Page.Text = "リリース ページを開く"; Page.AutoSize = true; Page.Font = Theme.Small; Page.LinkColor = Theme.Accent;
            Page.LinkClicked += (s, e) => HubUpdates.OpenReleasePage(r.PageUrl);
            bottom.Controls.Add(BtnUpdate); bottom.Controls.Add(BtnClose); bottom.Controls.Add(Page);
            bottom.Layout += (s, e) =>
            {
                int pad = Theme.S(12);
                BtnUpdate.Location = new Point(bottom.Width - pad - BtnUpdate.Width, (bottom.Height - BtnUpdate.Height) / 2);
                BtnClose.Location = new Point(BtnUpdate.Left - Theme.S(8) - BtnClose.Width, BtnUpdate.Top);
                Page.Location = new Point(pad, (bottom.Height - Page.Height) / 2);
            };
            BtnUpdate.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); updates.StartUpdate(); };
            BtnClose.Click += (s, e) => Close();
            CancelButton = BtnClose; AcceptButton = BtnUpdate;
            Controls.Add(body); Controls.Add(bottom); Controls.Add(header);
        }

        static string ShortDate(string iso)
        {
            DateTime d;
            return DateTime.TryParse(iso ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out d) ? d.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "-";
        }

        /// <summary>見出しの # や確認用の sha256 の行を除いて読みやすくする</summary>
        public static string CleanNotes(string notes)
        {
            var lines = new System.Collections.Generic.List<string>();
            bool inSha = false;
            foreach (var raw in (notes ?? "").Replace("\r", "").Split('\n'))
            {
                var l = raw.TrimEnd();
                if (l.TrimStart().StartsWith("#")) { var h = l.TrimStart('#', ' '); inSha = h.StartsWith("sha256", StringComparison.OrdinalIgnoreCase) || h.StartsWith("確認用") || h.StartsWith("ダウンロード"); if (!inSha) lines.Add("■ " + h); continue; }
                if (inSha || System.Text.RegularExpressions.Regex.IsMatch(l.Trim().Trim('`'), "^[0-9a-fA-F]{64}\\b") || l.Trim() == "```") continue;
                lines.Add(l.StartsWith("- ") || l.StartsWith("* ") ? "・" + l.Substring(2) : l);
            }
            var text = string.Join("\n", lines).Trim();
            return text.Length > 0 ? text : "(説明はありません)";
        }
    }
}
