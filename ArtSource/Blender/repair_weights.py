"""Repair mis-bound vertices in a body FBX: any vert whose DOMINANT bone's segment is far from
the vert itself is bound to a bone it does not sit on (Miyamoto's tsuba and hilt pieces were
snapshot-filled onto a HAND and floated beside him in idle). Each one takes the full weights of
its nearest well-bound neighbour. The rig and every well-bound vert are untouched.

    blender -b --python repair_weights.py -- <Name> <Body.fbx> [threshold=0.22]
"""
import bpy, sys, os
from mathutils import Vector, kdtree

args = sys.argv[sys.argv.index("--") + 1:]
NAME, FBX = args[0], args[1]
THRESH = float(args[2]) if len(args) > 2 else 0.22

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=FBX)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH'
            and any(m.type == 'ARMATURE' for m in o.modifiers))
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and o is not mesh]
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()

seg = {}
for b in rig.data.bones:
    seg[b.name] = (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local)


def dist_to_bone(p, name):
    hs = seg.get(name)
    if hs is None:
        return 1e9
    a, b = hs
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


gname = {g.index: g.name for g in mesh.vertex_groups}
flagged, clean = [], []
by_bone = {}
for v in mesh.data.vertices:
    ws = [(gname[g.group], g.weight) for g in v.groups if g.weight > 1e-4]
    if not ws:
        flagged.append(v.index)
        continue
    dom = max(ws, key=lambda t: t[1])[0]
    p = mesh.matrix_world @ v.co
    if dist_to_bone(p, dom) > THRESH:
        flagged.append(v.index)
        by_bone[dom] = by_bone.get(dom, 0) + 1
    else:
        clean.append(v.index)
print(f"{NAME}: {len(flagged)} of {len(mesh.data.vertices)} verts bound further than {THRESH} from their dominant bone:")
for n, c in sorted(by_bone.items(), key=lambda t: -t[1]):
    print(f"    {c:6d}  {n}")

if flagged:
    kd = kdtree.KDTree(len(clean))
    for i in clean:
        kd.insert(mesh.data.vertices[i].co, i)
    kd.balance()
    for i in flagged:
        _, j, _ = kd.find(mesh.data.vertices[i].co)
        src = [(gname[g.group], g.weight) for g in mesh.data.vertices[j].groups if g.weight > 1e-4]
        for g in mesh.vertex_groups:          # clear the bad binding first
            try:
                g.remove([i])
            except RuntimeError:
                pass
        for n, w in src:
            mesh.vertex_groups[n].add([i], w, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    print(f"  re-bound {len(flagged)} verts from their nearest well-bound neighbours")

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
else:
    print("nothing to repair; FBX untouched")
print("REPAIR-OK")
