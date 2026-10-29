"""Swap a rig's sword mesh for another sword, keeping the hand placement (story cast, 2026-10-29).

    blender -b <Rigged.blend> --python swap_blade.py -- <old_object> <new_name> <new_sword.fbx> <out_dir> [--length 1.0] [--tris 16000]

Used to put the wooden swords in place of the placeholder katana that hd_rig.py seated in the fist.
Each sword is measured the same way: its long axis (PCA), the TSUBA as the widest slice along it (on a
bokken with no guard, the step where the grip meets the blade: the widest slice still lands at the
blade root), the GRIP as the shorter side of it, and the CURVE as the way the blade's midpoint bows off
the tsuba->tip line. The new sword is laid into the old object's local frame tsuba-on-tsuba, grip
toward grip, curve toward curve, scaled to the old sword's overall length x --length. Its mesh data
then replaces the old object's, so the object, bone parent, matrix and fist seat are untouched; the
object and its material are renamed <new_name>. Writes <out_dir>/<new_name>.fbm/<new_name>_basecolor/
_normal/_metallic/_roughness from the new sword's own maps, saves the .blend and re-exports the .fbx
beside it with the project options.
"""
import bpy, sys, os, shutil, glob
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
OLD, NEW, SRC, OUT = argv[:4]
opts = {"--length": "1.0", "--tris": "16000"}
for i in range(4, len(argv), 2):
    opts[argv[i]] = argv[i + 1]


def frame(V):
    """(tsuba point, unit axis toward the grip end, unit curve direction, total length) in V's space."""
    c = V.mean(0)
    w, vec = np.linalg.eigh(np.cov((V - c).T))
    ax = vec[:, np.argmax(w)]
    t = (V - c) @ ax
    perp = (V - c) - np.outer(t, ax)
    bins = np.linspace(t.min(), t.max(), 81)
    width = np.zeros(80)
    cen = np.zeros((80, 3))
    for i in range(80):
        m = (t >= bins[i]) & (t < bins[i + 1])
        if m.sum() > 3:
            p = perp[m]
            cen[i] = p.mean(0)
            width[i] = np.linalg.norm(p - cen[i], axis=1).max()
    # the widest slice, searched in the middle 60% (the ends are a pommel and a tip, never the guard)
    lo, hi = 12, 68
    ti = lo + int(np.argmax(width[lo:hi]))
    tt = (bins[ti] + bins[ti + 1]) / 2
    grip_low = (tt - t.min()) < (t.max() - tt)
    to_grip = -ax if grip_low else ax
    tsuba = c + ax * tt + cen[ti]
    # curve: the blade's middle slice centroid relative to the straight line tsuba -> tip
    tip_t = t.max() if grip_low else t.min()
    mid_t = (tt + tip_t) / 2
    mi = int(np.clip(np.searchsorted(bins, mid_t) - 1, 0, 79))
    ti2 = 2 if not grip_low else 77
    line_mid = (cen[ti] + cen[ti2]) / 2
    bow = cen[mi] - line_mid
    bow = bow - ax * (bow @ ax)
    n = np.linalg.norm(bow)
    bow = bow / n if n > 1e-9 else np.cross(ax, [0, 0, 1])
    return tsuba, to_grip, bow, t.max() - t.min(), width[ti]


old = bpy.data.objects[OLD]
Vo = np.array([v.co[:] for v in old.data.vertices])          # old object's LOCAL space
o_ts, o_ax, o_bow, o_len, o_w = frame(Vo)

