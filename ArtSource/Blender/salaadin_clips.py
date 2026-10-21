"""Author Salaadin's own clips: the five the pack cannot give him, on his own rig.

Usage: blender -b --python salaadin_clips.py -- <Salaadin_Body.fbx> <pack_dir> <out_dir> <render_dir> [name ...]

Richard already holds the pack's only settled idle, and Salaadin strikes with the falchion and
nothing else - no thrusts, no shield bashes, no kicks - so his guard stance and the three cuts the
pack cannot give him are built here. Two tools do it:

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

# The scene name becomes the FBX take name, and that is how CrucibleMixamoSetup recognises a clip
# whose frame 0 is a rest-pose lead-in rather than animation (see rest_lead_in below).
REST_LEAD_SCENE = "restlead"

# A second, empty object exported beside the armature, so the armature is not hoisted to the
# file root and the clip's avatar keeps the body's scale chain (see export()). It must not share a
# name with anything load() brings in, or Blender renames it and the FBX carries a stray '.001'.
BODY_NODE = "Salaadin_ClipRoot"

# degrees the falchion's spine sits off the hand's thumb axis, from its mount (see pose())
BLADE_LEAN = -20.0



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
LEG = ("UpLeg", "Leg", "Foot")        # for a leg move, should one ever come back


def reach(arm, side, target, pole, chain=ARM):
    """Analytic two-bone IK, so the end of the chain lands on target (world space).

    The arms use it through pose(). It takes a chain because a limb keyed by rotating its bones
    about their own axes swings sideways on this rig - bone roll differs per rig, and the first
    build of a leg move came out as a crouch with no leg in front of him.
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
        # The elbow hangs BELOW the shoulder line and a little behind it. Richard's pole is taken
        # from the hips, which are most of an arm's length under his shoulders; written from the
        # shoulder it has to carry that drop explicitly, or the elbow is poled out level and both
        # arms read as wings.
        pole = shoulder + (lat * 0.45 * pole_sign - fwd * 0.40 - UP * 0.85) * span
        miss = reach(arm, side, shoulder + v, pole)
        if miss > 0.04 * span:
            print(f"    {side.lower()} hand miss {miss / span:.2f} of reach")
    if blade:
        hilt, palm = (rot @ blade[0]).normalized(), (rot @ blade[1]).normalized()
        # salaadin_body.py mounts the falchion with a -20 deg tweak, so its spine sits BLADE_LEAN
        # out of the hand's thumb axis and its edge the same amount out of square. Turning the
        # wrist back by that much about the palm puts the blade itself where the move asks for it,
        # with the edge perpendicular - without it the guard carries the edge 20 deg behind
        # straight down and every cut slices a little with the spine (measured).
        hilt = (Matrix.Rotation(math.radians(BLADE_LEAN), 4, palm) @ hilt).normalized()
        aim(arm, "Right", hilt, palm)
    if shield:
        # no lean to undo here: the shield's boss sits exactly on -palm and its top on the thumb
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


def shift_keys(arm, by=1):
    """Move every keyframe in the current action along by `by` frames."""
    ad = arm.animation_data
    act = ad.action
    try:
        curves = act.layers[0].strips[0].channelbag(ad.action_slot).fcurves
    except (AttributeError, IndexError):
        curves = act.fcurves
    for c in curves:
        for k in c.keyframe_points:
            k.co.x += by
            k.handle_left.x += by
            k.handle_right.x += by
        c.update()


def rest_lead_in(arm):
    """Key the rig's REST pose at frame 0 and push the clip itself to frame 1 onward.

    Blender writes the pose at the FIRST exported frame as the file's node transforms and bakes
    the animation relative to them. An armature-only FBX carries no skin, so there is no bind
    pose to say otherwise, and Unity builds the clip's avatar from exactly those transforms: a
    clip that opens in the guard makes THE GUARD the T-pose, and every retargeted frame then
    comes out as the body's own T-pose plus a delta. Measured on Salaadin's guard idle, whose
    arms barely move: the whole clip played back as the bind T-pose - elbows locked straight,
    arms out sideways, the shield lying flat. Richard's clips carry the same offset; it only
    hides there because his authored moves travel far enough to still read.

    So frame 0 holds the real rest pose, the clip runs 1..last+1, and the scene is renamed
    restlead - CrucibleMixamoSetup drops frame 0 by that name, so the lead-in never plays.
    """
    shift_keys(arm, 1)
    for pb in arm.pose.bones:
        # clear and key in the same breath: a view_layer.update() in between re-evaluates the
        # action and puts the posed values straight back, so the lead-in would key the pose again
        pb.matrix_basis = Matrix()
        pb.keyframe_insert("rotation_quaternion", frame=0)
        if pb.name.split(":")[-1] == "Hips":
            pb.keyframe_insert("location", frame=0)
    # and leave the rig EVALUATED on the lead-in frame: the exporter takes the file's node
    # transforms from the depsgraph as it finds it, so whatever is posed here becomes the bind
    # pose. Sitting on frame 0 is what makes that the rest pose instead of the last frame built.
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    bpy.context.scene.name = REST_LEAD_SCENE


