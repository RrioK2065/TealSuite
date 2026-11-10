// 開発用: 1.12.0 の Teal Macro の画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/V1120Shots.exe clicker|points|macro|step|recording
//   本物の入力の代わりに FakeInput (何もしない) を使うので、デスクトップには何もしない。データは一時フォルダー
//   ビルド: build/ に dist/TealSuite の中身と apps/TealMacro/TealMacro.exe をコピーしてから
//     I=assets/icons/TealMacro; mcs -out:build/V1120Shots.exe -codepage:utf8 -langversion:5 -win32icon:$I.ico -resource:$I.ico,AppIcon.ico -resource:$I.png,AppIcon.png -r:MACRO=build/apps/TealMacro/TealMacro.exe \
//         -r:System.Windows.Forms.dll -r:System.Drawing.dll tests/v1120_shots.cs
extern alias MACRO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using M = MACRO::TealSuite.Macro;

[assembly: System.Reflection.AssemblyVersion("1.12.0.0")]

static class V1120Shots
{
    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "clicker";
        var dir = Path.Combine(Path.GetTempPath(), "tealmacro-shots-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", dir);
        MACRO::TealSuite.Common.AppInfo.Init("TealMacro", "Teal Macro", "連打＆マクロ");
        var api = new M.FakeInput();
        api.Cursor = new Point(640, 420);
        api.AddWindow("game.exe", "UnityWndClass", "クリッカーゲーム", new Rectangle(100, 80, 1280, 720));
        api.AddWindow("excel.exe", "XLMAIN", "売上.xlsx - Excel", new Rectangle(0, 0, 1920, 1032));

        var s = new M.MacroSettings();
        var macros = new M.MacroFile();
        if (mode == "points")
        {
            s.Clicker.Target = M.ClickerSettings.TargetPoints;
            s.Clicker.WindowRelative = true;
            s.Clicker.PerSecond = 20;
            s.Clicker.StopByCount = true; s.Clicker.Count = 500;
            s.ShowAdvanced = true;
            foreach (var p in new[] { new Point(420, 380), new Point(860, 380), new Point(640, 610) })
                s.Clicker.Points.Add(M.WindowMatch.Make(p, api.WindowAt(p)));
        }
        if (mode == "macro" || mode == "step" || mode == "recording")
        {
            s.Tab = 1;
            var m1 = new M.MacroDef { Id = "a1b2c3d4", Name = "日報を送る", Repeat = 1, Speed = 1 };
            m1.Steps.Add(M.MacroStep.Click(220, 140, M.MouseBtn.Left));
            var wait = M.MacroStep.Wait(1500); m1.Steps.Add(wait);
            var t = M.MacroStep.TypeText("本日の作業は完了しました。"); t.Delay = 300; m1.Steps.Add(t);
            var k = M.MacroStep.KeyPress("Ctrl+Enter"); k.Delay = 450; m1.Steps.Add(k);
            var mv = M.MacroStep.Move(980, 620); mv.Delay = 600; mv.Path = new List<M.PathPoint> { new M.PathPoint { T = 0, X = 220, Y = 140 }, new M.PathPoint { T = 420, X = 980, Y = 620 } }; m1.Steps.Add(mv);
            var c2 = M.MacroStep.Click(980, 620, M.MouseBtn.Left, 2); c2.Delay = 200; m1.Steps.Add(c2);
            var w2 = new M.MacroStep { Kind = M.MacroStep.KWheel, X = 980, Y = 620, Delta = -360, Delay = 500 }; m1.Steps.Add(w2);
            var k2 = M.MacroStep.KeyPress("Esc"); k2.Delay = 800; m1.Steps.Add(k2);
            macros.Macros.Add(m1);
            var m2 = new M.MacroDef { Id = "e5f6a7b8", Name = "ゲームの周回", Repeat = 0, Speed = 2 };
            for (int i = 0; i < 24; i++) m2.Steps.Add(M.MacroStep.Click(300 + i * 10, 400, M.MouseBtn.Left));
            macros.Macros.Add(m2);
            var m3 = new M.MacroDef { Id = "c9d0e1f2", Name = "表をコピー", Repeat = 5 };
            m3.Steps.Add(M.MacroStep.KeyPress("Ctrl+C")); m3.Steps.Add(M.MacroStep.KeyPress("Down"));
            macros.Macros.Add(m3);
            s.SelectedMacro = m1.Id;
            // マクロごとのショートカットの見本 (Teal Hub で決めたことにする)
            var prefs = MACRO::TealSuite.Common.AppPrefs.Load();
            var ap = prefs.GetOrAdd("TealMacro");
            ap.SetSlotHotkey(m1.Slot, "Ctrl+Alt+1");
            MACRO::TealSuite.Common.AppPrefs.Save(prefs);
        }
        var hook = new M.FakeHook();
        var core = new M.MacroCore(s, macros, api, hook);
        var f = new M.MainForm(core);
        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(0, 0);
        f.Shown += (o, e) =>
        {
            f.Size = new Size(860, mode == "points" ? 880 : 740);
            if (mode == "points")
            {
                f.PointList.SelectedIndex = 1;
                core.StartClicker(false);   // 実行中の表示 (FakeInput なので何も押さない)
            }
            if (mode == "macro") f.StepList.SelectedIndex = 2;
            if (mode == "step")
            {
                f.StepList.SelectedIndex = 0;
                var st = new M.StepForm(core.Macros.Macros[0].Steps[0].Clone(), false, core);
                st.StartPosition = FormStartPosition.Manual;
                st.Location = new Point(240, 170);
                st.Show(f);
            }
            if (mode == "recording")
            {
                hook.Pending.Add(M.RawEvent.Mv(0, 10, 10));
                hook.Pending.Add(M.RawEvent.Mv(5, 20, 20));
                core.StartRecording();
                for (int i = 0; i < 40; i++) hook.Pending.Add(M.RawEvent.Mv(10 + i, 30 + i, 30));
                var bar = new M.RunBar();
                bar.StartPosition = FormStartPosition.Manual;
                bar.Location = new Point(380, 760);
                bar.SetState(core.State, core.StateTitle, core.StateDetail);
                bar.Show();
            }
        };
        Application.Run(f);
    }
}