before = set(bpy.data.objects)
bpy.ops.wm.fbx_import(filepath=SRC)
added = [o for o in set(bpy.data.objects) - before]
meshes = [o for o in added if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
new = bpy.context.view_layer.objects.active
for o in added:
    if o is not new:
        bpy.data.objects.remove(o)
new.data.transform(new.matrix_world)
new.matrix_world = Matrix.Identity(4)
tris = sum(len(p.vertices) - 2 for p in new.data.polygons)
if tris > int(opts["--tris"]) * 1.2:
    mod = new.modifiers.new("dec", 'DECIMATE'); mod.ratio = int(opts["--tris"]) / tris
    bpy.ops.object.modifier_apply(modifier=mod.name)
Vn = np.array([v.co[:] for v in new.data.vertices])
n_ts, n_ax, n_bow, n_len, n_w = frame(Vn)


def basis(ax, bow):
    a = Vector(ax).normalized(); b = Vector(bow); b = (b - a * b.dot(a)).normalized()
    return Matrix((a, b, a.cross(b))).transposed()             # columns: axis, bow, third


s = o_len / n_len * float(opts["--length"])
R = basis(o_ax, o_bow) @ basis(n_ax, n_bow).transposed()
M = Matrix.Translation(Vector(o_ts)) @ (R.to_4x4() @ Matrix.Scale(s, 4)) @ Matrix.Translation(-Vector(n_ts))
new.data.transform(M)
print(f"[swap] old len {o_len:.4f} guard w {o_w:.4f}; new len {n_len:.4f} guard w {n_w:.4f}; scale {s:.4f}")

# textures: the new sword's own maps, renamed to <NEW>_<kind>
fbm = glob.glob(os.path.splitext(SRC)[0] + ".fbm")
tex_dir = os.path.join(OUT, NEW + ".fbm"); os.makedirs(tex_dir, exist_ok=True)
for f in (glob.glob(os.path.join(fbm[0], "*")) if fbm else []):
    low = os.path.basename(f).lower()
    for kind in ("basecolor", "normal", "metallic", "roughness"):
        if low.split(".")[0].endswith(kind):
            shutil.copy(f, os.path.join(tex_dir, f"{NEW}_{kind}{os.path.splitext(f)[1].lower()}"))

olddata = old.data
old.data = new.data
bpy.data.objects.remove(new)
bpy.data.meshes.remove(olddata)
old.name = old.data.name = NEW + "_R"
mat = old.data.materials[0] if old.data.materials else bpy.data.materials.new(NEW)
mat.name = NEW
old.data.materials.clear(); old.data.materials.append(mat)

rig = old.parent
mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent is rig and any(m.type == 'ARMATURE' for m in o.modifiers))
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
rig.data.pose_position = 'POSE'
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, mesh, old):
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
stem = os.path.splitext(bpy.data.filepath)[0]
bpy.ops.export_scene.fbx(filepath=stem + ".fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, armature_nodetype='NULL',
    bake_anim=False, path_mode='STRIP', embed_textures=False, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
bpy.ops.wm.save_mainfile()

# check render: the fist round the grip, two views, textured
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
bsdf = None
mat.use_nodes = True
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
bc = glob.glob(os.path.join(tex_dir, f"{NEW}_basecolor.*"))
if bc:
    tx = mat.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(bc[0])
    mat.node_tree.links.new(tx.outputs[0], bsdf.inputs[0])
    sc.display.shading.color_type = 'TEXTURE'
cam = bpy.data.objects.new("swcam", bpy.data.cameras.new("swcam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
W = old.matrix_world
ts_w = W @ Vector(o_ts)
ax_w = (W.to_3x3() @ Vector(o_ax)).normalized()
Lw = o_len * W.to_scale()[0]
H = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
rd = os.path.join(OUT, "renders"); os.makedirs(rd, exist_ok=True)
for tag, ctr, size, res in (("fist", ts_w + ax_w * 0.12 * Lw, 0.45 * Lw, (800, 800)),
                            ("whole", Vector((0, 0, 0.55 * H)), 1.25 * H, (900, 900))):
    for k, d in enumerate((Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, -1)))):
        cam.data.ortho_scale = size
        cam.location = ctr - d * 10
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = os.path.join(rd, f"{NEW}_{tag}_{k}.png")
        bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam)
print(f"[swap] SWAP-OK {old.name} -> {stem}.fbx")
