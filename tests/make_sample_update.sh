#!/bin/sh
# 開発用: 動作確認とスクリーンショット用に「次のバージョン」のサンプル アップデートを作る (配布物には入れない)
#   ./tests/make_sample_update.sh [作業フォルダ]   (既定: /tmp/tealsuite-next)
#   ・ソースを作業フォルダにコピーし、Teal Hub・Teal Pass・Teal Clip・TealUpdater を 1.5.0 に上げ、
#     新しいアプリ「Teal Note」(Teal Memo のコピー) を足してビルドする
#   ・今の dist/TealSuite (1.4.0) との差分で <作業フォルダ>/TealSuite-1.5.0.tealupdate を作る
#   先に ./build.sh で dist/TealSuite を作っておくこと
set -e
ROOT=$(cd "$(dirname "$0")/.." && pwd)
WORK=${1:-/tmp/tealsuite-next}
[ -f "$ROOT/dist/TealSuite/TealUpdater.exe" ] || { echo "先に ./build.sh を実行してください"; exit 1; }
rm -rf "$WORK"; mkdir -p "$WORK"
cp -r "$ROOT/apps.json" "$ROOT/build.sh" "$ROOT/src" "$ROOT/assets" "$ROOT/docs" "$ROOT/tools" "$WORK/"
cd "$WORK"
python3 - <<'PY'
import json, collections
m = json.load(open("apps.json", encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
m["suiteVersion"] = "1.5.0"
apps = m["apps"]
for a in apps:
    if a["id"] in ("TealHub", "TealPass", "TealClip"): a["version"] = "1.5.0"
memo = [i for i, a in enumerate(apps) if a["id"] == "TealMemo"][0]
apps.insert(memo + 1, collections.OrderedDict([
    ("id", "TealNote"), ("name", "Teal Note"), ("role", "ノート"), ("exe", "apps/TealNote/TealNote.exe"),
    ("version", "1.5.0"), ("description", "(サンプル) 見出しつきのノート"), ("icon", "apps/TealNote/TealNote.ico")]))
json.dump(m, open("apps.json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY
for id in TealHub TealPass TealClip TealUpdater; do
  sed -i 's/"1\.4\.0\.0"/"1.5.0.0"/g' "src/Apps/$id/AppMeta.cs"
done
cp -r src/Apps/TealMemo src/Apps/TealNote
sed -i 's/"1\.4\.0\.0"/"1.5.0.0"/g; s/Id = "TealMemo"/Id = "TealNote"/; s/Name = "Teal Memo"/Name = "Teal Note"/; s/LegacyIds = new\[\] { "Memo" },.*$//' src/Apps/TealNote/AppMeta.cs
cp assets/icons/TealMemo.ico assets/icons/TealNote.ico
cp assets/icons/TealMemo.png assets/icons/TealNote.png
./build.sh >/dev/null
# 変えていないアプリは 1.4.0 と同じファイルにする (差分に入らないように)
for id in TealMemo TealVolume; do cp "$ROOT/dist/TealSuite/apps/$id/"* "dist/TealSuite/apps/$id/"; done
cp "$ROOT/dist/TealSuite/"*.ico dist/TealSuite/
./build.sh package-update --from "$ROOT/dist/TealSuite" --out "$WORK/TealSuite-1.5.0.tealupdate" \
  --notes "Teal Pass と Teal Clip の改善、新しいアプリ「Teal Note」の追加 (サンプル)"
