"""Re-skin a rebody'd fighter with bone-heat AUTO WEIGHTS, keeping the rig exactly as it is.

    blender -b --python reskin_heat.py -- <Name> <Body.fbx>   (overwritten in place)

rebody.py transferred weights from Salaadin's 5k-vert donor body, and nearest-face interpolation
across a different costume leaves huge gradient zones - arms that MELT in play (the user's
report, 2026-10-26). Bone heat computes weights from this mesh's own topology instead, against
the same 79 bones, so the rig (and every retarget) is untouched. The transferred weights are
snapshotted first and only used to fill verts heat cannot solve.
"""
import bpy, sys, os
from mathutils import kdtree

args = sys.argv[sys.argv.index("--") + 1:]
NAME, FBX = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=FBX)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH'
            and any(m.type == 'ARMATURE' for m in o.modifiers))
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and o is not mesh]
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
print(f"{NAME}: body {len(mesh.data.vertices)} verts, {len(mesh.vertex_groups)} groups, weapons {[w.name for w in weapons]}")

# snapshot the transferred weights for the heat-failure fill
old = {}
gname = {g.index: g.name for g in mesh.vertex_groups}
for v in mesh.data.vertices:
    ws = [(gname[g.group], g.weight) for g in v.groups if g.weight > 1e-4]
    if ws:
        old[v.index] = ws

# strip, then heat-skin against the same rig
for g in list(mesh.vertex_groups):
    mesh.vertex_groups.remove(g)
for m in list(mesh.modifiers):
    if m.type == 'ARMATURE':
        mesh.modifiers.remove(m)
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
try:
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    print("  bone heat ran")
except RuntimeError as e:
    print("  bone heat FAILED outright:", e)

weighted, unweighted = [], []
for v in mesh.data.vertices:
    (weighted if any(g.weight > 1e-4 for g in v.groups) else unweighted).append(v.index)
print(f"  heat left {len(unweighted)} of {len(mesh.data.vertices)} verts unweighted")
if len(unweighted) > len(mesh.data.vertices) * 0.5:
    print("  HEAT-UNUSABLE - keeping the transferred weights would be better; aborting export")
    sys.exit(1)

if unweighted:
    # Fill from the NEAREST HEAT-WEIGHTED vertex, never the transfer snapshot: heat fails on
    # detached accessory shells (a tsuba, a strap), and snapshot weights bound Miyamoto's to a
    # HAND - the pieces floated beside him in idle. The nearest solved vertex is the geometry
    # the piece actually sits on.
    kd = kdtree.KDTree(len(weighted))
    for i in weighted:
        kd.insert(mesh.data.vertices[i].co, i)
    kd.balance()
    by_bone = {}
    for i in unweighted:
        _, j, _ = kd.find(mesh.data.vertices[i].co)
        src = [(mesh.vertex_groups[g.group].name, g.weight)
               for g in mesh.data.vertices[j].groups if g.weight > 1e-4]
        for n, w in src:
            vg = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)
            vg.add([i], w, 'REPLACE')
        if src:
            dom = max(src, key=lambda t: t[1])[0]
            by_bone[dom] = by_bone.get(dom, 0) + 1
    print(f"  filled {len(unweighted)} from their nearest heat-weighted neighbours; by dominant bone:")
    for n, c in sorted(by_bone.items(), key=lambda t: -t[1]):
        print(f"    {c:6d}  {n}")

bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)

# parent_set may have reset the mesh name/parenting details; restore the conventions
mesh.name = mesh.data.name = f"{NAME}_Body"

bpy.ops.object.select_all(action='DESELECT')
for o in [rig, mesh] + weapons:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", FBX, f"({os.path.getsize(FBX) // 1024} KB)")
print("RESKIN-OK")
