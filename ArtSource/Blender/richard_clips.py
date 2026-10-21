"""Author the sword-and-shield clips the Mixamo pack does not contain, on Richard's own rig.

Usage: blender -b --python richard_clips.py -- <Richard_Body.fbx> <pack_dir> <out_dir> <render_dir> [name ...]

The Pro Sword and Shield pack has no thrust and no shield strike, which are Richard's Light attack
and the opening of his SP + Medium, so they are built here. Two tools do all four clips:

  pose()   analytic two-bone IK: the hand is put on a target point in the fighter's own frame
           (fwd / lateral / up metres from the hips) and the wrist is aimed so the blade points
           where the move needs it. No search - the elbow comes from the triangle, with a pole
           vector keeping it behind and below the arm the way a real elbow sits.
  take()   samples a pack clip into per-frame bone poses, so a mocap take can be spliced after an
           authored one (the double swing) or carry an authored arm over it (the bash, the charge).

Everything exports as an armature-only FBX at 30 fps, which Unity imports exactly like a pack file.
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
BODY_NODE = "Richard_ClipRoot"



def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=BODY)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
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
    frames = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        bpy.context.view_layer.update()
        frames.append({b.name.split(":")[-1]: (b.matrix_basis.copy(), b.location.copy())
                       for b in src.pose.bones})
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
        pb.matrix_basis = frame[short][0].copy()
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


def reach(arm, side, target, pole):
    """Analytic two-bone IK: upper arm + forearm, so the wrist lands on target (world space)."""
    uarm, farm, hand = B(arm, side + "Arm"), B(arm, side + "ForeArm"), B(arm, side + "Hand")
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


def pose(arm, right=None, left=None, blade=None, shield=None):
    """Hands to (fwd, lateral, up) metres from the hips; weapons aimed along world directions."""
    h = wpos(arm, B(arm, "Hips"))
    if right:
        t = h + FWD * right[0] + RIGHT * right[1] + UP * right[2]
        miss = reach(arm, "Right", t, h + RIGHT * 0.6 - FWD * 0.5)
        if miss > 0.03:
            print(f"    right hand miss {miss:.3f}")
    if left:
        t = h + FWD * left[0] + RIGHT * left[1] + UP * left[2]
        miss = reach(arm, "Left", t, h - RIGHT * 0.6 - FWD * 0.5)
        if miss > 0.03:
            print(f"    left hand miss {miss:.3f}")
    if blade:
        aim(arm, "Right", blade[0], blade[1])
    if shield:
        aim(arm, "Left", shield[0], shield[1])


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

GUARD = os.path.join(PACK, "sword and shield block idle.fbx")


def clip_thrust(arm, name="Richard_Thrust", travel=0.0, frames=(0, 7, 15, 19, 33)):
    """Light attack: from the guard, cock the sword by the hip and drive it straight forward.

    travel > 0 slides the hips forward through the stab, which is what makes the SP + Light
    charge version read as a run-in rather than a stand-and-poke.
    """
    guard = take(GUARD)[0]
    f_start, f_cock, f_hit, f_hold, f_end = frames
    apply_frame(arm, guard)
    hips = B(arm, "Hips")
    hips_rest = hips.location.copy()

    def stance():
        apply_frame(arm, guard)

    # f0 - the guard the clip starts and ends in
    stance()
    key(arm, f_start)
    # f_cock - sword drawn back to the right hip, point already forward, shield up
    stance()
    pose(arm, right=(-0.02, 0.28, 0.14), left=(0.26, -0.24, 0.26),
         blade=(FWD, -RIGHT), shield=(UP, FWD))
    key(arm, f_cock)
    # f_hit - full extension, shield tucked in front of the chest
    stance()
    hips.location = hips_rest + Vector((0, 0, travel / max(arm.matrix_world.to_scale()[0], 1e-6)))
    pose(arm, right=(0.46, 0.08, 0.33), left=(0.30, -0.22, 0.30),
         blade=(FWD, -RIGHT), shield=(UP, FWD))
    key(arm, f_hit)
    # f_hold - the point hangs out there for a couple of frames
    key(arm, f_hold)
    # back to the guard
    hips.location = hips_rest
    stance()
    key(arm, f_end)
    return f_end


def clip_shield_bash(arm, name="Richard_ShieldBash"):
    """SP + Medium: a shield strike that carries into the pack's low diagonal cut.

    The cut is mocap (slash (5)); the bash is keyed over its first frames on the left arm only,
    so the legs and the sword arm keep the take's weight.
    """
    cut = take(os.path.join(PACK, "sword and shield slash (5).fbx"))
    guard = take(GUARD)[0]
    bash_in, bash_hit, bash_out = 0, 9, 16
    # the bash: left arm punches the shield out, right arm waits low
    apply_frame(arm, guard)
    key(arm, bash_in)
    apply_frame(arm, guard)
    pose(arm, left=(0.02, -0.30, 0.26), right=(-0.06, 0.30, 0.10),
         shield=(UP, FWD), blade=(FWD, -RIGHT))
    key(arm, 4)
    apply_frame(arm, guard)
    pose(arm, left=(0.52, -0.10, 0.28), right=(-0.06, 0.32, 0.08),
         shield=(UP, FWD), blade=(FWD, -RIGHT))
    key(arm, bash_hit)
    # then the mocap cut, offset so its first frame sits right after the bash
    for i, fr in enumerate(cut):
        f = bash_out + i
        apply_frame(arm, fr)
        if i < 6:      # keep the shield out through the hand-over
            pose(arm, left=(0.46 - i * 0.05, -0.12, 0.28), shield=(UP, FWD))
        key(arm, f)
    return bash_out + len(cut) - 1


def clip_double_high(arm, name="Richard_DoubleHigh"):
    """SP + Heavy: the pack's overhead cut spliced straight into its rising cut - two high swings."""
    a = take(os.path.join(PACK, "sword and shield slash.fbx"))        # overhead, contact ~18
    b = take(os.path.join(PACK, "sword and shield slash (3).fbx"))    # diagonal-up, contact ~27
    a_end = 30            # drop the overhead's long recovery
    blend = 6
    f = 0
    for i in range(a_end):
        apply_frame(arm, a[i])
        key(arm, f)
        f += 1
    for i in range(10, len(b)):     # skip the rising cut's slow wind-up
        apply_frame(arm, b[i])
        key(arm, f)
        f += 1
    return f - 1


