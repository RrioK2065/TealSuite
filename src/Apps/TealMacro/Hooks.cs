// Teal Macro - 低レベル フック (WH_KEYBOARD_LL / WH_MOUSE_LL)。ほかのアプリに DLL を入れない方式
//  - 動かすのは「記録している間」と「連打・再生している間」だけ (いつもは何も見ていない)
//  - 専用のスレッドで受け取り、すぐ返す (UI が忙しくても Windows にフックを外されないように)
//  - Teal Macro 自身が送った入力 (INJECTED) は見ない (再生した操作を記録しない)
//  - 記録中: 入力を RawEvent にためる。ボタンを押した場所が Teal Macro の画面なら Own の印をつける (記録しない)
//  - 実行中: 本物のキーボードの Esc (設定でオフにできる) と「すべて停止」の組み合わせで止める (そのキーは相手のアプリに渡さない)
//    Teal Hub の「すべて停止」のショートカットと同じものを、Teal Hub が起動していないときのためにここでも見る
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public class InputHook : IHookSource
    {
        delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205,
            WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A;
        const uint LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x1;

        readonly HookProc kbProc, msProc;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly object gate = new object();
        Thread thread;
        uint tid;
        IntPtr kbHook, msHook;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<RawEvent> events = new List<RawEvent>();
        HotMods mods;

        /// <summary>記録中なら true (入力をためる)</summary>
        public volatile bool Recording;
        /// <summary>実行中なら true (Esc・停止キーを見る)</summary>
        public volatile bool Watching;
        public volatile bool EscStops = true;
        HotkeySpec stopKey;
        public HotkeySpec StopKey { get { lock (gate) return stopKey; } set { lock (gate) stopKey = value; } }
        /// <summary>止める合図 (フックのスレッドから呼ばれる。すぐ返すこと)。理由 (RunJob.ReasonEsc / ReasonHotkey)</summary>
        public Action<string> StopPressed;
        /// <summary>Teal Macro の画面の上か (フックのスレッドから呼ばれる)</summary>
        public Func<Point, bool> IsOwn { get; set; }
        public bool Running { get { return kbHook != IntPtr.Zero || msHook != IntPtr.Zero; } }

        public InputHook() { kbProc = KbCallback; msProc = MsCallback; }

        public bool Start()
        {
            if (!AppInfo.IsWindows) return false;
            if (thread != null) return Running;
            thread = new Thread(Run) { IsBackground = true, Name = "TealMacro.Hook" };
            thread.Start();
            ready.WaitOne(2000);
            return Running;
        }

        void Run()
        {
            try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }
            tid = GetCurrentThreadId();
            var mod = GetModuleHandle(null);
            kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, mod, 0);
            msHook = SetWindowsHookEx(WH_MOUSE_LL, msProc, mod, 0);
            if (kbHook == IntPtr.Zero || msHook == IntPtr.Zero) DataStore.Log("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
            ready.Set();
            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
            if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
            if (msHook != IntPtr.Zero) UnhookWindowsHookEx(msHook);
            kbHook = msHook = IntPtr.Zero;
        }

        /// <summary>記録を始める (ためたものを空にする)</summary>
        public void BeginRecording()
        {
            lock (gate) events.Clear();
            Recording = true;
        }

        /// <summary>記録を終えて、ためたものを返す</summary>
        public List<RawEvent> EndRecording()
        {
            Recording = false;
            lock (gate) { var l = new List<RawEvent>(events); events.Clear(); return l; }
        }

        public int RecordedCount { get { lock (gate) return events.Count; } }

        void Add(RawEvent e)
        {
            lock (gate) { if (events.Count < 200000) events.Add(e); }
        }

        IntPtr KbCallback(int code, IntPtr wp, IntPtr lp)
        {
            if (code >= 0)
            {
                try
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lp, typeof(KBDLLHOOKSTRUCT));
                    int msg = wp.ToInt32();
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN, up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                    if ((k.flags & LLKHF_INJECTED) == 0 && (down || up))
                    {
                        int vk = (int)k.vkCode;
                        var m = KeyText.ModOf(vk);
                        if (m != HotMods.None) { if (down) mods |= m; else mods &= ~m; }
                        if (Watching && down && m == HotMods.None)
                        {
                            var sk = StopKey;
                            bool esc = EscStops && !Recording && vk == (int)Keys.Escape && mods == HotMods.None;   // 記録中の Esc は記録する (止めない)
                            bool hot = !sk.IsEmpty && vk == (int)sk.Key && mods == sk.Mods;
                            if (esc || hot)
                            {
                                var h = StopPressed;
                                if (h != null) h(esc ? RunJob.ReasonEsc : RunJob.ReasonHotkey);
                                if (esc) return new IntPtr(1);   // 止めるための Esc は相手のアプリに渡さない
                            }
                        }
                        if (Recording) Add(new RawEvent { Kind = down ? RawKind.KeyDown : RawKind.KeyUp, Time = clock.ElapsedMilliseconds, Vk = vk });
                    }
                }
                catch { }
            }
            return CallNextHookEx(kbHook, code, wp, lp);
        }

        IntPtr MsCallback(int code, IntPtr wp, IntPtr lp)
        {
            if (code >= 0 && Recording)
            {
                try
                {
                    var s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lp, typeof(MSLLHOOKSTRUCT));
                    if ((s.flags & LLMHF_INJECTED) == 0)
                    {
                        int msg = wp.ToInt32();
                        var e = new RawEvent { Time = clock.ElapsedMilliseconds, X = s.pt.X, Y = s.pt.Y };
                        bool ok = true;
                        switch (msg)
                        {
                            case WM_MOUSEMOVE: e.Kind = RawKind.Move; break;
                            case WM_LBUTTONDOWN: e.Kind = RawKind.Down; e.Button = MouseBtn.Left; break;
                            case WM_LBUTTONUP: e.Kind = RawKind.Up; e.Button = MouseBtn.Left; break;
                            case WM_RBUTTONDOWN: e.Kind = RawKind.Down; e.Button = MouseBtn.Right; break;
                            case WM_RBUTTONUP: e.Kind = RawKind.Up; e.Button = MouseBtn.Right; break;
                            case WM_MBUTTONDOWN: e.Kind = RawKind.Down; e.Button = MouseBtn.Middle; break;
                            case WM_MBUTTONUP: e.Kind = RawKind.Up; e.Button = MouseBtn.Middle; break;
                            case WM_MOUSEWHEEL: e.Kind = RawKind.Wheel; e.Delta = (short)(s.mouseData >> 16); break;
                            default: ok = false; break;
                        }
                        if (ok)
                        {
                            if (e.Kind != RawKind.Move && IsOwn != null) { try { e.Own = IsOwn(new Point(e.X, e.Y)); } catch { } }
                            Add(e);
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(msHook, code, wp, lp);
        }

        public void Dispose()
        {
            Recording = Watching = false;
            if (thread == null) return;
            try { PostThreadMessage(tid, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero); } catch { }
            try { thread.Join(1000); } catch { }
            thread = null;
        }
    }
}
