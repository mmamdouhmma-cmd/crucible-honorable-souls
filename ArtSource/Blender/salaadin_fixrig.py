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

# THE FOOT. Mixamo left the ankle OUTSIDE the boot: 0.079 inboard of the shoe's centre line, 0.044
# behind its heel and above its top edge, with the foot bone splayed 30 deg out where the sole's
# own heel-to-toe axis runs at 11.5, and a toe chain long enough to end 0.076 past the toe cap and
# 0.022 BELOW the floor. Every step therefore pivoted about a point in mid air outside the shoe,
# swung the boot 19 deg wider than it is built, and drove the toe through the ground - which is the
# bulk of what reads as clumsy walking. Unlike the thigh, the foot CAN be measured: the boot is real
# geometry (everything above it is coat), so these are taken from the shoe at run time rather than
# guessed. Fractions are along the sole, from the heel; heights are fractions of body height.
FOOT_ANKLE_F = 0.28    # the ankle joint sits above the arch, a bit over a quarter back from the heel
FOOT_BALL_F = 0.72     # the ball of the foot, where the toes break
BOOT_TOP = 0.12        # everything below this fraction of height is boot, not coat
ANKLE_H = 0.075        # ankle joint height (the boot's own top edge measures ~0.109)
BALL_H = 0.030         # the ball, a sole's thickness off the floor
TIP_H = 0.015          # the toe cap, just clear of the floor

# Hanging the thigh over the ankle (below) makes it vertical, which is what the copied pack
# rotations need - but it also parks the WHOLE leg column at the ankle's fore/aft, and his calves
# and thigh plates sit 2.4-4.2% of height FORWARD of that. The hip and the knee therefore move
# forward TOGETHER by one offset: the thigh stays vertical and only the shin picks up a lean, about
# 8 deg, against the 7.5 deg Richard already rests at and the 23 deg that split Salaadin.
# Measured on the leg's OWN loose shells - boots, shoes, thigh plates - never the cape or the robe,
# which hang behind the leg and would drag the answer backwards.
LEG_FWD_LO, LEG_FWD_HI = 0.20, 0.45   # band the offset is measured over, as fractions of height
LEG_FWD = 1.0                         # 0 disables this pass, 1 puts the column on the leg's centre
# The same fault higher up: there is no head geometry under the helmet for Mixamo to fit to, so the
# head bone runs 5.5% of height BEHIND the head it drives, which is what makes him read as though
# his hips are not under his head. Puts the bone up the middle of the helmet.
HEAD_FWD = 1.0

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
    for side in ("Left", "Right"):
        an, bl, tp = P(side + "Foot"), P(side + "ToeBase"), P(side + "Toe_End")
        gz = min((m.matrix_world @ Vector(c)).z for m in meshes for c in m.bound_box)
        print(f"    {side:5} foot splay {math.degrees(math.atan2(abs(bl.x-an.x), abs(bl.y-an.y))):4.1f} deg "
              f"(the sole's own axis is 11.5), ankle {(an.z-gz)/H*100:4.1f}% up, "
              f"toe tip {(tp.z-gz)/H*100:+5.1f}% up, ankle->tip {(tp-an).length/H*100:4.1f}% of height")
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
TOUCHED = [s + b for s in ("Left", "Right")
           for b in ("UpLeg", "Leg", "Arm", "ForeArm", "Foot", "ToeBase")]
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

# Mesh vertices in WORLD space, where x is lateral about the body's centre line, y is fore/aft and
# z is up - the same frame the targets below are written in.
world_verts = [m.matrix_world @ v.co for m in meshes for v in m.data.vertices]
# Which way is forward on the y axis? Read it off the existing foot: the ball is always in front of
# the ankle, whichever way the FBX came in.
_lf = B("LeftFoot")
FWD = 1.0 if ((arm.matrix_world @ _lf.tail).y - (arm.matrix_world @ _lf.head).y) >= 0 else -1.0


def _loose_parts():
    """Every separate shell of the body mesh, in world space."""
    import bmesh
    out = []
    for m in meshes:
        bm = bmesh.new(); bm.from_mesh(m.data); bm.verts.ensure_lookup_table()
        seen = set()
        for v in bm.verts:
            if v.index in seen:
                continue
            stack, comp = [v], []
            seen.add(v.index)
            while stack:
                x = stack.pop(); comp.append(x.index)
                for e in x.link_edges:
                    o = e.other_vert(x)
                    if o.index not in seen:
                        seen.add(o.index); stack.append(o)
            out.append([m.matrix_world @ bm.verts[i].co for i in comp])
        bm.free()
    return out


PARTS = _loose_parts()


def leg_verts(sx):
    """One leg's own geometry, and nothing else.

    A leg shell lives to one side of the centre line and does not reach above the hip - that is the
    boot, the shoe and the thigh plate. It leaves out the cape and the robe, which are centre-line
    shells running to the shoulders, and the hands and sleeves, which are far out on x but high up.
    That is the whole reason this pass can be trusted: the cloak is never in it.
    """
    out = []
    for ps in PARTS:
        if len(ps) < 50:
            continue
        cx = sum(p.x for p in ps) / len(ps)
        if sx * cx <= 0.02 * H or max(p.z for p in ps) > ground + 0.50 * H:
            continue
        out.extend(ps)
    return out


def band_y(verts, z, half):
    """Fore/aft centre of whatever sits in a horizontal slice, or None if the slice is too thin."""
    b = [v for v in verts if abs(v.z - z) < half]
    return (sum(v.y for v in b) / len(b)) if len(b) > 8 else None


