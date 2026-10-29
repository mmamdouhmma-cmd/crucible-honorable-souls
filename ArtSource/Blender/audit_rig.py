"""Audit one HD-rigged character: skeleton, weights, deformation poses, weapon (story cast, 2026-10-29).

    blender -b <Name>_HD_rigged.blend --python audit_rig.py -- <Name> <template_body.fbx> <out_dir> [--renders 1]

READ-ONLY: the .blend is never saved. Writes <out_dir>/audit.json and, with --renders 1, one
three-quarter render per test pose (front and back) into <out_dir>/poses/.

What it measures (all distances as % of the character's height H):
  inventory   objects, parents, transforms (negative scale), materials and their images,
              armature bone set and hierarchy against the shared 79-bone template.
  skeleton    joint heights, limb ratios, knee/elbow break and bend DIRECTION, left/right mirror
              error per bone, foot splay against the facing, sole contact, 6-ray containment of every
              bone's head/mid/tail, finger chains (count, segment lengths, bend-axis consistency).
  weights     influences per vertex (Unity skins with 4 by default), unnormalised sums, vertices
              bound to a bone they do not sit near, deform bones with no vertices.
  poses       14 test poses built by aiming bones in world space (no clips), each scored by edge
              stretch against the rest mesh (worst bones listed) and by weapon vertices buried in
              the body away from the hands; the two-handed poses also report the off hand's miss
              on the haft.
  weapon      parent bone, local transform, grip point / tip / edge expressed in the HAND BONE's
              frame, so a story rig can be compared with the play-verified roster Miyamoto.
"""
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
NAME, TEMPLATE, OUT = argv[:3]
opts = {"--renders": "1"}
for i in range(3, len(argv), 2):
    opts[argv[i]] = argv[i + 1]
os.makedirs(os.path.join(OUT, "poses"), exist_ok=True)
MX = "mixamorig:"
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
R = {"name": NAME}


def r(x, n=2):
    return round(float(x), n)


# --- find the pieces --------------------------------------------------------------------------------
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
skinned = [o for o in bpy.data.objects if o.type == 'MESH'
           and any(m.type == 'ARMATURE' for m in o.modifiers)]
body = max(skinned, key=lambda o: len(o.data.vertices))
weapons = [o for o in bpy.data.objects if o.type == 'MESH' and o.parent == rig and o.parent_type == 'BONE']
rig.data.pose_position = 'POSE'
for pb in rig.pose.bones:                      # audit from the rest pose whatever the file was saved in
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()


def wpos(n, at="head"):
    pb = rig.pose.bones[n]
    return rig.matrix_world @ (pb.head if at == "head" else pb.tail)


def bdir(n):
    pb = rig.pose.bones[n]
    return ((rig.matrix_world @ pb.matrix).to_3x3() @ Vector((0, 1, 0))).normalized()


def blen(n):
    return (wpos(n, "tail") - wpos(n)).length


def rest_verts():
    M = body.matrix_world
    return np.array([tuple(M @ v.co) for v in body.data.vertices])


V0 = rest_verts()
H = V0[:, 2].max() - V0[:, 2].min()
FLOOR = V0[:, 2].min()
R["height"] = r(H, 4)

# facing: the toes sit in front of the heels
fwd = Vector((0, 0, 0))
for s in ("Left", "Right"):
    d = wpos(MX + s + "ToeBase") - wpos(MX + s + "Foot")
    d.z = 0
    fwd += d.normalized()
fwd.z = 0
fwd.normalize()
UP = Vector((0, 0, 1))
LEFT = UP.cross(fwd).normalized() * -1          # character's left
if (wpos(MX + "LeftArm") - wpos(MX + "RightArm")).dot(LEFT) < 0:
    LEFT = -LEFT
R["facing"] = [r(c, 3) for c in fwd]


# =================================================================================================
# 1. inventory
# =================================================================================================
def images_of(mat):
    out = []
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                out.append(os.path.basename(n.image.filepath) or n.image.name)
    return out


inv = []
for o in bpy.data.objects:
    m = o.matrix_world
    inv.append({
        "name": o.name, "type": o.type,
        "parent": o.parent.name if o.parent else None,
        "parent_type": o.parent_type, "parent_bone": o.parent_bone or None,
        "scale": [r(c, 4) for c in o.scale],
        "negative_scale": m.to_3x3().determinant() < 0,
        "materials": [{"name": s.material.name if s.material else None,
                       "images": images_of(s.material)} for s in o.material_slots] if o.type == 'MESH' else [],
        "verts": len(o.data.vertices) if o.type == 'MESH' else None,
    })
