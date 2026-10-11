"""Build the Tripo animation master and author/export every clip.

Usage (headless):
  blender -b --python build_clips.py -- <rig.fbx> <base.blend> <master.blend> <export_dir> <render_dir> [clip ...]

- If <base.blend> is missing, imports the rig FBX, decimates the mesh to a light proxy and saves it.
- Opens the base, authors every clip (or only the named ones) as its own action on the armature,
  renders check frames with Workbench into <render_dir>, exports each clip as armature-only FBX
  into <export_dir>, and saves everything as <master.blend> (actions kept with fake users).

Bone-space rotation conventions on this Tripo rig (character faces +X, left side +Y):
  spine/neck/head : +X = lean back, -X = lean forward ; +Z = lean left, -Z = lean right ; +Y = turn left
  upper arms      : +X = swing forward ; L +Z / R -Z = raise outward (stance lowers them: L -55 / R +55)
  forearms        : +X = bend forward (hand comes up/forward) ; hands : +X = wrist flex
  thighs          : +X = leg forward ; calves : -X = knee bend ; feet : +X = toe up
  Root            : +Z = spin left (whole body, becomes baked root rotation in Unity)
Every key is an OFFSET from STANCE, never from the T-pose rest.

Key format: (frame, pose) or (frame, pose, (interpolation, easing)). The easing applies to the
segment that STARTS at that key: e.g. ("EXPO", "EASE_IN") on the windup key makes the move
accelerate into the strike; ("BACK", "EASE_OUT") on the hit key overshoots into follow-through.
"""
import bpy, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
RIG_FBX, BASE_BLEND, MASTER_BLEND, EXPORT_DIR, RENDER_DIR = args[:5]
ONLY = set(args[5:])
os.makedirs(os.path.dirname(BASE_BLEND), exist_ok=True)
os.makedirs(EXPORT_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)

FPS = 60
PROXY_RATIO = 0.03  # ~30k verts from ~1M

# ---------------------------------------------------------------- pose helpers
SP = ["Waist", "Spine01", "Spine02", "NeckTwist01", "Head"]

def spine(x=0, y=0, z=0, scale=(0.4, 1.0, 1.0, 0.8, 1.2)):
    """Distribute a lean/twist over the spine chain: Waist, Spine01, Spine02, Neck, Head."""
    return {b: (x * s, y * s, z * s) for b, s in zip(SP, scale)}

def head(x=0, y=0, z=0):
    return {"Head": (x, y, z)}

def add(*dicts):
    """Sum rotations bone-wise (offsets stack instead of replacing)."""
    out = {}
    for d in dicts:
        for b, r in d.items():
            o = out.get(b, (0, 0, 0))
            out[b] = (o[0] + r[0], o[1] + r[1], o[2] + r[2])
    return out

def arms(l=(0, 0, 0), r=(0, 0, 0), lf=0, rf=0):
    return {"L_Upperarm": l, "R_Upperarm": r, "L_Forearm": (lf, 0, 0), "R_Forearm": (rf, 0, 0)}

def hands(l=(0, 0, 0), r=(0, 0, 0)):
    return {"L_Hand": l, "R_Hand": r}

def legs(lt=0, rt=0, lc=0, rc=0, lf=0, rf=0):
    return {"L_Thigh": (lt, 0, 0), "R_Thigh": (rt, 0, 0), "L_Calf": (lc, 0, 0), "R_Calf": (rc, 0, 0),
            "L_Foot": (lf, 0, 0), "R_Foot": (rf, 0, 0)}

def pelvis(y=0, z=0, x=0):
    return {"Pelvis": (x, y, z)}

HIP = "__hip__"  # pseudo-bone: world-space offset (metres, +X forward, +Y left, +Z up) on the Hip bone

def hip(fwd=0.0, left=0.0, up=0.0):
    return {HIP: (fwd, left, up)}

def mirror(pose):
    """Left/right mirror of a pose: swap L_/R_ bones, negate lateral rotations and offsets."""
    out = {}
    for b, (x, y, z) in pose.items():
        if b == HIP:
            out[b] = (x, -y, z)
            continue
        nb = "R_" + b[2:] if b.startswith("L_") else "L_" + b[2:] if b.startswith("R_") else b
        if nb.endswith(("Upperarm", "Hand")) or b in SP or b == "Pelvis":
            out[nb] = (x, -y, -z)
        else:
            out[nb] = (x, y, z)
    return out

def mirror_keys(keys):
    return [(f, mirror(p)) + tuple(rest) for f, p, *rest in keys]

# Easing presets (interpolation, easing) for the segment starting at the key.
SETTLE = ("SINE", "EASE_OUT")       # fast start, soft landing: into a windup or hold
WIND   = ("EXPO", "EASE_IN")        # slow start, explosive finish: windup -> strike
THRU   = ("LINEAR", "AUTO")         # constant speed through the active frames
OVER   = ("BACK", "EASE_OUT")       # overshoot then settle: follow-through
EASE   = ("SINE", "EASE_IN_OUT")    # gentle both ends: recovery -> stance

# Neutral fighting stance: arms down and a little forward, elbows soft, slight lean, soft knees.
STANCE = add(
    spine(x=-3, scale=(0.5, 1, 1, 0.3, 0.2)),
    arms(l=(18, 0, -55), r=(18, 0, 55), lf=35, rf=35),
    legs(lt=4, rt=4, lc=-8, rc=-8),
)
REST = {}

