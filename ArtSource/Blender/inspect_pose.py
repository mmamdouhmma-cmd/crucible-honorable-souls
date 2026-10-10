import bpy, sys, math
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
try:
    bpy.ops.wm.fbx_import(filepath=path)
    print("IMPORTER: wm.fbx_import (native)")
except Exception:
    bpy.ops.import_scene.fbx(filepath=path)
    print("IMPORTER: import_scene.fbx (python)")

print("EXPORTERS:", "wm.fbx_export" if hasattr(bpy.ops.wm, "fbx_export") else "-",
      "export_scene.fbx" if hasattr(bpy.ops.export_scene, "fbx") else "-")
print("SCENE fps:", bpy.context.scene.render.fps, "unit scale:", bpy.context.scene.unit_settings.scale_length)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
print("ARMATURE obj: loc", tuple(round(v, 3) for v in arm.location), "rot(deg)",
      tuple(round(math.degrees(v), 1) for v in arm.rotation_euler), "scale", tuple(round(v, 3) for v in arm.scale))
print("MESH obj: parent", mesh.parent.name if mesh.parent else None, "rot(deg)",
      tuple(round(math.degrees(v), 1) for v in mesh.rotation_euler), "scale", tuple(round(v, 3) for v in mesh.scale),
      "dims(world)", tuple(round(v, 3) for v in mesh.dimensions))
mods = [(m.type, getattr(m, 'object', None) and m.object.name) for m in mesh.modifiers]
print("MESH modifiers:", mods)

# World-space bounding box of the mesh
bb = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
print("MESH world bbox min", tuple(round(v, 3) for v in mn), "max", tuple(round(v, 3) for v in mx))

keys = ["Root", "Hip", "Pelvis", "Waist", "Spine01", "Spine02", "NeckTwist01", "NeckTwist02", "Head",
        "L_Clavicle", "L_Upperarm", "L_Forearm", "L_Hand", "R_Clavicle", "R_Upperarm", "R_Forearm", "R_Hand",
        "L_Thigh", "L_Calf", "L_Foot", "L_ToeBase", "R_Thigh", "R_Calf", "R_Foot"]
M = arm.matrix_world
print("\nBONE rest pose (world): head -> tail, length, and world direction of the bone's local X / Y / Z axes")
for name in keys:
    b = arm.data.bones.get(name)
    if b is None:
        print(f"  {name}: MISSING"); continue
    h = M @ b.head_local; t = M @ b.tail_local
    R = (M @ b.matrix_local).to_3x3()
    ax = [R @ Vector(a) for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1))]
    f = lambda v: "(" + ",".join(f"{c:+.2f}" for c in v) + ")"
    print(f"  {name:12s} head{f(h)} tail{f(t)} len {b.length:.3f}  X{f(ax[0])} Y{f(ax[1])} Z{f(ax[2])}")

# Arm pose: angle of the upper arm relative to horizontal (T-pose ~0, A-pose ~ -45..-60)
for side in "LR":
    b = arm.data.bones.get(f"{side}_Upperarm")
    if b:
        d = (M @ b.tail_local) - (M @ b.head_local)
        horiz = math.hypot(d.x, d.y)
        print(f"{side} upper arm droop from horizontal: {math.degrees(math.atan2(d.z, horiz)):+.0f} deg")