R["objects"] = inv
R["body"] = body.name
R["weapons"] = [w.name for w in weapons]

# template hierarchy
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=TEMPLATE)
new = [o for o in bpy.data.objects if o not in before]
trig = next(o for o in new if o.type == 'ARMATURE')
tpl = {b.name: (b.parent.name if b.parent else None) for b in trig.data.bones}
tpl_roll = {b.name: (trig.matrix_world.to_3x3() @ b.matrix_local.to_3x3()) for b in trig.data.bones}
for o in new:
    bpy.data.objects.remove(o, do_unlink=True)
mine = {b.name: (b.parent.name if b.parent else None) for b in rig.data.bones}
roll_dev = {}
for n in mine:
    if n in tpl_roll:
        a = (rig.matrix_world.to_3x3() @ rig.data.bones[n].matrix_local.to_3x3())
        b = tpl_roll[n]
        # roll: angle between the local Z axes once the bone axes (Y) are aligned
        ya, yb = a.col[1].normalized(), b.col[1].normalized()
        q = yb.rotation_difference(ya).to_matrix()
        roll_dev[n] = r(math.degrees((q @ b.col[2]).angle(a.col[2])), 1)
R["hierarchy"] = {
    "bones": len(mine), "template_bones": len(tpl),
    "missing": sorted(set(tpl) - set(mine)), "extra": sorted(set(mine) - set(tpl)),
    "parent_mismatch": sorted(n for n in mine if n in tpl and mine[n] != tpl[n]),
    "duplicates_dotted": sorted(n for n in mine if "." in n),
    "armature_scale": [r(c, 4) for c in rig.scale],
    "roll_vs_template_max_deg": max(roll_dev.values()) if roll_dev else None,
    "roll_vs_template_worst": sorted(roll_dev.items(), key=lambda kv: -kv[1])[:6],
}
mods = [m for m in body.modifiers if m.type == 'ARMATURE']
R["bind"] = {
    "armature_modifiers": len(mods),
    "modifier_object": mods[0].object.name if mods and mods[0].object else None,
    "body_parent": body.parent.name if body.parent else None,
    "body_transform_identity": all(abs(body.matrix_world[i][j] - (1 if i == j else 0)) < 1e-4
                                   for i in range(4) for j in range(4)),
    "vertex_groups_not_bones": sorted(g.name for g in body.vertex_groups if g.name not in mine),
}

# =================================================================================================
# 2. skeleton
# =================================================================================================
def zf(n, at="head"):
    return r((wpos(MX + n, at).z - FLOOR) / H * 100, 1)


def hinge(a, b, c, outward):
    """Break angle at b (180 = straight) and whether b bows along `outward`."""
    A, B, C = wpos(MX + a), wpos(MX + b), wpos(MX + c)
    ang = math.degrees((A - B).angle(C - B))
    mid = (A + C) / 2
    off = B - mid
    return {"angle": r(ang, 1), "bow_pct_H": r(off.length / H * 100, 2),
            "bows_correct_way": off.dot(outward) > 0}


sk = {"heights_pct_H": {
    "hips": zf("Hips"), "upleg": r((zf("LeftUpLeg") + zf("RightUpLeg")) / 2, 1),
    "knee": r((zf("LeftLeg") + zf("RightLeg")) / 2, 1), "ankle": r((zf("LeftFoot") + zf("RightFoot")) / 2, 1),
    "spine": zf("Spine"), "chest": zf("Spine2"), "neck": zf("Neck"), "head": zf("Head"),
    "shoulder": r((zf("LeftArm") + zf("RightArm")) / 2, 1),
}}
for s in ("Left", "Right"):
    sk[s] = {
        "upperarm_pct_H": r(blen(MX + s + "Arm") / H * 100, 2),
        "forearm_pct_H": r(blen(MX + s + "ForeArm") / H * 100, 2),
        "upper_to_fore": r(blen(MX + s + "Arm") / blen(MX + s + "ForeArm"), 3),
        "thigh_pct_H": r(blen(MX + s + "UpLeg") / H * 100, 2),
        "shin_pct_H": r(blen(MX + s + "Leg") / H * 100, 2),
        "thigh_to_shin": r(blen(MX + s + "UpLeg") / blen(MX + s + "Leg"), 3),
        "knee": hinge(s + "UpLeg", s + "Leg", s + "Foot", fwd),
        "elbow": hinge(s + "Arm", s + "ForeArm", s + "Hand", -fwd),
        "foot_splay_deg": r(math.degrees(math.atan2(
            (wpos(MX + s + "ToeBase") - wpos(MX + s + "Foot")).dot(LEFT),
            (wpos(MX + s + "ToeBase") - wpos(MX + s + "Foot")).dot(fwd))), 1),
        "toe_end_above_floor_pct_H": r((wpos(MX + s + "ToeBase", "tail").z - FLOOR) / H * 100, 2),
        "hip_width_pct_H": None,
    }