CLIPS = {}

# ---------------------------------------------------------------- idle / walks
def idle_keys(length=120, step=6):
    k = []
    for f in range(0, length + 1, step):
        p = 2 * math.pi * f / length
        breathe = math.sin(2 * p)
        shift = math.sin(p)
        k.append((f, add(
            spine(x=1.8 * breathe, y=3 * shift, z=2 * shift, scale=(0.3, 1, 1, 0.5, 0.6)),
            head(0, -2 * shift, -1 * shift),
            pelvis(y=-2 * shift, z=2.5 * shift),
            hip(left=0.006 * shift, up=0.003 * breathe),
            arms(l=(3 * breathe, 0, -2 * breathe + 2 * shift), r=(3 * breathe, 0, 2 * breathe + 2 * shift),
                 lf=5 * breathe, rf=5 * breathe),
            legs(lt=1.5 * shift, rt=-1.5 * shift, lc=-2 * shift, rc=2 * shift),
        )))
    return k
CLIPS["Idle"] = {"length": 120, "loop": True, "keys": idle_keys()}

def walk_keys(length=44, step=2, direction=1):
    k = []
    for f in range(0, length + 1, step):
        t = f if direction > 0 else length - f
        p = 2 * math.pi * t / length
        pl, pr = p, p + math.pi
        thigh = lambda ph: 30 * math.sin(ph)
        calf = lambda ph: -(8 + 42 * (1 + math.cos(ph)) / 2)
        foot = lambda ph: -4 + 16 * math.sin(ph)
        swing = lambda ph: -16 * math.sin(ph)
        bend = lambda ph: 12 + 10 * (1 - math.sin(ph)) / 2
        bob = 0.007 * math.cos(2 * p)
        sway = -0.007 * math.cos(p)
        k.append((f, add(
            spine(x=-6, y=7 * math.sin(p), z=2 * math.cos(p), scale=(0.3, 1, 1, 0.4, 0.2)),
            head(2, -4 * math.sin(p), -1.5 * math.cos(p)),
            pelvis(y=-8 * math.sin(p), z=4 * math.cos(p)),
            hip(up=bob, left=sway),
            legs(lt=thigh(pl), rt=thigh(pr), lc=calf(pl), rc=calf(pr), lf=foot(pl), rf=foot(pr)),
            arms(l=(swing(pl), 0, 3), r=(swing(pr), 0, -3), lf=bend(pl), rf=bend(pr)),
        )))
    return k
CLIPS["Walk_Forward"] = {"length": 44, "loop": True, "keys": walk_keys()}
CLIPS["Walk_Back"] = {"length": 44, "loop": True, "keys": walk_keys(direction=-1)}

# ---------------------------------------------------------------- reactions / block
CLIPS["React_Front"] = {"length": 20, "keys": [
    (0, REST, WIND),
    (3, add(spine(x=16), arms(l=(-12, 0, 22), r=(-12, 0, -22), lf=25, rf=25)), OVER),
    (8, add(spine(x=10), arms(l=(-7, 0, 14), r=(-7, 0, -14), lf=16, rf=16)), EASE),
    (20, REST)]}
CLIPS["React_Left"] = {"length": 20, "keys": [
    (0, REST, WIND),
    (3, add(spine(x=4, y=-12, z=-15), arms(l=(-5, 0, 30), r=(5, 0, -5), lf=15)), OVER),
    (8, add(spine(x=2, y=-8, z=-10), arms(l=(-3, 0, 18), r=(3, 0, -3), lf=10)), EASE),
    (20, REST)]}
CLIPS["React_Right"] = {"length": 20, "keys": mirror_keys(CLIPS["React_Left"]["keys"])}
BLOCK = add(spine(x=-4, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(30, 0, 60), r=(30, 0, -60), lf=60, rf=60))
BREATH = add(spine(x=-1, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(2, 0, 1), r=(2, 0, -1), lf=2, rf=2))
CLIPS["Block_Hold"] = {"length": 30, "loop": True, "keys": [(0, BLOCK), (15, add(BLOCK, BREATH)), (30, BLOCK)]}
CLIPS["Block_Impact"] = {"length": 14, "keys": [
    (0, BLOCK, WIND),
    (2, add(BLOCK, spine(x=8, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(-12, 0, 0), r=(-12, 0, 0), lf=10, rf=10)), OVER),
    (14, BLOCK)]}
CLIPS["Stance_Idle"] = {"length": 2, "loop": True, "keys": [(0, REST), (2, REST)]}

