"""Landmarks JSON (ArtSource/Mocap/pose_extract.py) -> a keyed clip on a fighter's anim master.

    blender -b --python video_mocap.py -- <Name>_AnimMaster.blend <landmarks.json> <ClipName> <out_dir> \
        [--frames N] [--trim A:B] [--smooth 5] [--key-every 2] [--render DIR] [--no-export]

What it does, in the conventions author_clips.py already uses (+X forward, +Y left, +Z up,
offsets relative to the rig's rest pose):

  * Builds the performer's basis from the held T-pose the extractor found, so the performer
    faces +X however they stood relative to the camera.
  * Drives the rig by DIRECTION, not by position: each limb target is the rig's own limb length
    times how far the performer extended that limb (as a fraction of their own T-pose limb).
    A 1.8 m human therefore drives a 0.98-unit rig with no unreachable IK targets and no skating.
  * Clip stays IN PLACE. MediaPipe world landmarks are hip-origin, so global translation is not
    in the data anyway, and every clip in this project is in place (the Animator has no root
    motion). Hip HEIGHT does come through - crouches, dips and the drive out of a stance.
  * Keys the IK foot/hand controls, hip height + body yaw, spine, neck and head. Hand and foot
    ROTATION are left at rest: single-camera capture has no reliable wrist orientation, so the
    weapon hand is aimed afterwards by hand (and in game by WeaponAim).

Then bake_and_export() from the master's export_tools text writes the armature-only FBX, same as
author_clips.py. One clip per Blender run (the export is destructive).

The Rig helper class is taken from author_clips.py at run time rather than copied, so the pose
conventions have exactly one source of truth.
"""
import bpy, sys, os, json, math
from mathutils import Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
if len(args) < 4:
    raise SystemExit(__doc__)
MASTER, LANDMARKS, CLIP, OUT_DIR = args[0], args[1], args[2], args[3]
HERE = os.path.dirname(os.path.abspath(__file__))


def opt(name, default=None, cast=str):
    if name in args:
        return cast(args[args.index(name) + 1])
    return default


CLIP_FRAMES = opt("--frames", 0, int)        # 0 = keep the source length (retimed to 60fps)
LEGS = opt("--legs", "auto")                 # auto | on | off - drive the legs from the capture?
CALIB_JSON = opt("--calib", None)            # landmarks of a separate T-pose video (limb lengths)
FACE = opt("--face", "take")                 # take | tpose - where the forward direction comes from
WEAPON = opt("--weapon", "on")               # on | off - swing the weapon with the two-hand line
GRIP = opt("--grip", "lr")                   # lr = left hand is the one nearer the head, rl = swapped
TRIM = opt("--trim", None)                   # "A:B" in SOURCE frame numbers
SMOOTH = opt("--smooth", 5, int)             # moving-average window, frames (odd)
KEY_EVERY = opt("--key-every", 2, int)
RENDER_DIR = opt("--render", None)
NO_EXPORT = "--no-export" in args
LOOP = "--loop" in args                      # close the clip: last frame = first (idles, walks)
CONTACT_AT = opt("--contact-at", -1, float)  # clip frame the strike should land on

# MediaPipe Pose indices
NOSE = 0
L_SH, R_SH, L_EL, R_EL, L_WR, R_WR = 11, 12, 13, 14, 15, 16
L_HIP, R_HIP, L_KN, R_KN, L_AN, R_AN = 23, 24, 25, 26, 27, 28

# Sanity clamps. The project's retarget rules (CLAUDE.md) are why these are tight: Unity's
# Humanoid muscle ranges clamp anything wilder and the pose comes out as hanging arms.
MAX_SPINE_LEAN, MAX_SPINE_SIDE, MAX_SPINE_TWIST = 40.0, 25.0, 35.0
MAX_NECK, MAX_HEAD = 25.0, 35.0
# Rig.spine keys Spine01 AND Spine02 (Rig.neck keys two bones too), so a measured angle handed
# to it straight lands TWICE on the body. These split the measurement across the chain.
SPINE_BONES, NECK_BONES = 2, 2
# How much of the performer's own standing posture to keep. The capture is filmed in a living
# room, not in a guard, and his habitual forward lean would otherwise be baked into every clip
# on top of the stance the rig already has. 0 = keep only the lean the MOVE adds, 1 = absolute.
POSTURE_KEEP = 0.35
HIP_DIP_MIN, HIP_DIP_MAX = -0.16, 0.06      # rig units
MAX_LIMB_EXTENSION = 0.97                    # fraction of the rig's own limb length

