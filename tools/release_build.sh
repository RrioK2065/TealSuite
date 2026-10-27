#!/usr/bin/env bash
# TealSuite のリリース用ファイルを作る (GitHub Actions の release.yml と、手元での確認の両方で使う)
#
#   tools/release_build.sh [--prev 前のリリースの TealSuite-x.y.z.zip] [--out 出力フォルダ] [--skip-build]
#
#   出力 (既定: release/):
#     TealSuite-<ver>.zip                     … 全部入り (初めて入れる人向け。展開して TealHub.exe)
#     TealSuite-update-<ver>.tealupdate       … アップデート ファイル (Teal Hub が自動で見つけてダウンロードする)
#     *.sha256                                 … sha256sum 形式 (「<hex>  <ファイル名>」)
#     notes.md                                 … CHANGELOG.md の <ver> の節 + 確認用の sha256
#
#   アップデート ファイルは「全ファイル入り (kind=full) + 前のリリースで消えたファイルの削除」。
#   1.4.0 以降ならどのバージョンからでも 1 回で入れられる (途中のバージョンを飛ばしても大丈夫)。
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PREV=""; OUT="$ROOT/release"; BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --prev) PREV="$2"; shift 2;;
    --out) OUT="$2"; shift 2;;
    --skip-build) BUILD=0; shift;;
    *) echo "不明なオプション: $1"; exit 2;;
  esac
done
VER=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8-sig"))["suiteVersion"])' "$ROOT/apps.json")
echo "TealSuite $VER のリリース ファイルを作ります -> $OUT"
[ "$BUILD" = 1 ] && "$ROOT/build.sh"
DIST="$ROOT/dist/TealSuite"
[ -f "$DIST/TealHub.exe" ] || { echo "dist がありません"; exit 1; }
mkdir -p "$OUT"
ZIP="TealSuite-$VER.zip"; UPD="TealSuite-update-$VER.tealupdate"
rm -f "$OUT/$ZIP" "$OUT/$UPD" "$OUT/$ZIP.sha256" "$OUT/$UPD.sha256" "$OUT/notes.md"

# 1. 全部入りの zip (TealSuite/ フォルダごと)
python3 - "$ROOT/dist" "$OUT/$ZIP" <<'PY'
import os, sys, zipfile
base, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for d, dirs, files in os.walk(os.path.join(base, "TealSuite")):
        dirs.sort()
        rel = os.path.relpath(d, base)
        z.write(d, rel + "/")
        for f in sorted(files):
            z.write(os.path.join(d, f), os.path.join(rel, f))
PY

# 2. CHANGELOG.md から <ver> の節を取り出す
SECTION=$(python3 - "$ROOT/CHANGELOG.md" "$VER" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read().replace("\r", "")
ver = sys.argv[2]
out, on = [], False
for line in text.split("\n"):
    if line.startswith("## "):
        if on: break
        on = re.match(r"^##\s*\[?v?" + re.escape(ver) + r"\]?(\s|$)", line) is not None
        continue
    if on: out.append(line)
print("\n".join(out).strip())
PY
)
[ -n "$SECTION" ] || { echo "CHANGELOG.md に「## $VER」の節がありません"; exit 1; }

# 3. アップデート ファイル (確認画面の説明には CHANGELOG の節をそのまま入れる)
ARGS=(--version "$VER" --notes "$SECTION" --out "$OUT/$UPD")
if [ -n "$PREV" ]; then ARGS+=(--from "$PREV" --all); fi
python3 "$ROOT/tools/package_update.py" --dist "$DIST" "${ARGS[@]}"

# 4. sha256 (Teal Hub はダウンロード後にこの値と照らし合わせる)
( cd "$OUT" && sha256sum "$ZIP" > "$ZIP.sha256" && sha256sum "$UPD" > "$UPD.sha256" )

# 5. リリースの説明文
{
  echo "$SECTION"
  echo
  echo "### ダウンロード"
  echo
  echo "- はじめて使う: **$ZIP** を展開して TealHub.exe を開きます。"
  echo "- 1.5.0 以降を使っている: Teal Hub が自動でお知らせします (「アップデート ▾」→「更新を確認」でもすぐ確認できます)。"
  echo "- 1.4.0 を使っている: **$UPD** をダウンロードしてダブルクリック (または Teal Hub の「アップデート ▾」→「アップデート ファイルを開く…」)。"
  echo
  echo "### 確認用 (sha256)"
  echo
  echo '```'
  cat "$OUT/$ZIP.sha256" "$OUT/$UPD.sha256"
  echo '```'
} > "$OUT/notes.md"
echo "できました:"
ls -l "$OUT"
