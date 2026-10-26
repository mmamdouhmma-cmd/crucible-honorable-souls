"""
Player 2's colours for a MIRROR match (both players on the same fighter).

Reads each fighter's body basecolor and writes a recoloured copy to
Assets/_project/Resources/MirrorSkins/<Name>_P2.jpg, which GameMenu's twin wears.
The palette is the user's pick (2026-10-26):

    Richard   crimson surcoat        -> royal blue (gold, steel, cream kept)
    Miyamoto  navy lacquer / cloth   -> crimson lacquer (gold kept)
    Salaadin  green robes            -> black (gold embroidery kept)
    Genghis   black-brown deel       -> deep sky blue, bronze embroidery -> silver
    Bjorn     brown leather          -> charcoal, grey fur -> snow white, crimson -> blue
    Buwaka    leopard pelt           -> black panther, red beads -> blue

Every rule is a SOFT mask in HSV (a smoothstep on hue distance, saturation and value), so
edges between materials blend instead of fringing. Skin is the hazard on every one of them -
its hue sits right beside brown leather and red cloth - so each rule is gated off the skin
band (hue 0-35, mid saturation, bright) unless it is far darker or far more saturated.

    python make_mirror_skins.py [Name ...]      (no names = all six)
    python make_mirror_skins.py --preview       (also writes side-by-side crops to ./_preview)
"""
import os
import sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CHARS = os.path.join(ROOT, "Assets", "_project", "Characters")
OUT = os.path.join(ROOT, "Assets", "_project", "Resources", "MirrorSkins")
SIZE = 2048          # the bodies import at 2048; anything bigger is wasted build size


def source(name):
    folder = "salaadin" if name == "Salaadin" else name
    return os.path.join(CHARS, folder, "Model", f"{name}_Body.fbm", f"{name.lower()}_basecolor.jpg")


# ------------------------------------------------------------------ colour maths

def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rm = nz & (mx == r)
    gm = nz & (mx == g) & ~rm
    bm = nz & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6.0
    h[gm] = (b - r)[gm] / d[gm] + 2.0
    h[bm] = (r - g)[bm] / d[bm] + 4.0
    h = h * 60.0
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0.0)
    return np.stack([h, s, mx], -1)


def hsv_to_rgb(hsv):
    h, s, v = hsv[..., 0] % 360.0, np.clip(hsv[..., 1], 0, 1), np.clip(hsv[..., 2], 0, 1)
    c = v * s
    hp = h / 60.0
    x = c * (1 - np.abs(hp % 2 - 1))
    z = np.zeros_like(h)
    i = np.floor(hp).astype(int) % 6
    r = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [c, x, z, z, x, c])
    g = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [x, c, c, x, z, z])
    b = np.select([i == 0, i == 1, i == 2, i == 3, i == 4, i == 5], [z, z, x, c, c, x])
    m = v - c
    return np.stack([r + m, g + m, b + m], -1)


def smooth(e0, e1, x):
    """0 below e0, 1 above e1 (or the reverse if e0 > e1), smoothstep between."""
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def hue_near(h, centre, inner, outer):
    """1 within `inner` degrees of `centre`, fading to 0 at `outer`."""
    d = np.abs((h - centre + 180.0) % 360.0 - 180.0)
    return smooth(outer, inner, d)


def skin(hsv):
    """The band every fighter's skin lives in: warm hue, middling saturation, lit."""
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    return hue_near(h, 18, 14, 26) * smooth(0.18, 0.28, s) * smooth(0.72, 0.62, s) * smooth(0.40, 0.52, v)


def recolour(hsv, mask, hue=None, sat=None, val=None):
    """Blend toward a new colour by `mask`. hue = absolute degrees; sat / val = functions of
    the old channel, so the texture's shading and weave survive the change."""
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    nh = h if hue is None else np.full_like(h, hue)
    ns = s if sat is None else sat(s)
    nv = v if val is None else val(v)
    out = hsv.copy()
    # Hue cannot be lerped linearly across the wrap, so it switches at half mask - the
    # saturation and value carry the soft edge.
    out[..., 0] = np.where(mask > 0.5, nh, h)
    out[..., 1] = s + (ns - s) * mask
    out[..., 2] = v + (nv - v) * mask
    return out


# ------------------------------------------------------------------ the six palettes