# ---------------------------------------------------------------- dashes (athletic hops, in place)
# Sidestep to the LEFT: dip, drive off the right leg, fly with the knees tucked and the body
# leaning into the direction of travel, land on the left leg and absorb, gather.
CLIPS["Dash_Left"] = {"length": 20, "keys": [
    (0, REST, WIND),
    (2, add(spine(x=-6, z=-4), head(0, 0, 3), pelvis(z=-3), hip(up=-0.02, left=-0.01),
            legs(lt=8, rt=8, lc=-28, rc=-28), arms(l=(10, 0, -8), r=(10, 0, 8), lf=25, rf=25)), SETTLE),       # dip
    (5, add(spine(x=-8, z=12), head(0, 0, -6), pelvis(z=8), hip(up=0.012),
            legs(lt=22, rt=-12, lc=-52, rc=-4, lf=-10, rf=-22), arms(l=(-20, 0, 28), r=(32, 0, -8), lf=20, rf=42),
            hands(l=(-10, 0, 0), r=(15, 0, 0))), SETTLE),                                                      # push-off
    (8, add(spine(x=-6, z=15), head(0, 0, -8), pelvis(z=6), hip(up=0.036),
            legs(lt=36, rt=26, lc=-72, rc=-62, lf=-16, rf=-16), arms(l=(-8, 0, 46), r=(36, 0, -32), lf=26, rf=46)), WIND),  # apex
    (11, add(spine(x=-11, z=-6), head(0, 0, 4), pelvis(z=-6), hip(up=-0.03),
             legs(lt=16, rt=30, lc=-26, rc=-70, lf=10, rf=-10), arms(l=(16, 0, 24), r=(42, 0, -20), lf=32, rf=50)), OVER),   # land, absorb
    (14, add(spine(x=-5, z=-2), hip(up=-0.012), legs(lt=8, rt=10, lc=-18, rc=-24),
             arms(l=(10, 0, 5), r=(15, 0, -5), lf=20, rf=25)), EASE),                                             # gather
    (20, REST)]}
CLIPS["Dash_Right"] = {"length": 20, "keys": mirror_keys(CLIPS["Dash_Left"]["keys"])}

# Forward lunge-hop: dip back, drive forward with the left leg leading, land low, gather.
CLIPS["Dash_Forward"] = {"length": 20, "keys": [
    (0, REST, WIND),
    (2, add(spine(x=4), pelvis(x=2), hip(fwd=-0.015, up=-0.02), legs(lt=8, rt=8, lc=-28, rc=-28),
            arms(l=(-10, 0, -5), r=(-10, 0, 5), lf=25, rf=25)), SETTLE),
    (5, add(spine(x=-14, scale=(0.5, 1, 1, 0.5, 0.2)), head(6), pelvis(x=-4), hip(fwd=0.012, up=0.012),
            legs(lt=42, rt=-26, lc=-62, rc=-4, lf=-12, rf=-24), arms(l=(-30, 0, 2), r=(40, 0, -2), lf=20, rf=55)), SETTLE),
    (8, add(spine(x=-12, scale=(0.5, 1, 1, 0.5, 0.2)), head(5), hip(fwd=0.024, up=0.036),
            legs(lt=46, rt=10, lc=-72, rc=-42, lf=-14, rf=-14), arms(l=(-20, 0, 8), r=(30, 0, -8), lf=30, rf=50)), WIND),
    (11, add(spine(x=-16, scale=(0.5, 1, 1, 0.5, 0.2)), head(6), pelvis(x=-5), hip(fwd=0.03, up=-0.03),
             legs(lt=26, rt=16, lc=-46, rc=-42, lf=8, rf=-6), arms(l=(10, 0, 6), r=(20, 0, -6), lf=30, rf=35)), OVER),
    (14, add(spine(x=-8), hip(fwd=0.014, up=-0.012), legs(lt=12, rt=8, lc=-22, rc=-18),
             arms(l=(8, 0, 2), r=(10, 0, -2), lf=22, rf=25)), EASE),
    (20, REST)]}

# Backward hop: dip, push off the front (left) leg, fly leaning back, land on the right leg, gather.
CLIPS["Dash_Back"] = {"length": 20, "keys": [
    (0, REST, WIND),
    (2, add(spine(x=-6), hip(fwd=0.01, up=-0.02), legs(lt=12, rt=6, lc=-30, rc=-26),
            arms(l=(12, 0, -5), r=(12, 0, 5), lf=30, rf=30)), SETTLE),
    (5, add(spine(x=12, scale=(0.5, 1, 1, 0.5, 0.3)), head(-4), pelvis(x=4), hip(fwd=-0.012, up=0.012),
            legs(lt=24, rt=-30, lc=-10, rc=-46, lf=-20, rf=-8), arms(l=(35, 0, 6), r=(35, 0, -6), lf=45, rf=45)), SETTLE),
    (8, add(spine(x=10, scale=(0.5, 1, 1, 0.5, 0.3)), head(-3), hip(fwd=-0.024, up=0.036),
            legs(lt=30, rt=-6, lc=-66, rc=-56, lf=-14, rf=-14), arms(l=(26, 0, 12), r=(26, 0, -12), lf=40, rf=40)), WIND),
    (11, add(spine(x=6), pelvis(x=2), hip(fwd=-0.03, up=-0.03),
             legs(lt=12, rt=-10, lc=-22, rc=-56, lf=6, rf=4), arms(l=(16, 0, 8), r=(16, 0, -8), lf=32, rf=32)), OVER),
    (14, add(spine(x=-2), hip(fwd=-0.014, up=-0.012), legs(lt=8, rt=4, lc=-18, rc=-26),
             arms(l=(10, 0, 2), r=(10, 0, -2), lf=26, rf=26)), EASE),
    (20, REST)]}

# ---------------------------------------------------------------- strikes (authored to frame data)
# Structure: anticipation with a short hold (hips and shoulders wind against the swing, weight back),
# an explosive segment into the first active frame, constant speed through the active frames,
# overshoot into follow-through, then an eased recovery to the stance.

