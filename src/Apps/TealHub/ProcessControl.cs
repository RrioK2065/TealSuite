// Teal Hub (アプリ管理) - プロセスの検出・起動・終了
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public enum StopResult { NotRunning, Closed, StillRunning }

    /// <summary>
    /// 実行中かどうかは「exe のフルパスが一致するプロセスがあるか」で判定する。
    /// 自分で起動したプロセスも追跡するので、パスが取れない環境 (mono のテストなど) でも動く。
    /// </summary>
    public class ProcessControl
    {
        readonly Dictionary<string, Process> started = new Dictionary<string, Process>(StringComparer.OrdinalIgnoreCase);
        readonly object gate = new object();

        static string NameKey(string exePath) { return Path.GetFileNameWithoutExtension(exePath); }

        /// <summary>全アプリぶんの実行中プロセス ID をまとめて調べる (タイマーから 1 回/周期)</summary>
        public Dictionary<string, List<int>> Snapshot(IList<AppEntry> apps)
        {
            var result = apps.ToDictionary(a => a.Id, a => new List<int>(), StringComparer.OrdinalIgnoreCase);
            var byName = apps.GroupBy(a => NameKey(a.ExePath), StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            Process[] all;
            try { all = Process.GetProcesses(); } catch { all = new Process[0]; }
            foreach (var p in all)
            {
                try
                {
                    List<AppEntry> cands;
                    if (!byName.TryGetValue(p.ProcessName, out cands)) continue;
                    var path = Native.GetProcessPath(p);
                    if (path == null) continue;
                    foreach (var a in cands)
                        if (SamePath(path, a.ExePath)) result[a.Id].Add(p.Id);
                }
                catch { }
                finally { p.Dispose(); }
            }
            lock (gate)
            {
                foreach (var kv in started.ToList())
                {
                    bool alive;
                    try { alive = !kv.Value.HasExited; } catch { alive = false; }
                    if (!alive) { kv.Value.Dispose(); started.Remove(kv.Key); continue; }
                    List<int> ids;
                    if (result.TryGetValue(kv.Key, out ids) && !ids.Contains(kv.Value.Id)) ids.Add(kv.Value.Id);
                }
            }
            return result;
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), AppInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
            catch { return false; }
        }

        public void Start(AppEntry app)
        {
            if (!File.Exists(app.ExePath)) throw new FileNotFoundException("実行ファイルが見つかりません。\n" + app.ExePath);
            var psi = new ProcessStartInfo(app.ExePath)
            {
                WorkingDirectory = Path.GetDirectoryName(app.ExePath),
                UseShellExecute = AppInfo.IsWindows,
            };
            var p = Process.Start(psi);
            if (p != null) lock (gate) { Process old; if (started.TryGetValue(app.Id, out old)) old.Dispose(); started[app.Id] = p; }
        }

        /// <summary>実行中のアプリの画面を前面に出す (アプリの Show イベントを鳴らす)</summary>
        public bool Show(AppEntry app)
        {
            try { if (AppInfo.IsWindows) Native.AllowSetForegroundWindow(-1); } catch { }
            return AppHost.Signal(AppInfo.ShowEventName(app.Id));
        }

        /// <summary>
        /// 穏やかに終了を頼む: 1) アプリの Exit イベント  2) 応答がなければメインウィンドウを閉じる。
        /// timeoutMs 以内に全プロセスが終われば Closed。UI スレッド以外から呼ぶこと。
        /// </summary>
        public StopResult RequestStop(AppEntry app, IList<int> pids, int timeoutMs)
        {
            var procs = Open(pids);
            try
            {
                if (procs.Count == 0) return StopResult.NotRunning;
                bool signaled = AppHost.Signal(AppInfo.ExitEventName(app.Id));
                if (!signaled) foreach (var p in procs) TryCloseMainWindow(p);
                var sw = Stopwatch.StartNew();
                bool triedClose = !signaled;
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (procs.All(Exited)) return StopResult.Closed;
                    // Exit イベントで反応がなければ途中からウィンドウを閉じる方法も試す
                    if (!triedClose && sw.ElapsedMilliseconds > timeoutMs / 2) { foreach (var p in procs) TryCloseMainWindow(p); triedClose = true; }
                    Thread.Sleep(100);
                }
                return procs.All(Exited) ? StopResult.Closed : StopResult.StillRunning;
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        /// <summary>強制終了 (保存していない内容は失われる)</summary>
        public bool Kill(IList<int> pids, int waitMs = 3000)
        {
            var procs = Open(pids);
            try
            {
                foreach (var p in procs) { try { if (!p.HasExited) p.Kill(); } catch { } }
                foreach (var p in procs) { try { p.WaitForExit(waitMs); } catch { } }
                return procs.All(Exited);
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        List<Process> Open(IList<int> pids)
        {
            var list = new List<Process>();
            foreach (var id in pids)
            {
                Process own = null;
                lock (gate) own = started.Values.FirstOrDefault(p => { try { return p.Id == id; } catch { return false; } });
                try { list.Add(own != null ? Process.GetProcessById(own.Id) : Process.GetProcessById(id)); }
                catch { } // もう終わっている
            }
            return list;
        }

        static bool Exited(Process p) { try { p.Refresh(); return p.HasExited; } catch { return true; } }
        static void TryCloseMainWindow(Process p) { try { p.Refresh(); p.CloseMainWindow(); } catch { } }
    }
}
