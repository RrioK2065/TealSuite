// TealSuite 共通: アプリ情報 (ID / 表示名 / 役割 / バージョン)
// 名前を変えるときは各アプリの AppMeta.cs の AppSpec と apps.json だけを直せばよい
// (ウィンドウタイトル・ヘッダー・フッター・「このアプリについて」・通知領域はここから表示名を取る)
using System;
using System.Reflection;

namespace TealSuite.Common
{
    /// <summary>アプリ 1 つぶんの名前の定義 (AppMeta.cs に置く)</summary>
    public class AppSpec
    {
        /// <summary>apps.json の id と同じ英数字。exe 名・データフォルダ名・イベント名に使う</summary>
        public string Id;
        /// <summary>表示名 (例: "Teal Pass")</summary>
        public string Name;
        /// <summary>日本語の短い役割 (例: "パスワード管理")。タイトルやヘッダーで名前の横に出す</summary>
        public string Role;
        /// <summary>「このアプリについて」の説明</summary>
        public string Description;
        /// <summary>以前の ID (データを %APPDATA%\AppSuite\&lt;旧ID&gt; から自動で引き継ぐ)</summary>
        public string[] LegacyIds = new string[0];
    }

    public static class AppInfo
    {
        /// <summary>apps.json の id と同じ英数字の ID。データフォルダ名やイベント名にも使う。</summary>
        public static string Id { get; private set; }
        /// <summary>画面に出す名前 (例: "Teal Pass")</summary>
        public static string DisplayName { get; private set; }
        /// <summary>日本語の短い役割 (例: "パスワード管理")</summary>
        public static string Role { get; private set; }
        public static string Description { get; private set; }
        public static string[] LegacyIds { get; private set; }

        /// <summary>ウィンドウタイトル用 "Teal Pass - パスワード管理"</summary>
        public static string WindowTitle
        {
            get { return string.IsNullOrEmpty(Role) ? DisplayName : DisplayName + " - " + Role; }
        }
        /// <summary>サブ画面のタイトル "Teal Clip - 設定"</summary>
        public static string TitleFor(string screen) { return DisplayName + " - " + screen; }
        /// <summary>ヘッダーの 2 行目 "パスワード管理 ・ 生成・保管・メモ" (detail が空なら役割だけ)</summary>
        public static string HeaderSubtitle(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return Role ?? "";
            return string.IsNullOrEmpty(Role) ? detail : Role + "  ・  " + detail;
        }

        public static void Init(AppSpec spec)
        {
            Id = spec.Id; DisplayName = spec.Name; Role = spec.Role; Description = spec.Description;
            LegacyIds = spec.LegacyIds ?? new string[0];
        }
        public static void Init(string id, string displayName, string description)
        {
            Init(new AppSpec { Id = id, Name = displayName, Description = description });
        }

        /// <summary>AssemblyVersion から "1.1.0" 形式で返す。</summary>
        public static string Version
        {
            get
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var v = asm.GetName().Version;
                return v == null ? "0.0.0" : v.Major + "." + v.Minor + "." + Math.Max(0, v.Build);
            }
        }

        public const string SuiteName = "TealSuite";
        /// <summary>1.0 までのスイート名 (データの引き継ぎ元フォルダ %APPDATA%\AppSuite)</summary>
        public const string LegacySuiteName = "AppSuite";
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        /// <summary>他プロセスと通信するための名前付きオブジェクト名 (Teal Hub と各アプリで共通の規約)</summary>
        public static string ExitEventName(string appId) { return @"Local\TealSuite." + appId + ".Exit"; }
        public static string ShowEventName(string appId) { return @"Local\TealSuite." + appId + ".Show"; }
        /// <summary>ショートカットなどの設定を読み直してほしいとき (Teal Hub がほかのアプリの設定を変えたあと)</summary>
        public static string ReloadEventName(string appId) { return @"Local\TealSuite." + appId + ".Reload"; }
        /// <summary>Teal Hub で登録したショートカットが押されたとき (1.6.0 から。Teal Clip は履歴、Teal Volume は開閉、ほかは前面へ)</summary>
        public static string HotkeyEventName(string appId) { return @"Local\TealSuite." + appId + ".Hotkey"; }
        public static string MutexName(string appId) { return @"Local\TealSuite." + appId + ".Instance"; }
    }
}