# Bjorn LIGHT: one-hand horizontal axe slash from his right (6 / 3 / 8 = 17 f)
CLIPS["Bjorn_Light_Slash"] = {"length": 17, "keys": [
    (0, REST, SETTLE),
    (3, add(spine(x=4, y=-22, z=-4), head(0, -6, 0), pelvis(y=-12, z=-2), hip(fwd=-0.02, left=-0.01, up=-0.005),
            arms(r=(-30, 0, -60), l=(30, 0, 10), rf=20, lf=42), hands(r=(-20, 0, 0)),
            legs(lt=4, rt=-2, lc=-14, rc=-12)), WIND),                                                        # coil right
    (6, add(spine(x=-6, y=8, z=2), head(0, 8, 0), pelvis(y=10, z=1), hip(fwd=0.025, left=0.004),
            arms(r=(60, 0, -60), l=(18, 0, 12), rf=5, lf=40), hands(r=(15, 0, 0)),
            legs(lt=14, rt=-8, lc=-10, rc=-6)), THRU),                                                        # hitbox arms
    (9, add(spine(x=-9, y=30, z=5), head(0, 10, 0), pelvis(y=16, z=2), hip(fwd=0.035, left=0.01),
            arms(r=(90, 0, -25), l=(6, 0, 18), rf=40, lf=34), hands(r=(25, 0, 0)),
            legs(lt=16, rt=-8, lc=-12, rc=-6)), OVER),                                                        # across the body
    (12, add(spine(x=-5, y=18, z=2), pelvis(y=9), hip(fwd=0.018),
             arms(r=(62, 0, -10), l=(10, 0, 10), rf=42, lf=36), legs(lt=9, rt=-5, lc=-10, rc=-8)), EASE),
    (17, REST)]}

# Bjorn MEDIUM: spinning axe slash, a full turn to the left with both axes out (10 / 5 / 14 = 29 f)
SPIN_ARMS = arms(l=(15, 0, 52), r=(15, 0, -52), lf=12, rf=12)
CROUCH = legs(lt=12, rt=12, lc=-30, rc=-30)
CLIPS["Bjorn_Medium_SpinningSlash"] = {"length": 29, "keys": [
    (0, REST, SETTLE),
    (4, add(spine(x=-12, y=-34, z=-4, scale=(0.5, 1, 1, 0.6, 0.4)), head(0, -8, 0), pelvis(y=-18, z=-3),
            hip(fwd=-0.02, left=-0.01, up=-0.02), arms(r=(-14, 0, -46), l=(62, 0, 22), rf=28, lf=52),
            hands(r=(-15, 0, 0)), CROUCH), WIND),                                                              # coil right, sink
    (10, add(spine(x=-8, y=12, scale=(0.5, 1, 1, 0.6, 0.4)), pelvis(y=6), hip(fwd=0.01, up=-0.016), SPIN_ARMS, CROUCH), THRU),  # hitbox arms
    (15, add(spine(x=-8, y=14, scale=(0.5, 1, 1, 0.6, 0.4)), pelvis(y=8), hip(fwd=0.012, up=-0.016), SPIN_ARMS, CROUCH), OVER), # end active
    (20, add(spine(x=-6, y=8), pelvis(y=4), hip(up=-0.012),
             arms(l=(22, 0, 25), r=(22, 0, -25), lf=22, rf=22), legs(lt=8, rt=8, lc=-18, rc=-18)), EASE),
    (29, REST)],
    "root_z": [(0, 0), (4, 0), (20, 360), (29, 360)]}

# Bjorn HEAVY: both axes raised high, a held beat at the top, then crashing straight down (15 / 5 / 15 = 35 f)
CLIPS["Bjorn_Heavy_OverheadDoubleAxe"] = {"length": 35, "keys": [
    (0, REST, SETTLE),
    (6, add(spine(x=10), head(6), pelvis(x=3), hip(fwd=-0.015, up=0.008),
            arms(r=(50, 0, -120), l=(50, 0, 120), rf=60, lf=60), hands(r=(-20, 0, 0), l=(-20, 0, 0)),
            legs(lt=-4, rt=6, lc=-6, rc=-10)), EASE),                                                         # raise
    (11, add(spine(x=16), head(8), pelvis(x=5), hip(fwd=-0.02, up=0.012),
             arms(r=(55, 0, -130), l=(55, 0, 130), rf=70, lf=70), hands(r=(-25, 0, 0), l=(-25, 0, 0)),
             legs(lt=-6, rt=8, lc=-4, rc=-8, lf=-8, rf=-8)), WIND),                                           # top: up on the toes, held
    (15, add(spine(x=-14), head(-8), pelvis(x=-5), hip(fwd=0.03, up=-0.015),
             arms(r=(90, 0, -75), l=(90, 0, 75), rf=15, lf=15), hands(r=(20, 0, 0), l=(20, 0, 0)),
             legs(lt=16, rt=-4, lc=-16, rc=-10)), THRU),                                                       # hitbox arms: crashing down
    (20, add(spine(x=-24), head(-10), pelvis(x=-8), hip(fwd=0.04, up=-0.03),
             arms(r=(75, 0, -25), l=(75, 0, 25), rf=5, lf=5), hands(r=(30, 0, 0), l=(30, 0, 0)),
             legs(lt=22, rt=-6, lc=-26, rc=-14)), OVER),                                                       # end active: buried low
    (26, add(spine(x=-14), head(-4), hip(fwd=0.025, up=-0.015),
             arms(r=(50, 0, -10), l=(50, 0, 10), rf=15, lf=15), legs(lt=12, rt=-4, lc=-14, rc=-10)), EASE),
    (35, REST)]}

