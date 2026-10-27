// Teal Pass (パスワード管理) - パスワードを安全寄りにコピーする (履歴アプリに残さない印 + 一定時間後に消去)
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Pass
{
    public class SecureClipboard : IDisposable
    {
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        readonly Timer timer = new Timer();
        string lastText;
        uint lastSeq;
        DateTime clearAt;
        public int Seconds = 30;
        public event Action Cleared;

        public SecureClipboard()
        {
            timer.Interval = 500;
            timer.Tick += (s, e) => { if (DateTime.Now >= clearAt) ClearIfOurs(); };
        }

        public int RemainingSeconds { get { return lastText == null ? 0 : Math.Max(0, (int)Math.Ceiling((clearAt - DateTime.Now).TotalSeconds)); } }

        /// <summary>
        /// テキストを置く。Windows のクリップボード履歴 (Win+V)・クラウド同期・
        /// 「コピー履歴」アプリなどの監視ツールには記録しないよう印を付ける。
        /// </summary>
        public void Copy(string text, bool sensitive)
        {
            if (string.IsNullOrEmpty(text)) return;
            var obj = new DataObject();
            obj.SetData(DataFormats.UnicodeText, true, text);
            obj.SetData(DataFormats.Text, true, text);
            if (sensitive)
            {
                obj.SetData("ExcludeClipboardContentFromMonitorProcessing", false, new MemoryStream(new byte[] { 0, 0, 0, 0 }));
                obj.SetData("CanIncludeInClipboardHistory", false, new MemoryStream(BitConverter.GetBytes(0)));
                obj.SetData("CanUploadToCloudClipboard", false, new MemoryStream(BitConverter.GetBytes(0)));
                obj.SetData("Clipboard Viewer Ignore", false, new MemoryStream(new byte[] { 0 }));
            }
            Clipboard.SetDataObject(obj, true, 5, 100);
            if (!sensitive) return;
            lastText = text;
            lastSeq = Seq();
            clearAt = DateTime.Now.AddSeconds(Seconds);
            timer.Start();
        }

        static uint Seq() { try { return AppInfo.IsWindows ? GetClipboardSequenceNumber() : 0; } catch { return 0; } }

        /// <summary>自分が置いた内容がまだ残っていれば消す (ほかのコピーで上書きされていたら何もしない)</summary>
        public void ClearIfOurs()
        {
            timer.Stop();
            if (lastText == null) return;
            try
            {
                bool ours = AppInfo.IsWindows && lastSeq != 0 ? Seq() == lastSeq : (Clipboard.ContainsText() && Clipboard.GetText() == lastText);
                if (ours) Clipboard.Clear();
            }
            catch { }
            lastText = null;
            if (Cleared != null) Cleared();
        }

        public void Dispose() { ClearIfOurs(); timer.Dispose(); }
    }
}
