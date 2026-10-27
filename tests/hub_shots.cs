// 開発用: Teal Hub の画面をスクリーンショット用に開く (Xvfb 上で tests/shot.sh から使う。配布物には入れない)
//   mono build/HubShots.exe main|list|edit
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
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]

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
        C.HotkeySpec k;
        C.ShortcutStore.Set("TealHub", "Teal Hub", C.ShortcutStore.OpenSlotFor("TealPass"), "Teal Pass を開く", C.HotkeySpec.TryParse("Alt+P", out k) ? k : C.HotkeySpec.None);
        var tray = new H.HubTray(new string[0], mode != "main");
        tray.Shortcuts.SignalReload = id => true;
        tray.ExitAction = Application.Exit;
        if (mode == "list")
        {
            var f = new H.ShortcutListForm(tray.Window, tray.Shortcuts) { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0) };
            f.Show();
        }
        else if (mode == "edit")
        {
            var slot = tray.Shortcuts.Find("TealPass", C.ShortcutStore.OpenSlotFor("TealPass"));
            var f = tray.Window.EditShortcut(slot);
            f.StartPosition = FormStartPosition.Manual; f.Location = new System.Drawing.Point(0, 0);
        }
        Application.Run(tray);
    }
}
