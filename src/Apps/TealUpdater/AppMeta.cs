// TealUpdater - TealSuite のアップデート (スイートのフォルダの直下に置く。apps.json には載せない)
//   使い方:  TealUpdater.exe <ファイル.tealupdate>   … アップデート
//            TealUpdater.exe --rollback             … 前のバージョンに戻す
//            TealUpdater.exe <ファイル> --expect-version 1.5.0 … Teal Hub がダウンロードしたもの (中身のバージョンを確かめる)
//            (内部用) --suite <フォルダ> --from-temp … %TEMP% にコピーした自分から、元のフォルダを指定して動く
//   バージョンは apps.json の suiteVersion と揃える (build.sh が確認)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Updater - TealSuite のアップデート")]
[assembly: AssemblyDescription("アップデート ファイル (.tealupdate) で TealSuite のアプリを新しくします")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.8.0.0")]
[assembly: AssemblyFileVersion("1.8.0.0")]

namespace TealSuite.Updater
{
    public class UpdaterArgs
    {
        public string Package;
        public bool Rollback, FromTemp;
        public string SuiteDir;
        /// <summary>--expect-version X.Y.Z … Teal Hub がダウンロードしたファイル。中身のバージョンが違えば入れない</summary>
        public string ExpectVersion;

        public static UpdaterArgs Parse(string[] args)
        {
            var r = new UpdaterArgs();
            for (int i = 0; i < (args ?? new string[0]).Length; i++)
            {
                var a = args[i] ?? "";
                if (a.Equals("--rollback", StringComparison.OrdinalIgnoreCase)) r.Rollback = true;
                else if (a.Equals("--from-temp", StringComparison.OrdinalIgnoreCase)) r.FromTemp = true;
                else if (a.Equals("--suite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) r.SuiteDir = args[++i];
                else if (a.Equals("--expect-version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) r.ExpectVersion = args[++i];
                else if (!a.StartsWith("--") && r.Package == null && a.Length > 0) r.Package = a;
            }
            if (string.IsNullOrEmpty(r.SuiteDir)) r.SuiteDir = AppDomain.CurrentDomain.BaseDirectory;
            r.SuiteDir = Path.GetFullPath(r.SuiteDir);
            if (r.Package != null) r.Package = Path.GetFullPath(r.Package);
            return r;
        }

        public string ToCommandLine()
        {
            var parts = new List<string>();
            if (Package != null) parts.Add(Quote(Package));
            if (Rollback) parts.Add("--rollback");
            if (!string.IsNullOrEmpty(ExpectVersion)) { parts.Add("--expect-version"); parts.Add(Quote(ExpectVersion)); }
            parts.Add("--suite"); parts.Add(Quote(SuiteDir.TrimEnd('\\', '/')));
            if (FromTemp) parts.Add("--from-temp");
            return string.Join(" ", parts);
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }
    }

    static class Program
    {
        public const string Id = "TealUpdater";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Updater",
            Role = "アップデート",
            Description = "アップデート ファイル (.tealupdate) で TealSuite のアプリを新しくします。\n置き換える前のファイルは backup フォルダに残るので、前のバージョンに戻せます。",
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppInfo.Init(Spec);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var a = UpdaterArgs.Parse(args);

            // Windows では動いている exe を置き換えられないので、%TEMP% にコピーした自分を起動して、そちらで作業する
            if (AppInfo.IsWindows && !a.FromTemp && (a.Package != null || a.Rollback))
            {
                try
                {
                    CleanOldTempCopies();
                    var dir = Path.Combine(Path.GetTempPath(), "TealSuiteUpdater-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(dir);
                    var copy = Path.Combine(dir, Path.GetFileName(Application.ExecutablePath));
                    File.Copy(Application.ExecutablePath, copy, true);
                    a.FromTemp = true;
                    using (Process.Start(new ProcessStartInfo(copy, a.ToCommandLine()) { UseShellExecute = false, WorkingDirectory = dir })) { }
                    return 0;
                }
                catch { a.FromTemp = false; /* コピーできなければこのまま (TealUpdater.exe 自身は置き換えられないことがある) */ }
            }

            Form form;
            if (a.Rollback) form = UpdaterForm.ForRollback(new UpdateEngine(a.SuiteDir));
            else if (a.Package != null) form = UpdaterForm.ForPackage(new UpdateEngine(a.SuiteDir) { ExpectVersion = a.ExpectVersion }, a.Package);
            else form = UpdaterForm.ForHelp(new UpdateEngine(a.SuiteDir));
            Application.Run(form);
            return 0;
        }

        static void CleanOldTempCopies()
        {
            try
            {
                foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "TealSuiteUpdater-*"))
                    if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-1)) { try { Directory.Delete(d, true); } catch { } }
            }
            catch { }
        }
    }
}
