// Teal Macro (連打＆マクロ) - アプリ情報とエントリポイント
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Macro - 連打＆マクロ")]
[assembly: AssemblyDescription("クリック・キーの連打と、操作の記録・再生")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.12.0.0")]
[assembly: AssemblyFileVersion("1.12.0.0")]

namespace TealSuite.Macro
{
    static class Program
    {
        public const string Id = "TealMacro";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Macro",
            Role = "連打＆マクロ",
            Description = "クリックやキーを自動で連打します (左・右・中クリック、ダブルクリック、好きなキー)。速さ・回数・時間と、クリックする場所 (マウスのある場所・決めた位置・ウィンドウの中の位置) を選べます。"
                + "マウスとキーボードの操作を記録してそのまま再生したり、「クリック → 待つ → 文字を入力」のような手順を組み立てたりもできます。"
                + "ショートカットは Teal Hub で (初期設定: 連打 Ctrl+Alt+R、位置を記録 Ctrl+Alt+P、マクロの記録 Ctrl+Alt+M、すべて停止 Ctrl+Alt+Esc)。実行中は Esc でも止まります。",
            LegacyIds = new string[0],
        };
        [STAThread]
        static void Main(string[] args)
        {
            AppHost.RunContext(Spec, () => new MacroTray(args, AppHost.StartedAtLogin));
        }
    }
}
