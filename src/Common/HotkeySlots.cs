// TealSuite 共通: 1 つのアプリに 2 つ以上のショートカット (1.12.0。Teal Macro で使う)
//  - 1 つめ (apps.json の shortcuts の先頭) は今までどおり app-settings.json の "hotkey" と Hotkey イベント
//  - 2 つめ以降は "slot" の名前で区別する。値は app-settings.json の "more": [{ "slot": "pick", "hotkey": "Ctrl+Alt+P" }]
//    押されたら Teal Hub が名前付きイベント Local\TealSuite.<ID>.Hotkey.<slot> を鳴らす (アプリが SlotEvents で待つ)。
//    アプリが起動していなければ Teal Hub が "--slot=<slot>" を付けて起動する
//  - 数が変わるショートカット (Teal Macro のマクロごとのキー) は、アプリが自分のデータ フォルダーの
//    apps.json の "slotsFile" (例: hub-slots.json) に { "slots": [{ "slot": "m.1a2b", "label": "マクロ「ログイン」を実行" }] } と書き、
//    Teal Hub に Reload を知らせる。Teal Hub はそれを読んで登録する (キーを決めるのは今までどおり Teal Hub の設定ページだけ)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace TealSuite.Common
{
    [DataContract]
    public class HubSlotDecl
    {
        [DataMember(Name = "slot", Order = 1)] public string Slot;
        [DataMember(Name = "label", Order = 2)] public string Label;
    }

    [DataContract]
    public class HubSlotsFile
    {
        [DataMember(Name = "version", Order = 1)] public int Version = 1;
        [DataMember(Name = "slots", Order = 2)] public List<HubSlotDecl> Slots = new List<HubSlotDecl>();
    }

    public static class HotkeySlots
    {
        /// <summary>Teal Hub が 2 つめ以降のショートカットでアプリを起動するときの引数 ("--slot=pick")</summary>
        public const string StartArgPrefix = "--slot=";
        public const int MaxDynamic = 100;

        /// <summary>slot の名前に使える文字か (英数字・. - _ で 1〜40 文字)</summary>
        public static bool ValidSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot) || slot.Length > 40) return false;
            return slot.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_');
        }

        /// <summary>起動の引数から "--slot=xxx" を読む (なければ null)</summary>
        public static string StartSlot(IEnumerable<string> args)
        {
            if (args == null) return null;
            foreach (var a in args)
            {
                if (a == null || !a.StartsWith(StartArgPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var s = a.Substring(StartArgPrefix.Length).Trim();
                if (ValidSlot(s)) return s;
            }
            return null;
        }

        /// <summary>保存値 (決めていなければ初期設定)</summary>
        public static HotkeySpec HotkeyOf(string appId, string slot, HotkeySpec fallback)
        {
            return HotkeySpec.FromSaved(AppPrefs.HotkeyOf(appId, slot), fallback);
        }

        /// <summary>アプリのデータ フォルダーの slotsFile を読む (なければ・読めなければ空)。Teal Hub が使う</summary>
        public static List<HubSlotDecl> ReadDynamic(string appId, string fileName)
        {
            var list = new List<HubSlotDecl>();
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return list;
            try
            {
                var p = Path.Combine(DataStore.DirFor(appId), fileName);
                if (!File.Exists(p)) return list;
                var f = DataStore.FromJson<HubSlotsFile>(File.ReadAllBytes(p));
                if (f == null || f.Slots == null) return list;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in f.Slots)
                {
                    if (d == null || !ValidSlot(d.Slot) || !seen.Add(d.Slot)) continue;
                    list.Add(new HubSlotDecl { Slot = d.Slot, Label = string.IsNullOrWhiteSpace(d.Label) ? d.Slot : d.Label.Trim() });
                    if (list.Count >= MaxDynamic) break;
                }
            }
            catch (Exception ex) { DataStore.Log("slots " + appId + ": " + ex.Message); }
            return list;
        }

        /// <summary>自分の slotsFile を書く (中身が同じなら書かない)。書いたら true</summary>
        public static bool WriteDynamic(string fileName, IEnumerable<HubSlotDecl> slots)
        {
            var f = new HubSlotsFile { Slots = slots.Where(s => s != null && ValidSlot(s.Slot)).Take(MaxDynamic).ToList() };
            var bytes = DataStore.ToJson(f);
            try
            {
                var p = DataStore.PathOf(fileName);
                if (File.Exists(p) && File.ReadAllBytes(p).SequenceEqual(bytes)) return false;
                DataStore.WriteBytes(fileName, bytes);
                return true;
            }
            catch (Exception ex) { DataStore.Log("write slots: " + ex.Message); return false; }
        }

        /// <summary>Teal Hub に「ショートカットの一覧が変わった」と知らせる (Teal Hub が起動していなければ何もしない)</summary>
        public static Func<bool> NotifyHub = () => AppHost.Signal(AppInfo.ReloadEventName(AppPrefs.OwnerId));
    }

    /// <summary>
    /// 2 つめ以降のショートカットの名前付きイベントを待つ (アプリ側)。Sync で待つ slot を増やしたり減らしたりできる。
    /// 押されたら Pressed(slot) を UI スレッドで呼ぶ (AppHost.OnUi)
    /// </summary>
    public class SlotEvents : IDisposable
    {
        class Entry { public EventWaitHandle Handle; public RegisteredWaitHandle Wait; }
        readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly string appId;
        public Action<string> Pressed;
        /// <summary>UI スレッドへ渡す処理 (テストでは差し替える)</summary>
        public Action<Action> Post = AppHost.OnUi;

        public SlotEvents(string appId) { this.appId = appId; }

        public IList<string> Slots { get { return map.Keys.ToList(); } }

        public void Sync(IEnumerable<string> slots)
        {
            var want = new HashSet<string>(slots.Where(HotkeySlots.ValidSlot), StringComparer.OrdinalIgnoreCase);
            foreach (var k in map.Keys.Where(k => !want.Contains(k)).ToList()) Remove(k);
            foreach (var s in want)
            {
                if (map.ContainsKey(s)) continue;
                var e = new Entry();
                try
                {
                    bool cn;
                    e.Handle = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.SlotEventName(appId, s), out cn);
                    var slot = s;
                    e.Wait = ThreadPool.RegisterWaitForSingleObject(e.Handle, (st, to) => Post(() => { if (Pressed != null) Pressed(slot); }), null, -1, false);
                }
                catch (Exception ex) { DataStore.Log("slot event " + s + ": " + ex.Message); }
                map[s] = e;
            }
        }

        void Remove(string k)
        {
            Entry e;
            if (!map.TryGetValue(k, out e)) return;
            try { if (e.Wait != null) e.Wait.Unregister(null); } catch { }
            try { if (e.Handle != null) e.Handle.Dispose(); } catch { }
            map.Remove(k);
        }

        public void Dispose() { foreach (var k in map.Keys.ToList()) Remove(k); }
    }
}
