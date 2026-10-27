// TealSuite 共通: 全体ショートカット (グローバル ホットキー)
//  - HotkeySpec … "Ctrl+Shift+V" / "Alt+V" のような表現と、使える組み合わせかの検証
//  - GlobalHotkey … RegisterHotKey で登録し、押されたら知らせる見えないウィンドウ
//  1.6.0 から登録するのは Teal Hub だけ (TealHub/HubShortcuts.cs)。押されたらアプリに Hotkey イベントで知らせる
//  「ショートカットなし」も正しい設定: 保存値 "none" (NoneText)。保存値がない (null) ときだけ初期設定を使う
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TealSuite.Common
{
    [Flags]
    public enum HotMods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

    public struct HotkeySpec
    {
        public HotMods Mods;
        public Keys Key;

        public static HotkeySpec Of(HotMods mods, Keys key) { return new HotkeySpec { Mods = mods, Key = key }; }

        public bool IsEmpty { get { return Key == Keys.None; } }
        /// <summary>「ショートカットなし」</summary>
        public static readonly HotkeySpec None = new HotkeySpec();
        /// <summary>「なし」を保存するときの文字列 (空や null は「まだ保存していない = 初期設定」と区別する)</summary>
        public const string NoneText = "none";

        /// <summary>保存する文字列 ("Alt+V" / なしなら "none")</summary>
        public string ToSaved() { return IsEmpty ? NoneText : ToString(); }

        /// <summary>
        /// 保存値を読む。null・空 = まだ保存していない → 初期設定。"none" = なし。読めない値 → 初期設定。
        /// </summary>
        public static HotkeySpec FromSaved(string saved, HotkeySpec defaultSpec)
        {
            if (string.IsNullOrWhiteSpace(saved)) return defaultSpec;
            if (IsNoneText(saved)) return None;
            HotkeySpec hk;
            return TryParse(saved, out hk) ? hk : defaultSpec;
        }

        public static bool IsNoneText(string s)
        {
            if (s == null) return false;
            var t = s.Trim();
            return t.Equals(NoneText, StringComparison.OrdinalIgnoreCase) || t == "なし";
        }

        /// <summary>画面表示用。なしなら「なし」</summary>
        public string DisplayOrNone { get { return IsEmpty ? "なし" : Display; } }
        public bool SameAs(HotkeySpec o) { return Mods == o.Mods && Key == o.Key; }

        public override string ToString()
        {
            if (IsEmpty) return "";
            var parts = new List<string>();
            if ((Mods & HotMods.Ctrl) != 0) parts.Add("Ctrl");
            if ((Mods & HotMods.Shift) != 0) parts.Add("Shift");
            if ((Mods & HotMods.Alt) != 0) parts.Add("Alt");
            if ((Mods & HotMods.Win) != 0) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        /// <summary>画面表示用 ("Ctrl + Shift + V")</summary>
        public string Display { get { return ToString().Replace("+", " + "); } }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return ";";       // JIS 配列では ; キー
                case Keys.Space: return "Space";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                case Keys.Escape: return "Esc";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                default: return k.ToString();
            }
        }

        /// <summary>文字列を読む。使えない組み合わせ (Validate が理由を返すもの) も false。</summary>
        public static bool TryParse(string s, out HotkeySpec spec)
        {
            spec = new HotkeySpec();
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (s.TrimEnd().EndsWith("++")) parts.Add("+");
            if (parts.Count == 0) return false;
            for (int i = 0; i < parts.Count - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": spec.Mods |= HotMods.Ctrl; break;
                    case "shift": spec.Mods |= HotMods.Shift; break;
                    case "alt": spec.Mods |= HotMods.Alt; break;
                    case "win": case "windows": spec.Mods |= HotMods.Win; break;
                    default: return false;
                }
            }
            var key = parts[parts.Count - 1];
            Keys k;
            if (key.Length == 1 && char.IsDigit(key[0])) k = Keys.D0 + (key[0] - '0');
            else if (key.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsDigit(key[3])) k = Keys.NumPad0 + (key[3] - '0');
            else
            {
                var named = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
                { { ",", Keys.Oemcomma }, { ".", Keys.OemPeriod }, { "-", Keys.OemMinus }, { ";", Keys.Oemplus }, { "Enter", Keys.Return },
                  { "Backspace", Keys.Back }, { "PageDown", Keys.Next }, { "PageUp", Keys.Prior }, { "Esc", Keys.Escape } };
                if (!named.TryGetValue(key, out k))
                {
                    int dummy;
                    if (int.TryParse(key, out dummy) || !Enum.TryParse(key, true, out k)) return false;
                }
            }
            spec.Key = k;
            return Validate(spec) == null;
        }

        public static bool IsModifierKey(Keys k)
        {
            return k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin
                || k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LMenu || k == Keys.RMenu;
        }

        static bool IsLetter(Keys k) { return k >= Keys.A && k <= Keys.Z; }

        // Ctrl + これらは、ほとんどのアプリでコピー・貼り付けなどに使われている
        static readonly Keys[] CtrlReserved = { Keys.A, Keys.C, Keys.V, Keys.X, Keys.Y, Keys.Z, Keys.S };

        /// <summary>
        /// 使えない組み合わせなら理由を返す (OK なら null)。
        /// ・Ctrl / Alt / Win のどれかが必要 (F キーだけは単独でも可)。文字だけ・Shift+文字は普段の入力と重なるので不可
        /// ・Alt + 文字 (例: Alt+V) と Ctrl + 文字は 1 つの修飾キーでも可。ただし下の「よく使う操作」は不可
        /// ・Ctrl+C/V/X/Z/Y/A/S、Alt+F4/Tab/Space/Esc、Ctrl+Esc、Ctrl+Shift+Esc、Ctrl+Alt+Delete、Win+L は不可
        /// </summary>
        public static string Validate(HotkeySpec s)
        {
            if (s.IsEmpty || IsModifierKey(s.Key)) return "Ctrl・Alt・Win などと一緒に、文字や数字のキーを押してください。";
            bool fkey = s.Key >= Keys.F1 && s.Key <= Keys.F24;
            bool hasMain = (s.Mods & (HotMods.Ctrl | HotMods.Alt | HotMods.Win)) != 0;
            if (!fkey && !hasMain) return "Ctrl・Alt・Win のどれかを含めてください (文字キーだけだと普段の入力と重なります)。";
            if (s.Mods == HotMods.Ctrl && CtrlReserved.Contains(s.Key))
                return "Ctrl + " + KeyName(s.Key) + " はコピー・貼り付けなどでよく使う操作なので割り当てられません。";
            if (s.Mods == HotMods.Alt && (s.Key == Keys.F4 || s.Key == Keys.Tab || s.Key == Keys.Space || s.Key == Keys.Escape))
                return "Alt + " + KeyName(s.Key) + " は Windows の操作で使われています。";
            if (s.Key == Keys.Escape && (s.Mods == HotMods.Ctrl || s.Mods == (HotMods.Ctrl | HotMods.Shift)))
                return s.Display + " は Windows の操作で使われています。";
            if (s.Key == Keys.Delete && (s.Mods & (HotMods.Ctrl | HotMods.Alt)) == (HotMods.Ctrl | HotMods.Alt))
                return "Ctrl + Alt + Delete は Windows の操作で使われています。";
            if (s.Mods == HotMods.Win && s.Key == Keys.L) return "Win + L は Windows のロックで使われています。";
            return null;
        }

        /// <summary>使えるが気を付けたほうがよい組み合わせなら一言 (なければ null)</summary>
        public static string Caution(HotkeySpec s)
        {
            if (s.Mods == HotMods.Alt && IsLetter(s.Key))
                return "Alt + 文字は、ほかのアプリのメニュー操作 (Alt + " + KeyName(s.Key) + ") より優先されます。";
            if (s.Mods == HotMods.None) return "F キー単独は、ほかのアプリの F キー操作より優先されます。";
            return null;
        }

        public static HotkeySpec FromKeyEvent(Keys keyData, bool winDown)
        {
            var spec = new HotkeySpec { Key = keyData & Keys.KeyCode };
            if ((keyData & Keys.Control) != 0) spec.Mods |= HotMods.Ctrl;
            if ((keyData & Keys.Shift) != 0) spec.Mods |= HotMods.Shift;
            if ((keyData & Keys.Alt) != 0) spec.Mods |= HotMods.Alt;
            if (winDown) spec.Mods |= HotMods.Win;
            if (IsModifierKey(spec.Key)) spec.Key = Keys.None;
            return spec;
        }
    }

    /// <summary>RegisterHotKey 用の見えないメッセージ専用ウィンドウ (1 つのショートカット)</summary>
    public class GlobalHotkey : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312, HotkeyId = 0xB001;
        const uint MOD_NOREPEAT = 0x4000;
        const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        /// <summary>テスト用 (Windows 以外): 登録の結果を差し替える。null を返せば成功、文字列ならその理由で失敗。</summary>
        public static Func<HotkeySpec, string> FakeRegister;

        public event Action Pressed;
        bool registered;
        public bool Active { get { return registered; } }

        public GlobalHotkey()
        {
            if (!AppInfo.IsWindows) return; // テスト環境 (mono) ではウィンドウを作らない
            CreateHandle(new CreateParams { Caption = "TealSuite." + AppInfo.Id + ".Hotkey", Parent = HWND_MESSAGE });
        }

        public static string ConflictMessage(HotkeySpec spec)
        {
            return spec.Display + " はほかのアプリがすでに使っています。別の組み合わせを選んでください。";
        }

        /// <summary>登録し直す。失敗したら理由 (null なら成功)。</summary>
        public string Register(HotkeySpec spec)
        {
            Unregister();
            if (!AppInfo.IsWindows)
            {
                var fake = FakeRegister != null ? FakeRegister(spec) : null;
                registered = fake == null;
                return fake;
            }
            if (!RegisterHotKey(Handle, HotkeyId, (uint)spec.Mods | MOD_NOREPEAT, (uint)spec.Key))
            {
                int err = Marshal.GetLastWin32Error();
                return err == ERROR_HOTKEY_ALREADY_REGISTERED ? ConflictMessage(spec) : spec.Display + " を登録できませんでした (エラー " + err + ")。";
            }
            registered = true;
            return null;
        }

        public void Unregister()
        {
            if (registered && AppInfo.IsWindows && Handle != IntPtr.Zero) UnregisterHotKey(Handle, HotkeyId);
            registered = false;
        }

        /// <summary>テスト用: ショートカットが押されたことにする</summary>
        public void SimulatePress() { if (Pressed != null) Pressed(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) { if (Pressed != null) Pressed(); return; }
            base.WndProc(ref m);
        }

        public static bool WinKeyDown()
        {
            if (!AppInfo.IsWindows) return false;
            try { return (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0; } catch { return false; }
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
