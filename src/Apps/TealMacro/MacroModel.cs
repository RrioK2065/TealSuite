// Teal Macro (連打＆マクロ) - データ (設定・マクロ) と、画面に関係しない計算
//  保存先: %APPDATA%\TealSuite\TealMacro\
//    settings.json   … 連打の設定・記録の設定など
//    macros.json     … 保存したマクロ (名前・手順・くり返し・速さ)
//    hub-slots.json  … マクロごとのショートカットの一覧 (Teal Hub が読む。macros.json から作り直すのでバックアップには入れない)
//  画面の位置はすべて「物理 px」(モニターごとの DPI に対応した Windows の座標) で持つ
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    /// <summary>マウスのボタン</summary>
    public enum MouseBtn { Left, Right, Middle }

    // ================================================================ 連打の設定
    [DataContract]
    public class TargetPoint
    {
        /// <summary>画面の位置 (物理 px)</summary>
        [DataMember(Name = "x", Order = 1)] public int X;
        [DataMember(Name = "y", Order = 2)] public int Y;
        /// <summary>そのときクリックする場所にあったウィンドウ (exe の名前・クラス・タイトル) と、その中の位置 (クライアント領域の左上から)</summary>
        [DataMember(Name = "exe", Order = 3, EmitDefaultValue = false)] public string Exe;
        [DataMember(Name = "cls", Order = 4, EmitDefaultValue = false)] public string WindowClass;
        [DataMember(Name = "title", Order = 5, EmitDefaultValue = false)] public string Title;
        [DataMember(Name = "rx", Order = 6, EmitDefaultValue = false)] public int RelX;
        [DataMember(Name = "ry", Order = 7, EmitDefaultValue = false)] public int RelY;

        public bool HasWindow { get { return !string.IsNullOrEmpty(Exe) || !string.IsNullOrEmpty(WindowClass); } }
        public Point Screen { get { return new Point(X, Y); } }

        public string Describe(bool windowRelative)
        {
            if (windowRelative && HasWindow)
            {
                var w = string.IsNullOrEmpty(Title) ? (Exe ?? WindowClass) : Title;
                if (w.Length > 28) w = w.Substring(0, 28) + "…";
                return "「" + w + "」の中の " + RelX + ", " + RelY;
            }
            return "画面の " + X + ", " + Y;
        }

        public TargetPoint Clone() { return (TargetPoint)MemberwiseClone(); }
    }

    [DataContract]
    public class ClickerSettings
    {
        public const string ActLeft = "left", ActRight = "right", ActMiddle = "middle", ActDouble = "double", ActKey = "key";
        public const string TargetCursor = "cursor", TargetPoints = "points";
        public const int MinRate = 1, MaxRate = 100, MaxCount = 1000000, MaxSeconds = 86400, MaxPoints = 50;

        /// <summary>何を連打するか (left / right / middle / double / key)</summary>
        [DataMember(Name = "action", Order = 1)] public string Action = ActLeft;
        /// <summary>キーのとき: "Space" / "Enter" / "Ctrl+V" など</summary>
        [DataMember(Name = "key", Order = 2, EmitDefaultValue = false)] public string Key = "Space";
        /// <summary>1 秒に何回</summary>
        [DataMember(Name = "perSecond", Order = 3)] public int PerSecond = 10;
        [DataMember(Name = "stopByCount", Order = 4)] public bool StopByCount;
        [DataMember(Name = "count", Order = 5)] public int Count = 100;
        [DataMember(Name = "stopByTime", Order = 6)] public bool StopByTime;
        [DataMember(Name = "seconds", Order = 7)] public int Seconds = 10;
        /// <summary>cursor (マウスのある場所) / points (決めた位置。2 か所以上なら順番に)</summary>
        [DataMember(Name = "target", Order = 8)] public string Target = TargetCursor;
        /// <summary>決めた位置をウィンドウの中の位置として使う (ウィンドウを動かしてもずれない)</summary>
        [DataMember(Name = "windowRelative", Order = 9)] public bool WindowRelative;
        [DataMember(Name = "points", Order = 10)] public List<TargetPoint> Points = new List<TargetPoint>();
        /// <summary>くわしい設定: ボタン・キーを押している時間 (ミリ秒)</summary>
        [DataMember(Name = "holdMs", Order = 11)] public int HoldMs = 10;
        /// <summary>くわしい設定: 間隔を少し (±20%) ばらつかせる</summary>
        [DataMember(Name = "jitter", Order = 12)] public bool Jitter;
        /// <summary>くわしい設定: 「開始」ボタンで始めたときの待ち時間 (秒)。ショートカットのときはすぐ</summary>
        [DataMember(Name = "startDelay", Order = 13)] public int StartDelaySec = 3;

        public bool IsKey { get { return Action == ActKey; } }
        public bool UsesPoints { get { return !IsKey && Target == TargetPoints; } }
        public double IntervalMs { get { return 1000.0 / Math.Max(MinRate, Math.Min(MaxRate, PerSecond)); } }

        public void Normalize()
        {
            if (Action != ActLeft && Action != ActRight && Action != ActMiddle && Action != ActDouble && Action != ActKey) Action = ActLeft;
            HotkeySpec k;
            if (!KeyText.TryParse(Key, out k)) Key = "Space"; else Key = k.ToString();
            PerSecond = Math.Max(MinRate, Math.Min(MaxRate, PerSecond));
            Count = Math.Max(1, Math.Min(MaxCount, Count));
            Seconds = Math.Max(1, Math.Min(MaxSeconds, Seconds));
            if (Target != TargetCursor && Target != TargetPoints) Target = TargetCursor;
            if (Points == null) Points = new List<TargetPoint>();
            Points.RemoveAll(p => p == null);
            if (Points.Count > MaxPoints) Points.RemoveRange(MaxPoints, Points.Count - MaxPoints);
            HoldMs = Math.Max(0, Math.Min(500, HoldMs));
            StartDelaySec = Math.Max(0, Math.Min(10, StartDelaySec));
        }

        public static string ActionName(string a)
        {
            switch (a)
            {
                case ActRight: return "右クリック";
                case ActMiddle: return "中クリック";
                case ActDouble: return "ダブルクリック";
                case ActKey: return "キー";
                default: return "左クリック";
            }
        }

        /// <summary>「左クリックを 1 秒に 10 回 ・ 100 回で止める」</summary>
        public string Summary()
        {
            HotkeySpec k;
            string what = IsKey ? (KeyText.TryParse(Key, out k) ? KeyText.Display(k) + " キー" : "キー") : ActionName(Action);
            var s = what + "を 1 秒に " + PerSecond + " 回";
            var stops = new List<string>();
            if (StopByCount) stops.Add(Count.ToString("#,0", CultureInfo.InvariantCulture) + " 回");
            if (StopByTime) stops.Add(Fmt_.Seconds(Seconds));
            s += stops.Count == 0 ? " ・ 止めるまで" : " ・ " + string.Join(" か ", stops) + "で止める";
            if (UsesPoints) s += Points.Count == 0 ? " ・ 位置がまだありません" : " ・ " + (Points.Count == 1 ? "決めた位置" : Points.Count + " か所を順番に");
            return s;
        }
    }

    // ================================================================ マクロ
    [DataContract]
    public class PathPoint
    {
        /// <summary>動き始めからのミリ秒</summary>
        [DataMember(Name = "t", Order = 1)] public int T;
        [DataMember(Name = "x", Order = 2)] public int X;
        [DataMember(Name = "y", Order = 3)] public int Y;
    }

    [DataContract]
    public class MacroStep
    {
        public const string KClick = "click", KMove = "move", KDown = "down", KUp = "up", KWheel = "wheel",
            KKey = "key", KKeyDown = "keydown", KKeyUp = "keyup", KText = "text", KWait = "wait";

        [DataMember(Name = "kind", Order = 1)] public string Kind;
        /// <summary>この手順の前に待つミリ秒 (記録したときの間)</summary>
        [DataMember(Name = "delay", Order = 2, EmitDefaultValue = false)] public int Delay;
        [DataMember(Name = "x", Order = 3, EmitDefaultValue = false)] public int X;
        [DataMember(Name = "y", Order = 4, EmitDefaultValue = false)] public int Y;
        /// <summary>left / right / middle</summary>
        [DataMember(Name = "button", Order = 5, EmitDefaultValue = false)] public string Button;
        /// <summary>クリックの回数 (1 / 2 = ダブルクリック)</summary>
        [DataMember(Name = "clicks", Order = 6, EmitDefaultValue = false)] public int Clicks;
        /// <summary>キー ("Ctrl+C" / "Enter" / "A")</summary>
        [DataMember(Name = "key", Order = 7, EmitDefaultValue = false)] public string Key;
        [DataMember(Name = "text", Order = 8, EmitDefaultValue = false)] public string Text;
        /// <summary>待つ手順のミリ秒</summary>
        [DataMember(Name = "ms", Order = 9, EmitDefaultValue = false)] public int Ms;
        /// <summary>ホイール (上が +120)</summary>
        [DataMember(Name = "delta", Order = 10, EmitDefaultValue = false)] public int Delta;
        /// <summary>記録したマウスの動き (なければ X, Y へすぐ動かす)</summary>
        [DataMember(Name = "path", Order = 11, EmitDefaultValue = false)] public List<PathPoint> Path;

        public static MacroStep Click(int x, int y, MouseBtn b, int clicks = 1) { return new MacroStep { Kind = KClick, X = x, Y = y, Button = BtnName(b), Clicks = clicks }; }
        public static MacroStep Move(int x, int y) { return new MacroStep { Kind = KMove, X = x, Y = y }; }
        public static MacroStep KeyPress(string key) { return new MacroStep { Kind = KKey, Key = key }; }
        public static MacroStep TypeText(string text) { return new MacroStep { Kind = KText, Text = text }; }
        public static MacroStep Wait(int ms) { return new MacroStep { Kind = KWait, Ms = ms }; }

        public static string BtnName(MouseBtn b) { return b == MouseBtn.Right ? "right" : b == MouseBtn.Middle ? "middle" : "left"; }
        public MouseBtn Btn { get { return Button == "right" ? MouseBtn.Right : Button == "middle" ? MouseBtn.Middle : MouseBtn.Left; } }
        public static string BtnLabel(MouseBtn b) { return b == MouseBtn.Right ? "右" : b == MouseBtn.Middle ? "中" : "左"; }

        /// <summary>この手順にかかる時間 (ミリ秒。前の待ちは含まない)</summary>
        public int DurationMs
        {
            get
            {
                if (Kind == KWait) return Ms;
                if (Kind == KMove && Path != null && Path.Count > 0) return Path[Path.Count - 1].T;
                return 0;
            }
        }

        public bool IsValid
        {
            get
            {
                switch (Kind)
                {
                    case KClick: case KMove: case KDown: case KUp: case KWheel: case KWait: return true;
                    case KKey: case KKeyDown: case KKeyUp: HotkeySpec k; return KeyText.TryParse(Key, out k);
                    case KText: return !string.IsNullOrEmpty(Text);
                    default: return false;
                }
            }
        }

        /// <summary>一覧に出す名前 (「クリック (左)」「キー Ctrl + C」)</summary>
        public string Title
        {
            get
            {
                HotkeySpec k;
                string key = KeyText.TryParse(Key, out k) ? KeyText.Display(k) : (Key ?? "");
                switch (Kind)
                {
                    case KClick: return (Clicks >= 2 ? "ダブルクリック" : "クリック") + " (" + BtnLabel(Btn) + ")";
                    case KMove: return Path != null && Path.Count > 1 ? "マウスを動かす" : "マウスを移動";
                    case KDown: return "ボタンを押す (" + BtnLabel(Btn) + ")";
                    case KUp: return "ボタンを離す (" + BtnLabel(Btn) + ")";
                    case KWheel: return "ホイール " + (Delta > 0 ? "↑" : "↓") + " " + Math.Max(1, Math.Abs(Delta) / 120);
                    case KKey: return "キー " + key;
                    case KKeyDown: return "キーを押す " + key;
                    case KKeyUp: return "キーを離す " + key;
                    case KText: return "文字を入力";
                    case KWait: return "待つ";
                    default: return Kind ?? "?";
                }
            }
        }

        /// <summary>一覧の 2 つめの欄 (位置・文字・時間)</summary>
        public string Detail
        {
            get
            {
                switch (Kind)
                {
                    case KClick: case KDown: case KUp: case KWheel: return X + ", " + Y;
                    case KMove: return Path != null && Path.Count > 1 ? "→ " + X + ", " + Y + " (" + Fmt_.Ms(DurationMs) + ")" : X + ", " + Y;
                    case KText: var t = (Text ?? "").Replace("\r", "").Replace("\n", "↵"); return "「" + (t.Length > 30 ? t.Substring(0, 30) + "…" : t) + "」";
                    case KWait: return Fmt_.Ms(Ms);
                    default: return "";
                }
            }
        }

        public MacroStep Clone()
        {
            var c = (MacroStep)MemberwiseClone();
            if (Path != null) c.Path = Path.Select(p => new PathPoint { T = p.T, X = p.X, Y = p.Y }).ToList();
            return c;
        }

        public void Normalize()
        {
            Delay = Math.Max(0, Math.Min(MacroDef.MaxWaitMs, Delay));
            Ms = Math.Max(0, Math.Min(MacroDef.MaxWaitMs, Ms));
            if (Kind == KClick) Clicks = Clicks >= 2 ? 2 : 1;
            if (Button != null && Button != "left" && Button != "right" && Button != "middle") Button = "left";
            if (Text != null && Text.Length > MacroDef.MaxText) Text = Text.Substring(0, MacroDef.MaxText);
            HotkeySpec k;
            if (Key != null && KeyText.TryParse(Key, out k)) Key = k.ToString();
            if (Path != null)
            {
                Path.RemoveAll(p => p == null);
                if (Path.Count == 0) Path = null;
                else { for (int i = 1; i < Path.Count; i++) if (Path[i].T < Path[i - 1].T) Path[i].T = Path[i - 1].T; }
            }
            if (Kind == KWheel && Delta == 0) Delta = -120;
        }
    }

    [DataContract]
    public class MacroDef
    {
        public const int MaxSteps = 5000, MaxWaitMs = 3600000, MaxText = 2000, MaxNameLength = 40, MaxRepeat = 100000;
        public static readonly double[] Speeds = { 0.25, 0.5, 1, 2, 4, 8 };

        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        [DataMember(Name = "steps", Order = 3)] public List<MacroStep> Steps = new List<MacroStep>();
        /// <summary>くり返す回数 (0 = 止めるまでずっと)</summary>
        [DataMember(Name = "repeat", Order = 4)] public int Repeat = 1;
        /// <summary>再生の速さ (1 = 記録したとおり、2 = 2 倍速)</summary>
        [DataMember(Name = "speed", Order = 5)] public double Speed = 1;
        [DataMember(Name = "created", Order = 6, EmitDefaultValue = false)] public string Created;

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }
        /// <summary>Teal Hub のショートカットの名前 ("m.1a2b3c4d")</summary>
        public string Slot { get { return "m." + Id; } }

        /// <summary>1 回ぶんの長さ (速さを入れて)</summary>
        public int TotalMs
        {
            get
            {
                long t = Steps.Sum(s => (long)s.Delay + s.DurationMs);
                return (int)Math.Min(int.MaxValue, t / Math.Max(0.01, Speed));
            }
        }

        public string Summary()
        {
            var s = Steps.Count + " 手順";
            if (Steps.Count > 0) s += " ・ " + Fmt_.Ms(TotalMs);
            s += " ・ " + (Repeat == 0 ? "ずっとくり返す" : Repeat == 1 ? "1 回" : Repeat + " 回くり返す");
            if (Speed != 1) s += " ・ " + SpeedName(Speed);
            return s;
        }

        public static string SpeedName(double v)
        {
            if (v == 1) return "記録どおり (1 倍)";
            return (v < 1 ? "ゆっくり " : "速く ") + v.ToString("0.##", CultureInfo.InvariantCulture) + " 倍";
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id) || !HotkeySlots.ValidSlot("m." + Id)) Id = NewId();
            Name = (Name ?? "").Trim();
            if (Name.Length == 0) Name = "マクロ";
            if (Name.Length > MaxNameLength) Name = Name.Substring(0, MaxNameLength);
            if (Steps == null) Steps = new List<MacroStep>();
            Steps.RemoveAll(s => s == null);
            foreach (var s in Steps) s.Normalize();
            Steps.RemoveAll(s => !s.IsValid);
            if (Steps.Count > MaxSteps) Steps.RemoveRange(MaxSteps, Steps.Count - MaxSteps);
            Repeat = Repeat < 0 ? 1 : Math.Min(MaxRepeat, Repeat);
            if (double.IsNaN(Speed) || Speed <= 0) Speed = 1;
            var sp = Speed;
            Speed = Speeds.OrderBy(v => Math.Abs(Math.Log(v / sp))).First();   // 選べる速さのいちばん近いものに
        }

        public MacroDef Clone()
        {
            var c = (MacroDef)MemberwiseClone();
            c.Steps = Steps.Select(s => s.Clone()).ToList();
            return c;
        }
    }

    [DataContract]
    public class MacroFile
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "macros", Order = 2)] public List<MacroDef> Macros = new List<MacroDef>();

        public const int MaxMacros = 100;

        public MacroDef Find(string id) { return Macros.FirstOrDefault(m => m.Id == id); }

        public void Normalize()
        {
            if (Macros == null) Macros = new List<MacroDef>();
            Macros.RemoveAll(m => m == null);
            var seen = new HashSet<string>();
            foreach (var m in Macros) { m.Normalize(); while (!seen.Add(m.Id)) m.Id = MacroDef.NewId(); }
            if (Macros.Count > MaxMacros) Macros.RemoveRange(MaxMacros, Macros.Count - MaxMacros);
        }

        /// <summary>まだ使っていない「マクロ 1」「マクロ 2」…</summary>
        public string NextName()
        {
            for (int i = 1; ; i++)
            {
                var n = "マクロ " + i;
                if (!Macros.Any(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        /// <summary>名前の確認 (OK なら null)</summary>
        public string ValidateName(string name, MacroDef self)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return "名前を入れてください";
            if (n.Length > MacroDef.MaxNameLength) return "名前は " + MacroDef.MaxNameLength + " 文字までです";
            if (Macros.Any(m => m != self && string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase))) return "同じ名前のマクロがあります";
            return null;
        }

        /// <summary>Teal Hub に渡すマクロごとのショートカットの一覧</summary>
        public List<HubSlotDecl> HubSlots()
        {
            return Macros.Select(m => new HubSlotDecl { Slot = m.Slot, Label = "マクロ「" + m.Name + "」を再生 / 停止" }).ToList();
        }
    }

    [DataContract]
    public class MacroSettings
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "clicker", Order = 2)] public ClickerSettings Clicker = new ClickerSettings();
        /// <summary>記録: マウスの動きも記録する (オフならクリックの位置だけ)</summary>
        [DataMember(Name = "recordMoves", Order = 3)] public bool RecordMoves = true;
        /// <summary>マクロの記録・再生のあいだ、この画面を最小化する</summary>
        [DataMember(Name = "hideWhileRunning", Order = 4)] public bool HideWhileRunning = true;
        /// <summary>実行中は Esc キー (本物のキーボード) でも止める</summary>
        [DataMember(Name = "escStops", Order = 5)] public bool EscStops = true;
        /// <summary>「再生」ボタンで始めたときの待ち時間 (秒)</summary>
        [DataMember(Name = "macroDelay", Order = 6)] public int MacroStartDelaySec = 1;
        [DataMember(Name = "selected", Order = 7, EmitDefaultValue = false)] public string SelectedMacro;
        [DataMember(Name = "tab", Order = 8)] public int Tab;
        [DataMember(Name = "showAdvanced", Order = 9)] public bool ShowAdvanced;
        [DataMember(Name = "trayTipShown", Order = 10)] public bool TrayTipShown;

        public void Normalize()
        {
            if (Clicker == null) Clicker = new ClickerSettings();
            Clicker.Normalize();
            MacroStartDelaySec = Math.Max(0, Math.Min(10, MacroStartDelaySec));
            Tab = Tab == 1 ? 1 : 0;
        }
    }

    public static class MacroStore
    {
        public const string SettingsFile = "settings.json", MacrosFile = "macros.json", SlotsFile = "hub-slots.json";

        public static MacroSettings LoadSettings()
        {
            var s = DataStore.LoadQuiet(SettingsFile, () => new MacroSettings());
            s.Normalize();
            return s;
        }

        public static bool SaveSettings(MacroSettings s) { s.Normalize(); return DataStore.TrySave(SettingsFile, s); }

        public static MacroFile LoadMacros()
        {
            var f = DataStore.LoadQuiet(MacrosFile, () => new MacroFile());
            f.Normalize();
            return f;
        }

        public static bool SaveMacros(MacroFile f) { f.Normalize(); return DataStore.TrySave(MacrosFile, f); }

        /// <summary>マクロごとのショートカットの一覧を Teal Hub 用に書き、変わっていれば Teal Hub に知らせる</summary>
        public static bool PublishSlots(MacroFile f)
        {
            if (!HotkeySlots.WriteDynamic(SlotsFile, f.HubSlots())) return false;
            try { HotkeySlots.NotifyHub(); } catch { }
            return true;
        }
    }

    // ================================================================ キーの文字列
    public static class KeyText
    {
        /// <summary>"Space" / "Ctrl+V" / "A" / "Enter" を読む (ショートカットと違い、1 つのキーだけでもよい)</summary>
        public static bool TryParse(string s, out HotkeySpec spec)
        {
            spec = new HotkeySpec();
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            var parts = t.Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (t.EndsWith("+") && (t.Length == 1 || t.EndsWith("++"))) parts.Add("+");
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
            else if (key.Length == 1 && key[0] >= 'a' && key[0] <= 'z') k = Keys.A + (key[0] - 'a');
            else if (key.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsDigit(key[3])) k = Keys.NumPad0 + (key[3] - '0');
            else
            {
                var named = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
                { { ",", Keys.Oemcomma }, { ".", Keys.OemPeriod }, { "-", Keys.OemMinus }, { ";", Keys.Oemplus }, { "Enter", Keys.Return },
                  { "Backspace", Keys.Back }, { "PageDown", Keys.Next }, { "PageUp", Keys.Prior }, { "Esc", Keys.Escape },
                  { "↑", Keys.Up }, { "↓", Keys.Down }, { "←", Keys.Left }, { "→", Keys.Right },
                  { "Ctrl", Keys.ControlKey }, { "Shift", Keys.ShiftKey }, { "Alt", Keys.Menu }, { "Win", Keys.LWin } };
                if (!named.TryGetValue(key, out k))
                {
                    int dummy;
                    if (int.TryParse(key, out dummy) || !Enum.TryParse(key, true, out k) || !Enum.IsDefined(typeof(Keys), k)) return false;
                    if ((k & Keys.Modifiers) != 0 || k == Keys.None) return false;
                }
            }
            spec.Key = k;
            return true;
        }

        public static string Name(Keys k)
        {
            switch (k)
            {
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Shift";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt";
                case Keys.LWin: case Keys.RWin: return "Win";
                default: return HotkeySpec.KeyName(k);
            }
        }

        /// <summary>画面表示用 ("Ctrl + V" / "Space" / "↑")</summary>
        public static string Display(HotkeySpec s)
        {
            if (s.IsEmpty) return "なし";
            var parts = new List<string>();
            if ((s.Mods & HotMods.Ctrl) != 0) parts.Add("Ctrl");
            if ((s.Mods & HotMods.Shift) != 0) parts.Add("Shift");
            if ((s.Mods & HotMods.Alt) != 0) parts.Add("Alt");
            if ((s.Mods & HotMods.Win) != 0) parts.Add("Win");
            parts.Add(Name(s.Key));
            return string.Join(" + ", parts);
        }

        public static string Display(string saved)
        {
            HotkeySpec k;
            return TryParse(saved, out k) ? Display(k) : (saved ?? "");
        }

        /// <summary>キー入力 (KeyDown の keyData) から。修飾キーだけなら空</summary>
        public static HotkeySpec FromKeyData(Keys keyData)
        {
            var spec = new HotkeySpec { Key = keyData & Keys.KeyCode };
            if ((keyData & Keys.Control) != 0) spec.Mods |= HotMods.Ctrl;
            if ((keyData & Keys.Shift) != 0) spec.Mods |= HotMods.Shift;
            if ((keyData & Keys.Alt) != 0) spec.Mods |= HotMods.Alt;
            if (HotkeySpec.IsModifierKey(spec.Key)) spec.Key = Keys.None;
            return spec;
        }

        public static bool IsModifierVk(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C || (vk >= 0xA0 && vk <= 0xA5);
        }

        /// <summary>修飾キーの VK → HotMods (それ以外は None)</summary>
        public static HotMods ModOf(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0xA0: case 0xA1: return HotMods.Shift;
                case 0x11: case 0xA2: case 0xA3: return HotMods.Ctrl;
                case 0x12: case 0xA4: case 0xA5: return HotMods.Alt;
                case 0x5B: case 0x5C: return HotMods.Win;
                default: return HotMods.None;
            }
        }

        /// <summary>HotMods → 押す修飾キーの VK (左)</summary>
        public static List<Keys> ModKeys(HotMods m)
        {
            var l = new List<Keys>();
            if ((m & HotMods.Ctrl) != 0) l.Add(Keys.LControlKey);
            if ((m & HotMods.Shift) != 0) l.Add(Keys.LShiftKey);
            if ((m & HotMods.Alt) != 0) l.Add(Keys.LMenu);
            if ((m & HotMods.Win) != 0) l.Add(Keys.LWin);
            return l;
        }
    }

    // ================================================================ 表示用
    public static class Fmt_
    {
        public static string Ms(int ms)
        {
            if (ms < 1000) return ms + " ミリ秒";
            if (ms < 60000) return (ms / 1000.0).ToString(ms % 1000 == 0 ? "0" : ms % 100 == 0 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " 秒";
            int s = ms / 1000;
            if (s < 3600) return (s / 60) + " 分" + (s % 60 == 0 ? "" : " " + (s % 60) + " 秒");
            return (s / 3600) + " 時間" + (s % 3600 / 60 == 0 ? "" : " " + (s % 3600 / 60) + " 分");
        }

        public static string Seconds(int s) { return Ms(s * 1000); }

        /// <summary>「+0.25 秒」(記録した間)</summary>
        public static string Delay(int ms) { return ms <= 0 ? "" : "+" + Ms(ms); }
    }
}
