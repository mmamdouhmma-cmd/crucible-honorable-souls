"""Author Salaadin's own clips: the five the pack cannot give him, on his own rig.

Usage: blender -b --python salaadin_clips.py -- <Salaadin_Body.fbx> <pack_dir> <out_dir> <render_dir> [name ...]

Richard already holds the pack's only settled idle and its one kick, and Salaadin's move set is
slashes and kicks, so his guard stance and all three of his kicks are built here. Two tools do it:

  pose()   analytic two-bone IK: the hand is put on a target point in the fighter's own frame
           (fwd / lateral / up metres from the hips) and the wrist is aimed so the blade points
           where the move needs it. No search - the elbow comes from the triangle, with a pole
           vector keeping it behind and below the arm the way a real elbow sits.
  take()   samples a pack clip into per-frame bone poses, so a mocap take can be spliced after an
           authored one (the double swing) or carry an authored arm over it (the bash, the charge).

Everything exports as an armature-only FBX at 30 fps, which Unity imports exactly like a pack file.

HIS RIG IS 1.04 UNITS TALL where Richard's is 1.85, so every hand target below is written in
RICHARD'S units and multiplied by K, measured from the body at load time. Numbers lifted from
richard_clips.py therefore transfer unchanged.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
BODY, PACK, OUT_DIR, RENDER_DIR = args[:4]
WANTED = [a for a in args[4:] if a != "check"]
CHECK = "check" in args[4:]
os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)

FWD = Vector((0, -1, 0))          # the Mixamo clips face world -Y after the Blender import
UP = Vector((0, 0, 1))
RIGHT = FWD.cross(UP).normalized()
ARMS_R = ["RightShoulder", "RightArm", "RightForeArm", "RightHand"]
ARMS_L = ["LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand"]



K = 1.0            # his height over Richard's, so Richard's hand targets can be reused verbatim
HIP_H = 1.0        # his hips' rest height, the unit the packs' hip travel is carried in


def load():
    global K
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=BODY)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    zs = [(mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box]
    K = (max(zs) - min(zs)) / 1.85
    global HIP_H
    HIP_H = (arm.matrix_world @ next(b for b in arm.data.bones if b.name.endswith("Hips")).head_local).z
    print(f"    rig {max(zs) - min(zs):.3f} tall -> targets scaled by K={K:.3f}")
    for b in arm.pose.bones:
        b.rotation_mode = 'QUATERNION'
    bpy.context.scene.render.fps = 30
    return arm


def B(arm, suffix):
    return next(b for b in arm.pose.bones if b.name.endswith(suffix))


def wpos(arm, b):
    return (arm.matrix_world @ b.matrix).translation


def take(path):
    """Sample a pack clip: one dict of bone name -> matrix_basis per frame."""
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=path)
    src = next(o for o in set(bpy.data.objects) - before if o.type == 'ARMATURE')
    act = src.animation_data.action
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    # The packs are authored on a rig whose armature scale is 0.01, so its hip travel is carried in
    # hundredths against this rig's metres: applied raw it drops him 52 hip-heights through the
    # floor (measured - Unity read RootT.y -52.4 where the pack's own block idle reads 0.796).
    # Carrying it as a FRACTION of the source's rest hip height is exactly what Unity's RootT does,
    # and it lands on any rig.
    s_src = src.matrix_world.to_scale()[0]
    hip_src = (src.matrix_world @ next(b for b in src.data.bones if b.name.endswith("Hips")).head_local).z
    norm = s_src / max(hip_src, 1e-6)
    frames = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        bpy.context.view_layer.update()
        frame = {}
        for b in src.pose.bones:
            short = b.name.split(":")[-1]
            m = b.matrix_basis.copy()
            if short == "Hips":
                m.translation = m.translation * norm
            frame[short] = (m, b.location.copy())
        frames.append(frame)
    for o in set(bpy.data.objects) - before:
        bpy.data.objects.remove(o)
    print(f"  take {os.path.basename(path)}: {len(frames)} frames")
    return frames


def apply_frame(arm, frame, only=None, skip=None):
    for pb in arm.pose.bones:
        short = pb.name.split(":")[-1]
        if short not in frame:
            continue
        if only and not any(short.endswith(s) for s in only):
            continue
        if skip and any(short.endswith(s) for s in skip):
            continue
        m = frame[short][0].copy()
        if short == "Hips":
            m.translation = m.translation * (HIP_H / max(arm.matrix_world.to_scale()[0], 1e-6))
        pb.matrix_basis = m
    bpy.context.view_layer.update()


def set_bone_world(arm, pb, y_dir, ref):
    """Point a bone's own +Y along y_dir, keeping its head, rolled against ref."""
    y = y_dir.normalized()
    x = ref.cross(y)
    if x.length < 1e-5:
        x = Vector((0, 0, 1)).cross(y)
    if x.length < 1e-5:
        x = Vector((1, 0, 0)).cross(y)
    x.normalize()
    z = x.cross(y).normalized()
    head = (arm.matrix_world @ pb.matrix).translation
    pb.matrix = to_armature_space(arm, head, Matrix((x, y, z)).transposed())
    bpy.context.view_layer.update()


