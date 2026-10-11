"""Animation master for a prepared fighter: body FBX (with weapons) + an IK control layer on the Tripo bones.

Usage: blender -b --python anim_rig.py -- <Name>_Body.fbx <out_master.blend>

Control bones added to the armature (non-deform, bone collection "Controls"):
  CTRL_root                         floor-level root for the whole character
  IK_L_Foot / IK_R_Foot             foot targets (at the ankle); the foot bones copy their rotation
  POLE_L_Knee / POLE_R_Knee         knee poles 0.4 units in front of the knees
  IK_L_Hand / IK_R_Hand             hand targets at the wrists; the hand bones copy their rotation
  POLE_L_Elbow / POLE_R_Elbow       elbow poles behind/below the elbows
IK constraints sit on the calves (chain 2) and the forearms (chain 2). Arm IK influence lives on the
custom property  arm["ik_arms"]  (0 = pure FK on the deform bones, 1 = the hand targets drive the arms),
so clips can mix: FK swings with the deform bones, IK where a hand must stay on a hilt or the ground.
Hip / spine / neck / head / clavicles / thighs-in-FK are animated on the deform bones directly.

`bake_and_export(fbx_path, frame_start, frame_end)` (also used by author_clips.py) bakes visual
transforms onto the deform bones, drops the control bones (use_armature_deform_only) and writes an
armature-only clip FBX with the rest pose keyed on REST_FRAME, exactly like build_clips.py did, so
Unity builds a valid Humanoid avatar from it.
"""
import bpy, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
BODY, OUT = args[0], args[1]
REST_FRAME = -10

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BODY)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
scene = bpy.context.scene
scene.render.fps = 60
scene.frame_start = 0

# ---------------------------------------------------------------- control bones (edit mode)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
ctrl_names = []

def add(name, head, tail, parent=None, roll=0.0):
    b = eb.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.roll = roll
    b.use_deform = False
    if parent: b.parent = eb[parent]
    ctrl_names.append(name)
    return b

def signed_angle(u, v, normal):
    a = u.angle(v)
    if u.cross(v).angle(normal) < 1: a = -a
    return a

def pole_angle(base, ik, pole_loc):
    pole_normal = (ik.tail - base.head).cross(pole_loc - base.head)
    projected = pole_normal.cross(base.tail - base.head)
    return signed_angle(base.x_axis, projected, base.tail - base.head)

root = add("CTRL_root", (0, 0, 0), (0, 0, 0.1))
fwd = Vector((1, 0, 0))   # Tripo rigs face +X in Blender
# Tripo rests the legs dead straight, which leaves the IK solver no bend direction: pre-bend each knee
# 1.2 cm forward (thigh tail + calf head). Unity rebuilds the avatar from this clip rig anyway.
for side in ("L", "R"):
    th, ca = eb[f"{side}_Thigh"], eb[f"{side}_Calf"]
    k = ca.head + fwd * 0.012
    th.tail = k; ca.head = k
    el = eb[f"{side}_Forearm"]
    # same for the elbows (A-pose arms are nearly straight): 1 cm back
    e = el.head - fwd * 0.010
    eb[f"{side}_Upperarm"].tail = e; el.head = e
poles = {}
for side in ("L", "R"):
    thigh, calf, foot = eb[f"{side}_Thigh"], eb[f"{side}_Calf"], eb[f"{side}_Foot"]
    uarm, farm, hand = eb[f"{side}_Upperarm"], eb[f"{side}_Forearm"], eb[f"{side}_Hand"]
    add(f"IK_{side}_Foot", foot.head, foot.head + Vector((0.08, 0, 0)), "CTRL_root")
    # Tripo calves end above the foot bone and forearms short of the hand (unconnected children with a fixed
    # offset). Non-deform helper bones bridge the gaps (ankle: calf tail -> foot head, wrist: forearm tail ->
    # hand head); the foot/hand hang under them and the IK solves through them, so the chain tip lands exactly
    # on the control. The exporter drops them and re-parents foot/hand to calf/forearm.
    hlp = add(f"HLP_{side}_Ankle", calf.tail, foot.head, f"{side}_Calf")
    foot.parent = hlp
    kp = calf.head + fwd * 0.4
    add(f"POLE_{side}_Knee", kp, kp + Vector((0, 0, 0.05)), "CTRL_root")
    poles[f"{side}_Calf"] = pole_angle(thigh, calf, kp)
    add(f"IK_{side}_Hand", hand.head, hand.head + (hand.tail - hand.head), "CTRL_root", roll=hand.roll)
    hlw = add(f"HLP_{side}_Wrist", farm.tail, hand.head, f"{side}_Forearm")
    hand.parent = hlw
    ep = farm.head - fwd * 0.35 + Vector((0, 0, -0.1))
    add(f"POLE_{side}_Elbow", ep, ep + Vector((0, 0, 0.05)), "CTRL_root")
    poles[f"{side}_Forearm"] = pole_angle(uarm, farm, ep)
# the feet and hand IK targets copy the deform rest orientation so the first key is neutral
for side in ("L", "R"):
    eb[f"IK_{side}_Foot"].roll = eb[f"{side}_Foot"].roll
    eb[f"IK_{side}_Foot"].tail = eb[f"IK_{side}_Foot"].head + (eb[f"{side}_Foot"].tail - eb[f"{side}_Foot"].head)

bpy.ops.object.mode_set(mode='POSE')
pb = arm.pose.bones
arm["ik_arms"] = 1.0
arm.id_properties_ui("ik_arms").update(min=0.0, max=1.0)

