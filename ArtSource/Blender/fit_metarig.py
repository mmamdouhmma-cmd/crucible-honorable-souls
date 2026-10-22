"""Fit a Rigify human metarig to a character body by CONSTRUCTION, the way Salaadin was rigged.

    blender -b --python fit_metarig.py -- <Name> <Body.fbx> <out.blend>

Salaadin's rig is the one that works, and the reason is in rigcompare's table: his joints sit on
human proportions and his left and right sides are IDENTICAL. The other three were not built that
way - Tripo interpolated Bjorn's and Miyamoto's, and Richard came off Mixamo with his hips eight
points of height too low. This builds each of them the shape Salaadin has: a Rigify metarig, face
bones removed, every joint placed by measurement, both sides mirrored so asymmetry is zero.

It does NOT invent the skeleton from nothing. The existing rig is a good anchor for what a mesh
cannot tell you on its own - where a shoulder sits inside a fur mantle, where a knuckle is inside
a closed fist - so each metarig bone starts from its counterpart on the old rig, and only then are
the faults the audit found corrected against human targets:

    hips to 52% of body height     (Richard measured 43.8)
    ankles to 4%                   (Richard 7.8, Bjorn and Miyamoto 2.0)
    hips over the ankles           (Richard's sat 2.15% of height forward - the same geometry
                                    that gave Salaadin the splits)
    knee break to ~164 deg         (Bjorn 176.6, Miyamoto 173.6: a knee that straight gives the
                                    retargeter no hint which way it folds)
    elbow break to ~164 deg        (Bjorn and Miyamoto both 143 - bent far too sharply)
    knee 50% along hip->ankle, elbow 52% along shoulder->wrist
    left mirrored onto right, so asym is 0.0 and not 3.7

The right hand gets a full finger chain whether or not the old rig had one. Bjorn's and Miyamoto's
right hands are still Tripo mitts, which is why Unity only ever filled 15 of 30 finger slots.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
NAME, BODY_FBX, OUT = args[0], args[1], args[2]

# Human targets. Salaadin's hand-built rig is the reference for every one of these.
HIP_UP      = 0.520
ANKLE_UP    = 0.040
KNEE_ALONG  = 0.500
ELBOW_ALONG = 0.520
KNEE_ANGLE  = 164.0
ELBOW_ANGLE = 164.0

# How each source rig spells the joints. Tripo and Mixamo disagree on all of it.
SRC = {
 "Mixamo": {
   "hips": "mixamorig:Hips", "spine": "mixamorig:Spine", "spine1": "mixamorig:Spine1",
   "spine2": "mixamorig:Spine2", "neck": "mixamorig:Neck", "head": "mixamorig:Head",
   "headtop": "mixamorig:HeadTop_End",
   "clav": "mixamorig:{S}Shoulder", "arm": "mixamorig:{S}Arm", "fore": "mixamorig:{S}ForeArm",
   "hand": "mixamorig:{S}Hand", "thigh": "mixamorig:{S}UpLeg", "shin": "mixamorig:{S}Leg",
   "foot": "mixamorig:{S}Foot", "toe": "mixamorig:{S}ToeBase",
   "finger": "mixamorig:{S}Hand{F}{i}",
 },
 "Tripo": {
   "hips": "Pelvis", "spine": "Waist", "spine1": "Spine01", "spine2": "Spine02",
   "neck": "NeckTwist01", "head": "Head", "headtop": None,
   "clav": "{s}_Clavicle", "arm": "{s}_Upperarm", "fore": "{s}_Forearm",
   "hand": "{s}_Hand", "thigh": "{s}_Thigh", "shin": "{s}_Calf",
   "foot": "{s}_Foot", "toe": "{s}_ToeBase",
   "finger": "{s}_{F}{i}",
 },
}
FINGERS = ("Index", "Middle", "Ring", "Pinky", "Thumb")

# ---------------------------------------------------------------------------------------------
# 1. load the body and read the old rig
# ---------------------------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BODY_FBX)
src = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
# The BODY is the mesh actually skinned to the armature; the weapons are separate objects riding
# a bone, and must survive into the export untouched because Unity mounts them by name.
skinned = [o for o in meshes if any(m.type == 'ARMATURE' for m in o.modifiers)]
body = max(skinned or meshes, key=lambda o: len(o.data.vertices))
weapons = [o for o in meshes if o is not body]
convention = "Mixamo" if any(b.name.startswith("mixamorig:") for b in src.data.bones) else "Tripo"
S = SRC[convention]
print("%s: source rig %d bones (%s), body %s %d verts, weapons %s"
      % (NAME, len(src.data.bones), convention, body.name, len(body.data.vertices),
         [o.name for o in weapons]))


def W(v):
    return src.matrix_world @ v


def _name(key, side):
    n = S.get(key)
    if n is None:
        return None
    return n.format(S=("Left" if side == "L" else "Right"), s=side, F="", i="") if side else n


def src_head(key, side=None):
    n = _name(key, side)
    if n is None:
        return None
    b = src.data.bones.get(n)
    return W(b.head_local) if b else None


def src_tail(key, side=None):
    n = _name(key, side)
    if n is None:
        return None
    b = src.data.bones.get(n)
    return W(b.tail_local) if b else None


def src_finger(side, finger, i):
    n = S["finger"].format(S=("Left" if side == "L" else "Right"), s=side, F=finger, i=i)
    b = src.data.bones.get(n)
    return W(b.head_local) if b else None


zs = [(body.matrix_world @ v.co).z for v in body.data.vertices]
FLOOR, TOP = min(zs), max(zs)
H = TOP - FLOOR
print("  body spans z %.3f..%.3f  (height %.3f)" % (FLOOR, TOP, H))

# which way is forward? the toes lead the ankle
_ank = (src_head("foot", "L") + src_head("foot", "R")) / 2
_toe = (src_head("toe", "L") + src_head("toe", "R")) / 2
FWD = Vector((_toe.x - _ank.x, _toe.y - _ank.y, 0.0))
FWD.normalize()
RIGHT = Vector((FWD.y, -FWD.x, 0.0))
MID = _ank.copy()
print("  forward %s" % (tuple(round(v, 3) for v in FWD),))


def mirror(p):
    """reflect a point across the character's own mid-sagittal plane"""
    return p - RIGHT * (2 * (p - MID).dot(RIGHT))


