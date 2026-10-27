// TealSuite 共通: アプリごとの設定 (ショートカット・常駐)。Teal Hub が書き、各アプリは読むだけ
//  保存先: %APPDATA%\TealSuite\TealHub\app-settings.json  (1.6.0 から)
//    { "version": 1, "apps": [ { "id": "TealClip", "hotkey": "Ctrl+Shift+V", "resident": true }, ... ] }
//  - hotkey   … "Alt+V" / "none" (なし)。項目がない = まだ決めていない (Teal Hub が 1.5 の設定から引き継ぐ)
//  - resident … true = 常駐 (×で閉じても通知領域に残る・スタートアップを使える) / false = ×で終了 / 省略 = アプリの初期設定
//  ショートカットを登録するのは Teal Hub だけ。アプリは表示 (「Alt + V で開閉」など) のために読む
//  Teal Hub は書き換えたあと、そのアプリの Reload イベントを鳴らす (アプリは読み直す)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace TealSuite.Common
{
    [DataContract]
    public class AppPref
    {
        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "hotkey", Order = 2, EmitDefaultValue = false)] public string Hotkey;
        [DataMember(Name = "resident", Order = 3, EmitDefaultValue = false)] public bool? Resident;
    }

    [DataContract]
    public class AppPrefsFile
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "apps", Order = 2)] public List<AppPref> Apps = new List<AppPref>();

        public AppPref Find(string appId)
        {
            return Apps.FirstOrDefault(a => a != null && string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
        }

        public AppPref GetOrAdd(string appId)
        {
            var p = Find(appId);
            if (p == null) { p = new AppPref { Id = appId }; Apps.Add(p); }
            return p;
        }
    }

    public static class AppPrefs
    {
        public const string OwnerId = "TealHub";
        public const string FileName = "app-settings.json";
        /// <summary>Teal Hub がショートカットでアプリを起動するときの引数 (起動したらショートカットと同じ動き)</summary>
        public const string ShowArg = "--show";

        public static string FilePath { get { return Path.Combine(DataStore.DirFor(OwnerId), FileName); } }
        public static bool Exists { get { return File.Exists(FilePath); } }

        /// <summary>読む (なければ・読めなければ空)</summary>
        public static AppPrefsFile Load()
        {
            try
            {
                var p = FilePath;
                if (File.Exists(p))
                {
                    var f = DataStore.FromJson<AppPrefsFile>(File.ReadAllBytes(p));
                    if (f != null)
                    {
                        if (f.Apps == null) f.Apps = new List<AppPref>();
                        f.Apps.RemoveAll(a => a == null || string.IsNullOrEmpty(a.Id));
                        return f;
                    }
                }
            }
            catch (Exception ex) { DataStore.Log("app-settings: " + ex.Message); }
            return new AppPrefsFile();
        }

        /// <summary>保存する (Teal Hub だけが呼ぶ)。失敗したら例外</summary>
        public static void Save(AppPrefsFile f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            DataStore.WriteFileAtomic(FilePath, DataStore.ToJson(f));
        }

        /// <summary>保存して、失敗したら理由を返す</summary>
        public static string TrySave(AppPrefsFile f)
        {
            try { Save(f); return null; }
            catch (Exception ex) { DataStore.Log("save app-settings: " + ex); return "設定を保存できませんでした: " + ex.Message; }
        }

        public static AppPref Get(string appId) { return Load().Find(appId); }

        /// <summary>常駐するか (Teal Hub で決めていなければ fallback)</summary>
        public static bool IsResident(string appId, bool fallback)
        {
            var p = Get(appId);
            return p != null && p.Resident.HasValue ? p.Resident.Value : fallback;
        }

        /// <summary>ショートカットの保存値 ("Alt+V" / "none")。決めていなければ null</summary>
        public static string HotkeyOf(string appId)
        {
            var p = Get(appId);
            return p == null ? null : p.Hotkey;
        }

        /// <summary>
        /// 常駐の初期設定。アプリの既定 (Teal Clip だけ true) か、すでにスタートアップに登録されていれば true
        /// (1.5 でスタートアップをオンにしていた人の動きを変えないため)
        /// </summary>
        public static bool DefaultResident(string appId, bool builtIn, string exePath)
        {
            if (builtIn) return true;
            try { return Startup.Get(appId, exePath).Command != null; } catch { return false; }
        }
    }
}
