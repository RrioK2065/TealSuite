// Teal Volume (音量操作) のロジックテスト
extern alias VM;
using System;
using System.Linq;
using V = VM::TealSuite.Volume;

static class VMTests
{
    public static void Run(Action<bool, string> Check)
    {
        var s = new VM::TealSuite.Common.FlatSlider { Width = 220, Minimum = 0, Maximum = 100, ShiftStep = 5 };
        // トラックは左右 10px の余白 → 10..210 が 0..100
        Check(s.ValueFromX(10, false) == 0 && s.ValueFromX(210, false) == 100, "端は 0% と 100%");
        Check(s.ValueFromX(10 + 2 * 37, false) == 37, "普通のドラッグは 1% 単位 (37%)");
        Check(s.ValueFromX(10 + 2 * 37, true) == 35, "Shift+ドラッグは 5% 単位 (37 → 35)");
        Check(s.ValueFromX(10 + 2 * 38, true) == 40, "Shift+ドラッグ (38 → 40)");
        bool allMultiples = Enumerable.Range(-20, 260).All(x => s.ValueFromX(x, true) % 5 == 0);
        Check(allMultiples, "Shift 中はどこでも 5 の倍数");
        Check(s.ValueFromX(-50, true) == 0 && s.ValueFromX(999, true) == 100, "範囲外は 0〜100 に収まる");

        Check(V.AudioNames.Pick(true, "@%SystemRoot%\\System32\\AudioSrv.Dll,-202", null, null) == "システム音", "システムのセッションは「システム音」");
        Check(V.AudioNames.Pick(false, "", "Google Chrome", "chrome") == "Google Chrome", "exe の説明を表示名に");
        Check(V.AudioNames.Pick(false, "@res,-1", "", "spotify") == "spotify", "説明がなければプロセス名");
        Check(V.AudioNames.Pick(false, "Teams 会議", "Microsoft Teams", "ms-teams") == "Teams 会議", "アプリが付けた名前を優先");
        Check(V.AudioNames.ToPercent(0.374f) == 37 && V.AudioNames.ToPercent(1.2f) == 100 && Math.Abs(V.AudioNames.FromPercent(45) - 0.45f) < 1e-6, "0.0〜1.0 と % の変換");

        var demo = new V.DemoBackend();
        var apps = demo.Refresh();
        Check(apps.Any(a => a.IsSystem && a.Name == "システム音"), "デモにシステム音の行");
        demo.SetVolume(apps[0], 0.25f); demo.SetMute(apps[0], true);
        Check(apps[0].Volume == 0.25f && apps[0].Muted, "音量とミュートを設定");
    }
}
