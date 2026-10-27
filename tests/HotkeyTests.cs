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

        // HotkeyController: 登録の失敗 (ほかのアプリが使用中) を報告し、元に戻す
        string taken = "Ctrl+Alt+K";
        C.GlobalHotkey.FakeRegister = s => s.ToString() == taken ? C.GlobalHotkey.ConflictMessage(s) : null;
        try
        {
            var hc = new C.HotkeyController(V.VolumeTray.DefaultHotkey, "おかしな値");
            Check(hc.Current.ToString() == "Alt+V" && hc.Error == null && hc.Active, "保存値が読めなければ初期設定で登録");
            int changed = 0; hc.Changed += () => changed++;
            var err = hc.Apply(C.HotkeySpec.Of(C.HotMods.Ctrl | C.HotMods.Alt, Keys.K));
            Check(err != null && err.Contains("ほかのアプリ") && hc.Current.ToString() == "Alt+V" && hc.Active && changed == 0, "使用中なら理由を返し、元の Alt+V に戻す");
            Check(hc.Apply(C.HotkeySpec.Of(C.HotMods.Ctrl, Keys.C)) != null && hc.Current.ToString() == "Alt+V", "不可の組み合わせは登録しない");
            Check(hc.Apply(C.HotkeySpec.Of(C.HotMods.Alt, Keys.B)) == null && hc.Current.ToString() == "Alt+B" && changed == 1, "変更に成功");
            hc.Suspend(); Check(!hc.Active, "設定中は一時的に外す"); hc.Resume(); Check(hc.Active, "設定画面を閉じると戻す");
            var hc2 = new C.HotkeyController(V.VolumeTray.DefaultHotkey, taken);
            Check(hc2.Error != null && !hc2.Active && hc2.Current.ToString() == taken, "起動時に登録できなければエラーを覚えておく");
            hc.Dispose(); hc2.Dispose();
        }
        finally { C.GlobalHotkey.FakeRegister = null; }
    }

    /// <summary>Teal Volume の常駐: ×で隠れる・ショートカットで開閉・--startup は隠れて起動・終了</summary>
    public static void RunVolumeTray(Action<bool, string> Check, string tmp)
    {
        C.AppInfo.Init("TealVolume", "Teal Volume", "");
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", "1");
        var key = new C.MemoryRunKey();
        C.Startup.RunKey = key;
        C.Startup.Folder = new C.StartupFolder(Path.Combine(tmp, "vm-startup"), p => null);
        try { File.Delete(C.DataStore.PathOf(V.VolumeTray.SettingsFile)); } catch { }

        // ログイン時 (--startup): 画面を出さない
        var t = new V.VolumeTray(new string[0], true);
        t.ExitAction = () => { };
        Application.DoEvents();
        Check(!t.Window.Visible, "--startup では画面を出さずに常駐");
        Check(t.Window.HotkeyText == "Alt + V で開閉", "ショートカットを画面に表示");

        bool fg = false; t.ForegroundCheck = () => fg;
        t.Hotkeys.SimulatePress(); Application.DoEvents();
        Check(t.Window.Visible, "Alt+V で開く");
        Check(t.Window.FooterText != null && t.Window.FooterText.StartsWith("Alt + V で開閉 ・ "), "下の帯に「Alt + V で開閉」(" + t.Window.FooterText + ")");
        fg = false; t.Hotkeys.SimulatePress(); Application.DoEvents();
        Check(t.Window.Visible, "後ろにあるときの Alt+V は前面に出す (隠さない)");
        fg = true; t.Hotkeys.SimulatePress(); Application.DoEvents();
        Check(!t.Window.Visible && t.TipCount == 1, "前面のときの Alt+V は隠す (初回だけ案内)");

        t.ToggleFromTray(); Application.DoEvents();
        Check(t.Window.Visible, "通知領域のクリックで開く");
        t.Window.Close(); Application.DoEvents();
        Check(!t.Window.IsDisposed && !t.Window.Visible && !t.IsExited, "×では終了せず隠れる");
        Check(t.TipCount == 1, "案内は 1 回だけ");
        t.ShowWindow(); Application.DoEvents();
        t.Window.WindowState = FormWindowState.Minimized; Application.DoEvents();
        t.ShowWindow(); Application.DoEvents();
        Check(t.Window.Visible && t.Window.WindowState == FormWindowState.Normal, "最小化中に開くと元に戻す (2 回目の起動・Teal Hub の「表示」も同じ)");
        Check(C.AppHost.ShowRequested != null && C.AppHost.ExitRequested != null, "2 回目の起動・Teal Hub の「終了」を受け取る");

        // 設定画面: スタートアップのスイッチ (Teal Hub と同じ Run キー)
        t.ShowSettings(); Application.DoEvents();
        var sf = t.OpenSettings;
        Check(sf != null && !sf.StartupSwitch.Checked, "設定画面を開く (スタートアップはオフ)");
        sf.StartupSwitch.Checked = true; sf.SaveForTest(); Application.DoEvents();
        var cmd = key.Get("TealSuite.TealVolume");
        Check(cmd != null && cmd.EndsWith(" --startup") && C.Startup.Get("TealVolume", C.Startup.CurrentExe).Status == C.StartupStatus.On, "設定画面でスタートアップをオン (" + cmd + ")");
        t.ShowSettings(); Application.DoEvents();
        Check(t.OpenSettings != null && t.OpenSettings.StartupSwitch.Checked, "開き直すと Run キーの状態を反映");
        t.OpenSettings.StartupSwitch.Checked = false; t.OpenSettings.SaveForTest(); Application.DoEvents();
        Check(key.Get("TealSuite.TealVolume") == null, "オフにすると Run キーから外す");

        // 終了 (通知領域のメニュー / Teal Hub の「終了」= AppHost.ExitRequested)
        C.AppHost.ExitRequested(); Application.DoEvents();
        Check(t.IsExited && t.Window.IsDisposed, "終了要求で本当に閉じる (隠れていても)");

        // 2 回目: 普通に起動すると画面を出す。案内は済みなので出さない
        var t2 = new V.VolumeTray(new string[0], false);
        t2.ExitAction = () => { };
        Application.DoEvents();
        Check(t2.Window.Visible, "普通に起動すると画面を出す");
        t2.HideWindow();
        Check(t2.TipCount == 0, "常駐の案内は次回からは出さない");
        t2.ExitApp(); Application.DoEvents();
        Check(t2.Window.IsDisposed, "終了");
        Environment.SetEnvironmentVariable("TEALSUITE_DEMO", null);
    }
}
