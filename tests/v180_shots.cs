// 開発用: 1.8.0 で増えた画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/V180Shots.exe <mode> [押すボタンの文字]
//     hub [文字] / volume [文字] / tasks [文字] / tasks:bulk / sub [文字]
//     例: hub "その他 ▾"、volume "プリセット ▾"、sub "支払い予定"
//   ビルド: build/ に dist/TealSuite の中身をコピーしてから (ヘッダーのアイコンを出すには、撮るアプリの -resource:assets/icons/<Id>.png,AppIcon.png と .ico,AppIcon.ico も付ける)
//     mcs -out:build/V180Shots.exe -codepage:utf8 -langversion:5 -r:AM=build/TealHub.exe -r:VM=build/apps/TealVolume/TealVolume.exe \
//         -r:SUB=build/apps/TealSub/TealSub.exe -r:TASKS=build/apps/TealTasks/TealTasks.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll tests/v180_shots.cs
extern alias AM;
extern alias VM;
extern alias SUB;
extern alias TASKS;
using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.8.0.0")]

static class V180Shots
{
    static Control Find(Control root, string text)
    {
        if (root.Text == text || (root.GetType().Name == "FlatButton" && root.Text.StartsWith(text))) return root;
        foreach (Control c in root.Controls) { var r = Find(c, text); if (r != null) return r; }
        return null;
    }

    static void Click(Form f, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var c = Find(f, text);
        if (c == null) { Console.Error.WriteLine("ボタンがありません: " + text); return; }
        Console.Error.WriteLine("found " + c.GetType().Name + " " + c.Text);
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { EventArgs.Empty });
    }

    static void After(Form f, int ms, Action a)
    {
        // Shown が先に済んでいる場合 (常駐アプリが自分で開く) もあるので、起動からの時間で動かす
        var t = new Timer { Interval = ms + 1200 }; t.Tick += (s2, e2) => { t.Stop(); Console.Error.WriteLine("click " + f.Visible); a(); }; t.Start();
    }

    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "hub";
        var click = args.Length > 1 ? args[1] : null;
        if (mode == "hub")
        {
            AM::TealSuite.Common.AppInfo.Init(new AM::TealSuite.Common.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
            AM::TealSuite.Common.GlobalHotkey.FakeRegister = s => null;
            AM::TealSuite.Common.Startup.RunKey = new AM::TealSuite.Common.MemoryRunKey();
            AM::TealSuite.Hub.FileAssoc.Store = null;
            var tray = new AM::TealSuite.Hub.HubTray(new string[0], false);
            tray.Shortcuts.SignalReload = id => true;
            tray.ExitAction = Application.Exit;
            tray.Window.StartPosition = FormStartPosition.Manual;
            tray.Window.Location = new System.Drawing.Point(0, 0);
            After(tray.Window, 600, () => Click(tray.Window, click));
            Application.Run(tray);
        }
        else if (mode == "volume")
        {
            VM::TealSuite.Common.AppInfo.Init("TealVolume", "Teal Volume", "音量操作");
            var tray = new VM::TealSuite.Volume.VolumeTray(new[] { "/show" }, false);
            tray.Window.StartPosition = FormStartPosition.Manual;
            tray.Window.Location = new System.Drawing.Point(0, 0);
            After(tray.Window, 800, () => Click(tray.Window, click));
            if (!tray.Window.Visible) tray.Window.Show();
            Application.Run(tray);
        }
        else if (mode == "sub")
        {
            SUB::TealSuite.Common.AppInfo.Init("TealSub", "Teal Sub", "サブスク整理");
            var f = new SUB::TealSuite.Sub.MainForm();
            f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(0, 0);
            After(f, 600, () => Click(f, click));
            Application.Run(f);
        }
        else
        {
            TASKS::TealSuite.Common.AppInfo.Init("TealTasks", "Teal Tasks", "タスク管理＆カレンダー");
            var f = new TASKS::TealSuite.Tasks.MainForm();
            f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(0, 0);
            if (mode == "tasks:bulk")
                After(f, 600, () =>
                {
                    var b = new TASKS::TealSuite.Tasks.BulkAddForm(DateTime.Now, null);
                    b.StartPosition = FormStartPosition.Manual; b.Location = new System.Drawing.Point(140, 90);
                    b.Lines.Text = "・明日10時 会議の資料を印刷 重要\r\n・金曜までに経費の精算 #仕事\r\n- [ ] 牛乳と卵を買う\r\n1. 平日 7時 ゴミ出し\r\n2. 毎年 12/1 年賀状の準備";
                    b.Shown += (s3, e3) => { b.Lines.SelectionStart = b.Lines.TextLength; b.Lines.SelectionLength = 0; };
                    b.Show(f);
                });
            else After(f, 600, () => Click(f, click));
            Application.Run(f);
        }
    }
}