def boot(sx):
    """The shoe on one side, with the cloak hem dropped.

    Low vertices on one side of the body are the boot AND whatever of the cloak hangs beside it;
    the cloak is a thin sheet well behind the heel, so the biggest gap along the fore/aft axis
    separates the two cleanly. Returns (heel, toe) as scalars along the forward axis, the sole's
    lateral drift over that span, and the shoe's centre line.
    """
    sel = [v for v in world_verts
           if (v.z - ground) < BOOT_TOP * H and sx * v.x > 0.02 * H]
    ps = sorted(FWD * v.y for v in sel)
    gaps = [(ps[i + 1] - ps[i], i) for i in range(len(ps) - 1)]
    gap, gi = max(gaps) if gaps else (0.0, 0)
    keep = [v for v in sel if FWD * v.y >= ps[gi + 1]] if gap > 0.04 * H else sel
    pmin = min(FWD * v.y for v in keep)
    pmax = max(FWD * v.y for v in keep)
    # the sole's own axis, from the centroid of the rear quarter to that of the front quarter
    s = sorted(keep, key=lambda v: FWD * v.y)
    k = max(1, len(s) // 4)
    heel_c = sum(s[:k], Vector()) / k
    toe_c = sum(s[-k:], Vector()) / k
    drift = (toe_c.x - heel_c.x) / max(FWD * (toe_c.y - heel_c.y), 1e-6)
    x_ctr = (min(v.x for v in keep) + max(v.x for v in keep)) / 2.0
    return pmin, pmax, drift, x_ctr


for side, sx in (("Left", 1.0), ("Right", -1.0)):
    # THE FOOT FIRST: the hip is placed over the ankle and the knee onto the hip->ankle line, so an
    # ankle in the wrong place propagates into the whole leg. Fixing it here means both inherit a
    # foot that is actually inside the shoe.
    pmin, pmax, drift, x_ctr = boot(sx)
    flen = pmax - pmin

    def sole(f, height, _p0=pmin + FOOT_ANKLE_F * (pmax - pmin)):
        """A point on the sole's own axis, f along the shoe from the heel, at a given height."""
        p = pmin + f * flen
        return Vector((x_ctr + (p - _p0) * drift, FWD * p, ground + height * H))

    ankle, ball, tip = sole(FOOT_ANKLE_F, ANKLE_H), sole(FOOT_BALL_F, BALL_H), sole(1.0, TIP_H)
    shin, foot, toeb, toee = B(side + "Leg"), B(side + "Foot"), B(side + "ToeBase"), B(side + "Toe_End")
    shin.tail = foot.head = to_local @ ankle
    foot.tail = toeb.head = to_local @ ball
    toeb.tail = toee.head = to_local @ tip
    # Toe_End is a leaf the exporter needs but nothing deforms: keep it a short stub along the sole
    # instead of the long spur that used to run out through the toe cap and into the floor.
    toee.tail = to_local @ (tip + (tip - ball).normalized() * (0.25 * flen))

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

    # ...and now slide hip and knee FORWARD together, onto the leg the character actually has.
    # The two steps above leave the column dead vertical at the ankle's fore/aft, which is right
    # for the thigh's REST DIRECTION and wrong for where the leg is: his boots and thigh plates
    # run a couple of points of height in front of it, so the shin bone sat behind his calf.
    # Moving both ends by the same offset keeps the thigh vertical - the property the copied pack
    # rotations depend on - and spends the whole correction on the shin's lean.
    if LEG_FWD > 0.0:
        lv = leg_verts(sx)
        ank_y = (arm.matrix_world @ B(side + "Foot").head).y
        offs, zz = [], ground + LEG_FWD_LO * H
        while zz <= ground + LEG_FWD_HI * H:
            fy = band_y(lv, zz, 0.018 * H)
            if fy is not None:
                offs.append(fy - ank_y)
            zz += 0.02 * H
        if offs:
            offs.sort()
            off = offs[len(offs) // 2] * LEG_FWD
            hipb, shinb = B(side + "UpLeg"), B(side + "Leg")
            hw = arm.matrix_world @ hipb.head
            kw = arm.matrix_world @ shinb.head
            hw.y += off
            kw.y += off
            hipb.head = to_local @ hw
            shinb.head = hipb.tail = to_local @ kw
            print(f"    {side}: leg column {abs(off) / H * 100:.1f}% of height forward onto the leg "
                  f"(from {len(offs)} slices)")

# THE HEAD, same fault as the legs. Under the helmet there is no head for the auto-rigger to fit
# to, so it left the bone behind the mass it drives - the head then swings about a pivot in the
# nape, and the whole stack reads as though his hips are not under his head. Take the helmet and
# face (the centre shells up top; the sleeves and hands are far out on x and are skipped) and run
# the bone straight up the middle of them. Only the head moves; the neck leans to meet it rather
# than kinking, and the spine below is left alone.
if HEAD_FWD > 0.0:
    head_v = [v for v in world_verts if v.z > ground + 0.80 * H and abs(v.x) < 0.10 * H]
    if head_v:
        hy = sum(v.y for v in head_v) / len(head_v)
        hb, nb = B("Head"), B("Neck")
        hw_h = arm.matrix_world @ hb.head
        dy = (hy - hw_h.y) * HEAD_FWD
        shift = Vector((0.0, dy, 0.0))
        hb.use_connect = False
        hb.head = to_local @ (hw_h + shift)
        hb.tail = to_local @ ((arm.matrix_world @ hb.tail) + shift)
        top = next((b for b in eb if b.name.endswith("HeadTop_End")), None)
        if top is not None:
            top.use_connect = False
            top.head = hb.tail
            top.tail = to_local @ ((arm.matrix_world @ top.tail) + shift)
        nb.tail = hb.head
        print(f"    head bone {abs(dy) / H * 100:.1f}% of height forward, up the middle of the helmet")

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
