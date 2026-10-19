# Measure Mixamo clips headless so a clip can be cast into a gameplay slot from numbers, not guesses.
#
#   "C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b --factory-startup \
#       --python ArtSource/Blender/clip_curves.py -- <out.json> <clip.fbx> [clip.fbx ...]
#
# Per clip it reports, in the fighter's own frame (origin = hips, facing from the hips bone averaged over
# the opening frames, +fwd forward, +lat to the fighter's right, +up up, metres):
#   - the right hand's path and speed, and a weapon-tip proxy (hand + 0.70 along the two-hand line)
#   - hip yaw per frame, unwrapped, so a spin reads as a real 360 instead of wrapping
#   - the swing core (frames where tip speed >= 30% of peak, contiguous around the peak)
#   - the frame the tip crosses the centre line in front of the body (the contact candidate)
#   - foot speed, so a kick is told from a sword swing
#   - how far the hips travel over the clip (an advancing move vs one thrown from a stand)
# and a silhouette guess from those numbers. Verbose per-frame curves go in the JSON ("frames_detail"),
# the console gets one summary line per clip.
import bpy, sys, json, math, os
from mathutils import Vector

HAND_TO_TIP = 0.70          # metres from the lead hand to the blade tip, the same proxy video_mocap.py uses
CORE_FRACTION = 0.30        # of peak tip speed
FACE_FRAMES = 5             # opening frames averaged for the reference facing
TWO_HAND_GRIP = 0.35        # hands closer than this are both on the haft

B = "mixamorig:"
BONES = dict(hips=B + "Hips", spine=B + "Spine1", head=B + "Head",
             rhand=B + "RightHand", lhand=B + "LeftHand",
             rfore=B + "RightForeArm", lfore=B + "LeftForeArm",
             rarm=B + "RightArm", larm=B + "LeftArm",
             rupleg=B + "RightUpLeg", lupleg=B + "LeftUpLeg",
             rfoot=B + "RightFoot", lfoot=B + "LeftFoot")


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=path)
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm is None:
        raise RuntimeError("no armature in " + path)
    act = arm.animation_data.action if arm.animation_data else None
    if act is None:
        raise RuntimeError("no action in " + path)
    first, last = (int(round(v)) for v in act.frame_range)
    return arm, first, last


def world(arm, name):
    pb = arm.pose.bones.get(name)
    return None if pb is None else arm.matrix_world @ pb.head


def facing(pos):
    # Facing from the HIP JOINT LINE, not a hips bone axis: a bone axis tilts toward vertical when the
    # pelvis pitches (a kick, a jump), and the ground projection then swings wildly - that is what read
    # as 728 degrees of yaw on the jump attack. The line between the hip joints stays horizontal.
    left = pos["lupleg"] - pos["rupleg"]
    left.z = 0
    if left.length < 1e-4:
        left = Vector((0, 1, 0))
    left.normalize()
    return left.cross(Vector((0, 0, 1)))           # fwd x left = up  =>  fwd = left x up


def sample(arm, first, last):
    rows = []
    for f in range(first, last + 1):
        bpy.context.scene.frame_set(f)
        pos = {k: world(arm, n) for k, n in BONES.items()}
        rows.append(dict(f=f, fwd=facing(pos), pos={k: (v.copy() if v else None) for k, v in pos.items()}))
    return rows


def unwrap(angles):
    out, off = [], 0.0
    for i, a in enumerate(angles):
        if i:
            d = a + off - out[-1]
            if d > 180:
                off -= 360
            elif d < -180:
                off += 360
        out.append(a + off)
    return out


