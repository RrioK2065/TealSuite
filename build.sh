#!/bin/sh
# TealSuite ビルドスクリプト (Linux / macOS, mono の mcs を使用)
#   使い方:  ./build.sh            … 全アプリをビルド
#            ./build.sh TealMemo      … 指定したアプリだけ
#            ./build.sh package-update [--from 前のdistかzip] [--out ファイル] [--notes 文]
#                                     … dist/TealSuite からアップデート ファイル (.tealupdate) を作る (DEVELOPING.md)
#   必要なもの: mono-devel (mcs)。アイコンを作り直すときは python3 + pillow (tools/make_icons.py)
#   出力: dist/TealSuite/  (TealHub.exe, TealUpdater.exe, apps.json, README.txt, apps/<AppId>/<AppId>.exe)
set -e
ROOT=$(cd "$(dirname "$0")" && pwd)
if [ "$1" = "package-update" ]; then
  shift
  command -v python3 >/dev/null 2>&1 || { echo "python3 が必要です"; exit 1; }
  exec python3 "$ROOT/tools/package_update.py" --dist "$ROOT/dist/TealSuite" "$@"
fi
OUT="$ROOT/dist/TealSuite"
# 全アプリ共通の参照。build.bat の REFS と同じにしておくこと
COMMON_REFS="-r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Runtime.Serialization.dll -r:System.Xml.dll"
# -langversion:5 … Windows 標準の csc (C# 5) でもビルドできる書き方に限定する
MCS_FLAGS="-target:winexe -codepage:utf8 -optimize+ -sdk:4.5 -langversion:5 -nowarn:1591"

command -v mcs >/dev/null 2>&1 || { echo "mcs が見つかりません (sudo apt install mono-devel)"; exit 1; }

# apps.json のバージョンとソースの AssemblyVersion が一致しているか確認
if command -v python3 >/dev/null 2>&1; then
  python3 - "$ROOT" "$@" <<'PY'
import json, re, sys, os
root = sys.argv[1]
only = set(sys.argv[2:])
m = json.load(open(os.path.join(root, "apps.json"), encoding="utf-8-sig"))
bad = 0
for a in m["apps"]:
    if only and a["id"] not in only: continue
    meta = os.path.join(root, "src", "Apps", a["id"], "AppMeta.cs")
    if not os.path.exists(meta):
        print("  ! apps.json の %s に対応するソース (src/Apps/%s) がありません" % (a["id"], a["id"])); bad = 1; continue
    v = re.search(r'AssemblyVersion\("(\d+\.\d+\.\d+)', open(meta, encoding="utf-8").read())
    if not v or v.group(1) != a["version"]:
        print("  ! %s: apps.json は %s、AppMeta.cs は %s です。揃えてください" % (a["id"], a["version"], v.group(1) if v else "?")); bad = 1
# TealUpdater は apps.json に載せない (Teal Hub の一覧に出さない) が、バージョンは suiteVersion と揃える
suite = m.get("suiteVersion")
if not suite:
    print("  ! apps.json に suiteVersion がありません"); bad = 1
elif not only or "TealUpdater" in only:
    meta = os.path.join(root, "src", "Apps", "TealUpdater", "AppMeta.cs")
    v = re.search(r'AssemblyVersion\("(\d+\.\d+\.\d+)', open(meta, encoding="utf-8").read()) if os.path.exists(meta) else None
    if not v or v.group(1) != suite:
        print("  ! TealUpdater: apps.json の suiteVersion は %s、AppMeta.cs は %s です。揃えてください" % (suite, v.group(1) if v else "?")); bad = 1
sys.exit(bad)
PY
fi

mkdir -p "$OUT/apps"
built=0
for dir in "$ROOT"/src/Apps/*/; do
  id=$(basename "$dir")
  if [ $# -gt 0 ]; then case " $* " in *" $id "*) ;; *) continue;; esac; fi
  refs=""
  if [ -f "$dir/refs.txt" ]; then
    refs=$(grep -v '^[[:space:]]*#' "$dir/refs.txt" | tr -d '\r' | sed '/^[[:space:]]*$/d; s/^[[:space:]]*/-r:/' | tr '\n' ' ')
  fi
  # Teal Hub と TealUpdater はスイートのフォルダの直下、ほかは apps/<id>/
  case "$id" in TealHub|TealUpdater) dest="$OUT";; *) dest="$OUT/apps/$id";; esac
  mkdir -p "$dest"
  icon="$ROOT/assets/icons/$id.ico"
  png="$ROOT/assets/icons/$id.png"
  [ -f "$icon" ] || { echo "  ! アイコンがありません: $icon  (python3 tools/make_icons.py $id)"; exit 1; }
  echo "== $id"
  # shellcheck disable=SC2086
  mcs $MCS_FLAGS $COMMON_REFS $refs \
      "-win32icon:$icon" "-resource:$icon,AppIcon.ico" "-resource:$png,AppIcon.png" \
      "-out:$dest/$id.exe" "$ROOT"/src/Common/*.cs "$dir"*.cs
  cp "$icon" "$dest/"
  built=$((built + 1))
done

cp "$ROOT/apps.json" "$OUT/apps.json"
cp "$ROOT/docs/README.txt" "$OUT/README.txt" 2>/dev/null || true
cp "$ROOT/CHANGELOG.md" "$OUT/CHANGELOG.md" 2>/dev/null || true
echo "ビルド完了: $built 個 -> $OUT"
