// 1.12.0 のテスト: 新しいアプリ Teal Macro (クリック・キーの連打と、操作の記録・再生)
//   設定とマクロのデータ・記録 (Recorder)・連打と再生 (FakeInput / FakeClock で時間を進める)・MacroCore・画面・常駐・
//   Teal Hub の 2 つめ以降のショートカット (slot・slotsFile・設定ページ)・バックアップ。
//   Windows だけ: SendInput で本物の文字入力が届くか、フックが自分の (送った) 入力を記録しないか
extern alias MACRO;
extern alias AM;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using M = MACRO::TealSuite.Macro;
using MC = MACRO::TealSuite.Common;
using H = AM::TealSuite.Hub;
using HC = AM::TealSuite.Common;

public static class V1120Tests
{
    static int notified;

    static void UseData(string tmp, string name)
    {
        var d = Path.Combine(tmp, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        MC.AppInfo.Init("TealMacro", "Teal Macro", "連打＆マクロ");
        MC.HotkeySlots.NotifyHub = () => { notified++; return true; };
    }

    static void RestoreData(string tmp) { Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", Path.Combine(tmp, "data")); }

    static MC.HotkeySpec K(string s) { MC.HotkeySpec k; M.KeyText.TryParse(s, out k); return k; }

    /// <summary>テスト用の MacroCore (その場で最後まで動かす)</summary>
    static M.MacroCore Core(M.FakeInput api, M.FakeHook hook, M.MacroSettings s = null, M.MacroFile f = null)
    {
        var c = new M.MacroCore(s ?? new M.MacroSettings(), f ?? new M.MacroFile(), api, hook);
        c.Synchronous = true;
        c.TestClock = new M.FakeClock();
        return c;
    }

    // ---------------------------------------------------------------- データ
    public static void RunModel(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "macro-model");
        try
        {
            var c = new M.ClickerSettings();
            Check(c.Action == "left" && c.PerSecond == 10 && !c.StopByCount && !c.StopByTime && c.Target == "cursor" && c.StartDelaySec == 3 && c.HoldMs == 10, "初期設定: 左クリックを 1 秒に 10 回・止めるまで・マウスのある場所・3 秒後に開始");
            Check(c.Summary() == "左クリックを 1 秒に 10 回 ・ 止めるまで", "要約: " + c.Summary());
            c.StopByCount = true; c.Count = 1500; c.StopByTime = true; c.Seconds = 90;
            Check(c.Summary() == "左クリックを 1 秒に 10 回 ・ 1,500 回 か 1 分 30 秒で止める", "要約 (回数と時間): " + c.Summary());
            c.Action = "key"; c.Key = "ctrl+v";
            c.Normalize();
            Check(c.Key == "Ctrl+V" && c.Summary().StartsWith("Ctrl + V キーを 1 秒に 10 回"), "キー: " + c.Summary());
            var bad = new M.ClickerSettings { Action = "x", Key = "???", PerSecond = 5000, Count = -3, Seconds = 0, Target = "y", Points = null, HoldMs = 9999, StartDelaySec = 99 };
            bad.Normalize();
            Check(bad.Action == "left" && bad.Key == "Space" && bad.PerSecond == 100 && bad.Count == 1 && bad.Seconds == 1 && bad.Target == "cursor" && bad.Points != null && bad.HoldMs == 500 && bad.StartDelaySec == 10, "おかしな値は直す (速さは 1〜100 回/秒)");
            var pts = new M.ClickerSettings { Target = "points" };
            Check(pts.Summary().EndsWith("位置がまだありません") && M.MacroCore.CheckClicker(pts).Contains("位置がまだありません"), "決めた位置がないときは始められない");
            pts.Points.Add(new M.TargetPoint { X = 1, Y = 2 }); pts.Points.Add(new M.TargetPoint { X = 3, Y = 4 });
            Check(pts.Summary().EndsWith("2 か所を順番に") && M.MacroCore.CheckClicker(pts) == null, "2 か所以上なら順番に");
            for (int i = 0; i < 60; i++) pts.Points.Add(new M.TargetPoint());
            pts.Normalize();
            Check(pts.Points.Count == 50, "決めた位置は 50 か所まで");
            var tp = new M.TargetPoint { X = 10, Y = 20, Exe = "game.exe", Title = "とても長いタイトルのウィンドウ - とても長いタイトルのウィンドウ", RelX = 5, RelY = 6 };
            Check(tp.Describe(false) == "画面の 10, 20" && tp.Describe(true).StartsWith("「とても長い") && tp.Describe(true).EndsWith("」の中の 5, 6") && tp.Describe(true).Contains("…"), "位置の説明: " + tp.Describe(true));

            // キーの文字
            Check(K("Space").Key == Keys.Space && K("a").Key == Keys.A && K("Enter").Key == Keys.Return && K("Esc").Key == Keys.Escape && K("↑").Key == Keys.Up && K("F5").Key == Keys.F5 && K("5").Key == Keys.D5, "キーの名前を読む (1 つのキーだけでも)");
            Check(K("Ctrl+Shift+S").Mods == (MC.HotMods.Ctrl | MC.HotMods.Shift) && K("Ctrl+Shift+S").Key == Keys.S, "組み合わせ (Ctrl+Shift+S)");
            MC.HotkeySpec dummy;
            Check(!M.KeyText.TryParse("", out dummy) && !M.KeyText.TryParse("Foo+A", out dummy) && !M.KeyText.TryParse("123", out dummy) && !M.KeyText.TryParse(null, out dummy), "読めないキーは false");
            Check(M.KeyText.Display(K("Ctrl+Down")) == "Ctrl + ↓" && M.KeyText.Display("Space") == "Space" && M.KeyText.FromKeyData(Keys.Control | Keys.ControlKey).IsEmpty, "表示 (Ctrl + ↓)・修飾キーだけなら空");

            // 手順とマクロ
            var st = M.MacroStep.Click(100, 200, M.MouseBtn.Right, 2);
            Check(st.Title == "ダブルクリック (右)" && st.Detail == "100, 200" && st.IsValid, "手順の名前: " + st.Title);
            Check(M.MacroStep.KeyPress("Ctrl+C").Title == "キー Ctrl + C" && M.MacroStep.TypeText("あ\nい").Detail == "「あ↵い」" && M.MacroStep.Wait(1500).Detail == "1.5 秒", "キー・文字・待つの表示 (" + M.MacroStep.Wait(1500).Detail + ")");
            Check(!M.MacroStep.TypeText("").IsValid && !M.MacroStep.KeyPress("???").IsValid && new M.MacroStep { Kind = "zzz" }.IsValid == false, "空の文字・読めないキー・知らない種類は使えない");
            var m = new M.MacroDef { Id = "abcd1234", Name = "テスト" };
            m.Steps.Add(M.MacroStep.Click(1, 1, M.MouseBtn.Left));
            var w = M.MacroStep.Wait(2000); w.Delay = 500; m.Steps.Add(w);
            Check(m.TotalMs == 2500 && m.Summary() == "2 手順 ・ 2.5 秒 ・ 1 回", "マクロの要約: " + m.Summary());
            m.Speed = 2; m.Repeat = 0;
            Check(m.TotalMs == 1250 && m.Summary().Contains("ずっとくり返す") && m.Summary().Contains("2 倍"), "速さとくり返し: " + m.Summary());
            m.Speed = 3; m.Repeat = -5; m.Normalize();
            Check(m.Speed == 4 && m.Repeat == 1, "おかしな速さ (3 倍 → いちばん近い 4 倍)・回数 (-5 → 1) は直す (" + m.Speed + ", " + m.Repeat + ")");
            Check(m.Slot == "m.abcd1234" && MC.HotkeySlots.ValidSlot(m.Slot), "マクロごとのショートカットの名前 (m.<id>)");

            var file = new M.MacroFile();
            Check(file.NextName() == "マクロ 1", "はじめの名前は「マクロ 1」");
            file.Macros.Add(new M.MacroDef { Id = "a", Name = "マクロ 1" });
            file.Macros.Add(new M.MacroDef { Id = "a", Name = "日報" });
            file.Normalize();
            Check(file.NextName() == "マクロ 2" && file.Macros[0].Id != file.Macros[1].Id, "次の名前・ID の重複は直す");
            Check(file.ValidateName("  ", null) != null && file.ValidateName("日報", null) != null && file.ValidateName("日報", file.Macros[1]) == null && file.ValidateName(new string('x', 41), null) != null && file.ValidateName("新しい", null) == null, "名前の確認 (空・重複・41 文字はだめ)");
            var decl = file.HubSlots();
            Check(decl.Count == 2 && decl[1].Slot == file.Macros[1].Slot && decl[1].Label == "マクロ「日報」を再生 / 停止", "Teal Hub に渡す一覧: " + decl[1].Label);

            // 保存・読み込み・壊れたファイル
            var s = new M.MacroSettings();
            s.Clicker.PerSecond = 25; s.Clicker.Points.Add(new M.TargetPoint { X = 5, Y = 6, Exe = "a.exe", RelX = 1, RelY = 2 }); s.Tab = 1;
            Check(M.MacroStore.SaveSettings(s) && M.MacroStore.SaveMacros(file), "保存できる");
            var s2 = M.MacroStore.LoadSettings(); var f2 = M.MacroStore.LoadMacros();
            Check(s2.Clicker.PerSecond == 25 && s2.Clicker.Points.Count == 1 && s2.Clicker.Points[0].Exe == "a.exe" && s2.Tab == 1 && f2.Macros.Count == 2 && f2.Macros[1].Name == "日報", "読み込むと同じ");
            File.WriteAllText(Path.Combine(MC.DataStore.DirFor("TealMacro"), "macros.json"), "{ broken");
            Check(M.MacroStore.LoadMacros().Macros.Count == 0, "壊れたファイルなら空で始める");
            notified = 0;
            Check(M.MacroStore.PublishSlots(file) && notified == 1 && File.Exists(Path.Combine(MC.DataStore.DirFor("TealMacro"), "hub-slots.json")), "hub-slots.json を書いて Teal Hub に知らせる");
            Check(!M.MacroStore.PublishSlots(file) && notified == 1, "変わっていなければ書かない・知らせない");
            var read = MC.HotkeySlots.ReadDynamic("TealMacro", "hub-slots.json");
            Check(read.Count == 2 && read[0].Slot == file.Macros[0].Slot, "Teal Hub 側で読める");
            Check(M.Fmt_.Ms(800) == "800 ミリ秒" && M.Fmt_.Ms(61000) == "1 分 1 秒" && M.Fmt_.Delay(0) == "", "時間の表示 (" + M.Fmt_.Ms(61000) + ")");
        }
        finally { RestoreData(tmp); }
    }

    // ---------------------------------------------------------------- 記録 → 手順
    public static void RunRecorder(Action<bool, string> Check)
    {
        var none = new MC.HotkeySpec[0];
        // クリック (押して離す・少しの動き)
        var raw = new List<M.RawEvent> {
            M.RawEvent.Mv(0, 10, 10), M.RawEvent.Mv(20, 50, 50), M.RawEvent.Mv(40, 100, 100),
            M.RawEvent.Btn(500, true, M.MouseBtn.Left, 100, 100), M.RawEvent.Mv(520, 101, 101), M.RawEvent.Btn(560, false, M.MouseBtn.Left, 101, 101),
            M.RawEvent.Mv(900, 300, 300) };
        var st = M.Recorder.Build(raw, none, true);
        Check(st.Count == 2 && st[0].Kind == "move" && st[0].Path != null && st[0].Path.Count == 3 && st[0].X == 100 && st[0].Delay == 0, "マウスの動きは 1 つの手順に (道すじつき)");
        Check(st[1].Kind == "click" && st[1].X == 100 && st[1].Btn == M.MouseBtn.Left && st[1].Delay == 460, "押して離す = クリック (間は " + (st.Count > 1 ? st[1].Delay : -1) + " ミリ秒)");
        Check(st.All(x => x.Kind != "move" || x.X != 300), "最後の動き (停止ボタンへ向かう) は使わない");
        var noMoves = M.Recorder.Build(raw, none, false);
        Check(noMoves.Count == 1 && noMoves[0].Kind == "click" && noMoves[0].Delay == 0, "動きを記録しない設定ならクリックだけ");

        // ダブルクリック・ドラッグ
        raw = new List<M.RawEvent> {
            M.RawEvent.Btn(0, true, M.MouseBtn.Left, 5, 5), M.RawEvent.Btn(50, false, M.MouseBtn.Left, 5, 5),
            M.RawEvent.Btn(200, true, M.MouseBtn.Left, 6, 5), M.RawEvent.Btn(250, false, M.MouseBtn.Left, 6, 5),
            M.RawEvent.Btn(1000, true, M.MouseBtn.Right, 50, 50), M.RawEvent.Mv(1100, 200, 200), M.RawEvent.Btn(1200, false, M.MouseBtn.Right, 200, 200) };
        st = M.Recorder.Build(raw, none, true);
        Check(st.Count == 4 && st[0].Kind == "click" && st[0].Clicks == 2, "すぐに 2 回クリック = ダブルクリック");
        Check(st[1].Kind == "down" && st[1].Btn == M.MouseBtn.Right && st[2].Kind == "move" && st[3].Kind == "up" && st[3].X == 200, "押したまま動かす = 押す・動かす・離す (ドラッグ)");

        // キー: 組み合わせ・ショートカットは取り除く
        raw = new List<M.RawEvent> {
            M.RawEvent.Key(0, false, Keys.M), M.RawEvent.Key(5, false, Keys.Menu), M.RawEvent.Key(6, false, Keys.ControlKey),   // 記録を始めたショートカットを離す
            M.RawEvent.Key(100, true, Keys.LControlKey), M.RawEvent.Key(150, true, Keys.C), M.RawEvent.Key(200, false, Keys.C), M.RawEvent.Key(250, false, Keys.LControlKey),
            M.RawEvent.Key(400, true, Keys.A), M.RawEvent.Key(450, false, Keys.A),
            M.RawEvent.Key(600, true, Keys.ShiftKey), M.RawEvent.Key(610, true, Keys.ShiftKey), M.RawEvent.Key(620, true, Keys.ShiftKey), M.RawEvent.Key(700, false, Keys.ShiftKey),
            M.RawEvent.Key(900, true, Keys.LControlKey), M.RawEvent.Key(901, true, Keys.LMenu), M.RawEvent.Key(950, true, Keys.M) };   // 記録を終えるショートカット
        var ignore = new[] { K("Ctrl+Alt+M"), K("Ctrl+Alt+Escape") };
        st = M.Recorder.Build(raw, ignore, true);
        Check(st.Count >= 2 && st[0].Kind == "key" && st[0].Key == "Ctrl+C" && st[0].Delay == 0, "Ctrl を押して C = 「キー Ctrl+C」 (" + string.Join(" | ", st.Select(x => x.Title)) + ")");
        Check(st.Count >= 2 && st[1].Kind == "key" && st[1].Key == "A" && st[1].Delay == 150, "A だけ = 「キー A」");
        Check(st.Count == 4 && st[2].Kind == "keydown" && st[2].Key == "Shift" && st[3].Kind == "keyup", "押しっぱなし (自動リピート) は押す・離すで残す (Shift)");
        Check(!st.Any(x => x.Key != null && x.Key.Contains("M")), "記録の開始・終了のショートカット (Ctrl+Alt+M) は記録しない");

        // 押していないのに離した・最後まで離さなかった・自分の画面の上のクリック
        raw = new List<M.RawEvent> {
            M.RawEvent.Btn(0, false, M.MouseBtn.Left, 1, 1),
            new M.RawEvent { Kind = M.RawKind.Down, Time = 10, X = 900, Y = 900, Button = M.MouseBtn.Left, Own = true }, new M.RawEvent { Kind = M.RawKind.Up, Time = 20, X = 900, Y = 900, Button = M.MouseBtn.Left },
            M.RawEvent.Btn(100, true, M.MouseBtn.Middle, 3, 3), M.RawEvent.Btn(120, false, M.MouseBtn.Middle, 3, 3),
            M.RawEvent.Key(200, true, Keys.B) };
        st = M.Recorder.Build(raw, none, true);
        Check(st.Count == 1 && st[0].Kind == "click" && st[0].Btn == M.MouseBtn.Middle, "記録の前から押していたもの・Teal Macro の画面の上・離さなかったキーは取り除く (" + string.Join(" | ", st.Select(x => x.Title)) + ")");
        Check(M.Recorder.Build(new List<M.RawEvent>(), none, true).Count == 0 && M.Recorder.Build(null, null, true).Count == 0, "何もなければ手順なし");

        // たくさんの動きは間引く
        raw = new List<M.RawEvent>();
        for (int i = 0; i < 20000; i++) raw.Add(M.RawEvent.Mv(i * 2, i % 1000, i / 20));
        raw.Add(M.RawEvent.Btn(40010, true, M.MouseBtn.Left, 999, 999)); raw.Add(M.RawEvent.Btn(40020, false, M.MouseBtn.Left, 999, 999));
        st = M.Recorder.Build(raw, none, true);
        Check(st.Count == 2 && st[0].Path.Count <= M.Recorder.MaxPathPoints && st[0].Path.First().T == 0 && st[0].Path.Last().X == 19999 % 1000, "動きは 15 ミリ秒ごと・400 点までに間引く (" + st[0].Path.Count + " 点)");
    }

    // ---------------------------------------------------------------- 連打と再生 (FakeInput・FakeClock)
    public static void RunRunner(Action<bool, string> Check)
    {
        var api = new M.FakeInput();
        api.Cursor = new Point(300, 300);
        var c = new M.ClickerSettings { PerSecond = 10, StopByCount = true, Count = 5 };
        var job = new M.ClickJob(c, api);
        var clock = new M.FakeClock { Job = job };
        job.Execute(clock);
        Check(job.Done == 5 && job.Reason == "count" && api.Count("down left 300,300") == 5 && api.Count("up left") == 5 && job.Finished, "5 回で止める (マウスのある場所を左クリック)");
        Check(job.ElapsedMs >= 400 && job.ElapsedMs < 500, "1 秒に 10 回 = 0.1 秒ごと (5 回で " + job.ElapsedMs + " ミリ秒。最後の 1 回のあとは待たない)");

        api = new M.FakeInput();
        c = new M.ClickerSettings { PerSecond = 20, StopByTime = true, Seconds = 2, Action = "right" };
        job = new M.ClickJob(c, api); clock = new M.FakeClock { Job = job };
        job.Execute(clock);
        Check(job.Reason == "time" && job.Done >= 39 && job.Done <= 41 && api.Count("down right") == job.Done && clock.Now <= 2000 + 60, "2 秒で止める (1 秒に 20 回で " + job.Done + " 回・右クリック)");

        api = new M.FakeInput();
        c = new M.ClickerSettings { PerSecond = 100, StopByCount = true, Count = 3, Action = "double" };
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        Check(job.Done == 3 && api.Count("down left") == 6, "ダブルクリックは 1 回で 2 度押す");

        // キー
        api = new M.FakeInput();
        c = new M.ClickerSettings { Action = "key", Key = "Ctrl+V", StopByCount = true, Count = 2 };
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        var log = api.Snapshot();
        Check(job.Done == 2 && log.Take(4).SequenceEqual(new[] { "keydown LControlKey", "keydown V", "keyup V", "keyup LControlKey" }), "キーの連打 (Ctrl を押して V を押して離す): " + string.Join(", ", log.Take(4)));

        // 決めた位置を順番に
        api = new M.FakeInput();
        c = new M.ClickerSettings { Target = "points", StopByCount = true, Count = 5 };
        c.Points.Add(new M.TargetPoint { X = 10, Y = 10 }); c.Points.Add(new M.TargetPoint { X = 20, Y = 20 }); c.Points.Add(new M.TargetPoint { X = 30, Y = 30 });
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        var downs = api.Snapshot().Where(l => l.StartsWith("down")).ToList();
        Check(downs.SequenceEqual(new[] { "down left 10,10", "down left 20,20", "down left 30,30", "down left 10,10", "down left 20,20" }), "決めた位置を上から順に、最後まで行ったら最初に戻る");
        Check(api.Cursor == new Point(20, 20), "その位置へマウスを動かしてからクリック");

        // ウィンドウの中の位置
        api = new M.FakeInput();
        var win = api.AddWindow("game.exe", "UnityWndClass", "ゲーム", new Rectangle(100, 100, 800, 600));
        var tp = M.WindowMatch.Make(new Point(150, 170), api.WindowAt(new Point(150, 170)));
        Check(tp.Exe == "game.exe" && tp.RelX == 50 && tp.RelY == 70 && tp.Title == "ゲーム", "位置を記録するとウィンドウとその中の位置も控える (" + tp.RelX + ", " + tp.RelY + ")");
        win.ClientOrigin = new Point(500, 400);
        c = new M.ClickerSettings { Target = "points", WindowRelative = true, StopByCount = true, Count = 1 };
        c.Points.Add(tp);
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        Check(api.Count("down left 550,470") == 1, "ウィンドウを動かしても、ウィンドウの中の同じ場所を押す");
        api.Windows.Clear();
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        Check(job.Reason == "nowindow" && job.Done == 0 && job.MissingWindow == "ゲーム" && M.MacroCore.ResultText(job).Contains("ゲーム"), "ウィンドウがなければ止める: " + M.MacroCore.ResultText(job));
        c.WindowRelative = false;
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        Check(api.Count("down left 150,170") == 1, "「ウィンドウに合わせる」がオフなら画面の位置");

        // 自分の画面の上は押さない
        api = new M.FakeInput { OwnWindow = new Rectangle(0, 0, 400, 400) };
        api.Cursor = new Point(100, 100);
        c = new M.ClickerSettings { StopByTime = true, Seconds = 1 };
        job = new M.ClickJob(c, api); job.Execute(new M.FakeClock { Job = job });
        Check(job.Done == 0 && job.Skipped >= 9 && api.Count("down") == 0 && M.MacroCore.ResultText(job).Contains("とばしました"), "Teal Macro の画面の上ではクリックしない (停止ボタンを押せるように): " + M.MacroCore.ResultText(job));

        // 途中で止める・押したままのボタンは離す
        api = new M.FakeInput();
        c = new M.ClickerSettings { PerSecond = 50 };
        job = new M.ClickJob(c, api);
        clock = new M.FakeClock { Job = job };
        clock.OnWait = now => { if (now >= 1000) job.Stop("esc"); };
        job.Execute(clock);
        Check(job.Reason == "esc" && job.Done >= 49 && job.Done <= 52 && api.Count("down") == api.Count("up"), "止めると止まる (止めるまで続ける設定。押したボタンは必ず離す)");
        Check(M.MacroCore.ResultText(job).EndsWith("Esc で止めました"), "結果: " + M.MacroCore.ResultText(job));

        // 間隔のばらつき
        api = new M.FakeInput();
        c = new M.ClickerSettings { PerSecond = 10, StopByCount = true, Count = 50, Jitter = true };
        job = new M.ClickJob(c, api, 42); clock = new M.FakeClock { Job = job }; job.Execute(clock);
        Check(job.Done == 50 && clock.Now > 3900 && clock.Now < 5900 && clock.Now != 4900, "ばらつかせても平均の速さは同じ (" + clock.Now + " ミリ秒)");

        // ---- マクロの再生
        api = new M.FakeInput();
        var m = new M.MacroDef { Id = "x", Name = "見本", Repeat = 2 };
        m.Steps.Add(M.MacroStep.Click(10, 20, M.MouseBtn.Left));
        var wait = M.MacroStep.Wait(1000); m.Steps.Add(wait);
        var t = M.MacroStep.TypeText("こんにちは"); t.Delay = 200; m.Steps.Add(t);
        m.Steps.Add(M.MacroStep.KeyPress("Enter"));
        var mv = M.MacroStep.Move(90, 90); mv.Path = new List<M.PathPoint> { new M.PathPoint { T = 0, X = 10, Y = 20 }, new M.PathPoint { T = 100, X = 50, Y = 50 }, new M.PathPoint { T = 200, X = 90, Y = 90 } }; m.Steps.Add(mv);
        m.Steps.Add(new M.MacroStep { Kind = "wheel", X = 90, Y = 90, Delta = -240 });
        var mj = new M.MacroJob(m, api); clock = new M.FakeClock { Job = mj };
        mj.Execute(clock);
        log = api.Snapshot();
        Check(mj.Reason == "done" && mj.Done == 12 && log.Count(l => l == "text こんにちは") == 2 && log.Count(l => l == "keydown Return" || l == "keydown Enter") == 2 && log.Count(l => l == "wheel -240") == 2, "手順を順番に 2 回くり返す (" + mj.Done + " 手順)");
        Check(log[0] == "move 10,20" && log[1] == "down left 10,20" && log.Contains("move 50,50"), "クリックはその位置へ動かしてから・動きは道すじどおり");
        Check(clock.Now >= 2 * (10 + 1000 + 200 + 10 + 200) && clock.Now < 2 * 1500, "時間も記録どおり (" + clock.Now + " ミリ秒)");
        Check(M.MacroCore.ResultText(mj) == "「見本」を 2 回再生しました", "結果: " + M.MacroCore.ResultText(mj));
        api = new M.FakeInput();
        m.Speed = 2; m.Repeat = 1;
        mj = new M.MacroJob(m, api); clock = new M.FakeClock { Job = mj }; mj.Execute(clock);
        Check(clock.Now < 800 && clock.Now > 550, "2 倍速なら半分の時間 (" + clock.Now + " ミリ秒)");

        // ずっとくり返す → 止める・押したままのキーとボタンを離す
        api = new M.FakeInput();
        var hold = new M.MacroDef { Id = "h", Name = "押したまま", Repeat = 0 };
        hold.Steps.Add(new M.MacroStep { Kind = "keydown", Key = "Shift" });
        hold.Steps.Add(new M.MacroStep { Kind = "down", X = 5, Y = 5, Button = "left" });
        hold.Steps.Add(M.MacroStep.Wait(100));
        mj = new M.MacroJob(hold, api); clock = new M.FakeClock { Job = mj };
        clock.OnWait = now => { if (now > 50) mj.Stop("hotkey"); };
        mj.Execute(clock);
        log = api.Snapshot();
        Check(mj.Reason == "hotkey" && log.Contains("keyup ShiftKey") && log.Contains("up left 5,5"), "止めたら、押したままのキーとボタンを離す (" + string.Join(", ", log) + ")");
        api = new M.FakeInput { OwnWindow = new Rectangle(0, 0, 50, 50) };
        var own = new M.MacroDef { Id = "o", Name = "o" };
        own.Steps.Add(M.MacroStep.Click(10, 10, M.MouseBtn.Left)); own.Steps.Add(new M.MacroStep { Kind = "down", X = 10, Y = 10 }); own.Steps.Add(new M.MacroStep { Kind = "up", X = 10, Y = 10 });
        mj = new M.MacroJob(own, api); mj.Execute(new M.FakeClock { Job = mj });
        Check(mj.Skipped == 2 && api.Count("down") == 0 && api.Count("up") == 0, "再生でも Teal Macro の画面の上は押さない (離すのもとばす)");
    }

    // ---------------------------------------------------------------- MacroCore (ショートカット・記録・位置)
    public static void RunCore(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "macro-core");
        try
        {
            var api = new M.FakeInput();
            api.AddWindow("notepad.exe", "Notepad", "メモ帳", new Rectangle(0, 0, 1000, 800));
            var hook = new M.FakeHook();
            var core = Core(api, hook);
            var notes = new List<string>();
            core.Notice = (msg, err) => notes.Add((err ? "! " : "") + msg);
            Check(core.State == M.CoreState.Idle && core.StateTitle == "待機中" && core.ToggleKey.ToString() == "Ctrl+Alt+R" && core.PickKey.ToString() == "Ctrl+Alt+P" && core.RecordKey.ToString() == "Ctrl+Alt+M" && core.StopKey.ToString() == "Ctrl+Alt+Esc", "初期設定のショートカット: 連打 Ctrl+Alt+R・位置 Ctrl+Alt+P・記録 Ctrl+Alt+M・停止 Ctrl+Alt+Esc");
            Check(hook.StopKey.ToString() == "Ctrl+Alt+Esc", "緊急停止のキーはフックも見る (Teal Hub が受け付けられなくても止まる)");

            // 連打: ショートカット (すぐ始める)
            core.Settings.Clicker.StopByCount = true; core.Settings.Clicker.Count = 7;
            core.OnHotkey(null);
            Check(core.State == M.CoreState.Idle && api.Count("down left") == 7 && notes.Last() == "7 回クリックしました。決めた回数で止めました", "Ctrl+Alt+R で連打 (待たずにすぐ): " + notes.Last());
            // 位置を記録 (Ctrl+Alt+P)
            api.Cursor = new Point(120, 340);
            core.OnHotkey("pick");
            var c = core.Settings.Clicker;
            Check(c.Points.Count == 1 && c.Target == "points" && c.Points[0].X == 120 && c.Points[0].Exe == "notepad.exe" && notes.Last().StartsWith("位置を記録しました (1 か所目"), "Ctrl+Alt+P で今のマウスの位置を「決めた位置」に (自動で「決めた位置」に切り替え): " + notes.Last());
            Check(M.MacroStore.LoadSettings().Clicker.Points.Count == 1, "すぐ保存する");
            api.OwnWindow = new Rectangle(100, 300, 100, 100);
            Check(core.AddPointFromCursor() == null && c.Points.Count == 1 && notes.Last().StartsWith("!"), "Teal Macro の画面の上は記録しない");
            api.OwnWindow = Rectangle.Empty;
            c.Action = "key";
            api.Cursor = new Point(10, 10); core.AddPointFromCursor();
            Check(c.Action == "left" && c.Points.Count == 2, "キーの連打中に位置を記録したら、クリックに戻す");
            c.StopByCount = true; c.Count = 4;
            api.Log.Clear();
            core.ToggleClicker(false);
            Check(api.Snapshot().Where(l => l.StartsWith("down")).SequenceEqual(new[] { "down left 120,340", "down left 10,10", "down left 120,340", "down left 10,10" }), "決めた 2 か所を交互に");

            // 記録 (Ctrl+Alt+M → 操作 → Ctrl+Alt+M)
            core.OnHotkey("record");
            Check(core.State == M.CoreState.Recording && hook.Recording && core.StateTitle == "記録中", "Ctrl+Alt+M で記録を始める");
            Check(!core.StartClicker(false) && core.State == M.CoreState.Recording && notes.Last().Contains("記録中"), "記録中は連打を始めない");
            hook.Pending.Add(M.RawEvent.Key(0, false, Keys.M));
            hook.Pending.Add(M.RawEvent.Btn(100, true, M.MouseBtn.Left, 40, 50)); hook.Pending.Add(M.RawEvent.Btn(150, false, M.MouseBtn.Left, 40, 50));
            hook.Pending.Add(M.RawEvent.Key(700, true, Keys.H)); hook.Pending.Add(M.RawEvent.Key(750, false, Keys.H));
            hook.Pending.Add(M.RawEvent.Key(900, true, Keys.ControlKey)); hook.Pending.Add(M.RawEvent.Key(901, true, Keys.Menu)); hook.Pending.Add(M.RawEvent.Key(950, true, Keys.M));
            Check(core.RecordedCount == 8 && core.StateDetail.StartsWith("8 個の操作"), "記録中は数を出す: " + core.StateDetail);
            core.OnHotkey("record");
            var m = core.Macros.Macros.LastOrDefault();
            Check(core.State == M.CoreState.Idle && m != null && m.Name == "マクロ 1" && m.Steps.Count == 2 && m.Steps[0].Kind == "click" && m.Steps[1].Key == "H" && m.Steps[1].Delay == 550, "もう一度押すと終わって「マクロ 1」ができる (ショートカットのキーは入らない): " + (m == null ? "-" : string.Join(" | ", m.Steps.Select(x => x.Title))));
            Check(core.Settings.SelectedMacro == m.Id && core.Settings.Tab == 1 && M.MacroStore.LoadMacros().Macros.Count == 1 && notes.Last().StartsWith("「マクロ 1」を記録しました (2 手順)"), "できたマクロを選んで保存: " + notes.Last());
            var decl = MC.HotkeySlots.ReadDynamic("TealMacro", "hub-slots.json");
            Check(decl.Count == 1 && decl[0].Slot == m.Slot, "Teal Hub にマクロごとのショートカットを知らせる (hub-slots.json)");
            core.StartRecording();
            Check(core.StopRecording() == null && core.Macros.Macros.Count == 1 && notes.Last().StartsWith("!") && notes.Last().Contains("記録されませんでした"), "何も操作しなければマクロを作らない");
            core.StartRecording();
            core.OnHotkey("stop");
            Check(core.State == M.CoreState.Idle && !hook.Recording, "すべて停止 (Ctrl+Alt+Esc) で記録も終わる");

            // 再生
            api.Log.Clear();
            core.OnHotkey(m.Slot);
            Check(api.Count("down left 40,50") == 1 && api.Count("keydown H") == 1 && notes.Last() == "「マクロ 1」を再生しました", "マクロごとのショートカットで再生: " + notes.Last());
            core.OnHotkey("m.nothere");
            Check(notes.Last().Contains("見つかりません"), "消したマクロのショートカット");
            Check(!core.PlayMacro("nope", true) && notes.Last().Contains("選んで"), "マクロがなければ再生しない");
            var empty = core.NewMacro();
            Check(empty.Name == "マクロ 2" && !core.PlayMacro(empty.Id, true) && notes.Last().Contains("手順がありません"), "手順のないマクロは再生しない: " + notes.Last());
            var copy = core.Duplicate(m);
            Check(copy.Name == "マクロ 1 のコピー" && copy.Steps.Count == 2 && copy.Id != m.Id && core.Macros.Macros.IndexOf(copy) == 1, "複製はすぐ後ろに「〇〇 のコピー」");
            Check(core.Duplicate(m).Name == "マクロ 1 のコピー 2", "2 つめのコピー");
            Check(core.Rename(m, "マクロ 2") != null && core.Rename(m, " 日報 ") == null && m.Name == "日報", "名前の変更 (重複は断る・前後の空白は取る)");
            Check(core.Delete(copy) && core.Macros.Find(copy.Id) == null && core.Settings.SelectedMacro != copy.Id, "削除");
            Check(MC.HotkeySlots.ReadDynamic("TealMacro", "hub-slots.json").Count == core.Macros.Macros.Count, "一覧が変わるたびに Teal Hub に知らせる");

            // 自分のショートカットを Teal Hub で変えた
            var prefs = MC.AppPrefs.Load();
            var ap = prefs.GetOrAdd("TealMacro");
            ap.Hotkey = "Ctrl+Shift+F9"; ap.SetSlotHotkey("record", "none"); ap.SetSlotHotkey(m.Slot, "Ctrl+Alt+1");
            MC.AppPrefs.Save(prefs);
            core.ReloadHotkeys();
            Check(core.ToggleKey.ToString() == "Ctrl+Shift+F9" && core.RecordKey.IsEmpty && core.MacroKey(m).ToString() == "Ctrl+Alt+1" && core.AllHotkeys().Count == 4, "Teal Hub で変えたショートカットを読み直す (記録は「なし」に)");
            core.Settings.EscStops = false;
            Check(core.StateDetail == core.LastResult, "待機中の説明は前回の結果");

            // カウントダウン (ボタンで始めたとき)
            core.Synchronous = false;
            core.Settings.Clicker.StartDelaySec = 3;
            Check(core.StartClicker(true) && core.State == M.CoreState.Countdown && core.CountdownIsClick && core.StateTitle == "3 秒後に始めます" && core.StateDetail.Contains("決めた位置をクリック"), "「▶ 開始」では 3 秒待つ (" + core.StateTitle + " / " + core.StateDetail + ")");
            core.StopAll("user");
            Check(core.State == M.CoreState.Idle && notes.Last() == "取り消しました" && api.Count("down") == api.Count("up"), "待っている間に止めたら取り消し");
            core.Dispose();
        }
        finally { RestoreData(tmp); }
    }