def avg_sides(key):
    """a joint averaged across both sides, returned on the LEFT - this is where the asymmetry goes"""
    l, r = src_head(key, "L"), src_head(key, "R")
    if l is None or r is None:
        return (l if l is not None else r).copy()
    return (l + mirror(r)) / 2


# ---------------------------------------------------------------------------------------------
# 2. the corrected joints, left side only - the right is mirrored from it
# ---------------------------------------------------------------------------------------------
J = {}

ankle = avg_sides("foot")
ankle.z = FLOOR + ANKLE_UP * H
hip = avg_sides("thigh")
hip.z = FLOOR + HIP_UP * H
hip -= FWD * (hip - ankle).dot(FWD)          # hip directly over the ankle: the splits fix
leg = ankle - hip
knee = hip + leg * KNEE_ALONG
knee = knee + FWD * (leg.length * KNEE_ALONG * math.tan(math.radians(180.0 - KNEE_ANGLE) / 2.0))
J["thigh"], J["shin"], J["foot"] = hip, knee, ankle

toe = avg_sides("toe")
toe.z = FLOOR + 0.25 * (ankle.z - FLOOR)
J["toe"] = toe
_te = src_tail("toe", "L")
J["toe_end"] = Vector((_te.x, _te.y, toe.z)) if _te else toe + FWD * (toe - ankle).length * 0.6
_heel = ankle - FWD * (toe - ankle).length * 0.45
J["heel"] = Vector((_heel.x, _heel.y, FLOOR))

clav = avg_sides("clav")
shoulder = avg_sides("arm")
wrist = avg_sides("hand")
armv = wrist - shoulder
elbow = shoulder + armv * ELBOW_ALONG
elbow = elbow - FWD * (armv.length * ELBOW_ALONG * math.tan(math.radians(180.0 - ELBOW_ANGLE) / 2.0))
J["clav"], J["arm"], J["fore"], J["hand"] = clav, shoulder, elbow, wrist

# spine: keep the old rig's proportions, re-seat on the corrected hips, straighten the lean
spine_keys = ["hips", "spine", "spine1", "spine2", "neck", "head"]
raw = [src_head(k) for k in spine_keys]
if raw[0] is None:
    raise SystemExit("no hips bone on the source rig")
