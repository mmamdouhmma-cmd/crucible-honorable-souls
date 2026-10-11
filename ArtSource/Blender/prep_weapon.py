"""Prepare a Tripo weapon GLB for Unity: slim, re-origin at the grip, scale, export FBX + textures.

Usage: blender -b --python prep_weapon.py -- <weapon.glb> <out_dir> <Name> <target_verts> <length> <grip_frac>

  <Name>        mesh/material/file name, e.g. Bjorn_Axe
  <length>      final length along the handle in file units (character bodies are ~0.98 tall)
  <grip_frac>   where the hand holds it, as a fraction of the length from the butt (0 = butt)

Conventions produced (and relied on by the socket setup in Blender/Unity):
  - the handle runs along local +Z in Blender, which becomes local +Y in Unity after the FBX axis swap
    ("local Y points from the grip toward the business end")
  - the origin sits on the handle axis at the grip point
  - blade / axe head faces local +Y in Blender (Unity -Z... see note in CLAUDE.md), measured from the widest axis
  - textures land in <out_dir>/<Name>.fbm/: <Name>_basecolor.png, <Name>_normal.png,
    <Name>_mask.png (R metallic, G AO, B 0, A smoothness) built from Tripo's ORM (R=AO, G=roughness, B=metallic)
"""
import bpy, sys, os
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_DIR, NAME, TARGET, LENGTH, GRIP = args[0], args[1], args[2], int(args[3]), float(args[4]), float(args[5])
EDGE = args[6] if len(args) > 6 else "mass"   # mass: blade = heavier side (axe); curve: edge opposite the tip deflection (katana)
GUARD = float(args[7]) if len(args) > 7 else 1.0   # scale factor applied radially to the widest thin band (a sword guard/tsuba); 1 = untouched
os.makedirs(OUT_DIR, exist_ok=True)
fbm = os.path.join(OUT_DIR, NAME + ".fbm")
os.makedirs(fbm, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
for o in list(bpy.data.objects):
    if o is not mesh and o.type != 'MESH':
        bpy.data.objects.remove(o)
mesh.parent = None
bpy.context.view_layer.objects.active = mesh
mesh.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# --- decimate --------------------------------------------------------------
before = len(mesh.data.vertices)
ratio = min(1.0, TARGET / max(before, 1))
dec = mesh.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = ratio
dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier="Decimate")
print(f"{NAME}: {before} -> {len(mesh.data.vertices)} verts (ratio {ratio:.3f})")

# --- orient: longest axis -> +Z (handle), widest remaining axis -> +Y (blade) -------------
co = np.array([v.co[:] for v in mesh.data.vertices], dtype=np.float64)
ext = co.max(0) - co.min(0)
long_axis = int(np.argmax(ext))
rest = [a for a in range(3) if a != long_axis]
wide_axis = rest[int(np.argmax(ext[rest]))]
thin_axis = [a for a in range(3) if a not in (long_axis, wide_axis)][0]
# build a rotation that maps long->Z, wide->Y, thin->X (right-handed; flip X if needed)
basis = np.zeros((3, 3))
basis[:, 2] = np.eye(3)[long_axis]
basis[:, 1] = np.eye(3)[wide_axis]
basis[:, 0] = np.cross(basis[:, 1], basis[:, 2])
co = co @ basis  # new coords: columns = (x=thin, y=wide, z=long)

# head/business end at +Z: the handle end is the end with the THINNER cross-section (axe butt, sword tip is
# thinner than the tsuka... no: the tip is thinner, so for swords the rule is inverted; use the widest
# cross-section instead: axe head and katana blade+tsuba are both wider than the butt/tip? The tsuba is
# widest. Rule: the end whose 15% band has the larger 90th-percentile radial extent is the HEAD end.)
L = ext[long_axis]
def radial90(sel):
    r = np.linalg.norm(sel[:, :2] - np.median(sel[:, :2], axis=0), axis=1)
    return np.percentile(r, 90) if len(r) else 0.0
z0 = co[:, 2].min()
top_r = radial90(co[co[:, 2] > z0 + 0.85 * L])
bot_r = radial90(co[co[:, 2] < z0 + 0.15 * L])
if EDGE == "curve":
    # sword: the tsuba/handle end is the wide end and must be at the BOTTOM
    if top_r > bot_r:
        co[:, 2] *= -1; co[:, 1] *= -1
        print(f"{NAME}: flipped so the tip is at +Z (end radii top {top_r:.3f} bottom {bot_r:.3f})")
else:
    # axe / mace: the head is the wide end and must be at the TOP
    if bot_r > top_r:
        co[:, 2] *= -1; co[:, 1] *= -1
        print(f"{NAME}: flipped so the head is at +Z (end radii top {top_r:.3f} bottom {bot_r:.3f})")

