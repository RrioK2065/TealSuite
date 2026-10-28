// 開発用: 1.11.0 の Teal Pin の画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/V1110Shots.exe list|rules|presets|settings|border
//   本物のウィンドウの代わりに FakeWindows (見本のウィンドウ) を使うので、デスクトップには何もしない
//   ビルド: build/ に dist/TealSuite の中身と apps/TealPin/TealPin.exe をコピーしてから
//     I=assets/icons/TealPin; mcs -out:build/V1110Shots.exe -codepage:utf8 -langversion:5 -win32icon:$I.ico -resource:$I.ico,AppIcon.ico -resource:$I.png,AppIcon.png -r:PIN=build/apps/TealPin/TealPin.exe \
//         -r:System.Windows.Forms.dll -r:System.Drawing.dll tests/v1110_shots.cs
//   border は枠線とタイトルバーのメニューのイメージ (mono では重ねるウィンドウを出せないので、同じ色・太さ・文字で描いた絵)
extern alias PIN;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using P = PIN::TealSuite.Pin;

[assembly: System.Reflection.AssemblyVersion("1.11.0.0")]

static class V1110Shots
{
    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "list";
        PIN::TealSuite.Common.AppInfo.Init("TealPin", "Teal Pin", "最前面に固定");
        if (mode == "border") { var bf = new BorderMock(); bf.StartPosition = FormStartPosition.Manual; bf.Location = new Point(0, 0); Application.Run(bf); return; }
        var api = new P.FakeWindows();
        var note = api.Add("議事録.txt - メモ帳", @"C:\Windows\System32\notepad.exe", new Rectangle(300, 200, 800, 600)); note.AppName = "メモ帳";
        var calc = api.Add("電卓", @"C:\Windows\System32\calc.exe", new Rectangle(1500, 60, 340, 520)); calc.AppName = "電卓";
        var video = api.Add("講座の動画 - VLC media player", @"C:\Program Files\VideoLAN\VLC\vlc.exe", new Rectangle(200, 100, 1280, 760)); video.AppName = "VLC media player";
        var meet = api.Add("定例会議 | Microsoft Teams", @"C:\Users\me\AppData\Local\Microsoft\Teams\ms-teams.exe", new Rectangle(100, 100, 1000, 700)); meet.AppName = "Microsoft Teams";
        api.Add("資料 - Microsoft Edge", @"C:\Program Files\Edge\msedge.exe", new Rectangle(0, 0, 1920, 1032)).AppName = "Microsoft Edge";
        var s = new P.PinSettings();
        if (mode == "rules")
        {
            s.Rules.Add(new P.PinRule { Id = "r1", Exe = "vlc.exe", PresetId = "b-br", Enabled = true });
            s.Rules.Add(new P.PinRule { Id = "r2", Exe = "ms-teams.exe", TitleContains = "会議", PresetId = "b-tr", Enabled = true });
            s.Rules.Add(new P.PinRule { Id = "r3", Exe = "calc*.exe", Enabled = true });
            s.Rules.Add(new P.PinRule { Id = "r4", Exe = "zoom.exe", Enabled = false });
        }
        if (mode == "presets")
        {
            s.Presets.Add(new P.PinPreset { Id = "u-1", Name = "右側の縦長", Kind = P.PinPreset.KindArea, X = 0.7, Y = 0, W = 0.3, H = 1 });
            s.Presets.Add(new P.PinPreset { Id = "u-2", Name = "動画を見ながら", Kind = P.PinPreset.KindArea, X = 0.6, Y = 0.55, W = 0.4, H = 0.45 });
        }
        var m = new P.PinManager(api, () => new P.FakeBorder(), s, 777);
        m.RuleDelayMs = 0;
        if (mode == "list" || mode == "presets")
        {
            m.Pin(video.Handle, P.PinManager.HowHotkey, null);
            m.Pin(note.Handle, P.PinManager.HowTitleBar, null);
            m.Pin(calc.Handle, "自動 (calc.exe)", null);
        }
        var f = new P.MainForm(m);
        f.HotkeyText = "Ctrl + Alt + T";
        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(0, 0);
        if (mode == "settings") f.Shown += (o, e) => { f.Height = 670; };
        f.Shown += (o, e) =>
        {
            int page = mode == "rules" ? 1 : mode == "presets" ? 2 : mode == "settings" ? 3 : 0;
            f.ShowPage(page);
            if (page == 0) f.PinList.SelectedIndex = 0;
            if (page == 1) f.RuleList.SelectedIndex = 1;
            if (page == 2) f.PresetList.SelectedIndex = 0;
        };
        Application.Run(f);
    }
}

