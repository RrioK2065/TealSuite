<#
  スモークテストを Windows で実行する (tests/run_tests.sh の Windows 版)
    powershell -ExecutionPolicy Bypass -File build.ps1 -Tests          (先にアプリとテストをビルド)
    powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1        (dist\TealSuite を試す)
    powershell -ExecutionPolicy Bypass -File tests\run_tests.ps1 -Dist dist-win\TealSuite
  -Dist の exe を build\ にコピーしてから build\SmokeTests.exe を実行する。
  最後の行は「結果: N ok / M fail」。失敗が 1 つでもあれば終了コード 1。
#>
param(
    [string]$Dist = "",
    [string]$TestsDir = "",
    [string]$Log = ""
)
$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Dist) { $Dist = Join-Path $Root "dist\TealSuite" }
$Dist = (Resolve-Path $Dist).Path
if (-not $TestsDir) { $TestsDir = Join-Path $Root "build" }
$exe = Join-Path $TestsDir "SmokeTests.exe"
if (-not (Test-Path $exe)) { Write-Host "  ! $exe がありません (先に build.ps1 -Tests)"; exit 1 }

# 参照先の exe を同じフォルダに置く (extern alias で読むため)
Get-ChildItem (Join-Path $Dist "apps") -Recurse -Filter *.exe | ForEach-Object { Copy-Item $_.FullName $TestsDir -Force }
Copy-Item (Join-Path $Dist "TealHub.exe"), (Join-Path $Dist "TealUpdater.exe") $TestsDir -Force

# 自動のアップデート確認 (GitHub への問い合わせ) を止める。
# スタートアップの登録は本物のレジストリ・スタートアップ フォルダに書かずに、一時ファイルへ
# (本物のレジストリは WinTests がテスト用の名前で読み書きして消す)
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("tealsuite-tests-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $tmp | Out-Null
$env:TEALSUITE_NO_UPDATE_CHECK = "1"
$env:TEALSUITE_RUNKEY_FILE = Join-Path $tmp "runkey.txt"
$env:TEALSUITE_STARTUP_DIR = Join-Path $tmp "startup"

Write-Host "テスト: $exe $Dist"
if ($Log) {
    # 画面とファイルの両方に出す (文字化けしないよう UTF-8 で受け取る)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = '"' + $Dist + '"'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $p = [System.Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()
    $w = New-Object System.IO.StreamWriter($Log, $false, (New-Object System.Text.UTF8Encoding($false)))
    while (($line = $p.StandardOutput.ReadLine()) -ne $null) { Write-Host $line; $w.WriteLine($line); $w.Flush() }
    $p.WaitForExit()
    $err = $errTask.Result
    if ($err) { Write-Host $err; $w.WriteLine($err) }
    $w.Close()
    $code = $p.ExitCode
} else {
    & $exe $Dist
    $code = $LASTEXITCODE
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
if ($code -ne 0) { Write-Host "::error::スモークテストに失敗があります (終了コード $code)" }
exit $code