# Miyamoto LIGHT: two-hand mid thrust, blade chambered at the hip then driven straight out with a lunge (7 / 3 / 10 = 20 f)
CLIPS["Miyamoto_Light_Thrust"] = {"length": 20, "keys": [
    (0, REST, SETTLE),
    (3, add(spine(x=4, y=-14), head(0, -4, 0), pelvis(y=-8), hip(fwd=-0.015, left=-0.004),
            arms(r=(-10, 0, -10), l=(20, 0, 25), rf=70, lf=60), hands(r=(-10, 0, 0)),
            legs(lt=-2, rt=6, lc=-10, rc=-14)), WIND),                                                        # chamber
    (7, add(spine(x=-10, y=12), head(12, 4, 0), pelvis(y=10), hip(fwd=0.035, left=0.004),
            arms(r=(75, 0, -45), l=(70, 0, 45), rf=5, lf=10), hands(r=(10, 0, 0)),
            legs(lt=18, rt=-10, lc=-12, rc=-4)), THRU),                                                       # hitbox arms, eyes on target
    (10, add(spine(x=-13, y=16), head(15, 5, 0), pelvis(y=13), hip(fwd=0.045),
             arms(r=(85, 0, -45), l=(80, 0, 45), rf=0, lf=5), legs(lt=22, rt=-12, lc=-16, rc=-2)), OVER),      # full extension
    (14, add(spine(x=-6, y=6), hip(fwd=0.02), arms(r=(45, 0, -30), l=(45, 0, 30), rf=30, lf=30),
             legs(lt=10, rt=-4, lc=-12, rc=-8)), EASE),                                                        # retract
    (20, REST)]}

# Miyamoto MEDIUM: two-hand horizontal side slash from his right (10 / 4 / 12 = 26 f)
CLIPS["Miyamoto_Medium_SideSlash"] = {"length": 26, "keys": [
    (0, REST, SETTLE),
    (5, add(spine(x=5, y=-32, z=-5), head(0, -8, 0), pelvis(y=-18, z=-3), hip(fwd=-0.02, left=-0.012, up=-0.005),
            arms(r=(10, 0, -70), l=(60, 0, 12), rf=30, lf=60), hands(r=(-15, 0, 0)),
            legs(lt=-6, rt=12, lc=-8, rc=-20)), WIND),                                                        # wound far right
    (10, add(spine(x=-7, y=-2), pelvis(y=4), hip(fwd=0.022), arms(r=(58, 0, -62), l=(58, 0, 32), rf=8, lf=30),
             hands(r=(10, 0, 0)), legs(lt=14, rt=-4, lc=-10, rc=-6)), THRU),                                  # hitbox arms
    (14, add(spine(x=-10, y=34, z=6), head(0, 10, 0), pelvis(y=22, z=3), hip(fwd=0.04, left=0.014),
             arms(r=(90, 0, -12), l=(28, 0, 64), rf=32, lf=18), hands(r=(25, 0, 0)),
             legs(lt=18, rt=-10, lc=-14, rc=-6)), OVER),                                                       # swept across
    (19, add(spine(x=-7, y=20, z=3), hip(fwd=0.026, left=0.008), arms(r=(62, 0, -10), l=(26, 0, 42), rf=34, lf=30),
             legs(lt=11, rt=-5, lc=-10, rc=-8)), EASE),
    (26, REST)]}

# Miyamoto HEAVY: two-hand overhead slash, high raise with a held beat, then down with a step (14 / 4 / 14 = 32 f)
CLIPS["Miyamoto_Heavy_Overhead"] = {"length": 32, "keys": [
    (0, REST, SETTLE),
    (6, add(spine(x=12), head(6), pelvis(x=3), hip(fwd=-0.015, up=0.006),
            arms(r=(56, 0, -126), l=(56, 0, 126), rf=70, lf=70), hands(r=(-20, 0, 0), l=(-20, 0, 0)),
            legs(lt=-4, rt=6, lc=-6, rc=-10)), EASE),                                                         # raise
    (10, add(spine(x=16), head(8), pelvis(x=5), hip(fwd=-0.02, up=0.01),
             arms(r=(60, 0, -132), l=(60, 0, 132), rf=75, lf=75), hands(r=(-25, 0, 0), l=(-25, 0, 0)),
             legs(lt=-6, rt=8, lc=-4, rc=-8, lf=-6, rf=-6)), WIND),                                           # top, held
    (14, add(spine(x=-12), head(-8), pelvis(x=-4), hip(fwd=0.03, up=-0.01),
             arms(r=(90, 0, -72), l=(90, 0, 72), rf=16, lf=16), hands(r=(20, 0, 0), l=(20, 0, 0)),
             legs(lt=16, rt=-4, lc=-14, rc=-10)), THRU),                                                       # hitbox arms
    (18, add(spine(x=-22), head(-10), pelvis(x=-7), hip(fwd=0.04, up=-0.025),
             arms(r=(74, 0, -22), l=(74, 0, 22), rf=4, lf=4), hands(r=(30, 0, 0), l=(30, 0, 0)),
             legs(lt=22, rt=-6, lc=-24, rc=-12)), OVER),                                                       # end active
    (24, add(spine(x=-13), head(-4), hip(fwd=0.024, up=-0.012),
             arms(r=(52, 0, -12), l=(52, 0, 12), rf=14, lf=14), legs(lt=12, rt=-4, lc=-13, rc=-9)), EASE),
    (32, REST)]}