shift = Vector((0.0, 0.0, hip.z - raw[0].z))
chain = [p + shift for p in raw]
lean = (chain[0] - ankle).dot(FWD)
chain = [p - FWD * lean for p in chain]
for k, p in zip(spine_keys, chain):
    J[k] = p
J["headtop"] = Vector((chain[-1].x, chain[-1].y, max(TOP, chain[-1].z + 0.05 * H)))

# fingers: take whichever side HAS them, so a mitt right hand inherits the left's chain
FJ = {}
for finger in FINGERS:
    pts = []
    for i in (1, 2, 3):
        p = src_finger("L", finger, i)
        if p is None:
            q = src_finger("R", finger, i)
            p = mirror(q) if q is not None else None
        pts.append(p)
    FJ[finger] = pts
have = [f for f in FINGERS if all(p is not None for p in FJ[f])]
print("  fingers available from the source rig: %s" % (have or "NONE",))
if len(have) < len(FINGERS):
    palm_dir = (wrist - elbow).normalized()
    span = (wrist - elbow).length * 0.55
    built = []
    for n, finger in enumerate(FINGERS):
        if finger in have:
            continue
        base = wrist + palm_dir * span * 0.35 + RIGHT * ((n - 1.5) * span * 0.18)
        FJ[finger] = [base + palm_dir * span * t for t in (0.0, 0.35, 0.62)]
        built.append(finger)
    print("  built %s by construction off the wrist" % built)

# ---------------------------------------------------------------------------------------------
# 3. the metarig
# ---------------------------------------------------------------------------------------------
import addon_utils
addon_utils.enable("rigify", default_set=False, persistent=True)
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.object.armature_human_metarig_add()
meta = bpy.context.object
meta.name = meta.data.name = NAME + "_Meta"

bpy.ops.object.mode_set(mode='EDIT')
eb = meta.data.edit_bones

# Salaadin's rig is 67 bones: the metarig's 159 less the whole face group. Unity maps none of
# them, nothing in the project animates them, and every extra bone is another chance for the
# auto-mapper to fill a slot with the wrong thing.
doomed = set()
stack = [eb["face"]] if "face" in eb else []
while stack:
    b = stack.pop()
    doomed.add(b)
    stack += list(b.children)
for nm in list(eb.keys()):
    if nm.startswith(("teeth", "tongue", "eye.", "ear.", "nose", "lip", "brow", "cheek",
                      "chin", "jaw", "forehead", "temple", "lid.")):
        doomed.add(eb[nm])
for b in doomed:
    eb.remove(b)
print("  metarig: removed %d face bones, %d left" % (len(doomed), len(eb)))


def place(name, head, tail=None):
    b = eb.get(name)
    if b is None:
        print("    (no bone %s)" % name)
        return
    b.head = head
    if tail is not None:
        b.tail = tail


place("spine",     J["hips"],   J["spine"])
place("spine.001", J["spine"],  J["spine1"])
place("spine.002", J["spine1"], J["spine2"])
place("spine.003", J["spine2"], J["neck"])
# The metarig has SEVEN spine bones against Unity's six humanoid slots. Split the neck here and
# let the converter dissolve spine.005, exactly as it does on Salaadin.
_neck_mid = J["neck"].lerp(J["head"], 0.5)
place("spine.004", J["neck"],   _neck_mid)
place("spine.005", _neck_mid,   J["head"])
place("spine.006", J["head"],   J["headtop"])

META_OF = {"Index": "palm.01", "Middle": "palm.02", "Ring": "palm.03", "Pinky": "palm.04"}
KEY_OF = {"Index": "f_index", "Middle": "f_middle", "Ring": "f_ring",
          "Pinky": "f_pinky", "Thumb": "thumb"}

