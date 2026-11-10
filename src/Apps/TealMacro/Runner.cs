// Teal Macro - 連打とマクロの再生 (別のスレッドで動かす。画面は 0.1 秒ごとに進み具合を読む)
//  - RunJob … 止める合図 (Stop) と進み具合。ClickJob (連打) と MacroJob (マクロの再生)
//  - IRunClock … 時間と待ち。本物は Stopwatch + 止める合図で起きる待ち (Windows ではタイマーの精度を 1ms に)。テストでは FakeClock (待たずに時間だけ進める)
//  - 押したままのボタン・キーは、止めたとき・終わったときに必ず離す
//  - Teal Macro の画面の上はクリックしない (連打中でも「停止」ボタンを押せるように)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public interface IRunClock
    {
        long Now { get; }
        /// <summary>ms だけ待つ。止める合図があれば false</summary>
        bool Wait(double ms);
    }

    public class RealClock : IRunClock
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly WaitHandle cancel;
        public RealClock(WaitHandle cancel) { this.cancel = cancel; }
        public long Now { get { return sw.ElapsedMilliseconds; } }
        public bool Wait(double ms)
        {
            if (ms <= 0) return !cancel.WaitOne(0);
            double until = sw.Elapsed.TotalMilliseconds + ms;
            while (true)
            {
                double left = until - sw.Elapsed.TotalMilliseconds;
                if (left <= 0) return !cancel.WaitOne(0);
                if (left >= 2) { if (cancel.WaitOne((int)(left - 1))) return false; }
                else { Thread.SpinWait(200); if (cancel.WaitOne(0)) return false; }
            }
        }
    }

    /// <summary>テスト用: 待たずに時間だけ進める</summary>
    public class FakeClock : IRunClock
    {
        double now;
        public RunJob Job;
        /// <summary>待つたびに呼ばれる (今の時刻)。テストで途中で止めるのに使う</summary>
        public Action<long> OnWait;
        public long Now { get { return (long)now; } }
        public bool Wait(double ms)
        {
            if (ms > 0) now += ms;
            if (OnWait != null) OnWait(Now);
            return Job == null || !Job.StopRequested;
        }
    }

    public abstract class RunJob
    {
        public const string ReasonUser = "user", ReasonDone = "done", ReasonCount = "count", ReasonTime = "time", ReasonNoPoints = "nopoints",
            ReasonNoWindow = "nowindow", ReasonError = "error", ReasonEsc = "esc", ReasonHotkey = "hotkey";

        readonly ManualResetEvent cancel = new ManualResetEvent(false);
        volatile bool stop;
        protected readonly IInputApi Api;
        readonly HashSet<MouseBtn> heldButtons = new HashSet<MouseBtn>();
        readonly HashSet<Keys> heldKeys = new HashSet<Keys>();

        public WaitHandle CancelHandle { get { return cancel; } }
        public bool StopRequested { get { return stop; } }
        /// <summary>止まった理由 (ReasonXxx)</summary>
        public string Reason { get; protected set; }
        /// <summary>連打: 押した回数 / マクロ: 実行した手順の数</summary>
        public int Done;
        /// <summary>Teal Macro の画面の上だったのでとばした回数</summary>
        public int Skipped;
        public bool Finished { get; private set; }
        public long StartedAt { get; private set; }
        public long ElapsedMs;
        public string Error { get; private set; }
        /// <summary>終わったとき (動かしたスレッドから呼ばれる)</summary>
        public event Action<RunJob> Ended;

        protected RunJob(IInputApi api) { Api = api; }

        public void Stop(string reason)
        {
            if (stop) return;
            if (Reason == null) Reason = reason;
            stop = true;
            try { cancel.Set(); } catch { }
        }

        /// <summary>最後まで動かす (このスレッドで)</summary>
        public void Execute(IRunClock clock)
        {
            long t0 = clock.Now;
            StartedAt = t0;
            try { Run(clock, t0); if (Reason == null) Reason = ReasonDone; }
            catch (Exception ex) { Error = ex.Message; Reason = ReasonError; DataStore.Log("run: " + ex); }
            finally
            {
                ReleaseAll();
                ElapsedMs = clock.Now - t0;
                Finished = true;
                var h = Ended; if (h != null) try { h(this); } catch (Exception ex) { DataStore.Log("run ended: " + ex.Message); }
            }
        }

        protected abstract void Run(IRunClock clock, long t0);

        protected void Press(MouseBtn b, bool down)
        {
            Api.Button(b, down);
            if (down) heldButtons.Add(b); else heldButtons.Remove(b);
        }

        protected void KeyPress(Keys k, bool down)
        {
            Api.Key(k, down);
            if (down) heldKeys.Add(k); else heldKeys.Remove(k);
        }

        /// <summary>キー (Ctrl+C など) を押して離す</summary>
        protected bool Combo(HotkeySpec k, IRunClock clock, int holdMs)
        {
            var mods = KeyText.ModKeys(k.Mods);
            foreach (var m in mods) KeyPress(m, true);
            KeyPress(k.Key, true);
            bool ok = holdMs <= 0 || clock.Wait(holdMs);
            KeyPress(k.Key, false);
            for (int i = mods.Count - 1; i >= 0; i--) KeyPress(mods[i], false);
            return ok;
        }

        protected bool ClickAt(MouseBtn b, int clicks, IRunClock clock, int holdMs)
        {
            for (int c = 0; c < Math.Max(1, clicks); c++)
            {
                if (c > 0 && !clock.Wait(40)) return false;
                Press(b, true);
                bool ok = holdMs <= 0 || clock.Wait(holdMs);
                Press(b, false);
                if (!ok) return false;
            }
            return true;
        }

        void ReleaseAll()
        {
            foreach (var b in heldButtons.ToList()) { try { Api.Button(b, false); } catch { } }
            heldButtons.Clear();
            foreach (var k in heldKeys.ToList()) { try { Api.Key(k, false); } catch { } }
            heldKeys.Clear();
        }
    }

    // ================================================================ 連打
    public class ClickJob : RunJob
    {
        readonly ClickerSettings s;
        readonly Random rnd;
        public ClickerSettings Settings { get { return s; } }
        /// <summary>ウィンドウの中の位置が見つからなかったときのウィンドウ名</summary>
        public string MissingWindow { get; private set; }

        public ClickJob(ClickerSettings settings, IInputApi api, int seed = 0) : base(api)
        {
            s = settings;
            rnd = seed == 0 ? new Random() : new Random(seed);
        }

        /// <summary>何回目 (0 から) にどこをクリックするか。null ならマウスのある場所。見つからなければ例外ではなく Stop</summary>
        public Point? TargetFor(int index)
        {
            if (!s.UsesPoints) return null;
            if (s.Points.Count == 0) { Stop(ReasonNoPoints); return null; }
            var tp = s.Points[index % s.Points.Count];
            if (s.WindowRelative && tp.HasWindow)
            {
                var w = Api.FindWindow(tp);
                if (w == null) { MissingWindow = string.IsNullOrEmpty(tp.Title) ? tp.Exe : tp.Title; Stop(ReasonNoWindow); return null; }
                return new Point(w.ClientOrigin.X + tp.RelX, w.ClientOrigin.Y + tp.RelY);
            }
            return tp.Screen;
        }

        protected override void Run(IRunClock clock, long t0)
        {
            double interval = s.IntervalMs;
            double next = t0;
            int index = 0;
            HotkeySpec key;
            KeyText.TryParse(s.Key, out key);
            if (s.IsKey && key.IsEmpty) { Stop(ReasonError); return; }
            var btn = s.Action == ClickerSettings.ActRight ? MouseBtn.Right : s.Action == ClickerSettings.ActMiddle ? MouseBtn.Middle : MouseBtn.Left;
            int hold = Math.Min(s.HoldMs, (int)(interval / 2));
            while (!StopRequested)
            {
                if (s.StopByCount && Done >= s.Count) { Stop(ReasonCount); break; }
                if (s.StopByTime && clock.Now - t0 >= s.Seconds * 1000L) { Stop(ReasonTime); break; }
                if (s.IsKey)
                {
                    if (!Combo(key, clock, hold)) break;
                    Done++;
                }
                else
                {
                    var p = TargetFor(index);
                    if (StopRequested) break;
                    var at = p ?? Api.CursorPos();
                    if (Api.IsOwnWindowAt(at)) Skipped++;
                    else
                    {
                        if (p.HasValue) Api.MoveTo(p.Value);
                        if (!ClickAt(btn, s.Action == ClickerSettings.ActDouble ? 2 : 1, clock, hold)) { Done++; break; }
                        Done++;
                    }
                }
                index++;
                if (s.StopByCount && Done >= s.Count) { Stop(ReasonCount); break; }
                next += s.Jitter ? interval * (0.8 + rnd.NextDouble() * 0.4) : interval;
                double now = clock.Now;
                if (now - next > interval * 5) next = now;   // 遅れたぶんを一気に取り戻さない
                if (s.StopByTime) next = Math.Min(next, t0 + s.Seconds * 1000.0);
                if (!clock.Wait(next - clock.Now)) break;
            }
        }
    }

    // ================================================================ マクロの再生
    public class MacroJob : RunJob
    {
        readonly MacroDef m;
        public MacroDef Macro { get { return m; } }
        public int Iteration;
        /// <summary>今の手順 (0 から)</summary>
        public int StepIndex;

        public MacroJob(MacroDef macro, IInputApi api) : base(api) { m = macro; }

        protected override void Run(IRunClock clock, long t0)
        {
            double speed = Math.Max(0.01, m.Speed);
            if (m.Steps.Count == 0) return;
            for (Iteration = 0; m.Repeat == 0 || Iteration < m.Repeat; Iteration++)
            {
                for (StepIndex = 0; StepIndex < m.Steps.Count; StepIndex++)
                {
                    var st = m.Steps[StepIndex];
                    if (st.Delay > 0 && !clock.Wait(st.Delay / speed)) return;
                    if (StopRequested || !Perform(st, clock, speed)) return;
                    Done++;
                }
                if (StopRequested) return;
                // くり返しの間に少しだけ間を置く (ずっとくり返すときに止められるように)
                if ((m.Repeat == 0 || Iteration + 1 < m.Repeat) && !clock.Wait(Math.Max(1, 30 / speed))) return;
            }
        }

        readonly HashSet<MouseBtn> skippedDown = new HashSet<MouseBtn>();

        bool Perform(MacroStep st, IRunClock clock, double speed)
        {
            var p = new Point(st.X, st.Y);
            HotkeySpec k;
            switch (st.Kind)
            {
                case MacroStep.KClick:
                    if (Api.IsOwnWindowAt(p)) { Skipped++; return true; }
                    Api.MoveTo(p);
                    return ClickAt(st.Btn, st.Clicks, clock, 10);
                case MacroStep.KMove:
                    if (st.Path != null && st.Path.Count > 1)
                    {
                        long start = clock.Now;
                        foreach (var pt in st.Path)
                        {
                            double wait = pt.T / speed - (clock.Now - start);
                            if (wait > 0 && !clock.Wait(wait)) return false;
                            Api.MoveTo(new Point(pt.X, pt.Y));
                        }
                    }
                    else Api.MoveTo(p);
                    return true;
                case MacroStep.KDown:
                    if (Api.IsOwnWindowAt(p)) { Skipped++; skippedDown.Add(st.Btn); return true; }
                    Api.MoveTo(p);
                    Press(st.Btn, true);
                    return true;
                case MacroStep.KUp:
                    if (skippedDown.Remove(st.Btn)) return true;
                    Api.MoveTo(p);
                    Press(st.Btn, false);
                    return true;
                case MacroStep.KWheel:
                    Api.MoveTo(p);
                    Api.Wheel(st.Delta);
                    return true;
                case MacroStep.KKey:
                    if (!KeyText.TryParse(st.Key, out k)) return true;
                    return Combo(k, clock, 10);
                case MacroStep.KKeyDown:
                    if (KeyText.TryParse(st.Key, out k)) KeyPress(k.Key, true);
                    return true;
                case MacroStep.KKeyUp:
                    if (KeyText.TryParse(st.Key, out k)) KeyPress(k.Key, false);
                    return true;
                case MacroStep.KText:
                    Api.Text(st.Text);
                    return true;
                case MacroStep.KWait:
                    return clock.Wait(st.Ms / speed);
                default: return true;
            }
        }
    }

    /// <summary>ジョブを別のスレッドで動かす</summary>
    public static class JobThread
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        public static Thread Start(RunJob job)
        {
            var t = new Thread(() =>
            {
                bool period = false;
                try { if (AppInfo.IsWindows) { timeBeginPeriod(1); period = true; } } catch { }
                try { job.Execute(new RealClock(job.CancelHandle)); }
                finally { if (period) try { timeEndPeriod(1); } catch { } }
            }) { IsBackground = true, Name = "TealMacro.Run", Priority = ThreadPriority.AboveNormal };
            t.Start();
            return t;
        }
    }
}
