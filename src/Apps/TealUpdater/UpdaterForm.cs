// TealUpdater - 画面 (確認 → 進み具合 → 結果)。前のバージョンに戻すときも同じ画面を使う
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Updater
{
    public enum UpdaterState { Help, Confirm, Working, Done, Failed }

    /// <summary>「変わる内容」の 1 行 (アプリ名 ・ 1.4.0 → 1.5.0 ・ 補足)</summary>
    public class ChangeRow : Panel
    {
        public readonly Label NameLabel = new Label(), ValueLabel = new Label(), NoteLabel = new Label();
        public ChangeRow(string name, string value, string note, Color valueColor, bool badgeNew)
        {
            BackColor = Theme.Surface;
            Height = Theme.S(note == null ? 30 : 46);
            Dock = DockStyle.Top;
            NameLabel.Text = name; NameLabel.Font = Theme.Bold; NameLabel.ForeColor = Theme.Text; NameLabel.AutoSize = true; NameLabel.UseMnemonic = false;
            ValueLabel.Text = value; ValueLabel.Font = Theme.Body; ValueLabel.ForeColor = valueColor; ValueLabel.AutoSize = true; ValueLabel.UseMnemonic = false;
            if (badgeNew) { ValueLabel.BackColor = Theme.AccentSoft; ValueLabel.Padding = Theme.P(6, 1, 6, 1); }
            NoteLabel.Text = note ?? ""; NoteLabel.Font = Theme.Small; NoteLabel.ForeColor = Theme.SubText; NoteLabel.AutoSize = true; NoteLabel.UseMnemonic = false;
            Controls.Add(NameLabel); Controls.Add(ValueLabel); Controls.Add(NoteLabel);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int top = Theme.S(4);
            NameLabel.Location = new Point(0, top);
            ValueLabel.Location = new Point(Math.Max(NameLabel.Right + Theme.S(12), Theme.S(150)), top - (ValueLabel.Padding.Top > 0 ? Theme.S(1) : 0));
            NoteLabel.Location = new Point(Math.Max(NameLabel.Right + Theme.S(12), Theme.S(150)), top + Theme.S(22));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    public class UpdaterForm : Form
    {
        // ---- テストで差し替えられる処理 (起動中アプリの検出・終了・再起動) ----
        public static Func<InstalledSuite, List<RunningApp>> FindRunning = SuiteProcesses.Find;
        public static Func<IList<RunningApp>, int, List<RunningApp>> CloseApps = SuiteProcesses.CloseAll;
        public static Func<InstalledSuite, IEnumerable<string>, bool, List<string>> RestartApps = SuiteProcesses.Restart;
        public static Func<IWin32Window, string, bool> AskKill = (o, m) => Dialogs.Confirm(o, m, "アプリを強制終了しますか？");
        public const int CloseTimeoutMs = 8000;

        readonly UpdateEngine engine;
        readonly HeaderBar header;
        readonly CardPanel card = new CardPanel("変わる内容");
        readonly Panel list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface };
        readonly Label info = new Label { Dock = DockStyle.Bottom, Font = Theme.Small, ForeColor = Theme.SubText, UseMnemonic = false };
        readonly Label notice = new Label { Dock = DockStyle.Bottom, Font = Theme.Small, UseMnemonic = false, Visible = false };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 8, Visible = false, Minimum = 0, Maximum = 100 };
        readonly LinkLabel undo = new LinkLabel { AutoSize = true, Visible = false, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentPressed, LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.Body };
        readonly FlatButton primary = new FlatButton("アップデート", ButtonKind.Primary);
        readonly FlatButton secondary = new FlatButton("キャンセル", ButtonKind.Secondary);
        readonly List<ChangeRow> rows = new List<ChangeRow>();

        UpdatePackage package;
        UpdatePlan plan;
        BackupInfo backup;          // 戻すとき
        List<BackupInfo> backups = new List<BackupInfo>();
        List<RunningApp> running = new List<RunningApp>();
        Action primaryAction;
        UpdaterState state;
        UpdateResult lastResult;

        public static UpdaterForm ForPackage(UpdateEngine engine, string path)
        {
            var f = new UpdaterForm(engine);
            f.LoadPackage(path);
            return f;
        }

        public static UpdaterForm ForRollback(UpdateEngine engine)
        {
            var f = new UpdaterForm(engine);
            f.ShowRollback();
            return f;
        }

        public static UpdaterForm ForHelp(UpdateEngine engine)
        {
            var f = new UpdaterForm(engine);
            f.ShowHelp();
            return f;
        }

        UpdaterForm(UpdateEngine engine)
        {
            this.engine = engine;
            Theme.ApplyForm(this);
            Text = "TealSuite のアップデート";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Theme.S(580, 560);
            MinimumSize = Theme.S(480, 420);
            MaximizeBox = false;

            header = new HeaderBar("TealSuite のアップデート", "", false);
            var body = new Panel { Dock = DockStyle.Fill, Padding = Theme.P(Theme.Pad), BackColor = Theme.Background };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(Theme.ButtonHeight + 14), Padding = Theme.P(0, 12, 0, 0), BackColor = Theme.Background, WrapContents = false };
            primary.FitWidth(110); secondary.FitWidth(96);
            primary.Margin = Theme.P(8, 0, 0, 0);
            undo.Margin = Theme.P(0, 8, 16, 0);
            buttons.Controls.Add(primary); buttons.Controls.Add(secondary); buttons.Controls.Add(undo);
            primary.Click += (s, e) => { var a = primaryAction; if (a != null) a(); };
            secondary.Click += (s, e) => Close();
            undo.LinkClicked += (s, e) => ShowRollback();

            info.Height = Theme.S(76);
            notice.Height = Theme.S(48);
            card.Dock = DockStyle.Fill;
            card.Controls.Add(list);
            card.Controls.Add(Ui.Spacer(8, DockStyle.Bottom));
            card.Controls.Add(progress);
            card.Controls.Add(notice);
            card.Controls.Add(info);
            body.Controls.Add(card);
            body.Controls.Add(buttons);
            Controls.Add(body);
            Controls.Add(header);
            AcceptButton = primary;
            FormClosing += (s, e) => { if (state == UpdaterState.Working) e.Cancel = true; };
        }

        // ---- テスト用 ----
        public UpdaterState State { get { return state; } }
        public UpdatePlan Plan { get { return plan; } }
        public IList<ChangeRow> Rows { get { return rows; } }
        public string InfoText { get { return info.Text; } }
        public string NoticeText { get { return notice.Visible ? notice.Text : ""; } }
        public string CardTitle { get { return card.Title; } }
        public string HeaderSubtitle { get { return header.Subtitle; } }
        public FlatButton PrimaryButton { get { return primary; } }
        public bool UndoVisible { get { return undo.Visible; } }
        public UpdateResult LastResult { get { return lastResult; } }
        public IList<RunningApp> RunningApps { get { return running; } }
        /// <summary>主ボタンを押したのと同じ。作業は別スレッドで進むので、テストでは WaitIdle で待つ</summary>
        public void ClickPrimaryForTest() { if (primary.Enabled && primaryAction != null) primaryAction(); }
        public bool WaitIdle(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (state == UpdaterState.Working && DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
            Application.DoEvents();
            return state != UpdaterState.Working;
        }

        // ---------------------------------------------------------------- 画面の組み立て

        void ClearRows()
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
            rows.Clear();
            list.ResumeLayout();
        }

        void SetRows(IEnumerable<ChangeRow> items)
        {
            ClearRows();
            var all = items.ToList();
            list.SuspendLayout();
            for (int i = all.Count - 1; i >= 0; i--) list.Controls.Add(all[i]);   // Dock=Top は後に足したものが上
            rows.AddRange(all);
            list.ResumeLayout();
        }

        void SetNotice(string text, Color color)
        {
            notice.Text = text ?? "";
            notice.ForeColor = color;
            notice.Visible = !string.IsNullOrEmpty(text);
        }

        void SetButtons(string primaryText, Action action, bool enabled, string secondaryText)
        {
            primary.Text = primaryText; primary.FitWidth(110);
            primary.Visible = primaryText != null;
            primary.Enabled = enabled;
            primaryAction = action;
            secondary.Text = secondaryText; secondary.FitWidth(96);
            secondary.Enabled = true;
        }

        void SetHeader(string sub) { header.Subtitle = sub; header.Invalidate(); }

        IEnumerable<ChangeRow> ChangeRows(UpdatePlan p, bool done)
        {
            foreach (var c in p.Changes)
            {
                var note = c.Files > 0 ? c.Files + " 個のファイル" : null;
                if (c.IsNew) note = (note != null ? note + " ・ " : "") + "Teal Hub に追加されます";
                yield return new ChangeRow((done ? "✓ " : "") + c.Name, c.Describe, note, c.IsNew ? Theme.Accent : Theme.Text, c.IsNew);
            }
            if (p.OtherFiles > 0) yield return new ChangeRow((done ? "✓ " : "") + "共通のファイル", p.OtherFiles + " 個 (README・TealUpdater など)", null, Theme.SubText, false);
            if (p.DeleteCount > 0) yield return new ChangeRow((done ? "✓ " : "") + "使わなくなったファイル", p.DeleteCount + " 個を削除", null, Theme.SubText, false);
        }

        // ---------------------------------------------------------------- 使い方 (引数なしで起動したとき)

        void ShowHelp()
        {
            state = UpdaterState.Help;
            SetHeader("アップデート ファイルを選んでください");
            card.Title = "アップデートのしかた"; card.Invalidate();
            ClearRows();
            var inst = SafeInstalled();
            info.Height = Theme.S(220);
            info.Dock = DockStyle.Fill;
            info.ForeColor = Theme.Text;
            info.Text = "今のバージョン: TealSuite " + (inst != null && inst.SuiteVersion.Length > 0 ? inst.SuiteVersion : "(不明)") + "\n\n"
                + "1. 新しいバージョンのアップデート ファイル (TealSuite-x.y.z.tealupdate) を受け取ります。\n"
                + "2. ファイルをダブルクリックするか、Teal Hub の画面にドラッグします。ここの「ファイルを選ぶ…」でも開けます。\n"
                + "3. 変わる内容を確認して「アップデート」を押します。\n\n"
                + "起動中のアプリはいったん終了し、終わったら起動し直します。置き換える前のファイルは backup フォルダに残ります。\n"
                + "パスワードやメモなどのデータ (%APPDATA%\\TealSuite) は変わりません。";
            SetButtons("ファイルを選ぶ…", ChooseFile, true, "閉じる");
            undo.Text = "前のバージョンに戻す…";
            undo.Visible = engine.ListBackups().Count > 0;
        }

        void ChooseFile()
        {
            using (var d = new OpenFileDialog { Filter = "TealSuite アップデート ファイル (*.tealupdate)|*.tealupdate|すべてのファイル (*.*)|*.*", Title = "アップデート ファイルを開く" })
                if (d.ShowDialog(this) == DialogResult.OK) LoadPackage(d.FileName);
        }

        InstalledSuite SafeInstalled() { try { return InstalledSuite.Load(engine.Dir); } catch { return null; } }

        // ---------------------------------------------------------------- 確認

        public void LoadPackage(string path)
        {
            info.Dock = DockStyle.Bottom; info.Height = Theme.S(76); info.ForeColor = Theme.SubText;
            undo.Visible = false;
            InstalledSuite inst;
            try
            {
                package = UpdatePackage.Read(path);
                inst = InstalledSuite.Load(engine.Dir);
                plan = engine.PlanChecked(package, inst);
            }
            catch (Exception ex)
            {
                ShowFailed("アップデート ファイルを開けませんでした", ex.Message + "\n\n" + path, false);
                return;
            }
            state = UpdaterState.Confirm;
            try { running = FindRunning(inst) ?? new List<RunningApp>(); } catch { running = new List<RunningApp>(); }
            SetHeader("TealSuite " + (plan.From.Length > 0 ? plan.From : "?") + " → " + plan.To);
            card.Title = "変わる内容"; card.Invalidate();
            SetRows(ChangeRows(plan, false));

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(package.Notes)) lines.Add(package.Notes.Trim());
            var runningNames = running.Select(r => r.Name).ToList();
            lines.Add(runningNames.Count > 0
                ? "起動中の " + string.Join("・", runningNames) + " はいったん終了し、終わったら起動し直します。"
                : "終わったら Teal Hub を起動します。");
            lines.Add("置き換える前のファイルは backup フォルダに残り、あとで元に戻せます。");
            lines.Add("パスワードやメモなどのデータ (%APPDATA%\\TealSuite) は変わりません。");
            info.Text = string.Join("\n", lines);
            info.Height = Theme.S(8) + (Theme.Small.Height + Theme.S(2)) * info.Text.Split('\n').Length;

            if (plan.Problems.Count > 0) SetNotice("⚠ " + string.Join("\n⚠ ", plan.Problems), Theme.Danger);
            else if (plan.Warnings.Count > 0) SetNotice(string.Join("\n", plan.Warnings), Theme.Warning);
            else SetNotice(null, Theme.SubText);
            SetButtons("アップデート", StartUpdate, plan.CanApply, "キャンセル");
        }

        // ---------------------------------------------------------------- 実行

        void StartUpdate()
        {
            var inst = InstalledSuite.Load(engine.Dir);
            RunWork("アップデートしています…", () =>
            {
                List<RunningApp> stop;
                try { stop = FindRunning(inst) ?? new List<RunningApp>(); } catch { stop = new List<RunningApp>(); }
                if (!StopApps(stop)) return null;
                engine.Progress = (p, w) => Ui_(() => { progress.Value = Math.Max(0, Math.Min(100, p)); SetNotice(w, Theme.SubText); });
                var res = engine.Apply(package);
                if (res.Ok || res.RolledBack)
                {
                    Ui_(() => SetNotice("アプリを起動しています…", Theme.SubText));
                    try { RestartApps(InstalledSuite.Load(engine.Dir), stop.Select(r => r.App.Id), true); } catch { }
                }
                return res;
            }, res =>
            {
                if (res == null) return;
                if (res.Ok) ShowDone(res, "アップデートが完了しました", "TealSuite " + res.To + " になりました。");
                else ShowFailed("アップデートできませんでした",
                    res.Error + "\n\n" + (res.RolledBack ? "元の状態 (" + res.From + ") に戻しました。" : res.RollbackError != null ? "元に戻す途中でも問題がありました: " + res.RollbackError + "\nbackup フォルダから手で戻せます。" : "何も変更していません。"),
                    res.RollbackError != null);
            });
        }

        /// <summary>起動中のアプリを終了してもらう (作業スレッドから呼ぶ)。取りやめたら false</summary>
        bool StopApps(List<RunningApp> stop)
        {
            if (stop.Count == 0) return true;
            Ui_(() => SetNotice("アプリを終了しています: " + string.Join("・", stop.Select(r => r.Name)), Theme.SubText));
            var left = CloseApps(stop, CloseTimeoutMs);
            if (left.Count == 0) return true;
            bool kill = false;
            Invoke((Action)(() => kill = AskKill(this, string.Join("・", left.Select(r => r.Name)) + " が終了しません。\n\n強制終了してアップデートを続けますか？\n(保存していない内容は失われます。「いいえ」でアップデートを取りやめます)")));
            if (kill && Kill(left).Count == 0) return true;
            Ui_(() => { state = UpdaterState.Confirm; LoadPackage(package.SourcePath); SetNotice("アプリが終了しなかったため、アップデートを取りやめました。アプリを閉じてからもう一度お試しください。", Theme.Danger); });
            return false;
        }

        static List<RunningApp> Kill(List<RunningApp> left) { try { return SuiteProcesses.Kill(left); } catch { return left; } }

        void RunWork(string title, Func<UpdateResult> work, Action<UpdateResult> done)
        {
            state = UpdaterState.Working;
            SetHeader(title);
            progress.Value = 0; progress.Visible = true;
            primary.Enabled = false; secondary.Enabled = false; undo.Visible = false;
            ControlBox = false;
            var t = new Thread(() =>
            {
                UpdateResult r = null; Exception err = null;
                try { r = work(); } catch (Exception ex) { err = ex; }
                Ui_(() =>
                {
                    progress.Visible = false; ControlBox = true;
                    if (state == UpdaterState.Working) state = UpdaterState.Confirm;
                    if (err != null) { ShowFailed("うまくいきませんでした", err.Message, true); return; }
                    lastResult = r;
                    done(r);
                });
            }) { IsBackground = true, Name = "update" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        void Ui_(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
            else a();
        }

        void ShowDone(UpdateResult res, string title, string sub)
        {
            state = UpdaterState.Done;
            SetHeader(sub);
            card.Title = title; card.Invalidate();
            if (plan != null && res.Changes.Count > 0) SetRows(ChangeRows(plan, true)); else ClearRows();
            info.Text = res.BackupDir != null ? "置き換える前のファイル: " + res.BackupDir : "";
            SetNotice("✓ " + title, Theme.Success);
            SetButtons(null, null, false, "閉じる");
            undo.Text = backup == null && res.BackupDir != null ? "元に戻す (" + res.From + " に戻す)" : "";
            undo.Visible = undo.Text.Length > 0;
        }

        void ShowFailed(string title, string message, bool serious)
        {
            state = UpdaterState.Failed;
            SetHeader(title);
            card.Title = title; card.Invalidate();
            ClearRows();
            info.Text = "";
            notice.Height = Theme.S(150);
            SetNotice(message, serious ? Theme.Danger : Theme.Text);
            SetButtons(null, null, false, "閉じる");
            undo.Visible = false;
        }

        // ---------------------------------------------------------------- 前のバージョンに戻す

        public void ShowRollback()
        {
            info.Dock = DockStyle.Bottom; info.Height = Theme.S(76); info.ForeColor = Theme.SubText;
            notice.Height = Theme.S(48);
            undo.Visible = false;
            backups = engine.ListBackups();
            var inst = SafeInstalled();
            var cur = inst != null ? inst.SuiteVersion : "";
            state = UpdaterState.Confirm;
            card.Title = "前のバージョンに戻す"; card.Invalidate();
            if (backups.Count == 0)
            {
                SetHeader("戻せるバージョンはありません");
                ClearRows();
                info.Text = "アップデートすると、置き換える前のファイルが backup フォルダに残り、ここから戻せるようになります。";
                SetNotice(null, Theme.SubText);
                SetButtons(null, null, false, "閉じる");
                return;
            }
            backup = backups[0];
            SetHeader("TealSuite " + cur + " → " + backup.From);
            SetRows(backups.Select((b, i) => new ChangeRow(
                (i == 0 ? "● " : "　 ") + b.From + " に戻す",
                b.Date,
                b.From + " → " + b.To + " のアップデートで残したもの ・ " + (b.Replaced.Count + b.Deleted.Count) + " 個のファイル" + (b.Created.Count > 0 ? "、" + b.Created.Count + " 個は削除" : ""),
                i == 0 ? Theme.Text : Theme.SubText, false)));
            info.Text = "いちばん新しいアップデートの前 (" + backup.From + ") に戻します。起動中のアプリはいったん終了し、終わったら起動し直します。\nデータ (%APPDATA%\\TealSuite) は変わりません。";
            if (VersionUtil.Compare(backup.To, cur) != 0)
                SetNotice("⚠ 今のバージョン (" + cur + ") は、この backup を作ったあとのバージョン (" + backup.To + ") と違います。戻すと一部のアプリだけ古くなることがあります。", Theme.Warning);
            else SetNotice(null, Theme.SubText);
            SetButtons(backup.From + " に戻す", StartRollback, true, "キャンセル");
        }

        void StartRollback()
        {
            var b = backup;
            var inst = InstalledSuite.Load(engine.Dir);
            RunWork("前のバージョンに戻しています…", () =>
            {
                List<RunningApp> stop;
                try { stop = FindRunning(inst) ?? new List<RunningApp>(); } catch { stop = new List<RunningApp>(); }
                if (!StopAppsForRollback(stop)) return null;
                engine.Progress = (p, w) => Ui_(() => { progress.Value = Math.Max(0, Math.Min(100, p)); SetNotice(w, Theme.SubText); });
                var res = engine.Restore(b);
                try { RestartApps(InstalledSuite.Load(engine.Dir), stop.Select(r => r.App.Id), true); } catch { }
                return res;
            }, res =>
            {
                if (res == null) return;
                plan = null;
                if (res.Ok) ShowDone(res, "前のバージョンに戻しました", "TealSuite " + res.To + " に戻りました。");
                else ShowFailed("戻せませんでした", res.Error + "\n\nbackup フォルダ (" + b.Dir + ") から手で戻せます。", true);
            });
        }

        bool StopAppsForRollback(List<RunningApp> stop)
        {
            if (stop.Count == 0) return true;
            var left = CloseApps(stop, CloseTimeoutMs);
            if (left.Count == 0) return true;
            bool kill = false;
            Invoke((Action)(() => kill = AskKill(this, string.Join("・", left.Select(r => r.Name)) + " が終了しません。\n\n強制終了して続けますか？ (保存していない内容は失われます)")));
            if (kill && Kill(left).Count == 0) return true;
            Ui_(() => { ShowRollback(); SetNotice("アプリが終了しなかったため、取りやめました。", Theme.Danger); });
            return false;
        }
    }
}
