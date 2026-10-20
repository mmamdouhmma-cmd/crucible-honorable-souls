"""Prepare a Tripo shield FBX for Unity: orient, scale, re-origin at the hand grip, export FBX + textures.

Usage: blender -b --python prep_shield.py -- <shield.fbx> <out_dir> <Name> <target_verts> <height>

Conventions produced (the mount script and the Unity setup rely on them):
  - the shield stands along local +Z: wide top at +Z, point at -Z
  - the face normal is local +Y: the convex front (boss) faces +Y, the arm side is -Y.
    CAVEAT: the test below picks the side whose centre band bulges furthest, and a shield whose
    grip bar stands prouder than its boss comes out the wrong way round - Richard's did, so
    richard_body.py mounts his with the face on -Y. Check a render before trusting the sign.
  - the origin sits where the fist grips, behind the boss on the inner face
  - textures land in <out_dir>/<Name>.fbm/<Name>_basecolor.* , like prep_weapon.py
"""
IMAGE_EXT = (".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".exr")
import bpy, sys, os, glob, shutil
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_DIR, NAME, TARGET, HEIGHT = args[0], args[1], args[2], int(args[3]), float(args[4])
os.makedirs(OUT_DIR, exist_ok=True)
fbm = os.path.join(OUT_DIR, NAME + ".fbm")
os.makedirs(fbm, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC)
mesh = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in list(bpy.data.objects):
    if o is not mesh:
        bpy.data.objects.remove(o)
mesh.parent = None
bpy.context.view_layer.objects.active = mesh
mesh.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

before = len(mesh.data.vertices)
ratio = min(1.0, TARGET / max(before, 1))
if ratio < 1.0:
    dec = mesh.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = ratio
    dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier="Decimate")
print(f"{NAME}: {before} -> {len(mesh.data.vertices)} verts")

co = np.array([v.co[:] for v in mesh.data.vertices], dtype=np.float64)
ext = co.max(0) - co.min(0)
tall = int(np.argmax(ext))                                   # height
thin = int(np.argmin(ext))                                   # through the face
wide = [a for a in range(3) if a not in (tall, thin)][0]      # across
basis = np.zeros((3, 3))
basis[:, 2] = np.eye(3)[tall]
basis[:, 1] = np.eye(3)[thin]
basis[:, 0] = np.cross(basis[:, 1], basis[:, 2])
co = co @ basis                       # x = across, y = through the face, z = height

# the point of a kite shield is the NARROW end: put it at -Z
H = co[:, 2].max() - co[:, 2].min()
z0 = co[:, 2].min()
def xwidth(sel): return (sel[:, 0].max() - sel[:, 0].min()) if len(sel) else 0.0
top_w = xwidth(co[co[:, 2] > z0 + 0.85 * H])
bot_w = xwidth(co[co[:, 2] < z0 + 0.15 * H])
if top_w < bot_w:
    co[:, 2] *= -1; co[:, 0] *= -1
    print(f"{NAME}: flipped so the point is at -Z (widths top {top_w:.3f} bottom {bot_w:.3f})")

# the boss bulges out of the FRONT: the face whose centre band reaches further from the mid-plane
co[:, 1] -= np.median(co[:, 1])
mid = co[(np.abs(co[:, 0]) < 0.12 * ext[wide]) & (np.abs(co[:, 2] - np.median(co[:, 2])) < 0.12 * H)]
if len(mid) and abs(mid[:, 1].min()) > abs(mid[:, 1].max()):
    co[:, 1] *= -1; co[:, 0] *= -1
    print(f"{NAME}: flipped so the boss faces +Y")

# scale to HEIGHT
co *= HEIGHT / H

# origin at the grip: centred across, behind the boss, on the inner face.
# The boss sits at the widest part of the shield, a third down from the top - that is where a fist holds it.
zc = co[:, 2].min() + 0.62 * (co[:, 2].max() - co[:, 2].min())
co[:, 0] -= np.median(co[:, 0])
co[:, 2] -= zc
band = co[(np.abs(co[:, 2]) < 0.08 * HEIGHT) & (np.abs(co[:, 0]) < 0.08 * HEIGHT)]
inner = band[:, 1].min() if len(band) else co[:, 1].min()
co[:, 1] -= inner
mesh.data.vertices.foreach_set("co", co.ravel())
mesh.data.update()
mesh.name = mesh.data.name = NAME
print(f"{NAME}: height {HEIGHT:.3f}, grip at origin, extents "
      f"x {co[:,0].min():.3f}..{co[:,0].max():.3f} y {co[:,1].min():.3f}..{co[:,1].max():.3f} z {co[:,2].min():.3f}..{co[:,2].max():.3f}")

mat = mesh.data.materials[0]
mat.name = NAME
imgs = {}
for d in glob.glob(os.path.join(os.path.dirname(SRC), "*.fbm")):
    for fp in os.listdir(d):
        lo = fp.lower()
        key = ("color" if "basecolor" in lo else "normal" if "normal" in lo else
               "metal" if "metallic" in lo else "rough" if "roughness" in lo else None)
        if key and lo.endswith(IMAGE_EXT):
            imgs[key] = os.path.join(d, fp)
for key, suffix in (("color", "_basecolor"), ("normal", "_normal")):
    if key in imgs:
        src = imgs[key]
        dst = os.path.join(fbm, NAME + suffix + os.path.splitext(src)[1].lower().replace(".jpeg", ".jpg"))
        shutil.copy2(src, dst)
        print("texture:", os.path.basename(dst))

out = os.path.join(OUT_DIR, NAME + ".fbx")
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'},
    mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
print("exported:", out, f"({os.path.getsize(out)//1024} KB)")
print("SHIELD-OK")