# --- scale to LENGTH, origin at the grip ------------------------------------
cur_len = co[:, 2].max() - co[:, 2].min()
co *= LENGTH / cur_len
zmin = co[:, 2].min()
grip_z = zmin + GRIP * LENGTH
band = co[np.abs(co[:, 2] - grip_z) < 0.03 * LENGTH]
axis_xy = np.median(band[:, :2], axis=0) if len(band) else np.zeros(2)
co[:, 0] -= axis_xy[0]
co[:, 1] -= axis_xy[1]
co[:, 2] -= grip_z
# blade side at +Y, judged after centring on the handle axis
head = co[co[:, 2] > 0.6 * co[:, 2].max()]
if EDGE == "curve":
    # the tip deflects toward the spine; the edge is the opposite side
    tip_dev = np.median(head[:, 1])
    if tip_dev > 0:
        co[:, 1] *= -1; co[:, 0] *= -1
        print(f"{NAME}: flipped so the edge faces +Y (tip deflects {tip_dev:+.3f})")
else:
    if abs(head[:, 1].min()) > abs(head[:, 1].max()):
        co[:, 1] *= -1; co[:, 0] *= -1
        print(f"{NAME}: flipped so the blade faces +Y")
# optional guard shrink: find the z-band whose radial extent peaks (the tsuba), scale its verts toward the axis
if GUARD != 1.0:
    r = np.hypot(co[:, 0], co[:, 1])
    zs = co[:, 2]
    nb = 60
    edges = np.linspace(zs.min(), zs.max(), nb + 1)
    idx = np.clip(np.digitize(zs, edges) - 1, 0, nb - 1)
    # a guard is wide along the THIN axis (x) where a blade is not; the blade curve only shows in y
    xext = np.array([(co[idx == i, 0].max() - co[idx == i, 0].min()) if (idx == i).any() else 0 for i in range(nb)])
    rmax = np.array([r[idx == i].max() if (idx == i).any() else 0 for i in range(nb)])
    handle_x = np.median(xext[: nb // 4])
    handle_r = np.median(rmax[: nb // 4])
    peak = int(np.argmax(xext))
    lo = peak
    while lo > 0 and xext[lo - 1] > 1.6 * handle_x: lo -= 1
    hi = peak
    while hi < nb - 1 and xext[hi + 1] > 1.6 * handle_x: hi += 1
    band = (zs >= edges[lo]) & (zs <= edges[hi + 1]) & (r > 1.2 * handle_r)
    co[band, 0] *= GUARD
    co[band, 1] *= GUARD
    print(f"{NAME}: guard band z {edges[lo]:.3f}..{edges[hi+1]:.3f} (peak x-width {xext[peak]:.3f}, handle x-width {handle_x:.3f}) scaled x{GUARD} on {band.sum()} verts")
mesh.data.vertices.foreach_set("co", co.ravel())
mesh.data.update()
mesh.data.name = NAME
mesh.name = NAME
print(f"{NAME}: length {LENGTH:.3f}, grip at z=0, head toward +Z, extents x {co[:,0].min():.3f}..{co[:,0].max():.3f} y {co[:,1].min():.3f}..{co[:,1].max():.3f} z {co[:,2].min():.3f}..{co[:,2].max():.3f}")

# --- textures -----------------------------------------------------------------
mat = mesh.data.materials[0]
mat.name = NAME
imgs = {}
for node in mat.node_tree.nodes:
    if node.type == 'TEX_IMAGE' and node.image:
        for link in node.outputs[0].links:
            imgs[link.to_socket.name] = node.image
        nm = node.image.name.lower()
        if nm.startswith("color"): imgs["color"] = node.image
        elif nm.startswith("orm"): imgs["orm"] = node.image
        elif nm.startswith("normal"): imgs["normal"] = node.image

def save(img, path, colorspace):
    img.colorspace_settings.name = colorspace
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()

if "color" in imgs:
    save(imgs["color"], os.path.join(fbm, NAME + "_basecolor.png"), 'sRGB')
if "normal" in imgs:
    save(imgs["normal"], os.path.join(fbm, NAME + "_normal.png"), 'Non-Color')
if "orm" in imgs:
    orm = imgs["orm"]; orm.colorspace_settings.name = 'Non-Color'
    w, h = orm.size
    a = np.array(orm.pixels[:], dtype=np.float32).reshape(h, w, 4)
    out = np.empty_like(a)
    out[..., 0] = a[..., 2]        # metallic
    out[..., 1] = a[..., 0]        # AO
    out[..., 2] = 0.0
    out[..., 3] = 1.0 - a[..., 1]  # smoothness
    m = bpy.data.images.new(NAME + "_mask", w, h, alpha=True)
    m.colorspace_settings.name = 'Non-Color'
    m.pixels[:] = out.ravel().tolist()
    save(m, os.path.join(fbm, NAME + "_mask.png"), 'Non-Color')
print("textures:", sorted(os.listdir(fbm)))

# --- export ----------------------------------------------------------------------
out_fbx = os.path.join(OUT_DIR, NAME + ".fbx")
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, bake_anim=False,
    path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", out_fbx, f"({os.path.getsize(out_fbx)//1024} KB)")
print("PREP-OK")
