// Teal Pin (最前面に固定) - アプリ情報とエントリポイント
// バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える (build.sh が一致を確認)
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Pin - 最前面に固定")]
[assembly: AssemblyDescription("ウィンドウを最前面に固定する")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.11.0.0")]
[assembly: AssemblyFileVersion("1.11.0.0")]

namespace TealSuite.Pin
{
    static class Program
    {
        public const string Id = "TealPin";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Pin",
            Role = "最前面に固定",
            Description = "好きなウィンドウを、ほかのウィンドウより前 (最前面) に固定します。固定したウィンドウはティールの枠線で囲みます。"
                + "前面のウィンドウでショートカット (初期設定: Ctrl+Alt+T) を押すか、タイトルバーを右クリックして「📌 最前面に固定」を選びます。"
                + "「右下の小窓」などの位置とサイズのプリセットや、決まったアプリを自動で固定するルールも使えます。終了すると固定はすべて外れます。",
            LegacyIds = new string[0],
        };
        [STAThread]
        static void Main(string[] args)
        {
            AppHost.RunContext(Spec, () => new PinTray(args, AppHost.StartedAtLogin));
        }
    }
}
