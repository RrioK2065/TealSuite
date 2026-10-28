// Teal Plan (タスク＆予定) - アプリ情報とエントリポイント
// 名前を変えるときは Spec と apps.json を直す。バージョンを上げるときは AssemblyVersion と apps.json の version を両方変える
using System;
using System.Reflection;
using TealSuite.Common;

[assembly: AssemblyTitle("Teal Plan - タスク＆予定")]
[assembly: AssemblyDescription("やること (タスク) と予定をひとつのカレンダーで管理するアプリ")]
[assembly: AssemblyProduct("TealSuite")]
[assembly: AssemblyVersion("1.10.0.0")]
[assembly: AssemblyFileVersion("1.10.0.0")]

namespace TealSuite.Tasks
{
    static class Program
    {
        public const string Id = "TealPlan";
        public static readonly AppSpec Spec = new AppSpec
        {
            Id = Id,
            Name = "Teal Plan",
            Role = "タスク＆予定",
            Description = "やること (タスク) と予定を「明日15時 歯医者」「明日 15:00-16:00 歯医者 @駅前」のように日本語で書くだけで登録できます。今日の予定とやることを 1 画面で見られ、月・週・日のカレンダーでドラッグして予定を作ったり動かしたりできます。タスクは小タスクに分けて一覧の中で進められます。データはこのパソコンの中だけに保存します。",
            // 1.9.x までの名前は Teal Tasks (ID TealTasks)。データ %APPDATA%\TealSuite\TealTasks\ は初回起動時にコピーして引き継ぐ
            LegacyIds = new[] { "TealTasks" },
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
