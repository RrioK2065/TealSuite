<#
  アップデートのチェック (Windows): 前のリリースの zip を展開し、新しく作った .tealupdate を
  「前のリリースに入っている本物の TealUpdater.exe」で当てて、結果を確かめる
    powershell -ExecutionPolicy Bypass -File tools\win\update-test.ps1 `
        -PrevZip prev\TealSuite-1.9.1.zip -Package release\TealSuite-update-1.10.0.tealupdate -Dist dist\TealSuite
    -Seed180 … 1.8.0 のデータ (Teal Sub のサブスク・Hub の設定・スタートアップの登録) を置いてから試し、
               Teal Wallet に引き継がれることも確かめる (Teal Tasks のデータも置く)
    -Seed19  … 1.9.x のデータ (Teal Tasks のタスク・Hub の設定・スタートアップの登録) を置いてから試し、
               Teal Plan に引き継がれることも確かめる (1.10.0)。前の版が 1.9.x なら付けなくても行う
  手順:
    1. 前の版を一時フォルダに展開し、前の版の Teal Hub と Teal Memo を起動しておく (アップデートで閉じて起動し直すかの確認)
    2. 前の版の TealUpdater.exe <ファイル> を起動 → %TEMP% にコピーされた TealUpdater の画面の「アップデート」を押す
       (ボタンに BM_CLICK → だめなら Enter キー。画面を操作できなかったときは、前の版の TealUpdater.exe の中の
        UpdateEngine を直接呼んで当てる。そのときは警告を出す)
    3. 確かめること: apps.json のバージョン・アプリの一覧 (TealSub・TealTasks がない・重複がない)・
       全ファイルのハッシュが新しいビルドと同じ・余分なファイルがない (backup\ は除く)・.update-staging が残っていない
  1 つでも失敗があれば終了コード 1。
#>
param(
    [Parameter(Mandatory = $true)][string]$PrevZip,
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$Dist,
    [switch]$Seed180,
    [switch]$Seed19,
    [string]$Shots = "",
    [int]$Timeout = 120
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
$PrevZip = (Resolve-Path $PrevZip).Path
$Package = (Resolve-Path $Package).Path
$Dist = (Resolve-Path $Dist).Path
$label = [System.IO.Path]::GetFileNameWithoutExtension($PrevZip)
$distManifest = Get-Content -Raw -Encoding UTF8 (Join-Path $Dist "apps.json") | ConvertFrom-Json
$expect = $distManifest.suiteVersion

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tealsuite-upd-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
Write-Host "アップデートのチェック: $label → $expect"
Write-Host "  ファイル: $Package"
Expand-Archive -Path $PrevZip -DestinationPath (Join-Path $work "x")
$suite = Join-Path $work "x\TealSuite"
if (-not (Test-Path (Join-Path $suite "apps.json"))) { Write-Bad "zip の中に TealSuite\apps.json がありません"; exit 1 }
$prevManifest = Get-Content -Raw -Encoding UTF8 (Join-Path $suite "apps.json") | ConvertFrom-Json
$prevVer = $prevManifest.suiteVersion
Write-Host "  前の版: $prevVer ($suite)"
$envRoot = Join-Path $work "env"
Set-IsolatedEnv $envRoot
$data = $env:TEALSUITE_DATA_DIR

# 1.10.0: Teal Tasks → Teal Plan。1.9.x から当てるときは自動で確かめる
$seedTasks = $Seed180 -or $Seed19 -or ("$prevVer" -like "1.9.*")
# 1.10.0 より前の版で、Teal Tasks がまだある (apps.json に TealTasks がある) ときだけ
if ($seedTasks -and -not ($prevManifest.apps | Where-Object { $_.id -eq "TealTasks" })) { $seedTasks = $false }
if ($Seed180 -or $seedTasks) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    New-Item -ItemType Directory -Force (Join-Path $data "TealHub") | Out-Null
    $hubApps = @('{"id":"TealMemo","hotkey":"Ctrl+Alt+M"}')
    $runKey = ""
    if ($Seed180) {
        # 1.8.0 の利用者のデータ (Teal Sub)
        New-Item -ItemType Directory -Force (Join-Path $data "TealSub") | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $data "TealSub\subs.json"), '{"version":1,"subs":[{"id":"ci1","name":"CI の動画サービス","amount":990,"cycle":"monthly"}],"settings":{"budget":18000}}', $utf8)
        $hubApps += '{"id":"TealSub","hotkey":"Ctrl+Alt+S","resident":true}'
        $runKey += "TealSuite.TealSub`t`"$(Join-Path $suite 'apps\TealSub\TealSub.exe')`" --startup`r`n"
        Write-Host "  1.8.0 のデータを置きました (TealSub\subs.json・Hub の設定・スタートアップ TealSuite.TealSub)"
    }
    if ($seedTasks) {
        # 1.9.x の利用者のデータ (Teal Tasks)
        New-Item -ItemType Directory -Force (Join-Path $data "TealTasks") | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $data "TealTasks\tasks.json"), '{"version":1,"tasks":[{"id":"ci1","title":"CI のタスク","priority":1,"tags":[],"repeat":"none","done":false,"subtasks":[{"text":"CI の小タスク","done":false}]}],"settings":{"remind":true,"remindMinutes":15,"morning":true},"notified":[]}', $utf8)
        $hubApps += '{"id":"TealTasks","hotkey":"Ctrl+Alt+T","resident":true}'
        $runKey += "TealSuite.TealTasks`t`"$(Join-Path $suite 'apps\TealTasks\TealTasks.exe')`" --startup`r`n"
        Write-Host "  1.9.x のデータを置きました (TealTasks\tasks.json・Hub の設定・スタートアップ TealSuite.TealTasks)"
    }
    [System.IO.File]::WriteAllText((Join-Path $data "TealHub\app-settings.json"), '{"version":1,"apps":[' + ($hubApps -join ',') + ']}', $utf8)
    [System.IO.File]::WriteAllText($env:TEALSUITE_RUNKEY_FILE, $runKey, $utf8)
}

# --- 1. 前の版のアプリを起動しておく ---
$before = @()
foreach ($rel in @("TealHub.exe", "apps\TealMemo\TealMemo.exe")) {
    $exe = Join-Path $suite $rel
    if (Test-Path $exe) {
        $p = Start-Process -FilePath $exe -PassThru
        $before += $p
    }
}
Start-Sleep -Seconds 4
foreach ($p in $before) { if ($p.HasExited) { Write-Warn "前の版の $($p.ProcessName) が起動後に終了しました" } else { Write-Host "  前の版を起動: $($p.ProcessName) ($($p.Id))" } }

function Get-InstalledVersion {
    try { return (Get-Content -Raw -Encoding UTF8 (Join-Path $suite "apps.json") | ConvertFrom-Json).suiteVersion } catch { return $null }
}
function Get-TempUpdaters {
    $tmp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    return @(Get-Process -Name TealUpdater -ErrorAction SilentlyContinue | Where-Object {
        $p = $null; try { $p = $_.Path } catch { }
        $p -and ($p -like "*TealSuiteUpdater-*") })
}

# --- 2. 前の版の TealUpdater で当てる (画面を操作) ---
$mode = "gui"
$updater = Join-Path $suite "TealUpdater.exe"
Get-TempUpdaters | ForEach-Object { try { $_.Kill() } catch { } }
Start-Process -FilePath $updater -ArgumentList ('"' + $Package + '"') | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
$clicked = $false; $entered = $false; $sentKeys = $false; $done = $false; $form = $null
while ($sw.Elapsed.TotalSeconds -lt $Timeout) {
    Start-Sleep -Milliseconds 500
    $u = Get-TempUpdaters | Select-Object -First 1
    if (-not $u) { $u = @(Get-Process -Name TealUpdater -ErrorAction SilentlyContinue) | Select-Object -First 1 }
    if ($u) {
        $form = Get-VisibleWindows $u.Id | Where-Object { $_.Class -ne "#32770" } | Select-Object -First 1
        foreach ($d in @(Get-VisibleWindows $u.Id | Where-Object { $_.Class -eq "#32770" })) { Write-Warn "TealUpdater のダイアログ: '$($d.Title)' $([TealCi.Win]::ChildTexts($d.Handle))" }
    }
    $v = Get-InstalledVersion
    if ($v -eq $expect -and -not (Test-Path (Join-Path $suite ".update-staging"))) {
        # 書き換えが終わってから「完了」の表示になるまで少し待つ
        if ($form -and [TealCi.Win]::FindChildByText($form.Handle, "閉じる") -ne [IntPtr]::Zero) { $done = $true; break }
        if ($sw.Elapsed.TotalSeconds -gt 20 -and -not $form) { $done = $true; break }
        continue
    }
    if (-not $form) { continue }
    $btn = [TealCi.Win]::FindChildByText($form.Handle, "アップデート")
    if (-not $clicked -and $btn -ne [IntPtr]::Zero) {
        Write-Host "  TealUpdater の画面: $([TealCi.Win]::AllTexts($form.Handle))"
        if ($Shots) { Save-WindowShot $form (Join-Path $Shots "update-$label-before.png") }
        [void][TealCi.Win]::PostMessage($btn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
        Write-Host "  「アップデート」を押しました (BM_CLICK, $([int]$sw.Elapsed.TotalSeconds) 秒)"
        $clicked = $true; $clickAt = $sw.Elapsed.TotalSeconds
        continue
    }
    if ($clicked -and -not $entered -and $btn -ne [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds - $clickAt -gt 8) {
        [void][TealCi.Win]::PostMessage($form.Handle, 0x0100, [IntPtr]13, [IntPtr]::Zero)   # WM_KEYDOWN VK_RETURN
        [void][TealCi.Win]::PostMessage($form.Handle, 0x0101, [IntPtr]13, [IntPtr]::Zero)
        Write-Host "  Enter キーを送りました (PostMessage)"
        $entered = $true; $enterAt = $sw.Elapsed.TotalSeconds
        continue
    }
    if ($entered -and -not $sentKeys -and $btn -ne [IntPtr]::Zero -and $sw.Elapsed.TotalSeconds - $enterAt -gt 8) {
        try {
            [void][TealCi.Win]::SetForegroundWindow($form.Handle); Start-Sleep -Milliseconds 300
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Write-Host "  Enter キーを送りました (SendKeys)"
        } catch { Write-Host "  (SendKeys できませんでした: $($_.Exception.Message))" }
        $sentKeys = $true
    }
}
if ($form) {
    Write-Host "  TealUpdater の画面 (最後): $([TealCi.Win]::AllTexts($form.Handle))"
    if ($Shots) { Save-WindowShot $form (Join-Path $Shots "update-$label-after.png") }
}
if (-not $done) {
    $mode = "engine"
    Write-Warn "$label : TealUpdater の画面を操作してアップデートできませんでした ($Timeout 秒)。前の版の UpdateEngine を直接呼んで続けます"
    if ($Shots) { Save-ScreenShot (Join-Path $Shots "update-$label-gui-failed.png") }
    Get-TempUpdaters | ForEach-Object { try { $_.Kill() } catch { } }
    Stop-Suite $suite
    if ((Get-InstalledVersion) -ne $expect) {
        # 別のプロセスで動かす (このプロセスに前の版の型を読み込まない)
        $harness = @"
`$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
`$asm = [System.Reflection.Assembly]::LoadFrom('$($updater -replace "'", "''")')
`$pkgType = `$asm.GetType('TealSuite.Updater.UpdatePackage', `$true)
`$engType = `$asm.GetType('TealSuite.Updater.UpdateEngine', `$true)
`$pkg = `$pkgType.GetMethod('Read', [Type[]]@([string])).Invoke(`$null, @('$($Package -replace "'", "''")'))
`$eng = [Activator]::CreateInstance(`$engType, @('$($suite -replace "'", "''")'))
`$res = `$engType.GetMethod('Apply').Invoke(`$eng, @(`$pkg))
Write-Host ("  UpdateEngine: Ok=" + `$res.Ok + " " + `$res.From + " -> " + `$res.To + " " + `$res.Error)
if (-not `$res.Ok) { exit 1 }
"@
        $hp = Join-Path $work "apply.ps1"
        [System.IO.File]::WriteAllText($hp, $harness, (New-Object System.Text.UTF8Encoding($true)))
        & powershell -NoProfile -ExecutionPolicy Bypass -File $hp
        if ($LASTEXITCODE -ne 0) { Write-Bad "$label : 前の版の UpdateEngine で当てられませんでした" }
    }
} else {
    Write-Ok "$label : 前の版の TealUpdater の画面でアップデートした ($([int]$sw.Elapsed.TotalSeconds) 秒)"
}

# --- アップデートのあとに起動し直されたか (画面で当てたときだけ。失敗にはしない) ---
if ($mode -eq "gui") {
    Start-Sleep -Seconds 5
    $running = Get-SuiteProcesses $suite
    foreach ($name in @("TealHub", "TealMemo")) {
        $r = $running | Where-Object { $_.ProcessName -eq $name } | Select-Object -First 1
        if ($r) {
            # Process.MainModule can block for a long time on some processes; the check only needs the name.
            Write-Ok "$label : $name はアップデートのあとに起動し直された (pid $($r.Id))"
        } else { Write-Warn "$label : $name が起動し直されていません" }
    }
    foreach ($p in $before) { if (-not $p.HasExited) { Write-Warn "$label : 前の版の $($p.ProcessName) ($($p.Id)) が閉じられずに残っています" } }
}
Get-TempUpdaters | ForEach-Object { try { $_.Kill() } catch { } }
Stop-Suite $suite
Start-Sleep -Seconds 1

# --- 3. 確かめる ---
$inst = Get-Content -Raw -Encoding UTF8 (Join-Path $suite "apps.json") | ConvertFrom-Json
if ($inst.suiteVersion -eq $expect) { Write-Ok "$label : apps.json の suiteVersion = $expect" } else { Write-Bad "$label : apps.json の suiteVersion が $($inst.suiteVersion) (期待 $expect)" }
$ids = @($inst.apps | ForEach-Object { $_.id })
$want = @($distManifest.apps | ForEach-Object { $_.id })
$dups = @($ids | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
if ($dups.Count -gt 0) { Write-Bad "$label : apps.json に重複: $($dups -join ', ')" }
foreach ($old in @("TealSub", "TealTasks")) { if ($ids -contains $old) { Write-Bad "$label : apps.json に $old が残っています" } }
$missing = @($want | Where-Object { $ids -notcontains $_ }); $extra = @($ids | Where-Object { $want -notcontains $_ })
if ($missing.Count -or $extra.Count) { Write-Bad "$label : アプリの一覧が違います (足りない: $($missing -join ', ') / 余分: $($extra -join ', '))" }
else { Write-Ok "$label : アプリの一覧 ($($ids -join ', '))" }
foreach ($a in $distManifest.apps) {
    $ia = $inst.apps | Where-Object { $_.id -eq $a.id } | Select-Object -First 1
    if ($ia -and ($ia.version -ne $a.version -or $ia.exe -ne $a.exe)) { Write-Bad "$label : $($a.id) の項目が違います ($($ia.exe) $($ia.version))" }
}

# ファイル (apps.json は項目で比べたので除く)
$distFiles = @{}
Get-ChildItem $Dist -Recurse -File | ForEach-Object { $distFiles[$_.FullName.Substring($Dist.Length + 1)] = $_.FullName }
$badHash = 0
foreach ($rel in $distFiles.Keys) {
    if ($rel -eq "apps.json") { continue }
    $f = Join-Path $suite $rel
    if (-not (Test-Path $f)) { Write-Bad "$label : $rel が入っていません"; $badHash++; continue }
    if ((Get-Sha256 $f) -ne (Get-Sha256 $distFiles[$rel])) { Write-Bad "$label : $rel の中身が新しいビルドと違います"; $badHash++ }
}
if ($badHash -eq 0) { Write-Ok "$label : $($distFiles.Count - 1) 個のファイルが新しいビルドと同じ (SHA-256)" }
$left = @(Get-ChildItem $suite -Recurse -File | ForEach-Object { $_.FullName.Substring($suite.Length + 1) } |
    Where-Object { -not $_.StartsWith("backup\") -and -not $distFiles.ContainsKey($_) })
if ($left.Count) { foreach ($l in $left) { Write-Bad "$label : 余分なファイルが残っています: $l" } } else { Write-Ok "$label : 余分なファイルなし" }
foreach ($d in @(Get-ChildItem $suite -Recurse -Directory | Where-Object { -not $_.FullName.Substring($suite.Length + 1).StartsWith("backup") -and @(Get-ChildItem $_.FullName -Force).Count -eq 0 })) {
    Write-Warn "$label : 空のフォルダが残っています: $($d.FullName.Substring($suite.Length + 1)) (前の版の TealUpdater は空のフォルダを消さない。害はない)"
}
if (Test-Path (Join-Path $suite ".update-staging")) { Write-Bad "$label : .update-staging が残っています" } else { Write-Ok "$label : .update-staging なし" }
if (Test-Path (Join-Path $suite "backup")) { Write-Ok "$label : backup フォルダに前の版を保存 ($(@(Get-ChildItem (Join-Path $suite 'backup')).Count) 件)" } else { Write-Warn "$label : backup フォルダがありません" }

# --- 1.8.0 / 1.9.x から: 新しい Teal Hub と、名前の変わったアプリを起動して、引き継ぎを確かめる ---
if ($Seed180 -or $seedTasks) {
    $launch = @("TealHub.exe")
    if ($Seed180) { $launch += "apps\TealWallet\TealWallet.exe" }
    if ($seedTasks) { $launch += "apps\TealPlan\TealPlan.exe" }
    foreach ($rel in $launch) {
        $p = Start-Process -FilePath (Join-Path $suite $rel) -PassThru
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 20 -and -not $p.HasExited -and -not (Get-VisibleWindows $p.Id | Where-Object { $_.Class -ne "#32770" -and $_.Title })) { Start-Sleep -Milliseconds 300 }
        Start-Sleep -Seconds 2
        $w = Get-VisibleWindows $p.Id | Where-Object { $_.Class -ne "#32770" -and $_.Title } | Select-Object -First 1
        if ($p.HasExited) { Write-Bad "$label : アップデート後の $rel が起動してすぐに終了しました" }
        elseif ($w) { Write-Ok "$label : アップデート後の $rel が起動した ('$($w.Title)')"; if ($Shots) { Save-WindowShot $w (Join-Path $Shots ("update-$label-" + [System.IO.Path]::GetFileNameWithoutExtension($rel) + ".png")) } }
        else { Write-Bad "$label : アップデート後の $rel の画面が出ません" }
    }
    Start-Sleep -Seconds 2
    foreach ($id in @("TealWallet", "TealPlan", "TealHub")) { [void](Send-ExitSignal $id) }
    Start-Sleep -Seconds 3
    Stop-Suite $suite
    $prefs = Get-Content -Raw -Encoding UTF8 (Join-Path $data "TealHub\app-settings.json") | ConvertFrom-Json
    $rk = if (Test-Path $env:TEALSUITE_RUNKEY_FILE) { Get-Content -Raw -Encoding UTF8 $env:TEALSUITE_RUNKEY_FILE } else { "" }
    Write-Host "  スタートアップ: $($rk -replace "`r?`n", ' / ')"
    if ($Seed180) {
        $subs = Join-Path $data "TealWallet\subs.json"
        if ((Test-Path $subs) -and ((Get-Content -Raw -Encoding UTF8 $subs) -match "CI の動画サービス")) { Write-Ok "$label : Teal Sub のデータが TealWallet\subs.json に引き継がれた" } else { Write-Bad "$label : TealWallet\subs.json に Teal Sub のデータがありません" }
        if (Test-Path (Join-Path $data "TealSub\subs.json")) { Write-Ok "$label : 元の TealSub\subs.json は残っている (前の版に戻しても使える)" } else { Write-Warn "$label : 元の TealSub\subs.json がありません" }
        $pw = $prefs.apps | Where-Object { $_.id -eq "TealWallet" } | Select-Object -First 1
        if ($pw -and $pw.hotkey -eq "Ctrl+Alt+S" -and -not ($prefs.apps | Where-Object { $_.id -eq "TealSub" })) { Write-Ok "$label : Hub の設定 (ショートカット Ctrl+Alt+S) が TealWallet に付け替えられた" } else { Write-Bad "$label : Hub の設定が付け替えられていません: $(Get-Content -Raw -Encoding UTF8 (Join-Path $data 'TealHub\app-settings.json'))" }
        if ($rk -match "TealSuite\.TealWallet\t.*apps\\TealWallet\\TealWallet\.exe" -and $rk -notmatch "TealSuite\.TealSub\t") { Write-Ok "$label : スタートアップが TealSuite.TealWallet (TealWallet.exe) に登録し直された" } else { Write-Bad "$label : スタートアップの登録が引き継がれていません (TealWallet)" }
    }
    if ($seedTasks) {
        $tasks = Join-Path $data "TealPlan\tasks.json"
        if ((Test-Path $tasks) -and ((Get-Content -Raw -Encoding UTF8 $tasks) -match "CI のタスク")) { Write-Ok "$label : Teal Tasks のデータが TealPlan\tasks.json に引き継がれた" } else { Write-Bad "$label : TealPlan\tasks.json に Teal Tasks のデータがありません" }
        if (Test-Path (Join-Path $data "TealTasks\tasks.json")) { Write-Ok "$label : 元の TealTasks\tasks.json は残っている (前の版に戻しても使える)" } else { Write-Warn "$label : 元の TealTasks\tasks.json がありません" }
        $pp = $prefs.apps | Where-Object { $_.id -eq "TealPlan" } | Select-Object -First 1
        if ($pp -and $pp.hotkey -eq "Ctrl+Alt+T" -and -not ($prefs.apps | Where-Object { $_.id -eq "TealTasks" })) { Write-Ok "$label : Hub の設定 (ショートカット Ctrl+Alt+T) が TealPlan に付け替えられた" } else { Write-Bad "$label : Hub の設定が TealPlan に付け替えられていません: $(Get-Content -Raw -Encoding UTF8 (Join-Path $data 'TealHub\app-settings.json'))" }
        if ($rk -match "TealSuite\.TealPlan\t.*apps\\TealPlan\\TealPlan\.exe" -and $rk -notmatch "TealSuite\.TealTasks\t") { Write-Ok "$label : スタートアップが TealSuite.TealPlan (TealPlan.exe) に登録し直された" } else { Write-Bad "$label : スタートアップの登録が引き継がれていません (TealPlan)" }
        $notices = Join-Path $data "TealHub\rename-notices.txt"
        if ((Test-Path $notices) -and ((Get-Content -Raw -Encoding UTF8 $notices) -match "TealTasks>TealPlan")) { Write-Ok "$label : Hub が「Teal Tasks は Teal Plan になりました」を知らせた (rename-notices.txt)" } else { Write-Bad "$label : Hub の「Teal Plan になりました」のお知らせの記録がありません" }
    }
    [void](Test-ErrorLogs $data "$label (アップデート後)")
}

Stop-Suite $suite
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host ""
if ($script:Failures -gt 0) { Write-Host "アップデートのチェック ($label): 失敗 $($script:Failures) 件"; exit 1 }
Write-Host "アップデートのチェック ($label → $expect): すべて ok (方法: $mode)"
exit 0