sk["hip_joint_separation_pct_H"] = r((wpos(MX + "LeftUpLeg") - wpos(MX + "RightUpLeg")).length / H * 100, 2)
sk["ankle_separation_pct_H"] = r((wpos(MX + "LeftFoot") - wpos(MX + "RightFoot")).length / H * 100, 2)
sk["hip_over_ankle_fwd_pct_H"] = {s: r((wpos(MX + s + "UpLeg") - wpos(MX + s + "Foot")).dot(fwd) / H * 100, 2)
                                   for s in ("Left", "Right")}
sk["spine_lean_deg"] = r(math.degrees(math.atan2((wpos(MX + "Neck") - wpos(MX + "Hips")).dot(fwd),
                                                 (wpos(MX + "Neck") - wpos(MX + "Hips")).z)), 1)

# mirror error: reflect every Left bone across the body's mid-plane and compare with its Right twin
mid_x = (wpos(MX + "Hips")).dot(LEFT)
mir = {}
for b in rig.data.bones:
    if MX + "Left" in b.name:
        o = b.name.replace("Left", "Right")
        if o not in rig.data.bones:
            continue
        e = 0
        for at in ("head", "tail"):
            p = wpos(b.name, at)
            q = wpos(o, at)
            pl = p.dot(LEFT) - mid_x
            p_m = p - LEFT * (2 * pl)
            e = max(e, (p_m - q).length)
        mir[b.name.replace(MX, "")] = r(e / H * 100, 2)
sk["mirror_error_pct_H"] = {"max": max(mir.values()), "worst": sorted(mir.items(), key=lambda kv: -kv[1])[:8],
                            "over_1pct": sum(1 for v in mir.values() if v > 1.0)}

# containment (6 axis rays, majority vote) on the rest mesh
me = body.data
co0 = [body.matrix_world @ v.co for v in me.vertices]
BVH0 = BVHTree.FromPolygons(co0, [tuple(p.vertices) for p in me.polygons])


def inside(bvh, p):
    votes = 0
    for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)),
              Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
        n, q = 0, p.copy()
        for _ in range(200):
            hit = bvh.ray_cast(q, d)
            if hit[0] is None:
                break
            n += 1
            q = hit[0] + d * 1e-6
        votes += n % 2
    return votes


def is_leaf(n):
    return n.endswith(("_End", "4", "_end")) or "Toe_End" in n or n.endswith("HeadTop_End")


out = []
for b in rig.data.bones:
    if is_leaf(b.name):
        continue
    for at, p in (("head", wpos(b.name)), ("mid", (wpos(b.name) + wpos(b.name, "tail")) / 2),
                  ("tail", wpos(b.name, "tail"))):
        v = inside(BVH0, p)
        if v < 4:
            d = (BVH0.find_nearest(p)[0] - p).length
            out.append({"bone": b.name.replace(MX, ""), "at": at, "votes": v, "dist_pct_H": r(d / H * 100, 2)})
sk["outside_mesh"] = out
# open boundary edges make ray parity unreliable - count them
from collections import Counter
ec = Counter()
for p in me.polygons:
    vs = list(p.vertices)
    for i in range(len(vs)):
        ec[tuple(sorted((vs[i], vs[(i + 1) % len(vs)])))] += 1
sk["mesh_boundary_edges"] = sum(1 for c in ec.values() if c == 1)
sk["mesh_nonmanifold_edges"] = sum(1 for c in ec.values() if c > 2)

