"""Take a hanging cape off the arm bones (Kojiro, 2026-10-29).

    blender -b <Name>_HD_rigged.blend --python fix_cape_weights.py -- <Name> [--below 0.10] [--behind 0.05] [--render <dir>]

Bone heat binds a cape that hangs at the fighter's sides to the nearest bone, which is the arm, so
raising the arms pulls the cape out sideways into flat fins. A kimono SLEEVE hangs under the arm
too, but at the arm's own depth; the cape hangs BEHIND it. So a vertex whose dominant bone is an
arm bone, which sits more than --below (fraction of height) under that bone and more than --behind
behind it, is re-bound wholly to the spine bone nearest it in height. Weights only; the rig and the
mesh are untouched. Saves the .blend and re-exports the .fbx beside it with the project options.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
NAME = argv[0]
opts = {"--below": "0.10", "--behind": "0.05", "--render": ""}
for i in range(1, len(argv), 2):
    opts[argv[i]] = argv[i + 1]
MX = "mixamorig:"
rig = bpy.data.objects[f"{NAME}_Rig"]
mesh = bpy.data.objects[f"{NAME}_Body"]
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
H = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
BELOW, BEHIND = float(opts["--below"]) * H, float(opts["--behind"]) * H

# which way is the back: the heels sit behind the toes
foot = rig.data.bones[MX + "LeftFoot"]
toe = rig.data.bones[MX + "LeftToeBase"]
fwd_y = math.copysign(1.0, (rig.matrix_world @ toe.tail_local).y - (rig.matrix_world @ foot.head_local).y)

ARM = [MX + s + n for s in ("Left", "Right") for n in ("Shoulder", "Arm", "ForeArm")]
SPINE = [MX + n for n in ("Hips", "Spine", "Spine1", "Spine2")]
seg = {n: (rig.matrix_world @ rig.data.bones[n].head_local, rig.matrix_world @ rig.data.bones[n].tail_local)
       for n in ARM + SPINE}


def nearest_on(n, p):
    a, b = seg[n]
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return a + ab * t


gi = {g.name: g.index for g in mesh.vertex_groups}
gname = {g.index: g.name for g in mesh.vertex_groups}
moved = 0
for v in mesh.data.vertices:
    if not v.groups:
        continue
    dom = max(v.groups, key=lambda g: g.weight)
    n = gname[dom.group]
    if n not in ARM:
        continue
    p = mesh.matrix_world @ v.co
    q = nearest_on(n, p)
    below = q.z - p.z
    behind = (q.y - p.y) * fwd_y          # positive when p is behind the bone
    if below < BELOW or behind < BEHIND:
        continue
    sp = min(SPINE, key=lambda s: abs(((seg[s][0] + seg[s][1]) / 2).z - p.z))
    for g in list(v.groups):
        mesh.vertex_groups[g.group].remove([v.index])
    mesh.vertex_groups[sp].add([v.index], 1.0, 'REPLACE')
    moved += 1
print(f"[{NAME}] cape: re-bound {moved} arm-bound verts to the spine (below {BELOW:.3f}, behind {BEHIND:.3f})")

# a light smooth across the seam so the cape does not tear where sleeve meets cape
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5, repeat=2)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
bpy.ops.object.mode_set(mode='OBJECT')

stem = os.path.splitext(bpy.data.filepath)[0]


def aim(n, want):
    pb = rig.pose.bones[n]
    Mw = rig.matrix_world @ pb.matrix
    h = Mw.translation
    cur = (Mw.to_3x3() @ Vector((0, 1, 0))).normalized()
    R = cur.rotation_difference(want.normalized()).to_matrix().to_4x4()
    pb.matrix = rig.matrix_world.inverted() @ (Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ Mw)
    bpy.context.view_layer.update()


if opts["--render"]:
    out = opts["--render"]; os.makedirs(out, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    cam = bpy.data.objects.new("capecam", bpy.data.cameras.new("capecam"))
    sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.5 * H
    sc.render.resolution_x, sc.render.resolution_y = 720, 820
    rig.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    for s_, sx in (("Left", 1), ("Right", -1)):
        aim(MX + s_ + "Arm", Vector((0.25 * sx, 0.0, 1.0)))
        aim(MX + s_ + "ForeArm", Vector((0.15 * sx, -0.1, 1.0)))
    c = Vector((0, 0, 0.6 * H))
    for tag, d in (("front", Vector((0, fwd_y, 0))), ("side", Vector((1, 0, 0))), ("back", Vector((0, -fwd_y, 0)))):
        view = -d   # camera looks back along d
        cam.location = c + d * 10
        cam.rotation_euler = view.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f"cape_arms_raised_{tag}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)

rig.data.pose_position = 'POSE'
for pb in rig.pose.bones:
    pb.matrix_basis.identity()
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
print(f"[{NAME}] CAPE-FIX-OK -> {stem}.fbx")
