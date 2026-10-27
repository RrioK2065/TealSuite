#!/bin/sh
# スモークテスト: ビルド済みの exe を参照して mono で実行する
#   tests/run_tests.sh            (先に ./build.sh を実行しておく)
#   tests/run_tests.sh seed <dir> (スクリーンショット用の見本データを作る)
set -e
# 自動のアップデート確認 (GitHub への問い合わせ) を止める。テストは FakeFetcher を使う
export TEALSUITE_NO_UPDATE_CHECK=1
cd "$(dirname "$0")/.."
D=dist/TealSuite
A=$D/apps
mkdir -p build
mcs -out:build/SmokeTests.exe -codepage:utf8 -langversion:5 -nowarn:1591 \
  -r:PM=$A/TealPass/TealPass.exe -r:AM=$D/TealHub.exe -r:MEMO=$A/TealMemo/TealMemo.exe \
  -r:CH=$A/TealClip/TealClip.exe -r:VM=$A/TealVolume/TealVolume.exe -r:UP=$D/TealUpdater.exe \
  -r:SUB=$A/TealSub/TealSub.exe -r:TASKS=$A/TealTasks/TealTasks.exe \
  -r:System.Security.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Runtime.Serialization.dll \
  tests/SmokeTests.cs tests/CHTests.cs tests/VMTests.cs tests/StartupTests.cs tests/HotkeyTests.cs tests/ShortcutTests.cs tests/UpdaterTests.cs tests/OnlineUpdateTests.cs tests/SubTasksTests.cs
# 参照先の exe を同じフォルダに置く (extern alias で読むため)
for f in $A/*/*.exe $D/TealHub.exe $D/TealUpdater.exe; do cp "$f" build/; done
mono build/SmokeTests.exe "$D" "$@"
