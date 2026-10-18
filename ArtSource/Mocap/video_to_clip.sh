#!/usr/bin/env bash
# A video of the user performing a move -> a clip FBX in Animation/Clips/Custom.
#
#   # once, from the T-pose video (calibrates limb lengths for every take afterwards):
#   ArtSource/Mocap/video_to_clip.sh --tpose raw/tpose.mp4
#
#   # then per move (the extractor prints the takes; pick one with --trim):
#   ArtSource/Mocap/video_to_clip.sh raw/bjorn_light.mp4 Bjorn_Light_Slash Bjorn --frames 17
#   ArtSource/Mocap/video_to_clip.sh raw/bjorn_light.mp4 Bjorn_Light_Slash Bjorn --frames 17 \
#       --trim 120:168 --reuse
#
# --reuse skips the MediaPipe pass and re-keys from the landmarks already extracted, which is
# what you want when trying a different take or frame count. A filmstrip lands in
# ArtSource/Characters/Renders/mocap/ - always look at it before Unity.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PY="$HOME/AppData/Local/Programs/Python/Python312/python.exe"
BLENDER="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
TPOSE_JSON="$HERE/landmarks/_tpose.json"

resolve() { [ -f "$1" ] && echo "$1" || echo "$HERE/$1"; }

if [ "${1:-}" = "--tpose" ]; then
    VIDEO="$(resolve "${2:?usage: --tpose <video>}")"
    [ -f "$VIDEO" ] || { echo "no such video: $2"; exit 1; }
    "$PY" -I "$HERE/pose_extract.py" "$VIDEO" "$TPOSE_JSON"
    echo
    echo "calibration saved: $TPOSE_JSON (used automatically by every clip from now on)"
    exit 0
fi

if [ $# -lt 3 ]; then
    sed -n '2,15p' "${BASH_SOURCE[0]}"
    exit 1
fi

VIDEO="$(resolve "$1")"; CLIP="$2"; CHAR="$3"; shift 3
[ -f "$VIDEO" ] || { echo "no such video: $1"; exit 1; }

MASTER="$ROOT/ArtSource/Blender/${CHAR}_AnimMaster.blend"
[ -f "$MASTER" ] || { echo "no anim master for $CHAR: $MASTER"; exit 1; }

JSON="$HERE/landmarks/${CLIP}.json"
OUT="$ROOT/Assets/_project/Animation/Clips/Custom"
STRIP="$ROOT/ArtSource/Characters/Renders/mocap"

EXTRA=()
REUSE=0
for a in "$@"; do [ "$a" = "--reuse" ] && REUSE=1 || EXTRA+=("$a"); done
# Calibrate from the T-pose video when one has been extracted and no --calib was given.
if [ -f "$TPOSE_JSON" ] && [[ ! " ${EXTRA[*]:-} " =~ " --calib " ]]; then
    EXTRA+=(--calib "$TPOSE_JSON")
fi

if [ "$REUSE" = "1" ] && [ -f "$JSON" ]; then
    echo "=== 1/2  reusing $JSON ==============================================="
else
    echo "=== 1/2  reading the video ==========================================="
    "$PY" -I "$HERE/pose_extract.py" "$VIDEO" "$JSON"
fi

echo
echo "=== 2/2  keying it onto $CHAR ========================================"
"$BLENDER" -b "$MASTER" --python "$ROOT/ArtSource/Blender/video_mocap.py" -- \
    "$MASTER" "$JSON" "$CLIP" "$OUT" --render "$STRIP" "${EXTRA[@]}" 2>&1 |
    grep -vE "^(Read blend|Blender [0-9]|Fra:|Saved:|Info:|$)" || true

echo
echo "clip:      $OUT/$CLIP.fbx"
echo "filmstrip: $STRIP/${CLIP}_*.png"
echo "landmarks: $JSON"
