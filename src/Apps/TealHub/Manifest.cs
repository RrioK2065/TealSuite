// Teal Hub (アプリ管理) - apps.json (アプリの登録簿) の読み込み
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using TealSuite.Common;

namespace TealSuite.Hub
{
    [DataContract]
    public class AppEntry
    {
        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        [DataMember(Name = "exe", Order = 3)] public string Exe;
        [DataMember(Name = "version", Order = 4)] public string Version;
        [DataMember(Name = "description", Order = 5)] public string Description;
        [DataMember(Name = "icon", Order = 6)] public string Icon;
        /// <summary>true なら Teal Hub 自身 (一覧には出さない)</summary>
        [DataMember(Name = "self", Order = 7, EmitDefaultValue = false)] public bool Self;
        /// <summary>日本語の短い役割 (例: "パスワード管理")。名前の横に表示する。省略可</summary>
        [DataMember(Name = "role", Order = 8, EmitDefaultValue = false)] public string Role;
        /// <summary>以前の ID (例: 1.0 の "ClipboardHistory")。古い exe の自動起動を見つけて置き換えるのに使う。省略可</summary>
        [DataMember(Name = "legacyIds", Order = 9, EmitDefaultValue = false)] public string[] LegacyIds;
        /// <summary>アプリ自身が登録する全体ショートカット (Teal Clip の「履歴を呼び出す」など)。省略可</summary>
        [DataMember(Name = "shortcuts", Order = 10, EmitDefaultValue = false)] public List<ShortcutDecl> Shortcuts;
        /// <summary>通知領域に常駐するアプリなら true (アップデート後は --startup で静かに起動し直す)</summary>
        [DataMember(Name = "resident", Order = 11, EmitDefaultValue = false)] public bool Resident;

        /// <summary>apps.json の場所を基準にした exe の絶対パス</summary>
        [IgnoreDataMember] public string ExePath;
        [IgnoreDataMember] public string IconPath;
    }

    /// <summary>apps.json の "shortcuts" の 1 項目: { "slot": "popup", "label": "履歴を呼び出す", "default": "Ctrl+Shift+V" }</summary>
    [DataContract]
    public class ShortcutDecl
    {
        [DataMember(Name = "slot", Order = 1)] public string Slot;
        [DataMember(Name = "label", Order = 2)] public string Label;
        /// <summary>初期設定 ("Alt+V" など。"none" や省略ならなし)</summary>
        [DataMember(Name = "default", Order = 3, EmitDefaultValue = false)] public string Default;
    }

    [DataContract]
    public class ManifestFile
    {
        [DataMember(Name = "manifestVersion", Order = 1)] public int ManifestVersion;
        [DataMember(Name = "suite", Order = 2)] public string Suite;
        /// <summary>TealSuite 全体のバージョン (アップデートの判定に使う)。1.3 までの apps.json にはない</summary>
        [DataMember(Name = "suiteVersion", Order = 3, EmitDefaultValue = false)] public string SuiteVersion;
        [DataMember(Name = "apps", Order = 4)] public List<AppEntry> Apps;
    }

    public static class Manifest
    {
        public const string FileName = "apps.json";

        /// <summary>既定は TealHub.exe と同じフォルダの apps.json (環境変数 TEALSUITE_MANIFEST で上書き可)</summary>
        public static string DefaultPath
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("TEALSUITE_MANIFEST");
                if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            }
        }

        /// <summary>apps.json の suiteVersion (なければ Teal Hub 自身の version、それもなければ exe のバージョン)</summary>
        public static string SuiteVersion(string path)
        {
            try
            {
                var file = DataStore.FromJson<ManifestFile>(File.ReadAllBytes(path));
                if (file != null && !string.IsNullOrEmpty(file.SuiteVersion)) return file.SuiteVersion;
                if (file != null && file.Apps != null)
                    foreach (var a in file.Apps)
                        if (a != null && (a.Self || Program.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(a.Version)) return a.Version;
            }
            catch { }
            return AppInfo.Version;
        }

        /// <summary>読み込んで、表示するアプリ (自分以外・id と exe があるもの) を返す。失敗時は例外。</summary>
        public static List<AppEntry> Load(string path) { return Load(path, false); }

        /// <param name="includeSelf">true なら Teal Hub 自身も含める (テスト用)</param>
        public static List<AppEntry> Load(string path, bool includeSelf)
        {
            var file = DataStore.FromJson<ManifestFile>(File.ReadAllBytes(path));
            if (file == null || file.Apps == null) throw new InvalidDataException("apps.json に \"apps\" がありません");
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path));
            var list = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in file.Apps)
            {
                if (a == null || string.IsNullOrEmpty(a.Id) || string.IsNullOrEmpty(a.Exe)) continue;
                if (!includeSelf && (a.Self || a.Id.Equals(Program.Id, StringComparison.OrdinalIgnoreCase))) continue;
                if (!seen.Add(a.Id)) continue;
                a.ExePath = Resolve(baseDir, a.Exe);
                a.IconPath = string.IsNullOrEmpty(a.Icon) ? null : Resolve(baseDir, a.Icon);
                if (string.IsNullOrEmpty(a.Name)) a.Name = a.Id;
                list.Add(a);
            }
            return list;
        }

        static string Resolve(string baseDir, string rel)
        {
            rel = rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir, rel));
        }
    }
}
