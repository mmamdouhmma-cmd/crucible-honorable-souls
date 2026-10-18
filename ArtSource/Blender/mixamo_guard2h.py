"""Re-pose a Mixamo clip's BOTH arms into a two-handed axe guard, keeping the clip's motion.

    blender -b --python mixamo_guard2h.py -- <in.fbx> <out.fbx> \
        [rfwd rup rside] [lfwd lup lside]

mixamo_carry2h.py only moved the right arm, which left both hands at belly height ~0.5 units
apart. A real two-handed axe guard needs them far apart - right hand high (the haft runs up past
it to put the head above the right shoulder), left hand low at the hip (holding near the butt) -
and the hand SEPARATION is what decides whether the weapon can lie on that diagonal at all.

Same method as the carry script: grid-search a constant rotation offset for the upper arm (plus a
forearm bend) so the hand lands on a target expressed relative to the hips, then add that offset
to every key of the clip. Done independently for each arm. Mixamo clips face world -Y after the
Blender import; targets are in metres on the Mixamo skeleton (~1.7 m tall).

Defaults are Bjorn's guard: right hand up by the chest/shoulder on the right, left hand down and
across at the left hip, giving ~0.55 m between the hands.
"""
import bpy, sys, math
from mathutils import Vector, Quaternion, Euler

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
def f(i, d): return float(args[i]) if len(args) > i else d
R_FWD, R_UP, R_SIDE = f(2, 0.26), f(3, 0.32), f(4, 0.16)
L_FWD, L_UP, L_SIDE = f(5, 0.24), f(6, -0.14), f(7, -0.14)

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
hips = B("Hips")

def fcurves():
    if hasattr(act, "fcurves"):
        return act.fcurves
    slot = arm.animation_data.action_slot
    for layer in act.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot)
            if cb:
                return cb.fcurves
    return []

def world(b):
    return (arm.matrix_world @ b.matrix).translation

scene.frame_set(f0)
bpy.context.view_layer.update()
hm = arm.matrix_world @ hips.matrix
fwd = Vector((0, -1, 0))           # Mixamo clips face -Y after import
up = Vector((0, 0, 1))
right = fwd.cross(up).normalized()

def solve_arm(side, tfwd, tup, tside):
    uarm, farm, hand = B(f"{side}Arm"), B(f"{side}ForeArm"), B(f"{side}Hand")
    for b in (uarm, farm):
        b.rotation_mode = 'QUATERNION'
    target = hm.translation + fwd * tfwd + up * tup + right * tside
    base_u, base_f = uarm.rotation_quaternion.copy(), farm.rotation_quaternion.copy()
    before = (world(hand) - target).length
    best = (1e9, None, None)
    for ax in range(-80, 81, 8):
        for ay in range(-50, 51, 10):
            for az in range(-80, 81, 8):
                for fb in (0, 15, 30, 45, 60):
                    du = Euler((math.radians(ax), math.radians(ay), math.radians(az)),
                               'XYZ').to_quaternion()
                    df = Euler((math.radians(fb), 0, 0), 'XYZ').to_quaternion()
                    uarm.rotation_quaternion = base_u @ du
                    farm.rotation_quaternion = base_f @ df
                    bpy.context.view_layer.update()
                    d = (world(hand) - target).length
                    if d < best[0]:
                        best = (d, du.copy(), df.copy())
    d, du, df = best
    uarm.rotation_quaternion, farm.rotation_quaternion = base_u, base_f
    print(f"{side}: hand was {before:.3f} from target, solved to {d:.3f}; "
          f"upper {tuple(round(math.degrees(a), 1) for a in du.to_euler())}, "
          f"fore {tuple(round(math.degrees(a), 1) for a in df.to_euler())}")

    for bone, delta in ((uarm, du), (farm, df)):
        path = f'pose.bones["{bone.name}"].rotation_quaternion'
        curves = sorted([fc for fc in fcurves() if fc.data_path == path],
                        key=lambda c: c.array_index)
        if len(curves) != 4:
            print(f"  no quaternion curves for {bone.name} ({len(curves)})")
            continue
        for i in range(len(curves[0].keyframe_points)):
            q = Quaternion((curves[0].keyframe_points[i].co.y, curves[1].keyframe_points[i].co.y,
                            curves[2].keyframe_points[i].co.y, curves[3].keyframe_points[i].co.y))
            q2 = q @ delta
            for k in range(4):
                curves[k].keyframe_points[i].co.y = q2[k]
        for c in curves:
            c.update()
    return target

rt = solve_arm("Right", R_FWD, R_UP, R_SIDE)
lt = solve_arm("Left", L_FWD, L_UP, L_SIDE)

scene.frame_set(f0)
bpy.context.view_layer.update()
rh, lh = world(B("RightHand")), world(B("LeftHand"))
print(f"after: right hand {(rh - rt).length:.3f} from its target, left {(lh - lt).length:.3f}; "
      f"hands {(rh - lh).length:.3f} m apart (was the thing that blocked the diagonal)")

bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
    path_mode='STRIP')
print("exported", OUT)
print("GUARD-OK")
