#!/usr/bin/env bash
# Rebuild the prepared fighter FBX files from the Tripo originals (Git Bash).
#   bash ArtSource/Blender/rebuild_fighters.sh [Bjorn|Miyamoto ...]      (default: both)
#   SKIP_PREP=1 bash ArtSource/Blender/rebuild_fighters.sh Bjorn          (reuse the clean body, redo weapons/fists)
# Steps per character: prep_body (slim + textures) -> keep a clean copy in ArtSource/Characters/Prepped
#   -> attach_weapons (bakes fists, bone-parents the weapons) -> Assets/_project/Characters/<Name>/Model/<Name>_Body.fbx
set -e
P="$(cd "$(dirname "$0")/../.." && pwd -W)"
B="C:/Program Files/Blender Foundation/Blender 5.1/blender.exe"
S="$P/ArtSource/Blender"; T="$P/ArtSource/Characters/Tripo"; C="$P/Assets/_project/Characters"
R="$P/ArtSource/Characters/Renders"; K="$P/ArtSource/Characters/Prepped"
mkdir -p "$K" "$R"
chars="${@:-Bjorn Miyamoto}"
for n in $chars; do
  if [ -z "$SKIP_PREP" ]; then
    echo "== $n: prep body"
    "$B" -b --python "$S/prep_body.py" -- "$T/${n}_rigged.fbx" "$C/$n/Model" "$n" 120000 2>&1 | grep -E "decimated|exported:|PREP-OK|Error|Traceback" || true
    cp "$C/$n/Model/${n}_Body.fbx" "$K/${n}_Body_clean.fbx"
  fi
  case $n in
    Bjorn)
      W=("$C/Bjorn/Weapons/Bjorn_Axe.fbx:R_Hand:0" "$C/Bjorn/Weapons/Bjorn_Axe.fbx:L_Hand:1") ;;
    Miyamoto)
      "$B" -b --python "$S/build_katana.py" -- "$C/Miyamoto/Weapons" Miyamoto_Katana 0.57 0.12 2>&1 | grep -E "BUILD-OK|Error|Traceback" || true
      W=("$C/Miyamoto/Weapons/Miyamoto_Katana.fbx:R_Hand:0" "none:L_Hand:0") ;;
  esac
  echo "== $n: attach weapons + fists"
  "$B" -b --python "$S/attach_weapons.py" -- "$K/${n}_Body_clean.fbx" "$C/$n/Model/${n}_Body.fbx" "$R" "${W[@]}" 2>&1 | grep -E "fist|parented|exported:|ATTACH-OK|Error|Traceback|line [0-9]+" || true
done
echo "REBUILD-DONE"
