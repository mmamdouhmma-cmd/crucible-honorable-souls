"""Stage a story character's rigged body + weapon into Assets/ the way CrucibleCharacterSetup.Rebuild expects.

    python ArtSource/Textures/stage_story_char.py <id>          e.g. Ch1_Musashi

Reads  ArtSource/Characters/HD/<id>/<id>_HD_rigged.fbx                  (hd_rig.py + swap_blade.py output)
       ArtSource/Characters/Tripo/Story_Ch1/<id>/*.fbm/*_{basecolor,normal,metallic,roughness}.*   (the download's maps)
       ArtSource/Characters/HD/<id>/weapon/<W>.fbm/<W>_*                 (swap_blade.py)  or  weapon/<W>_* (recolour_grip.py)
Writes Assets/_project/Characters/<id>/Model/<id>_Body.fbx
       Assets/_project/Characters/<id>/Model/<id>_Body.fbm/<id lower>_{basecolor.jpg,normal.png,mask.png}
       Assets/_project/Characters/<id>/Weapons/<W>.fbm/<W>_{basecolor,normal,mask}.png

The MASK is the roster's metallic treatment (the user, 2026-10-29: "same metallic texture"), packed from
the download's own PBR maps exactly as Bjorn's was: RGB = metallic, A = 1 - roughness (URP's metallic-map
alpha is smoothness). The FBX is copied over in place when it exists, so its .meta and GUID survive.
"""
import sys, os, glob, shutil
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
ID = sys.argv[1]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HD = os.path.join(ROOT, "ArtSource", "Characters", "HD", ID)
TRIPO = os.path.join(ROOT, "ArtSource", "Characters", "Tripo", "Story_Ch1", ID)
DST = os.path.join(ROOT, "Assets", "_project", "Characters", ID)


def find(folder, kind):
    hits = [f for f in glob.glob(os.path.join(folder, "*")) if os.path.splitext(os.path.basename(f))[0].lower().endswith(kind)]
    return hits[0] if hits else None


def pack_mask(metal_path, rough_path, out):
    m = np.asarray(Image.open(metal_path).convert("L"), dtype=np.float32)
    r = np.asarray(Image.open(rough_path).convert("L").resize((m.shape[1], m.shape[0])), dtype=np.float32)
    rgba = np.dstack([m, m, m, 255.0 - r]).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(out)
    print(f"  mask {os.path.relpath(out, ROOT)}: metallic mean {m.mean() / 255:.3f}, smoothness mean {1 - r.mean() / 255:.3f}")


def maps(src_dir, dst_dir, base):
    os.makedirs(dst_dir, exist_ok=True)
    bc, nm = find(src_dir, "basecolor"), find(src_dir, "normal")
    me, ro = find(src_dir, "metallic"), find(src_dir, "roughness")
    assert bc, f"no basecolor in {src_dir}"
    ext = os.path.splitext(bc)[1].lower().replace(".jpeg", ".jpg")
    shutil.copy(bc, os.path.join(dst_dir, f"{base}_basecolor{ext}"))
    if nm:
        Image.open(nm).convert("RGB").save(os.path.join(dst_dir, f"{base}_normal.png"))
    mask = find(src_dir, "mask")
    if me and ro:
        pack_mask(me, ro, os.path.join(dst_dir, f"{base}_mask.png"))
    elif mask:
        shutil.copy(mask, os.path.join(dst_dir, f"{base}_mask.png"))
    print(f"  {os.path.relpath(dst_dir, ROOT)}: basecolor{' normal' if nm else ''}{' mask' if (me and ro) or mask else ''}")


print(ID)
model = os.path.join(DST, "Model"); os.makedirs(model, exist_ok=True)
shutil.copy(os.path.join(HD, f"{ID}_HD_rigged.fbx"), os.path.join(model, f"{ID}_Body.fbx"))
print(f"  body fbx -> {os.path.relpath(os.path.join(model, ID + '_Body.fbx'), ROOT)}")
maps(glob.glob(os.path.join(TRIPO, "*.fbm"))[0], os.path.join(model, f"{ID}_Body.fbm"), ID.lower())

wdir = os.path.join(HD, "weapon")
for fbm in glob.glob(os.path.join(wdir, "*.fbm")):                      # swap_blade.py output
    w = os.path.splitext(os.path.basename(fbm))[0]
    maps(fbm, os.path.join(DST, "Weapons", w + ".fbm"), w)
flat = [f for f in glob.glob(os.path.join(wdir, "*_basecolor.*"))]     # recolour_grip.py output
for bc in flat:
    w = os.path.basename(bc).split("_basecolor")[0]
    maps(wdir, os.path.join(DST, "Weapons", w + ".fbm"), w)
