// TealSuite 共通: アプリから Teal Hub の設定ページを開く
//  ショートカット・常駐・スタートアップの設定は Teal Hub にまとめた (1.6.0)。
//  各アプリの「設定」は TealHub.exe --settings <アプリ ID> を起動するだけ。
//  Teal Hub がすでに起動していれば、2 回目の起動が頼みごとをファイル (TealHub\open-settings.txt) に書いて
//  Show イベントを鳴らし、起動中の Teal Hub がそれを読んでページを切り替える。
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public static class HubLink
    {
        public const string SettingsArg = "--settings";
        public const string RequestFile = "open-settings.txt";
        /// <summary>この秒数より古い頼みごとは無視する (前の起動の残りなど)</summary>
        public const int RequestMaxAgeSeconds = 30;

        /// <summary>テスト用: 起動を差し替える (exe, 引数)。null なら実際に起動する</summary>
        public static Action<string, string> LaunchHook;

        /// <summary>TealHub.exe の場所 (アプリは apps\&lt;ID&gt;\ にあるので 2 つ上。Teal Hub 自身なら同じフォルダ)</summary>
        public static string HubExe
        {
            get
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var same = Path.Combine(dir, "TealHub.exe");
                if (File.Exists(same)) return same;
                return Path.GetFullPath(Path.Combine(dir, "..", "..", "TealHub.exe"));
            }
        }

        public static string RequestPath { get { return Path.Combine(DataStore.DirFor(AppPrefs.OwnerId), RequestFile); } }

        /// <summary>Teal Hub で appId の設定ページを開く。開けなければ案内を出して false</summary>
        public static bool OpenSettings(IWin32Window owner, string appId)
        {
            var exe = HubExe;
            try
            {
                try { if (AppInfo.IsWindows) Native.AllowSetForegroundWindow(-1); } catch { }
                if (LaunchHook != null) { LaunchHook(exe, SettingsArg + " " + appId); return true; }
                if (!File.Exists(exe)) throw new FileNotFoundException(exe);
                Process.Start(new ProcessStartInfo(exe, SettingsArg + " " + appId) { UseShellExecute = AppInfo.IsWindows, WorkingDirectory = Path.GetDirectoryName(exe) });
                return true;
            }
            catch (Exception ex)
            {
                DataStore.Log("open hub settings: " + ex.Message);
                try { Dialogs.Info(owner, "設定 (ショートカット・常駐・スタートアップ) は Teal Hub で変更します。\nTeal Hub を開いて、" + AppInfo.DisplayName + " の「設定」ボタンを押してください。\n\n(Teal Hub が見つかりませんでした: " + exe + ")"); } catch { }
                return false;
            }
        }

        /// <summary>コマンドラインの --settings &lt;ID&gt; (なければ null)</summary>
        public static string SettingsTarget(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], SettingsArg, StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "/settings", StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : AppPrefs.OwnerId;
            return null;
        }

        /// <summary>Teal Hub の 2 回目の起動が、起動中の Teal Hub への頼みごとを書く</summary>
        public static void WriteRequest(string appId)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RequestPath));
                DataStore.WriteFileAtomic(RequestPath, System.Text.Encoding.UTF8.GetBytes(appId + "\n" + DateTime.UtcNow.Ticks));
            }
            catch (Exception ex) { DataStore.Log("settings request: " + ex.Message); }
        }

        /// <summary>頼みごとを読んで消す (なければ・古ければ null)</summary>
        public static string TakeRequest()
        {
            try
            {
                var p = RequestPath;
                if (!File.Exists(p)) return null;
                var lines = File.ReadAllText(p).Split('\n').Select(l => l.Trim()).ToArray();
                File.Delete(p);
                long ticks;
                if (lines.Length < 2 || !long.TryParse(lines[1], out ticks)) return null;
                var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                if (age.TotalSeconds > RequestMaxAgeSeconds || age.TotalSeconds < -5) return null;
                return lines[0].Length > 0 ? lines[0] : null;
            }
            catch { return null; }
        }
    }
}
