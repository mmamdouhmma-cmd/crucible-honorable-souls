"""Correct the bones that CAN be verified on Salaadin's hand-edited rig, in place.

Runs on Salaadin_Studio.blend, which is the only copy of that rig, and writes the body FBX.

Why only some bones: his real per-side leg geometry STOPS at 28% of height - above it there is
only coat skirt and cloak, which is why Mixamo put his knee in the skirt and why "is the bone
inside the mesh" returns a meaningless 2/6 on his thighs. Hip, thigh and knee are therefore left
to the user's own construction (hips 51.2% up and 10.6% apart, knee on the top of the real boot
shell - all sane) and only the bones with genuine flesh under them are corrected here.

    blender -b --python salaadin_rig_v2.py -- <Studio.blend> <out_Body.fbx>
"""
import bpy, sys, os, bmesh
from mathutils import Vector

blend, out_fbx = sys.argv[sys.argv.index("--")+1:][:3][:2]
bpy.ops.wm.open_mainfile(filepath=blend)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
arm.data.pose_position = 'REST'
bpy.context.view_layer.objects.active = arm
if bpy.context.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()

body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
world = [body.matrix_world @ v.co for v in body.data.vertices]
GND = min(p.z for p in world)
H = max(p.z for p in world) - GND
print(f"body height {H:.3f}")


def shells():
    bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen: continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            c = stack.pop(); comp.append(c)
            for e in c.link_edges:
                w = e.other_vert(c)
                if w.index not in seen:
                    seen.add(w.index); stack.append(w)
        out.append([body.matrix_world @ c.co for c in comp])
    bm.free()
    return [s for s in out if len(s) >= 20]


PARTS = shells()
print(f"loose shells: {len(PARTS)}")


def shoe(sign):
    """The shoe shell on one side: the only geometry that says where the foot really points."""
    best = None
    for s in PARTS:
        zs = [p.z for p in s]
        if (max(zs) - GND) / H > 0.12:       # the shoe proper, not the boot shaft
            continue
        cx = sum(p.x for p in s) / len(s)
        if cx * sign <= 0:
            continue
        if best is None or len(s) > len(best):
            best = s
    return best


def helmet():
    """The skull/helmet shell - the tallest centred one."""
    best = None
    for s in PARTS:
        zs = [p.z for p in s]
        if (min(zs) - GND) / H < 0.75:
            continue
        cx = abs(sum(p.x for p in s) / len(s)) / H
        if cx > 0.04:
            continue
        if best is None or max(p.z for p in s) > max(p.z for p in best):
            best = s
    return best


bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
to_local = arm.matrix_world.inverted()
rolls = {b.name: b.matrix.col[2].xyz.copy() for b in eb}


def find(short):
    return next((b for b in eb if b.name.split(":")[-1] == short), None)


report = []

# ---- feet: aim each foot bone at its own shoe's toe, inside the leather ----------------------
for sign, side in ((1, "Left"), (-1, "Right")):
    fb = find(side + "Foot")
    sh = shoe(sign)
    if fb is None or sh is None:
        print(f"  {side}Foot: no bone/shoe, skipped"); continue
    cx = sum(p.x for p in sh) / len(sh)
    toe_y = min(p.y for p in sh)                 # -Y is forward on this rig
    heel_y = max(p.y for p in sh)
    zmin, zmax = min(p.z for p in sh), max(p.z for p in sh)
    mid_z = zmin + (zmax - zmin) * 0.45
    old_t = arm.matrix_world @ fb.tail
    # ankle keeps the user's height (3.1% - anatomically right); only its lateral drift is fixed
    head_w = arm.matrix_world @ fb.head
    head_w.x = cx
    # tail: 85% of the way to the toe tip, so the joint stays INSIDE the shoe, never under the floor
    tail_w = Vector((cx, toe_y * 0.85, mid_z))
    fb.head = to_local @ head_w
    fb.tail = to_local @ tail_w
    report.append(f"  {side}Foot tail ({old_t.x/H*100:5.1f},{old_t.y/H*100:6.1f},{(old_t.z-GND)/H*100:5.1f})"
                  f" -> ({tail_w.x/H*100:5.1f},{tail_w.y/H*100:6.1f},{(tail_w.z-GND)/H*100:5.1f})%"
                  f"   [shoe toe {toe_y/H*100:.1f}, heel {heel_y/H*100:.1f}]")

# ---- head: up the middle of the helmet, and stopping inside it -------------------------------
hb, ht, nk = find("Head"), find("HeadTop_End"), find("Neck")
hm = helmet()
if hb and hm:
    cx = sum(p.x for p in hm) / len(hm)
    cy = sum(p.y for p in hm) / len(hm)
    top = max(p.z for p in hm)
    old_t = arm.matrix_world @ hb.tail
    head_w = arm.matrix_world @ hb.head
    head_w.x, head_w.y = cx, cy
    tail_w = Vector((cx, cy, GND + (top - GND) * 0.93))   # inside the crown, not above it
    hb.head = to_local @ head_w
    hb.tail = to_local @ tail_w
    if nk: nk.tail = hb.head
    if ht:
        ht.head = hb.tail
        ht.tail = to_local @ Vector((cx, cy, top))
    report.append(f"  Head tail ({old_t.x/H*100:5.1f},{old_t.y/H*100:6.1f},{(old_t.z-GND)/H*100:5.1f})"
                  f" -> ({tail_w.x/H*100:5.1f},{tail_w.y/H*100:6.1f},{(tail_w.z-GND)/H*100:5.1f})%"
                  f"   [helmet crown {(top-GND)/H*100:.1f}%]")

# a roll angle describes a different axis once the head or tail has moved
for b in eb:
    if b.name in rolls:
        b.align_roll(rolls[b.name])

bpy.ops.object.mode_set(mode='OBJECT')
print("\n--- corrections ---")
for r in report: print(r)

bpy.ops.wm.save_mainfile(filepath=blend)
print(f"\nsaved rig back into {os.path.basename(blend)}")

for o in bpy.data.objects:
    o.select_set(False)
sel = []
for o in bpy.data.objects:
    if o.type in {'ARMATURE', 'MESH'}:
        o.hide_set(False); o.hide_select = False; o.hide_viewport = False
        o.select_set(True); sel.append(o.name)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print(f"exported {out_fbx} ({os.path.getsize(out_fbx)//1024} KB), bones {len(arm.data.bones)}")
print("RIGV2-OK")
