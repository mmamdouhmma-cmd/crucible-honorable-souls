"""Give a fighter a NEW BODY on Salaadin's exact rig (the user's call, 2026-10-26: "same rigging
bone/joints positions as salaadin, all bones sizes and positions as salaadin").

    blender -b --python rebody.py -- <Name> <new_mesh.fbx> <old_body.fbx> <salaadin_body.fbx> <out.fbx> <render_dir>

Unlike fit_metarig.py (which fits a rig to each mesh), this keeps the rig IDENTICAL across
fighters - Salaadin_Body.fbx's 79-bone armature is imported untouched - and fits the MESH to it:

  1. The new mesh is decimated (if heavy), grounded at z=0, centred, scaled to the rig's height,
     and its feet y-aligned to the donor body's feet.
  2. If an arm hangs at a different angle than the rig's rest (the fantasy barbarian is A-posed
     at ~-38 deg against the rig's -10), the arm's vertices are rotated up about the shoulder
     joint and scaled along the arm so the fingertips land at the rig's, with a smooth falloff
     so the shoulder seam blends. Measured per side from band centroids, skipped when within
     8 deg (the samurai matches already - Tripo's canonical pose is Salaadin's own).
  3. Weights are TRANSFERRED from Salaadin's skinned body (nearest-face interpolation), never
     bone-heat - auto-weights failed on these Tripo meshes before (CLAUDE.md), and the donor is
     skinned to this exact skeleton. Unweighted leftovers copy their nearest weighted neighbour.
  4. The fighter's existing weapon is lifted out of his OLD body FBX and re-seated on the new
     rig's hand bone with the same bone-local placement (bone-parent frames measure from the
     TAIL, per salaadin_body.py).

Export options are salaadin_body.py's exactly; overwrite the Model FBX IN PLACE so the GUID and
every scene reference survive.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix, Quaternion, kdtree

args = sys.argv[sys.argv.index("--") + 1:]
NAME, NEW_FBX, OLD_FBX, SAL_FBX, OUT, RENDER_DIR = args[:6]
TARGET_VERTS = 123000
os.makedirs(RENDER_DIR, exist_ok=True)


def only_new(before):
    return set(bpy.data.objects) - before


# --- 1. Salaadin's rig + donor body ------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SAL_FBX)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
donor = max((o for o in bpy.data.objects if o.type == 'MESH'
             and any(m.type == 'ARMATURE' for m in o.modifiers)), key=lambda o: len(o.data.vertices))
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o is not donor]:
    bpy.data.objects.remove(o)                      # Salaadin's falchion and shield
rig.name = rig.data.name = f"{NAME}_Rig"
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()


def bone_head(n):
    return rig.matrix_world @ rig.data.bones[n].head_local


def bone_tail(n):
    return rig.matrix_world @ rig.data.bones[n].tail_local


# the height target is the DONOR BODY's crown, not a leaf bone's tail (HeadTop_End reaches past it)
RIG_H = max((donor.matrix_world @ v.co).z for v in donor.data.vertices)
donor_feet_y = [ (donor.matrix_world @ v.co).y for v in donor.data.vertices
                 if (donor.matrix_world @ v.co).z < 0.10 ]
DONOR_FEET_Y = sum(donor_feet_y) / len(donor_feet_y)
print(f"{NAME}: rig {len(rig.data.bones)} bones, donor crown z {RIG_H:.3f}, donor {len(donor.data.vertices)} verts")

# --- 2. the new mesh ---------------------------------------------------------------------------
before = set(bpy.data.objects)
bpy.ops.wm.fbx_import(filepath=NEW_FBX)
newmeshes = [o for o in only_new(before) if o.type == 'MESH']
mesh = max(newmeshes, key=lambda o: len(o.data.vertices))
for o in only_new(before):
    if o is not mesh:
        bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

if len(mesh.data.vertices) > TARGET_VERTS * 1.5:
    dec = mesh.modifiers.new("dec", 'DECIMATE')
    dec.ratio = TARGET_VERTS / len(mesh.data.vertices)
    bpy.ops.object.modifier_apply(modifier=dec.name)
    print(f"  decimated to {len(mesh.data.vertices)} verts")

# ground, centre, scale to the rig's height, feet y onto the donor's
co = [v.co.copy() for v in mesh.data.vertices]
zs = [c.z for c in co]
floor, top = min(zs), max(zs)
s = RIG_H / (top - floor)
feet = [c for c in co if (c.z - floor) * s < 0.10]
cx = sum(c.x for c in co) / len(co)
fy = sum(c.y for c in feet) / len(feet)
for v in mesh.data.vertices:
    v.co.x = (v.co.x - cx) * s
    v.co.y = (v.co.y - fy) * s + DONOR_FEET_Y
    v.co.z = (v.co.z - floor) * s
mesh.data.update()
print(f"  aligned: scale x{s:.4f}, floor {floor:.3f}->0, feet y -> {DONOR_FEET_Y:.3f}")


# --- 3. arm warp: measure each arm's axis, rotate it onto the rig's ----------------------------
def arm_axis_mesh(sgn):
    """Fit the arm direction from OUTER band centroids only (|x| 0.26..0.46) - the inner bands
    catch hip gear and shoulder armour (a scabbard at |x| 0.18 once read as a 36-deg droop)."""
    pts = []
    for v in mesh.data.vertices:
        c = v.co
        if sgn * c.x >= 0.18 and c.z > 0.45:
            pts.append(c.copy())
    lo = [p for p in pts if 0.26 <= sgn * p.x < 0.34]
    hi = [p for p in pts if 0.36 <= sgn * p.x < 0.46]
    if len(lo) < 20 or len(hi) < 20:
        return None, None
    a = sum(lo, Vector()) / len(lo)
    b = sum(hi, Vector()) / len(hi)
    return (b - a).normalized(), pts


for side, sgn in (("Left", 1), ("Right", -1)):
    P = bone_head(f"mixamorig:{side}Arm")
    axis_rig = (bone_head(f"mixamorig:{side}Hand") - P).normalized()
    tipb = rig.data.bones.get(f"mixamorig:{side}HandMiddle4") or rig.data.bones.get(f"mixamorig:{side}HandMiddle3")
    t_rig = ((rig.matrix_world @ tipb.tail_local) - P).dot(axis_rig)
    axis_mesh, pts = arm_axis_mesh(sgn)
    if axis_mesh is None:
        print(f"  {side} arm: too few verts to measure, left alone")
        continue
    ang = math.degrees(axis_mesh.angle(axis_rig))
    if ang < 8.0:
        print(f"  {side} arm: {ang:.1f} deg off the rig's rest - left alone")
        continue
    R = axis_mesh.rotation_difference(axis_rig).to_matrix()
    # the arm's reach along its own axis, sampled near the axis so fur does not vote
    ts = sorted((p - P).dot(axis_mesh) for p in pts if (p - P - axis_mesh * (p - P).dot(axis_mesh)).length < 0.08)
    t_mesh = ts[int(len(ts) * 0.985)] if ts else t_rig
    scale = max(0.85, min(1.15, t_rig / t_mesh))
    n = 0
    for v in mesh.data.vertices:
        d = v.co - P
        t = d.dot(axis_mesh)
        r = (d - axis_mesh * t).length
        if t < -0.02 or t > t_mesh * 1.35 or r > 0.14:
            continue
        w = min(1.0, max(0.0, (t - 0.015) / 0.055)) * min(1.0, max(0.0, (0.14 - r) / 0.04))
        if w <= 0.0:
            continue
        rot = P + R @ d
        drot = rot - P
        trot = drot.dot(axis_rig)
        stretched = P + axis_rig * (trot * scale) + (drot - axis_rig * trot)
        v.co = v.co.lerp(stretched, w)
        n += 1
    mesh.data.update()
    print(f"  {side} arm: rotated {ang:.1f} deg onto the rig's rest, length x{scale:.3f}, {n} verts")

# --- 4. weights from the donor -----------------------------------------------------------------
for g in donor.vertex_groups:
    if mesh.vertex_groups.get(g.name) is None:
        mesh.vertex_groups.new(name=g.name)
dt = mesh.modifiers.new("dt", 'DATA_TRANSFER')
dt.object = donor
dt.use_vert_data = True
dt.data_types_verts = {'VGROUP_WEIGHTS'}
dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'
dt.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.modifier_apply(modifier=dt.name)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)

weighted, unweighted = [], []
for v in mesh.data.vertices:
    (weighted if any(g.weight > 1e-4 for g in v.groups) else unweighted).append(v.index)
if unweighted:
    kd = kdtree.KDTree(len(weighted))
    for i in weighted:
        kd.insert(mesh.data.vertices[i].co, i)
    kd.balance()
    gname = {g.index: g.name for g in mesh.vertex_groups}
    for i in unweighted:
        _, j, _ = kd.find(mesh.data.vertices[i].co)
        for g in mesh.data.vertices[j].groups:
            if g.weight > 1e-4:
                mesh.vertex_groups[gname[g.group]].add([i], g.weight, 'REPLACE')
print(f"  weights: {len(unweighted)} of {len(mesh.data.vertices)} verts filled from a neighbour")

bpy.data.objects.remove(donor)
mesh.name = mesh.data.name = f"{NAME}_Body"
if mesh.data.materials:
    mesh.data.materials[0].name = f"{NAME}_Body"
mesh.parent = rig
mesh.matrix_parent_inverse = Matrix.Identity(4)
mod = mesh.modifiers.new("Armature", 'ARMATURE')
mod.object = rig

# --- 5. the old body's weapon, re-seated on the new rig's hand ---------------------------------
before = set(bpy.data.objects)
bpy.ops.wm.fbx_import(filepath=OLD_FBX)
added = only_new(before)
old_rig = next(o for o in added if o.type == 'ARMATURE')
old_rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
weapons = [o for o in added if o.type == 'MESH'
           and not any(m.type == 'ARMATURE' for m in o.modifiers)]


def tail_frame(armobj, bone):
    pb = armobj.pose.bones[bone]
    return armobj.matrix_world @ pb.matrix @ Matrix.Translation(Vector((0, pb.bone.length, 0)))


for w in weapons:
    hand = "mixamorig:LeftHand" if w.name.endswith("_L") else "mixamorig:RightHand"
    rel = tail_frame(old_rig, hand).inverted() @ w.matrix_world
    wname = w.name
    w.parent = rig
    w.parent_type = 'BONE'
    w.parent_bone = hand
    w.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    w.matrix_basis = rel
    bpy.context.view_layer.update()
    near = min(((w.matrix_world @ v.co) - bone_head(hand)).length
               for i, v in enumerate(w.data.vertices) if i % 7 == 0)
    print(f"  weapon {wname} -> {hand}, nearest vert to the hand {near:.3f} (healthy 0.02-0.24)")
for o in added:
    if o.type != 'MESH' or any(m.type == 'ARMATURE' for m in o.modifiers):
        bpy.data.objects.remove(o)
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and o is not mesh]

# --- 6. check renders + export ------------------------------------------------------------------
scn = bpy.context.scene
scn.render.resolution_x, scn.render.resolution_y = 640, 760
try:
    scn.render.engine = 'BLENDER_WORKBENCH'
except TypeError as e:
    print(e)
scn.display.shading.light = 'STUDIO'
scn.display.shading.color_type = 'OBJECT'
mesh.color = (0.55, 0.55, 0.58, 1)
for i, w in enumerate(weapons):
    w.color = (0.85, 0.75, 0.25, 1)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scn.collection.objects.link(cam)
scn.camera = cam
cam.data.type = 'ORTHO'
dg = bpy.context.evaluated_depsgraph_get()
lo = Vector((1e9,) * 3)
hi = Vector((-1e9,) * 3)
for o in [mesh] + weapons:
    ev = o.evaluated_get(dg)
    for c in ev.bound_box:
        p = ev.matrix_world @ Vector(c)
        for i in range(3):
            lo[i] = min(lo[i], p[i])
            hi[i] = max(hi[i], p[i])
ctr = (lo + hi) / 2
size = hi - lo
for view, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0)))):
    cam.location = ctr + d * max(size) * 3
    cam.data.ortho_scale = max(size) * 1.1
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = os.path.join(RENDER_DIR, f"{NAME}_rest_{view}.png")
    bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(cam)

bpy.ops.object.select_all(action='DESELECT')
for o in [rig, mesh] + weapons:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", OUT, f"({os.path.getsize(OUT) // 1024} KB)")
print("REBODY-OK")
