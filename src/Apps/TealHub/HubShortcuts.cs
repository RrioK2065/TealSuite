// Teal Hub (アプリ管理) - 全アプリのショートカットと「常駐」をまとめて扱う (1.6.0)
//
//  - 全体ショートカット (RegisterHotKey) を登録するのは Teal Hub だけ。アプリ 1 つにつき 1 つ + Teal Hub 自身
//    押されたら: 起動していなければ --show 付きで起動、起動中ならそのアプリの Hotkey イベントを鳴らす
//    (Teal Clip は履歴のポップアップ、Teal Volume はミキサー、ほかのアプリは画面を前面へ)
//  - 値は %APPDATA%\TealSuite\TealHub\app-settings.json (AppPrefs.cs)。変えたらアプリに Reload を知らせる
//  - 1.5.0 からの引き継ぎ (app-settings.json に値がないときだけ):
//      Teal Hub 自身 … TealHub\shortcuts.json の "open" (なければ Alt+H)
//      Teal Clip・Teal Volume … それぞれの shortcuts.json (1.4/1.5) → settings.json の "hotkey" (1.3) → 初期設定
//        (1.5 の Teal Hub は "open:TealClip" に "none" を書いていたので、これらには使わない)
//      ほかのアプリ … TealHub\shortcuts.json の "open:<ID>" (なければ apps.json の初期設定、なければなし)
//  どのショートカットも「なし」(解除) にできる。同じ組み合わせを 2 つに割り当てることはできない
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Hub
{
    /// <summary>ショートカット 1 つ (アプリ 1 つぶん)</summary>
    public class ShortcutSlot
    {
        internal HubShortcuts Owner_;
        /// <summary>対象のアプリ (Teal Hub 自身なら null)</summary>
        public AppEntry App;
        public string AppId, AppName;
        /// <summary>押したときの動き (例: 「履歴を呼び出す」「画面を開く」)</summary>
        public string Label;
        public HotkeySpec Default { get; internal set; }
        public HotkeySpec Current { get; internal set; }
        /// <summary>登録できなかった理由 (登録できていれば null)</summary>
        public string Error { get; internal set; }

        public bool IsSelf { get { return App == null; } }
        public bool Active { get { return Owner_.IsActive(this); } }
        /// <summary>「Teal Clip (履歴を呼び出す)」</summary>
        public string Describe() { return AppName + " (" + Label + ")"; }

        public string Apply(HotkeySpec spec) { return Owner_.Apply(this, spec); }
        public override string ToString() { return AppId + "=" + Current; }
    }

    public class HubShortcuts : IDisposable
    {
        public static readonly HotkeySpec DefaultHubKey = HotkeySpec.Of(HotMods.Alt, Keys.H);
        public const string SelfName = "Teal Hub";
        public const string SelfLabel = "画面を開く / 隠す";
        public const string OpenLabel = "画面を開く";

        /// <summary>1.5 まで自分でショートカットを登録していたアプリ (1.5 の保存場所の slot と初期設定)</summary>
        public static readonly Dictionary<string, ShortcutDecl> BuiltIn = new Dictionary<string, ShortcutDecl>(StringComparer.OrdinalIgnoreCase)
        {
            { "TealClip", new ShortcutDecl { Slot = "popup", Label = "履歴を呼び出す", Default = "Ctrl+Shift+V" } },
            { "TealVolume", new ShortcutDecl { Slot = "open", Label = "ミキサーを開く / 隠す", Default = "Alt+V" } },
        };

        readonly Dictionary<ShortcutSlot, GlobalHotkey> live = new Dictionary<ShortcutSlot, GlobalHotkey>();
        List<ShortcutSlot> slots = new List<ShortcutSlot>();
        List<AppEntry> apps = new List<AppEntry>();
        bool suspended;

        /// <summary>Teal Hub 自身のショートカット (開く / 隠す)</summary>
        public Action ToggleHub;
        /// <summary>アプリのショートカット (起動していなければ起動、起動中なら Hotkey イベント)</summary>
        public Action<AppEntry> TriggerApp;
        /// <summary>ショートカット・常駐が変わったとき</summary>
        public event Action Changed;
        /// <summary>ほかのアプリに Reload を知らせる (テストでは差し替える)。届いたら true</summary>
        public Func<string, bool> SignalReload = id => AppHost.Signal(AppInfo.ReloadEventName(id));
        /// <summary>直前の Load で 1.5 の設定から引き継いだなら true (初回だけ)</summary>
        public bool Migrated { get; private set; }

        public IList<ShortcutSlot> Slots { get { return slots; } }
        public ShortcutSlot Self { get { return slots.FirstOrDefault(s => s.IsSelf); } }
        /// <summary>Teal Hub が今登録しているショートカットの数</summary>
        public int ActiveCount { get { return live.Values.Count(g => g.Active); } }
        public bool IsActive(ShortcutSlot s) { GlobalHotkey g; return live.TryGetValue(s, out g) && g.Active; }

        public ShortcutSlot For(string appId)
        {
            return slots.FirstOrDefault(s => string.Equals(s.AppId, appId, StringComparison.OrdinalIgnoreCase));
        }

        public static string LabelFor(AppEntry a)
        {
            var d = Decl(a);
            return d != null && !string.IsNullOrEmpty(d.Label) ? d.Label : OpenLabel;
        }

        static ShortcutDecl Decl(AppEntry a)
        {
            if (a.Shortcuts != null)
            {
                var d = a.Shortcuts.FirstOrDefault(x => x != null);
                if (d != null) return d;
            }
            ShortcutDecl b;
            return BuiltIn.TryGetValue(a.Id, out b) ? b : null;
        }

        /// <summary>apps.json のアプリ (Teal Hub 自身は除く) からショートカットを作り、登録し直す</summary>
        public void Load(IList<AppEntry> appList)
        {
            UnregisterAll();
            apps = appList.ToList();
            var prefs = AppPrefs.Load();
            bool firstRun = !AppPrefs.Exists;
            bool dirty = false, migratedAny = false;
            var list = new List<ShortcutSlot>();

            var selfPref = prefs.GetOrAdd(Program.Id);
            if (selfPref.Hotkey == null)
            {
                var old = ShortcutStore.Get(Program.Id, "open");
                if (old != null) migratedAny = true;
                selfPref.Hotkey = old ?? DefaultHubKey.ToSaved();
                dirty = true;
            }
            list.Add(new ShortcutSlot { Owner_ = this, AppId = Program.Id, AppName = SelfName, Label = SelfLabel, Default = DefaultHubKey, Current = HotkeySpec.FromSaved(selfPref.Hotkey, DefaultHubKey) });

            foreach (var a in apps)
            {
                var d = Decl(a);
                var def = d == null ? HotkeySpec.None : HotkeySpec.FromSaved(d.Default, HotkeySpec.None);
                var p = prefs.GetOrAdd(a.Id);
                if (p.Hotkey == null)
                {
                    var old = MigratedHotkey(a.Id);
                    if (old != null) migratedAny = true;
                    p.Hotkey = old ?? def.ToSaved();
                    dirty = true;
                }
                if (!p.Resident.HasValue)
                {
                    p.Resident = AppPrefs.DefaultResident(a.Id, ResidentDefault(a), a.ExePath);
                    if (p.Resident.Value != ResidentDefault(a)) migratedAny = true;   // 1.5 でスタートアップに登録していた
                    dirty = true;
                }
                list.Add(new ShortcutSlot { Owner_ = this, App = a, AppId = a.Id, AppName = a.Name, Label = LabelFor(a), Default = def, Current = HotkeySpec.FromSaved(p.Hotkey, def) });
            }
            if (dirty)
            {
                var err = AppPrefs.TrySave(prefs);
                if (err != null) DataStore.Log(err);
            }
            Migrated = firstRun && migratedAny;   // 1.5 までの設定が実際に見つかったときだけ「引き継ぎました」
            slots = list;

            foreach (var s in slots)
            {
                var g = new GlobalHotkey();
                var slot = s;
                g.Pressed += () => OnPressed(slot);
                live[s] = g;
                if (s.Current.IsEmpty) continue;
                var dup = slots.TakeWhile(x => x != s).FirstOrDefault(x => x.Current.SameAs(s.Current));
                if (dup != null) { s.Error = s.Current.Display + " は " + dup.Describe() + " と重なっているため使えません。別の組み合わせを選んでください。"; continue; }
                s.Error = HotkeySpec.Validate(s.Current) ?? (suspended ? null : g.Register(s.Current));
                if (s.Error != null) DataStore.Log("hub hotkey " + s + ": " + s.Error);
            }
            if (Changed != null) Changed();
        }

        /// <summary>1.5 までの保存値 (なければ null)</summary>
        public static string MigratedHotkey(string appId)
        {
            ShortcutDecl b;
            if (BuiltIn.TryGetValue(appId, out b)) return ShortcutStore.Get(appId, b.Slot) ?? LegacyHotkey(appId);
            return ShortcutStore.Get(Program.Id, ShortcutStore.OpenSlotFor(appId));
        }

        [DataContract]
        class LegacySettings { [DataMember(Name = "hotkey")] public string Hotkey; }

        /// <summary>1.3 の settings.json の "hotkey"</summary>
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

        void OnPressed(ShortcutSlot s)
        {
            if (s.IsSelf) { if (ToggleHub != null) ToggleHub(); }
            else if (TriggerApp != null && s.App != null) TriggerApp(s.App);
        }

        /// <summary>テスト用: ショートカットが押されたことにする</summary>
        public void SimulatePress(ShortcutSlot s) { GlobalHotkey g; if (live.TryGetValue(s, out g)) g.SimulatePress(); }

        /// <summary>同じ組み合わせのほかのショートカット (なければ null)</summary>
        public ShortcutSlot DuplicateOf(ShortcutSlot s, HotkeySpec spec)
        {
            if (spec.IsEmpty) return null;
            return slots.FirstOrDefault(o => o != s && o.Current.SameAs(spec));
        }

        /// <summary>変更する (None で解除)。失敗したら理由 (日本語)</summary>
        public string Apply(ShortcutSlot s, HotkeySpec spec)
        {
            GlobalHotkey g;
            if (!live.TryGetValue(s, out g)) return "このショートカットは変更できません。";
            if (!spec.IsEmpty)
            {
                var why = HotkeySpec.Validate(spec);
                if (why != null) return why;
                var dup = DuplicateOf(s, spec);
                if (dup != null) return spec.Display + " は " + dup.Describe() + " で使っています。先にそちらを解除するか、別の組み合わせを選んでください。";
            }
            bool wasActive = g.Active;
            if (spec.IsEmpty) g.Unregister();
            else if (!suspended || !spec.SameAs(s.Current))
            {
                var err = g.Register(spec);
                if (err != null)
                {
                    if (wasActive && !suspended) g.Register(s.Current);
                    else g.Unregister();
                    return err;
                }
                if (suspended) g.Unregister();   // キー入力中 (入力が終われば Resume で戻る)
            }
            var prefs = AppPrefs.Load();
            prefs.GetOrAdd(s.AppId).Hotkey = spec.ToSaved();
            var saveErr = AppPrefs.TrySave(prefs);
            if (saveErr != null)
            {
                g.Unregister();
                if (!s.Current.IsEmpty && s.Error == null && !suspended) g.Register(s.Current);
                return saveErr;
            }
            s.Current = spec; s.Error = null;
            if (!s.IsSelf) SignalReload(s.AppId);
            if (Changed != null) Changed();
            return null;
        }

        // ---------- 常駐 ----------

        /// <summary>常駐するか (Teal Hub 自身は常に true)</summary>
        public static bool IsResident(AppEntry a)
        {
            if (a == null || a.Self || string.Equals(a.Id, Program.Id, StringComparison.OrdinalIgnoreCase)) return true;
            return AppPrefs.IsResident(a.Id, AppPrefs.DefaultResident(a.Id, ResidentDefault(a), a.ExePath));
        }

        /// <summary>常駐の初期設定 (apps.json の resident。古い apps.json でも Teal Clip はオン)</summary>
        public static bool ResidentDefault(AppEntry a)
        {
            return a.Resident || string.Equals(a.Id, "TealClip", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>常駐を切り替えて保存し、アプリに知らせる。失敗したら理由</summary>
        public string SetResident(AppEntry a, bool on)
        {
            var prefs = AppPrefs.Load();
            prefs.GetOrAdd(a.Id).Resident = on;
            var err = AppPrefs.TrySave(prefs);
            if (err != null) return err;
            SignalReload(a.Id);
            if (Changed != null) Changed();
            return null;
        }

        /// <summary>キー入力中は Teal Hub のショートカットを全部外す (Alt+H なども入力できるように)</summary>
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
                if (!kv.Key.Current.IsEmpty && kv.Key.Error == null && !kv.Value.Active)
                {
                    var err = kv.Value.Register(kv.Key.Current);
                    if (err != null) kv.Key.Error = err;
                }
            if (Changed != null) Changed();
        }

        public bool Suspended { get { return suspended; } }

        void UnregisterAll()
        {
            foreach (var g in live.Values) { try { g.Dispose(); } catch { } }
            live.Clear();
        }

        public void Dispose() { UnregisterAll(); }
    }
}
