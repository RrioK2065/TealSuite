// TealSuite 共通: 設定・データの保存 (JSON / 原子的な書き込み) と旧データの引き継ぎ
// 保存先: %APPDATA%\TealSuite\<AppId>\   (環境変数 TEALSUITE_DATA_DIR で上書き可。テスト用)
// 1.0 (AppSuite) のデータ %APPDATA%\AppSuite\<旧ID>\ は初回起動時にコピーして引き継ぐ (元は消さない)
using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TealSuite.Common
{
    public static class DataStore
    {
        public static string Root
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
                if (!string.IsNullOrEmpty(over)) return over;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.SuiteName);
            }
        }
        /// <summary>旧スイート (AppSuite 1.0) のデータの場所。テスト時は TEALSUITE_LEGACY_DATA_DIR、なければ TEALSUITE_DATA_DIR と同じ場所</summary>
        public static string LegacyRoot
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("TEALSUITE_LEGACY_DATA_DIR");
                if (!string.IsNullOrEmpty(over)) return over;
                over = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
                if (!string.IsNullOrEmpty(over)) return over;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.LegacySuiteName);
            }
        }
        public const string MigratedMarker = "migrated-from.txt";

        static bool HasFiles(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext(); }
            catch { return false; }
        }

        /// <summary>
        /// 新しい保存先がまだ空で、旧 ID のフォルダにデータがあればフォルダごとコピーする。
        /// 元のフォルダは消さない。引き継いだら元のフォルダのパスを返す (なければ null)。
        /// 一時フォルダにコピーしてから名前を変えるので、途中で失敗しても中途半端な状態は残らない。
        /// </summary>
        public static string MigrateLegacyData()
        {
            var ids = AppInfo.LegacyIds;
            if (ids == null || ids.Length == 0 || HasFiles(AppDir)) return null;
            foreach (var oldId in ids)
            {
                string src = Path.Combine(LegacyRoot, oldId);
                if (string.Equals(Path.GetFullPath(src).TrimEnd('\\', '/'), Path.GetFullPath(AppDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasFiles(src)) continue;
                string tmp = AppDir + ".migrating";
                try
                {
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    CopyDir(src, tmp);
                    File.WriteAllText(Path.Combine(tmp, MigratedMarker),
                        "このフォルダのデータは次の場所から引き継ぎました (元のデータは消していません)。" + Environment.NewLine +
                        src + Environment.NewLine + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + AppInfo.Version + Environment.NewLine, Encoding.UTF8);
                    Directory.CreateDirectory(Root);
                    if (Directory.Exists(AppDir)) Directory.Delete(AppDir, false); // 空のときだけ (HasFiles で確認済み)
                    Directory.Move(tmp, AppDir);
                    Log("旧データを引き継ぎました: " + src);
                    return src;
                }
                catch (Exception ex)
                {
                    try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                    Log("旧データの引き継ぎに失敗: " + src + " : " + ex.Message);
                    return null;
                }
            }
            return null;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), false);
            }
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
        public static string AppDir { get { return Path.Combine(Root, AppInfo.Id ?? "Unknown"); } }
        public static string PathOf(string fileName) { return Path.Combine(AppDir, fileName); }

        // ---- JSON ----
        public static T FromJson<T>(byte[] bytes) where T : class
        {
            if (bytes == null || bytes.Length == 0) return null;
            int off = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0; // BOM を許容
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(bytes, off, bytes.Length - off))
                return (T)ser.ReadObject(ms);
        }
        public static byte[] ToJson<T>(T obj)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, obj); return ms.ToArray(); }
        }

        /// <summary>ファイルがなければ null。壊れていれば例外。</summary>
        public static T Load<T>(string fileName) where T : class
        {
            string path = PathOf(fileName);
            if (!File.Exists(path)) return null;
            return FromJson<T>(File.ReadAllBytes(path));
        }

        /// <summary>読めなければ (壊れていれば) 退避して既定値を返す。warning に利用者向けメッセージ。</summary>
        public static T LoadOrDefault<T>(string fileName, Func<T> makeDefault, out string warning) where T : class
        {
            warning = null;
            try { return Load<T>(fileName) ?? makeDefault(); }
            catch (Exception ex)
            {
                string backup = Backup(fileName);
                warning = "データファイルを読み込めませんでした。\n元のファイルは次の場所に退避しました:\n" + backup + "\n\n(" + ex.Message + ")";
                return makeDefault();
            }
        }

        /// <summary>設定ファイルなど、読めなくても黙って既定値でよいもの</summary>
        public static T LoadQuiet<T>(string fileName, Func<T> makeDefault) where T : class
        {
            try { return Load<T>(fileName) ?? makeDefault(); } catch { return makeDefault(); }
        }

        public static void Save<T>(string fileName, T obj) { WriteBytes(fileName, ToJson(obj)); }
        public static bool TrySave<T>(string fileName, T obj) { try { Save(fileName, obj); return true; } catch { return false; } }

        public static string Backup(string fileName)
        {
            string path = PathOf(fileName);
            string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(path, backup, true); } catch { }
            return backup;
        }

        public static byte[] ReadBytes(string fileName)
        {
            string path = PathOf(fileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>一時ファイルに書いてから置き換える (途中で落ちてもデータが壊れない)</summary>
        public static void WriteBytes(string fileName, byte[] data)
        {
            Directory.CreateDirectory(AppDir);
            WriteFileAtomic(PathOf(fileName), data);
        }

        /// <summary>ほかのアプリのデータフォルダ (%APPDATA%\TealSuite\&lt;appId&gt;)</summary>
        public static string DirFor(string appId) { return Path.Combine(Root, appId); }

        /// <summary>任意の場所に、一時ファイル経由で書き込む (フォルダがなければ作る)</summary>
        public static void WriteFileAtomic(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                File.AppendAllText(PathOf("error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + AppInfo.Version + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