def analyse(path):
    arm, first, last = load(path)
    rows = sample(arm, first, last)
    n = len(rows)

    # reference frame: facing averaged over the opening frames, hips as the origin each frame
    ref = Vector((0, 0, 0))
    for r in rows[:FACE_FRAMES]:
        ref += r["fwd"]
    ref.normalize()
    right = Vector((0, 0, 1)).cross(ref)           # up x fwd = the fighter's right
    right.normalize()

    def local(p, hips):
        d = p - hips
        return Vector((d.dot(ref), d.dot(right), d.z))

    yaw = unwrap([math.degrees(math.atan2(r["fwd"].dot(right), r["fwd"].dot(ref))) for r in rows])

    # Which way the weapon points: with both hands on the haft the shaft IS the line between them; in the
    # axe pack's one-handed carry the left hand is nowhere near it, and the handle continues the forearm.
    grip0 = sum((local(r["pos"]["rhand"], r["pos"]["hips"]) - local(r["pos"]["lhand"], r["pos"]["hips"])).length
                for r in rows) / len(rows)
    two_handed = grip0 < TWO_HAND_GRIP

    curves = []
    for i, r in enumerate(rows):
        hips = r["pos"]["hips"]
        rh = local(r["pos"]["rhand"], hips)
        lh = local(r["pos"]["lhand"], hips)
        grip = (rh - lh).length
        along = (rh - lh) if two_handed else (rh - local(r["pos"]["rfore"], hips))
        along = along.normalized() if along.length > 1e-5 else Vector((1, 0, 0))
        curves.append(dict(f=r["f"], yaw=yaw[i], grip=grip, hand=rh, tip=rh + along * HAND_TO_TIP,
                           rfoot=local(r["pos"]["rfoot"], hips),
                           lfoot=local(r["pos"]["lfoot"], hips),
                           hips_world=hips.copy()))

    def speeds(key):
        return [0.0] + [(curves[i][key] - curves[i - 1][key]).length for i in range(1, n)]

    tip_s, hand_s = speeds("tip"), speeds("hand")
    foot_s = [max(a, b) for a, b in zip(speeds("rfoot"), speeds("lfoot"))]

    peak_i = max(range(n), key=lambda i: tip_s[i])
    thresh = tip_s[peak_i] * CORE_FRACTION
    cs = ce = peak_i
    while cs > 0 and tip_s[cs - 1] >= thresh:
        cs -= 1
    while ce < n - 1 and tip_s[ce + 1] >= thresh:
        ce += 1

    # contact candidate: the tip crossing the centre line while out in front, nearest the speed peak
    cross, crossings, source = None, 0, "crossing"
    for i in range(max(cs, 1), ce + 1):
        a, b = curves[i - 1]["tip"], curves[i]["tip"]
        if a.y * b.y <= 0 and a.y != b.y and max(a.x, b.x) > 0.15:
            crossings += 1
            c = curves[i - 1]["f"] + abs(a.y) / max(abs(a.y) + abs(b.y), 1e-6)
            if cross is None or abs(c - curves[peak_i]["f"]) < abs(cross - curves[peak_i]["f"]):
                cross = c
    if cross is None:                               # never crosses (a thrust stays on one side): most forward frame
        cross = float(curves[max(range(cs, ce + 1), key=lambda i: curves[i]["tip"].x)]["f"])
        source = "most_forward"

    core = curves[cs:ce + 1]
    d_lat = max(c["tip"].y for c in core) - min(c["tip"].y for c in core)
    d_up = core[-1]["tip"].z - core[0]["tip"].z
    up_span = max(c["tip"].z for c in core) - min(c["tip"].z for c in core)
    d_fwd = max(c["tip"].x for c in core) - min(c["tip"].x for c in core)
    yaw_total = max(c["yaw"] for c in curves) - min(c["yaw"] for c in curves)
    travel = curves[-1]["hips_world"] - curves[0]["hips_world"]
    travel.z = 0
    hi = max(c["tip"].z for c in core)
    lo = min(c["tip"].z for c in core)
    grip_avg = sum(c["grip"] for c in core) / len(core)
    foot_peak, hand_peak = max(foot_s), max(hand_s)
    foot_peak_i = max(range(n), key=lambda i: foot_s[i])
    lift = min(max(c["rfoot"].z for c in curves), max(c["lfoot"].z for c in curves))
    # the striking foot: how high and how far forward the faster foot gets
    foot_high = max(max(c["rfoot"].z for c in curves), max(c["lfoot"].z for c in curves))
    foot_fwd = max(max(c["rfoot"].x for c in curves), max(c["lfoot"].x for c in curves))

    # Silhouette = one arc label plus modifiers, because the two are independent: "great sword kick"
    # is a kick AND a full turn, and forcing a single label hid one of them.
    # The kick test measures the foot against the TIP, not the hand: in a body-driven sweep the hand
    # hardly moves while the blade flies, which read as a kick.
    kick = foot_peak > max(tip_s) * 0.8 and foot_high > -0.30 and foot_fwd > 0.25
    if kick:
        arc = "kick"
    elif d_up < -0.45 and hi > 0.45:
        # steep (a chop) or slanted (a diagonal cut): which dominates, the drop or the sideways sweep
        arc = "overhead" if abs(d_up) >= d_lat else "diagonal-down"
    elif d_up > 0.35:
        arc = "rising" if d_up >= d_lat else "diagonal-up"
    elif d_fwd > 0.45 and d_lat < 0.45 and up_span < 0.45:
        arc = "thrust"
    elif d_lat >= 0.55:
        arc = "horizontal"
    elif up_span > 0.5:
        arc = "diagonal"
    else:
        arc = "small"
    mods = []
    if yaw_total >= 150:
        mods.append("spin")
    if lift > -0.35 and travel.length > 0.8:
        mods.append("leap")
    elif travel.length > 0.8:
        mods.append("advancing")
    shape = "+".join([arc] + mods)

    fps = bpy.context.scene.render.fps
    return dict(
        clip=os.path.splitext(os.path.basename(path))[0],
        pack=os.path.basename(os.path.dirname(path)),
        first=first, last=last, frames=n, fps=fps, seconds=round(n / fps, 2),
        shape=shape, arc=arc, spin="spin" in mods,
        peak=curves[peak_i]["f"], peak_tip_speed=round(tip_s[peak_i], 4),
        core_start=core[0]["f"], core_end=core[-1]["f"], core_frames=len(core),
        contact=round(cross, 1), contact_frac=round((cross - first) / max(n - 1, 1), 3),
        contact_source=source, crossings=crossings, two_handed=two_handed,
        tip_lat_travel=round(d_lat, 3), tip_up_delta=round(d_up, 3), tip_up_span=round(up_span, 3),
        tip_fwd_travel=round(d_fwd, 3), tip_high=round(hi, 3), tip_low=round(lo, 3),
        yaw_total=round(yaw_total, 1), yaw_end=round(curves[-1]["yaw"] - curves[0]["yaw"], 1),
        hips_travel=round(travel.length, 3), hips_travel_fwd=round(travel.dot(ref), 3),
        grip_spread=round(grip_avg, 3),
        foot_peak_speed=round(foot_peak, 4), hand_peak_speed=round(hand_peak, 4),
        foot_peak_frame=curves[foot_peak_i]["f"], foot_high=round(foot_high, 3), foot_fwd=round(foot_fwd, 3),
        highest_foot=round(lift, 3),
        frames_detail=[dict(f=c["f"], yaw=round(c["yaw"], 1),
                            hand=[round(v, 3) for v in c["hand"]],
                            tip=[round(v, 3) for v in c["tip"]],
                            tip_speed=round(tip_s[i], 4), foot_speed=round(foot_s[i], 4))
                       for i, c in enumerate(curves)],
    )


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    out_path, paths = args[0], args[1:]
    results = []
    for p in paths:
        try:
            r = analyse(p)
        except Exception as e:
            print("FAIL {}: {}".format(os.path.basename(p), e))
            continue
        results.append(r)
        print("{clip:42s} {pack:14s} {frames:3d}f {seconds:4.2f}s {shape:20s} "
              "peak {peak:3d}/{peak_tip_speed:.3f} core {core_start:3d}-{core_end:3d} contact {contact:5.1f} ({contact_frac:.2f}) "
              "lat {tip_lat_travel:5.2f} up {tip_up_delta:6.2f} fwd {tip_fwd_travel:5.2f} "
              "high {tip_high:5.2f} yaw {yaw_total:6.1f} travel {hips_travel:5.2f} "
              "grip {grip_spread:4.2f} foot {foot_peak_speed:.3f} "
              "{contact_source}".format(**r))
    with open(out_path, "w") as fh:
        json.dump(results, fh, indent=1)
    print("\nwrote {} ({} clips)".format(out_path, len(results)))


main()