/// <summary>枠線とタイトルバーの「📌 最前面に固定」のイメージ (README 用)</summary>
class BorderMock : Form
{
    static readonly Color Teal = PIN::TealSuite.Common.Theme.Accent;
    public BorderMock()
    {
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1000, 600);
        DoubleBuffered = true;
        Text = "Teal Pin の枠線";
    }

    static void Win(Graphics g, Rectangle r, string title, Color body, bool active)
    {
        using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(sh, r.X + 4, r.Y + 6, r.Width, r.Height);
        using (var b = new SolidBrush(body)) g.FillRectangle(b, r);
        using (var b = new SolidBrush(active ? Color.White : Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(b, r.X, r.Y, r.Width, 32);
        using (var p = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8))) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        var f = PIN::TealSuite.Common.Theme.Body;
        TextRenderer.DrawText(g, title, f, new Rectangle(r.X + 12, r.Y, r.Width - 140, 32), Color.FromArgb(0x20, 0x20, 0x20), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "—     ☐     ✕", f, new Rectangle(r.Right - 130, r.Y, 120, 32), Color.FromArgb(0x30, 0x30, 0x30), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    static void Lines(Graphics g, Rectangle r, int n, int top)
    {
        using (var b = new SolidBrush(Color.FromArgb(0xD8, 0xDE, 0xDE)))
            for (int i = 0; i < n; i++) g.FillRectangle(b, r.X + 16, r.Y + top + i * 22, (i % 3 == 2) ? r.Width / 2 : r.Width - 40, 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(0x5E, 0x7F, 0x99), Color.FromArgb(0x2F, 0x4A, 0x60), 60f)) g.FillRectangle(bg, ClientRectangle);
        var taskbar = new Rectangle(0, ClientSize.Height - 40, ClientSize.Width, 40);
        using (var b = new SolidBrush(Color.FromArgb(0xEE, 0xF2, 0xF4))) g.FillRectangle(b, taskbar);

        // 後ろのウィンドウ (ブラウザー)
        var back = new Rectangle(30, 110, 640, 400);
        Win(g, back, "資料 - Microsoft Edge", Color.White, false);
        Lines(g, back, 12, 56);

        // 固定したウィンドウ (メモ帳) とティールの枠線 (3 px。BorderMath と同じ並べ方)
        var pinned = new Rectangle(470, 230, 420, 260);
        Win(g, pinned, "議事録.txt - メモ帳", Color.White, true);
        Lines(g, pinned, 7, 48);
        g.SmoothingMode = SmoothingMode.None;
        using (var b = new SolidBrush(Teal))
            foreach (var st in PIN::TealSuite.Pin.BorderMath.Strips(pinned, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height - 40), 3)) g.FillRectangle(b, st);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var small = PIN::TealSuite.Common.Theme.Small;
        var bold = PIN::TealSuite.Common.Theme.Bold;
        // 吹き出し
        var tag = new Rectangle(pinned.Right - 150, pinned.Bottom + 10, 150, 26);
        using (var b = new SolidBrush(Teal)) g.FillRectangle(b, tag);
        TextRenderer.DrawText(g, "📌 最前面に固定中", small, tag, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        // タイトルバーの右クリック: システム メニューと、そのすぐ上の Teal Pin のボタン
        var menu = new Rectangle(140, 128, 210, 6 * 28 + 10);   // タイトルバーで右クリックした所に開く
        using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillRectangle(sh, menu.X + 3, menu.Y + 4, menu.Width, menu.Height);
        g.FillRectangle(Brushes.White, menu);
        using (var p = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8))) g.DrawRectangle(p, menu.X, menu.Y, menu.Width - 1, menu.Height - 1);
        string[] items = { "元のサイズに戻す", "移動", "サイズ変更", "最小化", "最大化", "閉じる    Alt+F4" };
        for (int i = 0; i < items.Length; i++)
            TextRenderer.DrawText(g, items[i], PIN::TealSuite.Common.Theme.Body, new Rectangle(menu.X + 30, menu.Y + 5 + i * 28, menu.Width - 40, 28),
                i == 0 || i == 4 ? Color.FromArgb(0xA0, 0xA0, 0xA0) : Color.FromArgb(0x20, 0x20, 0x20), TextFormatFlags.VerticalCenter);
        var pop = new Rectangle(menu.X, menu.Y - 50 - 4, 210, 50);
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(Brushes.White, pop);
        using (var p = new Pen(Teal)) g.DrawRectangle(p, pop.X, pop.Y, pop.Width - 1, pop.Height - 1);
        using (var b = new SolidBrush(Teal)) g.FillRectangle(b, pop.X, pop.Y, 4, pop.Height);
        TextRenderer.DrawText(g, "📌 最前面に固定", bold, new Rectangle(pop.X + 16, pop.Y + 6, pop.Width - 16, 22), Teal, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Teal Pin", small, new Rectangle(pop.X + 16, pop.Y + 28, pop.Width - 16, 18), PIN::TealSuite.Common.Theme.SubText, TextFormatFlags.NoPrefix);
        // マウス
        var cur = new Point(menu.X - 2, menu.Y - 3);
        using (var b = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20)))
            g.FillPolygon(b, new[] { new Point(cur.X, cur.Y), new Point(cur.X, cur.Y + 18), new Point(cur.X + 5, cur.Y + 14), new Point(cur.X + 9, cur.Y + 21), new Point(cur.X + 12, cur.Y + 19), new Point(cur.X + 8, cur.Y + 12), new Point(cur.X + 14, cur.Y + 12) });

        // 説明
        TextRenderer.DrawText(g, "タイトルバーを右クリック → メニューのすぐ上に「📌 最前面に固定」", small, new Rectangle(20, ClientSize.Height - 40, 560, 40), Color.FromArgb(0x30, 0x40, 0x40), TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "固定したウィンドウはティールの枠線 (クリックは通ります)", small, new Rectangle(560, ClientSize.Height - 40, 430, 40), Teal, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

