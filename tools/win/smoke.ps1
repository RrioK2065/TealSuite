<#
  起動のチェック (Windows): 各アプリを本当に起動して、画面が出て落ちないことを確かめる
    powershell -ExecutionPolicy Bypass -File tools\win\smoke.ps1 -Suite dist\TealSuite -Shots out\screenshots
  アプリごとに:
    1. 空の一時データ (TEALSUITE_DATA_DIR・%APPDATA%) で起動する (常駐アプリの Teal Clip は /show で画面を出す)
    2. 最大 -Timeout 秒、プロセスが生きていてメイン ウィンドウが出るのを待つ
    3. 少し待って、まだ生きていること・エラーのダイアログ (#32770) が出ていないこと・error.log に例外がないことを確かめる
    4. スクリーンショットを撮る (撮れなくても失敗にしない)
    5. 終了の合図 (Local\TealSuite.<id>.Exit) を送り、-Timeout 秒以内に終わることを確かめる (終わらなければ失敗)
  1 つでも失敗があれば終了コード 1。
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,
    [string]$Shots = "",
    [int]$Timeout = 20,
    [int]$Settle = 3
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
$Suite = (Resolve-Path $Suite).Path
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $Suite "apps.json") | ConvertFrom-Json
Write-Host "起動のチェック: $Suite (v$($manifest.suiteVersion))"
# 名前が変わる前のアプリ (1.9.0: TealSub → TealWallet、1.10.0: TealTasks → TealPlan) が一覧に残っていないか
foreach ($old in @("TealSub", "TealTasks")) { if ($manifest.apps | Where-Object { $_.id -eq $old }) { Write-Bad "apps.json に $old が残っています" } }

$targets = @(@{ id = "TealHub"; exe = "TealHub.exe"; args = "" })
foreach ($a in $manifest.apps) {
    $appArgs = ""
    if ($a.id -eq "TealClip") { $appArgs = "/show" }   # 常駐中はトレイだけなので、画面を出す引数を付ける
    $targets += @{ id = $a.id; exe = $a.exe; args = $appArgs }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tealsuite-smoke-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
foreach ($t in $targets) {
    $id = $t.id
    Write-Host ""
    Write-Host "== $id ($($t.exe) $($t.args))"
    $exe = Join-Path $Suite ($t.exe -replace '/', '\')
    if (-not (Test-Path $exe)) { Write-Bad "$id : $exe がありません"; continue }
    Stop-Suite $Suite
    $root = Join-Path $work $id
    Set-IsolatedEnv $root
    $env:TEALSUITE_BOOTLOG = "1"
    Remove-Item (Join-Path ([System.IO.Path]::GetTempPath()) "tealsuite-boot.log") -ErrorAction SilentlyContinue
    $failBefore = $script:Failures
    try {
        if ($t.args) { $p = Start-Process -FilePath $exe -ArgumentList $t.args -PassThru } else { $p = Start-Process -FilePath $exe -PassThru }
        $null = $p.Handle   # 終了コードを後で読めるように
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $main = $null
        while ($sw.Elapsed.TotalSeconds -lt $Timeout) {
            Start-Sleep -Milliseconds 300
            if ($p.HasExited) { break }
            $wins = Get-VisibleWindows $p.Id
            $main = $wins | Where-Object { $_.Class -ne "#32770" -and $_.Title } | Select-Object -First 1
            if ($main -or ($wins | Where-Object { $_.Class -eq "#32770" })) { break }
        }
        if ($p.HasExited) {
            $boot = Join-Path ([System.IO.Path]::GetTempPath()) "tealsuite-boot.log"
            if (Test-Path $boot) { Write-Host "  --- boot"; Write-Host (Get-Content -Raw -Encoding UTF8 $boot) }
            Write-Bad "$id : 起動してすぐに終了しました (終了コード $($p.ExitCode))"; [void](Test-ErrorLogs $root $id); continue
        }
        Start-Sleep -Seconds $Settle
        $wins = Get-VisibleWindows $p.Id
        foreach ($w in $wins) { Write-Host ("  window [{0}] '{1}' {2}x{3}" -f $w.Class, $w.Title, $w.Width, $w.Height) }
        $dialogs = @($wins | Where-Object { $_.Class -eq "#32770" })
        foreach ($d in $dialogs) {
            $text = [TealCi.Win]::ChildTexts($d.Handle)
            if ($Shots) { Save-WindowShot $d (Join-Path $Shots "$id-dialog.png") }
            Write-Bad "$id : ダイアログが出ています: '$($d.Title)' $text"
        }
        $main = $wins | Where-Object { $_.Class -ne "#32770" -and $_.Title } | Select-Object -First 1
        if ($p.HasExited) { Write-Bad "$id : 画面が出たあとに終了しました (終了コード $($p.ExitCode))" }
        elseif (-not $main) { Write-Bad "$id : $Timeout 秒待ってもメイン ウィンドウが出ません" }
        else {
            Write-Ok "$id : 起動して画面が出た ('$($main.Title)' $($main.Width)x$($main.Height)、$([int]$sw.Elapsed.TotalMilliseconds) ms)"
            if ($Shots) { Save-WindowShot $main (Join-Path $Shots "$id.png") }
        }
        if (Test-ErrorLogs $root $id) { Write-Ok "$id : error.log に例外なし" }

        if (-not $p.HasExited) {
            if (-not (Send-ExitSignal $id)) {
                Write-Warn "$id : 終了の合図 (Local\TealSuite.$id.Exit) を開けませんでした。ウィンドウを閉じます"
                [void]$p.CloseMainWindow()
            }
            if ($p.WaitForExit($Timeout * 1000)) {
                Write-Ok "$id : 終了の合図で終わった (終了コード $($p.ExitCode))"
                if ($p.ExitCode -ne 0) { Write-Warn "$id : 終了コードが 0 ではありません ($($p.ExitCode))" }
            } else {
                Write-Bad "$id : 終了の合図を送っても $Timeout 秒以内に終わりません"
                try { $p.Kill() } catch { }
            }
        }
    } catch {
        Write-Bad "$id : チェック中にエラー: $($_.Exception.Message)"
    } finally {
        Stop-Suite $Suite
        if ($script:Failures -gt $failBefore -and $Shots) { Save-ScreenShot (Join-Path $Shots "$id-screen-failed.png") }
    }
}
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host ""
if ($script:Failures -gt 0) { Write-Host "起動のチェック: 失敗 $($script:Failures) 件"; exit 1 }
Write-Host "起動のチェック: すべて ok ($($targets.Count) 個)"
exit 0
