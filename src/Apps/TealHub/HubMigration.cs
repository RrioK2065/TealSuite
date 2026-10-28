// Teal Hub - 1.9.0: 名前が変わったアプリ (Teal Sub → Teal Wallet、1.10.0: Teal Tasks → Teal Plan) の設定を引き継ぐ
//  apps.json の legacyIds に、前の名前 (RenameMigration.OldNames にある ID) が書かれたアプリについて
//   ・app-settings.json のショートカット・常駐の項目を新しい ID に付け替える
//   ・スタートアップ (HKCU\…\Run の TealSuite.<旧ID>) を新しい exe で登録し直す
//  アプリのデータ (%APPDATA%\TealSuite\<旧ID>\) は、アプリ自身が初めて起動したときにコピーする (DataStore.MigrateLegacyData)
using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>お知らせを出したことを覚えておくファイル (%APPDATA%\TealSuite\TealHub\rename-notices.txt。1 行に「前の ID>新しい ID」)</summary>
        public static string NoticesPath { get { return Path.Combine(DataStore.DirFor("TealHub"), "rename-notices.txt"); } }

        /// <summary>
        /// 「〇〇 は △△ になりました」をまだ出していない名前の変更 (「Teal Tasks → Teal Plan」)。返したものは出したことにする。
        /// 引き継ぎをした (done にある) か、前の名前のデータ フォルダーがある (前の名前で使っていた) ときだけ。
        /// ファイルがまだないとき (この仕組みより前の版から) は、suiteVersion より前の版の変更はもう知らせたことにする
        /// </summary>
        public static List<string> PendingNotices(IEnumerable<AppEntry> apps, List<string> done, string suiteVersion)
        {
            var res = new List<string>();
            var path = NoticesPath;
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool fresh = !File.Exists(path);
            try { if (!fresh) foreach (var l in File.ReadAllLines(path)) if (l.Trim().Length > 0) shown.Add(l.Trim()); }
            catch (Exception ex) { DataStore.Log("rename notices: " + ex.Message); }
            bool changed = false;
            foreach (var a in apps)
            {
                if (a == null || a.LegacyIds == null) continue;
                foreach (var o in a.LegacyIds.Where(RenameMigration.OldNames.ContainsKey))
                {
                    var key = o + ">" + a.Id;
                    if (shown.Contains(key)) continue;
                    var text = RenameMigration.OldName(o) + " → " + a.Name;
                    string ver;
                    bool old = fresh && RenameMigration.RenamedIn.TryGetValue(o, out ver) && RenameMigration.CompareVersions(ver, suiteVersion) < 0;
                    bool used = (done != null && done.Contains(text)) || Directory.Exists(DataStore.DirFor(o));
                    if (old || used) { shown.Add(key); changed = true; }
                    if (!old && used && !res.Contains(text)) res.Add(text);
                }
            }
            if (changed || fresh)
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllLines(path, shown.OrderBy(x => x).ToArray()); }
                catch (Exception ex) { DataStore.Log("rename notices: " + ex.Message); }
            }
            return res;
        }

        /// <summary>フッターに出す文</summary>
        public static string Notice(List<string> done)
        {
            if (done == null || done.Count == 0) return null;
            return string.Join("・", done.Select(d => d.Replace(" → ", " を ") + " に")) + "引き継ぎました (ショートカット・常駐・スタートアップ)";
        }
    }
}
