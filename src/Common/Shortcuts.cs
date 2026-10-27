// TealSuite 共通: ショートカットの保存場所と、アプリをまたいだ重複の確認
//  保存先: %APPDATA%\TealSuite\<AppId>\shortcuts.json  (そのショートカットを「登録する」アプリのフォルダ)
//   - Teal Clip の「履歴を呼び出す」・Teal Volume の「開く / 隠す」… それぞれのアプリが登録する (Teal Hub なしでも動く)
//   - Teal Hub 自身・Teal Pass・Teal Memo などを「開く」… Teal Hub が登録する (TealHub\shortcuts.json の "open:<AppId>")
//  Teal Hub がほかのアプリの値を書き換えたときは、そのアプリの Reload イベントを鳴らす (アプリは読み直して登録し直す)
//  keys: "Alt+V" / "none" (なし)。項目がない = まだ決めていない (初期設定を使う)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace TealSuite.Common
{
    [DataContract]
    public class ShortcutItem
    {
        /// <summary>アプリの中での名前 (例: "popup"、"open"、"open:TealPass")</summary>
        [DataMember(Name = "slot", Order = 1)] public string Slot;
        /// <summary>画面に出す説明 (例: "履歴を呼び出す")。重複の案内に使う</summary>
        [DataMember(Name = "label", Order = 2)] public string Label;
        /// <summary>"Ctrl+Shift+V" / "none"</summary>
        [DataMember(Name = "keys", Order = 3)] public string Keys;
    }

    [DataContract]
    public class ShortcutFile
    {
        [DataMember(Name = "app", Order = 1)] public string App;
        [DataMember(Name = "appName", Order = 2)] public string AppName;
        [DataMember(Name = "items", Order = 3)] public List<ShortcutItem> Items = new List<ShortcutItem>();
    }

    /// <summary>登録済みのショートカット 1 つ (重複確認用)</summary>
    public class ShortcutUse
    {
        public string App, AppName, Slot, Label;
        public HotkeySpec Spec;
        /// <summary>「Teal Volume の『画面を開く / 隠す』」</summary>
        public string Describe() { return (AppName ?? App) + " の「" + (Label ?? Slot) + "」"; }
    }

    public static class ShortcutStore
    {
        public const string FileName = "shortcuts.json";
        /// <summary>Teal Hub が持つ「アプリを開く」ショートカットの slot 名</summary>
        public static string OpenSlotFor(string appId) { return "open:" + appId; }

        public static string PathFor(string appId) { return Path.Combine(DataStore.DirFor(appId), FileName); }

        /// <summary>読めなければ null</summary>
        public static ShortcutFile Load(string appId)
        {
            try
            {
                var p = PathFor(appId);
                if (!File.Exists(p)) return null;
                var f = DataStore.FromJson<ShortcutFile>(File.ReadAllBytes(p));
                if (f != null && f.Items == null) f.Items = new List<ShortcutItem>();
                return f;
            }
            catch (Exception ex) { DataStore.Log("shortcuts " + appId + ": " + ex.Message); return null; }
        }

        /// <summary>保存値 ("Alt+V" / "none")。まだ保存していなければ null</summary>
        public static string Get(string appId, string slot)
        {
            var f = Load(appId);
            if (f == null) return null;
            var it = f.Items.FirstOrDefault(i => i != null && string.Equals(i.Slot, slot, StringComparison.OrdinalIgnoreCase));
            return it == null ? null : it.Keys;
        }

        /// <summary>1 項目だけ書き換えて保存する (ほかの項目はそのまま)。失敗したら例外</summary>
        public static void Set(string appId, string appName, string slot, string label, HotkeySpec spec)
        {
            var f = Load(appId) ?? new ShortcutFile();
            f.App = appId;
            if (!string.IsNullOrEmpty(appName)) f.AppName = appName;
            f.Items.RemoveAll(i => i == null);
            var it = f.Items.FirstOrDefault(i => string.Equals(i.Slot, slot, StringComparison.OrdinalIgnoreCase));
            if (it == null) { it = new ShortcutItem { Slot = slot }; f.Items.Add(it); }
            if (!string.IsNullOrEmpty(label)) it.Label = label;
            it.Keys = spec.ToSaved();
            DataStore.WriteFileAtomic(PathFor(appId), DataStore.ToJson(f));
        }

        public static bool TrySet(string appId, string appName, string slot, string label, HotkeySpec spec)
        {
            try { Set(appId, appName, slot, label, spec); return true; }
            catch (Exception ex) { DataStore.Log("shortcuts save " + appId + "/" + slot + ": " + ex.Message); return false; }
        }

        /// <summary>保存されているショートカット (なし以外) をすべて集める</summary>
        public static List<ShortcutUse> AllInUse()
        {
            var list = new List<ShortcutUse>();
            string[] dirs;
            try { dirs = Directory.Exists(DataStore.Root) ? Directory.GetDirectories(DataStore.Root) : new string[0]; }
            catch { dirs = new string[0]; }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(d);
                var f = Load(id);
                if (f == null) continue;
                foreach (var i in f.Items)
                {
                    if (i == null || string.IsNullOrEmpty(i.Slot)) continue;
                    HotkeySpec hk;
                    if (!HotkeySpec.TryParse(i.Keys, out hk)) continue;
                    list.Add(new ShortcutUse { App = f.App ?? id, AppName = f.AppName ?? id, Slot = i.Slot, Label = i.Label, Spec = hk });
                }
            }
            return list;
        }

        /// <summary>同じ組み合わせを使っているほかのショートカット (自分自身は除く)。なければ null</summary>
        public static ShortcutUse FindDuplicate(IEnumerable<ShortcutUse> uses, HotkeySpec spec, string appId, string slot)
        {
            if (spec.IsEmpty) return null;
            return uses.FirstOrDefault(u => u.Spec.SameAs(spec)
                && !(string.Equals(u.App, appId, StringComparison.OrdinalIgnoreCase) && string.Equals(u.Slot, slot, StringComparison.OrdinalIgnoreCase)));
        }

        public static string DuplicateMessage(ShortcutUse other, HotkeySpec spec)
        {
            return spec.Display + " は " + other.Describe() + " で使っています。先にそちらを解除するか、別の組み合わせを選んでください。";
        }

        /// <summary>保存済みのショートカットと重なれば理由 (HotkeyController.DuplicateCheck 用)</summary>
        public static string CheckDuplicate(HotkeySpec spec, string appId, string slot)
        {
            var d = FindDuplicate(AllInUse(), spec, appId, slot);
            return d == null ? null : DuplicateMessage(d, spec);
        }

        /// <summary>重なっている組み合わせをまとめる (一覧画面の警告用)。キー = ToString()</summary>
        public static Dictionary<string, List<ShortcutUse>> Duplicates(IEnumerable<ShortcutUse> uses)
        {
            return uses.Where(u => !u.Spec.IsEmpty).GroupBy(u => u.Spec.ToString(), StringComparer.OrdinalIgnoreCase)
                       .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// アプリ自身が登録するショートカットを用意する (Teal Clip・Teal Volume の起動時)。
        /// shortcuts.json に値があればそれ、なければ 1.3 までの settings.json の値 (legacy)、それもなければ初期設定。
        /// まだ shortcuts.json に書いていなければ、今の値を書いておく (ほかのアプリが重複を確認できるように)。
        /// </summary>
        public static HotkeyController CreateOwned(string slot, string label, HotkeySpec defaultSpec, string legacySaved)
        {
            var saved = Get(AppInfo.Id, slot);
            bool missing = saved == null;
            if (missing) saved = legacySaved;
            var c = new HotkeyController(defaultSpec, saved);
            if (missing) TrySet(AppInfo.Id, AppInfo.DisplayName, slot, label, c.Current);
            c.DuplicateCheck = spec => CheckDuplicate(spec, AppInfo.Id, slot);
            c.Changed += () => TrySet(AppInfo.Id, AppInfo.DisplayName, slot, label, c.Current);
            return c;
        }
    }
}