def export(arm, name, last):
    out = os.path.join(OUT_DIR, name + ".fbx")
    rest_lead_in(arm)
    last += 1
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, last
    # An FBX whose only object is the armature has no wrapper: Unity makes the ARMATURE the file
    # root, so the 100x scale Blender writes on it lands on the avatar's root. The body FBX carries
    # a mesh too, so there it is root(1) -> Salaadin_Rig(100) -> Hips. Unity reads an avatar's human
    # scale in ROOT space, so that one missing level made the clip's avatar believe the rig was 100x
    # smaller than the body's - and retargeting across two human scales quietly rewrites every
    # stretched or twisted joint: the guard arrived with the elbows 16 deg tighter and the shield
    # rolled 76 deg off square, while the clip itself measured perfect. Exporting one empty object
    # beside the armature keeps the armature a CHILD of the file root, and the two avatars match.
    tag = bpy.data.objects.get(BODY_NODE)
    if tag is None or tag.type != 'EMPTY':
        tag = bpy.data.objects.new(BODY_NODE, None)
        bpy.context.scene.collection.objects.link(tag)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    tag.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, object_types={'ARMATURE', 'EMPTY'},
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
# pose() takes (where the BLADE points, where the palm faces) and squares the edge to it. Tip at
# the opponent and tilted down a little, so the edge leads any cut he opens with.
G_BLADE = (FWD - 0.35 * UP, -RIGHT)
# The shield's BOSS - the decorated face - sits on -palm (salaadin_body.py), so the palm argument
# points AWAY from the opponent: Richard's (UP, FWD) would show the enemy the shield's back.
G_SHIELD = (UP, -FWD)


def guard_pose(arm, guard, yaw=0.0, **over):
    """The stance, with any part of it overridden."""
    apply_frame(arm, guard)
    spec = dict(right=G_RIGHT, left=G_LEFT, blade=G_BLADE, shield=G_SHIELD, yaw=yaw)
    spec.update(over)
    pose(arm, **spec)


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


def clip_shin_cut(arm, name="Salaadin_ShinCut"):
    """His MEDIUM: a low saber cut across the shins, dropped under a high guard.

    This was the back half of the old Breaking Kick, whose front kick is gone - he fights with the
    falchion only now, so the move is the cut on its own, opened out so it still fills a swing: a
    deeper wind back, a step down into the cut, and a slower recovery to the guard.
    """
    guard = take(GUARD)[0]
    f = 0
    for df, spec in ((7,  dict(right=(-0.30, 0.60, -0.60), blade=(-FWD, -RIGHT))),  # wind the saber back low and wide
                     (4,  dict(right=(0.10, 0.55, -0.80), blade=(-FWD - UP, -RIGHT))),  # drop under the guard
                     (6,  dict(right=(0.88, 0.20, -0.40), blade=(FWD, -RIGHT))),    # the low cut lands
                     (5,  dict(right=(0.45, -0.45, -0.55), blade=(-RIGHT, UP))),    # follow through across the shins
                     (12, dict(right=G_RIGHT, blade=G_BLADE))):
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


def clip_saber_riposte(arm, name="Salaadin_SaberRiposte"):
    """The parry riposte: a short saber counter straight off the catch. No kick - he has none.

    Shorter and tighter than any of his normals, because it is paid for by the parry: the blade
    barely leaves the guard before it comes back across the opponent's centre.
    """
    guard = take(GUARD)[0]
    f = 0
    for df, spec in ((4,  dict(right=(0.15, 0.55, -0.35), blade=(-FWD + UP, -RIGHT))),  # cock the wrist, blade up and back
                     (5,  dict(right=(0.85, 0.05, 0.10), blade=(FWD, -RIGHT))),         # the counter, straight through centre
                     (4,  dict(right=(0.50, -0.45, 0.20), blade=(-RIGHT, UP))),         # across and out to his left
                     (9,  dict(right=G_RIGHT, blade=G_BLADE))):
        f += df
        guard_pose(arm, guard, **spec)
        key(arm, f)
    return f


BUILDERS = {
    "Salaadin_GuardIdle": clip_guard_idle,
    "Salaadin_VeiledRush": clip_veiled_rush,
    "Salaadin_ShinCut": clip_shin_cut,
    "Salaadin_WhirlingSaif": clip_whirling_saif,
    "Salaadin_SaberRiposte": clip_saber_riposte,
}

for name, build in BUILDERS.items():
    if WANTED and name not in WANTED:
        continue
    print(f"== {name}")
    arm = load()
    new_action(arm, name)
    last = build(arm)
    if CHECK:   # before the export, which inserts the rest-pose lead-in and shifts every key
        render_frames(arm, name, [int(last * f) for f in (0.0, 0.25, 0.45, 0.6, 1.0)])
        render_frames(arm, name, [int(last * 0.45)], view="side")
    export(arm, name, last)
print("CLIPS-OK")
