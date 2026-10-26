"""
The old Tripo GLAZE on every body (the user's call, 2026-10-26: "glaze and shiny, more of a
flesh texture", as the old Bjorn and Miyamoto FBX bodies had it).

What that look WAS, measured off the old masks (tripo_mat_*: URP Lit, Metallic 1 x mask R,
Smoothness 0.75 x mask A, plus a normal map):
    skin           smooth 0.62-0.65, metal ~0.05     <- the glaze
    steel          smooth 0.72,      metal 0.86
    cloth/leather  smooth 0.12-0.19, metal ~0.05     <- matte, so the skin and metal shine AGAINST it
The new bodies are a flat Smoothness 0.4 with no normal map (Bjorn's real mask is 0.36 skin /
0.375 leather - nearly flat too), which is why they read as plastic.

The old maps cannot be reused - they belong to the old meshes' UVs - and only Bjorn's download
shipped PBR maps at all, so the mask is DERIVED from each body's basecolor by classifying its
texels and writing the old measured values (x0.75 baked in, since BuildMaterial sets
Smoothness 1 when a mask exists). A normal map is derived the same way, from the basecolor's
high-passed luminance, for every body that has no real one.

Writes into each Assets/_project/Characters/<dir>/Model/<Name>_Body.fbm/:
    <name>_mask.png     R=G=B metallic, A smoothness (the layout BuildMaterial already reads)
    <name>_normal.png   only if none exists (Bjorn keeps his real one)
then run Crucible's material build (CrucibleCharacterSetup.BuildMaterial) on each body.

    python make_body_maps.py [Name ...]     (no names = all six)
"""
import os
import shutil
import sys
import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from make_mirror_skins import rgb_to_hsv, skin, hue_near, smooth, ROOT, CHARS  # noqa: E402

SIZE = 2048
SCALE = 0.75          # the old materials' Smoothness, baked into the alpha
BACKUP = os.path.join(ROOT, "ArtSource", "Characters", "_backup_20261026", "body_masks")

# (metallic, old-mask smoothness) per class, from the measurement above
SKIN = (0.04, 0.64)
STEEL = (0.85, 0.72)
GOLD = (0.75, 0.60)
MID = (0.05, 0.26)
DARK = (0.05, 0.16)

DIRS = {"Bjorn": "Bjorn", "Miyamoto": "Miyamoto", "Richard": "Richard",
        "Salaadin": "salaadin", "Genghis": "Genghis", "Buwaka": "Buwaka"}


def fbm(name):
    return os.path.join(CHARS, DIRS[name], "Model", f"{name}_Body.fbm")


def mask_for(rgb):
    hsv = rgb_to_hsv(rgb)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    w_skin = skin(hsv)
    rest = 1 - w_skin
    w_steel = smooth(0.20, 0.10, s) * smooth(0.30, 0.45, v) * rest
    w_gold = hue_near(h, 43, 9, 15) * smooth(0.40, 0.55, s) * smooth(0.40, 0.55, v) * rest * (1 - w_steel)
    left = rest * (1 - w_steel) * (1 - w_gold)
    w_mid = left * smooth(0.30, 0.42, v)
    w_dark = left * (1 - smooth(0.30, 0.42, v))
    metal = (w_skin * SKIN[0] + w_steel * STEEL[0] + w_gold * GOLD[0] + w_mid * MID[0] + w_dark * DARK[0])
    gloss = (w_skin * SKIN[1] + w_steel * STEEL[1] + w_gold * GOLD[1] + w_mid * MID[1] + w_dark * DARK[1])
    # a little of the texture's own light/dark in the gloss, so worn and shadowed bits dull off
    gloss = gloss * (0.80 + 0.40 * v)
    out = np.zeros(rgb.shape[:2] + (4,), np.float32)
    out[..., 0] = out[..., 1] = out[..., 2] = np.clip(metal, 0, 1)
    out[..., 3] = np.clip(gloss * SCALE, 0, 1)
    return out


def normal_for(img, strength=2.2):
    lum = np.asarray(img.convert("L")).astype(np.float32) / 255.0
    blur = np.asarray(img.convert("L").filter(ImageFilter.GaussianBlur(12))).astype(np.float32) / 255.0
    height = lum - blur                       # high-pass: weave, stitching, dents - not the paint
    height = np.asarray(Image.fromarray(((height + 0.5).clip(0, 1) * 255).astype(np.uint8))
                        .filter(ImageFilter.GaussianBlur(0.8))).astype(np.float32) / 255.0
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(dx)], -1)   # OpenGL (Unity) convention: +Y up in the texture
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), "RGB")


def build(name):
    d = fbm(name)
    lower = name.lower()
    src = os.path.join(d, f"{lower}_basecolor.jpg")
    if not os.path.exists(src):
        print(f"{name}: no basecolor - skipped")
        return
    img = Image.open(src).convert("RGB")
    if img.width != SIZE:
        img = img.resize((SIZE, SIZE), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32) / 255.0

    mask_path = os.path.join(d, f"{lower}_mask.png")
    if os.path.exists(mask_path):
        os.makedirs(BACKUP, exist_ok=True)
        keep = os.path.join(BACKUP, f"{lower}_mask_before.png")
        if not os.path.exists(keep):
            shutil.copy2(mask_path, keep)
    m = mask_for(rgb)
    Image.fromarray((m * 255 + 0.5).astype(np.uint8), "RGBA").save(mask_path)

    normal_path = os.path.join(d, f"{lower}_normal.png")
    made_normal = not os.path.exists(normal_path)
    if made_normal:
        normal_for(img).save(normal_path)
    print(f"{name}: mask metal {m[..., 0].mean():.3f} smooth {m[..., 3].mean():.3f}"
          f"{' + derived normal' if made_normal else ' (kept its real normal)'}")


if __name__ == "__main__":
    for n in (sys.argv[1:] or DIRS.keys()):
        build(n)
