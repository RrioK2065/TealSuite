// Teal Clip (コピー履歴) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。ID を変えるときは旧 ID を LegacyIds に足す (データを自動で引き継ぐ)
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Clip - コピー履歴")]
[assembly: AssemblyDescription("コピーした文字を10件まで記録して呼び出す")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.9.0.0")]
[assembly: AssemblyFileVersion("1.9.0.0")]

namespace TealSuite.Clip
{
    static class Program
    {
        public const string Id = "TealClip";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Clip",
            Role = "コピー履歴",
            Description = "コピーした文字を 10 件まで記録します。ショートカット (初期設定: Ctrl+Shift+V) で一覧を出し、数字キーやクリックで貼り付けできます。通知領域 (タスクバー右下) に常駐します (Teal Hub の設定でオフにもできます)。",
            LegacyIds = new[] { "ClipboardHistory" },   // 1.0 (AppSuite) の ID
        };
        [STAThread]
        static void Main(string[] args)
        {
            AppHost.RunContext(Spec, () => new TrayContext(args));
        }
    }
}
