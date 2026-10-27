// TealSuite 共通: 1.5.0 までのショートカットの保存場所 (読むだけ。1.6.0 で Teal Hub が app-settings.json に引き継ぐ)
//  %APPDATA%\TealSuite\<AppId>\shortcuts.json
//   - Teal Clip の "popup"・Teal Volume の "open" … 1.5 まではそれぞれのアプリが登録していた
//   - Teal Hub の "open" (Teal Hub 自身) と "open:<AppId>" (Teal Pass・Teal Memo など)
//  keys: "Alt+V" / "none" (なし)。1.6.0 からは書き込まない (1.5 に戻したときはそのまま使われる)
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
    }
}
