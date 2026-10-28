// Teal Pin - Windows のできごとを受け取る (Windows のときだけ)
//  - WinEventSource … SetWinEventHook (WINEVENT_OUTOFCONTEXT)。ほかのアプリに DLL を入れない方式。
//      ウィンドウの移動・大きさ・最小化・閉じる・表示・前面・タイトル・仮想デスクトップ (cloak) とメニューの開閉を受け取る
//  - MouseHookThread … 低レベルのマウス フック (WH_MOUSE_LL)。これも DLL を入れない方式。
//      専用のスレッドで受け取り、すぐに UI スレッドへ渡して返す (UI が忙しくても Windows にフックを外されないように)
//  - TitleBarMenu … タイトルバーの右クリック → 「📌 最前面に固定 / 固定を外す」をシステム メニューの上に出す
//      システム メニューそのものに項目を足すには、相手のアプリに DLL を入れる (サブクラス化する) 必要があるのでしない。
//      代わりに、右クリックの位置が相手のタイトルバーか (WM_NCHITTEST が HTCAPTION) を確かめて、
//      開いたメニューのすぐ上に Teal Pin の小さなボタンを並べる。
//      メニューが開いている間はマウスをメニューがつかんでいるので、ボタンの範囲のクリックはフックで受け取って (メニューには渡さない) 処理する
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using TealSuite.Common;

namespace TealSuite.Pin
{
    public class WinEventSource : IDisposable
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

        public const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MENUSTART = 0x4, EVENT_SYSTEM_MENUEND = 0x5,
            EVENT_SYSTEM_MENUPOPUPSTART = 0x6, EVENT_SYSTEM_MENUPOPUPEND = 0x7;

        static readonly uint[][] Ranges =
        {
            new uint[] { 0x0003, 0x0007 },   // 前面・メニュー
            new uint[] { 0x0016, 0x0017 },   // 最小化の始まり / 終わり
            new uint[] { 0x8001, 0x8003 },   // 閉じた・表示・非表示
            new uint[] { 0x800B, 0x800C },   // 位置・大きさ / タイトル
            new uint[] { 0x8017, 0x8018 },   // 隠れた (cloaked) / 戻った
        };

        readonly WinEventProc proc;   // GC に回収されないように持っておく
        readonly System.Collections.Generic.List<IntPtr> hooks = new System.Collections.Generic.List<IntPtr>();
        /// <summary>ウィンドウのできごと (UI スレッドで呼ばれる)</summary>
        public Action<WinEvt, IntPtr> OnWindow;
        /// <summary>メニューのできごと (イベント番号, ウィンドウ)</summary>
        public Action<uint, IntPtr> OnMenu;

        public WinEventSource() { proc = Callback; }

        public bool Start()
        {
            foreach (var r in Ranges)
            {
                var h = SetWinEventHook(r[0], r[1], IntPtr.Zero, proc, 0, 0, 0x0 /* OUTOFCONTEXT */ | 0x2 /* SKIPOWNPROCESS */);
                if (h != IntPtr.Zero) hooks.Add(h);
            }
            return hooks.Count > 0;
        }

