// Teal Pass (パスワード管理) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。ID を変えるときは旧 ID を LegacyIds に足す (データを自動で引き継ぐ)
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Pass - パスワード管理")]
[assembly: AssemblyDescription("パスワードの生成・保管・管理")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace TealSuite.Pass
{
    static class Program
    {
        public const string Id = "TealPass";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Pass",
            Role = "パスワード管理",
            Description = "パスワードの生成・保管・管理。サイトごとに ID やメモも残せます。保存データは Windows のログインユーザーに紐づけて暗号化 (DPAPI) されます。",
            LegacyIds = new[] { "PasswordMaker" },   // 1.0 (AppSuite) の ID
        };
        [STAThread]
        static void Main()
        {
            AppHost.Run(Spec, () => new MainForm());
        }
    }
}