# fingers
fing = {}
for s in ("Left", "Right"):
    hx = (rig.matrix_world.to_3x3() @ rig.data.bones[MX + s + "Hand"].matrix_local.to_3x3()).col[0]
    for f in FINGERS:
        segs = [MX + f"{s}Hand{f}{i}" for i in (1, 2, 3)]
        if not all(n in rig.data.bones for n in segs):
            fing[s + f] = "MISSING"
            continue
        L = [blen(n) / H * 100 for n in segs]
        xs = [(rig.matrix_world.to_3x3() @ rig.data.bones[n].matrix_local.to_3x3()).col[0] for n in segs]
        fing[s + f] = {
            "seg_pct_H": [r(x, 2) for x in L],
            "bend_axis_spread_deg": r(max(math.degrees(xs[i].angle(xs[j])) for i in range(3) for j in range(3)), 1),
            "bend_axis_vs_hand_deg": r(math.degrees(xs[0].angle(hx)), 1),
            "tip_inside_votes": inside(BVH0, wpos(segs[2], "tail")),
        }
sk["fingers"] = fing
R["skeleton"] = sk

# =================================================================================================
# 3. weights
# =================================================================================================
gname = {g.index: g.name for g in body.vertex_groups}
deform = {b.name for b in rig.data.bones if b.use_deform}
seg = {n: (wpos(n), wpos(n, "tail")) for n in deform}


