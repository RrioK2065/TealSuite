#!/usr/bin/env python3
"""TealSuite のアイコンを生成する (Pillow が必要: pip install pillow)

すべてのアプリで共通の「角丸スクエア + ティールのグラデーション」背景に、
アプリごとの白いグリフを重ねる。新しいアプリを追加するときは GLYPHS に関数を足すだけ。
出力: assets/icons/<AppId>.ico (16/24/32/48/64/128/256) と <AppId>.png (256px プレビュー)
"""
import math, os, sys
from PIL import Image, ImageDraw, ImageChops

S = 1024                     # 作業キャンバス (高解像度で描いて縮小 = きれいなアンチエイリアス)
GRAD_TOP = (38, 166, 154)    # #26A69A
GRAD_BOT = (0, 105, 105)     # #006969  (中央付近が Theme.Accent #008080)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icons")
SIZES = [16, 24, 32, 48, 64, 128, 256]


def background():
    grad = Image.new("RGBA", (S, S))
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * (S - 1))           # 左上 → 右下
            px[x, y] = tuple(int(GRAD_TOP[i] + (GRAD_BOT[i] - GRAD_TOP[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (S, S), 0)
    m = int(S * 0.04)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m - 1, S - m - 1], radius=int(S * 0.22), fill=255)
    bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bg.paste(grad, (0, 0), mask)
    # ごく薄い上部ハイライト
    hl = Image.new("L", (S, S), 0)
    hd = ImageDraw.Draw(hl)
    for y in range(S // 2):                      # 上から下へ消えていく柔らかいハイライト
        hd.line([(0, y), (S, y)], fill=int(26 * (1 - y / (S / 2)) ** 1.5))
    hl = ImageChops.multiply(hl, mask)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    bg = Image.composite(white, bg, hl).copy()
    bg.putalpha(mask)
    return bg


def arc_band(d, cx, cy, r, w, a0, a1, fill):
    d.arc([cx - r, cy - r, cx + r, cy + r], a0, a1, fill=fill, width=w)
    # 端を丸める
    for a in (a0, a1):
        rad = math.radians(a)
        rm = r - w / 2
        x, y = cx + rm * math.cos(rad), cy + rm * math.sin(rad)
        d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=fill)


# ---- グリフ: L モードのマスクに 255=白 / 0=切り抜き で描く ----
def g_appmanager(d):
    s, gap, x0 = 196, 48, 512 - 196 - 24
    for i in range(2):
        for j in range(2):
            x, y = x0 + i * (s + gap), x0 + j * (s + gap)
            d.rounded_rectangle([x, y, x + s, y + s], radius=52, fill=255)
    # 右下は「＋」(アプリを追加できる)
    x, y = x0 + s + gap, x0 + s + gap
    d.rounded_rectangle([x, y, x + s, y + s], radius=52, fill=0)
    d.rounded_rectangle([x + 16, y + 16, x + s - 16, y + s - 16], radius=40, fill=255)
    d.rounded_rectangle([x + 40, y + 40, x + s - 40, y + s - 40], radius=26, fill=0)
    c = x + s // 2
    d.rounded_rectangle([c - 14, y + 62, c + 14, y + s - 62], radius=14, fill=255)
    d.rounded_rectangle([x + 62, c - 14, x + s - 62, c + 14], radius=14, fill=255)


def g_password(d):
    # 南京錠 + ●●●
    arc_band(d, 512, 430, 150, 64, 180, 360, 255)
    d.rectangle([362 - 0, 430, 362 + 64, 500], fill=255)
    d.rectangle([662 - 64, 430, 662, 500], fill=255)
    d.rounded_rectangle([270, 470, 754, 790], radius=70, fill=255)
    for i in range(3):
        cx = 512 + (i - 1) * 118
        d.ellipse([cx - 42, 630 - 42, cx + 42, 630 + 42], fill=0)


def g_memo(d):
    d.rounded_rectangle([300, 230, 724, 794], radius=60, fill=255)
    # 折り返した角 (右下を切り落として、少し離して三角を置く)
    d.polygon([(724, 664), (724, 800), (588, 800)], fill=0)
    d.polygon([(732, 664), (588, 808), (588, 664)], fill=255, outline=0, width=22)
    for i, w in enumerate([300, 300, 220, 150]):
        y = 350 + i * 90
        if y > 610:
            break
        d.rounded_rectangle([370, y, 370 + w, y + 36], radius=18, fill=0)


def g_clipboard(d):
    d.rounded_rectangle([300, 270, 724, 810], radius=64, fill=255)
    # クリップ
    d.rounded_rectangle([400, 206, 624, 330], radius=40, fill=0)
    d.rounded_rectangle([420, 226, 604, 310], radius=28, fill=255)
    d.rounded_rectangle([462, 246, 562, 282], radius=18, fill=0)
    # 履歴 (番号つきの行)
    for i in range(3):
        y = 420 + i * 112
        d.ellipse([370, y, 426, y + 56], fill=0)
        d.rounded_rectangle([456, y + 10, 654 - (i * 50), y + 46], radius=18, fill=0)


def g_volume(d):
    d.rounded_rectangle([250, 418, 380, 606], radius=26, fill=255)
    d.polygon([(350, 430), (520, 290), (520, 734), (350, 594)], fill=255)
    d.rounded_rectangle([470, 290, 540, 734], radius=34, fill=255)
    arc_band(d, 540, 512, 130, 60, -48, 48, 255)
    arc_band(d, 540, 512, 250, 60, -50, 50, 255)


def g_updater(d):
    # 回る矢印 (更新) + 中央の下向き矢印 (取り込む)
    arc_band(d, 512, 512, 300, 84, 200, 500, 255)
    # 矢じり (左上の端から下へ向かう)
    d.polygon([(189, 540), (130, 373), (330, 445)], fill=255)
    d.rounded_rectangle([474, 330, 550, 600], radius=30, fill=255)
    d.polygon([(400, 560), (624, 560), (512, 690)], fill=255)


GLYPHS = {
    "TealHub": g_appmanager,
    "TealPass": g_password,
    "TealMemo": g_memo,
    "TealClip": g_clipboard,
    "TealVolume": g_volume,
    "TealUpdater": g_updater,
}


def write_ico(path, imgs):
    """ICO を自前で書く: 256px は PNG、それ未満は 32bit BMP (古い API や .NET の Icon クラスでも確実に読める形式)"""
    import io, struct
    blobs = []
    for im in imgs:
        n = im.size[0]
        if n >= 256:
            b = io.BytesIO(); im.save(b, "PNG"); blobs.append(b.getvalue()); continue
        px = im.convert("RGBA").tobytes("raw", "BGRA")
        rows = [px[y * n * 4:(y + 1) * n * 4] for y in range(n)][::-1]          # bottom-up
        mask_row = ((n + 31) // 32) * 4
        hdr = struct.pack("<IiiHHIIiiII", 40, n, n * 2, 1, 32, 0, n * n * 4 + mask_row * n, 0, 0, 0, 0)
        blobs.append(hdr + b"".join(rows) + b"\x00" * (mask_row * n))
    out = struct.pack("<HHH", 0, 1, len(imgs))
    off = 6 + 16 * len(imgs)
    for im, blob in zip(imgs, blobs):
        n = im.size[0]
        out += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(blob), off)
        off += len(blob)
    with open(path, "wb") as f:
        f.write(out + b"".join(blobs))


def make(app_id, fn):
    bg = background()
    mask = Image.new("L", (S, S), 0)
    fn(ImageDraw.Draw(mask))
    white = Image.new("RGBA", (S, S), (255, 255, 255, 255))
    img = Image.composite(white, bg, mask)
    img.putalpha(bg.getchannel("A"))
    imgs = [img.resize((n, n), Image.LANCZOS) for n in SIZES]
    os.makedirs(OUT, exist_ok=True)
    imgs[-1].save(os.path.join(OUT, app_id + ".png"))
    write_ico(os.path.join(OUT, app_id + ".ico"), imgs)
    return imgs[-1]


if __name__ == "__main__":
    ids = sys.argv[1:] or list(GLYPHS)
    done = [make(i, GLYPHS[i]) for i in ids]
    # 一覧プレビュー
    sheet = Image.new("RGBA", (len(done) * 288 + 32, 320), (243, 247, 247, 255))
    for k, im in enumerate(done):
        sheet.paste(im, (32 + k * 288, 32), im)
    sheet.save(os.path.join(OUT, "_preview_all.png"))
    small = Image.new("RGBA", (len(ids) * 120 + 16, 64), (255, 255, 255, 255))
    for k, i in enumerate(ids):
        x = 16 + k * 120
        for n in (16, 32, 48):
            ico = Image.open(os.path.join(OUT, i + ".ico")); ico.size = (n, n)
            ico = ico.convert("RGBA")
            small.paste(ico, (x, 8 + (48 - n) // 2), ico); x += n + 8
    small.save(os.path.join(OUT, "_preview_small.png"))
    print("generated:", ", ".join(ids))