def richard(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    red = hue_near(h, 356, 16, 28) * smooth(0.42, 0.58, s) * (1 - skin(hsv))
    return recolour(hsv, red, hue=222, sat=lambda s: np.clip(s * 0.95, 0, 1), val=lambda v: v * 1.05)


def miyamoto(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    navy = hue_near(h, 225, 35, 55) * smooth(0.10, 0.22, s)
    return recolour(hsv, navy, hue=354,
                    sat=lambda s: np.clip(0.62 + s * 0.5, 0, 0.92),
                    # the navy is DARK (v ~0.15-0.25); lacquer red needs light in it to read red
                    val=lambda v: np.clip(v * 2.1 + 0.04, 0, 0.78))


def salaadin(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    green = hue_near(h, 115, 40, 60) * smooth(0.18, 0.30, s)
    return recolour(hsv, green, sat=lambda s: s * 0.15, val=lambda v: v * 0.42)


def genghis(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    notskin = 1 - skin(hsv)
    # The bronze-copper embroidery: the most saturated warm on him, more than his skin
    # (skin tops out ~0.62). Decided on the ORIGINAL colours, before the coat moves.
    bronze = hue_near(h, 28, 16, 26) * smooth(0.60, 0.70, s) * smooth(0.22, 0.32, v) * notskin
    # The deel: everything darker than his skin (skin starts at v 0.48). Its hue wanders from
    # black through a mottled copper-brown, so value is the only thing that finds all of it -
    # measured, a coat-only mask on v < 0.26 left 29% of the texture still brown-copper.
    coat = smooth(0.46, 0.38, v) * notskin * (1 - bronze)
    hsv = recolour(hsv, bronze, sat=lambda s: s * 0.10, val=lambda v: np.clip(v * 1.15, 0, 1))
    return recolour(hsv, coat, hue=212,
                    sat=lambda s: np.clip(0.45 + s * 0.3, 0, 0.75),
                    val=lambda v: np.clip(v * 1.35 + 0.03, 0, 1))


def bjorn(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    notskin = 1 - skin(hsv)
    crimson = hue_near(h, 355, 14, 24) * smooth(0.50, 0.62, s) * smooth(0.70, 0.60, v) * notskin
    hsv = recolour(hsv, crimson, hue=218, sat=lambda s: s * 0.9, val=lambda v: v * 1.1)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    leather = hue_near(h, 22, 18, 30) * smooth(0.28, 0.38, s) * smooth(0.50, 0.40, v) * notskin
    hsv = recolour(hsv, leather, sat=lambda s: s * 0.22, val=lambda v: v * 0.55)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    fur = smooth(0.40, 0.28, s) * smooth(0.30, 0.40, v) * notskin
    return recolour(hsv, fur, sat=lambda s: s * 0.12, val=lambda v: np.clip(0.62 + v * 0.45, 0, 0.97))


def buwaka(hsv):
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    notskin = 1 - skin(hsv)
    red = hue_near(h, 356, 14, 24) * smooth(0.55, 0.68, s) * notskin
    hsv = recolour(hsv, red, hue=215, sat=lambda s: s * 0.9, val=lambda v: v * 1.05)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    # The leopard's gold ground: yellower and brighter than his hide or his skin.
    pelt = hue_near(h, 36, 7, 13) * smooth(0.45, 0.58, s) * smooth(0.42, 0.55, v)
    return recolour(hsv, pelt, sat=lambda s: s * 0.30, val=lambda v: v * 0.22)


PALETTES = {
    "Richard": richard, "Miyamoto": miyamoto, "Salaadin": salaadin,
    "Genghis": genghis, "Bjorn": bjorn, "Buwaka": buwaka,
}


def build(name, preview_dir=None):
    src = source(name)
    if not os.path.exists(src):
        print(f"{name}: no basecolor at {src} - skipped")
        return
    img = Image.open(src).convert("RGB")
    if img.width != SIZE:
        img = img.resize((SIZE, SIZE), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32) / 255.0
    hsv = rgb_to_hsv(rgb)
    out = hsv_to_rgb(PALETTES[name](hsv))
    changed = np.abs(out - rgb).max(-1) > 0.04
    res = Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))
    os.makedirs(OUT, exist_ok=True)
    dst = os.path.join(OUT, f"{name}_P2.jpg")
    res.save(dst, quality=92)
    print(f"{name}: {changed.mean() * 100:.1f}% of texels recoloured -> {dst}")
    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        a, b = img.resize((768, 768)), res.resize((768, 768))
        side = Image.new("RGB", (1536, 768))
        side.paste(a, (0, 0)); side.paste(b, (768, 0))
        side.save(os.path.join(preview_dir, f"{name}_compare.jpg"), quality=85)


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    prev = None
    if any(a.startswith("--preview") for a in sys.argv):
        prev = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--preview=")),
                    os.path.join(os.path.dirname(__file__), "_preview"))
    for n in (args or PALETTES.keys()):
        build(n, prev)
