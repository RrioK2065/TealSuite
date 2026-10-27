// Teal Hub (アプリ管理) - 全アプリのショートカットをまとめて扱う
//
// だれが登録するか (二重登録しない):
//   - アプリ自身が持つもの … apps.json の "shortcuts" (Teal Clip の「履歴を呼び出す」、Teal Volume の「画面を開く」)。
//     値は %APPDATA%\TealSuite\<アプリ>\shortcuts.json。Teal Hub は書き換えて Reload イベントで知らせるだけで、登録はしない
//   - Teal Hub が持つもの … Teal Hub 自身を開く (初期設定 Alt+H) と、自分で「開く」を持たないアプリを開く (初期設定なし)。
//     値は %APPDATA%\TealSuite\TealHub\shortcuts.json ("open" と "open:<アプリ ID>")。押すと、起動していなければ起動し、起動中なら前面に出す
// どのショートカットも「なし」(解除) にできる。同じ組み合わせを 2 つに割り当てることはできない
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>ショートカット 1 つ (一覧の 1 行)</summary>
    public class ShortcutSlot : IHotkeyTarget
    {
        internal HubShortcuts Owner_;
        /// <summary>対象のアプリ (Teal Hub 自身なら null)</summary>
        public AppEntry App;
        public string AppId, AppName;
        /// <summary>値を持つアプリ (shortcuts.json の場所)。Teal Hub が持つものは "TealHub"</summary>
        public string Owner;
        /// <summary>shortcuts.json の slot 名</summary>
        public string Slot;
        /// <summary>表示名 (例: 「履歴を呼び出す」「画面を開く」)</summary>
        public string Label;
        public HotkeySpec Default { get; internal set; }
        public HotkeySpec Current { get; internal set; }
        /// <summary>Teal Hub が登録できなかった理由 (Teal Hub が持つものだけ)</summary>
        public string Error { get; internal set; }

        public bool HubOwned { get { return string.Equals(Owner, Program.Id, StringComparison.OrdinalIgnoreCase); } }
        public bool IsSelf { get { return App == null; } }
        /// <summary>ほかのアプリが重複を確認するときに表示する名前</summary>
        public string StoreLabel { get { return HubOwned && !IsSelf ? AppName + " を開く" : Label; } }
        /// <summary>「Teal Clip の「履歴を呼び出す」」</summary>
        public string Describe() { return AppName + " の「" + Label + "」"; }

        public string Apply(HotkeySpec spec) { return Owner_.Apply(this, spec); }
        public void Suspend() { Owner_.SuspendAll(); }
        public void Resume() { Owner_.ResumeAll(); }
        public override string ToString() { return Owner + "/" + Slot + "=" + Current; }
    }

    public class HubShortcuts : IDisposable
    {
        public static readonly HotkeySpec DefaultHubKey = HotkeySpec.Of(HotMods.Alt, Keys.H);
        public const string SelfSlot = "open";
        public const string SelfName = "Teal Hub";
        public const string SelfLabel = "画面を開く / 隠す";
        public const string OpenLabel = "画面を開く";

        /// <summary>apps.json に "shortcuts" がない (1.3 までの apps.json を使い続けている) ときの既定</summary>
        public static readonly Dictionary<string, ShortcutDecl[]> BuiltIn = new Dictionary<string, ShortcutDecl[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "TealClip", new[] { new ShortcutDecl { Slot = "popup", Label = "履歴を呼び出す", Default = "Ctrl+Shift+V" } } },
            { "TealVolume", new[] { new ShortcutDecl { Slot = "open", Label = "画面を開く / 隠す", Default = "Alt+V" } } },
        };

        readonly Dictionary<ShortcutSlot, GlobalHotkey> live = new Dictionary<ShortcutSlot, GlobalHotkey>();
        List<ShortcutSlot> slots = new List<ShortcutSlot>();
        bool suspended;

        /// <summary>Teal Hub 自身のショートカット (開く / 隠す)</summary>
        public Action ToggleHub;
        /// <summary>アプリを開く (起動していなければ起動、起動中なら前面へ)</summary>
        public Action<AppEntry> OpenApp;
        /// <summary>だれかのショートカットが変わったとき</summary>
        public event Action Changed;
        /// <summary>ほかのアプリに Reload を知らせる (テストでは差し替える)。届いたら true</summary>
        public Func<string, bool> SignalReload = id => AppHost.Signal(AppInfo.ReloadEventName(id));

        public IList<ShortcutSlot> Slots { get { return slots; } }
        public ShortcutSlot Self { get { return slots.FirstOrDefault(s => s.IsSelf); } }
        /// <summary>Teal Hub が今登録しているショートカットの数</summary>
        public int ActiveCount { get { return live.Values.Count(g => g.Active); } }
        public bool IsActive(ShortcutSlot s) { GlobalHotkey g; return live.TryGetValue(s, out g) && g.Active; }

        public IEnumerable<ShortcutSlot> For(string appId)
        {
            return slots.Where(s => string.Equals(s.AppId, appId, StringComparison.OrdinalIgnoreCase));
        }

        public ShortcutSlot Find(string appId, string slot)
        {
            return slots.FirstOrDefault(s => string.Equals(s.AppId, appId, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Slot, slot, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>apps.json のアプリ (Teal Hub 自身は除く) からショートカットの一覧を作り、Teal Hub が持つものを登録し直す</summary>
        public void Load(IList<AppEntry> apps)
        {
            UnregisterAll();
            var list = new List<ShortcutSlot>();
            list.Add(Make(null, Program.Id, SelfName, Program.Id, SelfSlot, SelfLabel, DefaultHubKey, null));
            foreach (var a in apps)
            {
                var decls = Decls(a);
                bool first = true;
                foreach (var d in decls)
                {
                    HotkeySpec def = HotkeySpec.FromSaved(d.Default, HotkeySpec.None);
                    list.Add(Make(a, a.Id, a.Name, a.Id, d.Slot, string.IsNullOrEmpty(d.Label) ? d.Slot : d.Label, def, first ? LegacyHotkey(a.Id) : null));
                    first = false;
                }
                // 自分で「開く」を持たないアプリは、Teal Hub が開くショートカットを用意する (初期設定はなし)
                if (!decls.Any(d => string.Equals(d.Slot, "open", StringComparison.OrdinalIgnoreCase)))
                    list.Add(Make(a, a.Id, a.Name, Program.Id, ShortcutStore.OpenSlotFor(a.Id), OpenLabel, HotkeySpec.None, null));
            }
            slots = list;
            foreach (var s in slots.Where(x => x.HubOwned))
            {
                // ほかのアプリが重複を確認できるように、今の値を書いておく
                if (ShortcutStore.Get(Program.Id, s.Slot) == null) ShortcutStore.TrySet(Program.Id, SelfName, s.Slot, s.StoreLabel, s.Current);
                var g = new GlobalHotkey();
                var slot = s;
                g.Pressed += () => OnPressed(slot);
                live[s] = g;
                if (!s.Current.IsEmpty)
                {
                    s.Error = g.Register(s.Current);
                    if (s.Error != null) DataStore.Log("hub hotkey " + s + ": " + s.Error);
                }
            }
            if (Changed != null) Changed();
        }

        static IList<ShortcutDecl> Decls(AppEntry a)
        {
            if (a.Shortcuts != null) return a.Shortcuts.Where(d => d != null && !string.IsNullOrEmpty(d.Slot)).ToList();
            ShortcutDecl[] b;
            return BuiltIn.TryGetValue(a.Id, out b) ? b : new ShortcutDecl[0];
        }

        ShortcutSlot Make(AppEntry app, string appId, string appName, string owner, string slot, string label, HotkeySpec def, string legacy)
        {
            var saved = ShortcutStore.Get(owner, slot) ?? legacy;
            return new ShortcutSlot
            {
                Owner_ = this, App = app, AppId = appId, AppName = appName, Owner = owner, Slot = slot, Label = label,
                Default = def, Current = HotkeySpec.FromSaved(saved, def),
            };
        }

        [DataContract]
        class LegacySettings { [DataMember(Name = "hotkey")] public string Hotkey; }

        /// <summary>1.3 の settings.json の "hotkey" (アプリがまだ 1.4 で起動していないとき用)</summary>
        static string LegacyHotkey(string appId)
        {
            try
            {
                var p = Path.Combine(DataStore.DirFor(appId), "settings.json");
                if (!File.Exists(p)) return null;
                var s = DataStore.FromJson<LegacySettings>(File.ReadAllBytes(p));
                return s == null ? null : s.Hotkey;
            }
            catch { return null; }
        }

        /// <summary>ほかのアプリが自分で変えたかもしれないので、アプリが持つショートカットを読み直す。変わったら true</summary>
        public bool RefreshFromDisk()
        {
            bool changed = false;
            foreach (var s in slots.Where(x => !x.HubOwned))
            {
                var saved = ShortcutStore.Get(s.Owner, s.Slot);
                if (saved == null) continue;
                var hk = HotkeySpec.FromSaved(saved, s.Default);
                if (!hk.SameAs(s.Current)) { s.Current = hk; changed = true; }
            }
            if (changed && Changed != null) Changed();
            return changed;
        }

        void OnPressed(ShortcutSlot s)
        {
            if (s.IsSelf) { if (ToggleHub != null) ToggleHub(); }
            else if (OpenApp != null && s.App != null) OpenApp(s.App);
        }

        /// <summary>テスト用: ショートカットが押されたことにする</summary>
        public void SimulatePress(ShortcutSlot s) { GlobalHotkey g; if (live.TryGetValue(s, out g)) g.SimulatePress(); }

        /// <summary>同じ組み合わせのほかのショートカット (なければ null)</summary>
        public ShortcutSlot DuplicateOf(ShortcutSlot s, HotkeySpec spec)
        {
            if (spec.IsEmpty) return null;
            return slots.FirstOrDefault(o => o != s && o.Current.SameAs(spec));
        }
        public ShortcutSlot DuplicateOf(ShortcutSlot s) { return DuplicateOf(s, s.Current); }

        /// <summary>重なっている組み合わせの数 (一覧の警告用)</summary>
        public int DuplicateCount { get { return slots.Where(s => !s.Current.IsEmpty).GroupBy(s => s.Current.ToString()).Count(g => g.Count() > 1); } }

        /// <summary>変更する (None で解除)。失敗したら理由</summary>
        public string Apply(ShortcutSlot s, HotkeySpec spec)
        {
            if (!spec.IsEmpty)
            {
                var why = HotkeySpec.Validate(spec);
                if (why != null) return why;
                var dup = DuplicateOf(s, spec);
                if (dup != null) return spec.Display + " は " + dup.Describe() + " で使っています。先にそちらを解除するか、別の組み合わせを選んでください。";
                // apps.json に載っていないアプリ (古いものなど) の保存値とも比べる
                var other = ShortcutStore.FindDuplicate(ShortcutStore.AllInUse().Where(u => !slots.Any(x => x.Owner.Equals(u.App, StringComparison.OrdinalIgnoreCase) && x.Slot.Equals(u.Slot, StringComparison.OrdinalIgnoreCase))), spec, s.Owner, s.Slot);
                if (other != null) return ShortcutStore.DuplicateMessage(other, spec);
            }
            if (s.HubOwned)
            {
                GlobalHotkey g;
                if (!live.TryGetValue(s, out g)) return "このショートカットは変更できません。";
                if (spec.IsEmpty) g.Unregister();
                else
                {
                    var err = g.Register(spec);
                    if (err != null)
                    {
                        if (s.Error == null && !s.Current.IsEmpty && !suspended) g.Register(s.Current);
                        return err;
                    }
                    if (suspended) g.Unregister();   // 設定画面でキー入力中 (閉じたら Resume で戻る)
                }
                try { ShortcutStore.Set(Program.Id, SelfName, s.Slot, s.StoreLabel, spec); }
                catch (Exception ex) { return "保存できませんでした: " + ex.Message; }
            }
            else
            {
                // 登録するのはアプリ自身。ほかのアプリが使っていないかだけ、ここで試しておく
                if (!spec.IsEmpty && !spec.SameAs(s.Current))
                {
                    using (var test = new GlobalHotkey())
                    {
                        var err = test.Register(spec);
                        test.Unregister();
                        if (err != null) return err;
                    }
                }
                try { ShortcutStore.Set(s.Owner, s.AppName, s.Slot, s.Label, spec); }
                catch (Exception ex) { return "保存できませんでした: " + ex.Message; }
                SignalReload(s.Owner);
            }
            s.Current = spec; s.Error = null;
            if (Changed != null) Changed();
            return null;
        }

        /// <summary>設定画面でキーを取り込む間は Teal Hub のショートカットを全部外す (Alt+H も入力できるように)</summary>
        public void SuspendAll()
        {
            suspended = true;
            foreach (var g in live.Values) g.Unregister();
        }

        public void ResumeAll()
        {
            if (!suspended) return;
            suspended = false;
            foreach (var kv in live)
                if (!kv.Key.Current.IsEmpty && kv.Key.Error == null && !kv.Value.Active) kv.Value.Register(kv.Key.Current);
        }

        void UnregisterAll()
        {
            foreach (var g in live.Values) { try { g.Dispose(); } catch { } }
            live.Clear();
        }

        public void Dispose() { UnregisterAll(); }
    }
}
