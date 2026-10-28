// Teal Sub (サブスク整理) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Sub - サブスク整理")]
[assembly: AssemblyDescription("契約中のサブスクの金額・更新日・使い方をまとめて見直すアプリ")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.8.0.0")]
[assembly: AssemblyFileVersion("1.8.0.0")]

namespace TealSuite.Sub
{
    static class Program
    {
        public const string Id = "TealSub";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Sub",
            Role = "サブスク整理",
            Description = "契約中のサブスクの月額・年額・次の更新日をまとめて表示し、使っていないものを見直し候補として知らせます。データはこのパソコンの中だけに保存します (インターネットには送りません)。",
            LegacyIds = new string[0],
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