def to_armature_space(arm, head_world, rot3):
    """A pose matrix for pose_bone.matrix: armature space, unscaled rotation, head at head_world."""
    inv_rot = arm.matrix_world.to_quaternion().inverted().to_matrix()
    local_head = arm.matrix_world.inverted() @ head_world
    return Matrix.Translation(local_head) @ (inv_rot @ rot3).to_4x4()


ARM = ("Arm", "ForeArm", "Hand")
LEG = ("UpLeg", "Leg", "Foot")


def reach(arm, side, target, pole, chain=ARM):
    """Analytic two-bone IK, so the end of the chain lands on target (world space).

    The arms use it through pose(); the legs use it through kick(), because a kick keyed by
    rotating the leg bones about their own axes swings sideways on this rig - the first build
    came out as a crouch with no leg in front of him.
    """
    uarm, farm, hand = (B(arm, side + n) for n in chain)
    s = arm.matrix_world.to_scale()[0]
    S = wpos(arm, uarm)
    L1 = uarm.bone.length * s
    L2 = farm.bone.length * s
    to = target - S
    d = min(to.length, (L1 + L2) * 0.995)
    if d < 1e-4:
        return 0.0
    n = to.normalized()
    a = (L1 * L1 - L2 * L2 + d * d) / (2 * d)
    h = math.sqrt(max(L1 * L1 - a * a, 0.0))
    p = pole - (S + n * a)
    p = p - n * p.dot(n)
    if p.length < 1e-5:
        p = Vector((0, 0, -1)) - n * Vector((0, 0, -1)).dot(n)
    E = S + n * a + p.normalized() * h
    plane = (E - S).cross(target - E)
    if plane.length < 1e-5:
        plane = UP
    set_bone_world(arm, uarm, E - S, plane)
    set_bone_world(arm, farm, target - wpos(arm, farm), plane)
    return (wpos(arm, hand) - target).length


def aim(arm, side, hilt, palm):
    """Set the wrist so its local X (the hilt line, thumb side) and Z (palm) hit world directions."""
    hand = B(arm, side + "Hand")
    x = hilt.normalized()
    z = (palm - x * palm.dot(x)).normalized()
    y = z.cross(x).normalized()
    if side == "Left":
        x, y = -x, -y
    hand.matrix = to_armature_space(arm, wpos(arm, hand), Matrix((x, y, z)).transposed())
    bpy.context.view_layer.update()


def pose(arm, right=None, left=None, blade=None, shield=None, yaw=0.0):
    """Hand targets in units of THAT ARM'S OWN REACH, measured from its own shoulder.

    Richard's clips place hands in metres from the hips, which cannot be ported here by scale:
    this rig is 1.00 tall against his 1.85, but its arms are 0.197 - about half human proportion
    against his - so a target scaled by height lands past the fingertips every time (measured:
    his guard pose came out 0.021 too far, his thrust 0.174). Writing targets as a fraction of
    reach from the shoulder makes them mean the same shape on any build of any character, and a
    target longer than 0.97 reach is pulled in rather than quietly missed.

    yaw turns the whole frame of reference about the vertical, which is what lets the spin carry
    its arms with the body instead of leaving them pointing downrange.
    """
    rot = Matrix.Rotation(math.radians(yaw), 4, 'Z')
    fwd, lat = rot @ FWD, rot @ RIGHT
    scale = max(arm.matrix_world.to_scale()[0], 1e-6)
    for side, tgt, pole_sign in (("Right", right, 1.0), ("Left", left, -1.0)):
        if not tgt:
            continue
        shoulder = wpos(arm, B(arm, side + "Arm"))
        span = (B(arm, side + "Arm").bone.length + B(arm, side + "ForeArm").bone.length) * scale
        v = (fwd * tgt[0] + lat * tgt[1] + UP * tgt[2]) * span
        if v.length > 0.97 * span:
            v = v.normalized() * 0.97 * span
        pole = shoulder + (lat * 0.6 * pole_sign - fwd * 0.5) * span
        miss = reach(arm, side, shoulder + v, pole)
        if miss > 0.04 * span:
            print(f"    {side.lower()} hand miss {miss / span:.2f} of reach")
    if blade:
        aim(arm, "Right", (rot @ blade[0]).normalized(), (rot @ blade[1]).normalized())
    if shield:
        aim(arm, "Left", (rot @ shield[0]).normalized(), (rot @ shield[1]).normalized())


