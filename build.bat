@echo off
rem ===================================================================
rem  TealSuite build script for Windows
rem  Uses the C# compiler that ships with .NET Framework 4.x (no install).
rem    build.bat            build all apps
rem    build.bat TealMemo      build only the given app
rem    (update packages: build.sh package-update, needs python3; see DEVELOPING.md)
rem  Output: dist\TealSuite\  (TealHub.exe, TealUpdater.exe, apps.json, README.txt, apps\<AppId>\<AppId>.exe)
rem  Note: keep REFS identical to COMMON_REFS in build.sh.
rem ===================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo csc.exe not found. .NET Framework 4.x is required. & exit /b 1 )
set "OUT=dist\TealSuite"
set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll"
set FAILED=0
set BUILT=0
if not exist "%OUT%\apps" mkdir "%OUT%\apps"

for /d %%D in (src\Apps\*) do (
  set "ID=%%~nxD"
  set "SKIP=0"
  if not "%~1"=="" (
    set "SKIP=1"
    for %%A in (%*) do if /i "%%A"=="!ID!" set "SKIP=0"
  )
  if "!SKIP!"=="0" call :build "!ID!"
)
copy /y apps.json "%OUT%\apps.json" >nul
if exist docs\README.txt copy /y docs\README.txt "%OUT%\README.txt" >nul
if not "%FAILED%"=="0" ( echo. & echo BUILD FAILED & exit /b 1 )
echo. & echo Done: %BUILT% app^(s^) built into %OUT%
exit /b 0

:build
set "ID=%~1"
set "EXTRA="
if exist "src\Apps\%ID%\refs.txt" (
  for /f "usebackq eol=# tokens=*" %%R in ("src\Apps\%ID%\refs.txt") do set "EXTRA=!EXTRA! /r:%%R"
)
set "DEST=%OUT%\apps\%ID%"
rem TealHub and TealUpdater live in the suite root
if /i "%ID%"=="TealHub" set "DEST=%OUT%"
if /i "%ID%"=="TealUpdater" set "DEST=%OUT%"
if not exist "%DEST%" mkdir "%DEST%"
echo == %ID%
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 %REFS% !EXTRA! /win32icon:"assets\icons\%ID%.ico" /resource:assets\icons\%ID%.ico,AppIcon.ico /resource:assets\icons\%ID%.png,AppIcon.png /out:"%DEST%\%ID%.exe" "src\Common\*.cs" "src\Apps\%ID%\*.cs"
if errorlevel 1 ( set FAILED=1 & exit /b 1 )
copy /y "assets\icons\%ID%.ico" "%DEST%\" >nul
set /a BUILT+=1
exit /b 0
