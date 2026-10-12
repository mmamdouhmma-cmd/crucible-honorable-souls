"""Turn a one-handed Mixamo carry (weapon held out at the right hip) into a two-handed carry in front of
the body, keeping the clip's motion: a constant rotation offset is added to the right upper arm (and a
smaller one to the forearm) on every frame so the right hand ends up in front of the belly, where the
left-hand IK (TwoHandGrip in Unity) can reach the haft.

Usage: blender -b --python mixamo_carry2h.py -- <in.fbx> <out.fbx> [fwd=0.32] [height=-0.08] [side=0.05]

The offset is solved numerically on the clip's first frame: grid search over local X/Z rotations of
RightArm plus a forearm bend, minimising the distance from the right hand to the target point
(hips + forward*fwd + up*height + right*side, metres), then applied to all keys. The export keeps the
Mixamo skeleton and 30 fps so Unity imports it exactly like the original.
"""
import bpy, sys, math
from mathutils import Vector, Quaternion, Euler, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
FWD = float(args[2]) if len(args) > 2 else 0.32
HEIGHT = float(args[3]) if len(args) > 3 else -0.08
SIDE = float(args[4]) if len(args) > 4 else 0.05

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
act = bpy.data.actions[0]
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
scene = bpy.context.scene
scene.frame_start, scene.frame_end = f0, f1
scene.render.fps = 30
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
pb = arm.pose.bones
def B(n): return next(b for b in pb if b.name.endswith(n))
hips, uarm, farm, hand = B("Hips"), B("RightArm"), B("RightForeArm"), B("RightHand")
for b in (uarm, farm):
    b.rotation_mode = 'QUATERNION'

def world(b): return (arm.matrix_world @ b.matrix).translation

scene.frame_set(f0); bpy.context.view_layer.update()
hm = arm.matrix_world @ hips.matrix
# Mixamo clips face world -Y after the Blender import (the walks travel along -Y)
fwd = Vector((0, -1, 0))
up = Vector((0, 0, 1))
right = fwd.cross(up).normalized()
target = hm.translation + fwd * FWD + up * HEIGHT + right * SIDE
print(f"hips {tuple(round(v,3) for v in hm.translation)} fwd {tuple(round(v,2) for v in fwd)} target {tuple(round(v,3) for v in target)}; hand now {tuple(round(v,3) for v in world(hand))} (dist {(world(hand)-target).length:.3f})")

base_u = uarm.rotation_quaternion.copy()
base_f = farm.rotation_quaternion.copy()
best = (1e9, None, None)
for ax in range(-80, 81, 8):
  for ay in range(-40, 41, 10):
    for az in range(-80, 81, 8):
        for fb in (0, 20, 40):
            du = Euler((math.radians(ax), math.radians(ay), math.radians(az)), 'XYZ').to_quaternion()
            df = Euler((math.radians(fb), 0, 0), 'XYZ').to_quaternion()
            uarm.rotation_quaternion = base_u @ du
            farm.rotation_quaternion = base_f @ df
            bpy.context.view_layer.update()
            d = (world(hand) - target).length
            if d < best[0]:
                best = (d, du.copy(), df.copy())
d, du, df = best
print(f"best offset: dist {d:.3f}, upper arm {tuple(round(math.degrees(a),1) for a in du.to_euler())}, forearm {tuple(round(math.degrees(a),1) for a in df.to_euler())}")
uarm.rotation_quaternion = base_u; farm.rotation_quaternion = base_f

# apply to every key of both bones
def fcurves():
    if hasattr(act, "fcurves"): return act.fcurves
    slot = arm.animation_data.action_slot
    for layer in act.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot)
            if cb: return cb.fcurves
    return []
for bone, delta in ((uarm, du), (farm, df)):
    path = f'pose.bones["{bone.name}"].rotation_quaternion'
    curves = [fc for fc in fcurves() if fc.data_path == path]
    if len(curves) != 4:
        print("no quaternion curves for", bone.name, len(curves)); continue
    curves.sort(key=lambda c: c.array_index)
    n = len(curves[0].keyframe_points)
    for i in range(n):
        q = Quaternion((curves[0].keyframe_points[i].co.y, curves[1].keyframe_points[i].co.y,
                        curves[2].keyframe_points[i].co.y, curves[3].keyframe_points[i].co.y))
        q2 = q @ delta
        for k in range(4):
            curves[k].keyframe_points[i].co.y = q2[k]
    for c in curves: c.update()
scene.frame_set(f0); bpy.context.view_layer.update()
print(f"after: hand {tuple(round(v,3) for v in world(hand))} dist {(world(hand)-target).length:.3f}")
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y', path_mode='STRIP')
print("exported", OUT)
print("CARRY-OK")
