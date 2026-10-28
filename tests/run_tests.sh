#!/bin/sh
# スモークテスト: ビルド済みの exe を参照して mono で実行する
#   tests/run_tests.sh            (先に ./build.sh を実行しておく)
#   tests/run_tests.sh seed <dir> (スクリーンショット用の見本データを作る)
#   参照する exe・DLL とソースの一覧は tests/test-build.txt (Windows の build.ps1 -Tests と共通)
set -e
# 自動のアップデート確認 (GitHub への問い合わせ) を止める。テストは FakeFetcher を使う
export TEALSUITE_NO_UPDATE_CHECK=1
cd "$(dirname "$0")/.."
D=dist/TealSuite
mkdir -p build
ARGS=$(tr -d '\r' < tests/test-build.txt | awk -v D="$D" '
  /^[[:space:]]*(#|$)/ { next }
  $1 == "alias" { printf "-r:%s=%s/%s ", $2, D, $3; next }
  $1 == "ref"   { printf "-r:%s ", $2; next }
  $1 == "src"   { printf "%s ", $2; next }')
# shellcheck disable=SC2086
mcs -out:build/SmokeTests.exe -codepage:utf8 -langversion:5 -nowarn:1591 $ARGS
# 参照先の exe を同じフォルダに置く (extern alias で読むため)
for f in $D/apps/*/*.exe $D/TealHub.exe $D/TealUpdater.exe; do cp "$f" build/; done
mono build/SmokeTests.exe "$D" "$@"
