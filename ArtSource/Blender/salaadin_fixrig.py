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
# Where the hip joints belong, as fractions of mesh height: how high off the ground, and how far
# apart. Mixamo put his 20.5% of his height apart - twice his ankle separation - and 56% up, out in
# the coat and level with his lower ribs, because there is no pelvis geometry for the fitter to
# find either. A bind whose thighs converge that hard makes every retargeted clip that wants the
# feet apart swing them into a SPLIT. A human's hip joints sit a touch above half height and about
# a ninth of it apart.
HIP_HEIGHT = 0.52
HIP_SEPARATION = 0.11
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
    lu, ru, lf, rf = P("LeftUpLeg"), P("RightUpLeg"), P("LeftFoot"), P("RightFoot")
    gz = min((m.matrix_world @ Vector(c)).z for m in meshes for c in m.bound_box)
    print(f"    hips  {abs(lu.x-ru.x)/H*100:4.1f}% of height apart (ankles {abs(lf.x-rf.x)/H*100:4.1f}%), "
          f"{(lu.z-gz)/H*100:4.1f}% up, leg {((lu-P('LeftLeg')).length+(P('LeftLeg')-lf).length)/H*100:4.1f}% long")


report("as Mixamo delivered it")

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones

# KEEP EVERY MOVED BONE'S TWIST FRAME. A bone's roll is stored as an angle about its own length, so
# the moment its head or tail moves, the same roll number describes a DIFFERENT set of x/z axes.
# That matters here because the clips copy the pack's LOCAL bone rotations onto this rig
# (apply_frame in salaadin_clips.py), and a local rotation applied about a twisted axis comes out
# somewhere else entirely - narrowing the hips swung the thighs' frames and the guard stance opened
# into a 97 deg SPLIT. Recording each bone's z axis now and realigning to it after the edits keeps
# the frames where the pack's rotations expect them.
TOUCHED = [s + b for s in ("Left", "Right") for b in ("UpLeg", "Leg", "Arm", "ForeArm")]
saved_axis = {n: (next(b for b in eb if b.name.endswith(n)).matrix.to_3x3() @ Vector((0.0, 0.0, 1.0)))
              for n in TOUCHED}


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


ground = min((m.matrix_world @ Vector(c)).z for m in meshes for c in m.bound_box)
# Edit bones are in ARMATURE-LOCAL space, and an imported FBX armature is rotated, so its local up
# is not world z. Every target below is worked out in world space and converted here; writing world
# numbers straight into a bone head silently lands them on the wrong axis.
to_local = arm.matrix_world.inverted()

for side, sx in (("Left", 1.0), ("Right", -1.0)):
    # the elbow, onto the shoulder -> wrist line at a human's fraction
    move_joint(side + "Arm", side + "ForeArm", side + "Hand", ELBOW_F)

    # The hip joint: down to human height, in to human separation, and FORWARD/BACK OVER THE ANKLE.
    # That last axis is the one that actually causes the splits. The clips take their leg keys
    # straight from the pack's bones (apply_frame in salaadin_clips.py copies matrix_basis, which
    # is rest-relative), and that is only valid when the two rigs' legs REST the same way. The pack
    # thigh hangs at (0.00, -0.01, -1.00) and Richard's at (+0.08, +0.13, -0.99) - both vertical -
    # while Mixamo left Salaadin's at (-0.01, +0.39, -0.92), tilted 23 deg BACKWARD, because it put
    # his hip socket well forward inside the coat. Every copied leg rotation then landed 23 deg out
    # and compounded down the knee. Hanging the thigh over the ankle puts it vertical like the
    # other two. (The arms never showed this because clips key them with IK, not copied rotations.)
    # The pelvis bone itself is left alone - the spine hangs off it and the split is a LEG fault.
    hip = B(side + "UpLeg")
    hip.use_connect = False
    ankle = arm.matrix_world @ B(side + "Foot").head
    hip.head = to_local @ Vector((sx * HIP_SEPARATION / 2.0 * H,   # in to human separation
                                  ankle.y,                         # back over the ankle, see below
                                  ground + HIP_HEIGHT * H))        # down to human height

    # the knee, onto the (new) straight hip -> ankle line, at the height Mixamo chose - that part
    # it got right, 51% of the way down, because the boot tops gave it something real to fit to.
    p, c, g = B(side + "UpLeg"), B(side + "Leg"), B(side + "Foot")
    axis = g.head - p.head
    frac = (c.head - p.head).dot(axis) / axis.length_squared
    move_joint(side + "UpLeg", side + "Leg", side + "Foot", frac, blend=KNEE_STRAIGHT)

for n, axis in saved_axis.items():
    next(b for b in eb if b.name.endswith(n)).align_roll(axis)

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
