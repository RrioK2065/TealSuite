// Teal Volume (音量操作) - 音量の読み書き (本物の Core Audio と、画面確認用のデモ)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TealSuite.Common;
using TealSuite.Volume.Interop;

namespace TealSuite.Volume
{
    /// <summary>画面に 1 行として出す単位 (同じアプリの複数セッションはまとめる)</summary>
    public class AudioApp
    {
        public string Key = "";
        public string Name = "";
        public string ExePath;
        public int Pid;
        public bool IsSystem;
        public bool Active;
        public float Volume;      // 0.0 - 1.0
        public bool Muted;
        internal List<object> Handles = new List<object>(); // ISimpleAudioVolume (本物のときだけ)
    }

    public interface IAudioBackend : IDisposable
    {
        string DeviceName { get; }
        float MasterVolume { get; }
        bool MasterMuted { get; }
        bool HasMaster { get; }
        /// <summary>最新の状態を読み直す。前回の AudioApp は使えなくなる。</summary>
        List<AudioApp> Refresh();
        void SetVolume(AudioApp app, float v);
        void SetMute(AudioApp app, bool m);
        void SetMasterVolume(float v);
        void SetMasterMute(bool m);
    }

    public static class AudioNames
    {
        public const string SystemSounds = "システム音";

        /// <summary>表示名を決める: システム音 → セッション名 → exe の説明 → プロセス名</summary>
        public static string Pick(bool isSystem, string sessionName, string fileDescription, string processName)
        {
            if (isSystem) return SystemSounds;
            if (!string.IsNullOrWhiteSpace(sessionName) && !sessionName.StartsWith("@")) return sessionName.Trim();
            if (!string.IsNullOrWhiteSpace(fileDescription)) return fileDescription.Trim();
            if (!string.IsNullOrWhiteSpace(processName)) return processName;
            return "不明なアプリ";
        }

        public static int ToPercent(float v) { return (int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 100f, MidpointRounding.AwayFromZero); }
        public static float FromPercent(int p) { return Math.Max(0, Math.Min(100, p)) / 100f; }
    }

    /// <summary>Windows の Core Audio API を使う本物</summary>
    public class CoreAudioBackend : IAudioBackend
    {
        static Guid ctx = new Guid("6A3F1C52-2B7E-4C1D-9E55-0D3A7A1B5C11"); // 自分が変更したことを示す印
        IMMDeviceEnumerator enumerator;
        IAudioEndpointVolume master;
        string masterDeviceId;
        List<AudioApp> current = new List<AudioApp>();
        readonly Dictionary<int, Tuple<string, string, string>> procCache = new Dictionary<int, Tuple<string, string, string>>();

        public string DeviceName { get; private set; }
        public float MasterVolume { get; private set; }
        public bool MasterMuted { get; private set; }
        public bool HasMaster { get { return master != null; } }

        public CoreAudioBackend()
        {
            var t = Type.GetTypeFromCLSID(CA.CLSID_MMDeviceEnumerator);
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(t);
        }

        static string Friendly(IMMDevice dev)
        {
            IPropertyStore store = null;
            try
            {
                if (dev.OpenPropertyStore(CA.STGM_READ, out store) != CA.S_OK || store == null) return null;
                var key = CA.PKEY_Device_FriendlyName;
                PROPVARIANT pv;
                if (store.GetValue(ref key, out pv) != CA.S_OK) return null;
                string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p1) : null;
                CA.PropVariantClear(ref pv);
                return s;
            }
            catch { return null; }
            finally { CA.Release(store); }
        }