# ---------------------------------------------------------------------------------------------
# the Rig helper, lifted out of author_clips.py (one source of truth for the conventions)
# ---------------------------------------------------------------------------------------------
bpy.ops.wm.open_mainfile(filepath=MASTER)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
scene = bpy.context.scene
exec(bpy.data.texts["export_tools.py"].as_string())

FWD, LEFT, UP = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
src = open(os.path.join(HERE, "author_clips.py"), encoding="utf-8").read()
start = src.index("def action_fcurves")
end = src.index("# ---- clip registry")
exec(src[start:end])          # defines action_fcurves and class Rig

# ---------------------------------------------------------------------------------------------
# the rig's own measurements
# ---------------------------------------------------------------------------------------------
MW = arm.matrix_world
def rest(name):
    return (MW @ arm.pose.bones[name].bone.matrix_local).to_translation()

RIG = {
    "hip": rest("Hip"),
    "shoulder_mid": (rest("L_Upperarm") + rest("R_Upperarm")) / 2.0,
    "upper_arm": (rest("L_Forearm") - rest("L_Upperarm")).length,
    "forearm": (rest("L_Hand") - rest("L_Forearm")).length,
    "thigh": (rest("L_Calf") - rest("L_Thigh")).length,
    "shin": (rest("L_Foot") - rest("L_Calf")).length,
}
RIG["arm"] = RIG["upper_arm"] + RIG["forearm"]
RIG["leg"] = RIG["thigh"] + RIG["shin"]
RIG["spine"] = (RIG["shoulder_mid"] - RIG["hip"]).length
SHOULDER = {"L": rest("L_Upperarm"), "R": rest("R_Upperarm")}
HIP_JOINT = {"L": rest("L_Thigh"), "R": rest("R_Thigh")}
print(f"rig: arm {RIG['arm']:.3f}, leg {RIG['leg']:.3f}, spine {RIG['spine']:.3f}, "
      f"hip height {RIG['hip'].z:.3f}")

# ---------------------------------------------------------------------------------------------
# read the capture
# ---------------------------------------------------------------------------------------------
data = json.load(open(LANDMARKS, encoding="utf-8"))
# The T-pose is usually filmed once, in its own video: --calib points at that capture and only
# its limb lengths are used (they are rotation-invariant, so the angle it was filmed at does not
# matter). Facing comes from the take itself - see below.
calib_src = json.load(open(CALIB_JSON, encoding="utf-8")) if CALIB_JSON else data
calib = calib_src.get("calibration")
if not calib:
    raise SystemExit("no calibration available - pass --calib <tpose landmarks.json>")
if CALIB_JSON:
    print(f"calibration from {os.path.basename(CALIB_JSON)} "
          f"(T-pose score {calib['tpose_score']:.2f})")
src_fps = data["fps"]
frames = [f for f in data["frames"] if f["world"]]
if not frames:
    raise SystemExit("no frames with a detected pose")

# Fill gaps: carry the nearest detected frame into frames the detector missed.
by_index = {f["i"]: f for f in frames}
lo_i, hi_i = frames[0]["i"], frames[-1]["i"]
seq = []
last = frames[0]
for i in range(lo_i, hi_i + 1):
    f = by_index.get(i, last)
    seq.append([Vector(p) for p in f["world"]])
    last = f
missing = (hi_i - lo_i + 1) - len(by_index)
print(f"capture: {len(seq)} source frames at {src_fps:.1f} fps "
      f"({missing} filled from neighbours)")

# Legs: MediaPipe returns invented joints when they are out of frame, and an invented leg folds
# the fighter up. Drive the legs only when the capture actually saw them.
leg_vis = (data.get("visibility") or {}).get("legs")
if LEGS == "on":
    legs_ok = True
elif LEGS == "off":
    legs_ok = False
else:
    legs_ok = leg_vis is None or leg_vis >= 0.5
if not legs_ok:
    print(f"legs: NOT driven (tracking {leg_vis if leg_vis is None else round(leg_vis, 2)}) - "
          "feet stay in the rest stance and the hips hold their height. Reshoot with the whole "
          "body in frame, or pass --legs on to use them anyway.")
else:
    print(f"legs: driven from the capture (tracking "
          f"{'n/a' if leg_vis is None else round(leg_vis, 2)})")

UP_MP = Vector((0, -1, 0))                   # camera space: image y is down, so up is -y
mean = [Vector(p) for p in calib["landmarks_mean"]]

