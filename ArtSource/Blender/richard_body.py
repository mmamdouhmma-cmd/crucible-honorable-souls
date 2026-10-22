"""Build Richard's Unity body FBX: the Mixamo-auto-rigged knight with his sword and shield
bone-parented to the hands.

Usage: blender -b --python richard_body.py -- <rigged.fbx> <sword.fbx> <shield.fbx> <out_fbx> <render_dir> [pose_frame]

The rig is Mixamo's (65 bones, "mixamorig:" names), not the Tripo skeleton Bjorn and Miyamoto use,
so the hand frames are read from the bones rather than from attach_weapons.py's Tripo assumptions:

    along  = the hand bone's own Y, wrist -> knuckles
    palm   = the hand bone's Z; in the T-pose rest it points DOWN, the way the palms face
    thumb  = the hand bone's X for the right hand, mirrored for the left

The sword's handle (prep_weapon.py: +Z toward the tip, +Y the edge) runs along the thumb axis, which is
how a fist holds a hilt; the shield (prep_shield.py: +Z the top, +Y the boss) hangs in the shield plane
with its face turned away from the palm, the way a fist on the grip bar carries it.

Renders <render_dir>/Richard_<view>.png in the rest pose and at pose_frame of the imported clip so the
mount can be checked, then exports the body WITHOUT animation.
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
RIG, SWORD, SHIELD, OUT, RENDER_DIR = args[:5]
POSE_FRAME = int(args[5]) if len(args) > 5 else 0
os.makedirs(RENDER_DIR, exist_ok=True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)

# --- mount tuning (hand-frame units are metres; the knight is 1.85 m tall) ------------------
# position: how far along the bone the grip sits, and how far off its axis toward the palm / thumb
# The tweak is a rotation in the weapon's own frame AFTER the axis mapping, and because the mapping
# is written in the hand bone's axes it means the same thing in every pose: Rx(t) swings the weapon's
# +Z from the axis_z direction toward -axis_y (t > 0) or +axis_y (t < 0).
# NOTE (2026-10-21): the final say on this mount is Unity's, not this table. CrucibleCharacterSetup's
# ShieldWeaponMount re-applies a tuned local transform to the weapon after every Rebuild Player<N>, because
# the mount had to be solved against the LIVE rig (Richard's hilt sat 0.08 world above his fist; the falchion
# was rolled edge-up). Re-running this script is still safe - the Unity override lands on top of whatever it
# bakes - but change the numbers THERE, not here, or the two will disagree.
MOUNT = {
    # The blade runs out of the fist along the thumb, but the thumb axis leans 17 degrees back toward
    # the elbow, so a bare mount drove the blade through the forearm. -20 degrees squares it with the
    # forearm: in the idle the sword stands up and forward at about 50 degrees, clear of the arm.
    "sword":  dict(hand="mixamorig:RightHand", along=0.055, palm=0.010, thumb=0.0,
                   axis_z="thumb", axis_y="along", tweak=(-20.0, 0.0, 0.0)),
    # Kite shield: point DOWN and the boss at the opponent. Top (+Z) on the thumb, and the face on
    # -Y, NOT +Y: prep_shield.py's boss test picks the side whose centre band bulges most and the
    # grip bar out-bulges the dome, so on this asset +Y is the arm side. -24 degrees then stands it
    # upright - in the idle the boss lands within a degree of the fighter's forward, point straight
    # down. The 0.16 along is the depth of the shield itself: its origin is the TIP OF THE BOSS and
    # the whole mesh lies behind that, so the grip bar only meets the fist once it is pushed out.
    "shield": dict(hand="mixamorig:LeftHand",  along=0.235, palm=0.0, thumb=0.088,
                   axis_z="thumb", axis_y="-along", tweak=(-24.0, 0.0, 0.0)),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=RIG)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
arm.data.pose_position = 'REST'          # mount in the bind pose; the clip rides on top of it
bpy.context.view_layer.update()
arm.name = arm.data.name = "Richard_Rig"
body.name = body.data.name = "Richard_Body"
if body.data.materials:
    body.data.materials[0].name = "Richard_Body"
print(f"rig {len(arm.data.bones)} bones, body {len(body.data.vertices)} verts")


def hand_frame(bone_name):
    b = arm.data.bones[bone_name]
    M = (arm.matrix_world @ b.matrix_local).to_3x3()
    head = arm.matrix_world @ b.head_local
    x = (M @ Vector((1, 0, 0))).normalized()
    along = (M @ Vector((0, 1, 0))).normalized()
    palm = (M @ Vector((0, 0, 1))).normalized()
    thumb = x if "Right" in bone_name else -x
    length = (arm.matrix_world @ b.tail_local - head).length
    return head, thumb, palm, along, length


def mount(path, name, spec):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=path)
    obj = next(o for o in set(bpy.data.objects) - before if o.type == 'MESH')
    for o in set(bpy.data.objects) - before - {obj}:
        bpy.data.objects.remove(o)
    obj.name = obj.data.name = name
    if obj.data.materials:
        obj.data.materials[0].name = name
    head, thumb, palm, along, length = hand_frame(spec["hand"])
    axes = {"thumb": thumb, "-thumb": -thumb, "palm": palm, "-palm": -palm, "along": along, "-along": -along}
    zax = axes[spec["axis_z"]]
    yax = axes[spec["axis_y"]]
    yax = (yax - zax * yax.dot(zax)).normalized()
    xax = yax.cross(zax).normalized()
    R = Matrix((xax, yax, zax)).transposed().to_4x4()
    R = R @ Matrix.Rotation(math.radians(spec["tweak"][0]), 4, 'X') \
          @ Matrix.Rotation(math.radians(spec["tweak"][1]), 4, 'Y') \
          @ Matrix.Rotation(math.radians(spec["tweak"][2]), 4, 'Z')
    grip = head + along * spec["along"] + palm * spec["palm"] + thumb * spec["thumb"]
    want = Matrix.Translation(grip) @ R
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = spec["hand"]
    obj.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    # a bone parent measures from the bone TAIL, so the child's basis is taken against that frame
    pb = arm.pose.bones[spec["hand"]]
    bone_space = arm.matrix_world @ pb.matrix @ Matrix.Translation(Vector((0, pb.bone.length, 0)))
    obj.matrix_basis = bone_space.inverted() @ want
    bpy.context.view_layer.update()
    print(f"{name}: on {spec['hand']} at {tuple(round(v,3) for v in grip)} "
          f"(z={spec['axis_z']}, y={spec['axis_y']}, tweak {spec['tweak']})")
    return obj


sword = mount(SWORD, "Richard_Sword_R", MOUNT["sword"])
shield = mount(SHIELD, "Richard_Shield_L", MOUNT["shield"])

# --- check renders ---------------------------------------------------------------------------
scn = bpy.context.scene
scn.render.resolution_x, scn.render.resolution_y = 640, 760
try:
    scn.render.engine = 'BLENDER_WORKBENCH'
except TypeError as e:
    print(e)
scn.display.shading.light = 'STUDIO'
scn.display.shading.color_type = 'OBJECT'
body.color = (0.55, 0.55, 0.58, 1); sword.color = (0.85, 0.75, 0.25, 1); shield.color = (0.8, 0.2, 0.2, 1)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scn.collection.objects.link(cam); scn.camera = cam
cam.data.type = 'ORTHO'


def render(tag):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in (body, sword, shield):
        ev = o.evaluated_get(dg)
        for c in ev.bound_box:
            w = ev.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i]); hi[i] = max(hi[i], w[i])
    ctr = (lo + hi) / 2; size = hi - lo
    for view, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0)))):
        cam.location = ctr + d * max(size) * 3
        cam.data.ortho_scale = max(size) * 1.1
        cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(RENDER_DIR, f"Richard_{tag}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print("render:", scn.render.filepath)


render("rest")
if POSE_FRAME and arm.animation_data and arm.animation_data.action:
    arm.data.pose_position = 'POSE'
    scn.frame_set(POSE_FRAME)
    bpy.context.view_layer.update()
    render(f"pose{POSE_FRAME}")

arm.data.pose_position = 'REST'
bpy.context.view_layer.update()

# --- export ------------------------------------------------------------------------------------
if arm.animation_data:
    arm.animation_data_clear()
bpy.ops.object.select_all(action='DESELECT')
for o in (arm, body, sword, shield):
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.data.objects.remove(cam)
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", OUT, f"({os.path.getsize(OUT)//1024} KB)")
print("RICHARD-OK")