        void Callback(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (ev >= EVENT_SYSTEM_MENUSTART && ev <= EVENT_SYSTEM_MENUPOPUPEND)
                {
                    if (OnMenu != null) OnMenu(ev, h);
                    return;
                }
                if (idObject != 0 || idChild != 0 || h == IntPtr.Zero || OnWindow == null) return;   // OBJID_WINDOW / CHILDID_SELF だけ
                WinEvt e;
                switch (ev)
                {
                    case EVENT_SYSTEM_FOREGROUND: e = WinEvt.Foreground; break;
                    case 0x0016: e = WinEvt.MinimizeStart; break;
                    case 0x0017: e = WinEvt.MinimizeEnd; break;
                    case 0x8001: e = WinEvt.Destroy; break;
                    case 0x8002: e = WinEvt.Show; break;
                    case 0x8003: e = WinEvt.Hide; break;
                    case 0x800B: e = WinEvt.Location; break;
                    case 0x800C: e = WinEvt.NameChange; break;
                    case 0x8017: e = WinEvt.Cloaked; break;
                    case 0x8018: e = WinEvt.Uncloaked; break;
                    default: return;
                }
                // 子ウィンドウのできごとは使わない (閉じたときはもう調べられないのでそのまま渡す)
                if (e != WinEvt.Destroy && e != WinEvt.Location && PinNative.GetAncestor(h, PinNative.GA_ROOT) != h) return;
                OnWindow(e, h);
            }
            catch (Exception ex) { DataStore.Log("winevent: " + ex.Message); }
        }

        public void Dispose()
        {
            foreach (var h in hooks) { try { UnhookWinEvent(h); } catch { } }
            hooks.Clear();
        }
    }

    /// <summary>低レベルのマウス フック (専用スレッド)</summary>
    public class MouseHookThread : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public PinNative.POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public uint time; public PinNative.POINT pt; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, int msg, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;

        readonly HookProc proc;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        Thread thread;
        uint tid;
        IntPtr hook;
        readonly object gate = new object();
        Rectangle popupRect;
        volatile bool popupActive;
        bool lastInside;

        /// <summary>フックのスレッドから呼ばれる (すぐ返すこと)。(メッセージ, 物理 px の位置)</summary>
        public Action<int, Point> Post;
        public bool Running { get { return hook != IntPtr.Zero; } }

        public MouseHookThread() { proc = Callback; }

        /// <summary>この範囲 (物理 px) のクリックはフックで受け取る (空なら受け取らない)</summary>
        public void SetPopup(Rectangle r)
        {
            lock (gate) { popupRect = r; popupActive = r.Width > 0; lastInside = false; }
        }

        public bool Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "TealPin.MouseHook" };
            thread.Start();
            ready.WaitOne(2000);
            return hook != IntPtr.Zero;
        }

        void Run()
        {
            tid = GetCurrentThreadId();
            hook = SetWindowsHookEx(14 /* WH_MOUSE_LL */, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) DataStore.Log("mouse hook failed: " + Marshal.GetLastWin32Error());
            ready.Set();
            if (hook == IntPtr.Zero) return;
            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        IntPtr Callback(int code, IntPtr wp, IntPtr lp)
        {
            if (code >= 0)
            {
                try
                {
                    int msg = wp.ToInt32();
                    if (msg == WM_RBUTTONUP || msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_RBUTTONDOWN || (msg == WM_MOUSEMOVE && popupActive))
                    {
                        var s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lp, typeof(MSLLHOOKSTRUCT));
                        var pt = new Point(s.pt.X, s.pt.Y);
                        bool inside = false;
                        if (popupActive) lock (gate) inside = popupRect.Contains(pt);
                        var post = Post;
                        if (msg == WM_MOUSEMOVE)
                        {
                            if (inside != lastInside) { lastInside = inside; if (post != null) post(msg, pt); }
                        }
                        else
                        {
                            if (post != null && (msg == WM_RBUTTONUP || msg == WM_LBUTTONUP || popupActive)) post(msg, pt);
                            if (inside) return new IntPtr(1);   // ボタンのクリックはメニューに渡さない
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(hook, code, wp, lp);
        }

        public void Dispose()
        {
            try { if (tid != 0) PostThreadMessage(tid, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero); } catch { }
            try { if (thread != null) thread.Join(1500); } catch { }
            ready.Close();
        }
    }

    /// <summary>タイトルバーの右クリックのメニュー (A と並ぶ固定のしかた B)</summary>
    public class TitleBarMenu : IDisposable
    {
        readonly PinManager manager;
        readonly Win32Windows api;
        readonly Action<Action> post;
        readonly int selfPid;
        MouseHookThread hook;
        TitlePopup popup;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 8000 };
        IntPtr target, menuWindow;
        Point clickPt;
        DateTime clickAt, menuAt;
        Rectangle menuRect;
        bool menuSeen;
        double scale = 1;
        Size popupSize;

        public TitleBarMenu(PinManager manager, Win32Windows api, Action<Action> post, int selfPid)
        {
            this.manager = manager; this.api = api; this.post = post; this.selfPid = selfPid;
            timer.Tick += (s, e) => Close();
        }

        public bool Enabled { get { return hook != null && hook.Running; } }

        public void SetEnabled(bool on)
        {
            if (on && hook == null)
            {
                hook = new MouseHookThread();
                hook.Post = OnHook;
                if (!hook.Start()) { hook.Dispose(); hook = null; }
            }
            else if (!on && hook != null)
            {
                Close();
                hook.Dispose();
                hook = null;
            }
        }

        void OnHook(int msg, Point pt)
        {
            // フックのスレッド: UI スレッドに渡すだけ
            post(() => OnMouse(msg, pt));
        }

        void OnMouse(int msg, Point pt)
        {
            bool shown = popup != null && popup.Visible;
            switch (msg)
            {
                case MouseHookThread.WM_RBUTTONUP:
                    if (shown && popup.Bounds.Contains(pt)) return;
                    var when = DateTime.Now;
                    ThreadPool.QueueUserWorkItem(_ => Probe(pt, when));
                    break;
                case MouseHookThread.WM_LBUTTONUP:
                    if (shown && popup.Bounds.Contains(pt)) Click();
                    break;
                case MouseHookThread.WM_LBUTTONDOWN:
                case MouseHookThread.WM_RBUTTONDOWN:
                    if (shown && !popup.Bounds.Contains(pt)) Close();
                    break;
                case MouseHookThread.WM_MOUSEMOVE:
                    if (shown) popup.Hover = popup.Bounds.Contains(pt);
                    break;
            }
        }

        /// <summary>ほかのスレッド: 右クリックしたのはタイトルバーか (相手が応答しないとき UI を止めないように)</summary>
        void Probe(Point pt, DateTime when)
        {
            try
            {
                var root = Win32Windows.RootAt(pt);
                if (root == IntPtr.Zero || api.ProcessId(root) == selfPid) return;
                var cap = Win32Windows.IsCaption(root, pt);
                bool elevatedGuess = false;
                if (cap == null)
                {
                    // 聞けなかった: 管理者のウィンドウなら、タイトルバーのあたりかを目安で決めてお知らせだけ出す
                    if (!api.SelfElevated && api.IsElevated(api.ProcessId(root)) && api.InCaptionBand(root, pt)) elevatedGuess = true;
                    else return;
                }
                else if (!cap.Value) return;
                post(() => Open(root, pt, when, elevatedGuess));
            }
            catch (Exception ex) { DataStore.Log("titlebar probe: " + ex.Message); }
        }

        void Open(IntPtr root, Point pt, DateTime when, bool elevatedGuess)
        {
            if (!manager.Settings.TitleBarMenu) return;
            bool pinned = manager.IsPinned(root);
            string why = pinned ? null : (elevatedGuess ? PinManager.MsgElevated : manager.Check(root));
            string label, note;
            bool isMessage = false;
            if (pinned) { label = "📌 固定を外す"; note = "Teal Pin ・ 最前面に固定中"; }
            else if (why == null) { label = "📌 最前面に固定"; note = "Teal Pin"; }
            else if (why == PinManager.MsgElevated) { label = "管理者のウィンドウは固定できません"; note = "Teal Pin ・ 管理者として実行中のアプリ"; isMessage = true; }
            else if (why == PinManager.MsgAlreadyTop) { label = "もともと最前面に表示されています"; note = "Teal Pin ・ アプリ自身の設定"; isMessage = true; }
            else return;   // 自分の画面・デスクトップなど

            if (popup == null) popup = new TitlePopup();
            target = root; clickPt = pt; clickAt = when; menuSeen = false;
            scale = Win32Windows.ScaleAt(pt);
            popupSize = popup.Prepare(label, note, isMessage, scale);
            // メニューがもう開いていれば、その上に。まだならマウスの上に出しておき、開いたら合わせる
            if (menuAt >= when.AddMilliseconds(-200) && NearClick(menuRect)) { menuSeen = true; Place(menuRect); }
            else
            {
                int gap = (int)Math.Round(6 * scale);
                Show(new Rectangle(pt.X, pt.Y - popupSize.Height - gap, popupSize.Width, popupSize.Height));
            }
            timer.Stop(); timer.Start();
        }

        bool NearClick(Rectangle menu)
        {
            if (menu.Width <= 0) return false;
            var near = Rectangle.Inflate(menu, (int)(48 * scale), (int)(48 * scale));
            return near.Contains(clickPt);
        }

        /// <summary>システム メニューの上 (入らなければ下) に、左端をそろえて置く</summary>
        void Place(Rectangle menu)
        {
            int gap = (int)Math.Round(4 * scale);
            var work = Win32Windows.WorkAreaAt(new Point(menu.Left + 1, menu.Top + 1));
            var r = new Rectangle(menu.Left, menu.Top - popupSize.Height - gap, Math.Max(popupSize.Width, 0), popupSize.Height);
            if (work.Width > 0 && r.Top < work.Top) r.Y = menu.Bottom + gap;
            if (work.Width > 0 && r.Bottom > work.Bottom) r = new Rectangle(menu.Right + gap, menu.Top, r.Width, r.Height);
            if (work.Width > 0) r = PresetMath.Fit(r, work);
            Show(r);
        }

        void Show(Rectangle r)
        {
            var work = Win32Windows.WorkAreaAt(clickPt);
            if (work.Width > 0) r = PresetMath.Fit(r, work);
            popup.ShowAt(r, PinNative.HWND_TOPMOST);
            if (hook != null) hook.SetPopup(r);
        }

        /// <summary>メニューのできごと (WinEventSource から)</summary>
        public void OnMenu(uint ev, IntPtr h)
        {
            if (ev == WinEventSource.EVENT_SYSTEM_MENUPOPUPSTART)
            {
                var r = api.WindowRect(h);
                if (r.Width <= 0) return;
                menuRect = r; menuAt = DateTime.Now; menuWindow = h;
                if (popup != null && popup.Visible && NearClick(r) && (menuAt - clickAt).TotalSeconds < 2) { menuSeen = true; Place(r); }
            }
            else if (ev == WinEventSource.EVENT_SYSTEM_MENUEND || ev == WinEventSource.EVENT_SYSTEM_MENUPOPUPEND)
            {
                if (popup != null && popup.Visible && menuSeen) Close();   // メニューを閉じたら一緒に消す
            }
        }

        void Click()
        {
            var h = target;
            bool message = popup != null && popup.IsMessage;
            bool hadMenu = menuSeen;
            var menu = menuWindow;
            Close();
            if (hadMenu)
            {
                // 開いたままのシステム メニューを閉じる
                try
                {
                    if (menu != IntPtr.Zero) PinNative.PostMessage(menu, 0x0100 /* WM_KEYDOWN */, new IntPtr(0x1B) /* VK_ESCAPE */, IntPtr.Zero);
                    PinNative.PostMessage(h, 0x001F /* WM_CANCELMODE */, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
            }
            if (message || h == IntPtr.Zero) return;
            manager.Toggle(h, PinManager.HowTitleBar);
        }

        public void Close()
        {
            timer.Stop();
            if (hook != null) hook.SetPopup(Rectangle.Empty);
            if (popup != null) popup.HideNow();
            menuSeen = false;
        }

        public void Dispose()
        {
            SetEnabled(false);
            timer.Dispose();
            if (popup != null) popup.Dispose();
        }
    }
}