# The performer's own limb lengths, straight off the T-pose hold. Lengths do not depend on which
# way they were facing, so a T-pose filmed once in its own video calibrates every take.
P = {
    "upper_arm": ((mean[L_EL] - mean[L_SH]).length + (mean[R_EL] - mean[R_SH]).length) / 2,
    "forearm": ((mean[L_WR] - mean[L_EL]).length + (mean[R_WR] - mean[R_EL]).length) / 2,
    "thigh": ((mean[L_KN] - mean[L_HIP]).length + (mean[R_KN] - mean[R_HIP]).length) / 2,
    "shin": ((mean[L_AN] - mean[L_KN]).length + (mean[R_AN] - mean[R_KN]).length) / 2,
}
P["arm"] = P["upper_arm"] + P["forearm"]
P["leg"] = P["thigh"] + P["shin"]
P["spine"] = ((mean[L_SH] + mean[R_SH]) / 2 - (mean[L_HIP] + mean[R_HIP]) / 2).length
P["hip_height"] = abs(((mean[L_AN] + mean[R_AN]) / 2
                       - (mean[L_HIP] + mean[R_HIP]) / 2).dot(UP_MP))
print(f"performer: arm {P['arm']:.3f} m, leg {P['leg']:.3f} m, spine {P['spine']:.3f} m")

# Smooth (still in camera space - the basis has not been chosen yet).
if SMOOTH > 1:
    half = SMOOTH // 2
    smoothed = []
    for i in range(len(seq)):
        lo, hi = max(0, i - half), min(len(seq), i + half + 1)
        smoothed.append([sum((seq[j][k] for j in range(lo, hi)), Vector()) / (hi - lo)
                         for k in range(len(seq[i]))])
    seq = smoothed

if TRIM:
    a, b = (int(x) for x in TRIM.split(":"))
    a, b = max(0, a - lo_i), min(len(seq), b - lo_i + 1)
    seq = seq[a:b]
    print(f"trimmed to {len(seq)} frames")
if len(seq) < 2:
    raise SystemExit("fewer than 2 frames after trimming")

# Facing. One video holds the same move from several angles, so the forward direction has to come
# from THIS take, not from a calibration frame filmed facing somewhere else. It is taken from the
# HIP line (the hips stay square; the shoulders are bladed in a guard) over the opening frames,
# where the performer is still in the idle before the move starts.
def basis_from(poses):
    across = Vector()
    for pose in poses:
        a = pose[L_HIP] - pose[R_HIP]              # points to the performer's left
        across += a - UP_MP * a.dot(UP_MP)
    left = across.normalized()
    return left.cross(UP_MP).normalized(), left    # right-handed: X = Y x Z

if FACE == "tpose":
    fwd_mp, left_mp = basis_from([mean])
    print("facing: from the T-pose calibration (--face tpose)")