for side, sgn in (("L", +1.0), ("R", -1.0)):
    m = (lambda p: p.copy()) if side == "L" else mirror
    place("shoulder.%s" % side,  m(J["clav"]), m(J["arm"]))
    place("upper_arm.%s" % side, m(J["arm"]),  m(J["fore"]))
    place("forearm.%s" % side,   m(J["fore"]), m(J["hand"]))
    place("hand.%s" % side,      m(J["hand"]), m(FJ["Middle"][0]))
    place("thigh.%s" % side,     m(J["thigh"]), m(J["shin"]))
    place("shin.%s" % side,      m(J["shin"]),  m(J["foot"]))
    place("foot.%s" % side,      m(J["foot"]),  m(J["toe"]))
    place("toe.%s" % side,       m(J["toe"]),   m(J["toe_end"]))
    place("heel.02.%s" % side,   m(J["heel"]) - RIGHT * sgn * 0.02 * H,
                                 m(J["heel"]) + RIGHT * sgn * 0.02 * H)
    for finger in FINGERS:
        pts = [m(p) for p in FJ[finger]]
        if finger in META_OF:
            place("%s.%s" % (META_OF[finger], side), m(J["hand"]), pts[0])
        k = KEY_OF[finger]
        seg = pts[2] - pts[1]
        place("%s.01.%s" % (k, side), pts[0], pts[1])
        place("%s.02.%s" % (k, side), pts[1], pts[2])
        place("%s.03.%s" % (k, side), pts[2], pts[2] + seg * 0.8)
    # the two cosmetic pairs Salaadin's rig carries, parked where they cannot confuse the mapper
    place("breast.%s" % side, m(J["spine2"]) + RIGHT * sgn * 0.06 * H,
                              m(J["spine2"]) + RIGHT * sgn * 0.06 * H + FWD * 0.06 * H)
    place("pelvis.%s" % side, m(J["hips"]),
                              m(J["hips"]) + RIGHT * sgn * 0.05 * H + FWD * 0.05 * H)

bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()

# ---------------------------------------------------------------------------------------------
# 4. skin the body to it, and hand the weapons over
# ---------------------------------------------------------------------------------------------
# Clearing `parent` does NOT preserve a world transform - Blender keeps matrix_basis and drops
# the parent's contribution, so a body parented to an armature the FBX importer scaled lands at a
# different size. Richard's came out 42.75x and the converter then reported his ankle at 50% of
# body height. Capture each world matrix and put it back.
_mw_body = body.matrix_world.copy()
_mw_weap = {o.name: o.matrix_world.copy() for o in weapons}
for o in weapons:
    o.parent = None
    o.matrix_world = _mw_weap[o.name]
# Read the weights BEFORE the groups are deleted - doing it after left every vert unweighted
# on all three fighters, reported as a clean run.
def new_group_for(old_name):
    """which metarig bone inherits an old bone's weights, or None to drop it"""
    n = old_name.split(":")[-1]
    low = n.lower()
    # twist bones have no counterpart; their weight belongs to the limb they twist about
    low = low.replace("twist01", "").replace("twist02", "").replace("twist", "")
    side = None
    if low.startswith("l_") or low.startswith("left"):
        side = "L"
    elif low.startswith("r_") or low.startswith("right"):
        side = "R"
    stem = low
    for pre in ("l_", "r_", "left", "right"):
        if stem.startswith(pre):
            stem = stem[len(pre):]
            break
    SPINE = {"pelvis": "spine", "hip": "spine", "hips": "spine", "waist": "spine.001",
             "spine": "spine.001", "spine01": "spine.002", "spine1": "spine.002",
             "spine02": "spine.003", "spine2": "spine.003", "spine03": "spine.003",
             "neck": "spine.004", "neck01": "spine.004", "neck02": "spine.005",
             "head": "spine.006", "root": None, "headtop_end": None}
    if side is None and stem in SPINE:
        return SPINE[stem]
    if side is None:
        return None
    LIMB = {"clavicle": "shoulder", "shoulder": "shoulder",
            "upperarm": "upper_arm", "arm": "upper_arm",
            "forearm": "forearm", "lowerarm": "forearm",
            "hand": "hand", "thigh": "thigh", "upleg": "thigh",
            "calf": "shin", "leg": "shin", "shin": "shin",
            "foot": "foot", "ankle": "foot", "toebase": "toe", "toe": "toe",
            "toe_end": None, "heel": "heel.02"}
    if stem in LIMB:
        return None if LIMB[stem] is None else "%s.%s" % (LIMB[stem], side)
    # fingers: <finger><joint>, e.g. index2, handindex2
    for finger, key in (("index", "f_index"), ("middle", "f_middle"), ("ring", "f_ring"),
                        ("pinky", "f_pinky"), ("little", "f_pinky"), ("thumb", "thumb")):
        if finger in stem:
            digits = "".join(c for c in stem if c.isdigit())
            if not digits:
                continue
            j = int(digits[-1])
            if j >= 4:          # a Mixamo *4 is a leaf tip, it carries no weight worth keeping
                return None
            return "%s.0%d.%s" % (key, j, side)
    return None