def dseg(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


n_inf, sums, dom = [], [], []
count_by_bone = Counter()
for v in me.vertices:
    ws = [(gname[g.group], g.weight) for g in v.groups if g.weight > 1e-4 and gname[g.group] in deform]
    n_inf.append(len(ws))
    sums.append(sum(w for _, w in ws))
    if ws:
        b = max(ws, key=lambda t: t[1])[0]
        dom.append(b)
        count_by_bone[b] += 1
    else:
        dom.append(None)
n_inf = np.array(n_inf)
far = []
for i, v in enumerate(me.vertices):
    if dom[i]:
        d = dseg(co0[i], *seg[dom[i]])
        if d > 0.15 * H:
            far.append((dom[i], d))
fc = Counter(b for b, _ in far)
R["weights"] = {
    "verts": len(me.vertices),
    "zero_weight": int((n_inf == 0).sum()),
    "max_influences": int(n_inf.max()),
    "over_4_influences": int((n_inf > 4).sum()),
    "unnormalised_(sum_off_by_>1%)": int(sum(1 for s in sums if s > 0 and abs(s - 1) > 0.01)),
    "dominant_bone_over_15pct_H_away": len(far),
    "far_by_bone": fc.most_common(8),
    "verts_on_finger_bones": sum(c for b, c in count_by_bone.items() if any(f in b for f in FINGERS)),
    "deform_bones_without_verts": sorted(b.replace(MX, "") for b in deform
                                         if count_by_bone[b] == 0 and not is_leaf(b)
                                         and not any(f in b for f in FINGERS)),
    "verts_by_bone_top": [(b.replace(MX, ""), c) for b, c in count_by_bone.most_common(12)],
}

# =================================================================================================
# 4. weapon, in the hand bone's frame
# =================================================================================================
def blade_frame(V):
    """(tsuba point, unit axis toward the grip end, unit curve (edge-side) direction, length, grip end)."""
    c = V.mean(0)
    w, vec = np.linalg.eigh(np.cov((V - c).T))
    ax = vec[:, np.argmax(w)]
    t = (V - c) @ ax
    perp = (V - c) - np.outer(t, ax)
    bins = np.linspace(t.min(), t.max(), 81)
    width = np.zeros(80)
    cen = np.zeros((80, 3))
    for i in range(80):
        m = (t >= bins[i]) & (t < bins[i + 1])
        if m.sum() > 3:
            p = perp[m]
            cen[i] = p.mean(0)
            width[i] = np.linalg.norm(p - cen[i], axis=1).max()
    lo, hi = 12, 68
    ti = lo + int(np.argmax(width[lo:hi]))
    tt = (bins[ti] + bins[ti + 1]) / 2
    grip_low = (tt - t.min()) < (t.max() - tt)
    to_grip = -ax if grip_low else ax
    tsuba = c + ax * tt + cen[ti]
    tip_t = t.max() if grip_low else t.min()
    end_t = t.min() if grip_low else t.max()
    mid_t = (tt + tip_t) / 2
    mi = int(np.clip(np.searchsorted(bins, mid_t) - 1, 0, 79))
    ti2 = 2 if not grip_low else 77
    line_mid = (cen[ti] + cen[ti2]) / 2
    bow = cen[mi] - line_mid
    bow = bow - ax * (bow @ ax)
    n = np.linalg.norm(bow)
    bow = bow / n if n > 1e-9 else np.zeros(3)
    tip = c + ax * tip_t
    gend = c + ax * end_t
    return {"tsuba": tsuba, "to_grip": to_grip, "bow": bow, "bow_mag": n, "length": t.max() - t.min(),
            "tip": tip, "grip_end": gend, "tsuba_width": width[ti], "grip_len": abs(end_t - tt)}


def hand_frame(s):
    return rig.matrix_world @ rig.pose.bones[MX + s + "Hand"].matrix


def wverts(w):
    M = w.matrix_world
    return np.array([tuple(M @ v.co) for v in w.data.vertices])


def hand_vert_idx(s):
    names = {MX + s + "Hand"} | {MX + f"{s}Hand{f}{i}" for f in FINGERS for i in (1, 2, 3, 4)}
    return [i for i, b in enumerate(dom) if b in names]


wep = {}
for w in weapons:
    Wv = wverts(w)
    F = blade_frame(Wv)
    side = "Right" if "Right" in w.parent_bone else "Left"
    Hm = hand_frame(side)
    Hi = Hm.inverted()
    loc = lambda p: Hi @ Vector(tuple(p))
    vec = lambda d: (Hi.to_3x3() @ Vector(tuple(d))).normalized()
    hv = np.array([tuple(co0[i]) for i in hand_vert_idx(side)])
    palm_c = hv.mean(0)
    # grip axis distance from the palm centroid and from the wrist (hand bone origin)
    a = Vector(tuple(F["tsuba"]))
    u = Vector(tuple(F["to_grip"]))

    def axis_dist(p):
        p = Vector(tuple(p))
        return ((p - a) - u * (p - a).dot(u)).length
    # fist around the haft: hand verts within a ring around the grip segment
    t = (hv - np.array(a)) @ np.array(u)
    on_grip = (t > -0.01 * H) & (t < F["grip_len"] + 0.01 * H)
    radial = np.linalg.norm((hv - np.array(a)) - np.outer(t, np.array(u)), axis=1)
    # angular coverage of the fist round the haft (360 = closed)
    ref = Vector(tuple(F["bow"])) if F["bow_mag"] > 0 else u.orthogonal()
    ref2 = u.cross(ref)
    angs = []
    for p, ok, rr in zip(hv, on_grip, radial):
        if ok and rr < 0.06 * H:
            d = Vector(tuple(p)) - a
            angs.append(math.degrees(math.atan2(d.dot(ref2), d.dot(ref))) % 360)
    cover = len(set(int(x // 15) for x in angs)) * 15
    # weapon verts buried in the body away from the hands
    hand_all = np.array([tuple(co0[i]) for i in hand_vert_idx("Left") + hand_vert_idx("Right")])
    wep[w.name] = {
        "parent_bone": w.parent_bone, "side": side,
        "local_location": [r(c, 4) for c in w.location], "local_rotation_deg": [r(math.degrees(c), 1) for c in w.rotation_euler],
        "scale": [r(c, 4) for c in w.scale], "negative_scale": w.matrix_world.to_3x3().determinant() < 0,
        "length_pct_H": r(F["length"] / H * 100, 1), "grip_len_pct_H": r(F["grip_len"] / H * 100, 1),
        "curve_bow_pct_len": r(F["bow_mag"] / F["length"] * 100, 2),
        "in_hand_frame": {
            "tsuba": [r(c, 3) for c in (loc(F["tsuba"]) / H * 100)],
            "tip_dir": [r(c, 3) for c in vec(F["tip"] - F["tsuba"])],
            "edge_dir": [r(c, 3) for c in vec(F["bow"])] if F["bow_mag"] > 0 else None,
        },
        "grip_axis_to_palm_centroid_pct_H": r(axis_dist(palm_c) / H * 100, 2),
        "grip_axis_to_wrist_pct_H": r(axis_dist(Hm.translation) / H * 100, 2),
        "fist_verts_on_grip": int((on_grip & (radial < 0.06 * H)).sum()),
        "fist_wrap_deg": cover,
        "nearest_hand_vert_to_axis_pct_H": r(radial[on_grip].min() / H * 100, 2) if on_grip.any() else None,
        "_frame": F,
    }
R["weapon"] = {k: {kk: vv for kk, vv in v.items() if kk != "_frame"} for k, v in wep.items()}

# =================================================================================================
# 5. test poses
# =================================================================================================
def rot_about_head(n, R3):
    pb = rig.pose.bones[n]
    Mw = rig.matrix_world @ pb.matrix
    h = Mw.translation
    Mw = Matrix.Translation(h) @ R3.to_4x4() @ Matrix.Translation(-h) @ Mw
    pb.matrix = rig.matrix_world.inverted() @ Mw
    bpy.context.view_layer.update()


def aim(n, d):
    cur = bdir(n)
    rot_about_head(n, cur.rotation_difference(d.normalized()).to_matrix())


def two_bone(upper, lower, target, pole):
    S = wpos(MX + upper)
    a, b = blen(MX + upper), blen(MX + lower)
    d = target - S
    dist = min(d.length, (a + b) * 0.999)
    miss = max(0.0, d.length - (a + b))
    dn = d.normalized()
    cosA = (a * a + dist * dist - b * b) / (2 * a * dist)
    sinA = math.sqrt(max(0.0, 1 - cosA * cosA))
    p = pole - dn * pole.dot(dn)
    p = p.normalized() if p.length > 1e-6 else dn.orthogonal()
    E = S + dn * a * cosA + p * a * sinA
    aim(MX + upper, E - S)
    aim(MX + lower, (S + dn * dist) - wpos(MX + lower))
    return miss


def reset():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def lower_hips(dz, lean_deg=0.0):
    """Drop the pelvis by dz, keep both ankles where they were, knees forward."""
    ank = {s: wpos(MX + s + "Foot") for s in ("Left", "Right")}
    fdir = {s: bdir(MX + s + "Foot") for s in ("Left", "Right")}
    pb = rig.pose.bones[MX + "Hips"]
    Mw = rig.matrix_world @ pb.matrix
    Mw = Matrix.Translation(Vector((0, 0, -dz))) @ Mw
    pb.matrix = rig.matrix_world.inverted() @ Mw
    bpy.context.view_layer.update()
    if lean_deg:
        rot_about_head(MX + "Spine", Matrix.Rotation(math.radians(lean_deg), 3, LEFT * -1))
    for s, sg in (("Left", 1), ("Right", -1)):
        two_bone(s + "UpLeg", s + "Leg", ank[s], fwd + LEFT * sg * 0.25)
        aim(MX + s + "Foot", fdir[s])


def arm_dir(s, d_upper, d_fore=None):
    aim(MX + s + "Arm", d_upper)
    aim(MX + s + "ForeArm", d_fore if d_fore is not None else d_upper)


def side_vec(s):
    return LEFT if s == "Left" else -LEFT


def chest():
    return wpos(MX + "Spine2")


def weapon_tip_dir(w):
    F = wep[w.name]["_frame"]
    # the frame was measured at rest: carry it through the hand's pose change
    return None


W = weapons[0] if weapons else None
if W:
    side_w = wep[W.name]["side"]
    F0 = wep[W.name]["_frame"]
    Hrest_inv = hand_frame(side_w).inverted()
    tip_local = Hrest_inv @ Vector(tuple(F0["tip"]))
    tsuba_local = Hrest_inv @ Vector(tuple(F0["tsuba"]))
    gend_local = Hrest_inv @ Vector(tuple(F0["grip_end"]))


def weapon_points():
    Hm = hand_frame(side_w)
    return Hm @ tsuba_local, Hm @ tip_local, Hm @ gend_local


def point_weapon(d):
    """Turn the weapon hand so the blade points along d (minimal rotation about the wrist)."""
    ts, tp, _ = weapon_points()
    cur = (tp - ts).normalized()
    rot_about_head(MX + side_w + "Hand", cur.rotation_difference(d.normalized()).to_matrix())


def off_hand_on_haft():
    """Off hand's wrist onto the haft at the pommel end, the palm's own offset allowed for."""
    off = "Left" if side_w == "Right" else "Right"
    ts, tp, ge = weapon_points()
    u = (ge - ts).normalized()
    # second fist sits a fist below the first: aim the wrist a palm's depth off the haft
    target = ts + (ge - ts) * 0.8 + (wpos(MX + off + "Arm") - ts).normalized() * 0.03 * H
    miss = two_bone(off + "Arm", off + "ForeArm", target, -fwd + side_vec(off) * 0.5 - UP * 0.5)
    aim(MX + off + "Hand", bdir(MX + side_w + "Hand"))     # the two fists stacked parallel on the haft
    return miss, (wpos(MX + off + "Hand") - target).length


def two_hand(rh_target, tip_d):
    two_bone(side_w + "Arm", side_w + "ForeArm", rh_target, -fwd + side_vec(side_w) * 0.6 - UP * 0.4)
    point_weapon(tip_d)
    miss, err = off_hand_on_haft()
    return {"off_hand_reach_short_pct_H": r(miss / H * 100, 2), "off_wrist_err_pct_H": r(err / H * 100, 2)}


def P_tpose():
    return {}


def P_apose():
    for s in ("Left", "Right"):
        arm_dir(s, side_vec(s) - UP)
    return {}


def P_stance():
    lower_hips(0.05 * H, 6)
    hip = wpos(MX + "Hips")
    t = hip + fwd * 0.20 * H + UP * 0.02 * H - side_vec(side_w) * 0.0
    return two_hand(t, fwd + UP * 0.55)


def P_shoulder_up():
    for s in ("Left", "Right"):
        arm_dir(s, UP + side_vec(s) * 0.25)
    return {}


def P_shoulder_cross():
    for s in ("Left", "Right"):
        arm_dir(s, fwd - side_vec(s) * 0.7 + UP * 0.1)
    return {}


def P_elbow():
    for s in ("Left", "Right"):
        arm_dir(s, -UP + side_vec(s) * 0.3 + fwd * 0.1, UP * 0.9 + fwd * 0.45)
    return {}


def P_wrist():
    for s in ("Left", "Right"):
        arm_dir(s, side_vec(s) - UP * 0.4 + fwd * 0.3)
        ax = bdir(MX + s + "ForeArm")
        rot_about_head(MX + s + "Hand", Matrix.Rotation(math.radians(90), 3, ax))
        flex = ax.cross(UP).normalized()
        rot_about_head(MX + s + "Hand", Matrix.Rotation(math.radians(55), 3, flex))
    return {}


def P_fingers():
    moved = 0
    for s in ("Left", "Right"):
        for f in FINGERS:
            for i in (1, 2, 3):
                n = MX + f"{s}Hand{f}{i}"
                if n in rig.pose.bones:
                    x = (rig.matrix_world @ rig.pose.bones[n].matrix).to_3x3().col[0].normalized()
                    rot_about_head(n, Matrix.Rotation(math.radians(50), 3, x))
    return {}


def P_two_hand():
    hip = wpos(MX + "Hips")
    return two_hand(hip + fwd * 0.22 * H + UP * 0.05 * H, fwd + UP * 0.3)


def P_squat():
    lower_hips(0.22 * H, 22)
    for s in ("Left", "Right"):
        arm_dir(s, fwd + side_vec(s) * 0.3 - UP * 0.2)
    return {}


def P_hip():
    two_bone("LeftUpLeg", "LeftLeg", wpos(MX + "LeftUpLeg") + fwd * 0.18 * H - UP * 0.2 * H, fwd)
    aim(MX + "RightUpLeg", -UP + side_vec("Right") * 0.75)
    aim(MX + "RightLeg", -UP + side_vec("Right") * 0.75)
    return {}


def P_twist():
    for n in ("Spine", "Spine1", "Spine2"):
        rot_about_head(MX + n, Matrix.Rotation(math.radians(20), 3, UP))
    for s in ("Left", "Right"):
        arm_dir(s, side_vec(s) - UP * 0.6)
    return {}


def P_overhead():
    head = wpos(MX + "Head", "tail")
    return two_hand(head + UP * 0.06 * H + fwd * 0.04 * H, UP * 0.6 - fwd * 0.8)


def P_block():
    head = wpos(MX + "Head")
    return two_hand(head + fwd * 0.20 * H - side_vec(side_w) * 0.0 + UP * 0.02 * H + side_vec(side_w) * 0.08 * H,
                    -side_vec(side_w) + UP * 0.25 + fwd * 0.2)


POSES = [("01_tpose", P_tpose), ("02_apose", P_apose), ("03_combat_stance", P_stance),
         ("04a_shoulders_up", P_shoulder_up), ("04b_shoulders_cross", P_shoulder_cross),
         ("05_elbow_flex", P_elbow), ("06_wrist_rotate", P_wrist), ("07_08_finger_curl", P_fingers),
         ("09_two_hand_grip", P_two_hand), ("10_deep_knee_bend", P_squat), ("11_hip_rotation", P_hip),
         ("12_torso_twist", P_twist), ("13_overhead_strike", P_overhead), ("14_block", P_block)]

edges = np.array([tuple(e.vertices) for e in me.edges])
L0 = np.linalg.norm(V0[edges[:, 0]] - V0[edges[:, 1]], axis=1)
L0 = np.maximum(L0, 1e-9)
dom_arr = np.array([d.replace(MX, "") if d else "-" for d in dom])
hand_set = set(hand_vert_idx("Left") + hand_vert_idx("Right"))


def posed_verts():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    m = ev.to_mesh()
    M = body.matrix_world
    P = np.array([tuple(M @ v.co) for v in m.vertices])
    polys = [tuple(p.vertices) for p in m.polygons]
    ev.to_mesh_clear()
    return P, polys


def score(P, polys):
    Lp = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
    ratio = Lp / L0
    bad = (ratio > 1.6) | (ratio < 0.45)
    bb = Counter(dom_arr[edges[bad, 0]])
    s = {"edge_stretch_p99": r(np.percentile(ratio, 99), 2), "edge_stretch_max": r(ratio.max(), 2),
         "edge_squash_p1": r(np.percentile(ratio, 1), 2), "edges_extreme": int(bad.sum()),
         "extreme_by_bone": bb.most_common(5),
         "moved_verts_vs_rest": int((np.linalg.norm(P - V0, axis=1) > 1e-5).sum())}
    if W:
        bvh = BVHTree.FromPolygons([Vector(tuple(p)) for p in P], polys)
        Wv = wverts(W)
        Ph = P[list(hand_set)]
        buried = 0
        for p in Wv[::6]:
            if np.min(np.linalg.norm(Ph - p, axis=1)) < 0.05 * H:
                continue
            if inside(bvh, Vector(tuple(p))) >= 4:
                buried += 1
        s["weapon_verts_in_body_away_from_hands_(1in6_sampled)"] = buried
    return s


# render setup
scn = bpy.context.scene
scn.render.engine = 'BLENDER_WORKBENCH'
scn.display.shading.light = 'STUDIO'
scn.display.shading.color_type = 'SINGLE'
scn.display.shading.show_cavity = True
scn.render.resolution_x = scn.render.resolution_y = 520
scn.render.film_transparent = False
scn.world = scn.world or bpy.data.worlds.new("w")
cam_d = bpy.data.cameras.new("auditcam")
cam_d.lens = 50
cam = bpy.data.objects.new("auditcam", cam_d)
scn.collection.objects.link(cam)
scn.camera = cam


def shoot(path, yaw_deg):
    ctr = Vector((0, 0, FLOOR + H * 0.5)) + Vector(tuple(V0.mean(0))) * Vector((1, 1, 0))
    d = Matrix.Rotation(math.radians(yaw_deg), 3, UP) @ fwd
    cam.location = ctr + d * H * 1.85 + UP * H * 0.12
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)


res = {}
for name, fn in POSES:
    reset()
    extra = fn() or {}
    P, polys = posed_verts()
    s = score(P, polys)
    s.update(extra)
    res[name] = s
    if opts["--renders"] == "1":
        shoot(os.path.join(OUT, "poses", f"{name}_front.png"), -35)
        shoot(os.path.join(OUT, "poses", f"{name}_back.png"), 145)
        print("AUDIT pose", name, s["edges_extreme"])
reset()
R["poses"] = res

with open(os.path.join(OUT, "audit.json"), "w") as f:
    json.dump(R, f, indent=1, default=lambda o: r(o, 4) if isinstance(o, (np.floating,)) else str(o))
print("AUDIT done", NAME)
