// Teal Macro - 動きの中心 (画面に関係しない)。連打・マクロの再生・記録・位置の記録と、その状態
//  画面 (MainForm)・通知領域 (MacroTray)・実行中の小さな表示 (RunBar) は、これの状態を見て表示を変える
//  状態: 待機中 → (ボタンで始めたときだけ) カウントダウン → 実行中 (連打 / マクロ) → 待機中
//                待機中 → 記録中 → 待機中 (新しいマクロができる)
//  どの状態でも Stop() (「停止」ボタン・すべて停止のショートカット・実行中の Esc) で待機中に戻る
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public enum CoreState { Idle, Countdown, Clicking, Playing, Recording }

    /// <summary>記録のフック (本物は InputHook、テストは FakeHook)</summary>
    public interface IHookSource : IDisposable
    {
        bool Start();
        void BeginRecording();
        List<RawEvent> EndRecording();
        int RecordedCount { get; }
        bool Running { get; }
        HotkeySpec StopKey { get; set; }
        Func<Point, bool> IsOwn { get; set; }
    }

    /// <summary>テスト・デモ用のフック: Pending に入れたものを記録したことにする</summary>
    public class FakeHook : IHookSource
    {
        public List<RawEvent> Pending = new List<RawEvent>();
        public bool Started, Recording;
        public bool Start() { Started = true; return true; }
        public void BeginRecording() { Recording = true; }
        public List<RawEvent> EndRecording() { Recording = false; var l = Pending; Pending = new List<RawEvent>(); return l; }
        public int RecordedCount { get { return Pending.Count; } }
        public bool Running { get { return Started; } }
        public HotkeySpec StopKey { get; set; }
        public Func<Point, bool> IsOwn { get; set; }
        public void Dispose() { Started = false; }
    }

    public class MacroCore : IDisposable
    {
        // ---- ショートカットの名前と初期設定 (Teal Hub が受け付ける。apps.json と同じ)
        public const string SlotPick = "pick", SlotRecord = "record", SlotStop = "stop";
        public static readonly HotkeySpec DefaultToggle = HotkeySpec.Of(HotMods.Ctrl | HotMods.Alt, Keys.R);
        public static readonly HotkeySpec DefaultPick = HotkeySpec.Of(HotMods.Ctrl | HotMods.Alt, Keys.P);
        public static readonly HotkeySpec DefaultRecord = HotkeySpec.Of(HotMods.Ctrl | HotMods.Alt, Keys.M);
        public static readonly HotkeySpec DefaultStop = HotkeySpec.Of(HotMods.Ctrl | HotMods.Alt, Keys.Escape);

        public readonly MacroSettings Settings;
        public readonly MacroFile Macros;
        public readonly IInputApi Api;
        readonly IHookSource hook;
        public IHookSource Hook { get { return hook; } }
        readonly Timer ticker = new Timer { Interval = 100 };

        public CoreState State { get; private set; }
        /// <summary>カウントダウンのあと何をするか ("click" / マクロの ID)</summary>
        string pending;
        DateTime countdownEnd;
        public RunJob Job { get; private set; }
        public ClickJob ClickJob { get { return Job as ClickJob; } }
        public MacroJob MacroJob { get { return Job as MacroJob; } }
        DateTime recordStart;
        /// <summary>前回の結果 (「120 回クリックしました」など)</summary>
        public string LastResult { get; private set; }

        // ---- ショートカット (表示と、記録から取り除くため)
        public HotkeySpec ToggleKey { get; private set; }
        public HotkeySpec PickKey { get; private set; }
        public HotkeySpec RecordKey { get; private set; }
        public HotkeySpec StopKey { get; private set; }

        /// <summary>状態・進み具合が変わった (UI スレッド)</summary>
        public event Action Changed;
        /// <summary>マクロの一覧・中身が変わった</summary>
        public event Action MacrosChanged;
        /// <summary>連打の設定 (位置の一覧など) が変わった</summary>
        public event Action ClickerChanged;
        /// <summary>お知らせ (メッセージ, エラーなら true)</summary>
        public Action<string, bool> Notice;
        /// <summary>マクロの記録・再生の間、画面を隠す / 戻す (設定がオンのとき)</summary>
        public Action<bool> HideMain;
        /// <summary>UI スレッドで実行する (テストではそのまま実行)</summary>
        public Action<Action> Post;
        /// <summary>テスト用: true なら別のスレッドを使わず、その場で最後まで動かす (FakeClock)</summary>
        public bool Synchronous;
        public FakeClock TestClock;
        /// <summary>位置を画面から選ぶ (本物は全画面の選択。テストでは差し替え)。結果は選んだ位置 (取り消しなら null)</summary>
        public Action<Action<TargetPoint>> PickOnScreen;

        public MacroCore(MacroSettings settings, MacroFile macros, IInputApi api, IHookSource hook)
        {
            Settings = settings; Macros = macros; Api = api; this.hook = hook;
            Post = a => a();
            ReloadHotkeys();
            ticker.Tick += (s, e) => OnTick();
            if (hook != null)
            {
                hook.IsOwn = p => { try { return Api.IsOwnWindowAt(p); } catch { return false; } };
            }
        }

        // ================================================================ ショートカット
        public void ReloadHotkeys()
        {
            ToggleKey = HotkeySpec.FromSaved(AppPrefs.HotkeyOf(AppInfo.Id), DefaultToggle);
            PickKey = HotkeySlots.HotkeyOf(AppInfo.Id, SlotPick, DefaultPick);
            RecordKey = HotkeySlots.HotkeyOf(AppInfo.Id, SlotRecord, DefaultRecord);
            StopKey = HotkeySlots.HotkeyOf(AppInfo.Id, SlotStop, DefaultStop);
            if (hook != null) hook.StopKey = StopKey;
            Raise();
        }

        public HotkeySpec MacroKey(MacroDef m) { return m == null ? HotkeySpec.None : HotkeySlots.HotkeyOf(AppInfo.Id, m.Slot, HotkeySpec.None); }

        /// <summary>Teal Macro のショートカットすべて (記録から取り除く)</summary>
        public List<HotkeySpec> AllHotkeys()
        {
            var l = new List<HotkeySpec> { ToggleKey, PickKey, RecordKey, StopKey };
            l.AddRange(Macros.Macros.Select(MacroKey));
            return l.Where(h => !h.IsEmpty).ToList();
        }

        /// <summary>Teal Hub のショートカットが押された (slot: null = 1 つめ「連打」)</summary>
        public void OnHotkey(string slot)
        {
            if (slot == null) { ToggleClicker(false); return; }
            if (slot == SlotStop) { StopAll(RunJob.ReasonHotkey); return; }
            if (slot == SlotPick) { AddPointFromCursor(); return; }
            if (slot == SlotRecord) { ToggleRecording(); return; }
            if (slot.StartsWith("m."))
            {
                var m = Macros.Macros.FirstOrDefault(x => x.Slot == slot);
                if (m == null) { Say("このショートカットのマクロは見つかりません (削除されました)", true); return; }
                var mj = MacroJob;
                if (State == CoreState.Playing && mj != null && mj.Macro.Id == m.Id) { StopAll(RunJob.ReasonHotkey); return; }
                if (State == CoreState.Recording) return;
                PlayMacro(m.Id, false);
            }
        }

        // ================================================================ 連打
        public void ToggleClicker(bool byButton)
        {
            if (State == CoreState.Clicking || (State == CoreState.Countdown && pending == "click")) { StopAll(RunJob.ReasonUser); return; }
            StartClicker(byButton);
        }

        /// <summary>連打を始める。ボタンからなら設定の秒数だけ待ってから (マウスを動かす時間)</summary>
        public bool StartClicker(bool byButton)
        {
            if (State == CoreState.Recording) { Say("記録中は連打を始められません", true); return false; }
            if (State != CoreState.Idle) StopAll(RunJob.ReasonUser);
            var c = Settings.Clicker;
            var why = CheckClicker(c);
            if (why != null) { Say(why, true); return false; }
            // キーの連打を「▶ 開始」で始めたときは、この画面が前にあるとキーがここに来てしまうので最小化する (前に使っていたウィンドウに戻る)
            if (byButton && c.IsKey && HideMain != null && !Synchronous) HideMain(true);
            if (byButton && c.StartDelaySec > 0 && !Synchronous) { BeginCountdown("click", c.StartDelaySec); return true; }
            RunClicker();
            return true;
        }

        /// <summary>始められない理由 (なければ null)</summary>
        public static string CheckClicker(ClickerSettings c)
        {
            HotkeySpec k;
            if (c.IsKey && !KeyText.TryParse(c.Key, out k)) return "連打するキーを決めてください";
            if (c.UsesPoints && c.Points.Count == 0) return "クリックする位置がまだありません。「＋ 画面から選ぶ」か、位置を記録するショートカットで追加してください";
            return null;
        }

        void RunClicker()
        {
            var c = DataStore.FromJson<ClickerSettings>(DataStore.ToJson(Settings.Clicker));   // 実行中に設定を変えても影響しないように
            c.Normalize();
            var job = new ClickJob(c, Api);
            Begin(job, CoreState.Clicking);
        }

        // ================================================================ マクロの再生
        public bool PlayMacro(string id, bool byButton)
        {
            var m = Macros.Find(id);
            if (m == null) { Say("マクロを選んでください", true); return false; }
            if (m.Steps.Count == 0) { Say("「" + m.Name + "」には手順がありません。「● 記録」するか、「＋ 手順」で追加してください", true); return false; }
            if (State == CoreState.Recording) { Say("記録中は再生できません", true); return false; }
            if (State != CoreState.Idle) StopAll(RunJob.ReasonUser);
            if (Settings.HideWhileRunning && HideMain != null && !Synchronous) HideMain(true);
            if (byButton && Settings.MacroStartDelaySec > 0 && !Synchronous) { BeginCountdown(m.Id, Settings.MacroStartDelaySec); return true; }
            RunMacro(m);
            return true;
        }

        void RunMacro(MacroDef m)
        {
            var job = new MacroJob(m.Clone(), Api);
            Begin(job, CoreState.Playing);
        }

        // ================================================================ 共通: 始める・止める
        void BeginCountdown(string what, int sec)
        {
            pending = what;
            countdownEnd = DateTime.Now.AddSeconds(sec);
            State = CoreState.Countdown;
            WatchKeys(true);
            ticker.Start();
            Raise();
        }

        public int CountdownLeft { get { return State == CoreState.Countdown ? Math.Max(1, (int)Math.Ceiling((countdownEnd - DateTime.Now).TotalSeconds)) : 0; } }
        public bool CountdownIsClick { get { return State == CoreState.Countdown && pending == "click"; } }

        /// <summary>テスト用: カウントダウンを今すぐ終わらせる</summary>
        public void FinishCountdown() { if (State == CoreState.Countdown) { countdownEnd = DateTime.Now; OnTick(); } }

        void OnTick()
        {
            if (State == CoreState.Countdown && DateTime.Now >= countdownEnd)
            {
                var what = pending; pending = null;
                State = CoreState.Idle;
                if (what == "click") RunClicker();
                else { var m = Macros.Find(what); if (m != null) RunMacro(m); else Finish(); }
                return;
            }
            if (State == CoreState.Idle) { ticker.Stop(); return; }
            Raise();
        }

        void Begin(RunJob job, CoreState st)
        {
            Job = job;
            State = st;
            LastResult = null;
            WatchKeys(true);
            job.Ended += j => Post(() => OnEnded(j));
            if (Synchronous)
            {
                var clock = TestClock ?? new FakeClock();
                clock.Job = job;
                Raise();
                job.Execute(clock);
                return;
            }
            ticker.Start();
            Raise();
            JobThread.Start(job);
        }

        void OnEnded(RunJob j)
        {
            if (j != Job) return;
            LastResult = ResultText(j);
            Job = null;
            Finish();
            bool problem = j.Reason == RunJob.ReasonNoWindow || j.Reason == RunJob.ReasonError || j.Reason == RunJob.ReasonNoPoints;
            Say(LastResult, problem);
        }

        public static string ResultText(RunJob j)
        {
            var cj = j as ClickJob;
            string what;
            if (cj != null) what = (cj.Settings.IsKey ? "キーを " : "") + j.Done.ToString("#,0") + " 回" + (cj.Settings.IsKey ? "押しました" : "クリックしました");
            else
            {
                var mj = (MacroJob)j;
                what = "「" + mj.Macro.Name + "」を" + (mj.Iteration >= 1 && j.Reason == RunJob.ReasonDone ? (mj.Iteration == 1 ? "" : " " + mj.Iteration + " 回") + "再生しました" : "再生しました (" + j.Done + " 手順)");
            }
            if (j.Skipped > 0) what += " (Teal Macro の画面の上の " + j.Skipped + " 回はとばしました)";
            switch (j.Reason)
            {
                case RunJob.ReasonCount: return what + "。決めた回数で止めました";
                case RunJob.ReasonTime: return what + "。決めた時間で止めました";
                case RunJob.ReasonEsc: return what + "。Esc で止めました";
                case RunJob.ReasonHotkey: return what + "。ショートカットで止めました";
                case RunJob.ReasonUser: return what + "。止めました";
                case RunJob.ReasonNoPoints: return "クリックする位置がありません";
                case RunJob.ReasonNoWindow: return "ウィンドウ「" + (cj != null ? cj.MissingWindow : "") + "」が見つからないので止めました (開いているか、最小化していないか確かめてください)";
                case RunJob.ReasonError: return "うまく動きませんでした: " + j.Error;
                default: return what;
            }
        }

        /// <summary>すべて止める (連打・再生・カウントダウン・記録)</summary>
        public void StopAll(string reason)
        {
            switch (State)
            {
                case CoreState.Countdown:
                    pending = null;
                    Finish();
                    Say("取り消しました", false);
                    break;
                case CoreState.Clicking:
                case CoreState.Playing:
                    var j = Job;
                    if (j != null) j.Stop(reason);
                    if (Synchronous && j != null && j.Finished) OnEnded(j);
                    break;
                case CoreState.Recording:
                    StopRecording();
                    break;
            }
        }

        void Finish()
        {
            State = CoreState.Idle;
            ticker.Stop();
            WatchKeys(false);
            if (HideMain != null && !Synchronous) HideMain(false);
            Raise();
        }

        void WatchKeys(bool on)
        {
            var ih = hook as InputHook;
            if (ih == null) return;
            if (on && !ih.Running) ih.Start();
            ih.EscStops = Settings.EscStops;
            ih.Watching = on;
        }

        /// <summary>フックから (別のスレッド): 実行中の Esc・すべて停止</summary>
        public void OnHookStop(string reason)
        {
            Post(() => { if (State != CoreState.Idle && State != CoreState.Recording) StopAll(reason); else if (State == CoreState.Recording && reason == RunJob.ReasonHotkey) StopAll(reason); });
        }

        // ================================================================ 記録
        public void ToggleRecording()
        {
            if (State == CoreState.Recording) StopRecording();
            else StartRecording();
        }

        public bool StartRecording()
        {
            if (State != CoreState.Idle) StopAll(RunJob.ReasonUser);
            if (hook == null || (!hook.Running && !hook.Start())) { Say("記録を始められませんでした (入力を見るしくみを使えません)", true); return false; }
            if (Macros.Macros.Count >= MacroFile.MaxMacros) { Say("マクロは " + MacroFile.MaxMacros + " 個までです。使わないものを削除してください", true); return false; }
            hook.StopKey = StopKey;
            var ih = hook as InputHook;
            if (ih != null) { ih.EscStops = Settings.EscStops; ih.Watching = true; }
            hook.BeginRecording();
            recordStart = DateTime.Now;
            State = CoreState.Recording;
            if (Settings.HideWhileRunning && HideMain != null && !Synchronous) HideMain(true);
            ticker.Start();
            Raise();
            return true;
        }

        public int RecordedCount { get { return State == CoreState.Recording && hook != null ? hook.RecordedCount : 0; } }
        public TimeSpan RecordingTime { get { return State == CoreState.Recording ? DateTime.Now - recordStart : TimeSpan.Zero; } }

        /// <summary>記録を終えて、新しいマクロにする (何も記録されなければ作らない)。できたマクロ</summary>
        public MacroDef StopRecording()
        {
            if (State != CoreState.Recording) return null;
            var raw = hook.EndRecording();
            Finish();
            var steps = Recorder.Build(raw, AllHotkeys(), Settings.RecordMoves);
            if (steps.Count == 0) { LastResult = "操作が記録されませんでした"; Say(LastResult + "。記録中にクリックやキーを押してください", true); return null; }
            var m = new MacroDef { Id = MacroDef.NewId(), Name = Macros.NextName(), Steps = steps, Created = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") };
            Macros.Macros.Add(m);
            Settings.SelectedMacro = m.Id;
            Settings.Tab = 1;
            SaveMacros();
            SaveSettings();
            LastResult = "「" + m.Name + "」を記録しました (" + steps.Count + " 手順)";
            Raise();
            Say(LastResult + "。名前は「名前の変更」で変えられます", false);
            return m;
        }

        // ================================================================ 位置
        /// <summary>今のマウスの位置を、連打の「決めた位置」に足す (位置を記録のショートカット)</summary>
        public TargetPoint AddPointFromCursor()
        {
            var p = Api.CursorPos();
            if (Api.IsOwnWindowAt(p)) { Say("Teal Macro の画面の上は記録できません。クリックしたい場所にマウスを置いてから押してください", true); return null; }
            return AddPoint(WindowMatch.Make(p, Api.WindowAt(p)));
        }

        public TargetPoint AddPoint(TargetPoint t)
        {
            var c = Settings.Clicker;
            if (t == null) return null;
            if (c.Points.Count >= ClickerSettings.MaxPoints) { Say("決めた位置は " + ClickerSettings.MaxPoints + " か所までです", true); return null; }
            c.Points.Add(t);
            c.Target = ClickerSettings.TargetPoints;
            if (c.IsKey) c.Action = ClickerSettings.ActLeft;
            SaveSettings();
            if (ClickerChanged != null) ClickerChanged();
            Say("位置を記録しました (" + c.Points.Count + " か所目: " + t.Describe(c.WindowRelative) + ")", false);
            return t;
        }

        // ================================================================ マクロの編集
        public MacroDef NewMacro()
        {
            if (Macros.Macros.Count >= MacroFile.MaxMacros) { Say("マクロは " + MacroFile.MaxMacros + " 個までです", true); return null; }
            var m = new MacroDef { Id = MacroDef.NewId(), Name = Macros.NextName(), Created = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") };
            Macros.Macros.Add(m);
            Settings.SelectedMacro = m.Id;
            SaveMacros(); SaveSettings();
            return m;
        }

        public MacroDef Duplicate(MacroDef src)
        {
            if (src == null || Macros.Macros.Count >= MacroFile.MaxMacros) return null;
            var m = src.Clone();
            m.Id = MacroDef.NewId();
            var baseName = src.Name.Length > 34 ? src.Name.Substring(0, 34) : src.Name;
            m.Name = baseName + " のコピー";
            for (int i = 2; Macros.ValidateName(m.Name, null) != null; i++) m.Name = baseName + " のコピー " + i;
            Macros.Macros.Insert(Macros.Macros.IndexOf(src) + 1, m);
            Settings.SelectedMacro = m.Id;
            SaveMacros(); SaveSettings();
            return m;
        }

        public bool Delete(MacroDef m)
        {
            if (m == null) return false;
            var mj = MacroJob;
            if (mj != null && mj.Macro.Id == m.Id) StopAll(RunJob.ReasonUser);
            int i = Macros.Macros.IndexOf(m);
            if (!Macros.Macros.Remove(m)) return false;
            Settings.SelectedMacro = Macros.Macros.Count == 0 ? null : Macros.Macros[Math.Min(i, Macros.Macros.Count - 1)].Id;
            SaveMacros(); SaveSettings();
            return true;
        }

        public string Rename(MacroDef m, string name)
        {
            var why = Macros.ValidateName(name, m);
            if (why != null) return why;
            m.Name = name.Trim();
            SaveMacros();
            return null;
        }

        /// <summary>保存して、変わったら Teal Hub にマクロごとのショートカットの一覧を知らせる</summary>
        public void SaveMacros()
        {
            MacroStore.SaveMacros(Macros);
            try { MacroStore.PublishSlots(Macros); } catch (Exception ex) { DataStore.Log("slots: " + ex.Message); }
            if (MacrosChanged != null) MacrosChanged();
        }

        public void SaveSettings() { MacroStore.SaveSettings(Settings); }

        // ================================================================ 表示
        void Say(string msg, bool error) { if (Notice != null) Notice(msg, error); }
        void Raise() { if (Changed != null) Changed(); }

        public bool IsBusy { get { return State != CoreState.Idle; } }

        /// <summary>状態の見出し (「● 実行中」など)</summary>
        public string StateTitle
        {
            get
            {
                switch (State)
                {
                    case CoreState.Countdown: return CountdownLeft + " 秒後に始めます";
                    case CoreState.Clicking: return "実行中 ・ 連打";
                    case CoreState.Playing: return "実行中 ・ マクロ";
                    case CoreState.Recording: return "記録中";
                    default: return "待機中";
                }
            }
        }

        /// <summary>状態の説明 (「123 回 ・ 4.2 秒」など)</summary>
        public string StateDetail
        {
            get
            {
                var stop = StopKey.IsEmpty ? "" : StopKey.Display;
                string stopHint = Settings.EscStops ? "Esc" + (stop.Length > 0 ? " か " + stop : "") + " で止まります" : (stop.Length > 0 ? stop + " で止まります" : "「■ 停止」で止まります");
                switch (State)
                {
                    case CoreState.Countdown:
                        return (pending == "click" ? (Settings.Clicker.IsKey ? "キーを送りたいウィンドウをクリックして選んでください" : Settings.Clicker.UsesPoints ? "決めた位置をクリックします" : "マウスを連打したい場所へ動かしてください") : "マクロ「" + MacroName(pending) + "」を再生します") + " ・ " + stopHint;
                    case CoreState.Clicking:
                        {
                            var j = ClickJob; if (j == null) return "";
                            var c = j.Settings;
                            var t = j.Done.ToString("#,0") + " 回";
                            if (c.StopByCount) t += " / " + c.Count.ToString("#,0") + " 回";
                            var el = ElapsedMs(j);
                            t += " ・ " + (el / 1000.0).ToString("0.0") + " 秒";
                            if (c.StopByTime) t += " / " + c.Seconds + " 秒";
                            return t + " ・ " + stopHint;
                        }
                    case CoreState.Playing:
                        {
                            var j = MacroJob; if (j == null) return "";
                            var t = "「" + j.Macro.Name + "」 手順 " + Math.Min(j.Macro.Steps.Count, j.StepIndex + 1) + " / " + j.Macro.Steps.Count;
                            if (j.Macro.Repeat != 1) t += " ・ " + (j.Iteration + 1) + (j.Macro.Repeat == 0 ? " 回目" : " / " + j.Macro.Repeat + " 回");
                            return t + " ・ " + stopHint;
                        }
                    case CoreState.Recording:
                        return RecordedCount + " 個の操作 ・ " + (int)RecordingTime.TotalSeconds + " 秒 ・ " + (RecordKey.IsEmpty ? "「■ 記録を終える」" : RecordKey.Display + " か「■ 記録を終える」") + "で終わります";
                    default:
                        return LastResult ?? "";
                }
            }
        }

        readonly System.Diagnostics.Stopwatch runWatch = System.Diagnostics.Stopwatch.StartNew();
        RunJob watchJob; long watchStart;
        long ElapsedMs(RunJob j)
        {
            if (j != watchJob) { watchJob = j; watchStart = runWatch.ElapsedMilliseconds; }
            return runWatch.ElapsedMilliseconds - watchStart;
        }

        string MacroName(string id) { var m = Macros.Find(id); return m == null ? "" : m.Name; }

        public void Dispose()
        {
            try { if (Job != null) Job.Stop(RunJob.ReasonUser); } catch { }
            try { ticker.Stop(); ticker.Dispose(); } catch { }
            try { if (hook != null) hook.Dispose(); } catch { }
        }
    }
}
