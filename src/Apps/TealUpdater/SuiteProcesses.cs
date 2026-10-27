// TealUpdater - 起動中のアプリを探して終了してもらい、終わったら起動し直す
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TealSuite.Common;

namespace TealSuite.Updater
{
    public class RunningApp
    {
        public InstalledApp App;
        public string ExePath;
        public List<int> Pids = new List<int>();
        public bool ByMutexOnly;   // exe の場所は取れないが、アプリの実行中マークがある
        public string Name { get { return App.Name; } }
    }

    public static class SuiteProcesses
    {
        /// <summary>このスイートのフォルダにある exe で動いているアプリ</summary>
        public static List<RunningApp> Find(InstalledSuite suite)
        {
            var list = new List<RunningApp>();
            Process[] all;
            try { all = Process.GetProcesses(); } catch { all = new Process[0]; }
            try
            {
                foreach (var a in suite.Apps)
                {
                    if (string.IsNullOrEmpty(a.Exe)) continue;
                    var exe = suite.FullPath(a.Exe);
                    var r = new RunningApp { App = a, ExePath = exe };
                    var name = Path.GetFileNameWithoutExtension(exe);
                    foreach (var p in all)
                    {
                        try
                        {
                            if (!string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) continue;
                            var path = Native.GetProcessPath(p);
                            if (path != null && SamePath(path, exe)) r.Pids.Add(p.Id);
                        }
                        catch { }
                    }
                    if (r.Pids.Count == 0 && AppInfo.IsWindows && MutexExists(a.Id)) r.ByMutexOnly = true;
                    if (r.Pids.Count > 0 || r.ByMutexOnly) list.Add(r);
                }
            }
            finally { foreach (var p in all) p.Dispose(); }
            return list;
        }

        static bool MutexExists(string id)
        {
            try { Mutex m; if (Mutex.TryOpenExisting(AppInfo.MutexName(id), out m)) { m.Dispose(); return true; } } catch { }
            return false;
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), AppInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
            catch { return false; }
        }

        static bool Alive(RunningApp r)
        {
            if (r.ByMutexOnly) return MutexExists(r.App.Id);
            foreach (var id in r.Pids)
            {
                try { using (var p = Process.GetProcessById(id)) if (!p.HasExited) return true; }
                catch { }
            }
            return false;
        }

        /// <summary>穏やかに終了してもらう (Exit イベント → だめならウィンドウを閉じる)。まだ動いているものを返す</summary>
        public static List<RunningApp> CloseAll(IList<RunningApp> apps, int timeoutMs)
        {
            foreach (var r in apps)
            {
                if (AppHost.Signal(AppInfo.ExitEventName(r.App.Id))) continue;
                foreach (var id in r.Pids) { try { using (var p = Process.GetProcessById(id)) p.CloseMainWindow(); } catch { } }
            }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && apps.Any(Alive)) Thread.Sleep(150);
            return apps.Where(Alive).ToList();
        }

        public static List<RunningApp> Kill(IList<RunningApp> apps)
        {
            foreach (var r in apps)
                foreach (var id in r.Pids)
                {
                    try { using (var p = Process.GetProcessById(id)) { p.Kill(); p.WaitForExit(3000); } } catch { }
                }
            Thread.Sleep(300);
            return apps.Where(Alive).ToList();
        }

        /// <summary>
        /// アップデートのあと起動し直す。常駐アプリは --startup (通知領域に静かに)、Teal Hub は画面を出して起動する。
        /// Teal Hub は動いていなくても起動する (結果を見てもらうため)。
        /// </summary>
        public static List<string> Restart(InstalledSuite after, IEnumerable<string> wereRunning, bool startHub)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(wereRunning, StringComparer.OrdinalIgnoreCase);
            foreach (var a in after.Apps)
            {
                bool want = ids.Contains(a.Id) || a.LegacyIds.Any(ids.Contains) || (a.Self && startHub);
                if (!want || string.IsNullOrEmpty(a.Exe)) continue;
                var exe = after.FullPath(a.Exe);
                if (!File.Exists(exe)) { errors.Add(a.Name + ": " + exe + " がありません"); continue; }
                try
                {
                    var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = AppInfo.IsWindows };
                    // 常駐するアプリは画面を出さずに通知領域で起動し直す (常駐の設定は Teal Hub が app-settings.json に保存)
                    if (!a.Self && AppPrefs.IsResident(a.Id, AppPrefs.DefaultResident(a.Id, a.Resident || a.Id == "TealClip", exe))) psi.Arguments = Startup.Arg;
                    using (Process.Start(psi)) { }
                }
                catch (Exception ex) { errors.Add(a.Name + ": " + ex.Message); }
            }
            return errors;
        }
    }
}
