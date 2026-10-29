"""Take a sheathed katana fused into the body mesh off the thigh bone (story cast, 2026-10-29).

    blender -b <Name>_HD_rigged.blend --python fix_scabbard_weights.py -- <Name> [--radius 0.018] [--dry 1] [--render <dir>]

The story cast (Musashi, Partner, Sensei) wear the scabbard in the sash at the left hip, sculpted
into the same mesh island as the body. Bone heat binds it to the nearest bone, the LEFT THIGH, so a
raised knee swings the whole scabbard with the leg. A scabbard is a rigid tube: find its axis from
the thigh-bound verts that sit far (> 0.15 H) from the thigh, refine it on the inliers, then re-bind
every vert within --radius (fraction of H) of that axis, over the tube's length, wholly to Hips. A
band out to --band x the radius blends toward Hips (smoothstep) so the sash does not tear where the tube leaves it.
Weights only; the rig and the mesh are untouched. Without --dry it saves the .blend and re-exports
the .fbx beside it with the project options.
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
NAME = argv[0]
opts = {"--radius": "0.018", "--band": "3.0", "--dry": "0", "--save": "1", "--render": "", "--tag": "", "--lift": "65", "--zoom": "0.75"}
for i in range(1, len(argv), 2):
    opts[argv[i]] = argv[i + 1]
MX = "mixamorig:"
rig = bpy.data.objects[f"{NAME}_Rig"]
mesh = bpy.data.objects[f"{NAME}_Body"]
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
M = mesh.matrix_world
V = np.array([tuple(M @ v.co) for v in mesh.data.vertices])
FLOOR = V[:, 2].min()
H = V[:, 2].max() - FLOOR
RAD = float(opts["--radius"]) * H
gname = {g.index: g.name for g in mesh.vertex_groups}
dom = np.array([gname[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in mesh.data.vertices])


def seg_dist(n, P):
    b = rig.data.bones[n]
    a, e = np.array(rig.matrix_world @ b.head_local), np.array(rig.matrix_world @ b.tail_local)
    ab = e - a
    t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1)


THIGH = MX + "LeftUpLeg"
far = np.where((dom == THIGH) & (seg_dist(THIGH, V) > 0.15 * H))[0]
if len(far) < 50:
    raise SystemExit(f"[{NAME}] only {len(far)} far thigh verts - no scabbard to fix")

# axis: line fit, then refit on the verts that hug it (robe flaps fall away)
P = V[far]
keep = np.ones(len(P), bool)
for tol in (0.08, 0.04, 0.025, 0.02):
    c = P[keep].mean(0)
    ax = np.linalg.svd(P[keep] - c)[2][0]
    al = (P - c) @ ax
    rad = np.linalg.norm((P - c) - al[:, None] * ax, axis=1)
    keep = rad < tol * H
lo, hi = al[keep].min() - 0.01 * H, al[keep].max() + 0.01 * H
print(f"[{NAME}] scabbard axis {np.round(ax, 3)} length {(hi - lo) / H:.3f} H, {keep.sum()}/{len(far)} far verts on it")

al = (V - c) @ ax
rad = np.linalg.norm((V - c) - al[:, None] * ax, axis=1)
on = (al > lo) & (al < hi)
core = np.where(on & (rad < RAD))[0]
BAND = float(opts["--band"]) * RAD
band = np.where(on & (rad >= RAD) & (rad < BAND))[0]
print(f"[{NAME}] core {len(core)} verts -> Hips, blend band {len(band)}; core was on "
      f"{dict(zip(*np.unique(dom[core], return_counts=True)))}")

HIPS = MX + "Hips"
if HIPS not in mesh.vertex_groups:
    mesh.vertex_groups.new(name=HIPS)
hg = mesh.vertex_groups[HIPS]


def rebind(i, w_hips):
    v = mesh.data.vertices[i]
    old = [(g.group, g.weight) for g in v.groups]
    tot = sum(w for _, w in old) or 1.0
    for gi_, _ in old:
        mesh.vertex_groups[gi_].remove([i])
    keep_ = 1.0 - w_hips
    acc = {}
    for gi_, w in old:
        acc[gi_] = acc.get(gi_, 0.0) + keep_ * w / tot
    acc[hg.index] = acc.get(hg.index, 0.0) + w_hips
    top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
    s = sum(w for _, w in top)
    for gi_, w in top:
        if w > 1e-4:
            mesh.vertex_groups[gi_].add([i], w / s, 'REPLACE')


if opts["--dry"] != "1":
    for i in core:
        rebind(int(i), 1.0)
    for i in band:
        f = (rad[i] - RAD) / (BAND - RAD)
        rebind(int(i), float(1.0 - f * f * (3 - 2 * f)))     # smoothstep out to the band edge


def aim(n, want):
    pb = rig.pose.bones[n]
    Mw = rig.matrix_world @ pb.matrix
    h = Mw.translation
    cur = (Mw.to_3x3() @ Vector((0, 1, 0))).normalized()
    R = cur.rotation_difference(want.normalized()).to_matrix().to_4x4()
    pb.matrix = rig.matrix_world.inverted() @ (Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ Mw)
    bpy.context.view_layer.update()


# facing: toes in front of heels
foot, toe = rig.data.bones[MX + "LeftFoot"], rig.data.bones[MX + "LeftToeBase"]
fwd_y = 1.0 if (rig.matrix_world @ toe.tail_local).y > (rig.matrix_world @ foot.head_local).y else -1.0

# measure: left knee raised high; how far does the scabbard core travel? (should be ~0 after the fix)
rig.data.pose_position = 'POSE'
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
lift = math.radians(float(opts["--lift"]))           # thigh angle forward of straight down
aim(THIGH, Vector((0.1 * math.sin(lift), math.sin(lift) * fwd_y, -math.cos(lift))))
aim(MX + "LeftLeg", Vector((0.0, 0.1 * fwd_y, -1.0)))
dg = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(dg)
Vp = np.array([tuple(M @ ev.data.vertices[i].co) for i in core])
mv = np.linalg.norm(Vp - V[core], axis=1) / H
print(f"[{NAME}] knee raised: scabbard core moves mean {mv.mean() * 100:.2f}% H, max {mv.max() * 100:.2f}% H")
# and the seam: edges stretched past 1.6x or squashed under 0.45x (audit_rig.py's 'extreme'), near the tube and anywhere
E = np.array([tuple(e.vertices) for e in mesh.data.edges])
Va = np.array([tuple(M @ v.co) for v in ev.data.vertices])
ratio = np.linalg.norm(Va[E[:, 0]] - Va[E[:, 1]], axis=1) / np.maximum(np.linalg.norm(V[E[:, 0]] - V[E[:, 1]], axis=1), 1e-9)
bad = (ratio > 1.6) | (ratio < 0.45)
near = on[E[:, 0]] & (rad[E[:, 0]] < 4 * RAD)
print(f"[{NAME}] knee raised: extreme edges {int(bad.sum())}, of them near the scabbard {int((bad & near).sum())}")
ae = al[E[bad & near, 0]] / H
print(f"[{NAME}]   near-scabbard extreme edges by position along the tube: {np.histogram(ae, bins=np.linspace(lo / H, hi / H, 7))[0].tolist()}")

if opts["--render"]:
    out = opts["--render"]; os.makedirs(out, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'
    cam = bpy.data.objects.new("scabcam", bpy.data.cameras.new("scabcam"))
    sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = float(opts["--zoom"]) * H
    sc.render.resolution_x, sc.render.resolution_y = 640, 640
    cen = Vector((0, 0, FLOOR + 0.5 * H))
    tag = opts["--tag"] or ("dry" if opts["--dry"] == "1" else "fixed")
    for name_, d in (("front34", Vector((0.7, 0.7 * fwd_y, 0.15))), ("side", Vector((1, 0, 0.05)))):
        d.normalize()
        cam.location = cen + d * 10
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f"scabbard_knee_up_{name_}_{tag}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)

for pb in rig.pose.bones:
    pb.matrix_basis.identity()
bpy.context.view_layer.update()
if opts["--dry"] == "1" or opts["--save"] != "1":
    raise SystemExit(0)

stem = os.path.splitext(bpy.data.filepath)[0]
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and o is not mesh and o.parent is rig]
bpy.ops.object.select_all(action='DESELECT')
for o in [rig, mesh] + weapons:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=stem + ".fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
bpy.ops.wm.save_mainfile()
print(f"[{NAME}] SCABBARD-FIX-OK -> {stem}.fbx")
