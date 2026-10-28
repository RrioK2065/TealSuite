// 1.11.0 のテスト: 新しいアプリ Teal Pin (ウィンドウを最前面に固定)
//   ルール・プリセットの計算・設定の保存・PinManager (FakeWindows / FakeBorder)・画面・常駐・スイートへの登録。
//   Windows だけ: tests\win\PinTarget.exe を本物の最前面 (WS_EX_TOPMOST) にして外す
extern alias PIN;
extern alias AM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using P = PIN::TealSuite.Pin;
using PC = PIN::TealSuite.Common;
using H = AM::TealSuite.Hub;

public static class V1110Tests
{
    const string Notepad = @"C:\Windows\System32\notepad.exe";
    const string Vlc = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
    const string Teams = @"C:\Users\me\AppData\Local\Microsoft\Teams\ms-teams.exe";

    static P.PinRule Rule(string exe, string title = null, string preset = null, bool enabled = true)
    {
        return new P.PinRule { Id = P.PinSettings.NewId(), Exe = exe, TitleContains = title, PresetId = preset, Enabled = enabled };
    }

    static void UseData(string tmp, string name)
    {
        var d = Path.Combine(tmp, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        PC.AppInfo.Init("TealPin", "Teal Pin", "最前面に固定");
    }

    static void RestoreData(string tmp) { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", Path.Combine(tmp, "data")); }

    // ---------------------------------------------------------------- ルール
    public static void RunRules(Action<bool, string> Check)
    {
        Check(P.RuleMatcher.NormalizeExe(Vlc) == "vlc.exe" && P.RuleMatcher.NormalizeExe(" \"Notepad\" ") == "notepad.exe" && P.RuleMatcher.NormalizeExe("/usr/bin/Code.EXE") == "code.exe" && P.RuleMatcher.NormalizeExe("") == "", "exe の名前にそろえる (パス・大文字・拡張子なし・引用符)");
        Check(P.RuleMatcher.ExeMatches("VLC", Vlc) && P.RuleMatcher.ExeMatches("vlc.exe", "VLC.EXE") && !P.RuleMatcher.ExeMatches("vlc.exe", Notepad), "exe で比べる (大文字小文字は区別しない)");
        Check(P.RuleMatcher.ExeMatches("ms-teams*", Teams) && P.RuleMatcher.ExeMatches("*team*", Teams) && !P.RuleMatcher.ExeMatches("teams*", Teams) && !P.RuleMatcher.ExeMatches("", Teams), "* を使える (前・後ろ・途中)");
        Check(P.RuleMatcher.TitleMatches(null, "x") && P.RuleMatcher.TitleMatches("会議", "週次 会議 | Microsoft Teams") && P.RuleMatcher.TitleMatches("ｍｅｅｔｉｎｇ", "Team Meeting") && P.RuleMatcher.TitleMatches("カイギ", "かいぎ室") && !P.RuleMatcher.TitleMatches("会議", ""), "タイトルに含む (全角半角・ひらがなカタカナ・大文字小文字を区別しない)");

        var rules = new List<P.PinRule> { Rule("ms-teams.exe", "会議"), Rule("vlc.exe", null, "b-br", false), Rule("vlc.exe", null, "b-left"), Rule("ms-teams.exe") };
        Check(P.RuleMatcher.Find(rules, Teams, "週次会議") == rules[0] && P.RuleMatcher.Find(rules, Teams, "チャット") == rules[3], "上のルールから順に、最初に合うもの");
        Check(P.RuleMatcher.Find(rules, Vlc, "動画") == rules[2], "使わないルールはとばす");
        Check(P.RuleMatcher.Find(rules, Notepad, "メモ") == null && P.RuleMatcher.Find(null, Notepad, "x") == null, "合わなければ null");
        Check(P.RuleMatcher.Validate("", null) != null && P.RuleMatcher.Validate("*", null) != null && P.RuleMatcher.Validate("*.exe", null) != null && P.RuleMatcher.Validate("a<b.exe", null) != null, "空・すべてに合う・使えない文字はエラー");
        Check(P.RuleMatcher.Validate("notepad", "") == null && P.RuleMatcher.Validate("chrome*", "YouTube") == null && P.RuleMatcher.Validate("x.exe", new string('あ', 101)) != null, "ふつうの入力は OK・タイトルは 100 文字まで");
        Check(rules[0].Describe() == "ms-teams.exe ・ タイトルに「会議」" && rules[2].Describe() == "vlc.exe", "ルールの説明 (" + rules[0].Describe() + ")");
    }

    // ---------------------------------------------------------------- プリセットと枠線の計算
    public static void RunPresetMath(Action<bool, string> Check)
    {
        var all = P.PresetMath.BuiltIns();
        Func<string, P.PinPreset> B = id => all.First(p => p.Id == id);
        Check(all.Count == 6 && all.All(p => p.BuiltIn) && all.Any(p => p.Name == "右下の小窓") && all.Any(p => p.Name == "左半分") && all.Select(p => p.Id).Distinct().Count() == 6, "組み込みは 6 個 (右下の小窓・左半分 など)");
        var work = new Rectangle(0, 0, 1920, 1032);
        Check(P.PresetMath.Compute(B("b-br"), work, 1.0) == new Rectangle(1424, 696, 480, 320), "右下の小窓: 作業領域の右下にすきま 16px (" + P.PresetMath.Compute(B("b-br"), work, 1.0) + ")");
        Check(P.PresetMath.Compute(B("b-tr"), work, 1.0) == new Rectangle(1424, 16, 480, 320) && P.PresetMath.Compute(B("b-bl"), work, 1.0) == new Rectangle(16, 696, 480, 320), "右上・左下の小窓");
        Check(P.PresetMath.Compute(B("b-left"), work, 1.0) == new Rectangle(0, 0, 960, 1032) && P.PresetMath.Compute(B("b-right"), work, 1.0) == new Rectangle(960, 0, 960, 1032), "左半分・右半分");
        // 2 枚目のモニター (右側・150%・タスクバーが左)
        var work2 = new Rectangle(2000, 0, 2500, 1440);
        var br2 = P.PresetMath.Compute(B("b-br"), work2, 1.5);
        Check(br2 == new Rectangle(2000 + 2500 - 720 - 24, 1440 - 480 - 24, 720, 480), "拡大率 150% のモニター: 大きさとすきまも 1.5 倍、そのモニターの右下 (" + br2 + ")");
        Check(P.PresetMath.Compute(B("b-left"), work2, 1.5) == new Rectangle(2000, 0, 1250, 1440), "割合のプリセットは、そのモニターの作業領域に対して");
        var neg = new Rectangle(-1280, -200, 1280, 984);   // 左上にあるモニター (座標がマイナス)
        var c = P.PresetMath.Compute(B("b-center"), neg, 1.0);
        Check(neg.Contains(c) && c.Width == 896 && c.X == -1280 + 192, "座標がマイナスのモニターでも (" + c + ")");
        var tiny = new Rectangle(0, 0, 300, 200);
        var t = P.PresetMath.Compute(B("b-br"), tiny, 2.0);
        Check(tiny.Contains(t) && t.Width >= 160 && t.Height >= 80, "小さな画面でもはみ出さない (" + t + ")");
        Check(P.PresetMath.Compute(B("b-br"), Rectangle.Empty, 1.0).IsEmpty && P.PresetMath.Compute(null, work, 1).IsEmpty && P.PresetMath.Compute(B("b-br"), work, double.NaN) == new Rectangle(1424, 696, 480, 320), "空の作業領域・おかしな拡大率");
        Check(P.PresetMath.Fit(new Rectangle(1800, 1000, 400, 300), work) == new Rectangle(1520, 732, 400, 300) && P.PresetMath.Fit(new Rectangle(-50, -50, 3000, 2000), work) == work, "作業領域の中に入れる (大きすぎれば縮める)");

        // 今の位置から作る → ほかのモニターでも同じ割合
        var u = P.PresetMath.FromRect(" 会議の右 ", new Rectangle(480, 258, 960, 516), work);
        Check(u.Name == "会議の右" && u.Kind == P.PinPreset.KindArea && u.X == 0.25 && u.Y == 0.25 && u.W == 0.5 && u.H == 0.5 && u.Id.StartsWith("u-") && !u.BuiltIn, "今の位置からプリセット (画面に対する割合)");
        Check(P.PresetMath.Compute(u, new Rectangle(1920, 0, 2560, 1400), 1.25) == new Rectangle(2560, 350, 1280, 700), "保存したプリセットを別のモニターで");
        var half = P.PresetMath.FromRect("x", new Rectangle(1700, 900, 600, 400), work);   // 右下にはみ出していた
        Check(half.X + half.W <= 1.0001 && half.Y + half.H <= 1.0001 && half.W > 0, "はみ出していたら見えている部分で");
        Check(u.Summary.Contains("25%") && B("b-br").Summary.StartsWith("右下に 480 × 320"), "一覧の説明 (" + B("b-br").Summary + ")");

        // 見えない縁 (Windows 10/11 の左右と下の 7px)
        var desired = new Rectangle(1424, 696, 480, 320);
        Check(P.PresetMath.ToWindowRect(desired, new Rectangle(93, 100, 814, 607), new Rectangle(100, 100, 800, 600)) == Rectangle.FromLTRB(1417, 696, 1911, 1023), "見えない縁の分を足して動かす");
        Check(P.PresetMath.ToWindowRect(desired, new Rectangle(0, 0, 10, 10), Rectangle.Empty) == desired && P.PresetMath.ToWindowRect(desired, new Rectangle(-32000, -32000, 160, 28), new Rectangle(0, 0, 100, 100)) == desired, "縁がわからない・最小化中は足さない");

        var mons = new List<Rectangle> { new Rectangle(0, 0, 1920, 1080), new Rectangle(1920, 0, 2560, 1440) };
        Check(P.PresetMath.PickMonitor(new Rectangle(1800, 100, 800, 600), mons) == 1 && P.PresetMath.PickMonitor(new Rectangle(100, 100, 300, 300), mons) == 0 && P.PresetMath.PickMonitor(new Rectangle(5000, 100, 100, 100), mons) == 1 && P.PresetMath.PickMonitor(Rectangle.Empty, new List<Rectangle>()) == -1, "いちばん重なっているモニター (離れていればいちばん近い)");

        Check(P.PresetMath.ValidateName("", all, null) != null && P.PresetMath.ValidateName("左半分", all, null) != null && P.PresetMath.ValidateName(new string('a', 41), all, null) != null && P.PresetMath.ValidateName(" 小窓2 ", all, null) == null, "名前: 空・同じ名前・41 文字はだめ");
        var mine = new P.PinPreset { Id = "u-1", Name = "自分の" };
        Check(P.PresetMath.ValidateName("自分の", all.Concat(new[] { mine }), mine) == null, "名前を変えるとき、自分と同じ名前は OK");
        var bad = new P.PinPreset { Kind = "?", X = -1, Y = 0.9, W = 3, H = double.NaN };
        P.PresetMath.Clean(bad);
        Check(bad.Kind == P.PinPreset.KindArea && bad.X == 0 && bad.W == 1 && bad.H == 0.05 && bad.Y + bad.H <= 1, "おかしな値を直す (割合)");
        var bc = new P.PinPreset { Kind = P.PinPreset.KindCorner, Anchor = "zz", Width = -5, Height = 99999 };
        P.PresetMath.Clean(bc);
        Check(bc.Anchor == "br" && bc.Width == 480 && bc.Height == 10000, "おかしな値を直す (隅)");

        // 枠線
        var s = P.BorderMath.Strips(new Rectangle(100, 100, 500, 400), work, 3);
        Check(s.Length == 4 && s[0] == new Rectangle(97, 97, 506, 3) && s[1] == new Rectangle(97, 500, 506, 3) && s[2] == new Rectangle(97, 100, 3, 400) && s[3] == new Rectangle(600, 100, 3, 400), "枠線はウィンドウの外側に 4 本 (重ならない)");
        var m = P.BorderMath.Strips(work, work, 4);
        Check(m.Length == 4 && m.All(r => work.Contains(r)) && m[0] == new Rectangle(0, 0, 1920, 4) && m[3] == new Rectangle(1916, 4, 4, 1024), "最大化: 外側に場所がないので内側に (画面からはみ出さない)");
        var edge = P.BorderMath.Strips(new Rectangle(0, 500, 400, 300), work, 3);
        Check(edge[0].Y == 497 && edge[2].X == 0 && edge[0].X == 0, "画面の左端に付けた辺だけ内側");
        Check(P.BorderMath.Strips(new Rectangle(0, 0, 5, 5), work, 3).Length == 0 && P.BorderMath.Strips(new Rectangle(10, 10, 100, 100), work, 0).Length == 0, "小さすぎる・太さ 0 は描かない");
        Check(P.BorderMath.Thickness(3, 1.0) == 3 && P.BorderMath.Thickness(3, 2.0) == 6 && P.BorderMath.Thickness(3, 1.25) == 4 && P.BorderMath.Thickness(1, 0.5) == 1 && P.BorderMath.Thickness(3, -1) == 3, "太さは拡大率に合わせる (最低 1px)");
    }

    // ---------------------------------------------------------------- 設定
    public static void RunSettings(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "pin-settings");
        try
        {
            var d = P.PinStore.Load();
            Check(d.TitleBarMenu && d.ShowBorder && d.BorderWidth == 3 && d.Toast && d.KeepTopmost && d.RulesOnStart && d.Rules.Count == 0 && d.Presets.Count == 0 && !d.TrayTipShown, "初期設定 (タイトルバー・枠線 3px・お知らせ・固定し直す・起動時にルール: オン)");
            Check(d.AllPresets().Count == 6 && d.FindPreset("b-br").Name == "右下の小窓" && d.FindPreset(null) == null && d.FindPreset("nope") == null, "組み込みのプリセットを探せる");

            d.TitleBarMenu = false; d.BorderWidth = 5; d.Toast = false;
            d.Rules.Add(Rule("C:\\x\\VLC.exe", "  ", "b-br"));
            d.Rules.Add(Rule("ms-teams", " 会議 "));
            d.Presets.Add(P.PresetMath.FromRect("会議の右", new Rectangle(960, 0, 960, 1032), new Rectangle(0, 0, 1920, 1032)));
            Check(P.PinStore.Save(d), "保存できる");
            var file = Path.Combine(PC.DataStore.DirFor("TealPin"), "settings.json");
            Check(File.Exists(file) && file.Replace('\\', '/').EndsWith("/TealPin/settings.json"), "保存先は TealSuite\\TealPin\\settings.json");
            var json = File.ReadAllText(file);
            Check(!json.Contains("右下の小窓") && json.Contains("会議の右") && json.Contains("\"titleBarMenu\":false"), "組み込みのプリセットは保存しない");
            var b = P.PinStore.Load();
            Check(!b.TitleBarMenu && b.BorderWidth == 5 && !b.Toast && b.Rules.Count == 2 && b.Rules[0].Exe == "vlc.exe" && b.Rules[0].TitleContains == null && b.Rules[0].PresetId == "b-br" && b.Rules[1].Exe == "ms-teams.exe" && b.Rules[1].TitleContains == "会議", "読み込み (exe の名前をそろえ、空白を取る)");
            Check(b.Presets.Count == 1 && b.Presets[0].Name == "会議の右" && b.Presets[0].X == 0.5 && b.FindPreset(b.Presets[0].Id) != null && b.AllPresets().Count == 7, "自分のプリセットを読み込む");

            File.WriteAllText(file, "{\"version\":0,\"borderWidth\":99,\"rules\":[null,{\"exe\":\"\"},{\"id\":\"r\",\"exe\":\"a\"},{\"id\":\"r\",\"exe\":\"b\",\"preset\":\" \"}],\"presets\":[{\"id\":\"b-br\",\"name\":\"偽物\"},{\"id\":\"x\",\"name\":\" ok \",\"kind\":\"area\",\"x\":2,\"w\":0.5,\"y\":0,\"h\":0.5},{\"id\":\"u-x\",\"name\":\"\"},{\"id\":\"u-same\",\"name\":\"A\"},{\"id\":\"u-same\",\"name\":\"B\"}],\"newField\":1}");
            var c = P.PinStore.Load();
            Check(c.Version == 1 && c.BorderWidth == 8 && c.TitleBarMenu && c.ShowBorder, "おかしな値を直す (太さは 8px まで・ない項目は初期値)");
            Check(c.Rules.Count == 2 && c.Rules[0].Exe == "a.exe" && c.Rules[1].Exe == "b.exe" && c.Rules[0].Id != c.Rules[1].Id && c.Rules[1].PresetId == null, "空のルールを消し、同じ ID を付け直す");
            Check(c.Presets.Count == 3 && c.Presets[0].Name == "ok" && c.Presets[0].Id.StartsWith("u-") && c.Presets[0].X == 0.5 && c.Presets.Select(p => p.Id).Distinct().Count() == 3 && !c.Presets.Any(p => p.Name == "偽物"), "組み込みの ID・名前なしのプリセットを消し、値と ID を直す");
            File.WriteAllText(file, "{ 壊れた");
            var e = P.PinStore.Load();
            Check(e != null && e.TitleBarMenu && e.Rules.Count == 0, "壊れたファイルは初期設定で開く");
            for (int i = 0; i < 60; i++) e.Rules.Add(Rule("app" + i));
            e.Normalize();
            Check(e.Rules.Count == P.PinSettings.MaxRules, "ルールは 50 個まで");
        }
        finally { RestoreData(tmp); }
    }

    // ---------------------------------------------------------------- PinManager
    class Env
    {
        public P.FakeWindows Api = new P.FakeWindows();
        public P.PinSettings S = new P.PinSettings();
        public List<P.FakeBorder> Borders = new List<P.FakeBorder>();
        public List<string> Notices = new List<string>();
        public P.PinRecordFile LastRecords;
        public int Changes;
        public P.PinManager M;
        public P.FakeWindow Note, Calc, Edge, Admin, Self, Top;
        public Env()
        {
            Note = Api.Add("メモ.txt - メモ帳", Notepad, new Rectangle(300, 200, 800, 600)); Note.AppName = "メモ帳";
            Calc = Api.Add("電卓", @"C:\Windows\System32\calc.exe", new Rectangle(1500, 60, 340, 520)); Calc.AppName = "電卓";
            Edge = Api.Add("資料 - Microsoft Edge", @"C:\Program Files\Edge\msedge.exe", new Rectangle(0, 0, 1920, 1032)); Edge.AppName = "Microsoft Edge";
            Admin = Api.Add("管理者: コマンド プロンプト", @"C:\Windows\System32\cmd.exe", new Rectangle(240, 300, 800, 450)); Admin.Elevated = true;
            Self = Api.Add("Teal Pin", @"C:\TealSuite\apps\TealPin\TealPin.exe", new Rectangle(100, 100, 600, 500), 777);
            Top = Api.Add("付箋", @"C:\x\sticky.exe", new Rectangle(1600, 700, 200, 200)); Top.Topmost = true;
            M = new P.PinManager(Api, () => { var b = new P.FakeBorder(); Borders.Add(b); return b; }, S, 777);
            M.Notice = (m, h, err) => Notices.Add((err ? "!" : "") + m);
            M.SaveRecords = f => LastRecords = f;
            M.Changed += () => Changes++;
            M.RuleDelayMs = 0;
        }
    }

    public static void RunManager(Action<bool, string> Check)
    {
        var e = new Env();
        var m = e.M;
        Check(m.Check(e.Note.Handle) == null && m.Check(IntPtr.Zero) == P.PinManager.MsgNoWindow && m.Check(new IntPtr(1)) == P.PinManager.MsgGone, "固定できるか確かめる");
        Check(m.Check(e.Self.Handle) == P.PinManager.MsgSelf && m.Check(e.Admin.Handle) == P.PinManager.MsgElevated && m.Check(e.Top.Handle) == P.PinManager.MsgAlreadyTop, "Teal Pin 自身・管理者のウィンドウ・もとから最前面は固定しない");
        var shell = e.Api.Add("", "explorer.exe", new Rectangle(0, 0, 1920, 1080)); shell.ClassName = "Shell_TrayWnd";
        Check(m.Check(shell.Handle) == P.PinManager.MsgShell, "タスクバーは固定しない");

        // ショートカット: 前面のウィンドウを固定 → もう一度で外す
        e.Api.ForegroundHandle = e.Note.Handle;
        var r = m.ToggleForeground(P.PinManager.HowHotkey);
        Check(r.Ok && r.Pinned && e.Note.Topmost && m.Count == 1 && m.IsPinned(e.Note.Handle), "ショートカット: 前面のウィンドウを最前面に固定 (HWND_TOPMOST)");
        var pw = m.Find(e.Note.Handle);
        Check(pw.Title == "メモ.txt - メモ帳" && pw.AppName == "メモ帳" && pw.ExeName == "notepad.exe" && pw.How == "ショートカット" && pw.Subtitle == "メモ帳 ・ notepad.exe ・ ショートカット", "一覧の項目: タイトル・アプリ名・exe・固定のしかた (" + pw.Subtitle + ")");
        Check(e.Borders.Count == 1 && e.Borders[0].Visible && e.Borders[0].Strips.Length == 4 && e.Borders[0].Strips[0] == new Rectangle(297, 197, 806, 3), "ティールの枠線をウィンドウのまわりに");
        Check(e.Notices.Last() == "📌 最前面に固定しました" && e.Changes == 1 && e.LastRecords.Pinned.Count == 1 && e.LastRecords.Pinned[0].Hwnd == e.Note.Handle.ToInt64() && e.LastRecords.Pinned[0].Exe == Notepad, "お知らせ・一覧の更新・落ちたとき用の記録");
        r = m.ToggleForeground(P.PinManager.HowHotkey);
        Check(r.Ok && !r.Pinned && !e.Note.Topmost && m.Count == 0 && e.Borders[0].Disposed && e.LastRecords.Pinned.Count == 0, "もう一度押すと外す (HWND_NOTOPMOST・枠線を消す)");

        // 固定できないとき
        e.Api.ForegroundHandle = e.Admin.Handle;
        r = m.ToggleForeground(P.PinManager.HowHotkey);
        Check(!r.Ok && r.Message == P.PinManager.MsgElevated && e.Notices.Last() == "!" + P.PinManager.MsgElevated && !e.Admin.Topmost, "管理者として実行中のウィンドウ: 分かりやすいお知らせ");
        e.Api.ForegroundHandle = IntPtr.Zero;
        Check(!m.ToggleForeground("x").Ok && e.Notices.Last() == "!" + P.PinManager.MsgNoWindow, "前面にウィンドウがない");
        e.Calc.FailWith = 5;
        r = m.Pin(e.Calc.Handle, P.PinManager.HowList, null);
        Check(!r.Ok && r.Message == P.PinManager.MsgElevated && m.Count == 0, "Windows が拒んだ (アクセス拒否) ときも管理者のお知らせ");
        e.Calc.FailWith = 1234;
        r = m.Pin(e.Calc.Handle, P.PinManager.HowList, null);
        Check(!r.Ok && r.Message.Contains("1234"), "そのほかのエラーは番号つきで");
        e.Calc.FailWith = 0;

        // 動き・大きさ・最小化・最大化・閉じる (SetWinEventHook のできごと)
        m.Pin(e.Note.Handle, P.PinManager.HowTitleBar, null);
        var bd = e.Borders.Last();
        int places = bd.Places;
        e.Note.Rect = new Rectangle(400, 300, 640, 480);
        m.OnEvent(P.WinEvt.Location, e.Note.Handle, DateTime.Now);
        Check(bd.Places == places + 1 && bd.Strips[0] == new Rectangle(397, 297, 646, 3), "動かす・大きさを変えると枠線がついていく");
        m.OnEvent(P.WinEvt.Location, e.Note.Handle, DateTime.Now);
        Check(bd.Places == places + 1, "同じ場所なら置き直さない (ちらつかない)");
        bd.Order = false;
        m.OnEvent(P.WinEvt.Location, e.Note.Handle, DateTime.Now);
        Check(bd.Places == places + 2, "重なりの順番がずれたら置き直す");
        bd.Order = true;
        e.Note.Minimized = true;
        m.OnEvent(P.WinEvt.MinimizeStart, e.Note.Handle, DateTime.Now);
        Check(!bd.Visible && m.Find(e.Note.Handle).Minimized && m.Find(e.Note.Handle).Subtitle.EndsWith("最小化中"), "最小化すると枠線を隠す");
        e.Note.Minimized = false;
        m.OnEvent(P.WinEvt.MinimizeEnd, e.Note.Handle, DateTime.Now);
        Check(bd.Visible, "元に戻すと枠線も");
        e.Note.Maximized = true;
        m.OnEvent(P.WinEvt.Location, e.Note.Handle, DateTime.Now);
        var work = e.Api.WorkArea(e.Note.Handle);
        Check(bd.Strips.All(s => work.Contains(s)) && bd.Strips[0] == new Rectangle(0, 0, 1920, 3), "最大化: 枠線を画面の内側に");
        e.Note.Maximized = false;
        e.Note.Cloaked = true;
        m.OnEvent(P.WinEvt.Cloaked, e.Note.Handle, DateTime.Now);
        Check(!bd.Visible, "ほかの仮想デスクトップへ (cloaked) は隠す");
        e.Note.Cloaked = false;
        m.OnEvent(P.WinEvt.Uncloaked, e.Note.Handle, DateTime.Now);
        Check(bd.Visible, "戻ってきたら出す");
        e.Note.Title = "新しい名前 - メモ帳";
        int ch = e.Changes;
        m.OnEvent(P.WinEvt.NameChange, e.Note.Handle, DateTime.Now);
        Check(m.Find(e.Note.Handle).Title == "新しい名前 - メモ帳" && e.Changes == ch + 1, "タイトルが変わったら一覧も");
        e.S.ShowBorder = false; m.RefreshBorders();
        Check(!bd.Visible, "枠線をオフにすると消える");
        e.S.ShowBorder = true; e.S.BorderWidth = 6; m.RefreshBorders();
        Check(bd.Visible && bd.Strips[0].Height == 6, "太さを変える");
        e.Api.Scales[0] = 1.5; m.RefreshBorders();
        Check(bd.Strips[0].Height == 9, "拡大率 150% では 1.5 倍の太さ (DPI)");
        e.Api.Scales[0] = 1.0; e.S.BorderWidth = 3;

        // 2 枚目のモニター
        e.Api.Monitors.Add(new Rectangle(1920, 0, 2560, 1440)); e.Api.Scales.Add(1.25);
        m.Pin(e.Calc.Handle, P.PinManager.HowTray, null);
        e.Calc.Rect = new Rectangle(2500, 300, 340, 520);
        m.OnEvent(P.WinEvt.Location, e.Calc.Handle, DateTime.Now);
        var cb = e.Borders.Last();
        Check(cb.Strips[0].Height == 4 && cb.Strips[0].X == 2496, "別のモニターへ動かすと、そのモニターの拡大率で");
        var ar = m.ApplyPreset(e.Calc.Handle, m.Settings.FindPreset("b-br"));
        var cw = e.Api.WorkArea(e.Calc.Handle);
        Check(ar.Ok && ar.Pinned && e.Calc.Rect == new Rectangle(1920 + 2560 - 600 - 20, 1440 - 48 - 400 - 20, 600, 400) && cw.X == 1920, "プリセットはウィンドウがあるモニターの作業領域で (" + e.Calc.Rect + ")");
        Check(e.Notices.Last() == "「右下の小窓」にしました", "プリセットを使ったお知らせ");
        e.Edge.Maximized = true;
        ar = m.ApplyPreset(e.Edge.Handle, m.Settings.FindPreset("b-left"));
        Check(ar.Ok && !ar.Pinned && !e.Edge.Maximized && e.Edge.Rect == new Rectangle(0, 0, 960, 1032) && !e.Edge.Topmost, "最大化中のウィンドウにも使える (元に戻してから。固定していなくても)");
        Check(!m.ApplyPreset(e.Admin.Handle, m.Settings.FindPreset("b-left")).Ok && !m.ApplyPreset(new IntPtr(5), m.Settings.FindPreset("b-left")).Ok && !m.ApplyPreset(e.Calc.Handle, null).Ok, "管理者・閉じた・プリセットなしは失敗");
        var cap = m.CapturePreset(e.Edge.Handle, "左の資料");
        Check(cap != null && cap.X == 0 && cap.W == 0.5 && cap.H == 1 && cap.Name == "左の資料", "ウィンドウの今の位置からプリセットを作る");

        // 閉じた
        int before = m.Count;
        e.Api.Close(e.Calc.Handle);
        m.OnEvent(P.WinEvt.Destroy, e.Calc.Handle, DateTime.Now);
        Check(m.Count == before - 1 && cb.Disposed && !m.IsPinned(e.Calc.Handle), "閉じたら一覧から外し、枠線を片づける");

        // アプリが最前面をやめた → 固定し直す (1 秒ごとの確認)
        var now = new DateTime(2026, 10, 28, 9, 0, 0);
        e.Note.Topmost = false;
        m.Tick(now);
        Check(e.Note.Topmost, "アプリ自身が最前面を外しても固定し直す");
        e.S.KeepTopmost = false; e.Note.Topmost = false;
        m.Tick(now.AddSeconds(2));
        Check(!e.Note.Topmost, "「固定し直す」がオフなら何もしない");
        e.S.KeepTopmost = true;
        var gone = e.Api.Add("消える", "x.exe", new Rectangle(10, 10, 300, 300));
        m.Pin(gone.Handle, "x", null);
        e.Api.Close(gone.Handle);
        m.Tick(now.AddSeconds(4));
        Check(!m.IsPinned(gone.Handle), "閉じた知らせを取りこぼしても、確認で外す");

        // 前面のウィンドウを覚える (通知領域の「…を固定」)
        m.OnEvent(P.WinEvt.Foreground, e.Edge.Handle, DateTime.Now);
        m.OnEvent(P.WinEvt.Foreground, e.Self.Handle, DateTime.Now);
        m.OnEvent(P.WinEvt.Foreground, e.Admin.Handle, DateTime.Now);
        Check(m.LastForeground == e.Edge.Handle, "最後に前面だったウィンドウ (Teal Pin 自身・固定できないものは除く)");
        var cand = m.Candidates();
        Check(cand.Contains(e.Edge.Handle) && cand.Contains(e.Note.Handle) && !cand.Contains(e.Self.Handle) && !cand.Contains(e.Admin.Handle) && !cand.Contains(shell.Handle), "選べるウィンドウ (Teal Pin 自身・管理者・タスクバーは除く)");

        // すべて外す・終了
        m.Pin(e.Edge.Handle, "x", null);
        Check(m.Count == 2, "2 つ固定中");
        Check(m.UnpinAll() == 2 && m.Count == 0 && !e.Note.Topmost && !e.Edge.Topmost && e.Borders.All(b => b.Disposed) && e.LastRecords.Pinned.Count == 0, "すべての固定を外す");
        m.Pin(e.Note.Handle, "x", null);
        m.Dispose();
        Check(!e.Note.Topmost && m.Count == 0 && !m.Pin(e.Edge.Handle, "x", null).Ok, "終了するときは固定をすべて外す (終了後は固定しない)");

        RunRulesInManager(Check);
        RunRecover(Check);
    }

    static void RunRulesInManager(Action<bool, string> Check)
    {
        var e = new Env();
        var m = e.M;
        e.S.Rules.Add(Rule("vlc.exe", null, "b-br"));
        e.S.Rules.Add(Rule("ms-teams*", "会議"));
        e.S.Rules.Add(Rule("cmd.exe"));
        e.S.Rules.Add(Rule("notepad.exe", null, null, false));
        var t0 = new DateTime(2026, 10, 28, 10, 0, 0);
        var vlc = e.Api.Add("動画.mp4 - VLC", Vlc, new Rectangle(200, 200, 1000, 700));
        m.OnEvent(P.WinEvt.Show, vlc.Handle, t0);
        Check(!m.IsPinned(vlc.Handle), "ウィンドウが出てすぐには固定しない (少し待つ)");
        m.Tick(t0.AddMilliseconds(10));
        Check(m.IsPinned(vlc.Handle) && vlc.Topmost && vlc.Rect == new Rectangle(1424, 696, 480, 320) && m.Find(vlc.Handle).How.StartsWith("自動"), "exe のルールで自動で固定し、プリセット「右下の小窓」に");
        m.Unpin(vlc.Handle);
        m.OnEvent(P.WinEvt.Foreground, vlc.Handle, t0.AddSeconds(1));
        m.Tick(t0.AddSeconds(2));
        Check(!m.IsPinned(vlc.Handle), "自分で外したウィンドウは、また自動で固定しない");

        var chat = e.Api.Add("チャット | Microsoft Teams", Teams, new Rectangle(100, 100, 900, 700));
        m.OnEvent(P.WinEvt.Show, chat.Handle, t0); m.Tick(t0.AddSeconds(3));
        Check(!m.IsPinned(chat.Handle), "タイトルが合わなければ固定しない");
        chat.Title = "週次会議 | Microsoft Teams";
        m.OnEvent(P.WinEvt.NameChange, chat.Handle, t0.AddSeconds(4)); m.Tick(t0.AddSeconds(5));
        Check(m.IsPinned(chat.Handle) && chat.Rect == new Rectangle(100, 100, 900, 700), "タイトルが「会議」を含むようになったら固定 (プリセットなし: 位置はそのまま)");

        var adm = e.Api.Add("管理者: cmd", @"C:\Windows\System32\cmd.exe", new Rectangle(0, 0, 500, 300)); adm.Elevated = true;
        int notes = e.Notices.Count;
        m.OnEvent(P.WinEvt.Foreground, adm.Handle, t0); m.Tick(t0.AddSeconds(6));
        Check(!m.IsPinned(adm.Handle) && e.Notices.Count == notes, "ルールに合っても管理者のウィンドウはだまってとばす");
        Check(!m.TryRule(e.Note.Handle), "使わないルールでは固定しない");

        var vlc2 = e.Api.Add("2 本目 - VLC", Vlc, new Rectangle(0, 0, 400, 300));
        var hidden = e.Api.Add("隠れた VLC", Vlc, new Rectangle(0, 0, 400, 300)); hidden.Visible = false;
        int n = m.ScanRules();
        Check(n == 1 && m.IsPinned(vlc2.Handle) && !m.IsPinned(hidden.Handle) && !m.IsPinned(vlc.Handle), "開いているウィンドウにまとめて使う (見えないもの・外したものは除く)");
        m.ForgetRuleHistory();
        Check(m.ScanRules() == 1 && m.IsPinned(vlc.Handle), "「今すぐ使う」では外したものにも使い直す");
        e.S.Rules.Clear();
        var vlc3 = e.Api.Add("3 - VLC", Vlc, new Rectangle(0, 0, 400, 300));
        m.OnEvent(P.WinEvt.Show, vlc3.Handle, t0); m.Tick(t0.AddSeconds(9));
        Check(!m.IsPinned(vlc3.Handle) && m.ScanRules() == 0, "ルールがなければ何もしない");
        m.Dispose();
    }

    static void RunRecover(Action<bool, string> Check)
    {
        var api = new P.FakeWindows();
        var a = api.Add("a", Notepad, new Rectangle(0, 0, 300, 300)); a.Topmost = true;
        var b = api.Add("b", Vlc, new Rectangle(0, 0, 300, 300)); b.Topmost = true;
        var c = api.Add("c", Vlc, new Rectangle(0, 0, 300, 300)); c.Topmost = true;
        var f = new P.PinRecordFile();
        f.Pinned.Add(new P.PinRecord { Hwnd = a.Handle.ToInt64(), Pid = a.Pid, Exe = Notepad });
        f.Pinned.Add(new P.PinRecord { Hwnd = b.Handle.ToInt64(), Pid = b.Pid + 1, Exe = Vlc });        // 別のプロセス (ハンドルが使い回された)
        f.Pinned.Add(new P.PinRecord { Hwnd = c.Handle.ToInt64(), Pid = c.Pid, Exe = Notepad });     // exe が違う
        f.Pinned.Add(new P.PinRecord { Hwnd = 0x7777, Pid = 1, Exe = Notepad });                        // もうない
        f.Pinned.Add(null);
        Check(P.PinManager.Recover(api, f) == 1 && !a.Topmost && b.Topmost && c.Topmost, "前回落ちて残った固定を外す (同じウィンドウと確かめられたものだけ)");
        Check(P.PinManager.Recover(api, null) == 0, "記録がなければ何もしない");
    }

    // ---------------------------------------------------------------- 画面
    public static void RunUi(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "pin-ui");
        try
        {
            var e = new Env();
            var m = e.M;
            int saves = 0, changed = 0;
            var f = new P.MainForm(m);
            f.SaveSettings = () => { saves++; P.PinStore.Save(e.S); };
            f.SettingsChanged = () => changed++;
            f.HotkeyText = "Ctrl + Alt + T";
            f.Show(); Application.DoEvents();
            try
            {
                Check(f.Tabs.SelectedIndex == 0 && f.PinList.Items.Count == 0 && f.PinList.EmptyText.Contains("Ctrl + Alt + T"), "固定中の一覧: 空のときはショートカットの案内 (" + f.PinList.EmptyText.Split('\n')[0] + ")");
                m.Pin(e.Note.Handle, P.PinManager.HowHotkey, null);
                m.Pin(e.Edge.Handle, P.PinManager.HowTitleBar, null);
                Application.DoEvents();
                Check(f.PinList.Items.Count == 2 && f.Text.Contains("Teal Pin"), "固定するとすぐ一覧に出る");
                Check(f.IconFor(Notepad, "メモ帳") != null && f.IconFor(null, "電卓") != null && f.IconFor(null, null) != null, "アイコン (exe から。なければ頭文字)");
                f.PinList.SelectedIndex = 0; Application.DoEvents();
                Check(f.BtnUnpin.Enabled && f.BtnFocus.Enabled && f.BtnPresetFor.Enabled && f.BtnUnpinAll.Enabled, "選ぶとボタンが使える");
                e.Api.ForegroundHandle = e.Calc.Handle;
                f.FocusSelected();
                Check(e.Api.ForegroundHandle == e.Note.Handle, "「前面に出す」");
                f.BuildPresetMenu(f.PresetMenu, e.Note.Handle);
                Check(f.PresetMenu.Items.Count == 6, "「位置とサイズ ▾」のメニューにプリセット");
                f.PresetMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "左半分").PerformClick();
                Check(e.Note.Rect == new Rectangle(0, 0, 960, 1032) && f.LastMessage == "「左半分」にしました", "一覧からプリセットを使う");
                f.UnpinSelected(); Application.DoEvents();
                Check(f.PinList.Items.Count == 1 && !e.Note.Topmost && f.LastMessage.StartsWith("固定を外しました"), "選んだウィンドウの固定を外す");
                f.UnpinAll(); Application.DoEvents();
                Check(f.PinList.Items.Count == 0 && !e.Edge.Topmost && f.LastMessage == "1 個のウィンドウの固定を外しました" && !f.BtnUnpinAll.Enabled, "すべての固定を外す");
                f.BtnPinOther.SimulateClick(); Application.DoEvents();
                var other = f.PinOtherMenu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToList();
                f.PinOtherMenu.Close();
                Check(other.Contains("電卓") && !other.Contains("Teal Pin") && !other.Any(t => t.Contains("管理者")), "「ほかのウィンドウを固定 ▾」(" + string.Join(" / ", other) + ")");

                // 自動で固定
                f.ShowPage(1); Application.DoEvents();
                Check(f.RuleList.Items.Count == 0 && !f.BtnEditRule.Enabled, "ルールの一覧 (空)");
                f.EditRuleHook = r => Rule("calc", "電卓", "b-tr");
                f.AddRule(); Application.DoEvents();
                Check(e.S.Rules.Count == 1 && f.RuleList.Items.Count == 1 && saves > 0 && m.IsPinned(e.Calc.Handle) && e.Calc.Rect == new Rectangle(1424, 16, 480, 320), "ルールを追加すると、開いているウィンドウにもすぐ使う");
                f.RuleList.SelectedIndex = 0;
                f.ToggleSelectedRule();
                Check(!e.S.Rules[0].Enabled && f.BtnToggleRule.Text == "使う", "ルールを使わないように");
                f.EditRuleHook = r => { Check(r == e.S.Rules[0], "編集: 今のルールを渡す"); var x = Rule("calc.exe"); x.Id = r.Id; return x; };
                f.EditSelectedRule();
                Check(e.S.Rules[0].TitleContains == null && e.S.Rules[0].Enabled, "ルールを編集");
                f.EditRuleHook = r => null;
                f.EditSelectedRule();
                Check(e.S.Rules.Count == 1, "キャンセルなら変えない");
                f.ConfirmHook = q => false; f.DeleteSelectedRule();
                Check(e.S.Rules.Count == 1, "削除の確認でいいえ");
                f.ConfirmHook = q => true; f.RuleList.SelectedIndex = 0; f.DeleteSelectedRule();
                Check(e.S.Rules.Count == 0 && m.IsPinned(e.Calc.Handle), "ルールを削除 (固定中のウィンドウはそのまま)");

                // 位置とサイズ
                f.ShowPage(2); Application.DoEvents();
                Check(f.PresetList.Items.Count == 6, "プリセットの一覧 (組み込み 6 個)");
                f.PresetList.SelectedIndex = 0;
                Check(!f.BtnRenamePreset.Enabled && !f.BtnDeletePreset.Enabled, "組み込みは名前を変えたり削除したりできない");
                e.Edge.Rect = new Rectangle(960, 516, 960, 516);
                f.PresetNameHook = n => "右下 4 分の 1";
                f.SavePresetFrom(e.Edge.Handle); Application.DoEvents();
                Check(e.S.Presets.Count == 1 && e.S.Presets[0].X == 0.5 && f.PresetList.Items.Count == 7 && f.SelectedPreset == e.S.Presets[0], "ウィンドウの今の位置をプリセットに保存");
                f.PresetNameHook = n => "左半分";
                f.SavePresetFrom(e.Edge.Handle);
                Check(e.S.Presets.Count == 1 && f.LastMessage == "同じ名前のプリセットがあります", "同じ名前はだめ");
                e.S.Rules.Add(Rule("calc.exe", null, e.S.Presets[0].Id));
                f.PresetList.SelectedIndex = 6;
                f.PresetNameHook = n => { Check(n == "右下 4 分の 1", "名前を変える: 今の名前を出す"); return "四分の一"; };
                f.RenameSelectedPreset();
                Check(e.S.Presets[0].Name == "四分の一", "名前を変える");
                string asked = null; f.ConfirmHook = q => { asked = q; return true; };
                f.DeleteSelectedPreset();
                Check(e.S.Presets.Count == 0 && e.S.Rules[0].PresetId == null && asked != null && asked.Contains("1 個"), "削除すると、使っていたルールは「変えない」に");
                e.S.Rules.Clear();

                // 動作
                f.ShowPage(3); Application.DoEvents();
                Check(f.SwTitleBar.Checked && f.SwBorder.Checked && f.BorderWidthBox.SelectedIndex == 2, "動作: 今の設定を表示");
                changed = 0;
                f.SwTitleBar.Checked = false; Application.DoEvents();
                Check(!e.S.TitleBarMenu && changed == 1, "タイトルバーのメニューをオフ (常駐部分に知らせる)");
                var cbd = e.Borders.Last(b => !b.Disposed);
                f.SwBorder.Checked = false; Application.DoEvents();
                Check(!e.S.ShowBorder && !cbd.Visible && !f.BorderWidthBox.Enabled, "枠線をオフにするとすぐ消える");
                f.SwBorder.Checked = true; f.BorderWidthBox.SelectedIndex = 4; Application.DoEvents();
                Check(e.S.BorderWidth == 5 && cbd.Visible && cbd.Strips[0].Height == 5, "枠線の太さ");
                var back = P.PinStore.Load();
                Check(!back.TitleBarMenu && back.BorderWidth == 5, "設定は保存される");
                f.HotkeyText = null;
                Check(f.PinList.EmptyText.Contains("タイトルバー"), "ショートカットがオフなら、タイトルバーの案内");
                var launched = new List<string>();
                PC.HubLink.LaunchHook = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
                f.SettingsRequested += () => PC.HubLink.OpenSettings(f, "TealPin");
                f.BtnHubSettings.SimulateClick();
                PC.HubLink.LaunchHook = null;
                Check(launched.Count == 1 && launched[0] == "TealHub.exe --settings TealPin", "ショートカット・常駐は Teal Hub の設定ページで (" + string.Join(",", launched) + ")");
            }
            finally { f.Shutdown(); m.Dispose(); }

            // ダイアログ
            using (var rf = new P.RuleForm(null, e.S.AllPresets(), new List<P.RunningApp> { new P.RunningApp { Exe = "vlc.exe", AppName = "VLC", Title = "動画" }, new P.RunningApp { Exe = "vlc.exe", AppName = "VLC", Title = "2" } }))
            {
                rf.Show(); Application.DoEvents();
                rf.ExeBox.Text = "*";
                rf.OkButton.SimulateClick();
                Check(rf.Result == null && rf.ErrorLabel.Text.Length > 0 && rf.Visible, "ルールの入力: すべてに合うものはエラー");
                rf.ExeBox.Text = "C:\\Apps\\VLC.EXE"; rf.TitleBox.Text = " 会議 "; rf.PresetBox.SelectedIndex = 1;
                rf.OkButton.SimulateClick();
                Check(rf.Result != null && rf.Result.Exe == "vlc.exe" && rf.Result.TitleContains == "会議" && rf.Result.PresetId == "b-br" && rf.Result.Enabled && rf.DialogResult == DialogResult.OK, "ルールの入力: exe の名前にそろえ、プリセットを選べる");
            }
            using (var rf = new P.RuleForm(Rule("x.exe", "t", "b-left", false), e.S.AllPresets(), new List<P.RunningApp>()))
            {
                rf.Show(); Application.DoEvents();
                Check(rf.ExeBox.Text == "x.exe" && rf.TitleBox.Text == "t" && (string)rf.PresetBox.SelectedItem == "左半分" && !rf.EnabledSwitch.Checked, "ルールの編集: 今の値を出す");
            }
            using (var nf = new P.PresetNameForm("名前", "", "", n => P.PresetMath.ValidateName(n, e.S.AllPresets(), null)))
            {
                nf.Show(); Application.DoEvents();
                nf.NameBox.Text = "左半分"; nf.OkButton.SimulateClick();
                Check(nf.Visible && nf.ErrorLabel.Text == "同じ名前のプリセットがあります", "プリセットの名前: 同じ名前はエラー");
                nf.NameBox.Text = " 新しい "; nf.OkButton.SimulateClick();
                Check(nf.PresetName == "新しい" && nf.DialogResult == DialogResult.OK, "プリセットの名前: OK");
            }
        }
        finally { RestoreData(tmp); }
    }

    // ---------------------------------------------------------------- 常駐 (通知領域)
    static void WritePrefs(string json)
    {
        var p = PC.AppPrefs.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, json);
    }

    static P.FakeWindows TrayApi(out P.FakeWindow note, out P.FakeWindow calc)
    {
        var api = new P.FakeWindows();
        note = api.Add("メモ.txt - メモ帳", Notepad, new Rectangle(300, 200, 800, 600)); note.AppName = "メモ帳";
        calc = api.Add("電卓", @"C:\Windows\System32\calc.exe", new Rectangle(1500, 60, 340, 520)); calc.AppName = "電卓";
        return api;
    }

    static void Pump(int ms) { var until = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(5); } }

    public static void RunTray(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "pin-tray");
        PC.Startup.RunKey = new PC.MemoryRunKey();
        PC.Startup.Folder = new PC.StartupFolder(Path.Combine(tmp, "pin-startup"), p => null);
        P.FakeWindow note, calc;
        try
        {
            // ---- はじめて (Teal Hub の設定がまだない): 常駐オン・Ctrl+Alt+T
            var api = TrayApi(out note, out calc);
            var t = new P.PinTray(new string[0], true, api);
            t.ExitAction = () => { };
            Application.DoEvents();
            Check(t.Resident && t.TrayVisible && !t.Window.Visible && t.Demo, "常駐オンが初期設定: ログイン時は画面を出さずに通知領域に");
            Check(t.Hotkey.ToString() == "Ctrl+Alt+T" && t.Window.HotkeyText == "Ctrl + Alt + T" && P.PinTray.DefaultHotkey.ToString() == "Ctrl+Alt+T", "初期設定のショートカットは Ctrl+Alt+T");
            Check(PC.AppHost.HotkeyRequested != null && PC.AppHost.ExitRequested != null && PC.AppHost.ReloadRequested != null && PC.AppHost.ShowRequested != null, "Teal Hub からの合図 (Hotkey・Exit・Reload・Show) を受け取る");

            api.ForegroundHandle = note.Handle;
            PC.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(note.Topmost && t.Manager.Count == 1 && !t.Window.Visible && t.LastNotice == "📌 最前面に固定しました", "ショートカット: 前面のウィンドウを固定 (画面は出さない)");
            var rec = File.ReadAllText(Path.Combine(PC.DataStore.DirFor("TealPin"), "pinned.json"));
            Check(rec.Contains(note.Handle.ToInt64().ToString()), "固定中のウィンドウを pinned.json に控える (落ちたとき用)");
            PC.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(!note.Topmost && t.Manager.Count == 0, "もう一度押すと外す");

            // 通知領域のメニュー
            t.Manager.OnEvent(P.WinEvt.Foreground, calc.Handle, DateTime.Now);
            t.RefreshMenu();
            var items = t.TrayMenu.Items.Cast<ToolStripItem>().ToList();
            var pinFront = items.First(i => i.Text.StartsWith("📌 「電卓」"));
            Check(items[0].Text == "開く" && pinFront.Enabled && items.Any(i => i.Text == "設定 (Teal Hub)…") && items.Last().Text.StartsWith("終了"), "通知領域のメニュー: 開く・「電卓」を固定・設定・終了");
            pinFront.PerformClick();
            api.ForegroundHandle = note.Handle; PC.AppHost.HotkeyRequested();
            t.RefreshMenu();
            items = t.TrayMenu.Items.Cast<ToolStripItem>().ToList();
            var calcItem = items.OfType<ToolStripMenuItem>().First(i => i.Text == "電卓");
            Check(t.Manager.Count == 2 && calc.Topmost && items.Any(i => i.Text == "メモ.txt - メモ帳") && calcItem.DropDownItems.Count == 3, "固定中のウィンドウがメニューに (前面に出す・固定を外す・位置とサイズ)");
            var sizes = (ToolStripMenuItem)calcItem.DropDownItems[2];
            sizes.DropDownItems.Cast<ToolStripItem>().First(i => i.Text == "右下の小窓").PerformClick();
            Check(calc.Rect == new Rectangle(1424, 696, 480, 320), "通知領域からプリセットを使う");
            ((ToolStripMenuItem)calcItem.DropDownItems[1]).PerformClick();
            Check(!calc.Topmost && t.Manager.Count == 1, "通知領域から固定を外す");
            var border = items.OfType<ToolStripMenuItem>().First(i => i.Text == "ティールの枠線");
            Check(border.Checked, "枠線のオン・オフ (チェックつき)");
            border.PerformClick();
            Check(!t.Manager.Settings.ShowBorder && !P.PinStore.Load().ShowBorder, "通知領域から枠線をオフ (保存する)");
            border.PerformClick();
            var tb = items.OfType<ToolStripMenuItem>().First(i => i.Text.StartsWith("タイトルバー"));
            tb.PerformClick();
            Check(!t.Manager.Settings.TitleBarMenu && !t.Window.SwTitleBar.Checked, "タイトルバーのメニューのオン・オフ (画面にも反映)");
            tb.PerformClick();
            items.First(i => i.Text == "すべての固定を外す").PerformClick();
            Check(t.Manager.Count == 0 && !note.Topmost, "通知領域から「すべての固定を外す」");

            // 画面の開閉
            t.ToggleFromTray(); Application.DoEvents();
            Check(t.Window.Visible, "通知領域のクリックで開く");
            t.Window.Close(); Application.DoEvents();
            Check(!t.Window.Visible && !t.IsExited && t.TipCount == 1 && P.PinStore.Load().TrayTipShown, "×では隠れて常駐を続ける (初回だけ案内)");
            t.ShowWindow(); t.HideWindow(); Application.DoEvents();
            Check(t.TipCount == 1, "案内は 1 回だけ");
            var launched = new List<string>();
            PC.HubLink.LaunchHook = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
            t.ShowSettings();
            PC.HubLink.LaunchHook = null;
            Check(launched.Count == 1 && launched[0] == "TealHub.exe --settings TealPin", "「設定」は Teal Hub の設定ページ");

            // Teal Hub でショートカットを変えた / オフにした → Reload
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealPin\",\"hotkey\":\"Ctrl+Shift+F12\",\"resident\":true}]}");
            PC.AppHost.ReloadRequested();
            Check(t.Hotkey.ToString() == "Ctrl+Shift+F12" && t.Window.HotkeyText == "Ctrl + Shift + F12", "Teal Hub で変えたショートカットを表示");
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealPin\",\"hotkey\":\"none\",\"resident\":true}]}");
            PC.AppHost.ReloadRequested();
            Check(t.Hotkey.IsEmpty && t.Window.HotkeyText == null, "ショートカットをオフにできる");

            // 終了: 固定をすべて外す
            api.ForegroundHandle = note.Handle; t.PinForeground();
            t.Manager.Pin(calc.Handle, P.PinManager.HowList, null);
            bool exited = false; t.ExitAction = () => exited = true;
            PC.AppHost.ExitRequested();
            Check(exited && t.IsExited && !note.Topmost && !calc.Topmost && !t.TrayVisible, "終了するときは固定をすべて外し、通知領域から消える");
            var after = PC.DataStore.LoadQuiet(P.PinStore.RecoveryFile, () => new P.PinRecordFile());
            Check(after.Pinned.Count == 0, "pinned.json も空に");
            Check(t.PinForeground().Ok == false, "終了後は固定しない");

            // ---- 落ちて固定が残っていた → 次に起動したときに外す・起動時にルール
            var api2 = TrayApi(out note, out calc);
            calc.Topmost = true;
            var leftover = new P.PinRecordFile();
            leftover.Pinned.Add(new P.PinRecord { Hwnd = calc.Handle.ToInt64(), Pid = calc.Pid, Exe = calc.ExePath });
            PC.DataStore.TrySave(P.PinStore.RecoveryFile, leftover);
            var s = P.PinStore.Load(); s.Rules.Add(Rule("notepad.exe", null, "b-left")); P.PinStore.Save(s);
            var t2 = new P.PinTray(new string[0], false, api2);
            t2.ExitAction = () => { };
            Application.DoEvents();
            Check(t2.Recovered == 1 && !calc.Topmost, "前回落ちて残った固定を外す");
            Check(t2.Manager.IsPinned(note.Handle) && note.Rect == new Rectangle(0, 0, 960, 1032), "起動したとき、開いているウィンドウにもルールを使う (左半分)");
            Check(t2.Window.Visible, "ふつうに起動したら画面を出す");
            t2.ExitApp();
            Check(!note.Topmost, "終了で外す");

            // ---- 常駐オフ (ルールはなし)
            var s0 = P.PinStore.Load(); s0.Rules.Clear(); P.PinStore.Save(s0);
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealPin\",\"resident\":false}]}");
            var api3 = TrayApi(out note, out calc);
            var t3 = new P.PinTray(new string[0], true, api3);
            bool ex3 = false; t3.ExitAction = () => ex3 = true;
            Pump(200);
            Check(ex3 && !t3.Window.Visible, "常駐オフでログイン時に起動されたら、何もせず終了");

            var api4 = TrayApi(out note, out calc);
            var t4 = new P.PinTray(new string[0], false, api4);
            bool ex4 = false; t4.ExitAction = () => ex4 = true;
            Application.DoEvents();
            Check(!t4.Resident && t4.Window.Visible && t4.TrayVisible, "常駐オフ: 画面を出す");
            t4.Manager.Pin(note.Handle, P.PinManager.HowList, null);
            t4.Window.Close(); Pump(50);
            Check(!ex4 && t4.TrayVisible && t4.TipCount == 1, "常駐オフでも、固定中のウィンドウがある間は通知領域に残る (案内つき)");
            t4.Manager.UnpinAll(); Pump(50);
            Check(ex4, "固定がなくなったら終了");

            var api5 = TrayApi(out note, out calc);
            var t5 = new P.PinTray(new string[0], false, api5);
            bool ex5 = false; t5.ExitAction = () => ex5 = true;
            Application.DoEvents();
            t5.Window.Close(); Pump(50);
            Check(ex5, "常駐オフで固定がなければ、×で終了");
        }
        finally
        {
            PC.HubLink.LaunchHook = null;
            RestoreData(tmp);
        }
    }

    // ---------------------------------------------------------------- スイートへの登録
    public static void RunSuite(Action<bool, string> Check, string tmp, string dist)
    {
        var apps = H.Manifest.Load(Path.Combine(dist, "apps.json"));
        var pin = apps.FirstOrDefault(a => a.Id == "TealPin");
        Check(pin != null && pin.Name == "Teal Pin" && pin.Role == "最前面に固定" && pin.Version == "1.11.0" && pin.Resident && !string.IsNullOrEmpty(pin.ResidentNote), "apps.json に Teal Pin (常駐オンが初期設定)");
        Check(pin != null && pin.Shortcuts != null && pin.Shortcuts.Count == 1 && pin.Shortcuts[0].Default == "Ctrl+Alt+T" && pin.Shortcuts[0].Label.Contains("固定"), "ショートカットは Teal Hub が受け付ける (初期設定 Ctrl+Alt+T)");
        Check(pin != null && File.Exists(pin.ExePath) && File.Exists(pin.IconPath) && apps.Last().Id == "TealPin", "exe とアイコン (一覧のいちばん後ろ)");
        var keys = apps.Where(a => a.Shortcuts != null).SelectMany(a => a.Shortcuts.Where(s => !string.IsNullOrEmpty(s.Default)).Select(s => s.Default)).ToList();
        Check(keys.Count(k => k == "Ctrl+Alt+T") == 1 && H.HubShortcuts.DefaultHubKey.ToString() != "Ctrl+Alt+T", "ほかのアプリの初期設定と重ならない (" + string.Join(", ", keys) + ")");
        var hk = PC.HotkeySpec.FromSaved("Ctrl+Alt+T", PC.HotkeySpec.None);
        Check(PC.HotkeySpec.Validate(hk) == null, "Ctrl+Alt+T はショートカットとして使える組み合わせ");

        Check(!H.MainForm.IsWatchedFile(Path.Combine("x", "TealPin", "settings.json")) && !H.MainForm.IsWatchedFile(Path.Combine("x", "TealPin", "pinned.json")) && H.MainForm.IsWatchedFile(Path.Combine("x", "TealPlan", "tasks.json")), "Teal Hub の「今日」・検索には出さない (Teal Pin のファイルは見ない)");

        // バックアップ: 設定・ルール・プリセットは入れる。固定中の控え (pinned.json) は入れない
        var root = Path.Combine(tmp, "pin-backup");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "TealPin"));
        File.WriteAllText(Path.Combine(root, "TealPin", "settings.json"), "{\"version\":1,\"rules\":[{\"id\":\"a\",\"exe\":\"vlc.exe\",\"enabled\":true}]}");
        File.WriteAllText(Path.Combine(root, "TealPin", "pinned.json"), "{\"pinned\":[{\"hwnd\":1234,\"pid\":1,\"exe\":\"x\"}]}");
        var zip = Path.Combine(tmp, "pin-backup.zip");
        if (File.Exists(zip)) File.Delete(zip);
        int n = H.HubBackup.Create(root, zip, "1.11.0", new DateTime(2026, 10, 28, 12, 0, 0));
        List<string> names;
        using (var z = ZipFile.OpenRead(zip)) names = z.Entries.Select(x => x.FullName).ToList();
        Check(n == 1 && names.Contains("TealPin/settings.json") && !names.Contains("TealPin/pinned.json"), "バックアップに Teal Pin の設定を入れる (固定中の控えは入れない)");
        var info = H.HubBackup.Inspect(zip);
        Check(info.Valid && info.Apps.Contains("TealPin"), "バックアップの中身に Teal Pin");
        File.Delete(Path.Combine(root, "TealPin", "settings.json"));
        File.WriteAllText(Path.Combine(root, "TealPin", "pinned.json"), "{\"pinned\":[]}");
        H.HubBackup.Restore(zip, root, null, "1.11.0", new DateTime(2026, 10, 28, 12, 5, 0), null);
        Check(File.Exists(Path.Combine(root, "TealPin", "settings.json")) && File.ReadAllText(Path.Combine(root, "TealPin", "settings.json")).Contains("vlc.exe"), "復元すると Teal Pin のルールも戻る");
    }

    // ---------------------------------------------------------------- Windows のみ: 本物の最前面
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr h, int index);
    const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x8;
    static bool IsTopmostNow(IntPtr h) { return (GetWindowLong32(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }

    public static void RunWindows(Action<bool, string> Check)
    {
        if (!WinTests.IsWindows) { Console.WriteLine("  skip Windows のみ (本物の最前面: PinTarget.exe)"); return; }
        var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PinTarget.exe");
        Check(File.Exists(exe), "build\\PinTarget.exe がある (build.ps1 -Tests)");
        if (!File.Exists(exe)) return;
        var title = "TealPin PinTarget " + Guid.NewGuid().ToString("N").Substring(0, 6);
        var proc = Process.Start(new ProcessStartInfo(exe, "\"" + title + "\"") { UseShellExecute = false });
        try
        {
            IntPtr h = IntPtr.Zero;
            for (int i = 0; i < 100 && h == IntPtr.Zero; i++) { System.Threading.Thread.Sleep(100); proc.Refresh(); h = proc.MainWindowHandle; }
            Check(h != IntPtr.Zero, "テスト用のウィンドウが開いた");
            if (h == IntPtr.Zero) return;
            var api = new P.Win32Windows();
            Check(!IsTopmostNow(h) && !api.IsTopmost(h) && api.Title(h) == title && api.IsAppWindow(h), "はじめは最前面ではない (WS_EX_TOPMOST なし)");
            var exePath = api.ExePath(api.ProcessId(h));
            Check(exePath != null && exePath.EndsWith("PinTarget.exe", StringComparison.OrdinalIgnoreCase), "プロセスの exe がわかる (" + exePath + ")");
            var borders = new List<P.IBorder>();
            var m = new P.PinManager(api, () => { var b = new P.StripBorder(); borders.Add(b); return b; }, new P.PinSettings(), Process.GetCurrentProcess().Id);
            var r = m.Pin(h, P.PinManager.HowHotkey, null);
            Check(r.Ok && IsTopmostNow(h), "固定すると WS_EX_TOPMOST が付く (" + r.Message + ")");
            Check(borders.Count == 1 && borders[0].Visible, "ティールの枠線 (重ねたウィンドウ 4 枚) を出す");
            var frame = api.FrameBounds(h);
            Check(frame.Width > 100 && api.WorkArea(h).Width > 0 && api.Scale(h) >= 1, "枠の位置・作業領域・拡大率がわかる (" + frame + ", " + api.Scale(h) + ")");
            var ar = m.ApplyPreset(h, m.Settings.FindPreset("b-br"));
            var frame2 = api.FrameBounds(h);
            var work = api.WorkArea(h);
            Check(ar.Ok && work.Contains(frame2) && Math.Abs(frame2.Right - (work.Right - (int)Math.Round(16 * api.Scale(h)))) <= 2, "プリセット「右下の小窓」で作業領域の右下へ (" + frame2 + " / " + work + ")");
            Check(IsTopmostNow(h), "動かしても最前面のまま");
            r = m.Unpin(h);
            Check(r.Ok && !IsTopmostNow(h) && !borders[0].Visible, "外すと WS_EX_TOPMOST が消え、枠線も消える");
            m.Pin(h, P.PinManager.HowHotkey, null);
            m.Dispose();
            Check(!IsTopmostNow(h), "終了するとき (Dispose) に固定を外す");
            Check(P.Win32Windows.IsCaption(h, new Point(frame2.Left + frame2.Width / 2, frame2.Top + 8)) == true, "タイトルバーかどうか (WM_NCHITTEST = HTCAPTION)");
            Check(P.Win32Windows.IsCaption(h, new Point(frame2.Left + frame2.Width / 2, frame2.Top + frame2.Height / 2)) == false, "ウィンドウの中はタイトルバーではない");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            proc.Dispose();
        }
    }
}
