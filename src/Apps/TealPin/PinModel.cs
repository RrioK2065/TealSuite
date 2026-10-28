// Teal Pin (最前面に固定) - 設定・プリセット・自動で固定するルール (画面や Win32 に頼らない部分。mono でもテストできる)
//  - PinSettings  … settings.json (%APPDATA%\TealSuite\TealPin\)
//  - PinPreset    … 位置とサイズ。「隅に決まった大きさ」と「作業領域に対する割合」の 2 種類
//  - PinRule      … exe 名 (+ タイトルに含む文字) が合うウィンドウが出てきたら自動で固定
//  - PresetMath / BorderMath / RuleMatcher … 計算だけ (モニターの作業領域・DPI の倍率を引数でもらう)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Pin
{
    [DataContract]
    public class PinPreset
    {
        public const string KindCorner = "corner";   // 隅 (または中央) に、決まった大きさ (96 dpi のときの px)
        public const string KindArea = "area";       // 作業領域に対する割合 (0〜1)

        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        [DataMember(Name = "kind", Order = 3)] public string Kind = KindArea;
        /// <summary>corner のとき: "tl" "tr" "bl" "br" "c"</summary>
        [DataMember(Name = "anchor", Order = 4, EmitDefaultValue = false)] public string Anchor;
        [DataMember(Name = "width", Order = 5, EmitDefaultValue = false)] public int Width;
        [DataMember(Name = "height", Order = 6, EmitDefaultValue = false)] public int Height;
        [DataMember(Name = "x", Order = 7)] public double X;
        [DataMember(Name = "y", Order = 8)] public double Y;
        [DataMember(Name = "w", Order = 9)] public double W;
        [DataMember(Name = "h", Order = 10)] public double H;
        [OnDeserializing] void Defaults(StreamingContext c) { Kind = KindArea; }
        /// <summary>はじめから入っているもの (保存しない・削除できない)</summary>
        [IgnoreDataMember] public bool BuiltIn;

        public override string ToString() { return Name; }

        /// <summary>一覧に出す説明</summary>
        public string Summary
        {
            get
            {
                if (Kind == KindCorner)
                    return AnchorName(Anchor) + "に " + Width + " × " + Height + " (画面の拡大率に合わせて広げます)";
                return "画面の 左から " + Pct(X) + "・上から " + Pct(Y) + " に、幅 " + Pct(W) + " × 高さ " + Pct(H);
            }
        }

        static string Pct(double v) { return Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%"; }

        public static string AnchorName(string a)
        {
            switch (a)
            {
                case "tl": return "左上";
                case "tr": return "右上";
                case "bl": return "左下";
                case "br": return "右下";
                default: return "中央";
            }
        }
    }

    [DataContract]
    public class PinRule
    {
        [DataMember(Name = "id", Order = 1)] public string Id;
        /// <summary>exe の名前 (例: notepad.exe。* で何文字でも)</summary>
        [DataMember(Name = "exe", Order = 2)] public string Exe;
        /// <summary>空でなければ、タイトルにこの文字を含むときだけ</summary>
        [DataMember(Name = "title", Order = 3, EmitDefaultValue = false)] public string TitleContains;
        /// <summary>固定したあとに使うプリセット (なければ位置はそのまま)</summary>
        [DataMember(Name = "preset", Order = 4, EmitDefaultValue = false)] public string PresetId;
        [DataMember(Name = "enabled", Order = 5)] public bool Enabled = true;
        [OnDeserializing] void Defaults(StreamingContext c) { Enabled = true; }

        public string Describe()
        {
            var s = RuleMatcher.NormalizeExe(Exe);
            if (!string.IsNullOrEmpty(TitleContains)) s += " ・ タイトルに「" + TitleContains + "」";
            return s;
        }
    }

    [DataContract]
    public class PinSettings
    {
        public const int CurrentVersion = 1;
        public const int MinBorder = 1, MaxBorder = 8, DefaultBorder = 3;
        public const int MaxRules = 50, MaxPresets = 30;

        [DataMember(Name = "version", Order = 1)] public int Version = CurrentVersion;
        /// <summary>タイトルバーの右クリックで「📌 最前面に固定」を出す</summary>
        [DataMember(Name = "titleBarMenu", Order = 2)] public bool TitleBarMenu = true;
        /// <summary>固定したウィンドウにティールの枠線</summary>
        [DataMember(Name = "border", Order = 3)] public bool ShowBorder = true;
        /// <summary>枠線の太さ (96 dpi のときの px。画面の拡大率に合わせて太くする)</summary>
        [DataMember(Name = "borderWidth", Order = 4)] public int BorderWidth = DefaultBorder;
        /// <summary>固定・解除したときに小さなお知らせを出す</summary>
        [DataMember(Name = "toast", Order = 5)] public bool Toast = true;
        /// <summary>ほかのアプリが最前面を外したら、固定し直す</summary>
        [DataMember(Name = "keepTopmost", Order = 6)] public bool KeepTopmost = true;
        /// <summary>起動したとき (とルールを変えたとき)、すでに開いているウィンドウにもルールを使う</summary>
        [DataMember(Name = "rulesOnStart", Order = 7)] public bool RulesOnStart = true;
        [DataMember(Name = "rules", Order = 8)] public List<PinRule> Rules = new List<PinRule>();
        /// <summary>自分で保存したプリセット (はじめから入っているものは保存しない)</summary>
        [DataMember(Name = "presets", Order = 9)] public List<PinPreset> Presets = new List<PinPreset>();
        [DataMember(Name = "trayTipShown", Order = 10)] public bool TrayTipShown;

        // ファイルにない項目も初期値にする (DataContract はコンストラクターを呼ばないため)
        [OnDeserializing] void Defaults(StreamingContext c)
        {
            Version = CurrentVersion; TitleBarMenu = true; ShowBorder = true; BorderWidth = DefaultBorder;
            Toast = true; KeepTopmost = true; RulesOnStart = true;
            Rules = new List<PinRule>(); Presets = new List<PinPreset>();
        }

        public void Normalize()
        {
            if (Version < 1) Version = 1;
            if (BorderWidth < MinBorder || BorderWidth > MaxBorder) BorderWidth = BorderWidth <= 0 ? DefaultBorder : Math.Max(MinBorder, Math.Min(MaxBorder, BorderWidth));
            if (Rules == null) Rules = new List<PinRule>();
            if (Presets == null) Presets = new List<PinPreset>();
            Rules.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Exe));
            foreach (var r in Rules)
            {
                if (string.IsNullOrEmpty(r.Id)) r.Id = NewId();
                r.Exe = RuleMatcher.NormalizeExe(r.Exe);
                r.TitleContains = string.IsNullOrWhiteSpace(r.TitleContains) ? null : r.TitleContains.Trim();
                if (string.IsNullOrWhiteSpace(r.PresetId)) r.PresetId = null;
            }
            var builtIn = new HashSet<string>(PresetMath.BuiltIns().Select(b => b.Id));
            Presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name) || builtIn.Contains(p.Id ?? ""));
            foreach (var p in Presets)
            {
                if (string.IsNullOrEmpty(p.Id) || !p.Id.StartsWith("u-")) p.Id = "u-" + NewId();
                p.Name = p.Name.Trim();
                p.BuiltIn = false;
                PresetMath.Clean(p);
            }
            // 同じ ID が 2 つあれば後ろを付け直す
            var seen = new HashSet<string>();
            foreach (var p in Presets) { while (!seen.Add(p.Id)) p.Id = "u-" + NewId(); }
            seen.Clear();
            foreach (var r in Rules) { while (!seen.Add(r.Id)) r.Id = NewId(); }
            if (Rules.Count > MaxRules) Rules.RemoveRange(MaxRules, Rules.Count - MaxRules);
            if (Presets.Count > MaxPresets) Presets.RemoveRange(MaxPresets, Presets.Count - MaxPresets);
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 10); }

        /// <summary>はじめから入っているもの + 自分で保存したもの</summary>
        public List<PinPreset> AllPresets() { return PresetMath.BuiltIns().Concat(Presets).ToList(); }

        public PinPreset FindPreset(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return AllPresets().FirstOrDefault(p => p.Id == id);
        }
    }

    public static class PinStore
    {
        public const string FileName = "settings.json";
        public const string RecoveryFile = "pinned.json";

        public static PinSettings Load()
        {
            var s = DataStore.LoadQuiet(FileName, () => new PinSettings());
            s.Normalize();
            return s;
        }

        public static bool Save(PinSettings s) { s.Normalize(); return DataStore.TrySave(FileName, s); }
    }

    // ---------------------------------------------------------------- プリセットの計算
    public static class PresetMath
    {
        /// <summary>小窓と画面の端のすきま (96 dpi のときの px)</summary>
        public const int Margin = 16;
        /// <summary>小さくなりすぎないように (96 dpi のときの px)</summary>
        public const int MinSize = 160;

        public static List<PinPreset> BuiltIns()
        {
            return new List<PinPreset>
            {
                new PinPreset { Id = "b-br", Name = "右下の小窓", Kind = PinPreset.KindCorner, Anchor = "br", Width = 480, Height = 320, BuiltIn = true },
                new PinPreset { Id = "b-tr", Name = "右上の小窓", Kind = PinPreset.KindCorner, Anchor = "tr", Width = 480, Height = 320, BuiltIn = true },
                new PinPreset { Id = "b-bl", Name = "左下の小窓", Kind = PinPreset.KindCorner, Anchor = "bl", Width = 480, Height = 320, BuiltIn = true },
                new PinPreset { Id = "b-left", Name = "左半分", Kind = PinPreset.KindArea, X = 0, Y = 0, W = 0.5, H = 1, BuiltIn = true },
                new PinPreset { Id = "b-right", Name = "右半分", Kind = PinPreset.KindArea, X = 0.5, Y = 0, W = 0.5, H = 1, BuiltIn = true },
                new PinPreset { Id = "b-center", Name = "中央 (広め)", Kind = PinPreset.KindArea, X = 0.15, Y = 0.1, W = 0.7, H = 0.8, BuiltIn = true },
            };
        }

        static double Clamp01(double v) { return double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v)); }

        /// <summary>読み込んだ値を使える範囲に直す</summary>
        public static void Clean(PinPreset p)
        {
            if (p.Kind != PinPreset.KindCorner) p.Kind = PinPreset.KindArea;
            if (p.Kind == PinPreset.KindCorner)
            {
                if (!new[] { "tl", "tr", "bl", "br", "c" }.Contains(p.Anchor)) p.Anchor = "br";
                p.Width = Math.Max(MinSize, Math.Min(10000, p.Width <= 0 ? 480 : p.Width));
                p.Height = Math.Max(MinSize / 2, Math.Min(10000, p.Height <= 0 ? 320 : p.Height));
                return;
            }
            p.X = Clamp01(p.X); p.Y = Clamp01(p.Y);
            p.W = Clamp01(p.W); p.H = Clamp01(p.H);
            if (p.W < 0.05) p.W = 0.05;
            if (p.H < 0.05) p.H = 0.05;
            if (p.X + p.W > 1) p.X = Math.Max(0, 1 - p.W);
            if (p.Y + p.H > 1) p.Y = Math.Max(0, 1 - p.H);
        }

        /// <summary>
        /// プリセットの場所 (見えている枠の位置と大きさ)。work はウィンドウがあるモニターの作業領域 (タスクバーを除く)、
        /// scale はそのモニターの拡大率 (96 dpi = 1.0)。結果は必ず作業領域の中に収まる。
        /// </summary>
        public static Rectangle Compute(PinPreset p, Rectangle work, double scale)
        {
            if (p == null || work.Width <= 0 || work.Height <= 0) return Rectangle.Empty;
            if (scale <= 0 || double.IsNaN(scale)) scale = 1;
            int minW = Math.Min(work.Width, (int)Math.Round(MinSize * scale)), minH = Math.Min(work.Height, (int)Math.Round(MinSize / 2 * scale));
            if (p.Kind == PinPreset.KindCorner)
            {
                int m = (int)Math.Round(Margin * scale);
                int w = (int)Math.Round(p.Width * scale), h = (int)Math.Round(p.Height * scale);
                w = Math.Max(minW, Math.Min(w, work.Width - 2 * m));
                h = Math.Max(minH, Math.Min(h, work.Height - 2 * m));
                if (w > work.Width) w = work.Width;
                if (h > work.Height) h = work.Height;
                int mx = Math.Max(0, Math.Min(m, (work.Width - w) / 2)), my = Math.Max(0, Math.Min(m, (work.Height - h) / 2));
                int x, y;
                switch (p.Anchor)
                {
                    case "tl": x = work.Left + mx; y = work.Top + my; break;
                    case "tr": x = work.Right - w - mx; y = work.Top + my; break;
                    case "bl": x = work.Left + mx; y = work.Bottom - h - my; break;
                    case "br": x = work.Right - w - mx; y = work.Bottom - h - my; break;
                    default: x = work.Left + (work.Width - w) / 2; y = work.Top + (work.Height - h) / 2; break;
                }
                return new Rectangle(x, y, w, h);
            }
            double fx = Clamp01(p.X), fy = Clamp01(p.Y), fw = Clamp01(p.W), fh = Clamp01(p.H);
            int left = work.Left + (int)Math.Round(fx * work.Width);
            int top = work.Top + (int)Math.Round(fy * work.Height);
            int right = work.Left + (int)Math.Round(Math.Min(1, fx + fw) * work.Width);
            int bottom = work.Top + (int)Math.Round(Math.Min(1, fy + fh) * work.Height);
            var r = Rectangle.FromLTRB(left, top, right, bottom);
            if (r.Width < minW) r.Width = minW;
            if (r.Height < minH) r.Height = minH;
            return Fit(r, work);
        }

        /// <summary>作業領域の中に入るように動かす (大きすぎれば縮める)</summary>
        public static Rectangle Fit(Rectangle r, Rectangle work)
        {
            int w = Math.Min(r.Width, work.Width), h = Math.Min(r.Height, work.Height);
            int x = Math.Max(work.Left, Math.Min(r.Left, work.Right - w));
            int y = Math.Max(work.Top, Math.Min(r.Top, work.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        /// <summary>今のウィンドウの位置から、作業領域に対する割合のプリセットを作る (ほかのモニターでも同じ割合で使える)</summary>
        public static PinPreset FromRect(string name, Rectangle frame, Rectangle work)
        {
            var p = new PinPreset { Id = "u-" + PinSettings.NewId(), Name = (name ?? "").Trim(), Kind = PinPreset.KindArea };
            if (work.Width <= 0 || work.Height <= 0) { p.W = p.H = 0.5; return p; }
            var r = Rectangle.Intersect(frame, work);
            if (r.Width <= 0 || r.Height <= 0) r = Fit(frame, work);
            p.X = Math.Round((r.Left - work.Left) / (double)work.Width, 4);
            p.Y = Math.Round((r.Top - work.Top) / (double)work.Height, 4);
            p.W = Math.Round(r.Width / (double)work.Width, 4);
            p.H = Math.Round(r.Height / (double)work.Height, 4);
            Clean(p);
            return p;
        }

        /// <summary>
        /// 見えている枠 (DWM の範囲) を desired にしたいときに SetWindowPos に渡す範囲。
        /// Windows 10/11 のウィンドウには、見えない「つかむための縁」が左右と下にあるので、その分を足す。
        /// </summary>
        public static Rectangle ToWindowRect(Rectangle desiredFrame, Rectangle windowRect, Rectangle frameBounds)
        {
            if (frameBounds.Width <= 0 || frameBounds.Height <= 0) return desiredFrame;
            int l = Math.Max(0, frameBounds.Left - windowRect.Left), t = Math.Max(0, frameBounds.Top - windowRect.Top);
            int r = Math.Max(0, windowRect.Right - frameBounds.Right), b = Math.Max(0, windowRect.Bottom - frameBounds.Bottom);
            if (l > 64 || t > 64 || r > 64 || b > 64) return desiredFrame;   // おかしな値 (最小化中など) は使わない
            return Rectangle.FromLTRB(desiredFrame.Left - l, desiredFrame.Top - t, desiredFrame.Right + r, desiredFrame.Bottom + b);
        }

        /// <summary>いちばん多く重なっているモニター (重ならなければ中心がいちばん近いもの)。なければ -1</summary>
        public static int PickMonitor(Rectangle window, IList<Rectangle> monitors)
        {
            if (monitors == null || monitors.Count == 0) return -1;
            int best = -1; long bestArea = 0;
            for (int i = 0; i < monitors.Count; i++)
            {
                var x = Rectangle.Intersect(window, monitors[i]);
                long a = x.Width > 0 && x.Height > 0 ? (long)x.Width * x.Height : 0;
                if (a > bestArea) { bestArea = a; best = i; }
            }
            if (best >= 0) return best;
            var c = new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);
            double bestD = double.MaxValue;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                double dx = Math.Max(m.Left - c.X, Math.Max(0, c.X - m.Right)), dy = Math.Max(m.Top - c.Y, Math.Max(0, c.Y - m.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static string ValidateName(string name, IEnumerable<PinPreset> all, PinPreset self)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return "名前を入れてください";
            if (n.Length > 40) return "名前は 40 文字までにしてください";
            if (all.Any(p => p != self && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase))) return "同じ名前のプリセットがあります";
            return null;
        }
    }

    // ---------------------------------------------------------------- 枠線の計算
    public static class BorderMath
    {
        /// <summary>
        /// 枠線の 4 本 (上・下・左・右) の範囲。ふだんはウィンドウの外側に描き、外側に場所がない辺
        /// (最大化・画面の端にぴったり付けたとき) だけ内側に描く。area は「はみ出さない範囲」(モニターの作業領域)。
        /// 小さすぎるウィンドウ・空の範囲は空の配列。
        /// </summary>
        public static Rectangle[] Strips(Rectangle frame, Rectangle area, int t)
        {
            if (t <= 0 || frame.Width <= 2 * t || frame.Height <= 2 * t) return new Rectangle[0];
            bool noArea = area.Width <= 0 || area.Height <= 0;
            int left = noArea || frame.Left - t >= area.Left ? frame.Left - t : frame.Left;
            int top = noArea || frame.Top - t >= area.Top ? frame.Top - t : frame.Top;
            int right = noArea || frame.Right + t <= area.Right ? frame.Right + t : frame.Right;
            int bottom = noArea || frame.Bottom + t <= area.Bottom ? frame.Bottom + t : frame.Bottom;
            int innerH = bottom - top - 2 * t;
            if (innerH <= 0) return new Rectangle[0];
            return new[]
            {
                new Rectangle(left, top, right - left, t),           // 上
                new Rectangle(left, bottom - t, right - left, t),    // 下
                new Rectangle(left, top + t, t, innerH),             // 左
                new Rectangle(right - t, top + t, t, innerH),        // 右
            };
        }

        /// <summary>拡大率に合わせた太さ (最低 1px)</summary>
        public static int Thickness(int logical, double scale)
        {
            if (scale <= 0 || double.IsNaN(scale)) scale = 1;
            return Math.Max(1, (int)Math.Round(logical * scale));
        }
    }

    // ---------------------------------------------------------------- ルール
    public static class RuleMatcher
    {
        /// <summary>exe の名前にそろえる (パスなら名前だけ・小文字・拡張子がなければ .exe)</summary>
        public static string NormalizeExe(string exe)
        {
            var s = (exe ?? "").Trim().Trim('"');
            if (s.Length == 0) return "";
            int slash = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
            if (slash >= 0) s = s.Substring(slash + 1);
            s = s.ToLowerInvariant();
            // Path.GetExtension は Windows で < > | などを Illegal characters in path にする。確認は Validate が返す。
            if (!s.EndsWith("*") && ExtensionOf(s).Length == 0) s += ".exe";
            return s;
        }

        static string ExtensionOf(string s)
        {
            int dot = s.LastIndexOf('.');
            if (dot <= 0 || dot == s.Length - 1) return "";
            return s.Substring(dot);
        }

        /// <summary>* を使える比較 (大文字小文字は区別しない)</summary>
        public static bool ExeMatches(string pattern, string exe)
        {
            var p = NormalizeExe(pattern);
            var e = NormalizeExe(exe);
            if (p.Length == 0 || e.Length == 0) return false;
            if (p.IndexOf('*') < 0) return p == e;
            return Glob(p, 0, e, 0);
        }

        static bool Glob(string p, int i, string s, int j)
        {
            while (i < p.Length)
            {
                if (p[i] == '*')
                {
                    while (i < p.Length && p[i] == '*') i++;
                    if (i == p.Length) return true;
                    for (int k = j; k <= s.Length; k++) if (Glob(p, i, s, k)) return true;
                    return false;
                }
                if (j >= s.Length || p[i] != s[j]) return false;
                i++; j++;
            }
            return j == s.Length;
        }

        /// <summary>タイトルに含むか (大文字小文字・全角半角は区別しない)。part が空なら常に true</summary>
        public static bool TitleMatches(string part, string title)
        {
            if (string.IsNullOrWhiteSpace(part)) return true;
            if (string.IsNullOrEmpty(title)) return false;
            return CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, part.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType) >= 0;
        }

        public static bool Matches(PinRule r, string exe, string title)
        {
            return r != null && r.Enabled && ExeMatches(r.Exe, exe) && TitleMatches(r.TitleContains, title);
        }

        /// <summary>最初に合うルール (なければ null)</summary>
        public static PinRule Find(IEnumerable<PinRule> rules, string exe, string title)
        {
            if (rules == null) return null;
            return rules.FirstOrDefault(r => Matches(r, exe, title));
        }

        /// <summary>入力の確認 (null なら OK)</summary>
        static readonly char[] BadExeChars = { '<', '>', ':', '"', '/', '\\', '|', '?' };

        public static string Validate(string exe, string title)
        {
            var e = NormalizeExe(exe);
            if (e.Length == 0) return "アプリの exe の名前を入れてください (例: notepad.exe)";
            // Windows のファイル名に使えない文字 (Path.GetInvalidFileNameChars は OS ごとに違うので自前で)
            if (e.IndexOfAny(BadExeChars) >= 0 || e.Any(c => c < ' ')) return "exe の名前に使えない文字が入っています";
            if (e.Trim('*', '.').Length == 0 || e == "*.exe") return "すべてのアプリに合ってしまうルールは作れません";
            if (title != null && title.Trim().Length > 100) return "タイトルの文字は 100 文字までにしてください";
            return null;
        }
    }
}
