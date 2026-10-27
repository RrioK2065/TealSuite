// TealSuite 共通: ウィンドウの位置・大きさを覚える (window.json)
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace TealSuite.Common
{
    [DataContract]
    public class WindowState
    {
        [DataMember(Name = "x")] public int X;
        [DataMember(Name = "y")] public int Y;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        [DataMember(Name = "maximized")] public bool Maximized;
        [DataMember(Name = "splitter")] public int Splitter;   // 分割位置など、アプリが自由に使う値
    }

    public static class WindowMemory
    {
        /// <summary>保存済みの位置を復元し、閉じるときに保存する。key ごとに別ファイル。
        /// defaultWidth/Height は 96dpi 基準の値を渡す (中で Theme.S を掛けるので呼び出し側では掛けない)</summary>
        public static WindowState Attach(Form f, int defaultWidth, int defaultHeight, string key = "window", Func<int> splitterGetter = null)
        {
            string file = key + ".json";
            var s = DataStore.LoadQuiet<WindowState>(file, () => null);
            f.StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Theme.S(defaultWidth), wa.Width), h = Math.Min(Theme.S(defaultHeight), wa.Height);
            var b = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            if (s != null && s.Width >= 200 && s.Height >= 150)
            {
                var saved = new Rectangle(s.X, s.Y, s.Width, s.Height);
                // モニタ構成が変わって画面外になっていないか (タイトルバー付近が見えているか)
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(saved.X + 40, saved.Y + 4, Math.Max(1, saved.Width - 80), 30))))
                    b = saved;
            }
            f.Bounds = b;
            if (s != null && s.Maximized) f.WindowState = FormWindowState.Maximized;
            f.FormClosing += (o, e) =>
            {
                if (e.Cancel) return;
                var r = f.WindowState == FormWindowState.Normal ? f.Bounds : f.RestoreBounds;
                if (r.Width <= 0 || r.Height <= 0) return;
                int sp = 0;
                try { if (splitterGetter != null) sp = splitterGetter(); } catch { }
                DataStore.TrySave(file, new WindowState { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Maximized = f.WindowState == FormWindowState.Maximized, Splitter = sp });
            };
            return s;
        }
    }
}
