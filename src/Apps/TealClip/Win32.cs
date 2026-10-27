// Teal Clip (コピー履歴) - Windows API (クリップボード監視 / キー送信)
// (全体ショートカットは共通の Common/Hotkey.cs)
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Clip
{
    static class W32
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr hWnd);

        // ---- SendInput の構造体 (x86 / x64 どちらでも正しいサイズになるよう union を明示) ----
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        public static INPUT Key(ushort vk, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.U.ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 };
            return i;
        }
    }

    /// <summary>
    /// 見えないメッセージ専用ウィンドウ。クリップボードの更新通知を受け取る。
    /// </summary>
    public class ClipboardWatcher : NativeWindow, IDisposable
    {
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        public event Action ClipboardChanged;
        bool listening;

        public ClipboardWatcher()
        {
            if (!AppInfo.IsWindows) return; // テスト環境 (mono) では何もしない
            CreateHandle(new CreateParams { Caption = "TealSuite.Clip.Watcher", Parent = HWND_MESSAGE });
            listening = W32.AddClipboardFormatListener(Handle);
            if (!listening) DataStore.Log("AddClipboardFormatListener failed: " + Marshal.GetLastWin32Error());
        }

        public bool IsListening { get { return listening; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == W32.WM_CLIPBOARDUPDATE) { if (ClipboardChanged != null) ClipboardChanged(); m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (listening) W32.RemoveClipboardFormatListener(Handle);
            listening = false;
            DestroyHandle();
        }
    }

    /// <summary>元のウィンドウに戻して Ctrl+V を送る</summary>
    public static class Paster
    {
        const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_V = 0x56, VK_NONAME = 0xE8;

        public static bool Restore(IntPtr target)
        {
            if (target == IntPtr.Zero || !W32.IsWindow(target)) return false;
            if (W32.SetForegroundWindow(target)) return WaitForeground(target);
            // 失敗したら入力キューを一時的につないで再試行
            uint pid;
            uint fg = W32.GetWindowThreadProcessId(W32.GetForegroundWindow(), out pid);
            uint me = W32.GetCurrentThreadId();
            bool attached = fg != me && W32.AttachThreadInput(me, fg, true);
            try { W32.SetForegroundWindow(target); }
            finally { if (attached) W32.AttachThreadInput(me, fg, false); }
            return WaitForeground(target);
        }

        static bool WaitForeground(IntPtr target)
        {
            for (int i = 0; i < 30; i++)
            {
                if (W32.GetForegroundWindow() == target) return true;
                Thread.Sleep(10);
            }
            return W32.GetForegroundWindow() == target;
        }

        static bool Down(int vk) { return (W32.GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static void SendCtrlV()
        {
            var inputs = new System.Collections.Generic.List<W32.INPUT>();
            bool alt = Down(VK_MENU), win = Down(VK_LWIN) || Down(VK_RWIN);
            // Alt / Win を離すだけだとメニューやスタートが開くので、意味のないキーを挟む
            if (alt || win) { inputs.Add(W32.Key(VK_NONAME, false)); inputs.Add(W32.Key(VK_NONAME, true)); }
            if (Down(VK_SHIFT)) inputs.Add(W32.Key(VK_SHIFT, true));
            if (alt) inputs.Add(W32.Key(VK_MENU, true));
            if (Down(VK_LWIN)) inputs.Add(W32.Key(VK_LWIN, true));
            if (Down(VK_RWIN)) inputs.Add(W32.Key(VK_RWIN, true));
            inputs.Add(W32.Key(VK_CONTROL, false));
            inputs.Add(W32.Key(VK_V, false));
            inputs.Add(W32.Key(VK_V, true));
            inputs.Add(W32.Key(VK_CONTROL, true));
            var arr = inputs.ToArray();
            uint sent = W32.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(W32.INPUT)));
            if (sent != arr.Length) DataStore.Log("SendInput sent " + sent + "/" + arr.Length + " err " + Marshal.GetLastWin32Error());
        }
    }
}
