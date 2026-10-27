"""Turns the Forest Clearing's magenta floor green (the user's call, 2026-10-27).

The floor is a set of UV charts in the 4096 basecolor atlas. The mask of floor texels is rasterized
in Unity (up-facing triangles of the arena mesh inside the clearing, see Sessions.md 2026-10-27);
this grows it into the pink chart padding only, so the purple orbs elsewhere keep their colour,
and recolours it to moss keeping the original light/dark variation.

    python -I green_forest_floor.py <original basecolor> <floor mask png> <out basecolor>
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

src, mask_path, out = sys.argv[1:4]
img = Image.open(src).convert("RGB")
hsv = np.array(img.convert("HSV")).astype(np.float32) / 255.0
h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]

mask = np.array(Image.open(mask_path).convert("L")) > 0
grown = np.array(Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))) > 0
pink = (h > 0.75) & (h < 0.97) & (s > 0.25)
sel = mask | (grown & pink)

vm = v[sel].mean()
new_h = 0.27 + (h - h[sel].mean()) * 0.15          # moss, with a little of the original hue wander
new_s = np.clip(0.45 + (s - s[sel].mean()) * 0.5, 0.25, 0.75)
new_v = np.clip(0.34 + (v - vm) * 0.9, 0.08, 0.85)

out_hsv = hsv.copy()
out_hsv[..., 0][sel] = new_h[sel] % 1.0
out_hsv[..., 1][sel] = new_s[sel]
out_hsv[..., 2][sel] = new_v[sel]
res = Image.fromarray((out_hsv * 255).astype(np.uint8), "HSV").convert("RGB")
res.save(out, quality=95)
print(f"recoloured {sel.mean() * 100:.2f}% of the atlas (mask {mask.mean() * 100:.2f}%)")