def clip_charge(arm, name="Richard_ShieldCharge"):
    """SP + Light: shield up, run in, and finish on the thrust."""
    run = take(os.path.join(PACK, "sword and shield run.fbx"))
    guard = take(GUARD)[0]
    n_run = min(len(run), 18)
    f = 0
    for i in range(n_run):
        apply_frame(arm, run[i])
        # shield carried high and square through the run-in
        pose(arm, left=(0.34, -0.20, 0.30), shield=(UP, FWD),
             right=(-0.02, 0.30, 0.12), blade=(FWD, -RIGHT))
        key(arm, f)
        f += 1
    # the stab, with the legs still carrying the run's last pose
    for i, (df, spec) in enumerate((
            (6, dict(right=(0.46, 0.08, 0.33), left=(0.30, -0.22, 0.30))),
            (4, dict(right=(0.46, 0.08, 0.33), left=(0.30, -0.22, 0.30))),
            (12, dict(right=(0.12, 0.26, 0.16), left=(0.26, -0.24, 0.26))))):
        f += df
        apply_frame(arm, run[n_run - 1], only=["Hips", "UpLeg", "Leg", "Foot", "ToeBase", "Spine"])
        pose(arm, blade=(FWD, -RIGHT), shield=(UP, FWD), **spec)
        key(arm, f)
    return f


BUILDERS = {
    "Richard_Thrust": lambda a: clip_thrust(a),
    "Richard_ShieldCharge": clip_charge,
    "Richard_ShieldBash": clip_shield_bash,
    "Richard_DoubleHigh": clip_double_high,
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
