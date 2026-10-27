#!/usr/bin/env python3
"""TealSuite のアップデート ファイル (.tealupdate) を作る。

  ./build.sh                       # まず dist/TealSuite をビルド
  ./build.sh package-update        # 全ファイル入りのアップデート (full)
  ./build.sh package-update --from 前のバージョンの TealSuite.zip か dist フォルダ
                                   # 変わったファイルだけのアップデート (diff)。消えたファイルは delete に入る

  オプション:
    --dist DIR        元にするビルド結果 (既定: dist/TealSuite。build.sh が指定する)
    --from PATH       前のバージョン (TealSuite.zip か、展開したフォルダ)。指定すると差分だけ入れる
    --version X.Y.Z   アップデート後のバージョン (既定: dist の apps.json の suiteVersion)
    --min-from X.Y.Z  これより古いバージョンには入れられないようにする
                      (既定: --from のバージョン。なければ 1.4.0 = TealUpdater が入った最初のバージョン)
    --all             --from と一緒に使う: 全ファイルを入れたまま、前のバージョンで消えたファイルだけ delete に入れる
                      (リリース用。--min-from の既定は 1.4.0 のまま = 1.4.0 以降ならどのバージョンからでも入れられる)
    --notes TEXT      確認画面に出す説明 (変更点など)
    --out FILE        出力先 (既定: dist/TealSuite-<version>.tealupdate)

  形式は DEVELOPING.md の「アップデート ファイル」を参照。
"""
import argparse, datetime, hashlib, io, json, os, sys, zipfile

FORMAT = 1
FIRST_UPDATER_VERSION = "1.4.0"
SKIP_TOP = {"backup", ".update-staging"}
FIXED_TIME = (2020, 1, 1, 0, 0, 0)


def die(msg):
    print("  ! " + msg, file=sys.stderr)
    sys.exit(1)


def vkey(v):
    return tuple(int(p) for p in v.split("."))


def skip(rel):
    parts = rel.split("/")
    return rel.lower() == "apps.json" or parts[0] in SKIP_TOP or rel.endswith(".tmp") or rel.endswith(".tealupdate")


def read_tree(path):
    """{相対パス: bytes} と apps.json (dict)。path はフォルダか、TealSuite.zip"""
    files, manifest = {}, None
    if os.path.isdir(path):
        for dp, _, names in os.walk(path):
            for n in names:
                full = os.path.join(dp, n)
                rel = os.path.relpath(full, path).replace(os.sep, "/")
                with open(full, "rb") as f:
                    data = f.read()
                if rel.lower() == "apps.json":
                    manifest = json.loads(data.decode("utf-8-sig"))
                elif not skip(rel):
                    files[rel] = data
    elif zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as z:
            names = [n for n in z.namelist() if not n.endswith("/")]
            # TealSuite.zip は TealSuite/ フォルダごと入っている。apps.json のある階層を根にする
            roots = [n[: -len("apps.json")] for n in names if n.endswith("apps.json") and n.count("/") <= 1]
            if not roots:
                die(path + " の中に apps.json がありません")
            root = min(roots, key=len)
            for n in names:
                if not n.startswith(root):
                    continue
                rel = n[len(root):]
                data = z.read(n)
                if rel.lower() == "apps.json":
                    manifest = json.loads(data.decode("utf-8-sig"))
                elif not skip(rel):
                    files[rel] = data
    else:
        die(path + " はフォルダでも zip でもありません")
    if manifest is None:
        die(path + " に apps.json がありません")
    return files, manifest


def suite_version(m):
    if m.get("suiteVersion"):
        return m["suiteVersion"]
    for a in m.get("apps", []):
        if a.get("self") or a.get("id") == "TealHub":
            return a.get("version")
    return None


def app_of(rel, apps):
    parts = rel.split("/")
    if parts[0] == "apps" and len(parts) >= 3:
        return parts[1]
    stem = os.path.splitext(parts[-1])[0]
    if len(parts) == 1 and any(a.get("id") == stem for a in apps):
        return stem
    return ""


def main():
    ap = argparse.ArgumentParser(description="TealSuite のアップデート ファイルを作る")
    ap.add_argument("--dist", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "dist", "TealSuite"))
    ap.add_argument("--from", dest="old")
    ap.add_argument("--version")
    ap.add_argument("--min-from")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--notes", default="")
    ap.add_argument("--out")
    a = ap.parse_args()

    dist = os.path.abspath(a.dist)
    if not os.path.isdir(dist):
        die(dist + " がありません。先に ./build.sh を実行してください")
    files, manifest = read_tree(dist)
    apps = manifest.get("apps", [])
    version = a.version or suite_version(manifest)
    if not version:
        die("バージョンがわかりません (--version を指定してください)")
    if "TealUpdater.exe" not in files:
        die("dist に TealUpdater.exe がありません")

    kind, deletes, include_apps = "full", [], list(apps)
    min_from = a.min_from
    if a.old:
        old_files, old_manifest = read_tree(os.path.abspath(a.old))
        old_ver = suite_version(old_manifest) or "0.0.0"
        if vkey(old_ver) > vkey(version):
            die("--from (%s) のほうが新しいバージョンです (%s)" % (old_ver, version))
        if a.all:
            deletes = sorted(p for p in old_files if p not in files)
            if not files:
                die("dist が空です")
        else:
          kind = "diff"
          min_from = min_from or old_ver
          changed = {p: d for p, d in files.items() if old_files.get(p) != d}
          deletes = sorted(p for p in old_files if p not in files)
          old_apps = {x.get("id"): x for x in old_manifest.get("apps", [])}
          touched = {app_of(p, apps) for p in changed}
          include_apps = [x for x in apps if x.get("id") in touched or old_apps.get(x.get("id")) != x]
          files = changed
          if not files and not deletes and not include_apps:
              die("前のバージョンと同じです (変わったファイルがありません)")
    min_from = min_from or FIRST_UPDATER_VERSION

    entries = []
    for rel in sorted(files):
        data = files[rel]
        entries.append({"path": rel, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data), "app": app_of(rel, apps)})
    upd = {
        "format": FORMAT,
        "suite": manifest.get("suite", "TealSuite"),
        "version": version,
        "minFromVersion": min_from,
        "created": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "kind": kind,
        "notes": a.notes,
        "files": entries,
        "delete": deletes,
        "apps": include_apps,
    }
    out = a.out or os.path.join(os.path.dirname(dist), "%s-%s.tealupdate" % (upd["suite"], version))
    tmp = out + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        zi = zipfile.ZipInfo("update.json", FIXED_TIME); zi.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(zi, json.dumps(upd, ensure_ascii=False, indent=2) + "\n")
        for e in entries:
            zi = zipfile.ZipInfo("files/" + e["path"], FIXED_TIME); zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, files[e["path"]])
    os.replace(tmp, out)
    print("アップデート ファイルを作りました: %s" % out)
    print("  %s %s (%s, %s 以降に適用)  ファイル %d 個  削除 %d 個  アプリ情報 %d 件"
          % (upd["suite"], version, kind, min_from, len(entries), len(deletes), len(include_apps)))


if __name__ == "__main__":
    main()
