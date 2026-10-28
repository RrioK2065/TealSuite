// 開発用: 1.10.0 で増えた画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/V1100Shots.exe plan:month   … Teal Plan の月のカレンダー (Teal Wallet の支払い日つき)
//   mono build/V1100Shots.exe pass:settings … Teal Pass の設定 (「サイト名を検索に出す」)
//   ビルド: build/ に dist/TealSuite の中身をコピーしてから
//     mcs -out:build/V1100Shots.exe -codepage:utf8 -langversion:5 -r:PM=build/apps/TealPass/TealPass.exe \
//         -r:TASKS=build/apps/TealPlan/TealPlan.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll tests/v1100_shots.cs
extern alias PM;
extern alias TASKS;
using System;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.10.0.0")]

static class V1100Shots
{
    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "plan:month";
        if (mode == "pass:settings")
        {
            PM::TealSuite.Common.AppInfo.Init("TealPass", "Teal Pass", "生成・保管・メモ");
            // 設定のダイアログだけを開く (後ろの一覧やパスワードを画像に写さない)
            var f = new PM::TealSuite.Pass.PassSettingsForm(new PM::TealSuite.Pass.PmSettings { SiteIndex = true });
            f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(0, 0);
            Application.Run(f);
            return;
        }
        TASKS::TealSuite.Common.AppInfo.Init("TealPlan", "Teal Plan", "タスク＆予定");
        var p = new TASKS::TealSuite.Tasks.MainForm();
        p.StartPosition = FormStartPosition.Manual; p.Location = new System.Drawing.Point(0, 0);
        p.Shown += (s, e) => { p.Tabs.SelectedIndex = 1; p.Calendar.Mode = TASKS::TealSuite.Tasks.CalMode.Month; p.Calendar.Day = DateTime.Today; };
        Application.Run(p);
    }
}
