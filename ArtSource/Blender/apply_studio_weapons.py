"""Carry hand-adjusted weapon placements from a <Name>_Studio.blend back into <Name>_Body.fbx.

    blender -b --python apply_studio_weapons.py -- <Name> <Studio.blend> <Body.fbx>

The studio file is where the user re-seats a weapon by hand. A bone-parented object's
placement lives in matrix_parent_inverse @ matrix_basis - pose-independent, so it does not
matter what pose the rig was left in. Both files carry the same rig, so those two matrices
copy across verbatim. The body FBX is overwritten in place (GUID preserved).
"""
import bpy, sys, os
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
NAME, STUDIO, FBX = args[0], args[1], args[2]

bpy.ops.wm.open_mainfile(filepath=STUDIO)
grabbed = {}
for o in bpy.data.objects:
    if o.type == 'MESH' and o.parent_type == 'BONE' and o.parent_bone:
        grabbed[o.name] = (o.parent_bone, o.matrix_parent_inverse.copy(), o.matrix_basis.copy())
        s = o.matrix_basis.to_scale()
        print(f"studio {o.name}: bone {o.parent_bone}, scale ({s.x:.3f},{s.y:.3f},{s.z:.3f})")
assert grabbed, "studio holds no bone-parented weapons"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=FBX)
applied = 0
for o in bpy.data.objects:
    if o.name in grabbed:
        bone, mpi, basis = grabbed[o.name]
        assert o.parent_bone == bone, f"{o.name}: bone mismatch {o.parent_bone} vs {bone}"
        before = o.matrix_world.to_translation().copy()
        o.matrix_parent_inverse = mpi
        o.matrix_basis = basis
        bpy.context.view_layer.update()
        after = o.matrix_world.to_translation()
        print(f"applied {o.name}: moved {(after - before).length:.4f} in rig space")
        applied += 1
assert applied == len(grabbed), f"applied {applied} of {len(grabbed)}"

rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH'):
        o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", FBX, f"({os.path.getsize(FBX) // 1024} KB)")
print("APPLY-OK")