def ik(bone, target, pole, chain, angle, driven=None, use_tail=True):
    c = pb[bone].constraints.new('IK')
    c.target = arm; c.subtarget = target
    c.pole_target = arm; c.pole_subtarget = pole
    c.pole_angle = angle
    c.chain_count = chain
    c.use_tail = use_tail
    if driven:
        d = c.driver_add("influence").driver
        d.type = 'SCRIPTED'
        v = d.variables.new(); v.name = "ik"; v.type = 'SINGLE_PROP'
        v.targets[0].id = arm; v.targets[0].data_path = '["ik_arms"]'
        d.expression = "ik"
    return c

def copy_rot(bone, target, driven=None):
    c = pb[bone].constraints.new('COPY_ROTATION')
    c.target = arm; c.subtarget = target
    c.target_space = 'WORLD'; c.owner_space = 'WORLD'
    if driven:
        d = c.driver_add("influence").driver
        d.type = 'SCRIPTED'
        v = d.variables.new(); v.name = "ik"; v.type = 'SINGLE_PROP'
        v.targets[0].id = arm; v.targets[0].data_path = '["ik_arms"]'
        d.expression = "ik"
    return c

for side in ("L", "R"):
    # Tripo calves end above the foot bone and forearms short of the hand (unconnected children with a
    # fixed offset), so the IK sits on the foot / hand with the END EFFECTOR AT ITS HEAD and a 3-bone chain:
    # the solver then places the foot head (sole) and the wrist exactly on the controls.
    ik(f"HLP_{side}_Ankle", f"IK_{side}_Foot", f"POLE_{side}_Knee", 3, poles[f"{side}_Calf"])
    for ax in "xyz": setattr(pb[f"HLP_{side}_Ankle"], f"ik_stiffness_{ax}", 0.9)   # bend the knee and hip before the ankle
    copy_rot(f"{side}_Foot", f"IK_{side}_Foot")
    ik(f"HLP_{side}_Wrist", f"IK_{side}_Hand", f"POLE_{side}_Elbow", 3, poles[f"{side}_Forearm"], driven=True)
    for ax in "xyz": setattr(pb[f"HLP_{side}_Wrist"], f"ik_stiffness_{ax}", 0.9)
    copy_rot(f"{side}_Hand", f"IK_{side}_Hand", driven=True)

# bone collections for tidy display
coll = arm.data.collections.new("Controls")
for n in ctrl_names:
    coll.assign(arm.data.bones[n])
for b in arm.data.bones:
    if b.name not in ctrl_names:
        b.hide = False
for n in ctrl_names:
    arm.data.bones[n].show_wire = True

bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- export helper stored in the file
EXPORT_SRC = r'''
import bpy
REST_FRAME = -10
def bake_and_export(fbx_path, frame_start, frame_end, arm=None):
    """DESTRUCTIVE export (reopen the master afterwards): samples the world matrices of every deform bone
    over [frame_start, frame_end], deletes the control/helper bones (re-parenting foot/hand to calf/forearm
    with their rest offsets intact), re-keys the deform bones from the sampled matrices, keys the rest pose
    on REST_FRAME, and writes an armature-only FBX with exactly the 41 Tripo deform bones."""
    arm = arm or next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    scene = bpy.context.scene
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='OBJECT')
    deform = [b.name for b in arm.data.bones if b.use_deform]
    # hierarchy order (parents first) over deform bones only
    order = []
    def walk(b):
        if b.use_deform: order.append(b.name)
        for c in b.children: walk(c)
    for b in arm.data.bones:
        if b.parent is None: walk(b)
    frames = list(range(frame_start, frame_end + 1))
    world = {}
    for fr in frames:
        scene.frame_set(fr)
        bpy.context.view_layer.update()
        world[fr] = {n: (arm.matrix_world @ arm.pose.bones[n].matrix).copy() for n in deform}
    # strip the control layer
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for b in list(eb):
        if not b.use_deform:
            for c in list(b.children):
                if c.use_deform:
                    p = b.parent
                    while p is not None and not p.use_deform: p = p.parent
                    c.use_connect = False
                    c.parent = p
            eb.remove(b)
    bpy.ops.object.mode_set(mode='POSE')
    for pbone in arm.pose.bones:
        for c in list(pbone.constraints): pbone.constraints.remove(c)
    if arm.animation_data and arm.animation_data.action:
        arm.animation_data.action = None
    act = bpy.data.actions.new("Scene")
    arm.animation_data_create(); arm.animation_data.action = act
    inv_world = arm.matrix_world.inverted()
    for fr in frames:
        scene.frame_set(fr)
        for n in order:
            pbone = arm.pose.bones[n]
            pbone.matrix = inv_world @ world[fr][n]
            bpy.context.view_layer.update()
        for n in order:
            pbone = arm.pose.bones[n]
            pbone.keyframe_insert("location", frame=fr)
            pbone.keyframe_insert("rotation_quaternion", frame=fr)
            pbone.keyframe_insert("scale", frame=fr)
    for pbone in arm.pose.bones:
        pbone.location = (0, 0, 0); pbone.rotation_quaternion = (1, 0, 0, 0); pbone.scale = (1, 1, 1)
        pbone.keyframe_insert("location", frame=REST_FRAME)
        pbone.keyframe_insert("rotation_quaternion", frame=REST_FRAME)
        pbone.keyframe_insert("scale", frame=REST_FRAME)
    scene.frame_set(REST_FRAME)
    scene.frame_start, scene.frame_end = frame_start, frame_end
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.name = "Armature"   # the clip metas' explicit human map names the skeleton root "Armature"
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={'ARMATURE'},
        use_armature_deform_only=True, add_leaf_bones=False, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
    return fbx_path
'''
txt = bpy.data.texts.new("export_tools.py")
txt.write(EXPORT_SRC)

scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print(f"anim master saved: {OUT}; controls {ctrl_names}; pole angles {{k: round(math.degrees(v),1) for k,v in poles.items()}}")
print("RIG-OK")
