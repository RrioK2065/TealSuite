// TealSuite 共通: アプリの起動処理
//  - 二重起動の防止 (2 回目の起動は既存ウィンドウを前面に出す)
//  - Teal Hub (アプリ管理) からの「終了」要求を名前付きイベントで受け取り、きれいに終了する
//  - 予期しない例外を error.log に記録
//  - Windows のログイン時 (--startup 付き) に起動されたときは静かに起動する
//    (ウィンドウ型は最小化、トレイ型は案内の通知を出さない。すでに起動中なら何も表示せずに終わる)
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public static class AppHost
    {
        /// <summary>終了処理中なら true。「閉じる→トレイに隠す」アプリはこれを見て本当に閉じる。</summary>
        public static bool IsExiting { get; private set; }
        /// <summary>2 回目の起動やTeal Hub の「表示」で呼ばれる。既定は最初のフォームを前面へ。</summary>
        public static Action ShowRequested;
        /// <summary>Teal Hub の「終了」で呼ばれる。既定は RequestExit()。</summary>
        public static Action ExitRequested;
        /// <summary>Teal Hub がこのアプリの設定 (ショートカットなど) を書き換えたときに呼ばれる。既定は何もしない。</summary>
        public static Action ReloadRequested;

        static Control marshal;


        /// <summary>前回の起動で旧データを引き継いだときの元フォルダ (なければ null)</summary>
        public static string MigratedFrom { get; private set; }

        /// <summary>Windows のログイン時に (スタートアップ登録の --startup 付きで) 起動されたなら true</summary>
        public static bool StartedAtLogin { get; private set; }

        public static void Run(AppSpec spec, Func<Form> createMainForm)
        {
            RunContext(spec, () =>
            {
                var f = createMainForm();
                // ログイン時は最小化で起動 (WindowMemory が位置を復元した後に最小化する)
                if (StartedAtLogin) f.Load += (s, e) => f.WindowState = FormWindowState.Minimized;
                return new ApplicationContext(f);
            });
        }

        public static void RunContext(AppSpec spec, Func<ApplicationContext> createContext)
        {
            AppInfo.Init(spec);
            try { StartedAtLogin = Startup.HasStartupArg(Environment.GetCommandLineArgs().Skip(1)); } catch { StartedAtLogin = false; }
            string id = spec.Id, name = spec.Name;
            Mutex mutex = null;
            bool created = true;
            try { mutex = new Mutex(true, AppInfo.MutexName(id), out created); } catch { created = true; }
            if (!created)
            {
                // ログイン時の自動起動と重なった (すでに起動中) ときは何も表示しない
                if (StartedAtLogin) { if (mutex != null) mutex.Dispose(); return; }
                try { if (AppInfo.IsWindows) Native.AllowSetForegroundWindow(-1); } catch { }
                if (!Signal(AppInfo.ShowEventName(id)))
                    MessageBox.Show(name + " はすでに起動しています。", name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (mutex != null) mutex.Dispose();
                return;
            }

            EventWaitHandle exitEvt = null, showEvt = null, reloadEvt = null;
            RegisteredWaitHandle exitWait = null, showWait = null, reloadWait = null;
            try
            {
                // 二重起動でないと確定してから旧データを引き継ぐ (フォームがデータを読む前)
                try { MigratedFrom = DataStore.MigrateLegacyData(); } catch { }
                try { if (AppInfo.IsWindows) Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ReportError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => DataStore.Log("Unhandled: " + e.ExceptionObject);

                marshal = new Control();
                marshal.CreateControl();
                var unused = marshal.Handle;

                exitEvt = TryCreateEvent(AppInfo.ExitEventName(id));
                showEvt = TryCreateEvent(AppInfo.ShowEventName(id));
                if (exitEvt != null)
                    exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvt, (st, to) => OnUi(() => { if (ExitRequested != null) ExitRequested(); else RequestExit(); }), null, -1, false);
                if (showEvt != null)
                    showWait = ThreadPool.RegisterWaitForSingleObject(showEvt, (st, to) => OnUi(() => { if (ShowRequested != null) ShowRequested(); else ActivateMainForm(); }), null, -1, false);
                reloadEvt = TryCreateEvent(AppInfo.ReloadEventName(id));
                if (reloadEvt != null)
                    reloadWait = ThreadPool.RegisterWaitForSingleObject(reloadEvt, (st, to) => OnUi(() => { if (ReloadRequested != null) ReloadRequested(); }), null, -1, false);

                Application.Run(createContext());
            }
            finally
            {
                if (exitWait != null) exitWait.Unregister(null);
                if (showWait != null) showWait.Unregister(null);
                if (reloadWait != null) reloadWait.Unregister(null);
                if (reloadEvt != null) reloadEvt.Dispose();
                if (exitEvt != null) exitEvt.Dispose();
                if (showEvt != null) showEvt.Dispose();
                if (marshal != null) marshal.Dispose();
                if (mutex != null) { try { mutex.ReleaseMutex(); } catch { } mutex.Dispose(); }
            }
        }

        static EventWaitHandle TryCreateEvent(string name)
        {
            try { bool cn; return new EventWaitHandle(false, EventResetMode.AutoReset, name, out cn); }
            catch (Exception ex) { DataStore.Log("event " + name + ": " + ex.Message); return null; }
        }

        /// <summary>他プロセスの名前付きイベントをセットする。成功したら true。</summary>
        public static bool Signal(string eventName)
        {
            try
            {
                EventWaitHandle h;
                if (!EventWaitHandle.TryOpenExisting(eventName, out h)) return false;
                using (h) h.Set();
                return true;
            }
            catch { return false; }
        }

        /// <summary>UI スレッドで実行する</summary>
        public static void OnUi(Action a)
        {
            try
            {
                var m = marshal;
                if (m != null && m.IsHandleCreated && !m.IsDisposed) m.BeginInvoke(a);
            }
            catch { }
        }

        public static void RequestExit()
        {
            IsExiting = true;
            Application.Exit();
        }

        public static void ActivateMainForm()
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f.Modal || f.Owner != null) continue;
                BringToFront(f);
                return;
            }
        }

        public static void BringToFront(Form f)
        {
            if (!f.Visible) f.Show();
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
            try { if (AppInfo.IsWindows) Native.SetForegroundWindow(f.Handle); } catch { }
        }

        public static void ReportError(Exception ex)
        {
            DataStore.Log(ex.ToString());
            try
            {
                MessageBox.Show("予期しないエラーが発生しました。\n\n" + ex.Message + "\n\n詳細はログに記録しました:\n" + DataStore.PathOf("error.log"),
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        public static void OpenFolder(string path)
        {
            try
            {
                System.IO.Directory.CreateDirectory(path);
                if (AppInfo.IsWindows) Process.Start("explorer.exe", "\"" + path + "\"");
                else Process.Start("xdg-open", path);
            }
            catch (Exception ex) { DataStore.Log("open folder: " + ex.Message); }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { DataStore.Log("open url: " + ex.Message); }
        }
    }
}
