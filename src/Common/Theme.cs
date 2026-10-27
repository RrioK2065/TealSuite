// TealSuite 共通デザイン: 色・フォント・余白・アイコン
// ここを変えると全アプリの見た目が一緒に変わる。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public static class Theme
    {
        // ---- カラーパレット (アクセントは 1 色だけ) ----
        // テーマカラー: ティール #008080 (白文字とのコントラスト比 4.8:1 で AA を満たす)
        public static readonly Color Accent        = Color.FromArgb(0x00, 0x80, 0x80); // #008080
        public static readonly Color AccentHover   = Color.FromArgb(0x00, 0x6E, 0x6E); // #006E6E
        public static readonly Color AccentPressed = Color.FromArgb(0x00, 0x5C, 0x5C); // #005C5C
        public static readonly Color AccentSoft    = Color.FromArgb(0xE0, 0xF2, 0xF1); // #E0F2F1 選択行など
        public static readonly Color AccentLight   = Color.FromArgb(0x4D, 0xB6, 0xAC); // #4DB6AC 補助 (グラデーションの明るい側)
        public static readonly Color Background    = Color.FromArgb(0xF3, 0xF7, 0xF7); // ウィンドウ背景 (ごく薄いティール寄りのグレー)
        public static readonly Color Surface       = Color.White;                       // カード
        public static readonly Color SurfaceAlt    = Color.FromArgb(0xEC, 0xF3, 0xF3); // ホバー
        public static readonly Color Border        = Color.FromArgb(0xDA, 0xE4, 0xE4);
        public static readonly Color BorderStrong  = Color.FromArgb(0xBF, 0xCF, 0xCF);
        public static readonly Color Text          = Color.FromArgb(0x1A, 0x26, 0x26); // 本文 (白地で 15:1)
        public static readonly Color SubText       = Color.FromArgb(0x5A, 0x6B, 0x6B); // 補足 (白地で 5.6:1)
        public static readonly Color Disabled      = Color.FromArgb(0xA0, 0xAE, 0xAE);
        public static readonly Color Danger        = Color.FromArgb(0xD6, 0x40, 0x45); // 白地で 4.6:1
        public static readonly Color DangerHover   = Color.FromArgb(0xBD, 0x32, 0x37);
        public static readonly Color DangerSoft    = Color.FromArgb(0xFC, 0xEB, 0xEB);
        public static readonly Color Success       = Color.FromArgb(0x23, 0x86, 0x3F); // 緑 (ティールと区別できる色味)
        public static readonly Color SuccessSoft   = Color.FromArgb(0xE3, 0xF4, 0xE7);
        public static readonly Color Warning       = Color.FromArgb(0xD9, 0x8A, 0x0B);

        // ---- 寸法 (96dpi 基準。S() で現在の DPI に合わせる) ----
        public const int Pad = 12;          // カード内側・ウィンドウ外周の余白
        public const int Gap = 8;           // 要素間
        public const int Radius = 6;        // 角丸
        public const int ButtonHeight = 32;
        public const int HeaderHeight = 60;
        public const int FooterHeight = 28;

        static float scale = -1;
        public static float ScaleFactor
        {
            get
            {
                if (scale < 0)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; }
                    catch { scale = 1f; }
                    if (scale < 1f) scale = 1f;
                }
                return scale;
            }
        }
        public static int S(int px) { return (int)Math.Round(px * ScaleFactor); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Padding P(int all) { return new Padding(S(all)); }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        // ---- フォント ----
        static readonly string[] UiFamilies = { "Meiryo UI", "Yu Gothic UI", "Noto Sans CJK JP", "MS UI Gothic" };
        static readonly string[] MonoFamilies = { "Consolas", "BIZ UDGothic", "MS Gothic", "Noto Sans Mono CJK JP", "DejaVu Sans Mono" };
        static Font body, small, bold, title, header, mono, monoLarge, editor;
        public static Font Body   { get { return body   ?? (body   = MakeFont(UiFamilies, 9.75f, FontStyle.Regular)); } }
        public static Font Small  { get { return small  ?? (small  = MakeFont(UiFamilies, 8.25f, FontStyle.Regular)); } }
        public static Font Bold   { get { return bold   ?? (bold   = MakeFont(UiFamilies, 9.75f, FontStyle.Bold)); } }
        public static Font Title  { get { return title  ?? (title  = MakeFont(UiFamilies, 10.5f, FontStyle.Bold)); } }
        public static Font Header { get { return header ?? (header = MakeFont(UiFamilies, 13f, FontStyle.Bold)); } }
        public static Font Editor { get { return editor ?? (editor = MakeFont(UiFamilies, 11.25f, FontStyle.Regular)); } }
        public static Font Mono   { get { return mono   ?? (mono   = MakeFont(MonoFamilies, 10.5f, FontStyle.Regular)); } }
        public static Font MonoLarge { get { return monoLarge ?? (monoLarge = MakeFont(MonoFamilies, 14f, FontStyle.Regular)); } }

        public static Font MakeFont(string[] names, float size, FontStyle style)
        {
            foreach (string n in names)
            {
                try
                {
                    var f = new Font(n, size, style);
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style);
        }

        // ---- アイコン (exe に埋め込んだ AppIcon.ico を使う) ----
        static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        var s = Assembly.GetEntryAssembly().GetManifestResourceStream("AppIcon.ico");
                        if (s != null) using (s) appIcon = new Icon(s);
                    }
                    catch { }
                    if (appIcon == null)
                        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    if (appIcon == null) appIcon = SystemIcons.Application;
                }
                return appIcon;
            }
        }
        /// <summary>指定サイズに最も近い画像をアイコンから取り出す (トレイ=16, ヘッダー=32 など)</summary>
        public static Icon AppIconSized(int px) { try { return new Icon(AppIcon, px, px); } catch { return AppIcon; } }
        static Bitmap iconPng;
        /// <summary>画面に描く用のアイコン画像。埋め込みの 256px PNG (AppIcon.png) を高品質に縮小する。</summary>
        public static Bitmap AppIconBitmap(int px)
        {
            if (iconPng == null)
            {
                try
                {
                    var s = Assembly.GetEntryAssembly().GetManifestResourceStream("AppIcon.png");
                    if (s != null) using (s) using (var tmp = new Bitmap(s)) iconPng = new Bitmap(tmp);
                }
                catch { }
            }
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (iconPng != null) g.DrawImage(iconPng, 0, 0, px, px);
                else using (var ic = AppIconSized(px)) using (var src = ic.ToBitmap()) g.DrawImage(src, 0, 0, px, px);
            }
            return bmp;
        }

        /// <summary>exe や .ico ファイルから表示用画像を作る (Teal Hub の一覧などで使う)</summary>
        public static Bitmap BitmapFromIconFile(string path, int px)
        {
            try
            {
                Icon ic = path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? new Icon(path, px, px) : Icon.ExtractAssociatedIcon(path);
                using (ic)
                {
                    var bmp = new Bitmap(px, px);
                    using (var g = Graphics.FromImage(bmp))
                    using (var src = ic.ToBitmap())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, px, px);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        /// <summary>フォームに共通スタイルを適用する。各アプリの MainForm コンストラクタの最初で呼ぶ。</summary>
        public static void ApplyForm(Form f)
        {
            f.AutoScaleMode = AutoScaleMode.None; // サイズは S() で自前スケーリング
            f.Font = Body;
            f.BackColor = Background;
            f.ForeColor = Text;
            try { f.Icon = AppIcon; } catch { }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }
}
