// Teal Hub (アプリ管理) - アップデート ファイル (.tealupdate) の関連付けと TealUpdater.exe の呼び出し
//  - HKCU\Software\Classes に登録する (管理者権限は不要)。開くと TealUpdater.exe "<ファイル>" が起動する
//  - Teal Hub が起動するたびに確認し、フォルダーを移動していれば登録し直す (同じなら何もしない)
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>関連付けの保存先 (テストでは差し替える)</summary>
    public interface IAssocStore
    {
        /// <summary>今の「開く」コマンド (なければ null)</summary>
        string GetCommand();
        void Register(string command, string icon, string description);
    }

    public class RegistryAssoc : IAssocStore
    {
        const string Classes = @"Software\Classes\";
        public string GetCommand()
        {
            try
            {
                using (var ext = Registry.CurrentUser.OpenSubKey(Classes + FileAssoc.Extension))
                {
                    if (ext == null || !FileAssoc.ProgId.Equals(ext.GetValue("") as string, StringComparison.OrdinalIgnoreCase)) return null;
                }
                using (var cmd = Registry.CurrentUser.OpenSubKey(Classes + FileAssoc.ProgId + @"\shell\open\command"))
                    return cmd == null ? null : cmd.GetValue("") as string;
            }
            catch { return null; }
        }

        public void Register(string command, string icon, string description)
        {
            using (var ext = Registry.CurrentUser.CreateSubKey(Classes + FileAssoc.Extension))
            {
                ext.SetValue("", FileAssoc.ProgId);
                ext.SetValue("Content Type", "application/x-tealsuite-update");
            }
            using (var prog = Registry.CurrentUser.CreateSubKey(Classes + FileAssoc.ProgId))
            {
                prog.SetValue("", description);
                using (var di = prog.CreateSubKey("DefaultIcon")) di.SetValue("", icon);
                using (var c = prog.CreateSubKey(@"shell\open\command")) c.SetValue("", command);
                using (var o = prog.CreateSubKey(@"shell\open")) o.SetValue("", "アップデートする");
            }
            try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }   // SHCNE_ASSOCCHANGED
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }

    public static class FileAssoc
    {
        public const string Extension = ".tealupdate";
        public const string ProgId = "TealSuite.UpdatePackage";
        public const string Description = "TealSuite アップデート ファイル";
        /// <summary>null なら Windows のレジストリ (Windows 以外では何もしない)</summary>
        public static IAssocStore Store;

        public static string CommandFor(string updaterExe) { return "\"" + Path.GetFullPath(updaterExe) + "\" \"%1\""; }

        /// <summary>関連付けが今の TealUpdater.exe を指していなければ登録し直す。登録したら true</summary>
        public static bool Ensure(string updaterExe)
        {
            try
            {
                var store = Store;
                if (store == null) { if (!AppInfo.IsWindows) return false; store = new RegistryAssoc(); }
                if (!File.Exists(updaterExe)) return false;
                var cmd = CommandFor(updaterExe);
                if (string.Equals(store.GetCommand(), cmd, StringComparison.OrdinalIgnoreCase)) return false;
                store.Register(cmd, "\"" + Path.GetFullPath(updaterExe) + "\",0", Description);
                DataStore.Log("file association: " + cmd);
                return true;
            }
            catch (Exception ex) { DataStore.Log("file association: " + ex.Message); return false; }
        }
    }

    /// <summary>TealUpdater.exe (スイートのフォルダーの直下) を起動する</summary>
    public static class UpdaterLauncher
    {
        public const string Extension = FileAssoc.Extension;
        public const string ExeName = "TealUpdater.exe";
        /// <summary>テスト用: 起動を差し替える (exe, 引数)</summary>
        public static Action<string, string> StartProcess;

        public static string SuiteDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string UpdaterExe { get { return Path.Combine(SuiteDir, ExeName); } }
        public static string BackupDir { get { return Path.Combine(SuiteDir, "backup"); } }

        public const string HowTo =
            "アップデートのしかた\n\n"
            + "新しいバージョンが公開されると、Teal Hub が自動で見つけてお知らせします (1 日 1 回。「アップデート ▾」→「更新を確認」でもすぐ確認できます)。\n"
            + "お知らせの「今すぐアップデート」を押すと、ダウンロードして確認画面を出します。\n\n"
            + "ファイルで更新するときは:\n"
            + "1. アップデート ファイル (TealSuite-update-x.y.z.tealupdate) を受け取ります。\n"
            + "2. ファイルをダブルクリックするか、Teal Hub の画面にドラッグします (「アップデート ▾」→「アップデート ファイルを開く…」でも可)。\n"
            + "3. 変わる内容 (アプリごとの古いバージョン → 新しいバージョン) を確認して「アップデート」を押します。\n\n"
            + "起動中のアプリはいったん終了し、アップデート後に自動で起動し直します。\n"
            + "置き換える前のファイルは backup フォルダーに残り、「前のバージョンに戻す…」でいつでも戻せます。\n"
            + "パスワードやメモなどのデータ (%APPDATA%\\TealSuite) は変わりません。";

        static bool Launch(IWin32Window owner, string args)
        {
            var exe = UpdaterExe;
            if (StartProcess != null) { StartProcess(exe, args); return true; }
            if (!File.Exists(exe))
            {
                Dialogs.Error(owner, ExeName + " が見つかりません。\n\n" + exe + "\n\nTealSuite 1.4.0 以降をフォルダーごと入れ直してください。");
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = SuiteDir });
                return true;
            }
            catch (Exception ex) { Dialogs.Error(owner, ExeName + " を起動できませんでした。\n\n" + ex.Message); return false; }
        }

        public static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }

        /// <summary>アップデート ファイルを開く (確認画面は TealUpdater が出す)</summary>
        public static bool Open(IWin32Window owner, string package)
        {
            if (!File.Exists(package)) { Dialogs.Error(owner, "ファイルが見つかりません。\n\n" + package); return false; }
            return Launch(owner, Quote(Path.GetFullPath(package)));
        }

        /// <summary>Teal Hub がダウンロードしたアップデート ファイルを開く (中身のバージョンが version でなければ TealUpdater が断る)</summary>
        public static bool OpenDownloaded(IWin32Window owner, string package, string version)
        {
            if (!File.Exists(package)) { Dialogs.Error(owner, "ファイルが見つかりません。\n\n" + package); return false; }
            return Launch(owner, Quote(Path.GetFullPath(package)) + " --expect-version " + Quote(version));
        }

        public static bool HasBackups
        {
            get
            {
                try { return Directory.Exists(BackupDir) && Directory.GetDirectories(BackupDir).Any(d => File.Exists(Path.Combine(d, "backup.json"))); }
                catch { return false; }
            }
        }

        /// <summary>前のバージョンに戻す (どれに戻すかは TealUpdater で選ぶ)</summary>
        public static bool Rollback(IWin32Window owner)
        {
            if (!HasBackups) { Dialogs.Info(owner, "戻せる前のバージョンはありません。\n\nアップデートすると、置き換える前のファイルが backup フォルダーに残り、ここから戻せるようになります。"); return false; }
            return Launch(owner, "--rollback");
        }
    }
}
