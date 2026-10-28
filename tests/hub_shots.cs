// 開発用: Teal Hub の画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/HubShots.exe main|main:tall|today|search:<文字>|settings:<AppId>   (例: settings:TealClip、search:#買い物。main:tall は全アプリが入る高さで開く)
//   main・main:tall は「アプリ」のタブ、today は「今日」(1.10.0)
//   アップデートのお知らせ: TEALSUITE_UPDATE_API=tests/fixtures/release-latest.json で main (起動 5 秒後に確認する)
//   ビルド: I=assets/icons/TealHub; mcs -out:build/HubShots.exe -codepage:utf8 -langversion:5 -win32icon:$I.ico -resource:$I.ico,AppIcon.ico -resource:$I.png,AppIcon.png -r:AM=dist/TealSuite/TealHub.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll tests/hub_shots.cs
//          (build/ に dist/TealSuite の TealHub.exe・apps.json・apps をコピーしておく)
extern alias AM;
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using C = AM::TealSuite.Common;
using H = AM::TealSuite.Hub;
[assembly: System.Reflection.AssemblyVersion("1.10.0.0")]

static class HubShots
{
    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "main";
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        C.GlobalHotkey.FakeRegister = s => null;
        C.Startup.RunKey = new C.MemoryRunKey();
        H.FileAssoc.Store = null;
        // 見本: Teal Pass に Alt+P、Teal Clip はスタートアップに登録
        var prefs = C.AppPrefs.Load();
        prefs.GetOrAdd("TealPass").Hotkey = "Alt+P";
        C.AppPrefs.Save(prefs);
        var clipExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps", "TealClip", "TealClip.exe");
        C.Startup.TrySet("TealClip", clipExe, true);
        if (mode == "today" || mode.StartsWith("search:"))
        {
            // 「今日」と検索の画像では、上のお知らせ (更新内容・スタートアップ) を出さない
            C.Startup.TrySet("TealHub", Application.ExecutablePath, true);
            var dir = C.DataStore.DirFor("TealHub");
            Directory.CreateDirectory(dir);
            var sf = Path.Combine(dir, H.HubTray.SettingsFile);
            if (!File.Exists(sf)) File.WriteAllText(sf, "{\"lastSeenVersion\":\"1.10.0\"}");
        }
        var tray = new H.HubTray(new string[0], false);
        tray.Shortcuts.SignalReload = id => true;
        tray.ExitAction = Application.Exit;
        tray.Window.StartPosition = FormStartPosition.Manual;
        tray.Window.Location = new System.Drawing.Point(0, 0);
        if (mode == "main:tall") tray.Window.Height = 990;
        if (mode.StartsWith("main")) tray.Window.ViewTabs.SelectedIndex = 1;
        if (mode == "today") { tray.Window.ViewTabs.SelectedIndex = 0; tray.Window.Height = 890; }
        if (mode.StartsWith("search:"))
        {
            var q = mode.Substring(7);
            var t = new Timer { Interval = 800 };
            t.Tick += (s, e) => { t.Stop(); tray.Window.SearchBox.Text = q; tray.Window.ApplyView(); tray.Window.HomeList_.Focus(); };
            t.Start();
        }
        if (mode.StartsWith("settings:")) tray.Window.ShowSettings(mode.Substring(9));
        Application.Run(tray);
    }
}
