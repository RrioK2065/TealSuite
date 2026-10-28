# Windows のチェック (tools/win/*.ps1) で共通に使う関数。 . "$PSScriptRoot\common.ps1" で読み込む
# PowerShell 5.1 (Windows に入っているもの) で動くように書く
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
if (-not ("TealCi.Win" -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace TealCi {
  public class WinInfo { public IntPtr Handle; public int Pid; public string Class; public string Title; public bool Visible; public int Left, Top, Width, Height; }
  public static class Win {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    // そのプロセスの最上位のウィンドウ (見えていないものも)
    public static List<WinInfo> Windows(int pid) {
      var list = new List<WinInfo>();
      EnumWindows((h, l) => {
        int p; GetWindowThreadProcessId(h, out p);
        if (pid == 0 || p == pid) {
          var c = new StringBuilder(256); GetClassName(h, c, 256);
          var t = new StringBuilder(512); GetWindowText(h, t, 512);
          RECT r; GetWindowRect(h, out r);
          list.Add(new WinInfo { Handle = h, Pid = p, Class = c.ToString(), Title = t.ToString(), Visible = IsWindowVisible(h), Left = r.L, Top = r.T, Width = r.R - r.L, Height = r.B - r.T });
        }
        return true;
      }, IntPtr.Zero);
      return list;
    }
    delegate bool EnumChildProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    // 子ウィンドウ (入れ子も) のうち、文字がちょうど text で、見えていて押せるもの
    public static IntPtr FindChildByText(IntPtr parent, string text) {
      IntPtr found = IntPtr.Zero;
      EnumChildWindows(parent, (h, l) => {
        var t = new StringBuilder(512); GetWindowText(h, t, 512);
        if (t.ToString() == text && IsWindowVisible(h) && IsWindowEnabled(h)) { found = h; return false; }
        return true;
      }, IntPtr.Zero);
      return found;
    }
    // 子ウィンドウ (入れ子も) の文字を全部 (ログ用)
    public static string AllTexts(IntPtr parent) {
      var sb = new StringBuilder();
      EnumChildWindows(parent, (h, l) => {
        var t = new StringBuilder(1024); GetWindowText(h, t, 1024);
        if (t.Length > 0 && IsWindowVisible(h)) sb.Append(t.ToString().Replace("\r", " ").Replace("\n", " ")).Append(" | ");
        return true;
      }, IntPtr.Zero);
      return sb.ToString();
    }
    // ダイアログの中の文字 (メッセージ ボックスの本文など)
    public static string ChildTexts(IntPtr h) {
      var sb = new StringBuilder(); IntPtr c = IntPtr.Zero;
      while ((c = FindWindowEx(h, c, null, null)) != IntPtr.Zero) {
        var t = new StringBuilder(1024); GetWindowText(c, t, 1024);
        if (t.Length > 0) sb.Append(t.ToString().Replace("\r", " ").Replace("\n", " ")).Append(" | ");
      }
      return sb.ToString();
    }
  }
}
"@
}

function Write-Ok([string]$m)   { Write-Host "  ok   $m" }
function Write-Bad([string]$m)  { Write-Host "  FAIL $m"; Write-Host "::error::$m"; $script:Failures++ }
function Write-Warn([string]$m) { Write-Host "  warn $m"; Write-Host "::warning::$m" }
$script:Failures = 0

# 見えているウィンドウ (メッセージ ボックス = クラス #32770 は別に数える)
function Get-VisibleWindows([int]$procId) {
    return @([TealCi.Win]::Windows($procId) | Where-Object { $_.Visible -and $_.Width -gt 0 -and $_.Height -gt 0 })
}

# ウィンドウの画像 (PrintWindow。うまくいかないときは画面から切り取る)。失敗してもチェックは止めない
function Save-WindowShot($win, [string]$path) {
    try {
        [void][TealCi.Win]::ShowWindow($win.Handle, 9)   # SW_RESTORE
        [void][TealCi.Win]::SetForegroundWindow($win.Handle)
        Start-Sleep -Milliseconds 400
        $bmp = New-Object System.Drawing.Bitmap ([Math]::Max(1, $win.Width)), ([Math]::Max(1, $win.Height))
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        $ok = [TealCi.Win]::PrintWindow($win.Handle, $hdc, 2)   # PW_RENDERFULLCONTENT
        $g.ReleaseHdc($hdc)
        if (-not $ok) { $g.CopyFromScreen($win.Left, $win.Top, 0, 0, $bmp.Size) }
        $g.Dispose()
        New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "  shot $path"
    } catch { Write-Host "  (スクリーンショットを撮れませんでした: $($_.Exception.Message))" }
}

function Save-ScreenShot([string]$path) {
    try {
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
        $g.Dispose()
        New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "  shot $path"
    } catch { Write-Host "  (画面全体のスクリーンショットを撮れませんでした: $($_.Exception.Message))" }
}

# 終了の合図 (Local\TealSuite.<id>.Exit。Teal Hub や TealUpdater が使うのと同じ)
function Send-ExitSignal([string]$appId) {
    try {
        $e = [System.Threading.EventWaitHandle]::OpenExisting("Local\TealSuite.$appId.Exit")
        [void]$e.Set(); $e.Dispose(); return $true
    } catch { return $false }
}

# そのフォルダの中の exe から起動したプロセス
function Get-SuiteProcesses([string]$suite) {
    $full = [System.IO.Path]::GetFullPath($suite).TrimEnd('\') + '\'
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $p = $null; try { $p = $_.Path } catch { }
        $p -and $p.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) })
}

function Stop-Suite([string]$suite) {
    foreach ($p in Get-SuiteProcesses $suite) {
        Write-Host "  (残っていたプロセスを終了: $($p.ProcessName) $($p.Id))"
        try { $p.Kill(); [void]$p.WaitForExit(5000) } catch { }
    }
}

# error.log に例外が書かれていないか (DataStore.Log はふつうのお知らせも書くので「Exception」「Unhandled」だけを見る)
function Test-ErrorLogs([string]$dataDir, [string]$label) {
    $bad = $false
    foreach ($f in @(Get-ChildItem -Path $dataDir -Recurse -Filter "error.log" -ErrorAction SilentlyContinue)) {
        $text = Get-Content -Raw -Encoding UTF8 $f.FullName
        Write-Host "  --- $($f.FullName)"; Write-Host $text
        if ($text -match "Exception|Unhandled") { Write-Bad "$label : $($f.Directory.Name)\error.log に例外が記録されています"; $bad = $true }
    }
    return (-not $bad)
}

function Get-Sha256([string]$path) {
    $s = [System.Security.Cryptography.SHA256]::Create()
    $fs = [System.IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($s.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $fs.Dispose(); $s.Dispose() }
}

# 試すときの環境: データ・スタートアップの登録を一時フォルダへ (本物の %APPDATA% やレジストリに触らない)
function Set-IsolatedEnv([string]$root) {
    New-Item -ItemType Directory -Force (Join-Path $root "data"), (Join-Path $root "appdata"), (Join-Path $root "startup") | Out-Null
    $env:TEALSUITE_DATA_DIR = Join-Path $root "data"
    $env:APPDATA = Join-Path $root "appdata"
    $env:TEALSUITE_RUNKEY_FILE = Join-Path $root "runkey.txt"
    $env:TEALSUITE_STARTUP_DIR = Join-Path $root "startup"
    $env:TEALSUITE_NO_UPDATE_CHECK = "1"
    $env:TEALSUITE_LEGACY_DATA_DIR = Join-Path $root "legacy"
}
