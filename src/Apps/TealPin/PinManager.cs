// Teal Pin (最前面に固定) - 固定の管理 (どのウィンドウを固定しているか・枠線・自動で固定するルール)
//  Win32 は IWindowApi の向こう側 (本物は Win32Windows.cs、テストと見本は FakeWindows)。ここは mono でもテストできる
//  - 固定: SetWindowPos(HWND_TOPMOST)。解除: HWND_NOTOPMOST。終了するときはすべて解除する
//  - もとから最前面のウィンドウ (アプリ自身の設定) には手を出さない
//  - 管理者として実行中のアプリのウィンドウは (Teal Pin が管理者でなければ) Windows が変更を許さないので、理由を出してとばす
//  - 落ちたとき用に、固定中のウィンドウを pinned.json に書いておき、次に起動したときに解除する
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Pin
{
    public interface IWindowApi
    {
        bool IsWindow(IntPtr h);
        bool IsVisible(IntPtr h);
        bool IsMinimized(IntPtr h);
        bool IsMaximized(IntPtr h);
        /// <summary>ほかの仮想デスクトップにある・UWP の隠れたウィンドウなど (見えない)</summary>
        bool IsCloaked(IntPtr h);
        bool IsTopmost(IntPtr h);
        /// <summary>ふつうのアプリのウィンドウか (見えている・持ち主なし・ツール ウィンドウでない・タイトルバーあり)</summary>
        bool IsAppWindow(IntPtr h);
        bool SetTopmost(IntPtr h, bool on);
        /// <summary>最後に失敗した Win32 のエラー番号 (5 = アクセス拒否)</summary>
        int LastError { get; }
        string Title(IntPtr h);
        string ClassName(IntPtr h);
        int ProcessId(IntPtr h);
        string ExePath(int pid);
        /// <summary>exe の説明 (ファイルの情報の「ファイルの説明」。なければ exe 名)</summary>
        string AppName(string exePath);
        bool IsElevated(int pid);
        bool SelfElevated { get; }
        /// <summary>見えている枠 (DWM の範囲。物理 px)</summary>
        Rectangle FrameBounds(IntPtr h);
        Rectangle WindowRect(IntPtr h);
        /// <summary>ウィンドウがあるモニターの作業領域 (タスクバーを除く)</summary>
        Rectangle WorkArea(IntPtr h);
        /// <summary>ウィンドウがあるモニターの拡大率 (96 dpi = 1.0)</summary>
        double Scale(IntPtr h);
        bool Restore(IntPtr h);
        bool Move(IntPtr h, Rectangle windowRect);
        bool Activate(IntPtr h);
        IntPtr Foreground();
        /// <summary>いちばん外側のウィンドウ (GA_ROOT)</summary>
        IntPtr Root(IntPtr h);
        List<IntPtr> TopLevelWindows();
    }

    /// <summary>ティールの枠線 (本物は BorderOverlay。テストでは記録するだけ)</summary>
    public interface IBorder : IDisposable
    {
        /// <summary>4 本の線を置く (target のすぐ上に重ねる)</summary>
        void Place(Rectangle[] strips, IntPtr target);
        void Hide();
        bool Visible { get; }
        /// <summary>target のすぐ上にあるか (ほかのウィンドウが間に入ったら false)</summary>
        bool OrderOk(IntPtr target);
    }

    public enum WinEvt { Location, MinimizeStart, MinimizeEnd, Destroy, Show, Hide, Foreground, NameChange, Cloaked, Uncloaked }

    public class PinnedWindow
    {
        public IntPtr Handle;
        public int Pid;
        public string ExePath, ExeName, AppName, Title, How;
        public DateTime Since;
        public bool Minimized, Hidden, Cloaked;
        public IBorder Border;
        public Rectangle[] LastStrips = new Rectangle[0];
        public override string ToString() { return Title; }
        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                parts.Add(AppName);
                if (!string.IsNullOrEmpty(ExeName) && !string.Equals(ExeName, AppName, StringComparison.OrdinalIgnoreCase)) parts.Add(ExeName);
                if (!string.IsNullOrEmpty(How)) parts.Add(How);
                if (Minimized) parts.Add("最小化中");
                else if (Hidden || Cloaked) parts.Add("隠れています");
                return string.Join(" ・ ", parts.Where(p => !string.IsNullOrEmpty(p)));
            }
        }
    }

    public class PinResult
    {
        public bool Ok;
        /// <summary>この操作のあと固定されているか</summary>
        public bool Pinned;
        public string Message;
        public static PinResult Fail(string m) { return new PinResult { Ok = false, Message = m }; }
    }

    [DataContract]
    public class PinRecord
    {
        [DataMember(Name = "hwnd", Order = 1)] public long Hwnd;
        [DataMember(Name = "pid", Order = 2)] public int Pid;
        [DataMember(Name = "exe", Order = 3)] public string Exe;
    }

    [DataContract]
    public class PinRecordFile
    {
        [DataMember(Name = "pinned", Order = 1)] public List<PinRecord> Pinned = new List<PinRecord>();
    }

    public class PinManager : IDisposable
    {
        public const string HowHotkey = "ショートカット";
        public const string HowTitleBar = "タイトルバー";
        public const string HowList = "一覧から";
        public const string HowTray = "通知領域から";
        public const string MsgElevated = "管理者として実行中のアプリのウィンドウは固定できません (Windows が、ふつうの権限のアプリからの変更を許さないため)。";
        public const string MsgSelf = "Teal Pin 自身の画面は固定できません。";
        public const string MsgAlreadyTop = "このウィンドウは、アプリ自身の設定で、もともと最前面に表示されています。";
        public const string MsgShell = "デスクトップやタスクバーは固定できません。";
        public const string MsgGone = "ウィンドウが見つかりません (閉じられた可能性があります)。";
        public const string MsgNoWindow = "固定できるウィンドウが前面にありません。";

        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "#32768", "tooltips_class32", "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        };

        readonly IWindowApi api;
        readonly Func<IBorder> borderFactory;
        readonly int selfPid;
        PinSettings settings;
        readonly List<PinnedWindow> pins = new List<PinnedWindow>();
        /// <summary>ルールで固定済み・自分で外したウィンドウ (同じウィンドウを何度も自動で固定しない)</summary>
        readonly HashSet<IntPtr> handled = new HashSet<IntPtr>();
        readonly Dictionary<int, string> exeCache = new Dictionary<int, string>();
        readonly Dictionary<IntPtr, DateTime> pending = new Dictionary<IntPtr, DateTime>();
        DateTime lastVerify = DateTime.MinValue, lastSweep = DateTime.MinValue;
        bool disposed;

        /// <summary>固定中のウィンドウが増えた・減った・タイトルが変わった</summary>
        public event Action Changed;
        /// <summary>お知らせ (文, 対象のウィンドウ, 失敗か)</summary>
        public Action<string, IntPtr, bool> Notice;
        /// <summary>固定中のウィンドウが変わったら、落ちたとき用の記録を書く</summary>
        public Action<PinRecordFile> SaveRecords;
        /// <summary>ルールで固定するまで待つ時間 (ウィンドウが自分で位置を決め終わるのを待つ)</summary>
        public int RuleDelayMs = 400;
        /// <summary>最後に前面だった (固定できる) ウィンドウ。通知領域のメニューから固定するとき用</summary>
        public IntPtr LastForeground { get; private set; }

        public PinManager(IWindowApi api, Func<IBorder> borderFactory, PinSettings settings, int selfPid)
        {
            this.api = api;
            this.borderFactory = borderFactory;
            this.settings = settings ?? new PinSettings();
            this.selfPid = selfPid;
        }

        public IWindowApi Api { get { return api; } }
        public PinSettings Settings
        {
            get { return settings; }
            set { settings = value ?? new PinSettings(); RefreshBorders(); }
        }
        public IList<PinnedWindow> Pins { get { return pins.AsReadOnly(); } }
        public int Count { get { return pins.Count; } }
        public bool IsPinned(IntPtr h) { return Find(h) != null; }
        public PinnedWindow Find(IntPtr h) { return h == IntPtr.Zero ? null : pins.FirstOrDefault(p => p.Handle == h); }

        void Raise() { if (Changed != null) try { Changed(); } catch (Exception ex) { DataStore.Log("pin changed: " + ex.Message); } }
        void Tell(string msg, IntPtr h, bool error) { if (Notice != null) try { Notice(msg, h, error); } catch { } }

        string ExeOf(int pid)
        {
            string e;
            if (exeCache.TryGetValue(pid, out e)) return e;
            try { e = api.ExePath(pid); } catch { e = null; }
            if (exeCache.Count > 500) exeCache.Clear();
            exeCache[pid] = e;
            return e;
        }

        /// <summary>固定できないときの理由 (できるなら null)</summary>
        public string Check(IntPtr h)
        {
            if (h == IntPtr.Zero) return MsgNoWindow;
            if (!api.IsWindow(h)) return MsgGone;
            if (IsPinned(h)) return null;
            int pid = api.ProcessId(h);
            if (pid == selfPid) return MsgSelf;
            if (ShellClasses.Contains(api.ClassName(h) ?? "")) return MsgShell;
            if (!api.IsVisible(h)) return MsgNoWindow;
            if (!api.SelfElevated && api.IsElevated(pid)) return MsgElevated;
            if (api.IsTopmost(h)) return MsgAlreadyTop;
            return null;
        }

        /// <summary>前面のウィンドウ (子ウィンドウなら外側) を固定 / 解除</summary>
        public PinResult ToggleForeground(string how)
        {
            var h = api.Foreground();
            if (h != IntPtr.Zero) h = api.Root(h);
            return Toggle(h, how);
        }

        public PinResult Toggle(IntPtr h, string how)
        {
            if (h != IntPtr.Zero && IsPinned(h)) return Unpin(h);
            return Pin(h, how, null);
        }

        public PinResult Pin(IntPtr h, string how, PinPreset preset)
        {
            if (disposed) return PinResult.Fail("終了中です");
            var why = Check(h);
            if (why != null) { Tell(why, h, true); return PinResult.Fail(why); }
            var existing = Find(h);
            if (existing != null)
            {
                if (preset != null) ApplyPreset(h, preset);
                return new PinResult { Ok = true, Pinned = true, Message = "すでに固定しています" };
            }
            if (!api.SetTopmost(h, true))
            {
                int err = api.LastError;
                var msg = err == 5 ? MsgElevated : "固定できませんでした (Windows のエラー " + err + ")。";
                Tell(msg, h, true);
                return PinResult.Fail(msg);
            }
            int pid = api.ProcessId(h);
            var exe = ExeOf(pid);
            var pw = new PinnedWindow
            {
                Handle = h, Pid = pid, ExePath = exe,
                ExeName = string.IsNullOrEmpty(exe) ? "" : RuleMatcher.NormalizeExe(exe),
                AppName = SafeAppName(exe),
                Title = TitleOf(h), How = how, Since = DateTime.Now,
                Minimized = api.IsMinimized(h), Cloaked = api.IsCloaked(h),
            };
            try { pw.Border = borderFactory != null ? borderFactory() : null; } catch (Exception ex) { DataStore.Log("border: " + ex.Message); }
            pins.Add(pw);
            handled.Add(h);
            pending.Remove(h);
            if (preset != null) ApplyPreset(h, preset, false);
            UpdateBorder(pw, true);
            Record();
            Raise();
            var text = "📌 最前面に固定しました";
            Tell(text, h, false);
            return new PinResult { Ok = true, Pinned = true, Message = text + ": " + pw.Title };
        }

        string SafeAppName(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "(アプリ名がわかりません)";
            try { var n = api.AppName(exe); if (!string.IsNullOrWhiteSpace(n)) return n.Trim(); } catch { }
            return System.IO.Path.GetFileNameWithoutExtension(exe);
        }

        string TitleOf(IntPtr h)
        {
            var t = "";
            try { t = api.Title(h) ?? ""; } catch { }
            return t.Trim().Length == 0 ? "(タイトルなし)" : t.Trim();
        }

        public PinResult Unpin(IntPtr h) { return Unpin(h, true); }

        PinResult Unpin(IntPtr h, bool tell)
        {
            var pw = Find(h);
            if (pw == null) return PinResult.Fail("固定していないウィンドウです");
            bool ok = true;
            if (api.IsWindow(h)) ok = api.SetTopmost(h, false);
            DisposeBorder(pw);
            pins.Remove(pw);
            Record();
            Raise();
            if (!ok) { var m = "固定を外せませんでした (Windows のエラー " + api.LastError + ")。"; Tell(m, h, true); return new PinResult { Ok = false, Message = m }; }
            if (tell) Tell("固定を外しました", h, false);
            return new PinResult { Ok = true, Pinned = false, Message = "固定を外しました: " + pw.Title };
        }

        /// <summary>すべて解除 (終了するときも)。外した数</summary>
        public int UnpinAll()
        {
            int n = 0;
            foreach (var pw in pins.ToArray())
            {
                try { if (api.IsWindow(pw.Handle)) api.SetTopmost(pw.Handle, false); } catch { }
                DisposeBorder(pw);
                n++;
            }
            pins.Clear();
            Record();
            if (n > 0) Raise();
            return n;
        }

        void DisposeBorder(PinnedWindow pw)
        {
            if (pw.Border == null) return;
            try { pw.Border.Dispose(); } catch { }
            pw.Border = null;
        }

        public bool Activate(IntPtr h)
        {
            if (!api.IsWindow(h)) return false;
            if (api.IsMinimized(h)) api.Restore(h);
            return api.Activate(h);
        }

        // ---------------------------------------------------------------- 位置とサイズ
        public PinResult ApplyPreset(IntPtr h, PinPreset p) { return ApplyPreset(h, p, true); }

        PinResult ApplyPreset(IntPtr h, PinPreset p, bool tell)
        {
            if (p == null) return PinResult.Fail("プリセットがありません");
            if (!api.IsWindow(h)) return PinResult.Fail(MsgGone);
            if (!api.SelfElevated && api.IsElevated(api.ProcessId(h))) { Tell(MsgElevated, h, true); return PinResult.Fail(MsgElevated); }
            if (api.IsMaximized(h) || api.IsMinimized(h)) api.Restore(h);
            var work = api.WorkArea(h);
            var desired = PresetMath.Compute(p, work, api.Scale(h));
            if (desired.IsEmpty) return PinResult.Fail("画面の大きさがわかりませんでした");
            var target = PresetMath.ToWindowRect(desired, api.WindowRect(h), api.FrameBounds(h));
            if (!api.Move(h, target))
            {
                var m = api.LastError == 5 ? MsgElevated : "位置を変えられませんでした (Windows のエラー " + api.LastError + ")。";
                Tell(m, h, true);
                return PinResult.Fail(m);
            }
            var pw = Find(h);
            if (pw != null) { pw.Minimized = false; UpdateBorder(pw, true); }
            if (tell) Tell("「" + p.Name + "」にしました", h, false);
            return new PinResult { Ok = true, Pinned = pw != null, Message = "「" + p.Name + "」にしました" };
        }

        /// <summary>このウィンドウの今の位置からプリセットを作る (保存はしない)</summary>
        public PinPreset CapturePreset(IntPtr h, string name)
        {
            if (!api.IsWindow(h)) return null;
            return PresetMath.FromRect(name, api.FrameBounds(h), api.WorkArea(h));
        }

        // ---------------------------------------------------------------- 枠線
        public void RefreshBorders() { foreach (var pw in pins) UpdateBorder(pw, true); }

        void UpdateBorder(PinnedWindow pw, bool force)
        {
            if (pw.Border == null) return;
            var h = pw.Handle;
            bool show = settings.ShowBorder && !pw.Minimized && !pw.Hidden && !pw.Cloaked && api.IsWindow(h) && api.IsVisible(h) && !api.IsMinimized(h);
            if (!show) { if (pw.Border.Visible) pw.Border.Hide(); pw.LastStrips = new Rectangle[0]; return; }
            var frame = api.FrameBounds(h);
            var strips = BorderMath.Strips(frame, api.WorkArea(h), BorderMath.Thickness(settings.BorderWidth, api.Scale(h)));
            if (strips.Length == 0) { if (pw.Border.Visible) pw.Border.Hide(); pw.LastStrips = strips; return; }
            if (!force && pw.Border.Visible && strips.SequenceEqual(pw.LastStrips) && pw.Border.OrderOk(h)) return;
            pw.Border.Place(strips, h);
            pw.LastStrips = strips;
        }

        // ---------------------------------------------------------------- Windows のできごと (SetWinEventHook)
        public void OnEvent(WinEvt e, IntPtr h, DateTime now)
        {
            if (disposed || h == IntPtr.Zero) return;
            var pw = Find(h);
            switch (e)
            {
                case WinEvt.Location:
                    if (pw != null) UpdateBorder(pw, false);
                    break;
                case WinEvt.MinimizeStart:
                    if (pw != null) { pw.Minimized = true; UpdateBorder(pw, true); Raise(); }
                    break;
                case WinEvt.MinimizeEnd:
                    if (pw != null) { pw.Minimized = false; UpdateBorder(pw, true); Raise(); }
                    break;
                case WinEvt.Destroy:
                    handled.Remove(h); pending.Remove(h);
                    if (pw != null) { DisposeBorder(pw); pins.Remove(pw); Record(); Raise(); }
                    break;
                case WinEvt.Hide:
                    if (pw != null && !api.IsVisible(h)) { pw.Hidden = true; UpdateBorder(pw, true); }
                    break;
                case WinEvt.Show:
                    if (pw != null) { pw.Hidden = false; UpdateBorder(pw, true); }
                    else QueueRule(h, now);
                    break;
                case WinEvt.Cloaked:
                case WinEvt.Uncloaked:
                    if (pw != null) { pw.Cloaked = e == WinEvt.Cloaked; UpdateBorder(pw, true); }
                    else if (e == WinEvt.Uncloaked) QueueRule(h, now);
                    break;
                case WinEvt.Foreground:
                    if (pw != null || Check(h) == null) { if (api.ProcessId(h) != selfPid) LastForeground = h; }
                    foreach (var p in pins) UpdateBorder(p, false);   // 前面に来たウィンドウの下に枠線が隠れないように
                    if (pw == null) QueueRule(h, now);
                    break;
                case WinEvt.NameChange:
                    if (pw != null)
                    {
                        var t = TitleOf(h);
                        if (t != pw.Title) { pw.Title = t; Raise(); }
                    }
                    else if (settings.Rules.Any(r => r.Enabled && !string.IsNullOrEmpty(r.TitleContains))) QueueRule(h, now);
                    break;
            }
        }

        void QueueRule(IntPtr h, DateTime now)
        {
            if (!settings.Rules.Any(r => r.Enabled) || handled.Contains(h) || IsPinned(h)) return;
            if (!pending.ContainsKey(h)) pending[h] = now.AddMilliseconds(RuleDelayMs);
        }

        /// <summary>ルールに合えば固定する (合って固定したら true)</summary>
        public bool TryRule(IntPtr h)
        {
            if (h == IntPtr.Zero || handled.Contains(h) || IsPinned(h) || !api.IsWindow(h)) return false;
            if (!api.IsAppWindow(h) || api.IsCloaked(h)) return false;
            int pid = api.ProcessId(h);
            if (pid == selfPid) return false;
            var exe = ExeOf(pid);
            if (string.IsNullOrEmpty(exe)) return false;
            var rule = RuleMatcher.Find(settings.Rules, exe, api.Title(h));
            if (rule == null) return false;
            handled.Add(h);
            if (Check(h) != null) return false;   // 管理者のウィンドウ・もとから最前面などは、だまってとばす
            var r = Pin(h, "自動 (" + rule.Describe() + ")", settings.FindPreset(rule.PresetId));
            return r.Ok;
        }

        /// <summary>今開いているウィンドウにルールを使う (起動したとき・ルールを変えたとき)。固定した数</summary>
        public int ScanRules()
        {
            if (!settings.Rules.Any(r => r.Enabled)) return 0;
            int n = 0;
            foreach (var h in api.TopLevelWindows()) if (TryRule(h)) n++;
            return n;
        }

        /// <summary>自分で外したウィンドウをルールでまた固定しないように (Unpin の後に呼ぶ必要はない: handled に残る)</summary>
        public void ForgetRuleHistory() { handled.RemoveWhere(h => !IsPinned(h)); }

        /// <summary>定期的に (250ms ごと): ルールの順番待ち・固定の確認・枠線の位置</summary>
        public void Tick(DateTime now)
        {
            if (disposed) return;
            foreach (var kv in pending.Where(kv => kv.Value <= now).ToList())
            {
                pending.Remove(kv.Key);
                try { TryRule(kv.Key); } catch (Exception ex) { DataStore.Log("rule: " + ex.Message); }
            }
            if ((now - lastVerify).TotalMilliseconds < 1000 && now >= lastVerify) return;
            lastVerify = now;
            bool changed = false;
            foreach (var pw in pins.ToArray())
            {
                var h = pw.Handle;
                if (!api.IsWindow(h)) { DisposeBorder(pw); pins.Remove(pw); changed = true; continue; }
                if (settings.KeepTopmost && !api.IsTopmost(h)) api.SetTopmost(h, true);
                bool min = api.IsMinimized(h);
                if (min != pw.Minimized) { pw.Minimized = min; changed = true; }
                bool hidden = !api.IsVisible(h);
                if (hidden != pw.Hidden) { pw.Hidden = hidden; changed = true; }
                bool cloaked = api.IsCloaked(h);
                if (cloaked != pw.Cloaked) pw.Cloaked = cloaked;
                var t = TitleOf(h);
                if (t != pw.Title) { pw.Title = t; changed = true; }
                UpdateBorder(pw, false);
            }
            if ((now - lastSweep).TotalSeconds > 30 || now < lastSweep)
            {
                lastSweep = now;
                handled.RemoveWhere(h => !api.IsWindow(h));
                exeCache.Clear();
            }
            if (changed) { Record(); Raise(); }
        }

        /// <summary>固定できるウィンドウ (一覧の「ほかのウィンドウを固定」用。固定中のものも含む)</summary>
        public List<IntPtr> Candidates()
        {
            return api.TopLevelWindows().Where(h => api.IsAppWindow(h) && !api.IsCloaked(h) && api.ProcessId(h) != selfPid && (IsPinned(h) || Check(h) == null)).ToList();
        }

        // ---------------------------------------------------------------- 落ちたとき用の記録
        public PinRecordFile Records()
        {
            var f = new PinRecordFile();
            foreach (var p in pins) f.Pinned.Add(new PinRecord { Hwnd = p.Handle.ToInt64(), Pid = p.Pid, Exe = p.ExePath });
            return f;
        }

        void Record()
        {
            if (SaveRecords == null) return;
            try { SaveRecords(Records()); } catch (Exception ex) { DataStore.Log("pin records: " + ex.Message); }
        }

        /// <summary>
        /// 前回 Teal Pin が落ちて残った固定を外す。同じウィンドウ (ハンドル・プロセス・exe が同じ) で、まだ最前面のものだけ。外した数
        /// </summary>
        public static int Recover(IWindowApi api, PinRecordFile f)
        {
            if (f == null || f.Pinned == null) return 0;
            int n = 0;
            foreach (var r in f.Pinned)
            {
                if (r == null || r.Hwnd == 0) continue;
                var h = new IntPtr(r.Hwnd);
                try
                {
                    if (!api.IsWindow(h) || api.ProcessId(h) != r.Pid) continue;
                    var exe = api.ExePath(r.Pid);
                    if (!string.Equals(RuleMatcher.NormalizeExe(exe), RuleMatcher.NormalizeExe(r.Exe), StringComparison.OrdinalIgnoreCase)) continue;
                    if (api.IsTopmost(h) && api.SetTopmost(h, false)) n++;
                }
                catch { }
            }
            return n;
        }

        public void Dispose()
        {
            if (disposed) return;
            UnpinAll();
            disposed = true;
        }
    }
}
