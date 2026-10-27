// 全体ショートカット (Common/Hotkey.cs) と Teal Volume の常駐 (TrayContext.cs) のテスト
// Windows 以外では RegisterHotKey を呼べないので、GlobalHotkey.FakeRegister で登録の成否を差し替える。
extern alias VM;
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using C = VM::TealSuite.Common;
using V = VM::TealSuite.Volume;

static class HotkeyTests
{
    static C.HotkeySpec P(string s) { C.HotkeySpec k; return C.HotkeySpec.TryParse(s, out k) ? k : new C.HotkeySpec(); }
    static bool Ok(string s) { C.HotkeySpec k; return C.HotkeySpec.TryParse(s, out k); }

    /// <summary>組み合わせの決まり (Alt+文字を許可、よく使う操作は不可)</summary>
    public static void RunRules(Action<bool, string> Check)
    {
        Check(Ok("Alt+V") && P("Alt+V").Mods == C.HotMods.Alt && P("Alt+V").Key == Keys.V, "Alt+V は使える (修飾キー 1 つ + 文字)");
        Check(Ok("Alt+1") && Ok("Ctrl+Alt+V") && Ok("Alt+Shift+V") && Ok("Win+Alt+V") && Ok("Ctrl+B"), "Alt+数字・Ctrl+Alt+文字・Ctrl+B なども可");
        Check(!Ok("V") && !Ok("Shift+V") && !Ok("1") && !Ok("Esc"), "文字だけ・Shift+文字・Esc だけは不可");
        Check(Ok("F9") && Ok("Shift+F9"), "F キーは単独 / Shift と一緒でも可");
        Check(new[] { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Ctrl+S" }.All(x => !Ok(x)), "Ctrl+C/V/X/Z/Y/A/S は不可");
        Check(Ok("Ctrl+Shift+C") && Ok("Ctrl+Shift+V"), "Shift を足せば Ctrl+Shift+C なども可");
        Check(new[] { "Alt+F4", "Alt+Tab", "Alt+Space", "Alt+Esc", "Ctrl+Esc", "Ctrl+Shift+Esc", "Ctrl+Alt+Delete", "Win+L" }.All(x => !Ok(x)), "Windows の操作 (Alt+F4・Alt+Tab・Ctrl+Alt+Delete・Win+L など) は不可");
        var why = C.HotkeySpec.Validate(C.HotkeySpec.Of(C.HotMods.Ctrl, Keys.C));
        Check(why != null && why.Contains("Ctrl + C"), "不可の理由を表示 (" + why + ")");
        Check(C.HotkeySpec.Caution(P("Alt+V")) != null && C.HotkeySpec.Caution(P("Ctrl+Alt+V")) == null, "Alt+文字には注意書き (ほかのアプリのメニューより優先)");
        Check(P("alt+v").ToString() == "Alt+V" && P("Alt+V").Display == "Alt + V" && P("Ctrl+Alt+Esc").ToString() == "Ctrl+Alt+Esc", "表示と保存の形 (Alt+V / Alt + V)");
        var fk = C.HotkeySpec.FromKeyEvent(Keys.V | Keys.Alt, false);
        Check(fk.ToString() == "Alt+V" && C.HotkeySpec.Validate(fk) == null, "押したキーから組み立て (Alt+V)");
        Check(V.VolumeTray.DefaultHotkey.ToString() == "Alt+V" && C.HotkeySpec.Validate(V.VolumeTray.DefaultHotkey) == null, "Teal Volume の初期設定は Alt+V");

        // GlobalHotkey: 登録の失敗 (ほかのアプリが使用中) を報告する (1.6.0 では Teal Hub だけが使う)
        string taken = "Ctrl+Alt+K";
        C.GlobalHotkey.FakeRegister = s => s.ToString() == taken ? C.GlobalHotkey.ConflictMessage(s) : null;
        try
        {
            var g = new C.GlobalHotkey();
            var err = g.Register(P(taken));
            Check(err != null && err.Contains("ほかのアプリ") && !g.Active, "使用中なら理由を返す (" + err + ")");
            Check(g.Register(P("Alt+V")) == null && g.Active, "空いていれば登録できる");
            int n = 0; g.Pressed += () => n++; g.SimulatePress();
            Check(n == 1, "押されたら知らせる");
            g.Unregister(); Check(!g.Active, "外す");
            g.Dispose();
        }
        finally { C.GlobalHotkey.FakeRegister = null; }
    }

    static void WritePrefs(string json)
    {
        var p = C.AppPrefs.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p, json);
    }

    /// <summary>Teal Volume: 常駐オン (×で隠れる・ショートカットで開閉・--startup は隠れて起動) / 常駐オフ (×で終了)</summary>
    public static void RunVolumeTray(Action<bool, string> Check, string tmp)
    {
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var d = Path.Combine(tmp, "vm-data");
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", d);
        C.AppInfo.Init("TealVolume", "Teal Volume", "");
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
        var key = new C.MemoryRunKey();
        C.Startup.RunKey = key;
        C.Startup.Folder = new C.StartupFolder(Path.Combine(tmp, "vm-startup"), p => null);
        try
        {
            // ---- 常駐オフ (1.6.0 の初期設定)
            var t0 = new V.VolumeTray(new string[0], false);
            t0.ExitAction = () => { };
            Application.DoEvents();
            Check(!t0.Resident && !t0.TrayVisible && t0.Window.Visible, "初期設定は常駐オフ: 通知領域に出さず、画面を出す");
            Check(t0.Window.HotkeyText == "Alt + V で開閉", "ショートカット (Teal Hub が受け付ける Alt+V) を画面に表示");
            Check(C.AppHost.HotkeyRequested != null, "Teal Hub からの Hotkey イベントを受け取る");
            t0.Window.Close(); Application.DoEvents();
            Check(t0.IsExited, "常駐オフでは×で閉じると終了する");

            // ---- 常駐オフでログイン時に起動された (登録が残っていた) → 何も出さずに終わる
            var t1 = new V.VolumeTray(new string[0], true);
            bool exited1 = false; t1.ExitAction = () => exited1 = true;
            for (int i = 0; i < 20 && !exited1; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            Check(exited1 && !t1.Window.Visible, "常駐オフで --startup のときは画面を出さずに終了");

            // ---- 常駐オン (Teal Hub が app-settings.json に書く)
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealVolume\",\"hotkey\":\"Ctrl+Alt+M\",\"resident\":true}]}");
            var t = new V.VolumeTray(new string[0], true);
            t.ExitAction = () => { };
            Application.DoEvents();
            Check(t.Resident && t.TrayVisible && !t.Window.Visible, "常駐オン + --startup: 画面を出さずに通知領域に常駐");
            Check(t.Window.HotkeyText == "Ctrl + Alt + M で開閉" && t.Hotkey.ToString() == "Ctrl+Alt+M", "Teal Hub で変えたショートカットを表示");

            bool fg = false; t.ForegroundCheck = () => fg;
            C.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(t.Window.Visible, "ショートカット (Hotkey イベント) で開く");
            Check(t.Window.FooterText != null && t.Window.FooterText.StartsWith("Ctrl + Alt + M で開閉 ・ "), "下の帯にショートカット (" + t.Window.FooterText + ")");
            fg = false; C.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(t.Window.Visible, "後ろにあるときは前面に出す (隠さない)");
            fg = true; C.AppHost.HotkeyRequested(); Application.DoEvents();
            Check(!t.Window.Visible && t.TipCount == 1 && !t.IsExited, "前面のときは隠す (初回だけ案内)");

            t.ToggleFromTray(); Application.DoEvents();
            Check(t.Window.Visible, "通知領域のクリックで開く");
            t.Window.Close(); Application.DoEvents();
            Check(!t.Window.IsDisposed && !t.Window.Visible && !t.IsExited && t.TipCount == 1, "×では終了せず隠れる (案内は 1 回だけ)");
            t.ShowWindow(); Application.DoEvents();
            t.Window.WindowState = FormWindowState.Minimized; Application.DoEvents();
            t.ShowWindow(); Application.DoEvents();
            Check(t.Window.Visible && t.Window.WindowState == FormWindowState.Normal, "最小化中に開くと元に戻す");

            // 設定は Teal Hub の設定ページで (--settings TealVolume を付けて Teal Hub を起動)
            var launched = new System.Collections.Generic.List<string>();
            C.HubLink.LaunchHook = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
            t.ShowSettings();
            Check(launched.Count == 1 && launched[0] == "TealHub.exe --settings TealVolume", "「設定」は Teal Hub の設定ページを開く (" + (launched.Count > 0 ? launched[0] : "") + ")");
            C.HubLink.LaunchHook = null;

            // Teal Hub で常駐をオフにした (Reload) → 隠れていれば終了
            t.HideWindow();
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealVolume\",\"hotkey\":\"none\",\"resident\":false}]}");
            C.AppHost.ReloadRequested(); Application.DoEvents();
            Check(!t.Resident && t.Hotkey.IsEmpty && t.IsExited, "Teal Hub で常駐をオフにすると、隠れていた Teal Volume は終了");

            // 表示中に常駐オフ → 通知領域から消え、閉じたら終了
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealVolume\",\"resident\":true}]}");
            var t2 = new V.VolumeTray(new string[0], false);
            t2.ExitAction = () => { };
            Application.DoEvents();
            Check(t2.Window.Visible && t2.Resident && t2.Hotkey.ToString() == "Alt+V", "保存値がなければショートカットは初期設定の Alt+V");
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealVolume\",\"resident\":false}]}");
            C.AppHost.ReloadRequested(); Application.DoEvents();
            Check(!t2.IsExited && !t2.TrayVisible && t2.Window.Visible, "表示中に常駐をオフ → 通知領域から消える (画面はそのまま)");
            t2.HideWindow(); Application.DoEvents();
            Check(t2.IsExited && t2.TipCount == 0, "そのあと閉じると終了 (常駐の案内は出さない)");

            // 終了要求
            WritePrefs("{\"version\":1,\"apps\":[{\"id\":\"TealVolume\",\"resident\":true}]}");
            var t3 = new V.VolumeTray(new string[0], true);
            t3.ExitAction = () => { };
            C.AppHost.ExitRequested(); Application.DoEvents();
            Check(t3.IsExited && t3.Window.IsDisposed, "終了要求 (Teal Hub の「終了」) で本当に閉じる (隠れていても)");

            // 1.5 でスタートアップに登録していた人は、常駐オンのまま (app-settings.json がまだないとき)
            File.Delete(C.AppPrefs.FilePath);
            C.Startup.TrySet("TealVolume", System.Windows.Forms.Application.ExecutablePath, true);
            var t4 = new V.VolumeTray(new string[0], true);
            t4.ExitAction = () => { };
            Application.DoEvents();
            Check(t4.Resident && !t4.IsExited, "1.5 でスタートアップに登録していれば常駐オンとして動く");
            t4.ExitApp();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEALSUITE_DEMO", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }
}
