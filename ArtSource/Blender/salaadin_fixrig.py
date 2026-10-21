"""Straighten the joints Mixamo could not find inside Salaadin's clothes.

Usage: blender -b --python salaadin_fixrig.py -- <Salaadin_Rigged.fbx> <out.fbx> <render_dir>

Mixamo's auto-rigger fits its skeleton to the mesh VOLUME, and this mesh is mostly cloth: the leg
geometry stops at z=0.33 (the top of the boots) and everything above it is coat skirt and cloak,
which reaches 0.22 BEHIND him. So the fitter has nothing but fabric where the thigh should be and
drops the knee into the skirt, 10% of body height forward of the hip-ankle line - a permanent
crouch that no front-view marker can correct. Two rounds of marker placement proved it: the second
was symmetric (the markers were placed well) and still bent 128 deg.

The same thing happens at the elbow, where the shoulder cape swallows the upper arm: Mixamo put the
joint 35% of the way from shoulder to wrist, so the upper arm came out 6.9% of his height against a
human's ~16% and SHORTER than his own forearm. That is the arm that bends up near the armpit.

Both are fixed here by construction rather than by fitting:

    knee  -> onto the straight hip->ankle line, at the fraction along it the rig already chose
    elbow -> onto the shoulder->wrist line at ELBOW_F, where a human elbow sits

Moving a bone in EDIT mode moves the REST pose. The mesh does not move with it and the vertex
groups do not change, so the skin keeps Mixamo's weights and is simply bound to a skeleton whose
joints are now where the character's actually are - which is what the bind pose should have been.
Nothing is re-weighted, so no deformation quality is traded away.
"""
import bpy, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
RIG, OUT, RENDER_DIR = args[:3]
os.makedirs(RENDER_DIR, exist_ok=True)
os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)

# Where a human elbow sits along shoulder -> wrist. Richard, whose arms Mixamo found because he
# wears plate rather than a cape, came back at 0.56-0.57; anatomy texts put it at 0.52.
ELBOW_F = 0.52
# How straight to leave the legs. Exactly 1.0 would put hip, knee and ankle on one line, and a
# perfectly straight chain has no bend direction for the IK to pick, so the knee can solve
# backwards. A hair of forward bend keeps the solver honest and still reads as a T-pose.
KNEE_STRAIGHT = 0.985

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=RIG)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
zs = [(m.matrix_world @ Vector(c)).z for m in meshes for c in m.bound_box]
H = max(zs) - min(zs)


def report(tag):
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()

    def P(n):
        b = next(x for x in arm.data.bones if x.name.endswith(n))
        return arm.matrix_world @ b.head_local

    print(f"--- {tag} ---")
    for side in ("Left", "Right"):
        sh, el, wr = P(side + "Arm"), P(side + "ForeArm"), P(side + "Hand")
        f = (el - sh).length / ((el - sh).length + (wr - el).length)
        print(f"    {side:5} upper arm {(el-sh).length/H*100:4.1f}% of height, "
              f"elbow {f*100:3.0f}% along shoulder->wrist")
    for side in ("Left", "Right"):
        hp, kn, an = P(side + "UpLeg"), P(side + "Leg"), P(side + "Foot")
        axis = an - hp
        t = (kn - hp).dot(axis) / axis.length_squared
        off = ((kn - hp) - axis * t).length / H * 100
        print(f"    {side:5} knee bend {math.degrees((hp-kn).angle(an-kn)):3.0f} deg, "
              f"{off:4.1f}% of height off the hip-ankle line")


report("as Mixamo delivered it")

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones


def B(n):
    return next(b for b in eb if b.name.endswith(n))


def move_joint(parent, child, grandchild, frac, blend=1.0):
    """Put the joint between parent and child onto the parent->grandchild line.

    Both bones are moved together - the parent's tail and the child's head are the same joint - so
    the chain stays connected and only the one joint travels.
    """
    p, c, g = B(parent), B(child), B(grandchild)
    target = p.head + (g.head - p.head) * frac
    target = c.head + (target - c.head) * blend
    c.head = target
    p.tail = target


for side in ("Left", "Right"):
    # the elbow, onto the shoulder -> wrist line at a human's fraction
    move_joint(side + "Arm", side + "ForeArm", side + "Hand", ELBOW_F)
    # the knee, onto the hip -> ankle line, keeping the height Mixamo chose (it was right: 51%)
    p, c, g = B(side + "UpLeg"), B(side + "Leg"), B(side + "Foot")
    axis = g.head - p.head
    frac = (c.head - p.head).dot(axis) / axis.length_squared
    move_joint(side + "UpLeg", side + "Leg", side + "Foot", frac, blend=KNEE_STRAIGHT)

bpy.ops.object.mode_set(mode='OBJECT')
report("after straightening")

bpy.ops.object.select_all(action='DESELECT')
for o in list(meshes) + [arm]:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, armature_nodetype='NULL', bake_anim=False,
    path_mode='COPY', embed_textures=True,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y')
print(f"FIXRIG-OK {OUT} ({os.path.getsize(OUT)//1024} KB)")