        void RefreshMaster()
        {
            IMMDevice dev = null;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != CA.S_OK || dev == null)
                {
                    CA.Release(master); master = null; masterDeviceId = null; DeviceName = null; return;
                }
                string id; dev.GetId(out id);
                if (master == null || id != masterDeviceId)
                {
                    CA.Release(master); master = null;
                    var iid = CA.IID_IAudioEndpointVolume;
                    object o;
                    if (dev.Activate(ref iid, CA.CLSCTX_ALL, IntPtr.Zero, out o) == CA.S_OK) master = o as IAudioEndpointVolume;
                    masterDeviceId = id;
                    DeviceName = Friendly(dev);
                }
                if (master != null)
                {
                    float v; bool m;
                    if (master.GetMasterVolumeLevelScalar(out v) == CA.S_OK) MasterVolume = v;
                    if (master.GetMute(out m) == CA.S_OK) MasterMuted = m;
                }
            }
            finally { CA.Release(dev); }
        }

        Tuple<string, string, string> ProcInfo(int pid)
        {
            Tuple<string, string, string> info;
            if (procCache.TryGetValue(pid, out info)) return info;
            string path = null, desc = null, pname = null;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    pname = p.ProcessName;
                    path = Native.GetProcessPath(p);
                }
                if (path != null && File.Exists(path)) desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
            }
            catch { }
            info = Tuple.Create(path, desc, pname);
            if (pname != null) procCache[pid] = info;
            return info;
        }

        public List<AudioApp> Refresh()
        {
            RefreshMaster();
            var result = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
            var order = new List<AudioApp>();
            IMMDeviceCollection devices = null;
            try
            {
                if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, CA.DEVICE_STATE_ACTIVE, out devices) != CA.S_OK || devices == null) return Swap(order);
                uint n; devices.GetCount(out n);
                for (uint d = 0; d < n; d++)
                {
                    IMMDevice dev = null; object mgrObj = null; IAudioSessionEnumerator sessions = null;
                    try
                    {
                        if (devices.Item(d, out dev) != CA.S_OK || dev == null) continue;
                        var iid = CA.IID_IAudioSessionManager2;
                        if (dev.Activate(ref iid, CA.CLSCTX_ALL, IntPtr.Zero, out mgrObj) != CA.S_OK) continue;
                        var mgr = mgrObj as IAudioSessionManager2;
                        if (mgr == null || mgr.GetSessionEnumerator(out sessions) != CA.S_OK || sessions == null) continue;
                        int count; sessions.GetCount(out count);
                        for (int i = 0; i < count; i++)
                        {
                            IAudioSessionControl2 s = null;
                            bool keep = false;
                            try
                            {
                                if (sessions.GetSession(i, out s) != CA.S_OK || s == null) continue;
                                AudioSessionState st; s.GetState(out st);
                                if (st == AudioSessionState.Expired) continue;
                                uint pid; s.GetProcessId(out pid);
                                bool isSys = s.IsSystemSoundsSession() == CA.S_OK;
                                if (!isSys && pid == 0) continue;
                                var vol = s as ISimpleAudioVolume; // QueryInterface
                                if (vol == null) continue;
                                string disp; s.GetDisplayName(out disp);
                                var info = isSys ? Tuple.Create<string, string, string>(null, null, null) : ProcInfo((int)pid);
                                if (!isSys && info.Item3 == null) continue; // プロセスが終了済み
                                string key = isSys ? "#system" : (info.Item1 ?? ("pid:" + pid));
                                AudioApp app;
                                if (!result.TryGetValue(key, out app))
                                {
                                    app = new AudioApp
                                    {
                                        Key = key, IsSystem = isSys, Pid = (int)pid, ExePath = info.Item1,
                                        Name = AudioNames.Pick(isSys, disp, info.Item2, info.Item3)
                                    };
                                    float v; bool m;
                                    vol.GetMasterVolume(out v); vol.GetMute(out m);
                                    app.Volume = v; app.Muted = m;
                                    result[key] = app; order.Add(app);
                                }
                                app.Active |= st == AudioSessionState.Active;
                                app.Handles.Add(s); keep = true; // s と vol は同じ RCW
                            }
                            catch (Exception ex) { DataStore.Log("session: " + ex.Message); }
                            finally { if (!keep) CA.Release(s); }
                        }
                    }
                    catch (Exception ex) { DataStore.Log("device: " + ex.Message); }
                    finally { CA.Release(sessions); CA.Release(mgrObj); CA.Release(dev); }
                }
            }
            finally { CA.Release(devices); }
            // システム音は最後、ほかは名前順
            order = order.OrderBy(a => a.IsSystem).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return Swap(order);
        }

        List<AudioApp> Swap(List<AudioApp> next)
        {
            foreach (var a in current) foreach (var h in a.Handles) CA.Release(h);
            current = next;
            if (procCache.Count > 200) procCache.Clear();
            return next;
        }

        public void SetVolume(AudioApp app, float v)
        {
            v = Math.Max(0f, Math.Min(1f, v));
            foreach (var h in app.Handles) { var s = h as ISimpleAudioVolume; if (s != null) s.SetMasterVolume(v, ref ctx); }
            app.Volume = v;
        }

        public void SetMute(AudioApp app, bool m)
        {
            foreach (var h in app.Handles) { var s = h as ISimpleAudioVolume; if (s != null) s.SetMute(m, ref ctx); }
            app.Muted = m;
        }

        public void SetMasterVolume(float v)
        {
            if (master == null) return;
            v = Math.Max(0f, Math.Min(1f, v));
            if (master.SetMasterVolumeLevelScalar(v, ref ctx) == CA.S_OK) MasterVolume = v;
        }

        public void SetMasterMute(bool m)
        {
            if (master == null) return;
            if (master.SetMute(m, ref ctx) == CA.S_OK) MasterMuted = m;
        }

        public void Dispose()
        {
            Swap(new List<AudioApp>());
            CA.Release(master); master = null;
            CA.Release(enumerator); enumerator = null;
        }
    }

    /// <summary>Windows 以外 (開発環境) や TEALSUITE_DEMO=1 のときの見本データ</summary>
    public class DemoBackend : IAudioBackend
    {
        readonly List<AudioApp> apps = new List<AudioApp>
        {
            new AudioApp { Key = "chrome", Name = "Google Chrome", Volume = 0.80f, Active = true },
            new AudioApp { Key = "spotify", Name = "Spotify", Volume = 0.45f, Active = true },
            new AudioApp { Key = "teams", Name = "Microsoft Teams", Volume = 1.00f },
            new AudioApp { Key = "game", Name = "Minecraft", Volume = 0.25f, Muted = true },
            new AudioApp { Key = "#system", Name = AudioNames.SystemSounds, IsSystem = true, Volume = 0.60f },
        };
        public string DeviceName { get { return "スピーカー (Realtek(R) Audio)"; } }
        public float MasterVolume { get; private set; }
        public bool MasterMuted { get; private set; }
        public bool HasMaster { get { return true; } }
        public DemoBackend() { MasterVolume = 0.70f; }
        public List<AudioApp> Refresh() { return apps.ToList(); }
        public void SetVolume(AudioApp app, float v) { app.Volume = Math.Max(0f, Math.Min(1f, v)); }
        public void SetMute(AudioApp app, bool m) { app.Muted = m; }
        public void SetMasterVolume(float v) { MasterVolume = Math.Max(0f, Math.Min(1f, v)); }
        public void SetMasterMute(bool m) { MasterMuted = m; }
        public void Dispose() { }
    }
}
