// TealSuite 共通: Windows のスタートアップ (ログイン時の自動起動)
//  - HKCU\Software\Microsoft\Windows\CurrentVersion\Run に "TealSuite.<id>" = "\"<exe のフルパス>\" --startup" を書く
//    (現在のユーザーだけの設定なので管理者権限は不要)
//  - タスク マネージャーの「スタートアップ」で無効にされた状態 (…\Explorer\StartupApproved\Run) も読む
//  - 1.0 (AppSuite) の exe や手作業のショートカット (shell:startup) を見つけて置き換えられるようにする
//    置き換えるときも削除はせず、退避フォルダへ移動・記録する (利用者がボタンを押したときだけ)
// レジストリとスタートアップ フォルダは差し替えられる (テスト・mono 用): Startup.RunKey / Startup.Folder
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TealSuite.Common
{
    /// <summary>Run キー (値の名前 → コマンドライン) の読み書き</summary>
    public interface IRunKey
    {
        string Get(string name);
        void Set(string name, string command);
        void Delete(string name);
        string[] Names();
        /// <summary>タスク マネージャーで無効にされていれば true (StartupApproved の先頭バイトが奇数)</summary>
        bool IsDisabledByUser(string name);
        /// <summary>タスク マネージャーの無効設定を消す (= 有効に戻す)</summary>
        void ClearDisabled(string name);
    }

    /// <summary>本物のレジストリ (HKCU)</summary>
    public class RegistryRunKey : IRunKey
    {
        public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static Microsoft.Win32.RegistryKey Open(string path, bool write)
        {
            var cu = Microsoft.Win32.Registry.CurrentUser;
            return write ? (cu.OpenSubKey(path, true) ?? cu.CreateSubKey(path)) : cu.OpenSubKey(path, false);
        }
        public string Get(string name)
        {
            using (var k = Open(RunPath, false)) return k == null ? null : k.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        public void Set(string name, string command) { using (var k = Open(RunPath, true)) k.SetValue(name, command, Microsoft.Win32.RegistryValueKind.String); }
        public void Delete(string name) { using (var k = Open(RunPath, true)) if (k != null) k.DeleteValue(name, false); }
        public string[] Names() { using (var k = Open(RunPath, false)) return k == null ? new string[0] : k.GetValueNames(); }
        public bool IsDisabledByUser(string name)
        {
            try { using (var k = Open(ApprovedPath, false)) { var b = k == null ? null : k.GetValue(name) as byte[]; return b != null && b.Length > 0 && (b[0] & 1) == 1; } }
            catch { return false; }
        }
        public void ClearDisabled(string name)
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedPath, true)) if (k != null) k.DeleteValue(name, false); }
            catch { }
        }
    }

    /// <summary>メモリ上の Run キー (テスト用)。Disabled に名前を入れると「タスク マネージャーで無効」扱い。</summary>
    public class MemoryRunKey : IRunKey
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public virtual string Get(string name) { string v; return Values.TryGetValue(name, out v) ? v : null; }
        public virtual void Set(string name, string command) { Values[name] = command; Changed(); }
        public virtual void Delete(string name) { Values.Remove(name); Changed(); }
        public string[] Names() { return Values.Keys.ToArray(); }
        public bool IsDisabledByUser(string name) { return Disabled.Contains(name); }
        public void ClearDisabled(string name) { if (Disabled.Remove(name)) Changed(); }
        protected virtual void Changed() { }
    }

    /// <summary>ファイルに保存する Run キー (mono での画面確認用。TEALSUITE_RUNKEY_FILE で指定)
    /// 1 行 1 件 "名前&lt;TAB&gt;コマンド"。"!名前" の行はタスク マネージャーで無効。</summary>
    public class FileRunKey : MemoryRunKey
    {
        readonly string path;
        public FileRunKey(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.StartsWith("!")) { Disabled.Add(line.Substring(1).Trim()); continue; }
                int t = line.IndexOf('\t');
                if (t > 0) Values[line.Substring(0, t)] = line.Substring(t + 1);
            }
        }
        protected override void Changed()
        {
            var lines = Values.Select(kv => kv.Key + "\t" + kv.Value).Concat(Disabled.Select(d => "!" + d));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }
    }

    /// <summary>ログイン時に開かれるスタートアップ フォルダ (shell:startup) のショートカット</summary>
    public class StartupFolder
    {
        public readonly string Dir;
        /// <summary>.lnk の実行先を返す (分からなければ null)。既定は Windows の WScript.Shell</summary>
        public Func<string, string> ResolveTarget;
        public StartupFolder(string dir, Func<string, string> resolve = null) { Dir = dir; ResolveTarget = resolve ?? ShellLinkTarget; }

        public string[] Shortcuts()
        {
            try
            {
                if (string.IsNullOrEmpty(Dir) || !Directory.Exists(Dir)) return new string[0];
                return Directory.GetFiles(Dir).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            catch { return new string[0]; }
        }

        public static string ShellLinkTarget(string lnk)
        {
            if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return lnk.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? lnk : null;
            if (!AppInfo.IsWindows) return null;
            object shell = null, link = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);
                link = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string;
            }
            catch { return null; }
            finally
            {
                try { if (link != null) Marshal.FinalReleaseComObject(link); } catch { }
                try { if (shell != null) Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }
    }

    public enum StartupStatus { Off, On, PathMismatch, DisabledByUser }

    public class StartupState
    {
        public StartupStatus Status;
        /// <summary>Run キーに書かれているコマンドライン (なければ null)</summary>
        public string Command;
        /// <summary>Command から取り出した exe のパス</summary>
        public string RegisteredPath;
        /// <summary>ログイン時に起動する設定か (場所違いでも登録はされている)</summary>
        public bool Registered { get { return Status == StartupStatus.On || Status == StartupStatus.PathMismatch; } }
    }

    public enum LegacyKind { RunValue, Shortcut }

    /// <summary>置き換え候補: 1.0 の exe や手作業のショートカットなど、TealSuite の正式な登録以外の自動起動</summary>
    public class LegacyStartupItem
    {
        public LegacyKind Kind;
        /// <summary>RunValue: 値の名前 / Shortcut: ファイルのフルパス</summary>
        public string Name;
        /// <summary>実行先 (分かれば)</summary>
        public string Target;
        /// <summary>置き換え先のアプリ ID</summary>
        public string AppId;
        /// <summary>実行される exe のファイル名 (例: ClipboardHistory.exe)。分からなければ null</summary>
        public string ExeName
        {
            get
            {
                if (Target == null && Kind == LegacyKind.Shortcut) return null;   // 実行先が読めないショートカット
                var p = Target ?? Name;
                var i = p.LastIndexOfAny(new[] { '\\', '/' });
                return i >= 0 ? p.Substring(i + 1) : p;
            }
        }
        /// <summary>短い説明: レジストリ「ClipboardHistory」 / スタートアップ フォルダー「…lnk」</summary>
        public string Where
        {
            get { return Kind == LegacyKind.RunValue ? "レジストリ「" + Name + "」" : "スタートアップ フォルダー「" + Path.GetFileName(Name) + "」"; }
        }
        /// <summary>詳しい説明 (確認画面用)</summary>
        public string Describe()
        {
            return (Target != null ? Target : Path.GetFileName(Name)) + "  (" + Where + ")";
        }
    }

    public static class Startup
    {
        /// <summary>Run キーに書くときに付ける引数。アプリはこれを見て静かに (最小化・通知なしで) 起動する。</summary>
        public const string Arg = "--startup";
        public const string ValuePrefix = AppInfo.SuiteName + ".";

        static IRunKey runKey;
        static StartupFolder folder;

        /// <summary>既定: TEALSUITE_RUNKEY_FILE があればそのファイル、なければ HKCU のレジストリ</summary>
        public static IRunKey RunKey
        {
            get
            {
                if (runKey == null)
                {
                    var f = Environment.GetEnvironmentVariable("TEALSUITE_RUNKEY_FILE");
                    runKey = string.IsNullOrEmpty(f) ? (IRunKey)new RegistryRunKey() : new FileRunKey(f);
                }
                return runKey;
            }
            set { runKey = value; }
        }

        /// <summary>既定: TEALSUITE_STARTUP_DIR があればそこ、なければ shell:startup</summary>
        public static StartupFolder Folder
        {
            get
            {
                if (folder == null)
                {
                    var d = Environment.GetEnvironmentVariable("TEALSUITE_STARTUP_DIR");
                    if (string.IsNullOrEmpty(d)) { try { d = Environment.GetFolderPath(Environment.SpecialFolder.Startup); } catch { d = null; } }
                    folder = new StartupFolder(d);
                }
                return folder;
            }
            set { folder = value; }
        }

        /// <summary>起動引数に --startup (または /startup) が含まれるか</summary>
        public static bool HasStartupArg(IEnumerable<string> args)
        {
            return args != null && args.Any(a => a != null && (a.Equals(Arg, StringComparison.OrdinalIgnoreCase) || a.Equals("/startup", StringComparison.OrdinalIgnoreCase)));
        }

        public static string ValueName(string appId) { return ValuePrefix + appId; }

        public static string CommandFor(string exePath) { return "\"" + Path.GetFullPath(exePath) + "\" " + Arg; }

        /// <summary>コマンドラインから exe のパスを取り出す ("C:\a b\x.exe" -x / C:\a\x.exe -x / %LOCALAPPDATA%\x.exe)</summary>
        public static string ParsePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var c = Environment.ExpandEnvironmentVariables(command.Trim());
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                return end > 1 ? c.Substring(1, end - 1) : c.Trim('"');
            }
            // 引用符なし: ".exe" で終わる所まで (空白を含むパスにも対応)
            int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (i + 4 == c.Length || c[i + 4] == ' ')) return c.Substring(0, i + 4);
            int sp = c.IndexOf(' ');
            return sp > 0 ? c.Substring(0, sp) : c;
        }

        /// <summary>同じファイルを指すか (大文字小文字・区切り文字・相対パスの違いは無視)</summary>
        /// <summary>長いパスを「D:\…\TealPass\TealPass.exe」のように縮める (表示用)</summary>
        public static string ShortPath(string p, int maxLength = 48)
        {
            if (string.IsNullOrEmpty(p) || p.Length <= maxLength) return p;
            char sep = p.IndexOf('\\') >= 0 ? '\\' : '/';
            var parts = p.Split(sep);
            if (parts.Length <= 3) return p;
            var tail = parts[parts.Length - 2] + sep + parts[parts.Length - 1];
            return parts[0] + sep + "…" + sep + tail;
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        static string Normalize(string p)
        {
            try { p = Path.GetFullPath(p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)); } catch { }
            return p.TrimEnd(Path.DirectorySeparatorChar);
        }

        public static StartupState Get(string appId, string currentExe)
        {
            var name = ValueName(appId);
            var s = new StartupState();
            try { s.Command = RunKey.Get(name); } catch (Exception ex) { DataStore.Log("startup get: " + ex.Message); }
            if (s.Command == null) { s.Status = StartupStatus.Off; return s; }
            s.RegisteredPath = ParsePath(s.Command);
            bool disabled = false;
            try { disabled = RunKey.IsDisabledByUser(name); } catch { }
            if (disabled) s.Status = StartupStatus.DisabledByUser;
            else s.Status = SamePath(s.RegisteredPath, currentExe) ? StartupStatus.On : StartupStatus.PathMismatch;
            return s;
        }

        /// <summary>ログイン時に起動するよう登録する (場所違い・タスク マネージャーの無効も直す)</summary>
        public static void Enable(string appId, string exePath)
        {
            var name = ValueName(appId);
            RunKey.Set(name, CommandFor(exePath));
            RunKey.ClearDisabled(name);
        }

        public static void Disable(string appId)
        {
            var name = ValueName(appId);
            RunKey.Delete(name);
            RunKey.ClearDisabled(name);
        }

        /// <summary>on/off を切り替える。成功したら null、失敗したら理由。</summary>
        public static string TrySet(string appId, string exePath, bool on)
        {
            try
            {
                if (on) Enable(appId, exePath); else Disable(appId);
                return null;
            }
            catch (Exception ex)
            {
                DataStore.Log("startup set " + appId + ": " + ex);
                return ex.Message;
            }
        }

        /// <summary>アプリ ID → そのアプリとみなす exe のファイル名 (新しい ID と旧 ID)</summary>
        static IEnumerable<string> ExeNames(string id, IEnumerable<string> legacyIds)
        {
            yield return id + ".exe";
            if (legacyIds != null) foreach (var l in legacyIds) if (!string.IsNullOrEmpty(l)) yield return l + ".exe";
        }

        /// <summary>
        /// 正式な登録 (TealSuite.&lt;id&gt;) 以外で、アプリを自動起動している項目を探す。
        /// apps: (ID, 旧 ID の一覧)。exe のファイル名 (新旧どちらも) で判定し、ショートカットの実行先が分からないときは名前で判定する。
        /// </summary>
        public static List<LegacyStartupItem> FindLegacy(IEnumerable<KeyValuePair<string, string[]>> apps)
        {
            var list = new List<LegacyStartupItem>();
            var appList = apps.ToList();
            string[] names = new string[0];
            try { names = RunKey.Names(); } catch (Exception ex) { DataStore.Log("startup names: " + ex.Message); }
            foreach (var n in names)
            {
                if (n.StartsWith(ValuePrefix, StringComparison.OrdinalIgnoreCase)) continue; // 正式な登録
                string cmd = null;
                try { cmd = RunKey.Get(n); } catch { }
                var exe = ParsePath(cmd);
                if (exe == null) continue;
                var file = SafeFileName(exe);
                foreach (var a in appList)
                    if (ExeNames(a.Key, a.Value).Any(x => x.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    { list.Add(new LegacyStartupItem { Kind = LegacyKind.RunValue, Name = n, Target = exe, AppId = a.Key }); break; }
            }
            foreach (var lnk in Folder.Shortcuts())
            {
                string target = null;
                try { target = Folder.ResolveTarget(lnk); } catch { }
                var baseName = Path.GetFileNameWithoutExtension(lnk);
                foreach (var a in appList)
                {
                    var exes = ExeNames(a.Key, a.Value).ToList();
                    bool hit = target != null
                        ? exes.Any(x => x.Equals(SafeFileName(target), StringComparison.OrdinalIgnoreCase))
                        : exes.Any(x => baseName.IndexOf(Path.GetFileNameWithoutExtension(x), StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hit) { list.Add(new LegacyStartupItem { Kind = LegacyKind.Shortcut, Name = lnk, Target = target, AppId = a.Key }); break; }
                }
            }
            return list;
        }

        static string SafeFileName(string p)
        {
            try { return Path.GetFileName(p.Replace('\\', Path.DirectorySeparatorChar)); } catch { return p; }
        }

        /// <summary>
        /// 古い自動起動をやめる。削除はせず、ショートカットは backupDir へ移動し、レジストリの値は内容を backupDir\removed-startup.txt に記録してから消す。
        /// 戻り値: 退避先 (ショートカット) または記録ファイル。
        /// </summary>
        public static string RemoveLegacy(LegacyStartupItem item, string backupDir)
        {
            Directory.CreateDirectory(backupDir);
            var log = Path.Combine(backupDir, "removed-startup.txt");
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (item.Kind == LegacyKind.RunValue)
            {
                var cmd = RunKey.Get(item.Name);
                File.AppendAllText(log, stamp + "  レジストリ HKCU\\" + RegistryRunKey.RunPath + "  \"" + item.Name + "\" = " + cmd + "\r\n", Encoding.UTF8);
                RunKey.Delete(item.Name);
                return log;
            }
            var dest = Path.Combine(backupDir, Path.GetFileName(item.Name));
            for (int i = 2; File.Exists(dest); i++)
                dest = Path.Combine(backupDir, Path.GetFileNameWithoutExtension(item.Name) + " (" + i + ")" + Path.GetExtension(item.Name));
            File.Move(item.Name, dest);
            File.AppendAllText(log, stamp + "  ショートカット " + item.Name + " → " + dest + "\r\n", Encoding.UTF8);
            return dest;
        }

        /// <summary>古い自動起動をやめて、正式な登録 (現在の exe) に置き換える</summary>
        public static void ReplaceLegacy(IEnumerable<LegacyStartupItem> items, string appId, string exePath, string backupDir)
        {
            foreach (var it in items.Where(x => x.AppId == appId).ToList()) RemoveLegacy(it, backupDir);
            Enable(appId, exePath);
        }

        /// <summary>このアプリ自身の exe のパス</summary>
        public static string CurrentExe
        {
            get
            {
                try { var a = Assembly.GetEntryAssembly(); if (a != null) return Path.GetFullPath(a.Location); } catch { }
                return System.Windows.Forms.Application.ExecutablePath;
            }
        }
    }
}