# ---------------------------------------------------------------- weapon stances: idle / walk / block per character
# Offsets on top of STANCE. Bjorn: two axes held up in a crossed guard, low and wide. Miyamoto:
# two-handed chudan, blade forward at waist height, right foot leading, narrow and level.
# Arm numbers below were solved live in Blender with livepose.py (reach() for hand position, aim() for
# weapon direction) against the real meshes. Weapon directions in the rest pose, armature space:
#   Bjorn's axe handles continue the arm line (L (-0.02, 0.93, -0.37), R (-0.08, -0.93, -0.35));
#   Miyamoto's katana points straight UP from the right hand (0, -0.04, 1), i.e. along R_Hand local X.
# Upper arm Y twist and forearm Y twist (pronation) are what turn the blade; wrist X/Z finish the aim.
def limb(side, upper, fore, hand=(0, 0, 0)):
    return {side + "_Upperarm": upper, side + "_Forearm": fore, side + "_Hand": hand}

# Keep solved values inside Unity's Humanoid muscle ranges or the retarget clamps them: forearm twist <= 75,
# wrist flex <= 60, wrist deviation <= 30, upper-arm twist <= 40 (all degrees). The 2026-10-11 guards that used
# a 90 twist and 80 wrist came out as hanging arms in Unity.
GUARD_AXE = add(   # heavy low guard: elbows at the sides, forearms level, hands at belt height, axe heads forward
    spine(x=-6, scale=(0.5, 1, 1, 0.3, 0.1)), head(3),
    limb("L", (20, 0, 0), (0, -15, 0), (50, 0, -10)), limb("R", (20, 0, 0), (0, 15, 0), (50, 0, 10)),
    legs(lt=10, rt=10, lc=-24, rc=-24), pelvis(x=2), hip(up=-0.015),
)
GUARD_KATANA = add(   # low chudan: hands in front of the belt, forearms angled down, blade forward and up 35 deg
    spine(x=-3, y=-8, scale=(0.5, 1, 1, 0.3, 0.1)), head(0, 8, 0),
    # Elbows must stay bent >= 50 deg total: with a near-straight elbow Unity cannot split twist between the
    # upper arm and the forearm and flips between the two solutions every frame (the katana span in the idle).
    limb("R", (-10, -40, 30), (15, -60, 0), (-60, 0, -20)), limb("L", (-10, -20, -60), (15, 0, 0)),
    legs(lt=-3, rt=12, lc=-12, rc=-18, rf=-4), pelvis(y=-8, x=1), hip(up=-0.008, fwd=0.01),
)

def guard_idle_keys(guard, length=120, step=6, breathe_amt=1.5, sway_amt=1.2, arm_amt=2.0):
    k = []
    for f in range(0, length + 1, step):
        p = 2 * math.pi * f / length
        breathe = math.sin(2 * p)
        sway = math.sin(p)
        k.append((f, add(guard,
            spine(x=breathe_amt * breathe, z=sway_amt * sway, scale=(0.3, 1, 1, 0.4, 0.3)),
            hip(left=0.004 * sway, up=0.003 * breathe),
            arms(l=(arm_amt * breathe, 0, -arm_amt * 0.5 * breathe), r=(arm_amt * breathe, 0, arm_amt * 0.5 * breathe),
                 lf=1.5 * breathe, rf=1.5 * breathe),
            legs(lt=1.0 * sway, rt=-1.0 * sway),
        )))
    return k

def guard_walk_keys(guard, length=40, step=2, direction=1, stride=16, knee_extra=22, bob=0.004, foot=10,
                    arm_sway=3.0, twist=2.5, slide=False):
    """Fighting-stance walk: short steps, knees stay bent, guard stays up, body square to the opponent.
    slide=True gives the katana's suri-ashi: feet stay flat and the body does not bob."""
    k = []
    for f in range(0, length + 1, step):
        t = f if direction > 0 else length - f
        p = 2 * math.pi * t / length
        pl, pr = p, p + math.pi
        thigh = lambda ph: stride * math.sin(ph)
        calf = lambda ph: -(knee_extra * (1 + math.cos(ph)) / 2)
        ft = (lambda ph: 0.0) if slide else (lambda ph: -2 + foot * math.sin(ph))
        k.append((f, add(guard,
            spine(y=twist * math.sin(p), z=1.0 * math.cos(p), scale=(0.3, 1, 1, 0.3, 0.1)),
            pelvis(y=-twist * 1.2 * math.sin(p), z=2.0 * math.cos(p)),
            hip(up=(0.0 if slide else bob * math.cos(2 * p)), left=-0.004 * math.cos(p)),
            legs(lt=thigh(pl), rt=thigh(pr), lc=calf(pl), rc=calf(pr), lf=ft(pl), rf=ft(pr)),
            arms(l=(arm_sway * math.sin(p), 0, 0), r=(-arm_sway * math.sin(p), 0, 0),
                 lf=1.5 * math.sin(p), rf=-1.5 * math.sin(p)),
        )))
    return k

