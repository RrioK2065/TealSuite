// TealSuite 共通 (1.10.0): ほかのアプリに「この項目を開いて」と頼む
//  - 頼む側: データフォルダの <アプリ>\open-request.json に {kind, key, at} を書き、Local\TealSuite.<アプリ>.Open を合図する。
//    アプリが起動していなければ exe を起動する (起動したアプリが頼みごとを読む)
//  - 頼まれた側: AppHost が合図を受けて画面を前に出し、AppHost.OpenRequested を呼ぶ。起動したときにも 1 回読む
//  - 頼みごとは 1 回読んだら消す。5 分より古いものは使わない
//  - key は項目の id (Teal Pass だけはサイト名。Teal Pass の id は外に出さない)
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace TealSuite.Common
{
    [DataContract]
    public class OpenRequest
    {
        public const string FileName = "open-request.json";
        public const int MaxAgeSeconds = 300;

        /// <summary>何を開くか (Teal Wallet: sub / shop / want、Teal Plan: task / event、Teal Memo: note、Teal Pass: site)</summary>
        [DataMember(Name = "kind")] public string Kind;
        [DataMember(Name = "key")] public string Key;
        /// <summary>頼んだ時刻 (UTC の ticks)</summary>
        [DataMember(Name = "at")] public long At;

        public override string ToString() { return Kind + ":" + Key; }

        public static string PathFor(string appId) { return Path.Combine(DataStore.DirFor(appId), FileName); }

        /// <summary>頼みごとを書く (合図はしない)。書けたら true</summary>
        public static bool Write(string appId, string kind, string key)
        {
            try
            {
                var p = PathFor(appId);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                DataStore.WriteFileAtomic(p, DataStore.ToJson(new OpenRequest { Kind = kind, Key = key, At = DateTime.UtcNow.Ticks }));
                return true;
            }
            catch (Exception ex) { DataStore.Log("open request: " + ex.Message); return false; }
        }

        /// <summary>起動中のアプリに合図する。アプリが起動していなければ false</summary>
        public static bool Notify(string appId) { return AppHost.Signal(AppInfo.OpenEventName(appId)); }

        /// <summary>
        /// 頼みごとを書いて合図する。起動していなければ exePath (null なら同じスイートの apps\&lt;アプリ&gt;\&lt;アプリ&gt;.exe) を起動する。
        /// 戻り値: "open" (起動中のアプリに頼んだ) / "start" (起動した) / "missing" (アプリが見つからない) / "error"
        /// </summary>
        public static string Send(string appId, string kind, string key, string exePath = null)
        {
            if (!Write(appId, kind, key)) return "error";
            if (Notify(appId)) return "open";
            var exe = exePath ?? DefaultExe(appId);
            if (exe == null || !File.Exists(exe)) { Discard(appId); return "missing"; }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                return "start";
            }
            catch (Exception ex) { DataStore.Log("open request start " + appId + ": " + ex.Message); Discard(appId); return "error"; }
        }

        /// <summary>同じスイートにあるアプリの exe (アプリから: ..\&lt;アプリ&gt;\、Teal Hub から: apps\&lt;アプリ&gt;\)</summary>
        public static string DefaultExe(string appId)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var cands = new[] {
                    Path.Combine(Path.Combine(baseDir, ".."), Path.Combine(appId, appId + ".exe")),
                    Path.Combine(Path.Combine(baseDir, "apps"), Path.Combine(appId, appId + ".exe")),
                };
                foreach (var c in cands) if (File.Exists(c)) return Path.GetFullPath(c);
            }
            catch { }
            return null;
        }

        public static void Discard(string appId) { try { File.Delete(PathFor(appId)); } catch { } }

        /// <summary>自分あての頼みごとを読んで消す (なければ・古ければ・壊れていれば null)</summary>
        public static OpenRequest Take(string appId)
        {
            try
            {
                var p = PathFor(appId);
                if (!File.Exists(p)) return null;
                byte[] bytes = File.ReadAllBytes(p);
                try { File.Delete(p); } catch { }
                var r = DataStore.FromJson<OpenRequest>(bytes);
                if (r == null || string.IsNullOrEmpty(r.Kind)) return null;
                var age = DateTime.UtcNow - new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, r.At)), DateTimeKind.Utc);
                if (age.TotalSeconds > MaxAgeSeconds || age.TotalSeconds < -5) return null;
                return r;
            }
            catch { return null; }
        }
    }
}
