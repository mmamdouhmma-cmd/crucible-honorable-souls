"""Take the guard off a wooden sword that should not have one (story cast, 2026-10-29).

    blender -b <Rigged.blend> --python strip_guard.py -- <sword_object> [--min-ratio 1.5]

The Sensei's plain bokken (`wooden+bokken+3d+model.zip`, reference `Weathered Wooden Bokken on White.png`:
no guard, a STEPPED grip) came out of Tripo with a metal tsuba fused into the one mesh. Run this AFTER
swap_blade.py, which needs the guard to seat the sword in the fist (it aligns tsuba on tsuba).

The guard is found as the run of slices along the sword's long axis (PCA) whose width stands well above
the median slice. It is not cut out: every vertex in that run is pulled radially onto a shaft whose radius
eases from the grip's to the blade's across the run - the step in the reference - so the topology, the
skin and the export stay exactly as they were. The collapsed vertices take the UV of the nearest vertex
just past the run on the blade side, so the step reads as wood, not metal. Re-exports the .fbx beside the
.blend with the project options and saves the .blend.
"""
import bpy, sys, os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
NAME = argv[0]
opts = {"--min-ratio": "1.5"}
for i in range(1, len(argv), 2):
    opts[argv[i]] = argv[i + 1]

ob = bpy.data.objects[NAME]
me = ob.data
V = np.array([v.co[:] for v in me.vertices])
c = V.mean(0)
w, vec = np.linalg.eigh(np.cov((V - c).T))
ax = vec[:, np.argmax(w)]
t = (V - c) @ ax
L = t.max() - t.min()
u = (t - t.min()) / L                       # 0..1 along the sword

NB = 100
bins = np.minimum((u * NB).astype(int), NB - 1)
centre = np.zeros((NB, 3)); r95 = np.zeros(NB)
for b in range(NB):
    m = bins == b
    if m.sum() < 4:
        continue
    p = V[m] - np.outer(t[m], ax)
    centre[b] = np.median(p, 0)
    r95[b] = np.percentile(np.linalg.norm(p - centre[b], axis=1), 95)
med = np.median(r95[r95 > 0])
wide = r95 > med * float(opts["--min-ratio"])
wide[:5] = False; wide[-5:] = False      # a pommel or a tip is never the guard
if not wide.any():
    raise SystemExit(f"[strip] no guard found on {NAME} (median r {med:.4f})")
# the longest contiguous run of wide slices
runs, start = [], None
for b in range(NB + 1):
    on = b < NB and wide[b]
    if on and start is None:
        start = b
    if not on and start is not None:
        runs.append((start, b)); start = None
lo, hi = max(runs, key=lambda r: r[1] - r[0])
lo -= 1; hi += 1                          # one slice of margin each side: the guard's fillets
grip_side = lo - 1 if r95[lo - 1] <= r95[min(hi, NB - 1)] else min(hi, NB - 1)
blade_bin = hi if grip_side == lo - 1 else lo - 1
r_grip, r_blade = r95[grip_side], r95[blade_bin]
print(f"[strip] {NAME}: guard slices {lo}-{hi} of {NB} (r95 {r95[lo:hi].max():.4f} vs median {med:.4f}); "
      f"grip r {r_grip:.4f} at {grip_side}, blade r {r_blade:.4f} at {blade_bin}")

band = (bins >= lo) & (bins < hi)
idx = np.where(band)[0]
# shaft centre interpolated across the run (the guard's own median sits on the axis anyway)
c_lo, c_hi = centre[lo - 1], centre[min(hi, NB - 1)]
moved = 0
for i in idx:
    f = (u[i] * NB - lo) / max(hi - lo, 1)          # 0 at the low end of the run, 1 at the high end
    f = min(max(f, 0.0), 1.0)
    # grip -> blade, eased: the step sits in the middle of where the guard was
    s = f * f * (3 - 2 * f)
    if grip_side > blade_bin:
        s = 1 - s
    r_t = r_grip + (r_blade - r_grip) * s
    ctr = c_lo + (c_hi - c_lo) * f
    p = V[i] - np.outer([t[i]], ax)[0]
    d = p - ctr
    n = np.linalg.norm(d)
    if n > r_t:
        V[i] = V[i] - d + d / n * r_t
        moved += 1
for i in idx:
    me.vertices[i].co = Vector(V[i])

# wood UVs for the collapsed band: the nearest vertex just past the run on the blade side
uvl = me.uv_layers.active
if uvl is not None:
    src_bins = range(blade_bin, blade_bin + 3) if blade_bin >= hi else range(blade_bin - 2, blade_bin + 1)
    src = np.where(np.isin(bins, list(src_bins)))[0]
    vert_uv = {}
    for loop in me.loops:
        vert_uv.setdefault(loop.vertex_index, uvl.data[loop.index].uv.copy())
    src = np.array([s for s in src if s in vert_uv])
    from mathutils.kdtree import KDTree
    kd = KDTree(len(src))
    for k, s in enumerate(src):
        kd.insert(Vector(V[s]), k)
    kd.balance()
    band_set = set(idx.tolist())
    for loop in me.loops:
        if loop.vertex_index in band_set:
            _, k, _ = kd.find(Vector(V[loop.vertex_index]))
            uvl.data[loop.index].uv = vert_uv[int(src[k])]
me.update()
print(f"[strip] collapsed {moved} of {len(idx)} band verts onto the stepped shaft")

rig = ob.parent
mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent is rig and any(m.type == 'ARMATURE' for m in o.modifiers))
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, mesh, ob):
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
stem = os.path.splitext(bpy.data.filepath)[0]
bpy.ops.export_scene.fbx(filepath=stem + ".fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, armature_nodetype='NULL',
    bake_anim=False, path_mode='STRIP', embed_textures=False, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
bpy.ops.wm.save_mainfile()
print("[strip] STRIP-OK", stem + ".fbx")