old_groups = {g.index: g.name for g in body.vertex_groups}
old_weights = {}           # vert index -> {new group name: weight}
dropped = {}
for v in body.data.vertices:
    acc = {}
    for g in v.groups:
        if g.weight <= 1e-5:
            continue
        tgt = new_group_for(old_groups[g.group])
        if tgt is None:
            dropped[old_groups[g.group]] = dropped.get(old_groups[g.group], 0) + 1
            continue
        acc[tgt] = acc.get(tgt, 0.0) + g.weight
    if acc:
        old_weights[v.index] = acc
unmapped = sorted(dropped)
print("  weight transfer: %d/%d verts carry weights; old groups dropped: %s"
      % (len(old_weights), len(body.data.vertices), unmapped or "none"))

for mod in list(body.modifiers):
    if mod.type == 'ARMATURE':
        body.modifiers.remove(mod)
for g in list(body.vertex_groups):
    body.vertex_groups.remove(g)
body.parent = None
body.matrix_world = _mw_body
bpy.context.view_layer.update()
_h = (max((body.matrix_world @ v.co).z for v in body.data.vertices)
      - min((body.matrix_world @ v.co).z for v in body.data.vertices))
assert abs(_h - H) < 1e-3 * H,     "body was %.4f tall and is %.4f after unparenting - the metarig was fitted to the old size" % (H, _h)

# TRANSFER the old weights, do not rebuild them. Bone-heat weighting fails outright on these
# costumed meshes - it returned "failed to find solution" on Miyamoto and left all 119,952 of his
# verts unweighted - and even where it succeeds it throws away work that was done deliberately:
# Tripo's costume weights, and the left-hand finger chains add_fingers.py built by hand. So every
# old vertex group is poured into the metarig bone that replaces it, which is also the project's
# standing rule for weights (redistribute, never rebuild).

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
meta.select_set(True)
bpy.context.view_layer.objects.active = meta
bpy.ops.object.parent_set(type='ARMATURE_NAME')     # parent + modifier, empty groups, no heat solve

for b in meta.data.bones:
    if b.name not in body.vertex_groups:
        body.vertex_groups.new(name=b.name)
for vi, acc in old_weights.items():
    tot = sum(acc.values())
    for name, w in acc.items():
        body.vertex_groups[name].add([vi], w / tot if tot > 1e-6 else w, 'REPLACE')

orphan = [v.index for v in body.data.vertices if sum(g.weight for g in v.groups) <= 1e-5]
print("  transferred; unweighted verts %d / %d" % (len(orphan), len(body.data.vertices)))

# Bones the old rig had nothing for - the right-hand fingers on a Tripo mitt - take their share of
# the PARENT's weights by proximity, each vert keeping the total weight it already had. This is
# what add_fingers.py did for the left hands, and the reason the right hand could sit exactly on a
# haft and still read as holding air.
import mathutils
for side in ("L", "R"):
    hand_g = body.vertex_groups.get("hand.%s" % side)
    if hand_g is None:
        continue
    finger_bones = [b for b in meta.data.bones
                    if b.name.endswith(".%s" % side)
                    and b.name.split(".")[0] in ("f_index", "f_middle", "f_ring", "f_pinky", "thumb")]
    if not finger_bones:
        continue
    if any(any(g.group == body.vertex_groups[b.name].index and g.weight > 1e-4 for g in v.groups)
           for b in finger_bones for v in body.data.vertices[:0]):
        pass
    has = sum(1 for v in body.data.vertices
              for g in v.groups
              if g.weight > 1e-4 and old_groups.get(g.group) is None)
    # does any finger group on this side already carry weight?
    carried = False
    for b in finger_bones:
        gi = body.vertex_groups[b.name].index
        if any(any(g.group == gi and g.weight > 1e-4 for g in v.groups) for v in body.data.vertices):
            carried = True
            break
    if carried:
        continue
    seg = [(b, meta.matrix_world @ b.head_local, meta.matrix_world @ b.tail_local)
           for b in finger_bones]
    moved = 0
    for v in body.data.vertices:
        w = next((g.weight for g in v.groups if g.group == hand_g.index), 0.0)
        if w <= 1e-4:
            continue
        p = body.matrix_world @ v.co
        def d(e):
            a, bb = e[1], e[2]
            ab = bb - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
            return (p - (a + ab * t)).length
        near = sorted(seg, key=d)[:2]
        if d(near[0]) > (seg[0][2] - seg[0][1]).length * 2.5:
            continue                       # too far from any finger - this is palm or wrist
        d0, d1 = max(d(near[0]), 1e-6), max(d(near[1]), 1e-6)
        s0, s1 = (1 / d0), (1 / d1)
        hand_g.add([v.index], w * 0.25, 'REPLACE')
        body.vertex_groups[near[0][0].name].add([v.index], w * 0.75 * s0 / (s0 + s1), 'REPLACE')
        body.vertex_groups[near[1][0].name].add([v.index], w * 0.75 * s1 / (s0 + s1), 'REPLACE')
        moved += 1
    if moved:
        print("  %s hand: %d verts split off the mitt onto %d finger bones"
              % (side, moved, len(finger_bones)))