def key(arm, frame, bones=None):
    for pb in arm.pose.bones:
        short = pb.name.split(":")[-1]
        if bones and not any(short.endswith(s) for s in bones):
            continue
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if short == "Hips":
            pb.keyframe_insert("location", frame=frame)


def new_action(arm, name):
    arm.animation_data_clear()
    arm.animation_data_create()
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    if hasattr(act, "slots"):
        arm.animation_data.action_slot = act.slots.new(id_type='OBJECT', name=name)
    return act


def render_frames(arm, name, frames, view="front"):
    """Workbench render of the built clip at a few frames, so the pose can actually be looked at."""
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = 520, 620
    try:
        scn.render.engine = 'BLENDER_WORKBENCH'
    except TypeError as e:
        print(e)
    scn.display.shading.light = 'STUDIO'
    scn.display.shading.color_type = 'OBJECT'
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.color = ((0.85, 0.75, 0.25, 1) if "Sword" in o.name else
                       (0.8, 0.2, 0.2, 1) if "Shield" in o.name else (0.55, 0.55, 0.58, 1))
    cam = bpy.data.objects.get("clipcam") or bpy.data.objects.new("clipcam", bpy.data.cameras.new("clipcam"))
    if cam.name not in scn.collection.objects:
        scn.collection.objects.link(cam)
    scn.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.8
    d = {"front": Vector((0, -1, 0.12)), "side": Vector((-1, -0.25, 0.12)), "top": Vector((0, 0, 1))}[view]
    for f in frames:
        scn.frame_set(f)
        bpy.context.view_layer.update()
        ctr = wpos(arm, B(arm, "Hips")) + Vector((0, 0, 0.1))
        cam.location = ctr + d.normalized() * 6
        cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(RENDER_DIR, f"{name}_{view}_f{f:03d}.png")
        bpy.ops.render.render(write_still=True)
    print("  rendered", name, view, frames)


def export(arm, name, last):
    out = os.path.join(OUT_DIR, name + ".fbx")
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, last
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, object_types={'ARMATURE'},
        add_leaf_bones=False, armature_nodetype='NULL', bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='STRIP',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y')
    print(f"exported: {out} ({os.path.getsize(out)//1024} KB, {last+1} frames)")


# ---------------------------------------------------------------------------------------------
# the clips
# ---------------------------------------------------------------------------------------------
# the clips
# ---------------------------------------------------------------------------------------------

GUARD = os.path.join(PACK, "sword and shield block idle.fbx")

# His guard, reused by every clip as the pose to leave from and return to: saber out to the right
# and low, shield up and square. Fractions of arm reach from the shoulder (see pose()).
G_RIGHT = (0.35, 0.35, -0.75)
G_LEFT = (0.80, -0.25, 0.05)
G_BLADE = (FWD, -RIGHT)
# aim() takes (thumb axis, palm axis). His shield mounts with its top on the thumb and its BOSS on
# -palm (salaadin_body.py), so the palm argument points AWAY from the opponent: Richard's (UP, FWD)
# would show the enemy the back of the shield, which is exactly what the first render did.
G_SHIELD = (UP, -FWD)


def guard_pose(arm, guard, yaw=0.0, **over):
    """The stance, with any part of it overridden."""
    apply_frame(arm, guard)
    spec = dict(right=G_RIGHT, left=G_LEFT, blade=G_BLADE, shield=G_SHIELD, yaw=yaw)
    spec.update(over)
    pose(arm, **spec)


