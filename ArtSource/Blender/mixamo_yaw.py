"""Yaw a whole Mixamo clip about the world up axis (through the hips) by a constant angle, every frame.
Use it to re-centre a strike that lands off the character's forward line (e.g. an axe chop that drifts
left: yaw the clip right by the drift angle). Position is untouched, so the clip stays in place.

Usage: blender -b --python mixamo_yaw.py -- <in.fbx> <out.fbx> <degrees>   (+ = turn left, - = turn right)
"""
import bpy, sys, math
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, DEG = args[0], args[1], float(args[2])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
act = bpy.data.actions[0]
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
scene = bpy.context.scene
scene.frame_start, scene.frame_end = f0, f1
scene.render.fps = 30
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
hips = next(b for b in arm.pose.bones if b.name.endswith("Hips"))
hips.rotation_mode = 'QUATERNION'
R = Matrix.Rotation(math.radians(DEG), 4, 'Z')
inv = arm.matrix_world.inverted()
for f in range(f0, f1 + 1):
    scene.frame_set(f); bpy.context.view_layer.update()
    mw = arm.matrix_world @ hips.matrix
    pos = mw.translation.copy()
    new = Matrix.Translation(pos) @ R @ Matrix.Translation(-pos) @ mw
    hips.matrix = inv @ new
    hips.keyframe_insert("rotation_quaternion", frame=f)
    hips.keyframe_insert("location", frame=f)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y', path_mode='STRIP')
print(f"yawed {DEG:+.1f} deg over frames {f0}-{f1}; exported {OUT}")
print("YAW-OK")