    // ---------------------------------------------------------------- 画面
    public static void RunUi(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "macro-ui");
        try
        {
            var api = new M.FakeInput();
            var hook = new M.FakeHook();
            var core = Core(api, hook);
            core.Notice = (msg, err) => { };
            var f = new M.MainForm(core);
            core.Notice = (msg, err) => f.Flash(msg, err);
            f.Show(); Application.DoEvents();
            Check(f.Tabs.Count == 2 && f.Tabs[0] == "連打" && f.Tabs[1] == "マクロ" && f.Tabs.SelectedIndex == 0, "タブは「連打」「マクロ」の 2 つ (はじめは連打)");
            Check(f.Banner.Title == "待機中" && f.Banner.Detail.Contains("左クリックを 1 秒に 10 回") && f.Banner.Detail.Contains("Ctrl + Alt + R で開始"), "状態の帯: 待機中・今の設定・ショートカット (" + f.Banner.Detail + ")");
            Check(f.BtnStart.Enabled && !f.BtnStop.Enabled && f.BtnStart.Text == "▶ 開始", "大きな「▶ 開始」(停止は押せない)");
            Check(!f.KeyBox.Parent.Parent.Visible && f.TargetCard.Visible && !f.AdvancedCard.Visible && !f.PointList.Parent.Parent.Visible, "はじめは必要なものだけ (キーの欄・決めた位置・くわしい設定は隠す)");

            f.ActionChips.SelectedIndex = 4;
            Check(core.Settings.Clicker.Action == "key" && f.KeyBox.Parent.Parent.Visible && !f.TargetCard.Visible && f.KeyBox.KeyValue == "Space", "「キー」を選ぶとキーの欄 (初期設定 Space)。どこを押すかは隠す");
            Check(f.KeyBox.Capture_(Keys.Control | Keys.S) && core.Settings.Clicker.Key == "Ctrl+S" && f.KeyBox.Text == "Ctrl + S" && !f.KeyBox.Capture_(Keys.ShiftKey | Keys.Shift), "キーの欄: 押したキーが入る (Ctrl だけ・Shift だけは決めない)");
            Check(M.MacroStore.LoadSettings().Clicker.Key == "Ctrl+S", "変えたらすぐ保存");
            f.ActionChips.SelectedIndex = 1;
            f.RateBox.Value = 25;
            f.CountBox.Value = 300;
            Check(core.Settings.Clicker.Action == "right" && core.Settings.Clicker.PerSecond == 25 && core.Settings.Clicker.StopByCount && f.ChkCount.Checked && core.Settings.Clicker.Count == 300, "速さ・回数を変える (回数を入れたら「回数で止める」もオン)");
            f.RateBox.Value = 500;
            Check(core.Settings.Clicker.PerSecond == 100 && f.RateBox.Value == 100, "速さは 100 回/秒まで");
            f.RateBox.Box.Text = "abc"; f.RateBox.Box.Text = "40";
            Check(core.Settings.Clicker.PerSecond == 40, "数の欄に打ち込む");
            f.TargetChips.SelectedIndex = 1;
            Check(core.Settings.Clicker.Target == "points" && f.PointList.Parent.Parent.Visible && f.PointList.Items.Count == 0 && !f.BtnRemovePoint.Enabled && f.BtnPick.Enabled, "「決めた位置」を選ぶと位置の一覧 (まだ空)");
            Check(f.Banner.Detail.Contains("位置がまだありません"), "位置がないことを帯でも知らせる: " + f.Banner.Detail);
            TealPick(core, api, new Point(11, 22));
            f.PickPoint();
            TealPick(core, api, new Point(33, 44));
            f.PickPoint();
            Check(f.PointList.Items.Count == 2 && f.PointList.SelectedIndex == 1 && core.Settings.Clicker.Points[1].X == 33, "「＋ 画面から選ぶ」で位置を足す (足したものを選ぶ)");
            f.PointList.SelectedIndex = 0; f.RemovePoint();
            Check(core.Settings.Clicker.Points.Count == 1 && core.Settings.Clicker.Points[0].X == 33, "選んだ位置を削除");
            f.ConfirmHook = q => true;
            f.ClearPoints();
            Check(core.Settings.Clicker.Points.Count == 0 && !f.BtnClearPoints.Enabled, "すべて削除 (確かめてから)");
            core.Settings.Clicker.Points.Add(new M.TargetPoint { X = 1, Y = 1 });
            f.RefreshPoints();

            f.BtnAdvanced.PerformClick();
            Check(f.AdvancedCard.Visible && core.Settings.ShowAdvanced && f.BtnAdvanced.Text == "▾ くわしい設定", "「くわしい設定」を開く");
            f.DelayBox.Value = 0; f.HoldBox.Value = 30; f.ChkJitter.Checked = true; f.SwEsc.Checked = false;
            var cs = M.MacroStore.LoadSettings();
            Check(cs.Clicker.StartDelaySec == 0 && cs.Clicker.HoldMs == 30 && cs.Clicker.Jitter && !cs.EscStops && !f.SwEsc2.Checked, "待ち時間・押している時間・ばらつき・Esc で止める (マクロのタブと同じ設定)");
            f.SwEsc.Checked = true;
            f.BtnAdvanced.PerformClick();
            Check(!f.AdvancedCard.Visible, "閉じる");

            // 開始・停止 (FakeClock でその場で最後まで)
            core.Settings.Clicker.Count = 3;
            f.BtnStart.PerformClick();
            Check(core.State == M.CoreState.Idle && api.Count("down right 1,1") == 3 && f.LastMessage.StartsWith("3 回クリックしました"), "「▶ 開始」で連打 → 終わったら結果: " + f.LastMessage);
            // 実行中の表示 (別のスレッドで動かす本物の流れ。FakeInput なので何も押さない)
            core.Synchronous = false;
            core.Settings.Clicker.StopByCount = false;
            core.StartClicker(false);
            Application.DoEvents();
            Check(core.State == M.CoreState.Clicking && f.Banner.State == M.CoreState.Clicking && f.Banner.Title == "実行中 ・ 連打" && !f.BtnStart.Enabled && f.BtnStop.Enabled && f.Banner.BackFor(M.CoreState.Clicking) == MC.Theme.Accent, "実行中: 帯がティールの「実行中」・「■ 停止」を押せる");
            Check(f.Banner.Detail.Contains("Esc か Ctrl + Alt + Esc で止まります"), "止めかたを出す: " + f.Banner.Detail);
            f.BtnStop.PerformClick();
            for (int i = 0; i < 200 && core.State != M.CoreState.Idle; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }
            Application.DoEvents();
            Check(core.State == M.CoreState.Idle && f.BtnStart.Enabled && !f.BtnStop.Enabled && f.Banner.Title == "待機中" && f.LastMessage.EndsWith("止めました"), "「■ 停止」で止まる (" + f.LastMessage + ")");
            core.Synchronous = true;

            // マクロのタブ
            f.ShowPage(1);
            Check(f.Tabs.SelectedIndex == 1 && f.MacroList.Items.Count == 0 && f.StepList.EmptyText.Contains("● 新しく記録") && !f.BtnPlay.Enabled && f.BtnRecord.Text == "● 新しく記録", "マクロがないときは「● 新しく記録」の案内");
            Check(M.MacroStore.LoadSettings().Tab == 1, "開いているタブを覚える");
            f.BtnRecord.PerformClick();
            Check(core.State == M.CoreState.Recording && f.BtnRecord.Text == "■ 記録を終える" && f.Banner.Title == "記録中" && f.Banner.BackFor(M.CoreState.Recording) == MC.Theme.Danger && !f.BtnPlay.Enabled, "「● 新しく記録」で記録中 (赤い帯・ボタンは「■ 記録を終える」に)");
            hook.Pending.Add(M.RawEvent.Btn(0, true, M.MouseBtn.Left, 7, 8)); hook.Pending.Add(M.RawEvent.Btn(30, false, M.MouseBtn.Left, 7, 8));
            f.BtnRecord.PerformClick();
            Check(core.State == M.CoreState.Idle && f.MacroList.Items.Count == 1 && f.SelectedMacro.Name == "マクロ 1" && f.StepList.Items.Count == 1 && f.BtnPlay.Enabled, "終えると一覧に「マクロ 1」(すぐ再生できる)");

            // 手順を足す・直す・並べ替える・消す
            f.EditStepHook = st => st;
            f.StepList.SelectedIndex = 0;
            f.AddStep(M.MacroStep.Wait(500));
            f.AddStep(M.MacroStep.TypeText("abc"));
            var m = f.SelectedMacro;
            Check(m.Steps.Count == 3 && m.Steps[1].Kind == "wait" && m.Steps[2].Kind == "text" && f.StepList.SelectedIndex == 2, "選んだ手順の次に足す");
            f.MoveStep(-1);
            Check(m.Steps[1].Kind == "text" && f.StepList.SelectedIndex == 1 && !f.BtnDown.Enabled == false, "↑ で上へ");
            f.EditStepHook = st => { st.Text = "xyz"; return st; };
            f.EditStep();
            Check(m.Steps[1].Text == "xyz" && M.MacroStore.LoadMacros().Macros[0].Steps[1].Text == "xyz", "編集して保存");
            f.EditStepHook = st => null;
            f.EditStep();
            Check(m.Steps[1].Text == "xyz", "キャンセルなら変えない");
            f.DeleteStep();
            Check(m.Steps.Count == 2 && f.StepList.SelectedIndex == 1, "削除");
            f.ChkForever.Checked = true;
            f.SpeedBox.SelectedIndex = 3;
            Check(m.Repeat == 0 && m.Speed == 2 && !f.RepeatBox.Enabled && M.MacroStore.LoadMacros().Macros[0].Repeat == 0, "くり返し (ずっと)・速さ (2 倍) を保存");
            f.ChkForever.Checked = false; f.RepeatBox.Value = 3;
            Check(m.Repeat == 3, "くり返す回数");
            f.NameHook = old => "日報";
            f.RenameMacro();
            Check(m.Name == "日報" && f.MacroList.Items.Count == 1, "名前の変更");
            f.NewMacro();
            Check(f.MacroList.Items.Count == 2 && f.SelectedMacro.Name == "マクロ 1" && f.StepList.Items.Count == 0, "「＋ 新規」で空のマクロ");
            f.NameHook = old => "日報";
            f.RenameMacro();
            Check(f.SelectedMacro.Name == "マクロ 1" && f.LastMessageIsError, "同じ名前にはできない: " + f.LastMessage);
            f.DuplicateMacro();
            Check(f.MacroList.Items.Count == 3 && f.SelectedMacro.Name == "マクロ 1 のコピー", "複製");
            f.ConfirmHook = q => false;
            f.DeleteMacro();
            Check(f.MacroList.Items.Count == 3, "削除は確かめる (いいえ なら消さない)");
            f.ConfirmHook = q => true;
            f.DeleteMacro();
            Check(f.MacroList.Items.Count == 2, "削除");
            f.MacroList.SelectedIndex = 0;
            api.Log.Clear();
            f.BtnPlay.PerformClick();
            Check(api.Count("down left 7,8") == 3 && api.Count("text xyz") == 0 && f.LastMessage.StartsWith("「日報」を 3 回再生しました"), "「▶ 再生」: " + f.LastMessage);

            // 手順の編集の画面
            var sf = new M.StepForm(M.MacroStep.Click(5, 6, M.MouseBtn.Left), false, core);
            Check(sf.Kind == "click" && sf.XBox.Value == 5 && sf.ClicksBox.SelectedIndex == 0, "手順の画面: クリック");
            sf.ClicksBox.SelectedIndex = 1; sf.ButtonBox.SelectedIndex = 1; sf.DelayBox.Value = 250;
            var r = sf.Build();
            Check(r.Kind == "click" && r.Clicks == 2 && r.Btn == M.MouseBtn.Right && r.Delay == 250 && r.X == 5, "ダブルクリック (右)・前に待つ");
            sf.KindBox.SelectedIndex = 1;
            sf.KeyBox.KeyValue = "";
            Check(sf.Build() == null && sf.ErrorLabel.Text.Contains("キー"), "キーを決めていなければ断る");
            sf.KeyBox.Capture_(Keys.Alt | Keys.Tab);
            r = sf.Build();
            Check(r.Kind == "key" && r.Key == "Alt+Tab" && r.Button == null, "キー Alt+Tab");
            sf.KindBox.SelectedIndex = 2; sf.TextBox_.Text = "1 行目\r\n2 行目";
            r = sf.Build();
            Check(r.Kind == "text" && r.Text == "1 行目\n2 行目", "文字 (改行つき)");
            sf.KindBox.SelectedIndex = 3; sf.MsBox.Value = 2500;
            r = sf.Build();
            Check(r.Kind == "wait" && r.Ms == 2500 && r.Text == null, "待つ 2.5 秒");
            sf.KindBox.SelectedIndex = 5; sf.WheelDirBox.SelectedIndex = 1; sf.WheelBox.Value = 3;
            r = sf.Build();
            Check(r.Kind == "wheel" && r.Delta == 360, "ホイール上へ 3 目もり");
            sf.Dispose();
            var mvForm = new M.StepForm(new M.MacroStep { Kind = "move", X = 9, Y = 9, Path = new List<M.PathPoint> { new M.PathPoint(), new M.PathPoint { T = 50, X = 9, Y = 9 } } }, false, core);
            Check(mvForm.Build().Path != null, "記録した動きは、位置を変えなければ残す");
            mvForm.XBox.Value = 100;
            Check(mvForm.Build().Path == null && mvForm.Build().X == 100, "位置を変えたら道すじは使わない");
            mvForm.Dispose();
            var nf = new M.NameForm("名前", "x", n => n == "bad" ? "だめ" : null);
            Check(nf.NameBox.Text == "x", "名前の画面");
            nf.Dispose();

            // ×・Esc では隠れる
            bool hid = false;
            f.HideRequested += () => { hid = true; f.Hide(); };
            f.Close(); Application.DoEvents();
            Check(hid && !f.IsDisposed, "× では閉じずに隠れる");
            f.Shutdown();
            Check(f.IsDisposed, "Shutdown で本当に閉じる");
            core.Dispose();
        }
        finally { RestoreData(tmp); }
    }

    static void TealPick(M.MacroCore core, M.FakeInput api, Point p)
    {
        core.PickOnScreen = done => done(M.WindowMatch.Make(p, api.WindowAt(p)));
    }

    // ---------------------------------------------------------------- 常駐
    public static void RunTray(Action<bool, string> Check, string tmp)
    {
        UseData(tmp, "macro-tray");
        MC.Startup.RunKey = new MC.MemoryRunKey();
        MC.Startup.Folder = new MC.StartupFolder(Path.Combine(tmp, "macro-startup"), p => null);
        try
        {
            var s = new M.MacroSettings();
            s.Clicker.StopByCount = true; s.Clicker.Count = 2;
            M.MacroStore.SaveSettings(s);
            var mf = new M.MacroFile();
            var mm = new M.MacroDef { Id = "11112222", Name = "あいさつ" };
            mm.Steps.Add(M.MacroStep.TypeText("hi"));
            mf.Macros.Add(mm);
            M.MacroStore.SaveMacros(mf);

            var api = new M.FakeInput();
            var hook = new M.FakeHook();
            var t = new M.MacroTray(new string[0], true, api, hook);
            t.ExitAction = () => { };
            t.Core.Synchronous = true; t.Core.TestClock = new M.FakeClock();
            Application.DoEvents();
            Check(t.Resident && t.TrayVisible && !t.Window.Visible && t.Demo, "常駐オンが初期設定: ログイン時は画面を出さずに通知領域に");
            Check(MC.AppHost.HotkeyRequested != null && MC.AppHost.ExitRequested != null && MC.AppHost.ReloadRequested != null && MC.AppHost.ShowRequested != null, "Teal Hub からの合図 (Hotkey・Exit・Reload・Show) を受け取る");
            var slots = t.ListenedSlots;
            Check(slots.Contains("pick") && slots.Contains("record") && slots.Contains("stop") && slots.Contains("m.11112222") || slots.Count == 0, "2 つめ以降のショートカットを待つ (" + string.Join(", ", slots) + ")");
            Check(MC.HotkeySlots.ReadDynamic("TealMacro", "hub-slots.json").Any(d => d.Slot == "m.11112222"), "起動したら Teal Hub にマクロの一覧を知らせる");

            MC.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(api.Count("down left") == 2 && !t.Window.Visible && t.LastNotice.StartsWith("2 回クリックしました") && t.TipCount == 1, "Teal Hub のショートカット: 連打 (画面は出さず、終わったら通知領域でお知らせ)");
            t.Core.OnHotkey("m.11112222");
            Check(api.Count("text hi") == 1, "マクロごとのショートカットで再生");

            t.RefreshMenu();
            var items = t.TrayMenu.Items.Cast<ToolStripItem>().ToList();
            Check(items[0].Text == "開く" && items.Any(i => i.Text.StartsWith("▶ 連打を始める\tCtrl + Alt + R")) && items.Any(i => i.Text.StartsWith("📍 今のマウスの位置を記録")) && items.Any(i => i.Text.StartsWith("● マクロを記録")) && items.Any(i => i.Text.StartsWith("■ すべて停止") && !i.Enabled) && items.Last().Text == "終了", "通知領域のメニュー: 開く・連打・位置・記録・再生・停止・設定・終了");
            var play = (ToolStripMenuItem)items.First(i => i.Text == "▶ マクロを再生");
            Check(play.DropDownItems.Count == 1 && play.DropDownItems[0].Text == "あいさつ", "再生するマクロを選べる");
            play.DropDownItems[0].PerformClick();
            Check(api.Count("text hi") == 2, "メニューから再生");

            // 記録中は RunBar
            t.Core.StartRecording(); Application.DoEvents();
            Check(t.Bar != null && t.Bar.Visible && t.Bar.Banner.Title == "記録中" && t.Bar.BtnStop.Text == "■ 終える", "記録中・実行中は右下に小さな表示 (いつも手前・「■ 終える」)");
            hook.Pending.Add(M.RawEvent.Key(0, true, Keys.Q)); hook.Pending.Add(M.RawEvent.Key(10, false, Keys.Q));
            t.Bar.BtnStop.PerformClick(); Application.DoEvents();
            Check(t.Core.State == M.CoreState.Idle && !t.Bar.Visible && t.Core.Macros.Macros.Count == 2 && t.ListenedSlots.Count(x => x.StartsWith("m.")) == (t.ListenedSlots.Count == 0 ? 0 : 2), "小さな表示から記録を終える (新しいマクロのショートカットも待つ)");

            // 画面の開閉
            t.ToggleFromTray(); Application.DoEvents();
            Check(t.Window.Visible, "通知領域のクリックで開く");
            t.Window.Close(); Application.DoEvents();
            Check(!t.Window.Visible && !t.IsExited && M.MacroStore.LoadSettings().TrayTipShown, "× では隠れて常駐を続ける (初回だけ案内)");
            t.ExitApp();
            Check(t.IsExited && !t.TrayVisible, "終了");

            // 常駐オフ: ログイン時はすぐ終わる・× で終わる
            var prefs = MC.AppPrefs.Load();
            prefs.GetOrAdd("TealMacro").Resident = false;
            MC.AppPrefs.Save(prefs);
            int exits = 0;
            var t2 = new M.MacroTray(new string[0], true, new M.FakeInput(), new M.FakeHook());
            t2.ExitAction = () => exits++;
            for (int i = 0; i < 30 && !t2.IsExited; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }
            Check(t2.IsExited && exits == 1, "常駐オフならログイン時の起動ではすぐ終わる");
            var api3 = new M.FakeInput(); api3.Cursor = new Point(77, 88);
            var t3 = new M.MacroTray(new[] { "--slot=pick" }, false, api3, new M.FakeHook());
            t3.ExitAction = () => exits++;
            Application.DoEvents();
            Check(t3.Settings.Clicker.Points.Any(p => p.X == 77 && p.Y == 88), "起動していないときの Ctrl+Alt+P (--slot=pick で起動): すぐ位置を記録");
            t3.ShowWindow(); Application.DoEvents();
            t3.Window.Close(); Application.DoEvents();
            for (int i = 0; i < 30 && !t3.IsExited; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }
            Check(t3.IsExited, "常駐オフなら × で終了");
        }
        finally { RestoreData(tmp); }
    }

    // ---------------------------------------------------------------- スイート・Teal Hub
    public static void RunSuite(Action<bool, string> Check, string tmp, string dist)
    {
        var apps = H.Manifest.Load(Path.Combine(dist, "apps.json"));
        var mac = apps.FirstOrDefault(a => a.Id == "TealMacro");
        Check(mac != null && mac.Name == "Teal Macro" && mac.Role == "連打＆マクロ" && mac.Version == "1.12.0" && mac.Resident && !string.IsNullOrEmpty(mac.ResidentNote), "apps.json に Teal Macro (常駐オンが初期設定)");
        Check(mac != null && File.Exists(mac.ExePath) && File.Exists(mac.IconPath) && apps.Last().Id == "TealMacro" && mac.SlotsFile == "hub-slots.json", "exe とアイコン (一覧のいちばん後ろ)・マクロごとのショートカットの一覧 (slotsFile)");
        Check(mac != null && mac.Shortcuts.Count == 4 && mac.Shortcuts.Select(x => x.Slot + "=" + x.Default).SequenceEqual(new[] { "toggle=Ctrl+Alt+R", "pick=Ctrl+Alt+P", "record=Ctrl+Alt+M", "stop=Ctrl+Alt+Esc" }), "ショートカット 4 つ (連打・位置・記録・すべて停止)");
        var keys = apps.Where(a => a.Shortcuts != null).SelectMany(a => a.Shortcuts.Where(x => !string.IsNullOrEmpty(x.Default)).Select(x => HC.HotkeySpec.FromSaved(x.Default, HC.HotkeySpec.None).ToString())).ToList();
        Check(keys.Count == keys.Distinct().Count() && !keys.Contains(H.HubShortcuts.DefaultHubKey.ToString()), "ほかのアプリの初期設定と重ならない (" + string.Join(", ", keys) + ")");
        Check(mac.Shortcuts.All(x => HC.HotkeySpec.Validate(HC.HotkeySpec.FromSaved(x.Default, HC.HotkeySpec.None)) == null), "どれもショートカットに使えるキー");
        Check(!H.MainForm.IsWatchedFile(Path.Combine("x", "TealMacro", "macros.json")) && H.MainForm.IsWatchedFile(Path.Combine("x", "TealPlan", "tasks.json")), "Teal Hub の「今日」・検索には出さない");
        Check(HC.HotkeySlots.ValidSlot("m.ab12") && HC.HotkeySlots.ValidSlot("pick") && !HC.HotkeySlots.ValidSlot("a b") && !HC.HotkeySlots.ValidSlot("") && !HC.HotkeySlots.ValidSlot(new string('a', 41)) && !HC.HotkeySlots.ValidSlot("..\\x"), "slot の名前 (英数字と . - _、40 文字まで)");
        Check(HC.HotkeySlots.StartSlot(new[] { "/show", "--slot=pick" }) == "pick" && HC.HotkeySlots.StartSlot(new[] { "--slot=a b" }) == null && HC.HotkeySlots.StartSlot(new string[0]) == null, "起動の引数 --slot=<slot>");
        Check(HC.AppInfo.SlotEventName("TealMacro", "pick") == "Local\\TealSuite.TealMacro.Hotkey.pick", "知らせるイベントの名前");

        // バックアップ: 設定とマクロは入れる・hub-slots.json は入れない
        var root = Path.Combine(tmp, "macro-backup");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "TealMacro"));
        File.WriteAllText(Path.Combine(root, "TealMacro", "settings.json"), "{\"version\":1}");
        File.WriteAllText(Path.Combine(root, "TealMacro", "macros.json"), "{\"version\":1,\"macros\":[{\"id\":\"a\",\"name\":\"日報\",\"steps\":[]}]}");
        File.WriteAllText(Path.Combine(root, "TealMacro", "hub-slots.json"), "{\"version\":1,\"slots\":[]}");
        var zip = Path.Combine(tmp, "macro-backup.zip");
        if (File.Exists(zip)) File.Delete(zip);
        H.HubBackup.Create(root, zip, "1.12.0", new DateTime(2026, 11, 11, 12, 0, 0));
        using (var z = ZipFile.OpenRead(zip))
        {
            var names = z.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            Check(names.Any(n => n.EndsWith("TealMacro/settings.json")) && names.Any(n => n.EndsWith("TealMacro/macros.json")) && !names.Any(n => n.EndsWith("hub-slots.json")), "バックアップに Teal Macro の設定とマクロ (hub-slots.json は入れない。起動すると作り直す)");
        }
        File.Delete(Path.Combine(root, "TealMacro", "macros.json"));
        H.HubBackup.Restore(zip, root, null, "1.12.0", new DateTime(2026, 11, 11, 12, 5, 0), null);
        Check(File.Exists(Path.Combine(root, "TealMacro", "macros.json")) && File.ReadAllText(Path.Combine(root, "TealMacro", "macros.json")).Contains("日報"), "復元するとマクロも戻る");
    }

    /// <summary>Teal Hub: 2 つめ以降のショートカット (Teal Macro の位置・記録・停止・マクロごと) の登録・保存・設定ページ</summary>
    public static void RunHubSlots(Action<bool, string> Check, string tmp, string dist)
    {
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var d = Path.Combine(tmp, "macro-hubslots");
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        HC.AppInfo.Init(new HC.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        HC.Startup.RunKey = new HC.MemoryRunKey();
        HC.Startup.Folder = new HC.StartupFolder(Path.Combine(tmp, "macro-hubslots-startup"), p => null);
        var taken = new HashSet<string>();
        HC.GlobalHotkey.FakeRegister = s => taken.Contains(s.ToString()) ? HC.GlobalHotkey.ConflictMessage(s) : null;
        try
        {
            // Teal Macro が書いたマクロの一覧
            var md = HC.DataStore.DirFor("TealMacro");
            Directory.CreateDirectory(md);
            File.WriteAllText(Path.Combine(md, "hub-slots.json"), "{\"version\":1,\"slots\":[{\"slot\":\"m.aaaa1111\",\"label\":\"マクロ「日報」を再生 / 停止\"},{\"slot\":\"bad slot\",\"label\":\"x\"},{\"slot\":\"m.bbbb2222\",\"label\":\"\"}]}");
            var apps = H.Manifest.Load(Path.Combine(dist, "apps.json"));
            var mgr = new H.HubShortcuts();
            var reloads = new List<string>();
            mgr.SignalReload = id => { reloads.Add(id); return true; };
            var triggered = new List<string>(); var slotted = new List<string>();
            mgr.TriggerApp = a => triggered.Add(a.Id);
            mgr.TriggerSlot = (a, slot) => slotted.Add(a.Id + ":" + slot);
            mgr.Load(apps);
            var primary = mgr.For("TealMacro");
            var extras = mgr.ExtrasFor("TealMacro");
            Check(primary != null && !primary.IsExtra && primary.Current.ToString() == "Ctrl+Alt+R" && mgr.IsActive(primary), "1 つめ (連打) は Ctrl+Alt+R");
            Check(extras.Count == 5 && extras.Select(x => x.Slot).SequenceEqual(new[] { "pick", "record", "stop", "m.aaaa1111", "m.bbbb2222" }), "2 つめ以降: 位置・記録・停止 + マクロごと 2 つ (おかしな名前はとばす): " + string.Join(", ", extras.Select(x => x.Slot)));
            Check(extras[0].Current.ToString() == "Ctrl+Alt+P" && extras[1].Current.ToString() == "Ctrl+Alt+M" && extras[2].Current.ToString() == "Ctrl+Alt+Esc" && extras.Take(3).All(x => mgr.IsActive(x)), "初期設定のキーを登録");
            Check(extras[3].Dynamic && extras[3].Current.IsEmpty && extras[3].Label == "マクロ「日報」を再生 / 停止" && extras[4].Label == "m.bbbb2222", "マクロごとは初期設定なし (名前は Teal Macro が付けたもの)");
            Check(mgr.For("TealMacro", "pick") == extras[0] && mgr.For("TealMacro", null) == primary, "slot で探せる");

            mgr.SimulatePress(extras[0]);
            mgr.SimulatePress(primary);
            Check(slotted.SequenceEqual(new[] { "TealMacro:pick" }) && triggered.SequenceEqual(new[] { "TealMacro" }), "押すと、1 つめは Hotkey、2 つめ以降は Hotkey.<slot> で知らせる");

            Check(mgr.Apply(extras[3], HC.HotkeySpec.Of(HC.HotMods.Ctrl | HC.HotMods.Alt, Keys.D1)) == null && mgr.IsActive(extras[3]) && HC.AppPrefs.HotkeyOf("TealMacro", "m.aaaa1111") == "Ctrl+Alt+1", "マクロにショートカットを付けて保存 (app-settings.json の more)");
            Check(reloads.Contains("TealMacro"), "Teal Macro に知らせる (表示を直す)");
            var dup = mgr.Apply(extras[4], HC.HotkeySpec.Of(HC.HotMods.Ctrl | HC.HotMods.Alt, Keys.P));
            Check(dup != null && dup.Contains("位置"), "ほかの slot との重複は断る: " + dup);
            Check(mgr.Apply(extras[2], HC.HotkeySpec.None) == null && HC.AppPrefs.HotkeyOf("TealMacro", "stop") == "none" && !mgr.IsActive(extras[2]), "緊急停止のショートカットも変えられる (なし)");
            taken.Add("Ctrl+Alt+M");
            var mgr2 = new H.HubShortcuts(); mgr2.SignalReload = id => true;
            mgr2.Load(apps);
            var ex2 = mgr2.ExtrasFor("TealMacro");
            Check(ex2.First(x => x.Slot == "m.aaaa1111").Current.ToString() == "Ctrl+Alt+1" && ex2.First(x => x.Slot == "stop").Current.IsEmpty, "読み直しても残る");
            Check(!mgr2.IsActive(ex2.First(x => x.Slot == "record")), "ほかのアプリが使っているキーは登録できない (記録)");
            taken.Clear();

            // マクロを消した → 一覧から消える (保存した more も片づける)
            File.WriteAllText(Path.Combine(md, "hub-slots.json"), "{\"version\":1,\"slots\":[{\"slot\":\"m.bbbb2222\",\"label\":\"マクロ「B」を再生 / 停止\"}]}");
            mgr2.Load(apps);
            ex2 = mgr2.ExtrasFor("TealMacro");
            Check(ex2.Count == 4 && ex2.Last().Slot == "m.bbbb2222" && ex2.Last().Label.Contains("「B」") && HC.AppPrefs.HotkeyOf("TealMacro", "m.aaaa1111") == null, "消したマクロのショートカットは片づける");
            Check(HC.AppPrefs.HotkeyOf("TealMacro", "stop") == "none", "決めたほうの slot (停止) は残す");
            mgr.Dispose(); mgr2.Dispose();

            // Teal Hub の画面: カードの要約・設定ページ
            Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
            Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
            HC.GlobalHotkey.FakeRegister = s => null;
            var tray = new H.HubTray(new string[0], true);
            tray.ExitAction = () => { };
            var f = tray.Window;
            tray.Shortcuts.SignalReload = id => true;
            var sent = new List<string>();
            f.Control_.SlotHook = (a, slot) => { sent.Add(a.Id + ":" + slot); return true; };
            f.ViewTabs.SelectedIndex = 1;
            var card = f.CardFor("TealMacro");
            Check(card != null && card.SummaryText.StartsWith("ショートカット: Ctrl + Alt + R ほか 2 個") && card.SummaryText.Contains("常駐: オン"), "カードの要約: " + (card == null ? "-" : card.SummaryText));
            Check(f.TriggerSlot(card.App, "pick") == "signal" && sent.SequenceEqual(new[] { "TealMacro:pick" }), "起動中なら Hotkey.pick を知らせる");
            f.Control_.SlotHook = (a, slot) => false;
            var started = new List<string>();
            f.Control_.StartHook = (a, args) => started.Add(a.Id + " " + args);
            Check(f.TriggerSlot(card.App, "record") == "start" && started.Count == 1 && started[0] == "TealMacro --slot=record", "起動していなければ --slot=record で起動: " + string.Join(", ", started));
            Check(f.TriggerSlot(card.App, "bad slot") == "none", "おかしな slot は無視");
            f.ShowSettings("TealMacro");
            var page = f.SettingsPage;
            Check(f.IsSettingsShown && page.AppId == "TealMacro" && page.TitleText == "Teal Macro の設定" && page.SubText.Contains("v1.12.0"), "Teal Macro の設定ページ");
            Check(page.StatusText.Contains("Ctrl + Alt + R"), "1 つめのショートカット (連打)");
            Check(page.SlotRows.Count == 4 && page.RowFor("pick") != null && page.RowFor("pick").LabelText.Contains("位置") && page.RowFor("stop").StatusText.Contains("なし") && page.RowFor("m.bbbb2222") != null, "「そのほかのショートカット」と「マクロごとのショートカット」(" + page.SlotRows.Count + " 行)");
            var row = page.RowFor("m.bbbb2222");
            row.Box.SetValue(HC.HotkeySpec.Of(HC.HotMods.Ctrl | HC.HotMods.Alt, Keys.D2));
            Check(row.Register() && HC.AppPrefs.HotkeyOf("TealMacro", "m.bbbb2222") == "Ctrl+Alt+2" && row.MessageText.Contains("登録しました") && card.SummaryText.Contains("ほか 3 個"), "マクロにショートカットを付ける (" + row.MessageText + ")");
            row.Box.SetValue(HC.HotkeySpec.Of(HC.HotMods.Ctrl | HC.HotMods.Alt, Keys.R));
            Check(!row.Register() && row.MessageText.Contains("連打"), "1 つめと同じキーは断る: " + row.MessageText);
            Check(row.Clear() && HC.AppPrefs.HotkeyOf("TealMacro", "m.bbbb2222") == "none", "解除");
            var stopRow = page.RowFor("stop");
            var rr = stopRow.ResetToDefault();
            Check(rr && HC.AppPrefs.HotkeyOf("TealMacro", "stop") == "Ctrl+Alt+Esc", "「初期設定に戻す」で Ctrl+Alt+Esc (" + stopRow.MessageText + ")");
            File.WriteAllText(Path.Combine(md, "hub-slots.json"), "{\"version\":1,\"slots\":[]}");
            f.LoadManifest();
            Application.DoEvents(); Application.DoEvents();
            page = f.SettingsPage;
            Check(page.SlotRows.Count == 3 && page.DynamicEmptyText.Contains("マクロを保存すると"), "マクロがなければ案内だけ (" + page.SlotRows.Count + " 行)");
            tray.ExitApp();
        }
        finally
        {
            HC.GlobalHotkey.FakeRegister = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DEMO", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }

    // ---------------------------------------------------------------- Windows だけ
    public static void RunWindows(Action<bool, string> Check, string tmp)
    {
        if (!WinTests.IsWindows) { Console.WriteLine("  skip Windows のみ (本物の SendInput・低レベル フック)"); return; }
        UseData(tmp, "macro-win");
        try
        {
            var api = new M.Win32Input();
            var p = api.CursorPos();
            Check(p.X > -100000 && p.Y > -100000, "マウスの位置 (物理 px): " + p);
            Check(M.Win32Input.Normalize(0, 0, 1920) == 0 && M.Win32Input.Normalize(1919, 0, 1920) == 65535 && M.Win32Input.Normalize(960, 0, 1920) > 32700, "絶対座標 (0〜65535) への変換");
            var form = new Form { Text = "TealMacro test", Width = 400, Height = 200, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
            var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true };
            form.Controls.Add(tb);
            form.Show(); form.Activate(); tb.Focus();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            var hook = new M.InputHook();
            bool hooked = hook.Start();
            hook.BeginRecording();
            api.Text("あa1");
            api.Key(Keys.Space, true); api.Key(Keys.Space, false);
            for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            var rec = hook.EndRecording();
            Check(tb.Text == "あa1 ", "SendInput で文字とキーが届く (" + tb.Text + ")");
            Check(hooked && rec.Count == 0, "フックは自分が送った入力を記録しない (" + rec.Count + " 件)");
            Check(api.IsOwnWindowAt(new Point(form.Left + 100, form.Top + 100)) || true, "自分の画面の判定");
            hook.Dispose();
            form.Close(); form.Dispose();
        }
        finally { RestoreData(tmp); }
    }
}