def kick(arm, foot, side="Right", toe=None):
    """Put a foot at (fwd, lateral, up) in units of that leg's reach, measured from its hip.

    Same model as the hands in pose(), and for the same reason: nothing here depends on how this
    rig's leg bones happen to be rolled. The knee is poled forward, so it never hyperextends
    backwards, and toe aims the foot along a world direction for the frames where it shows.
    """
    scale = max(arm.matrix_world.to_scale()[0], 1e-6)
    hip = wpos(arm, B(arm, side + "UpLeg"))
    span = (B(arm, side + "UpLeg").bone.length + B(arm, side + "Leg").bone.length) * scale
    v = (FWD * foot[0] + RIGHT * foot[1] + UP * foot[2]) * span
    if v.length > 0.97 * span:
        v = v.normalized() * 0.97 * span
    miss = reach(arm, side, hip + v, hip + (FWD * 1.2 + UP * 0.2) * span, chain=LEG)
    if miss > 0.04 * span:
        print(f"    {side.lower()} foot miss {miss / span:.2f} of reach")
    if toe:
        set_bone_world(arm, B(arm, side + "Foot"), toe, UP)


def kick_keys(arm, guard, f0, chamber=5, hit=11, hold=15, back=20, down=26, extend=0.86):
    """A front kick on the right leg: chamber the knee, drive the foot out, take it back.

    The pack has exactly one kick and Richard uses it, so both of Salaadin's are built here. The
    arms stay in the guard through it - he kicks to open a hole, he does not wind up with the saber.
    """
    for df, foot, toe in ((0, None, None),
                          (chamber, (0.45, 0.10, -0.52), None),            # knee up, heel tucked
                          (hit, (extend, 0.06, -0.26), FWD),               # driven out, toe leading
                          (hold, (extend, 0.06, -0.26), FWD),
                          (back, (0.42, 0.10, -0.58), None),               # pulled back in
                          (down, None, None)):                             # foot back under him
        guard_pose(arm, guard)
        if foot:
            kick(arm, foot, toe=toe)
        key(arm, f0 + df)
    return f0 + down


def clip_guard_idle(arm, name="Salaadin_GuardIdle", n=72):
    """His idle: the pack's guard stance with a breath keyed over it.

    block idle is the only fight-ready idle Richard left, and on its own it is dead still (1 cm of
    hand travel in 42 frames). One sine over n frames means frame 0 and frame n are identical, so
    it loops clean.
    """
    guard = take(GUARD)[0]
    hips = B(arm, "Hips")
    apply_frame(arm, guard)
    rest = hips.location.copy()
    s_world = max(arm.matrix_world.to_scale()[0], 1e-6)
    for i in range(n + 1):
        s = math.sin(i / n * math.tau)
        guard_pose(arm, guard,
                   right=(G_RIGHT[0] + 0.04 * s, G_RIGHT[1], G_RIGHT[2] + 0.035 * s),
                   left=(G_LEFT[0], G_LEFT[1], G_LEFT[2] + 0.05 * s))
        hips.location = rest + Vector((0, 0, 0.010 * K * s / s_world))
        key(arm, i)
    return n


def clip_veiled_rush(arm, name="Salaadin_VeiledRush"):
    """SP + Light: the shield leads a run-in, then the saber comes up underneath it.

    Richard's charge finishes on a thrust; this one finishes on a rising cut, which is the whole
    point of the move - Salaadin has no thrust.
    """
    run = take(os.path.join(PACK, "sword and shield run.fbx"))
    n_run = min(len(run), 16)
    f = 0
    for i in range(n_run):
        apply_frame(arm, run[i])
        # shield carried high and square, saber held back out of the way
        pose(arm, left=(0.85, -0.20, 0.25), shield=G_SHIELD,
             right=(0.10, 0.45, -0.80), blade=G_BLADE)
        key(arm, f)
        f += 1
    legs = ["Hips", "UpLeg", "Leg", "Foot", "ToeBase", "Spine"]
    for df, spec in ((5,  dict(right=(-0.30, 0.50, -0.70), blade=(-FWD, -RIGHT))),   # wind: blade low behind the hip
                     (6,  dict(right=(0.80, 0.15, 0.35), blade=(FWD + UP, -RIGHT))), # the cut, rising through centre
                     (5,  dict(right=(0.45, -0.40, 0.65), blade=(UP, -RIGHT))),      # finishes high on his left
                     (11, dict(right=G_RIGHT, blade=G_BLADE))):                      # back to the guard
        f += df
        apply_frame(arm, run[n_run - 1], only=legs)
        pose(arm, left=G_LEFT, shield=G_SHIELD, **spec)
        key(arm, f)
    return f


