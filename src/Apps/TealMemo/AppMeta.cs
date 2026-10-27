// Teal Memo (メモ) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。ID を変えるときは旧 ID を LegacyIds に足す (データを自動で引き継ぐ)
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Memo - メモ")]
[assembly: AssemblyDescription("思いついたことをすぐに書き留めるメモ帳")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace TealSuite.Memo
{
    static class Program
    {
        public const string Id = "TealMemo";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Memo",
            Role = "メモ",
            Description = "思いついたことをすぐに書き留めるメモ帳。入力は自動で保存されます。",
            LegacyIds = new[] { "Memo" },   // 1.0 (AppSuite) の ID
        };
        [STAThread]
        static void Main()
        {
            AppHost.Run(Spec, () => new MainForm());
        }
    }
}
