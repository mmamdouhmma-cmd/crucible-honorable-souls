"""Pull a Tripo arm's joints back inside the arm mesh.

Tripo lays the shoulder->elbow->wrist chain out as a straight line, which on a bent or
asymmetric arm leaves joints outside the sleeve: Miyamoto's left elbow sat 2.67% of body
height clear of the surface, so everything weighted to his forearm was driven from a pivot
in open air, and his left hand read as holding the sword from outside his own arm.

Each joint is re-centred on the arm's own flesh: take the vertices the segment below it
drives, keep the slice of them nearest the joint, and move the joint to that slice's
centroid without changing how far down the arm it sits.  The move happens in EDIT mode,
which moves the REST pose while the mesh and the vertex groups stay where they are, so
Tripo's weights survive untouched.  Bone roll is restored afterwards from each bone's
saved z axis, because a roll angle names a different axis once the head or tail has moved.

  blender -b --python fix_tripo_arms.py -- <in.fbx> <out.fbx> [apply]
"""
import bpy, sys, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv  = sys.argv[sys.argv.index("--")+1:]
SRC, DST = argv[0], argv[1]
APPLY = "apply" in argv[2:]
BAND     = 0.30     # fraction of a segment, nearest the joint, used as its cross-section
MIN_MOVE = 0.25     # %H below which a joint counts as already centred

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm  = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
M, mw = arm.matrix_world, mesh.matrix_world
zs = [(M @ b.head_local).z for b in arm.data.bones] + [(M @ b.tail_local).z for b in arm.data.bones]
H  = max(zs) - min(zs)

dg, mwi = bpy.context.evaluated_depsgraph_get(), mw.inverted()
bvh = BVHTree.FromObject(mesh, dg)
DIRS = [Vector(d) for d in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))]

def inside(p_world):
    p, votes = mwi @ p_world, 0
    for d in DIRS:
        n, o = 0, p + d * 1e-5
        while True:
            hit = bvh.ray_cast(o, d)
            if hit[0] is None: break
            n += 1; o = hit[0] + d * 1e-5
            if n > 60: break
        votes += (n % 2)
    return votes

def nearest(p_world):
    return bvh.find_nearest(mwi @ p_world)[3]

gidx = {g.name: g.index for g in mesh.vertex_groups}
def group_verts(name, thresh=0.2):
    i = gidx.get(name)
    if i is None: return []
    return [mw @ v.co for v in mesh.data.vertices
            for g in v.groups if g.group == i and g.weight >= thresh]

def W(n, tail=False):
    b = arm.data.bones[n]
    return M @ (b.tail_local if tail else b.head_local)

# (label, bone whose own flesh defines the cross-section, bones sharing this point)
def stations(S):
    # The upper arm is deliberately left alone.  Its vertex groups carry Bjorn's fur mantle
    # and Miyamoto's shoulder plates, so the flesh centroid there is pulled out into the
    # costume rather than the arm - the same trap that made Salaadin's coat read as a
    # 14%-off-limb thigh.  Those joints already test inside; the forearm is a plain sleeve
    # and is where every genuinely-outside bone sits.
    return [
      (f"{S} elbow",     f"{S}_ForearmTwist01",
       [(f"{S}_UpperarmTwist02","tail"), (f"{S}_Forearm","head"), (f"{S}_ForearmTwist01","head")]),
      (f"{S} mid-fore",  f"{S}_ForearmTwist02",
       [(f"{S}_Forearm","tail"), (f"{S}_ForearmTwist01","tail"), (f"{S}_ForearmTwist02","head")]),
    ]

print("FIXARM", SRC)
moves = []
for S in ("L", "R"):
    if f"{S}_Forearm" not in arm.data.bones: continue
    for label, ref, members in stations(S):
        if ref not in arm.data.bones: continue
        head, tail = W(ref), W(ref, True)
        seg = tail - head
        L = seg.length
        if L < 1e-6: continue
        ax = seg.normalized()
        vs = group_verts(ref)
        band = [v for v in vs if 0.0 <= (v - head).dot(ax) <= BAND * L]
        if not band:
            print(f"  {label:<14} no cross-section verts, skipped"); continue
        centre = sum(band, Vector()) / len(band)
        target = centre - ax * (centre - head).dot(ax)     # keep its station along the arm
        v0, v1 = inside(head), inside(target)
        d = (target - head).length / H * 100
        print(f"  {label:<14} inside {v0}/6 surf {nearest(head)/H*100:5.2f}%H"
              f"  ->  inside {v1}/6 surf {nearest(target)/H*100:5.2f}%H   move {d:5.2f}%H")
        if v1 < 4:        print(f"                 target not inside - left alone"); continue
        if d < MIN_MOVE:  print(f"                 already centred - left alone");   continue
        moves.append((label, target, members))

if not moves:
    print("  nothing to move"); sys.exit(0)
print("  moving:", ", ".join(m[0] for m in moves))
if not APPLY:
    print("  (dry run - pass 'apply' to write the FBX)"); sys.exit(0)

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
saved = {b.name: (b.matrix.to_3x3() @ Vector((0,0,1))).normalized() for b in eb}
Mi = M.inverted()
for _, target, members in moves:
    t_local = Mi @ target
    for name, which in members:
        if name in eb: setattr(eb[name], which, t_local)

def align_roll(b, zax):
    best, bestd = b.roll, -2.0
    for i in range(721):
        b.roll = math.radians(i * 0.5)
        d = (b.matrix.to_3x3() @ Vector((0,0,1))).normalized().dot(zax)
        if d > bestd: bestd, best = d, b.roll
    b.roll = best
for b in eb:
    if b.name in saved: align_roll(b, saved[b.name])
bpy.ops.object.mode_set(mode='OBJECT')

bpy.ops.object.select_all(action='SELECT')
# same options the rest of this project's rig surgery exports with - orientation and unit
# scale have to match or Unity reimports the body lying down at a hundredth of its size
bpy.ops.export_scene.fbx(
    filepath=DST, use_selection=True, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, armature_nodetype='NULL', bake_anim=False,
    path_mode='COPY', embed_textures=True,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y')
import os
print(f"FIXARM-OK {DST} ({os.path.getsize(DST)//1024} KB)")
