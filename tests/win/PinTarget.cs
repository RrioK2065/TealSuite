// テスト用のウィンドウ (Windows のみ: tests/V1110Tests.cs の「Windows: 本物の最前面 (Teal Pin)」)
//   ふつうの (最前面でない) ウィンドウを 1 つ出して、閉じられるか終了させられるまで待つ。
//   Teal Pin がこのウィンドウを最前面に固定し、WS_EX_TOPMOST が付いた・外れたことをテストが確かめる
//   build.ps1 -Tests が build\PinTarget.exe を作る。引数 = ウィンドウのタイトル
using System;
using System.Drawing;
using System.Windows.Forms;

static class PinTarget
{
    [STAThread]
    static void Main(string[] args)
    {
        var f = new Form
        {
            Text = args.Length > 0 ? args[0] : "TealPin PinTarget",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(640, 420),
            ShowInTaskbar = true,
        };
        Application.Run(f);
    }
}
