// Teal Wallet (サブスク＆買い物リスト) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Wallet - サブスク＆買い物リスト")]
[assembly: AssemblyDescription("サブスクの金額・更新日と、買い物リスト・欲しいものをまとめて管理するアプリ")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.11.0.0")]
[assembly: AssemblyFileVersion("1.11.0.0")]

namespace TealSuite.Wallet
{
    static class Program
    {
        public const string Id = "TealWallet";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Wallet",
            Role = "サブスク＆買い物リスト",
            Description = "契約中のサブスクの月額・年額・次の更新日をまとめて表示し、使っていないものを見直し候補として知らせます。買い物リスト (お店ごと・売り場ごと) と欲しいものリストも使えます。データはこのパソコンの中だけに保存します (インターネットには送りません)。",
            // 1.8.0 までの名前は Teal Sub (ID TealSub)。データ %APPDATA%\TealSuite\TealSub\ は初回起動時にコピーして引き継ぐ
            LegacyIds = new[] { "TealSub" },
        };
        /// <summary>Teal Hub で決めていないときの常駐の初期設定 (apps.json の resident と同じ)。
        /// 更新日・無料期間のお知らせは起動している間しか出せないので、最初から常駐にしておく</summary>
        public const bool ResidentByDefault = true;

        [STAThread]
        static void Main()
        {
            AppHost.RunContext(Spec, () =>
            {
                var form = new MainForm();
                var ctx = new ResidentContext(form, ResidentByDefault, AppHost.StartedAtLogin);
                form.AttachResident(ctx);
                return ctx;
            });
        }
    }
}