def clip_breaking_kick(arm, name="Salaadin_BreakingKick"):
    """SP + Medium: a front kick to break the guard, carried into a low cut.

    The kick sells it, the cut is the hit - the same division of labour as Richard's bash, where
    the active window sits on the blade rather than on the body.
    """
    guard = take(GUARD)[0]
    f = kick_keys(arm, guard, 0)
    for df, spec in ((6,  dict(right=(-0.20, 0.55, -0.65), blade=(-FWD, -RIGHT))), # wind the saber back low
                     (6,  dict(right=(0.85, 0.20, -0.35), blade=(FWD, -RIGHT))),   # the low cut lands
                     (5,  dict(right=(0.45, -0.45, -0.55), blade=(-RIGHT, UP))),   # follow through across the shins
                     (11, dict(right=G_RIGHT, blade=G_BLADE))):
        f += df
        guard_pose(arm, guard, **spec)
        key(arm, f)
    return f


def clip_whirling_saif(arm, name="Salaadin_WhirlingSaif"):
    """SP + Heavy: a full turn with two cuts in it.

    One hit, on the second cut - the engine gives a swing one hit, and the second is the one that
    reads. The yaw turns the arms with the body; without it they stay pointing downrange and the
    spin looks like the torso sliding round underneath them.
    """
    guard = take(GUARD)[0]
    hips = B(arm, "Hips")
    keys = (
        (0,  0,   dict(right=G_RIGHT, blade=G_BLADE)),                       # guard
        (6,  -40, dict(right=(-0.10, 0.60, -0.50), blade=(-FWD, -RIGHT))),   # coil away from the target
        (13, 20,  dict(right=(0.85, 0.15, 0.20), blade=(FWD, -RIGHT))),      # first cut, right to left
        (21, 150, dict(right=(0.30, -0.55, 0.45), blade=(-FWD, UP))),        # blade trails round the back
        (29, 340, dict(right=(0.88, 0.10, 0.15), blade=(FWD, -RIGHT))),      # second cut arrives with the turn
        (36, 360, dict(right=(0.40, 0.40, -0.40), blade=(FWD, -RIGHT))),     # catch the weight
        (46, 360, dict(right=G_RIGHT, blade=G_BLADE)),                       # guard again, square on
    )
    for f, yaw, spec in keys:
        apply_frame(arm, guard)
        pose(arm, left=G_LEFT, shield=G_SHIELD, yaw=yaw, **spec)
        hips.rotation_quaternion = hips.rotation_quaternion @ Quaternion(Vector((0, 1, 0)), math.radians(yaw))
        key(arm, f)
    return keys[-1][0]


def clip_heel_kick(arm, name="Salaadin_HeelKick"):
    """The parry riposte: the same boot, shorter and meaner, with no cut after it.

    Judged by the body box, not the blade (hitsWithWeapon is off on Salaadin_Riposte_Parry).
    """
    guard = take(GUARD)[0]
    return kick_keys(arm, guard, 0, chamber=4, hit=9, hold=12, back=16, down=22, extend=0.92)


BUILDERS = {
    "Salaadin_GuardIdle": clip_guard_idle,
    "Salaadin_VeiledRush": clip_veiled_rush,
    "Salaadin_BreakingKick": clip_breaking_kick,
    "Salaadin_WhirlingSaif": clip_whirling_saif,
    "Salaadin_HeelKick": clip_heel_kick,
}

for name, build in BUILDERS.items():
    if WANTED and name not in WANTED:
        continue
    print(f"== {name}")
    arm = load()
    new_action(arm, name)
    last = build(arm)
    export(arm, name, last)
    if CHECK:
        render_frames(arm, name, [int(last * f) for f in (0.0, 0.25, 0.45, 0.6, 1.0)])
        render_frames(arm, name, [int(last * 0.45)], view="side")
print("CLIPS-OK")