# Hand the weapons to the new hand bones WITHOUT moving them. A weapon's placement relative to
# the body in the bind pose is correct already - it was authored that way - and the only thing the
# re-rig changes underneath it is which bone carries it. Computing a parent inverse from the new
# bone's frame instead THROWS THAT AWAY: it re-seats the weapon on the bone's own axes, and since
# every rig's hand bone points a different way, Miyamoto's katana came out rotated through his
# chest (nearest blade vertex 0.038 from his torso axis, blade reaching z -1.11 behind him) and
# his off hand followed it onto the grip, which is what read as handcuffed.
for o in weapons:
    bone = "hand.R" if o.name.endswith("_R") else "hand.L"
    if bone not in meta.data.bones:
        continue
    keep = o.matrix_world.copy()
    o.parent = meta
    o.parent_type = 'BONE'
    o.parent_bone = bone
    bpy.context.view_layer.update()
    o.matrix_world = keep               # solves matrix_basis against the bone, weapon does not move
    bpy.context.view_layer.update()
    moved = (o.matrix_world.translation - keep.translation).length
    assert moved < 1e-4, "%s moved %.5f when re-parented" % (o.name, moved)
    print("  %s -> %s (held in place)" % (o.name, bone))

bpy.data.objects.remove(src, do_unlink=True)

# ---------------------------------------------------------------------------------------------
# 5. report, against the same numbers rigcompare prints
# ---------------------------------------------------------------------------------------------
def h(n):
    return meta.matrix_world @ meta.data.bones[n].head_local


def ang(a, b, c):
    return 180 - math.degrees((h(b) - h(a)).angle(h(c) - h(b)))


def along(a, b, c):
    A, B, C = h(a), h(b), h(c)
    return 100 * (B - A).length / max((C - A).length, 1e-9)


print("\n%s fitted metarig: %d bones" % (NAME, len(meta.data.bones)))
for side in ("L", "R"):
    print("  %s: knee %5.1f%% ang %6.1f   elbow %5.1f%% ang %6.1f"
          % (side,
             along("thigh.%s" % side, "shin.%s" % side, "foot.%s" % side),
             ang("thigh.%s" % side, "shin.%s" % side, "foot.%s" % side),
             along("upper_arm.%s" % side, "forearm.%s" % side, "hand.%s" % side),
             ang("upper_arm.%s" % side, "forearm.%s" % side, "hand.%s" % side)))
hipc = (h("thigh.L") + h("thigh.R")) / 2
ankc = (h("foot.L") + h("foot.R")) / 2
print("  hip %4.1f%% up, ankle %4.1f%% up, hip fore/aft of ankle %4.2f%%"
      % (100 * (hipc.z - FLOOR) / H, 100 * (ankc.z - FLOOR) / H,
         100 * abs((hipc - ankc).dot(FWD)) / H))
asym = max((h(n + ".L") - mirror(h(n + ".R"))).length
           for n in ("thigh", "shin", "foot", "upper_arm", "forearm", "hand"))
print("  asymmetry %.6f world units (%.3f%% of height)" % (asym, 100 * asym / H))

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("saved:", OUT)
print("FIT-OK")
