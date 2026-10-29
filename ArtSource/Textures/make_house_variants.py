"""Timber/roof colour variants of the Musashi Chapter One two-story house.

Usage: python make_house_variants.py <house basecolor.jpg> <out dir>

The house is one Tripo atlas, so a material tint would shift timber, roof tiles and plaster
together. This classifies every texel instead - roof tiles are near-grey, timber is a saturated
dark brown, plaster a pale beige - with SOFT masks (no hard edges inside a texel island) and
writes two recoloured copies next to the original:
  <name>_b.jpg   dark weathered timber, blue-charcoal roof
  <name>_c.jpg   red-brown timber, warm grey roof, warmer plaster
The original basecolor is read, never written.
"""
import sys, os
import numpy as np
from PIL import Image

src, out_dir = sys.argv[1], sys.argv[2]
rgb = np.asarray(Image.open(src).convert("RGB"), dtype=np.float32) / 255.0
hsv = np.asarray(Image.open(src).convert("RGB").convert("HSV"), dtype=np.float32) / 255.0
h, s, v = hsv[..., 0] * 360.0, hsv[..., 1], hsv[..., 2]

def ramp(x, a, b):
    """0 below a, 1 above b, linear between (a > b flips it)."""
    return np.clip((x - a) / (b - a), 0.0, 1.0)

roof = ramp(s, 0.22, 0.12) * ramp(v, 0.62, 0.45)                       # grey and not bright
brownish = ramp(np.abs(((h - 28.0 + 180.0) % 360.0) - 180.0), 30.0, 15.0)   # hue near 28 deg
timber = brownish * ramp(s, 0.38, 0.52) * ramp(v, 0.58, 0.46) * (1.0 - roof)
plaster = brownish * ramp(s, 0.62, 0.48) * ramp(v, 0.40, 0.52) * (1.0 - roof) * (1.0 - timber)
print(f"coverage: roof {roof.mean():.2f}  timber {timber.mean():.2f}  plaster {plaster.mean():.2f}")

def grade(img, mask, mul, sat=1.0):
    """Multiply by an RGB colour and scale saturation around the texel's own grey, under a mask."""
    grey = img.mean(axis=2, keepdims=True)
    graded = (grey + (img - grey) * sat) * np.asarray(mul, dtype=np.float32)
    m = mask[..., None]
    return img * (1.0 - m) + np.clip(graded, 0.0, 1.0) * m

variants = {
    "b": [(timber, (0.70, 0.66, 0.62), 0.65), (roof, (0.82, 0.88, 0.98), 1.0), (plaster, (0.92, 0.90, 0.86), 0.85)],
    "c": [(timber, (0.86, 0.66, 0.58), 1.10), (roof, (1.06, 1.00, 0.94), 1.0), (plaster, (1.04, 0.98, 0.90), 1.0)],
}
stem = os.path.splitext(os.path.basename(src))[0]
for key, grades in variants.items():
    img = rgb.copy()
    for mask, mul, sat in grades:
        img = grade(img, mask, mul, sat)
    path = os.path.join(out_dir, f"{stem}_{key}.jpg")
    Image.fromarray((img * 255.0 + 0.5).astype(np.uint8)).save(path, quality=92)
    print("wrote", path)
