// Teal Tasks (タスク管理＆カレンダー) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Tasks - タスク管理＆カレンダー")]
[assembly: AssemblyDescription("やることを期限・優先度で並べ、カレンダーで予定を動かせるアプリ")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.9.0.0")]
[assembly: AssemblyFileVersion("1.9.0.0")]

namespace TealSuite.Tasks
{
    static class Program
    {
        public const string Id = "TealTasks";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Tasks",
            Role = "タスク管理＆カレンダー",
            Description = "やることを「明日15時 歯医者」のように日本語で書くだけで登録できます。今日やることをおすすめ順に並べ、月・週・日のカレンダーでドラッグして予定を動かせます。データはこのパソコンの中だけに保存します。",
            LegacyIds = new string[0],
        };
        /// <summary>Teal Hub で決めていないときの常駐の初期設定 (apps.json の resident と同じ)。
        /// 期限のお知らせは起動している間しか出せないので、最初から常駐にしておく</summary>
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