CLIPS["Bjorn_Idle"] = {"length": 120, "loop": True, "keys": guard_idle_keys(GUARD_AXE, breathe_amt=1.8, sway_amt=1.5, arm_amt=2.5)}
CLIPS["Bjorn_Walk_Forward"] = {"length": 40, "loop": True, "keys": guard_walk_keys(GUARD_AXE)}
CLIPS["Bjorn_Walk_Back"] = {"length": 40, "loop": True, "keys": guard_walk_keys(GUARD_AXE, direction=-1)}
CLIPS["Miyamoto_Idle"] = {"length": 120, "loop": True, "keys": guard_idle_keys(GUARD_KATANA, breathe_amt=1.2, sway_amt=0.8, arm_amt=1.2)}
CLIPS["Miyamoto_Walk_Forward"] = {"length": 36, "loop": True, "keys": guard_walk_keys(GUARD_KATANA, length=36, stride=11, knee_extra=14, bob=0.0, arm_sway=1.5, twist=1.5, slide=True)}
CLIPS["Miyamoto_Walk_Back"] = {"length": 36, "loop": True, "keys": guard_walk_keys(GUARD_KATANA, length=36, direction=-1, stride=11, knee_extra=14, bob=0.0, arm_sway=1.5, twist=1.5, slide=True)}

# Blocks (absolute poses, not built on the guards): the axes cross in front of the face, heads up and out;
# the katana stands near vertical in front of the chest with both hands on the hilt.
BLOCK_AXE = add(   # axes crossed in front of the face, heads up ~55 deg and out ~30 deg
    spine(x=-2, scale=(0.5, 1, 1, 0.3, 0.3)), head(-4),
    limb("L", (0, 20, -70), (20, 60, 0), (60, 0, 10)), limb("R", (0, -20, 70), (20, -60, 0), (60, 0, -10)),
    legs(lt=8, rt=8, lc=-16, rc=-16), hip(up=-0.008),
)
BLOCK_KATANA = add(   # blade raised across the chest, up ~60 deg (a true vertical needs a wrist beyond Humanoid range)
    spine(x=-2, y=-6, scale=(0.5, 1, 1, 0.3, 0.3)), head(-3, 6, 0),
    limb("R", (0, -40, 40), (15, -60, 0), (-60, 0, -30)), limb("L", (-10, -20, -50), (15, 0, 0)),
    legs(lt=-3, rt=12, lc=-10, rc=-14, rf=-4), pelvis(y=-8, x=1), hip(up=-0.006),
)
BREATH_SMALL = add(spine(x=-1, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(1.5, 0, 0.5), r=(1.5, 0, -0.5), lf=1.5, rf=1.5))
CLIPS["Bjorn_Block_Hold"] = {"length": 30, "loop": True, "keys": [(0, BLOCK_AXE), (15, add(BLOCK_AXE, BREATH_SMALL)), (30, BLOCK_AXE)]}
CLIPS["Bjorn_Block_Impact"] = {"length": 14, "keys": [
    (0, BLOCK_AXE, WIND),
    (2, add(BLOCK_AXE, spine(x=9, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(-14, 0, 0), r=(-14, 0, 0), lf=10, rf=10), hip(fwd=-0.02)), OVER),
    (14, BLOCK_AXE)]}
CLIPS["Miyamoto_Block_Hold"] = {"length": 30, "loop": True, "keys": [(0, BLOCK_KATANA), (15, add(BLOCK_KATANA, BREATH_SMALL)), (30, BLOCK_KATANA)]}
CLIPS["Miyamoto_Block_Impact"] = {"length": 14, "keys": [
    (0, BLOCK_KATANA, WIND),
    (2, add(BLOCK_KATANA, spine(x=8, scale=(0.5, 1, 1, 0.5, 0.3)), arms(l=(-12, 0, 0), r=(-12, 0, 0), lf=8, rf=8), hip(fwd=-0.018)), OVER),
    (14, BLOCK_KATANA)]}

if ONLY:
    CLIPS = {n: c for n, c in CLIPS.items() if n in ONLY}

# ---------------------------------------------------------------- base file
def build_base():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=RIG_FBX)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    arm.name = "TripoRig"
    mesh.name = "ProxyMesh"

    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    dec = mesh.modifiers.new("Decimate", 'DECIMATE')
    dec.ratio = PROXY_RATIO
    bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    print(f"proxy mesh: {len(mesh.data.vertices)} verts")
    mesh.select_set(False)

    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'
    sc.display.shading.single_color = (0.75, 0.70, 0.62)
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x = 540
    sc.render.resolution_y = 540
    sc.render.film_transparent = False

    bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0))
    bpy.context.active_object.name = "Ground"
    cam_data = bpy.data.cameras.new("CheckCam")
    cam = bpy.data.objects.new("CheckCam", cam_data)
    sc.collection.objects.link(cam)
    cam.location = Vector((1.55, -1.05, 0.55))
    target = Vector((0.0, 0.0, 0.36))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 50
    sc.camera = cam

    bpy.ops.wm.save_as_mainfile(filepath=BASE_BLEND)
    print("saved base:", BASE_BLEND)

