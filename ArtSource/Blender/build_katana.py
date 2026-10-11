"""Build a clean katana procedurally and export it with the weapon conventions of prep_weapon.py.

Usage: blender -b --python build_katana.py -- <out_dir> <Name> <length_units> [grip_frac=0.12]

Real proportions (metres) scaled so the whole sword is <length_units> long:
  tsuka 25 cm (oval 3.6 x 2.0 cm, kashira cap and fuchi collar in iron), tsuba 0.5 cm thick oval 8 x 7.2 cm,
  habaki brass collar 2.5 cm, blade 72 cm: 3.0 cm wide tapering to 2.4, 0.7 cm at the spine, sori 1.5 cm,
  kissaki over the last 4 cm.  +Z runs butt -> tip, the EDGE faces +Y (the curve deflects the tip to -Y),
  origin on the handle axis at grip_frac of the length from the butt.

Material slots (in this order; written to <out_dir>/<Name>.slots.txt as name:r,g,b:metallic:smoothness):
  Steel, Iron, Wrap, Brass
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, NAME, LENGTH = args[0], args[1], float(args[2])
GRIP = float(args[3]) if len(args) > 3 else 0.12
os.makedirs(OUT_DIR, exist_ok=True)
U = LENGTH / 1.0   # metres -> units (the real sword is 1.00 m)

bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new(NAME)
obj = bpy.data.objects.new(NAME, mesh)
bpy.context.scene.collection.objects.link(obj)
bm = bmesh.new()
slots = [("Steel", (0.78, 0.79, 0.82), 1.0, 0.92), ("Iron", (0.16, 0.15, 0.14), 0.9, 0.45),
         ("Wrap", (0.05, 0.04, 0.05), 0.0, 0.25), ("Brass", (0.75, 0.58, 0.28), 1.0, 0.7)]
for n, c, m, s in slots:
    mat = bpy.data.materials.new(n)
    mat.use_nodes = True
    bsdf = next(nd for nd in mat.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*c, 1.0)
    bsdf.inputs['Metallic'].default_value = m
    bsdf.inputs['Roughness'].default_value = 1.0 - s
    mesh.materials.append(mat)
SLOT = {n: i for i, (n, *_r) in enumerate(slots)}

def ring(zs, pts, mat):
    """Sweep a closed 2D profile (list of (x,y) per station) along z stations; cap both ends. Returns nothing."""
    rings = []
    for z, prof in zip(zs, pts):
        rings.append([bm.verts.new(Vector((x * U, y * U, z * U))) for x, y in prof])
    faces = []
    for a, b in zip(rings[:-1], rings[1:]):
        n = len(a)
        for i in range(n):
            f = bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
            f.material_index = mat; f.smooth = True
    for r, rev in ((rings[0], True), (rings[-1], False)):
        try:
            f = bm.faces.new(list(reversed(r)) if rev else r)
            f.material_index = mat
        except ValueError:
            pass

def oval(rx, ry, n=16, yoff=0.0):
    return [(rx * math.cos(2 * math.pi * i / n), ry * math.sin(2 * math.pi * i / n) + yoff) for i in range(n)]

# --- tsuka (handle) -------------------------------------------------------
tsuka = [0.0, 0.25]
ring([0.0, 0.01], [oval(0.019, 0.011), oval(0.019, 0.011)], SLOT["Iron"])            # kashira
ring([0.01, 0.24], [oval(0.018, 0.010), oval(0.0175, 0.0095)], SLOT["Wrap"])           # wrap
ring([0.24, 0.25], [oval(0.0185, 0.0105), oval(0.0185, 0.0105)], SLOT["Iron"])         # fuchi
# --- tsuba ----------------------------------------------------------------------
ring([0.25, 0.254], [oval(0.036, 0.032, 32), oval(0.036, 0.032, 32)], SLOT["Iron"])
# --- habaki ----------------------------------------------------------------------
ring([0.255, 0.28], [oval(0.0065, 0.019, 12), oval(0.006, 0.0175, 12)], SLOT["Brass"])

# --- blade ------------------------------------------------------------------------
z0, z1 = 0.28, 1.00
L = z1 - z0
sori = 0.015
n_st = 40
zs, profs = [], []
for i in range(n_st + 1):
    t = i / n_st
    z = z0 + L * t
    w = 0.030 - 0.006 * t                 # width edge->spine
    th = 0.007 - 0.003 * t                # thickness at the shinogi
    if z > z1 - 0.04:                     # kissaki: converge to the point on the edge line
        k = (z1 - z) / 0.04
        w = w * (0.08 + 0.92 * k)
        th = th * (0.15 + 0.85 * k)
    yc = -sori * math.sin(math.pi * t)    # curve toward the spine
    ye = w * 0.5 + yc                     # edge
    ys = -w * 0.5 + yc                    # spine
    ysh = ye - 0.38 * w                   # shinogi ridge
    prof = [(0.0, ye), (th * 0.5, ysh), (th * 0.45, ys + 0.002), (0.0, ys), (-th * 0.45, ys + 0.002), (-th * 0.5, ysh)]
    zs.append(z); profs.append(prof)
ring(zs, profs, SLOT["Steel"])

bm.to_mesh(mesh); bm.free()
mesh.update()

# origin at the grip point
grip_z = GRIP * 1.0 * U
for v in mesh.vertices:
    v.co.z -= grip_z
mesh.update()
print(f"{NAME}: {len(mesh.vertices)} verts, length {LENGTH:.3f}, grip at z=0, tip at +Z, edge +Y")

with open(os.path.join(OUT_DIR, NAME + ".slots.txt"), "w") as fh:
    for n, c, m, s in slots:
        fh.write(f"{n}:{c[0]:.3f},{c[1]:.3f},{c[2]:.3f}:{m}:{s}\n")

out_fbx = os.path.join(OUT_DIR, NAME + ".fbx")
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.shade_smooth()
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={'MESH'},
    mesh_smooth_type='FACE', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", out_fbx)
print("BUILD-OK")
