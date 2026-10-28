// Teal Hub (アプリ管理) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。ID を変えるときは旧 ID を LegacyIds に足す (データを自動で引き継ぐ)
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Hub - アプリ管理")]
[assembly: AssemblyDescription("TealSuite のアプリをまとめて起動・終了します")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.10.0.0")]
[assembly: AssemblyFileVersion("1.10.0.0")]

namespace TealSuite.Hub
{
    static class Program
    {
        public const string Id = "TealHub";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Hub",
            Role = "アプリ管理",
            Description = "TealSuite のアプリをまとめて起動・終了します。アプリは apps.json に登録されたものが表示されます。\n通知領域に常駐し、Alt+H (変更・解除できます) で開けます。各アプリの「設定」で、ショートカット・常駐・スタートアップを選べます。",
            LegacyIds = new[] { "AppManager" },   // 1.0 (AppSuite) の ID
        };
        [STAThread]
        static int Main(string[] args)
        {
            // Teal Hub にアップデート ファイルを渡されたとき (exe へのドラッグ・「プログラムから開く」) は TealUpdater に任せる
            foreach (var a in args ?? new string[0])
            {
                if (a != null && a.EndsWith(UpdaterLauncher.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    AppInfo.Init(Spec);
                    return UpdaterLauncher.Open(null, a) ? 0 : 1;
                }
            }
            // すでに起動中に「--settings <アプリ>」で起動されたら、起動中の Teal Hub にそのページを開いてもらう
            AppHost.SecondInstance = () => { var t = HubLink.SettingsTarget(args); if (t != null) HubLink.WriteRequest(t); };
            AppHost.RunContext(Spec, () => new HubTray(args, AppHost.StartedAtLogin));
            return 0;
        }
    }
}
