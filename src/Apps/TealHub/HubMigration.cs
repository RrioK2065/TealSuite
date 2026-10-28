// Teal Hub - 1.9.0: 名前が変わったアプリ (Teal Sub → Teal Wallet) の設定を引き継ぐ
//  apps.json の legacyIds に、前の名前 (RenameMigration.OldNames にある ID) が書かれたアプリについて
//   ・app-settings.json のショートカット・常駐の項目を新しい ID に付け替える
//   ・スタートアップ (HKCU\…\Run の TealSuite.<旧ID>) を新しい exe で登録し直す
//  アプリのデータ (%APPDATA%\TealSuite\<旧ID>\) は、アプリ自身が初めて起動したときにコピーする (DataStore.MigrateLegacyData)
using System;
using System.Collections.Generic;
using System.Linq;
using TealSuite.Common;

namespace TealSuite.Hub
{
    public static class HubMigration
    {
        /// <summary>引き継いだアプリの説明 (「Teal Sub → Teal Wallet」)。何もしなければ空</summary>
        public static List<string> Run(IEnumerable<AppEntry> apps)
        {
            var done = new List<string>();
            AppPrefsFile prefs = null;
            bool prefsChanged = false;
            foreach (var a in apps)
            {
                if (a == null || a.LegacyIds == null) continue;
                var olds = a.LegacyIds.Where(RenameMigration.OldNames.ContainsKey).ToList();
                if (olds.Count == 0) continue;
                bool any = false;
                try
                {
                    if (AppPrefs.Exists)
                    {
                        if (prefs == null) prefs = AppPrefs.Load();
                        if (RenameMigration.MigratePrefs(prefs, a.Id, olds)) { prefsChanged = true; any = true; }
                    }
                }
                catch (Exception ex) { DataStore.Log("rename prefs: " + ex.Message); }
                foreach (var o in olds)
                {
                    try { if (RenameMigration.MigrateStartup(o, a.Id, a.ExePath)) any = true; }
                    catch (Exception ex) { DataStore.Log("rename startup: " + ex.Message); }
                }
                if (any) done.Add(RenameMigration.OldName(olds[0]) + " → " + a.Name);
            }
            if (prefsChanged)
            {
                var err = AppPrefs.TrySave(prefs);
                if (err != null) DataStore.Log(err);
            }
            return done;
        }

        /// <summary>フッターに出す文</summary>
        public static string Notice(List<string> done)
        {
            if (done == null || done.Count == 0) return null;
            return string.Join("・", done.Select(d => d.Replace(" → ", " を ") + " に")) + "引き継ぎました (ショートカット・常駐・スタートアップ)";
        }
    }
}
