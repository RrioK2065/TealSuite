#!/bin/sh
# Xvfb (:99) 上で exe を mono で起動し、数秒後に画面を撮って終了させる (テスト用)
#   [SHOT_ARGS="/show"] tests/shot.sh <exe> <out.png> [待ち秒数=4]
EXE="$1"; OUT="$2"; WAIT="${3:-4}"
export DISPLAY=:99
mono "$EXE" $SHOT_ARGS > /tmp/shot_app.log 2>&1 &
PID=$!
sleep "$WAIT"
python3 - "$OUT" <<'PY'
import sys
from PIL import ImageGrab, Image
im = ImageGrab.grab(xdisplay=':99').convert('RGB')
bbox = im.point(lambda v: 255 if v > 8 else 0).getbbox()
if bbox: im = im.crop(bbox)
im.save(sys.argv[1]); print("saved", sys.argv[1], im.size)
PY
kill $PID 2>/dev/null; sleep 0.5; kill -9 $PID 2>/dev/null
true
