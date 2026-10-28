// TealSuite 共通: アプリの名前 (ID) が変わったときの引き継ぎ (1.9.0 から。Teal Sub → Teal Wallet)
//  - データ        … DataStore.MigrateLegacyData() が %APPDATA%\TealSuite\<旧ID>\ もコピーする (元は消さない = 1.8.0 に戻しても使える)
//  - Hub の設定    … app-settings.json の旧 ID の項目 (ショートカット・常駐) を新しい ID に付け替える
//  - スタートアップ … HKCU\…\Run の "TealSuite.<旧ID>" を消して "TealSuite.<新ID>" (新しい exe) に登録し直す
//                    (タスク マネージャーで無効にしていたときは登録し直さない = オフのまま)
//  どれも何度呼んでも同じ結果になる (2 回目からは何もしない)。Teal Hub と、名前が変わったアプリ自身の両方が起動時に呼ぶ
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TealSuite.Common
{
    public static class RenameMigration
    {
        /// <summary>表示用: 前の ID → 前の名前</summary>
        public static readonly Dictionary<string, string> OldNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TealSub", "Teal Sub" },
        };

        public static string OldName(string id) { string n; return OldNames.TryGetValue(id ?? "", out n) ? n : id; }

        /// <summary>
        /// app-settings.json の旧 ID の項目を新しい ID にする。新しい ID の項目がすでにあれば、
        /// 決まっていない値 (ショートカット・常駐) だけ旧い項目から補って、旧い項目は消す。変えたら true
        /// </summary>
        public static bool MigratePrefs(AppPrefsFile f, string newId, IEnumerable<string> oldIds)
        {
            if (f == null || string.IsNullOrEmpty(newId) || oldIds == null) return false;
            bool changed = false;
            foreach (var oldId in oldIds)
            {
                if (string.IsNullOrEmpty(oldId) || string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase)) continue;
                var old = f.Find(oldId);
                if (old == null) continue;
                var now = f.Find(newId);
                if (now == null) old.Id = newId;
                else
                {
                    if (now.Hotkey == null) now.Hotkey = old.Hotkey;
                    if (!now.Resident.HasValue) now.Resident = old.Resident;
                    f.Apps.Remove(old);
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// スタートアップの登録を旧 ID から新しい ID へ移す。変えたら true。
        /// newExe が見つからないとき (まだ置き換え中など) は何もしない
        /// </summary>
        public static bool MigrateStartup(string oldId, string newId, string newExe)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrEmpty(newExe) || !File.Exists(newExe)) return false;
            var oldName = Startup.ValueName(oldId);
            var rk = Startup.RunKey;
            string cmd = rk.Get(oldName);
            if (cmd == null) return false;
            bool disabled = false;
            try { disabled = rk.IsDisabledByUser(oldName); } catch { }
            if (!disabled && rk.Get(Startup.ValueName(newId)) == null) Startup.Enable(newId, newExe);
            rk.Delete(oldName);
            try { rk.ClearDisabled(oldName); } catch { }
            DataStore.Log("スタートアップを引き継ぎました: " + oldName + " → " + Startup.ValueName(newId) + (disabled ? " (無効にされていたので登録しない)" : ""));
            return true;
        }

        /// <summary>
        /// 名前が変わったアプリ自身が起動時に呼ぶ: Hub の設定ファイルはそのまま (Hub だけが書く)、スタートアップだけ直す。
        /// </summary>
        public static void RunForSelf()
        {
            var ids = AppInfo.LegacyIds;
            if (ids == null) return;
            foreach (var oldId in ids.Where(OldNames.ContainsKey))
            {
                try { MigrateStartup(oldId, AppInfo.Id, Startup.CurrentExe); }
                catch (Exception ex) { DataStore.Log("rename startup: " + ex.Message); }
            }
        }
    }
}