else:
    head = max(2, min(len(seq) // 6, 15))          # the idle at the head of the take
    fwd_mp, left_mp = basis_from(seq[:head])
    print(f"facing: from the first {head} frames of this take (hips)")
print(f"basis: performer faces ({fwd_mp.x:+.2f}, {fwd_mp.y:+.2f}, {fwd_mp.z:+.2f}) in camera space")

def to_rig(v):
    """MediaPipe world vector (hip-origin, metres) -> rig axes (+X fwd, +Y left, +Z up)."""
    return Vector((v.dot(fwd_mp), v.dot(left_mp), v.dot(UP_MP)))

seq = [[to_rig(p) for p in pose] for pose in seq]
ref = seq[0]

# ---------------------------------------------------------------------------------------------
# solve each frame into rig terms
# ---------------------------------------------------------------------------------------------
def yaw_of(v):
    return math.degrees(math.atan2(v.y, v.x))

def clamp(v, lo, hi):
    return max(lo, min(hi, v))

# Zero of the body yaw: the idle at the head of this take (so a take filmed at any angle starts
# the clip facing +X, and the yaw curve is the turn the move itself makes).
_head = max(2, min(len(seq) // 6, 15))
base_hip_yaw = sum(yaw_of(p[L_HIP] - p[R_HIP]) for p in seq[:_head]) / _head
solved, warn_reach = [], 0
prev_raw_yaw = 0.0
# Baseline torso attitude over those same opening idle frames: what this performer calls
# "standing still". Leans are measured against it (see POSTURE_KEEP).
def _torso_angles(p):
    t = ((p[L_SH] + p[R_SH]) / 2 - (p[L_HIP] + p[R_HIP]) / 2)
    t = t.normalized() if t.length > 1e-5 else Vector((0, 0, 1))
    return math.degrees(math.atan2(t.x, t.z)), math.degrees(math.atan2(t.y, t.z))
base_lean = sum(_torso_angles(p)[0] for p in seq[:_head]) / _head
base_side = sum(_torso_angles(p)[1] for p in seq[:_head]) / _head
print(f"posture: standing lean {base_lean:+.1f} deg, side {base_side:+.1f} deg "
      f"(keeping {POSTURE_KEEP:.0%} of it)")

for pose in seq:
    hip_mid = (pose[L_HIP] + pose[R_HIP]) / 2
    sh_mid = (pose[L_SH] + pose[R_SH]) / 2
    hip_line = pose[L_HIP] - pose[R_HIP]
    sh_line = pose[L_SH] - pose[R_SH]

    # body yaw: how far the hips have turned since the T-pose
    # UNWRAPPED. Wrapping each frame into (-180, 180] turns a spin into a jump: Bjorn's combo
    # take sweeps +252 deg, which wraps to -108 and rotates him the wrong way. Accumulate the
    # frame-to-frame step instead, so a 360 reads as 360.
    raw_yaw = yaw_of(hip_line) - base_hip_yaw
    if solved:
        step = (raw_yaw - prev_raw_yaw + 180) % 360 - 180
        body_yaw = solved[-1]["yaw"] + step
    else:
        body_yaw = (raw_yaw + 180) % 360 - 180
    prev_raw_yaw = raw_yaw

    # torso attitude
    torso = (sh_mid - hip_mid)
    if torso.length < 1e-5:
        torso = Vector((0, 0, 1))
    t = torso.normalized()
    lean = math.degrees(math.atan2(t.x, t.z)) - base_lean * (1.0 - POSTURE_KEEP)
    side = math.degrees(math.atan2(t.y, t.z)) - base_side * (1.0 - POSTURE_KEEP)
    lean = clamp(lean, -MAX_SPINE_LEAN, MAX_SPINE_LEAN)
    side = clamp(side, -MAX_SPINE_SIDE, MAX_SPINE_SIDE)
    twist = (yaw_of(sh_line) - yaw_of(hip_line) + 180) % 360 - 180
    twist = clamp(twist, -MAX_SPINE_TWIST, MAX_SPINE_TWIST)

    # head, relative to the shoulder line
    hv = pose[NOSE] - sh_mid
    head_lean = clamp(math.degrees(math.atan2(hv.x, max(1e-5, hv.z))) - lean, -MAX_HEAD, MAX_HEAD)
    head_side = clamp(math.degrees(math.atan2(hv.y, max(1e-5, hv.z))) - side, -MAX_HEAD, MAX_HEAD)

    # hip height: how much the performer dropped, as a fraction of their standing hip height,
    # applied to the rig's hip height
    if legs_ok:
        ankle_mid = (pose[L_AN] + pose[R_AN]) / 2
        stand = abs((ankle_mid - hip_mid).z)
        dip = clamp((stand - P["hip_height"]) / max(1e-5, P["hip_height"]) * RIG["hip"].z,
                    HIP_DIP_MIN, HIP_DIP_MAX)
    else:
        dip = 0.0

    entry = {"yaw": body_yaw, "lean": lean, "side": side, "twist": twist,
             "head_lean": head_lean, "head_side": head_side, "dip": dip,
             "hands": {}, "feet": {}}

    # limbs: direction from the capture, length from the rig
    hip_rig = RIG["hip"] + Vector((0, 0, dip))
    for side_tag, sh_i, wr_i in (("L", L_SH, L_WR), ("R", R_SH, R_WR)):
        reach = pose[wr_i] - pose[sh_i]
        frac = reach.length / max(1e-5, P["arm"])
        if frac > MAX_LIMB_EXTENSION:
            warn_reach += 1
            frac = MAX_LIMB_EXTENSION
        shoulder_rig = SHOULDER[side_tag] + Vector((0, 0, dip))
        target = shoulder_rig + reach.normalized() * (RIG["arm"] * frac)
        entry["hands"][side_tag] = target

    if legs_ok:
        for side_tag, hp_i, an_i in (("L", L_HIP, L_AN), ("R", R_HIP, R_AN)):
            leg = pose[an_i] - pose[hp_i]
            frac = min(MAX_LIMB_EXTENSION, leg.length / max(1e-5, P["leg"]))
            target = (HIP_JOINT[side_tag] + Vector((0, 0, dip))
                      + leg.normalized() * (RIG["leg"] * frac))
            entry["feet"][side_tag] = target
    solved.append(entry)

# Ground solve: shift the WHOLE body (hip, hands, feet together, so the leg geometry and the IK
# reach stay exactly as solved) until the lowest foot of the clip sits on the floor. Then clamp
# the hip to a sane height and shift everything by that correction too - a bad detection must
# not bury the fighter in the floor or hang him in the air.
floor = min(min(e["feet"]["L"].z, e["feet"]["R"].z) for e in solved) if legs_ok else 0.0
if legs_ok and floor > 0.05:
    print(f"ground solve: lowest foot is {floor:.3f} above the floor - airborne capture, or "
          "the legs were not tracked well")
shift = -floor
dips = [e["dip"] + shift for e in solved]
over = max(0.0, max(dips) - HIP_DIP_MAX) or min(0.0, min(dips) - HIP_DIP_MIN)
shift -= over
for e in solved:
    e["dip"] = clamp(e["dip"] + shift, HIP_DIP_MIN, HIP_DIP_MAX)
    for k in ("L", "R"):
        if legs_ok:
            e["feet"][k] = e["feet"][k] + Vector((0, 0, shift))
            if e["feet"][k].z < 0:
                e["feet"][k].z = 0.0
        e["hands"][k] = e["hands"][k] + Vector((0, 0, shift))
print(f"ground solve: shifted {shift:+.3f} (floor {floor:+.3f}, hip clamp {over:+.3f}); "
      f"{warn_reach} frames clamped at max arm extension")

# ---------------------------------------------------------------------------------------------
# key it
# ---------------------------------------------------------------------------------------------
clip_frames = CLIP_FRAMES or max(2, int(round(len(solved) / src_fps * 60.0)))
print(f"clip: {len(solved)} source frames -> {clip_frames} frames at 60fps")

rig = Rig(arm)
if arm.animation_data and arm.animation_data.action:
    arm.animation_data.action = None
act = bpy.data.actions.new(CLIP)
arm.animation_data_create()
arm.animation_data.action = act
scene.frame_start, scene.frame_end = 0, clip_frames

REST_HAND = {"L": rest("IK_L_Hand"), "R": rest("IK_R_Hand")}
REST_FOOT = {"L": rest("IK_L_Foot"), "R": rest("IK_R_Foot")}

# ---- the weapon swings with the line between the two hands ------------------------------------
# A single camera gives no wrist orientation, so without this the axe stays at whatever angle the
# rest pose holds it and the fighter looks like he is carrying it, not swinging it. But on a
# two-handed grip the shaft IS the line between the wrists, and both wrists are tracked. R_Hand
# copies its rotation from IK_R_Hand and the weapon is bone-parented to R_Hand, so rotating that
# control swings the weapon. The twist about the shaft (edge alignment) is not knowable from one
# camera: rotation_difference gives the minimal arc, which leaves the authored edge alone.
weapon_obj = next((o for o in bpy.data.objects
                   if o.type == 'MESH' and o.parent_bone == "R_Hand"), None)
shaft_rest = None
if WEAPON == "on" and weapon_obj is not None:
    # prep_weapon.py lays the handle along mesh-local +Z, pointing from the grip to the head.
    shaft_rest = (weapon_obj.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
    print(f"weapon: {weapon_obj.name}, rest shaft ({shaft_rest.x:+.2f}, {shaft_rest.y:+.2f}, "
          f"{shaft_rest.z:+.2f}); swung from the two-hand line (grip {GRIP})")
elif WEAPON == "on":
    print("weapon: none parented to R_Hand - hand rotation left at rest")

def set_hand_rotation(side, q_world):
    """Put a world-space rotation on an IK hand control (same mapping Rig._ctrl uses)."""
    name = f"IK_{side}_Hand"
    b = rig.pb[name]
    R = rig.rest_world[name].to_3x3()
    b.rotation_quaternion = (R.inverted() @ q_world.to_matrix() @ R).to_quaternion()
    rig.pending[name] = True

# Where the strike lands. Retiming a capture linearly puts the contact wherever the performance
# happened to put it, which is rarely startup + active/2 of the AttackData. Find the moment of
# the strike (peak speed of the weapon TIP - on a swing the head crosses well before the hand
# peaks) and bend the retime around it: everything before the contact is squeezed or stretched
# into [0, contact_at], everything after into [contact_at, clip_frames].
contact_src = -1
if CONTACT_AT >= 0:
    tip_len = max(weapon_obj.dimensions) if weapon_obj else 0.0
    tips, best = [], (0.0, -1)
    for e in solved:
        shaft = e["hands"]["L"] - e["hands"]["R"]
        if GRIP == "rl":
            shaft = -shaft
        tips.append(e["hands"]["R"] + (shaft.normalized() * tip_len if shaft.length > 1e-4
                                       else Vector()))
    for i in range(1, len(tips)):
        speed = (tips[i] - tips[i - 1]).length
        if speed > best[0]:
            best = (speed, i)
    contact_src = best[1]
    print(f"contact: source frame {contact_src}/{len(solved) - 1} (tip speed peak) "
          f"-> clip frame {CONTACT_AT:.0f}/{clip_frames}")

def clip_frame(i):
    n = max(1, len(solved) - 1)
    if contact_src <= 0 or contact_src >= n:
        return int(round(i / n * clip_frames))
    if i <= contact_src:
        return int(round(i / contact_src * CONTACT_AT))
    return int(round(CONTACT_AT + (i - contact_src) / (n - contact_src)
                     * (clip_frames - CONTACT_AT)))

keyed = 0
for i, e in enumerate(solved):
    if i % KEY_EVERY and i != len(solved) - 1:
        continue
    frame = clip_frame(i)
    rig.hip(dz=e["dip"], yaw=e["yaw"])
    rig.spine(lean=e["lean"] / SPINE_BONES, side=e["side"] / SPINE_BONES,
              twist=e["twist"] / SPINE_BONES)
    rig.neck(lean=clamp(e["head_lean"] * 0.4, -MAX_NECK, MAX_NECK) / NECK_BONES,
             side=clamp(e["head_side"] * 0.4, -MAX_NECK, MAX_NECK) / NECK_BONES)
    rig.head(lean=e["head_lean"] * 0.6, side=e["head_side"] * 0.6)
    for k in ("L", "R"):
        d = e["hands"][k] - REST_HAND[k]
        rig.hand(k, d.x, d.y, d.z)
        if legs_ok:
            d = e["feet"][k] - REST_FOOT[k]
            rig.foot(k, d.x, d.y, d.z)
    if shaft_rest is not None:
        shaft = e["hands"]["L"] - e["hands"]["R"]       # grip -> head, for a left-forward grip
        if GRIP == "rl":
            shaft = -shaft
        if shaft.length > 1e-4:
            set_hand_rotation("R", shaft_rest.rotation_difference(shaft.normalized()))
    rig.key(frame, ease="bezier")
    keyed += 1

if LOOP:
    # Same close author_clips.py uses: copy frame 0 onto the last frame of every curve.
    for fc in action_fcurves(act, arm):
        fc.keyframe_points.insert(clip_frames, fc.evaluate(0), options={'FAST'})
        fc.update()
    print(f"looped: frame {clip_frames} = frame 0")

print(f"keyed {keyed} poses, {len(action_fcurves(act, arm))} curves")
yaws = [e["yaw"] for e in solved]
dips = [e["dip"] for e in solved]
print(f"body yaw {min(yaws):+.0f}..{max(yaws):+.0f} deg, hip {min(dips):+.3f}..{max(dips):+.3f}")

if RENDER_DIR:
    # Same filmstrip author_clips.py renders: workbench, weapons red, 8 frames across the clip.
    os.makedirs(RENDER_DIR, exist_ok=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    cam = bpy.data.objects.new("StripCam", bpy.data.cameras.new("StripCam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (1.45, -1.75, 0.62); cam.rotation_euler = (math.radians(82), 0, math.radians(40))
    cam.data.lens = 50
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'OBJECT'
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.color = (0.9, 0.2, 0.2, 1) if "Body" not in o.name else (0.65, 0.65, 0.65, 1)
    scene.render.resolution_x, scene.render.resolution_y = 480, 720
    for n in range(8):
        f = int(round(n / 7.0 * clip_frames))
        scene.frame_set(f)
        scene.render.filepath = os.path.join(RENDER_DIR, f"{CLIP}_{n:02d}_f{f:03d}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    print(f"filmstrip: {RENDER_DIR}")

if not NO_EXPORT:
    # bake_and_export starts with select_all, whose poll needs object mode (Rig left us in pose).
    bpy.ops.object.mode_set(mode='OBJECT')
    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, f"{CLIP}.fbx")
    bake_and_export(out, 0, clip_frames, arm)
    print(f"exported: {out}")
else:
    print("--no-export: clip left in the blend, nothing written")
