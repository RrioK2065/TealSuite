// Teal Volume (音量操作) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。ID を変えるときは旧 ID を LegacyIds に足す (データを自動で引き継ぐ)
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Volume - 音量操作")]
[assembly: AssemblyDescription("アプリごとの音量を調整する")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]

namespace TealSuite.Volume
{
    static class Program
    {
        public const string Id = "TealVolume";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Volume",
            Role = "音量操作",
            Description = "Windows の「アプリの音量」と同じように、アプリごとの音量とミュートを調整します。スライダーは 1% ずつ、Shift を押しながらドラッグすると 5% ずつ動きます。ショートカット (初期設定: Alt+V) で開いたり隠したりできます。Teal Hub の設定で「常駐」をオンにすると、閉じても通知領域 (タスクバー右下) に残ります。",
            LegacyIds = new[] { "VolumeMixer" },   // 1.0 (AppSuite) の ID
        };
        [STAThread]
        static void Main(string[] args)
        {
            AppHost.RunContext(Spec, () => new VolumeTray(args, AppHost.StartedAtLogin));
        }
    }
}