if not os.path.exists(BASE_BLEND):
    build_base()

# A subset build works inside the existing master so the other actions survive.
source = MASTER_BLEND if (ONLY and os.path.exists(MASTER_BLEND)) else BASE_BLEND
bpy.ops.wm.open_mainfile(filepath=source)
sc = bpy.context.scene
arm = bpy.data.objects["TripoRig"]
sc.render.fps = FPS

# ---------------------------------------------------------------- authoring
def reset_pose():
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)

def action_fcurves(act):
    curves = []
    if hasattr(act, "layers"):  # Blender 4.4+ layered actions
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    curves.extend(cb.fcurves)
    else:
        curves = list(act.fcurves)
    return curves

def author(name, spec):
    arm.animation_data_clear()
    reset_pose()
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    keys = [(k[0], k[1], k[2] if len(k) > 2 else None) for k in spec["keys"]]
    touched = set(STANCE.keys())
    for _, pose, _ in keys:
        touched.update(pose.keys())
    touched.discard(HIP)
    hip_pb = arm.pose.bones["Hip"]
    hip_inv = (arm.matrix_world @ arm.data.bones["Hip"].matrix_local).to_3x3().inverted()
    for frame, pose, _ in keys:
        full = add(STANCE, pose)
        for b in touched:
            pb = arm.pose.bones.get(b)
            if pb is None:
                raise KeyError(f"{name}: bone {b} not in rig")
            rx, ry, rz = full.get(b, (0, 0, 0))
            pb.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
            pb.keyframe_insert("rotation_euler", frame=frame)
        fwd, left, up = full.get(HIP, (0, 0, 0))
        hip_pb.location = hip_inv @ Vector((fwd, left, up))
        hip_pb.keyframe_insert("location", frame=frame)
    for frame, deg in spec.get("root_z", []):
        pb = arm.pose.bones["Root"]
        pb.rotation_euler = (0, 0, math.radians(deg))
        pb.keyframe_insert("rotation_euler", frame=frame)

    act = arm.animation_data.action
    act.name = name
    act.use_fake_user = True

    # Per-key easing on every curve; root spin stays linear so the turn rate is even.
    ease_at = {f: e for f, _, e in keys if e}
    for fc in action_fcurves(act):
        is_root = fc.data_path.startswith('pose.bones["Root"]')
        for kp in fc.keyframe_points:
            if is_root:
                kp.interpolation = 'LINEAR'
                continue
            e = ease_at.get(int(round(kp.co.x)))
            if e:
                kp.interpolation, kp.easing = e
    sc.frame_start = 0
    sc.frame_end = spec["length"]
    return act

def render_checks(name, spec):
    frames = sorted({k[0] for k in spec["keys"]})
    if spec.get("loop") and len(frames) > 5:
        frames = frames[::max(1, len(frames) // 4)]
    for f in frames:
        sc.frame_set(f)
        sc.render.filepath = os.path.join(RENDER_DIR, f"{name}_f{f:02d}.png")
        bpy.ops.render.render(write_still=True)

REST_FRAME = -10  # frame holding the T-pose for export; outside the baked 0..length range

def key_rest_pose(act, frame):
    """Key every animated channel at its rest value on `frame`, so the exported node defaults are the T-pose."""
    for fc in action_fcurves(act):
        if "rotation_euler" in fc.data_path or "location" in fc.data_path:
            fc.keyframe_points.insert(frame, 0.0, options={'FAST'})
        elif "scale" in fc.data_path:
            fc.keyframe_points.insert(frame, 1.0, options={'FAST'})
        fc.update()

def unkey_frame(act, frame):
    for fc in action_fcurves(act):
        for kp in list(fc.keyframe_points):
            if abs(kp.co.x - frame) < 0.01:
                fc.keyframe_points.remove(kp)
                break
        fc.update()

def export(name, spec):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT_DIR, f"{name}.fbx")
    # Blender's FBX exporter writes each bone's node defaults from the CURRENT pose, and Unity builds a Humanoid
    # avatar for an armature-only file from those defaults. A guard pose there (folded, twisted arms) is not a
    # valid T-pose, the avatar fails and the clip plays as a T-pose. So export from a rest-pose frame.
    act = arm.animation_data.action
    key_rest_pose(act, REST_FRAME)
    sc.frame_set(REST_FRAME)
    bpy.context.view_layer.update()
    # The clip importers copy the Bjorn model's avatar, and Unity matches it by transform path. The Tripo model
    # files name the armature node "Armature", so the exported clip must too, or the copy is rejected
    # ("Rig Error: Invalid copied Avatar Rig Configuration") and the clip imports with no animation at all.
    arm.name = "Armature"
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE'},
        add_leaf_bones=False, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', path_mode='AUTO',
    )
    arm.name = "TripoRig"
    unkey_frame(act, REST_FRAME)
    sc.frame_set(0)
    print(f"exported {path} ({os.path.getsize(path)//1024} KB)")

for name, spec in CLIPS.items():
    author(name, spec)
    render_checks(name, spec)
    export(name, spec)

arm.animation_data_clear()
reset_pose()
bpy.ops.wm.save_as_mainfile(filepath=MASTER_BLEND)
print("saved master:", MASTER_BLEND, "actions:", [a.name for a in bpy.data.actions])
print("BUILD-OK")
