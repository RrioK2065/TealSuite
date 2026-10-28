<#
  TealSuite ビルド スクリプト (Windows / PowerShell。Windows に入っている .NET Framework の csc を使う)
  Linux / macOS の ./build.sh (mono の mcs) と同じソース・参照・出力の形でビルドする。
    ソース: src/Common/*.cs + src/Apps/<id>/*.cs
    参照  : tools/common-refs.txt (全アプリ共通) + src/Apps/<id>/refs.txt (アプリごと)
    テスト: tests/test-build.txt (tests/run_tests.sh と共通)

  使い方:
    powershell -ExecutionPolicy Bypass -File build.ps1                     … 全アプリを dist\TealSuite に
    powershell -ExecutionPolicy Bypass -File build.ps1 -Apps TealMemo      … 指定したアプリだけ
    powershell -ExecutionPolicy Bypass -File build.ps1 -Out dist-win\TealSuite -Tests
                                         … 別の場所にビルドし、テスト (build\SmokeTests.exe) も作る
    powershell -ExecutionPolicy Bypass -File build.ps1 -TestsOnly -Out dist\TealSuite
                                         … アプリは作らず、そのフォルダの exe を使ってテストだけ作る
  -Csc で csc.exe の場所を指定できる (省略時: .NET Framework 4 の csc → 見つからなければ Visual Studio の Roslyn)
  実行は tests\run_tests.ps1 (GitHub Actions の Windows のチェックもこれを使う: DEVELOPING.md)
#>
param(
    [string[]]$Apps = @(),
    [string]$Out = "",
    [string]$Csc = "",
    [string]$TestsDir = "",
    [switch]$Tests,
    [switch]$TestsOnly
)
$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = Join-Path $Root "dist\TealSuite" }
if (-not [System.IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location) $Out }
if (-not $TestsDir) { $TestsDir = Join-Path $Root "build" }
if (-not [System.IO.Path]::IsPathRooted($TestsDir)) { $TestsDir = Join-Path (Get-Location) $TestsDir }

function Fail([string]$msg) { Write-Host "  ! $msg"; exit 1 }

# --- csc を探す ---
$roslyn = $false
if (-not $Csc) {
    $fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $fw)) { $fw = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
    if (Test-Path $fw) { $Csc = $fw }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $found = & $vswhere -latest -products * -find "MSBuild\**\Roslyn\csc.exe" | Select-Object -First 1
            if ($found) { $Csc = $found }
        }
    }
}
if (-not $Csc -or -not (Test-Path $Csc)) { Fail "csc.exe が見つかりません (.NET Framework 4.x が必要です。-Csc で指定もできます)" }
# Roslyn (Visual Studio 付属) なら C# 5 に揃える。.NET Framework の csc はもともと C# 5
#   (.NET Framework の csc は「only supports language versions up to C# 5」と表示する)
$ver = ""
try { $ver = (& $Csc /help | Out-String) } catch { }
if ($ver -notmatch "up to C# 5") { $roslyn = $true }
Write-Host "csc: $Csc$(if ($roslyn) { ' (Roslyn, /langversion:5)' } else { '' })"

function Read-List([string]$path) {
    if (-not (Test-Path $path)) { return @() }
    return @(Get-Content -Encoding UTF8 $path | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") })
}

function Invoke-Csc([string[]]$argv, [string]$what) {
    # 引数が長いので応答ファイルで渡す (パスに空白があっても大丈夫なように引用符で囲む)
    $rsp = [System.IO.Path]::GetTempFileName()
    $lines = $argv | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^"') { '"' + $_ + '"' } else { $_ } }
    [System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))
    & $Csc "@$rsp"
    $code = $LASTEXITCODE
    Remove-Item $rsp -ErrorAction SilentlyContinue
    if ($code -ne 0) { Fail "$what のビルドに失敗しました (csc の終了コード $code)" }
}

$flags = @("/noconfig", "/nologo", "/optimize+", "/codepage:65001", "/nowarn:1591", "/utf8output")
if ($roslyn) { $flags += "/langversion:5" }

if (-not $TestsOnly) {
    # --- apps.json のバージョンと AppMeta.cs の AssemblyVersion が揃っているか (build.sh と同じ確認) ---
    $manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $Root "apps.json") | ConvertFrom-Json
    $bad = $false
    foreach ($a in $manifest.apps) {
        if ($Apps.Count -gt 0 -and $Apps -notcontains $a.id) { continue }
        $meta = Join-Path $Root "src\Apps\$($a.id)\AppMeta.cs"
        if (-not (Test-Path $meta)) { Write-Host "  ! apps.json の $($a.id) に対応するソース (src/Apps/$($a.id)) がありません"; $bad = $true; continue }
        $m = [regex]::Match((Get-Content -Raw -Encoding UTF8 $meta), 'AssemblyVersion\("(\d+\.\d+\.\d+)')
        if (-not $m.Success -or $m.Groups[1].Value -ne $a.version) { Write-Host "  ! $($a.id): apps.json は $($a.version)、AppMeta.cs は $($m.Groups[1].Value) です。揃えてください"; $bad = $true }
    }
    if (-not $manifest.suiteVersion) { Write-Host "  ! apps.json に suiteVersion がありません"; $bad = $true }
    elseif ($Apps.Count -eq 0 -or $Apps -contains "TealUpdater") {
        $m = [regex]::Match((Get-Content -Raw -Encoding UTF8 (Join-Path $Root "src\Apps\TealUpdater\AppMeta.cs")), 'AssemblyVersion\("(\d+\.\d+\.\d+)')
        if ($m.Groups[1].Value -ne $manifest.suiteVersion) { Write-Host "  ! TealUpdater: apps.json の suiteVersion は $($manifest.suiteVersion)、AppMeta.cs は $($m.Groups[1].Value) です。揃えてください"; $bad = $true }
    }
    if ($bad) { exit 1 }

    # --- アプリ ---
    $commonRefs = Read-List (Join-Path $Root "tools\common-refs.txt") | ForEach-Object { "/reference:$_" }
    $commonSrc = @(Get-ChildItem (Join-Path $Root "src\Common") -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
    New-Item -ItemType Directory -Force (Join-Path $Out "apps") | Out-Null
    $built = 0
    foreach ($dir in Get-ChildItem (Join-Path $Root "src\Apps") -Directory | Sort-Object Name) {
        $id = $dir.Name
        if ($Apps.Count -gt 0 -and $Apps -notcontains $id) { continue }
        $refs = Read-List (Join-Path $dir.FullName "refs.txt") | ForEach-Object { "/reference:$_" }
        # Teal Hub と TealUpdater はスイートのフォルダの直下、ほかは apps\<id>\
        if ($id -eq "TealHub" -or $id -eq "TealUpdater") { $dest = $Out } else { $dest = Join-Path $Out "apps\$id" }
        New-Item -ItemType Directory -Force $dest | Out-Null
        $icon = Join-Path $Root "assets\icons\$id.ico"
        $png = Join-Path $Root "assets\icons\$id.png"
        if (-not (Test-Path $icon)) { Fail "アイコンがありません: $icon" }
        Write-Host "== $id"
        $src = @(Get-ChildItem $dir.FullName -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
        $argv = $flags + @("/target:winexe") + $commonRefs + $refs + @("/win32icon:$icon", "/resource:$icon,AppIcon.ico", "/resource:$png,AppIcon.png", "/out:$(Join-Path $dest "$id.exe")") + $commonSrc + $src
        Invoke-Csc $argv $id
        Copy-Item $icon $dest -Force
        $built++
    }
    Copy-Item (Join-Path $Root "apps.json") (Join-Path $Out "apps.json") -Force
    Copy-Item (Join-Path $Root "docs\README.txt") (Join-Path $Out "README.txt") -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $Root "CHANGELOG.md") (Join-Path $Out "CHANGELOG.md") -Force -ErrorAction SilentlyContinue
    Write-Host "ビルド完了: $built 個 -> $Out"
}

if ($Tests -or $TestsOnly) {
    # --- テスト (tests/test-build.txt。tests/run_tests.sh と同じ) ---
    New-Item -ItemType Directory -Force $TestsDir | Out-Null
    $targs = @()
    foreach ($line in Read-List (Join-Path $Root "tests\test-build.txt")) {
        $p = $line -split '\s+'
        switch ($p[0]) {
            "alias" { $targs += "/reference:$($p[1])=$(Join-Path $Out ($p[2] -replace '/', '\'))" }
            "ref"   { $targs += "/reference:$($p[1])" }
            "src"   { $targs += (Join-Path $Root ($p[1] -replace '/', '\')) }
        }
    }
    Write-Host "== tests (SmokeTests.exe)"
    Invoke-Csc ($flags + @("/target:exe", "/out:$(Join-Path $TestsDir 'SmokeTests.exe')") + $targs) "SmokeTests"
    Write-Host "== tests (Sleeper.exe)"
    Invoke-Csc ($flags + @("/target:winexe", "/reference:System.dll", "/out:$(Join-Path $TestsDir 'Sleeper.exe')", (Join-Path $Root "tests\win\Sleeper.cs"))) "Sleeper"
    Write-Host "== tests (PinTarget.exe)"
    Invoke-Csc ($flags + @("/target:winexe", "/reference:System.dll", "/reference:System.Drawing.dll", "/reference:System.Windows.Forms.dll", "/out:$(Join-Path $TestsDir 'PinTarget.exe')", (Join-Path $Root "tests\win\PinTarget.cs"))) "PinTarget"
    Write-Host "テストのビルド完了 -> $TestsDir (実行: tests\run_tests.ps1 -Dist `"$Out`")"
}
