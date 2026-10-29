"""Rig a Tripo HD body on the roster's skeleton (the user's spec, 2026-10-27).

    blender -b --python hd_rig.py -- <Name> <hd.fbx> <template_body.fbx> <current_body.fbx> <out_dir>
            [--hands grip|markers] [--markers-run 1|2] [--tris 60000] [--shield-blend <studio.blend>]
            [--renders <dir>] [--keep-rig 1] [--curl a,b,c]

Writes <out_dir>/<Name>_HD_rigged.fbx and .blend. NEVER writes to its inputs.

The template (Salaadin_Body.fbx, the 79-bone converted Rigify rig the whole roster shares) only
supplies NAMES, HIERARCHY and ROLL conventions. Every joint is re-fitted to this mesh:

  1. mesh      - import, apply transforms, decimate to --tris, scale to the template's crown,
                 floor at z=0, centred on the feet.
  2. fit       - joints placed from mesh landmarks: feet/toes from the boots, legs on the
                 hip->ankle line, spine on the torso's FRONT profile (capes hang behind, so the
                 back is not trusted), head from a slab above the beard, arms on the arm's own
                 axis (median of outer bands, so a hanging sleeve does not vote), wrist/elbow
                 centred on their cross-sections, fingers traced from the tip clusters inward.
                 Rolls are carried over from the template by the same rotation that moved each
                 bone, so every bone keeps the template's axis convention.
  3. bind      - bone heat for the body; fingers get nearest-segment weights inside the hand only
                 (heat bleeds between fused fingers). Heat failures are filled from the nearest
                 solved vertex, then any vertex bound to a bone it does not sit on is re-bound.
  4. rest pose - the arms are posed onto the TEMPLATE's rest directions (A-pose -> the roster's
                 near-T), the mesh is baked in that pose and the pose becomes the new rest, so
                 every fighter ends on an identical rest pose.
  5. hands     - grip  : fingers curled into a weapon grip (MCP/PIP/DIP 35/30/20, thumb wrapped),
                         baked, rest re-applied, then everything past the wrist plane weighted
                         100% to the hand bone. Finger bones stay (Humanoid needs them), zero weight.
                 markers: run 1 drops 12 empties (wrist + 5 tips per hand) and saves a .blend for
                         hand-snapping; run 2 builds the chains wrist->tip along each finger's
                         centreline and weights by nearest finger segment, hand verts only,
                         normalised and smoothed at the knuckles.
  6. weapons   - the fighter's weapons are lifted out of his CURRENT body FBX and re-seated with
                 the same bone-local placement (or, with --shield-blend, the shield's placement
                 is taken from that studio file).
  7. checks    - zero-weight verts, bones outside the mesh (6-ray majority vote), renders.

--keep-rig 1 (Richard, Buwaka: the user's call, 2026-10-27 - "their rigging is perfect, just try
the new model"): the fit runs on the shared template as usual, and the fitted rig is then used to
CONFORM the mesh onto the fighter's ORIGINAL skeleton (read from <current_body.fbx>): bone by bone,
parents first, each fitted body bone is posed onto the original bone's head and direction, the long
bones stretched to its length (head, hands and feet stay rigid). The posed mesh is baked, the grip
baked into its hands, the fitted rig thrown away, and the mesh scaled into the current body's space
and skinned to the original skeleton, unchanged - every joint, roll and finger. Posing the arms
alone is not enough: Richard's HD shoulders sit 13-19 cm below his skeleton's. (Fitting directly
against the current body also fails: the fit is tuned at the template's size, and Richard's rig is
1.85 tall against the template's 1.0.)
No bone is moved for containment and the weapons keep their current bone-local placement.

Export options are the project's (CLAUDE.md): apply_unit_scale, FBX_SCALE_NONE, -Z forward, Y up,
no added leaf bones, armature + mesh only.
"""
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix, kdtree
from mathutils.bvhtree import BVHTree

# --- arguments -----------------------------------------------------------------------------------
argv = sys.argv[sys.argv.index("--") + 1:]
NAME, HD_FBX, TEMPLATE_FBX, CURRENT_FBX, OUT_DIR = argv[:5]
opts = {"--hands": "grip", "--markers-run": "1", "--tris": "60000", "--shield-blend": "", "--renders": "",
        "--keep-rig": "0", "--curl": "", "--arm-top": "0", "--arm-axis": "bands", "--arm-mirror": "0"}
i = 5
while i < len(argv):
    opts[argv[i]] = argv[i + 1]; i += 2
HANDS = opts["--hands"]
KEEP = opts["--keep-rig"] == "1"
TRIS = int(opts["--tris"])
RENDERS = opts["--renders"] or os.path.join(OUT_DIR, "renders", NAME)
os.makedirs(OUT_DIR, exist_ok=True); os.makedirs(RENDERS, exist_ok=True)
STEM = os.path.join(OUT_DIR, f"{NAME}_HD_rigged")
MARKER_BLEND = os.path.join(OUT_DIR, f"{NAME}_HD_markers.blend")
for p in (HD_FBX, TEMPLATE_FBX, CURRENT_FBX):
    assert os.path.abspath(p) not in (os.path.abspath(STEM + ".fbx"), os.path.abspath(STEM + ".blend")), "refusing to overwrite an input"

MX = "mixamorig:"
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
SIDES = (("Left", 1.0), ("Right", -1.0))
REPORT = {"name": NAME, "hands": HANDS}


def log(*a):
    print(f"[{NAME}]", *a, flush=True)


def only_new(before):
    return set(bpy.data.objects) - before


def select(*objs, active=None):
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]


def mesh_np(obj, evaluated=False):
    if evaluated:
        dg = bpy.context.evaluated_depsgraph_get()
        me = obj.evaluated_get(dg).to_mesh()
    else:
        me = obj.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(obj.matrix_world)
    co = co @ M[:3, :3].T + M[:3, 3]
    if evaluated:
        obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return co


# =================================================================================================
# 1. template + mesh
# =================================================================================================
def load_template():
    """The template rig at its own scale, plus its donor body (used only to measure where the
    template's joints sit relative to ITS mesh, so the same relation can be applied here)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=TEMPLATE_FBX)
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    donor = max((o for o in bpy.data.objects if o.type == 'MESH'
                 and any(m.type == 'ARMATURE' for m in o.modifiers)), key=lambda o: len(o.data.vertices))
    rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    dv = mesh_np(donor)
    for o in [o for o in bpy.data.objects if o.type == 'MESH']:
        bpy.data.objects.remove(o)
    rig.name = rig.data.name = f"{NAME}_Rig"
    rig.data.pose_position = 'POSE'                 # REST was only for reading the template
    T = {}
    for b in rig.data.bones:
        T[b.name] = {"head": rig.matrix_world @ b.head_local, "tail": rig.matrix_world @ b.tail_local,
                     "z": (rig.matrix_world.to_3x3() @ b.matrix_local.to_3x3()).col[2].normalized()}
    return rig, T, dv


def load_mesh(crown):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=HD_FBX)
    added = only_new(before)
    mesh = max((o for o in added if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    for o in added:
        if o is not mesh:
            bpy.data.objects.remove(o)
    select(mesh)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mesh.data.calc_loop_triangles()
    tris = len(mesh.data.loop_triangles)
    REPORT["source_tris"] = tris
    HD["V"] = mesh_np(mesh)
    ed = np.empty(len(mesh.data.edges) * 2, dtype=np.int64)
    mesh.data.edges.foreach_get("vertices", ed)
    HD["E"] = ed.reshape(-1, 2)
    if tris > TRIS * 1.1:
        dec = mesh.modifiers.new("dec", 'DECIMATE')
        dec.decimate_type = 'COLLAPSE'
        dec.ratio = TRIS / tris
        dec.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=dec.name)
    mesh.data.calc_loop_triangles()
    REPORT["tris"] = len(mesh.data.loop_triangles); REPORT["verts"] = len(mesh.data.vertices)
    log(f"mesh {tris} -> {REPORT['tris']} tris, {REPORT['verts']} verts")

    V = mesh_np(mesh)
    floor, top = V[:, 2].min(), V[:, 2].max()
    s = crown / (top - floor)
    feet = V[(V[:, 2] - floor) * s < 0.06 * crown]
    cx, cy = (V[:, 0].min() + V[:, 0].max()) / 2, np.median(feet[:, 1])
    for v in mesh.data.vertices:
        v.co.x = (v.co.x - cx) * s
        v.co.y = (v.co.y - cy) * s
        v.co.z = (v.co.z - floor) * s
    mesh.data.update()
    Vh = HD["V"]
    HD["V"] = np.stack([(Vh[:, 0] - cx) * s, (Vh[:, 1] - cy) * s, (Vh[:, 2] - floor) * s], axis=1)
    mesh.name = mesh.data.name = f"{NAME}_Body"
    if mesh.data.materials and mesh.data.materials[0]:
        mesh.data.materials[0].name = f"{NAME}_Body"
    REPORT["scale_applied"] = round(float(s), 5)
    return mesh


HD = {}      # the full-resolution mesh (normalised), used for every landmark; the bind uses the 60k one


def import_current():
    """The current body's rig (rest) and its body verts; every other imported object removed."""
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=CURRENT_FBX)
    added = only_new(before)
    rig = next(o for o in added if o.type == 'ARMATURE')
    donor = max((o for o in added if o.type == 'MESH' and any(m.type == 'ARMATURE' for m in o.modifiers)),
                key=lambda o: len(o.data.vertices))
    rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    dv = mesh_np(donor)
    for o in added:
        if o is not rig:
            bpy.data.objects.remove(o)
    return rig, dv


def keep_targets(mesh):
    """--keep-rig: the current skeleton's bones, mapped into THIS mesh's space by the scale and
    offset that stand the mesh where the current body stands (same height, floor, feet).
    Returns (targets, k, off): orig = mesh_space * k + off."""
    rig, dv = import_current()
    V = mesh_np(mesh)
    k = float((dv[:, 2].max() - dv[:, 2].min()) / (V[:, 2].max() - V[:, 2].min()))

    def anchor(P):
        floor = P[:, 2].min()
        feet = P[P[:, 2] - floor < 0.06 * (P[:, 2].max() - floor)]
        return np.array([(P[:, 0].min() + P[:, 0].max()) / 2, np.median(feet[:, 1]), floor])
    off = anchor(dv) - anchor(V) * k
    K = {}
    for b in rig.data.bones:
        h = (np.array(rig.matrix_world @ b.head_local) - off) / k
        t = (np.array(rig.matrix_world @ b.tail_local) - off) / k
        K[b.name] = (Vector(h), Vector(t))
    bpy.data.objects.remove(rig)
    return K, k, off


CONFORM_RIGID = ("Head", "Hand", "Foot", "ToeBase")      # moved and turned, never stretched


def conform(rig, K):
    """Pose every fitted body bone onto the original skeleton's bone (head, direction, and length
    for the long bones). Fingers, leaves and helpers carry no weight and are left alone. Inherited
    scale is switched off so a stretched parent does not shear its children."""
    names = [b.name for b in rig.data.bones if b.name in K and not is_finger(b.name) and not is_leaf(b.name)]
    depth = lambda n: len(rig.data.bones[n].parent_recursive)
    for b in rig.data.bones:
        b.inherit_scale = 'NONE'
    moved = {}
    for n in sorted(names, key=depth):
        pb = rig.pose.bones[n]
        h, t = K[n]
        Mw = rig.matrix_world @ pb.matrix
        R0 = Mw.to_3x3().normalized()
        cur = (R0 @ Vector((0, 1, 0))).normalized()
        new = (t - h)
        if new.length < 1e-6:
            continue
        R = cur.rotation_difference(new.normalized()).to_matrix() @ R0
        sy = 1.0 if any(n.endswith(r) for r in CONFORM_RIGID) else new.length / max(rig.data.bones[n].length, 1e-6)
        S = Matrix.Diagonal((1.0, sy, 1.0, 1.0))
        pb.matrix = rig.matrix_world.inverted() @ (Matrix.Translation(h) @ R.to_4x4() @ S)
        bpy.context.view_layer.update()
        moved[n] = (round((Mw.translation - h).length, 4), round(math.degrees(cur.angle(new)), 1), round(sy, 3))
    return moved


def align_to_donor(mesh, dv):
    """--keep-rig: stand the mesh where the ORIGINAL body stood (same floor, same feet), since the
    skeleton will not move to meet it."""
    floor = dv[:, 2].min()
    crown = dv[:, 2].max() - floor
    feet = dv[dv[:, 2] - floor < 0.06 * crown]
    off = np.array([(dv[:, 0].min() + dv[:, 0].max()) / 2, np.median(feet[:, 1]), floor])
    for v in mesh.data.vertices:
        v.co.x += off[0]; v.co.y += off[1]; v.co.z += off[2]
    mesh.data.update()
    HD["V"] = HD["V"] + off
    REPORT["aligned_to_donor"] = [round(float(x), 4) for x in off]


def swap_rig(fitted, mesh, H, k, off):
    """--keep-rig: drop the temporary fitted rig; carry the (conformed, gripped) mesh into the
    current body's space (x k, + off) and skin it to the ORIGINAL skeleton."""
    mw = mesh.matrix_world.copy()
    for m in [m for m in mesh.modifiers if m.type == 'ARMATURE']:
        mesh.modifiers.remove(m)
    mesh.parent = None
    mesh.matrix_world = mw
    select(mesh)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    name, fdata = fitted.name, fitted.data
    bpy.data.objects.remove(fitted)
    bpy.data.armatures.remove(fdata)
    orig, dv = import_current()
    orig.data.pose_position = 'POSE'
    ov = Vector(off)
    for v in mesh.data.vertices:
        v.co = v.co * k + ov
    mesh.data.update()
    HD["V"] = HD["V"] * k + off
    for F in GRIP_F.values():
        F["wr"] = F["wr"] * k + (ov if isinstance(F["wr"], Vector) else off)
        for key in ("L", "v_lo", "v_hi", "w_mid"):
            F[key] = F[key] * k
    orig.name = orig.data.name = name
    H = H * k
    REPORT["keep_rig_scale"] = round(k, 5)
    bind(orig, mesh, H)
    return orig, H


# =================================================================================================
# 2. fitting helpers
# =================================================================================================
class Probe:
    """Containment and nearest-surface queries on the CURRENT mesh shape."""
    def __init__(self, mesh, evaluated=False):
        dg = bpy.context.evaluated_depsgraph_get()
        src = mesh.evaluated_get(dg) if evaluated else mesh
        me = src.to_mesh() if evaluated else src.data
        M = mesh.matrix_world
        self.co = [M @ v.co for v in me.vertices]
        self.bvh = BVHTree.FromPolygons(self.co, [tuple(p.vertices) for p in me.polygons])
        if evaluated:
            src.to_mesh_clear()

    def inside(self, p):
        """Votes out of 6 axis rays; a clean interior point reads 6, a clean exterior 0."""
        votes = 0
        for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)),
                  Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
            n, q = 0, p.copy()
            for _ in range(200):
                hit = self.bvh.ray_cast(q, d)
                if hit[0] is None:
                    break
                n += 1
                q = hit[0] + d * 1e-6
            votes += n % 2
        return votes

    def pull_inside(self, p, inset):
        if self.inside(p) >= 4:
            return p, False
        loc, nrm, _, _ = self.bvh.find_nearest(p)
        for k in (1.0, 2.0, 3.0, 0.5):
            q = loc - nrm * inset * k
            if self.inside(q) >= 4:
                return q, True
        return loc - nrm * inset, True


def slab(V, center, axis, half, radius):
    d = V - np.asarray(center)
    t = d @ np.asarray(axis)
    perp = d - np.outer(t, axis)
    m = (np.abs(t) < half) & (np.linalg.norm(perp, axis=1) < radius)
    return V[m]


def front_y(V, z, half_w, band):
    m = (np.abs(V[:, 0]) < half_w) & (np.abs(V[:, 2] - z) < band)
    return float(np.percentile(V[m, 1], 3)) if m.sum() > 10 else None


def front_line(V, z_lo, z_hi, half_w, band, samples=24):
    """front_y sampled up the torso and fitted with a straight line, dropping readings more than
    2.5 MADs off it (repeatedly) - so a hilt or a knot in front of the belly is ignored, not
    followed. Returns a callable z -> y (with .info for the report), or None if too few readings."""
    zs, ys = [], []
    for z in np.linspace(z_lo, z_hi, samples):
        y = front_y(V, z, half_w, band)
        if y is not None:
            zs.append(z); ys.append(y)
    zs, ys = np.array(zs), np.array(ys)
    if len(zs) < 6:
        return None
    keep = np.ones(len(zs), bool)
    for _ in range(4):
        a, b = np.polyfit(zs[keep], ys[keep], 1)
        r = ys - (a * zs + b)
        mad = float(np.median(np.abs(r[keep] - np.median(r[keep])))) + 1e-6
        new = np.abs(r) < 2.5 * 1.4826 * mad + 0.004 * (z_hi - z_lo)
        if new.sum() < 6 or (new == keep).all():
            break
        keep = new
    a, b = np.polyfit(zs[keep], ys[keep], 1)
    f = lambda z: float(a * z + b)
    f.info = {"slope": round(float(a), 4), "kept": int(keep.sum()), "of": len(zs),
              "dropped_z": [round(float(z), 3) for z in zs[~keep]]}
    return f


def mid_y(V, z, half_w, band):
    m = (np.abs(V[:, 0]) < half_w) & (np.abs(V[:, 2] - z) < band)
    if m.sum() < 10:
        return None
    return float((np.percentile(V[m, 1], 5) + np.percentile(V[m, 1], 95)) / 2)


def components(n, edges):
    parent = np.arange(n)
    if len(edges):
        a, b = edges[:, 0], edges[:, 1]
        while True:
            pa, pb = parent[a], parent[b]
            lo, hi = np.minimum(pa, pb), np.maximum(pa, pb)
            m = lo != hi
            if not m.any():
                break
            np.minimum.at(parent, hi[m], lo[m])
            while True:
                nxt = parent[parent]
                if np.array_equal(nxt, parent):
                    break
                parent = nxt
    return parent


def arm_axis(V, sgn, H):
    """Arm direction from the median of two OUTER bands (|x| 0.26-0.34H and 0.36-0.46H, above
    the hips). Medians, so a hanging sleeve or fur cuff does not drag the line down."""
    x = V[:, 0] * sgn
    up = V[:, 2] > 0.45 * H
    tipx = x[up].max()
    a = V[up & (x > 0.55 * tipx) & (x < 0.66 * tipx)]
    b = V[up & (x > 0.78 * tipx) & (x < 0.88 * tipx)]
    if opts["--arm-top"] == "1":
        # --arm-top 1 (Kojiro, 2026-10-29): kimono sleeves hang from the arm to the waist, so the
        # band median sits in the cloth. The cloth drapes OVER the arm, so keep each band's top slab.
        a = a[a[:, 2] > a[:, 2].max() - 0.06 * H]
        b = b[b[:, 2] > b[:, 2].max() - 0.06 * H]
    ca, cb = np.median(a, axis=0), np.median(b, axis=0)
    u = (cb - ca) / np.linalg.norm(cb - ca)
    tip = V[up][np.argmax(x[up])]
    if opts["--arm-axis"] == "section":
        # --arm-axis section (Sensei, 2026-10-29): a band median caught the robe ridge over his right
        # shoulder, 1.2% H high, and the line from the band nearest the shoulder magnified it to 3.3% H.
        # Use only slices that are ARM alone (short vertical extent: no hanging sleeve, no robe panel)
        # and fit the line through their centres: height from the slice's bounding box, depth from its
        # MEDIAN (the box's depth takes in the sleeve hanging behind, which put the shoulder 2% H out the back).
        cen = []
        for xs in np.arange(0.55 * tipx, 0.88 * tipx, 0.01 * H):
            sl = V[up & (np.abs(x - xs) < 0.004 * H)]
            if len(sl) > 20 and np.ptp(sl[:, 2]) < 0.10 * H:
                cen.append(np.array([np.median(sl[:, 0]), np.median(sl[:, 1]), (sl[:, 2].min() + sl[:, 2].max()) / 2]))
        if len(cen) >= 4:
            C = np.array(cen)
            c0 = C.mean(0)
            d = np.linalg.svd(C - c0)[2][0]
            d = d if d[0] * sgn > 0 else -d
            REPORT.setdefault("arm_axis_sections", {})["L" if sgn > 0 else "R"] = len(cen)
            return Vector(c0), Vector(d), Vector(tip)
        log(f"arm-axis section: only {len(cen)} clean slices on side {sgn}, keeping the band medians")
    return Vector(ca), Vector(u), Vector(tip)


def line_point_at_x(p0, u, x):
    return p0 + u * ((x - p0.x) / u.x)


# =================================================================================================
# 3. fit the template's joints to the mesh
# =================================================================================================
def fit_joints(T, dv, mesh):
    """Returns {bone: (head, tail)} for every template bone, fitted to this mesh. Landmarks come
    from the FULL-RESOLUTION mesh; containment is judged on the decimated one that ships."""
    V = HD["V"]
    H = float(V[:, 2].max())
    Ht = float(dv[:, 2].max())
    k = H / Ht
    J = {n: [T[n]["head"] * k, T[n]["tail"] * k] for n in T}
    hd = lambda n: J[MX + n][0]
    probe = Probe(mesh)

    # ---- feet: ankle over the boot, toe at the boot's front -----------------------------------
    for side, s in SIDES:
        ank_t = T[MX + side + "Foot"]["head"] * k
        band = V[(V[:, 2] > 0.045 * H) & (V[:, 2] < 0.075 * H) & (V[:, 0] * s > 0.01 * H)]
        ank = Vector((np.median(band[:, 0]), np.median(band[:, 1]), ank_t.z))
        sole = V[(V[:, 2] < 0.03 * H) & (V[:, 0] * s > 0.01 * H)]
        toe_tip = Vector(sole[np.argmin(sole[:, 1])]); toe_tip.z = T[MX + side + "Toe_End"]["tail"].z * k
        # map the template foot (ankle -> toe-end tail) onto ankle -> toe tip: yaw + scale in plan
        ta, tt = T[MX + side + "Foot"]["head"] * k, T[MX + side + "Toe_End"]["tail"] * k
        src2, dst2 = (tt - ta).xy, (toe_tip - ank).xy
        yaw = math.atan2(dst2.y, dst2.x) - math.atan2(src2.y, src2.x)
        sc = dst2.length / max(src2.length, 1e-6)
        R = Matrix.Rotation(yaw, 3, 'Z')
        def mapf(p):
            d = R @ (p - ta); return ank + Vector((d.x * sc, d.y * sc, d.z))
        for n in (side + "Foot", side + "ToeBase", side + "Toe_End"):
            J[MX + n] = [mapf(T[MX + n]["head"] * k), mapf(T[MX + n]["tail"] * k)]
        heel = side[0] + "_Heel"
        if heel in J:
            J[heel] = [mapf(T[heel]["head"] * k), mapf(T[heel]["tail"] * k)]

    # ---- spine: on the torso's FRONT profile (capes hang behind) -------------------------------
    # The profile is a straight LINE fitted through the front over the whole torso, not read height by
    # height: anything worn in front of the belly (Miyamoto's sword through the sash, knotted cords)
    # pushed single readings forward, the spine zig-zagged round it (Spine1 111 deg off), and conform
    # then folded the abdomen to straighten it (2026-10-27).
    spine_names = ["Hips", "Spine", "Spine1", "Spine2"]
    z_lo, z_hi = T[MX + "Hips"]["head"].z, T[MX + "Spine2"]["tail"].z
    line_t = front_line(dv, z_lo, z_hi, 0.08 * Ht, 0.012 * Ht)
    line = front_line(V, z_lo * k, z_hi * k, 0.08 * H, 0.012 * H)
    front_offset = lambda z_t: None if line_t is None else line_t(z_t)
    REPORT["spine_front_line"] = None if line is None else line.info
    for n in spine_names:
        h_t, t_t = T[MX + n]["head"], T[MX + n]["tail"]
        out = []
        for p_t in (h_t, t_t):
            fy_t = front_offset(p_t.z)
            fy = None if line is None else line(p_t.z * k)
            y = p_t.y * k if fy_t is None or fy is None else fy + (p_t.y - fy_t) * k
            out.append(Vector((0.0, y, p_t.z * k)))
        J[MX + n] = out
    # chain continuity
    J[MX + "Spine1"][0] = J[MX + "Spine"][1].copy()
    J[MX + "Spine2"][0] = J[MX + "Spine1"][1].copy()

    # ---- head: centre of a slab ABOVE the beard; neck between the chest and the head -----------
    zh = 0.93 * Ht
    my_t = mid_y(dv, zh, 0.06 * Ht, 0.015 * Ht)
    my = mid_y(V, zh * k, 0.06 * H, 0.015 * H)
    dy = 0.0 if my_t is None or my is None else my - my_t * k
    for n in ("Head", "HeadTop_End"):
        J[MX + n] = [Vector((0, T[MX + n]["head"].y * k + dy, T[MX + n]["head"].z * k)),
                     Vector((0, T[MX + n]["tail"].y * k + dy, T[MX + n]["tail"].z * k))]
    J[MX + "Neck"] = [Vector((0, (T[MX + "Neck"]["head"].y * k + J[MX + "Spine2"][1].y) / 2,
                              T[MX + "Neck"]["head"].z * k)), J[MX + "Head"][0].copy()]

    # ---- legs: hip joint from the template's width, knee on the hip->ankle line ----------------
    for side, s in SIDES:
        hip_t, knee_t, ank_t = (T[MX + side + n]["head"] for n in ("UpLeg", "Leg", "Foot"))
        ank = J[MX + side + "Foot"][0]
        hip = Vector((hip_t.x * k, J[MX + "Hips"][0].y + (hip_t.y - T[MX + "Hips"]["head"].y) * k, hip_t.z * k))
        # keep the template's knee bend: its offset from its own hip->ankle line, in leg lengths
        L_t = (ank_t - hip_t); t_k = (knee_t - hip_t).dot(L_t) / L_t.length_squared
        off_t = (knee_t - (hip_t + L_t * t_k)) / L_t.length
        L = ank - hip
        knee = hip + L * t_k + off_t * L.length
        knee, _ = probe.pull_inside(knee, 0.01 * H)
        J[MX + side + "UpLeg"] = [hip, knee]
        J[MX + side + "Leg"] = [knee.copy(), ank.copy()]
        pel = side[0] + "_Skirt"
        if pel in J:
            J[pel] = [J[MX + "Hips"][0].copy(), J[MX + "Hips"][0] + (T[pel]["tail"] - T[pel]["head"]) * k]

    # ---- arms: on the arm's own axis; wrist and elbow centred on their cross-sections -----------
    axes = {s: arm_axis(V, s, H) for _, s in SIDES}
    if opts["--arm-mirror"] == "1":
        # --arm-mirror 1 (Sensei, 2026-10-29): the sculpt's arms mirror to ~0.5% H but his sleeves do not,
        # and one side's fit came out 2% H further back. Average each side with the other's mirror image.
        mir = lambda p: Vector((-p.x, p.y, p.z))
        avg = {}
        for _, s in SIDES:
            (ca, u, tip), (cb, w, tb) = axes[s], axes[-s]
            avg[s] = ((ca + mir(cb)) / 2, ((u + mir(w)) / 2).normalized(), tip)
        axes = avg
    for side, s in SIDES:
        ca, u, tip = axes[s]
        sh_t = T[MX + side + "Arm"]["head"]
        shoulder = line_point_at_x(ca, u, sh_t.x * k)
        # hand length (wrist -> middle tip) as the template's fraction of shoulder -> middle tip
        mid_end = T.get(MX + side + "HandMiddle4") or T[MX + side + "HandMiddle3"]
        hl_t = (mid_end["tail"] - T[MX + side + "Hand"]["head"]).length
        arm_t = (mid_end["tail"] - sh_t).length
        reach = (tip - shoulder).dot(u)
        wrist = shoulder + u * (reach * (1 - hl_t / arm_t))
        cs = slab(V, wrist, u, 0.006 * H, 0.05 * H)
        if len(cs) > 20:
            wrist = Vector(np.median(cs, axis=0))
        el_t = T[MX + side + "ForeArm"]["head"]; wr_t = T[MX + side + "Hand"]["head"]
        A_t = wr_t - sh_t; t_e = (el_t - sh_t).dot(A_t) / A_t.length_squared
        off_e = (el_t - (sh_t + A_t * t_e)) / A_t.length
        A = wrist - shoulder
        elbow = shoulder + A * t_e
        cs = slab(V, elbow, A.normalized(), 0.008 * H, 0.06 * H)
        if len(cs) > 20:
            elbow = Vector(np.median(cs, axis=0))
        elbow = elbow + off_e * A.length * 0.5      # half the template's bend: a hint, not a pose
        elbow, _ = probe.pull_inside(elbow, 0.01 * H)
        J[MX + side + "Arm"] = [shoulder, elbow]
        J[MX + side + "ForeArm"] = [elbow.copy(), wrist.copy()]
        clav_t = T[MX + side + "Shoulder"]
        J[MX + side + "Shoulder"] = [Vector((clav_t["head"].x * k, J[MX + "Spine2"][1].y + (clav_t["head"].y - T[MX + "Spine2"]["tail"].y) * k,
                                             shoulder.z + (clav_t["head"].z - sh_t.z) * k)), shoulder.copy()]
        fit_hand(T, J, side, s, wrist, tip, V, H, k, probe)
    for side in ("L", "R"):
        b = side + "_Pec"
        if b in J:
            off = (T[b]["head"] - T[MX + "Spine2"]["head"]) * k
            J[b] = [J[MX + "Spine2"][0] + off, J[MX + "Spine2"][0] + off + (T[b]["tail"] - T[b]["head"]) * k]
    return J, H


def hand_frame(V, wrist, tip, H):
    """u along the hand, w the palm normal (sign: the side the fingertips curl toward), v across."""
    u = (tip - wrist).normalized()
    pts = slab(V, (wrist + tip) / 2, np.asarray(u), (tip - wrist).length * 0.55, 0.06 * H)
    c = pts.mean(axis=0)
    evals, evecs = np.linalg.eigh(np.cov((pts - c).T))
    w = Vector(evecs[:, 0])                          # thinnest axis = palm normal
    w = (w - u * w.dot(u)).normalized()
    # fingertips curl toward the palm: the outer quarter sits on the palm side of the hand line
    t = (pts - np.asarray(wrist)) @ np.asarray(u)
    outer = pts[t > 0.75 * (tip - wrist).length]
    if len(outer):
        d = Vector(outer.mean(axis=0)) - wrist
        if d.dot(w) < 0:
            w = -w
    v = u.cross(w).normalized()
    return u, v, w, pts


def fit_hand(T, J, side, s, wrist, tip, V, H, k, probe):
    hand_t = T[MX + side + "Hand"]
    mid_end = T.get(MX + side + "HandMiddle4") or T[MX + side + "HandMiddle3"]
    # template hand frame: u along the hand, w = the hand bone's z (the palm, per hand_frame()'s
    # convention in salaadin_body.py), v across
    ut = (mid_end["tail"] - hand_t["head"]).normalized()
    wt = (hand_t["z"] - ut * hand_t["z"].dot(ut)).normalized()
    vt = ut.cross(wt).normalized()
    u, v, w, pts = hand_frame(V, wrist, tip, H)
    Mt = Matrix((ut, vt, wt)).transposed()         # template frame -> world
    Mm = Matrix((u, v, w)).transposed()
    R = Mm @ Mt.transposed()
    sc = (tip - wrist).length / (mid_end["tail"] - hand_t["head"]).length
    def mapp(p):
        return wrist + (R @ ((p - hand_t["head"]))) * sc
    names = [n for n in T if n.startswith(MX + side + "Hand")] + [f"{side[0]}_Meta{i}" for i in range(1, 5)]
    for n in names:
        if n in T:
            J[n] = [mapp(T[n]["head"]), mapp(T[n]["tail"])]
    J[MX + side + "Hand"][0] = wrist.copy()
    for n in names:                                # rolls follow the MEASURED palm frame
        if n in T:
            ROLL[n] = R @ T[n]["z"]

    # refine each fingertip onto its own tip cluster (fingers separate only near the tips)
    tips = finger_tip_clusters(V, wrist, u, v, w, (tip - wrist).length, H)
    used = set()
    for f in FINGERS[1:]:
        end = J.get(MX + side + f"Hand{f}4") or J[MX + side + f"Hand{f}3"]
        guess = end[1]
        best = min(((i, (Vector(c) - guess).length) for i, c in enumerate(tips) if i not in used),
                   key=lambda t: t[1], default=(None, 1e9))
        if best[0] is not None and best[1] < 0.03 * H:
            used.add(best[0])
            new_tip = Vector(tips[best[0]])
            retarget_chain(J, side, f, new_tip)
    centre_fingers(J, side, V, H, probe)
    # every finger joint must be INSIDE the flesh
    moved = 0
    for n in names:
        if n in J and n != MX + side + "Hand":
            for i in (0, 1):
                J[n][i], m = probe.pull_inside(J[n][i], 0.0025 * H)
                moved += m
    # re-chain: each child head = parent tail
    for f in FINGERS:
        for i in (2, 3, 4):
            a, b = MX + side + f"Hand{f}{i-1}", MX + side + f"Hand{f}{i}"
            if a in J and b in J:
                J[b][0] = J[a][1].copy()
    REPORT.setdefault("fingers", {})[side] = {"tip_clusters": len(tips), "matched": len(used), "joints_pulled_inside": int(moved)}


ROLL = {}     # bone -> desired z axis, for bones whose roll is measured rather than carried over


def finger_tip_clusters(V, wrist, u, v, w, hl, H):
    """Separate pieces in thin slabs near the hand's end; returns one tip point per piece found
    in the deepest slab that still shows >= 4 pieces."""
    ed = HD["E"]
    t = (V - np.asarray(wrist)) @ np.asarray(u)
    r = np.linalg.norm((V - np.asarray(wrist)) - np.outer(t, u), axis=1)
    hand = (t > 0.45 * hl) & (t < 1.05 * hl) & (r < 0.06 * H)
    best = []
    for k_ in range(14):
        a0 = hl * (1.02 - 0.03 * (k_ + 1)); a1 = a0 + 0.03 * hl
        m = hand & (t >= a0) & (t < a1)
        idx = np.nonzero(m)[0]
        if len(idx) < 8:
            continue
        remap = -np.ones(len(V), dtype=np.int64); remap[idx] = np.arange(len(idx))
        e = ed[m[ed[:, 0]] & m[ed[:, 1]]]
        lab = components(len(idx), remap[e])
        labs, cnt = np.unique(lab, return_counts=True)
        pieces = [labs[i] for i in range(len(labs)) if cnt[i] >= 6]
        if len(pieces) >= 4:
            best = []
            for p in pieces:
                pv = V[idx[lab == p]]
                tt = (pv - np.asarray(wrist)) @ np.asarray(u)
                best.append(pv[np.argmax(tt)])
    return best


def seg_dist_np(P, a, b):
    a, b = np.asarray(a), np.asarray(b)
    ab = b - a
    t = np.clip((P - a) @ ab / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(P - (a + np.outer(t, ab)), axis=1)


def centre_fingers(J, side, V, H, probe):
    """Put each finger's middle joints on the finger's own CURVED centreline: a hand vertex
    belongs to the finger whose knuckle->tip chord is nearest, and each joint is the median of
    that finger's verts in a slab across its chord. A curled finger's joints therefore move
    toward the palm side, where the flesh actually is."""
    chains = {}
    for f in FINGERS:
        c = [MX + side + f"Hand{f}{i}" for i in (1, 2, 3, 4) if MX + side + f"Hand{f}{i}" in J]
        chains[f] = (J[c[0]][0].copy(), J[c[-1]][1].copy(), c)
    wr = J[MX + side + "Hand"][0]
    reach = max((P - wr).length for _, P, _ in chains.values())
    Vh = V[np.linalg.norm(V - np.asarray(wr), axis=1) < reach * 1.1]
    if len(Vh) < 50:
        return
    d = np.stack([seg_dist_np(Vh, K, P) for K, P, _ in chains.values()])
    owner = np.argmin(d, axis=0)
    for fi, (f, (K, P, chain)) in enumerate(chains.items()):
        mine = Vh[owner == fi]
        u = (P - K).normalized()
        L = (P - K).length
        pts = [K]
        for n in chain[1:]:
            fr = (J[n][0] - K).dot(u) / L
            s_ = slab(mine, K + u * (fr * L), np.asarray(u), 0.004 * H, 0.02 * H)
            q = Vector(np.median(s_, axis=0)) if len(s_) > 5 else J[n][0]
            q, _ = probe.pull_inside(q, 0.002 * H)
            pts.append(q)
        last_dir = P - pts[-1]
        for i, n in enumerate(chain):
            h = pts[i]
            if i + 1 < len(pts):
                t = pts[i + 1]
            elif n.endswith("4"):
                t = P + last_dir.normalized() * 0.002 * H
            else:
                t = P
            J[n] = [h, t]


def retarget_chain(J, side, f, new_tip):
    """Keep the knuckle, re-aim the finger at the measured tip, keep the template's joint ratios."""
    chain = [MX + side + f"Hand{f}{i}" for i in (1, 2, 3, 4) if MX + side + f"Hand{f}{i}" in J]
    knuckle = J[chain[0]][0]
    old_tip = J[chain[-1]][1]
    old = [(J[c][0] - knuckle).length for c in chain] + [(old_tip - knuckle).length]
    L_old = old[-1]
    for i, c in enumerate(chain):
        f0 = old[i] / L_old
        f1 = (old[i + 1] / L_old) if i + 1 < len(old) else 1.0
        J[c] = [knuckle.lerp(new_tip, f0), knuckle.lerp(new_tip, f1)]


def apply_joints(rig, T, J):
    """Move every bone, carrying its roll over by the same rotation that moved its direction."""
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    for n, (h, t) in J.items():
        if n not in eb:
            continue
        b = eb[n]
        b.use_connect = False
    for n, (h, t) in J.items():
        if n not in eb:
            continue
        b = eb[n]
        old_dir = (T[n]["tail"] - T[n]["head"]).normalized()
        new_dir = (t - h)
        if new_dir.length < 1e-5:
            new_dir = old_dir * 0.01; t = h + new_dir
        b.head, b.tail = h, t
        z = ROLL.get(n) or (old_dir.rotation_difference(new_dir.normalized()) @ T[n]["z"])
        b.align_roll(z)
    bpy.ops.object.mode_set(mode='OBJECT')


# =================================================================================================
# 4. binding
# =================================================================================================
def is_finger(n):
    return any(f"Hand{f}" in n for f in FINGERS) or "_Meta" in n


def is_leaf(n):
    return n.endswith("_End") or n.endswith("4") or any(k in n for k in ("_Heel", "_Pec", "_Skirt"))


def bind(rig, mesh, H):
    """Bone heat for the body (fingers and leaf/helper bones excluded), then nearest-segment
    weights for the fingers inside each hand. Unsolved verts take their nearest solved
    neighbour; verts bound to a bone they do not sit on are re-bound."""
    saved = {}
    for b in rig.data.bones:
        saved[b.name] = b.use_deform
        b.use_deform = not (is_finger(b.name) or is_leaf(b.name))
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    for m in [m for m in mesh.modifiers if m.type == 'ARMATURE']:
        mesh.modifiers.remove(m)
    select(mesh, rig, active=rig)
    try:
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    except RuntimeError as e:
        log("bone heat raised:", e)
    for b in rig.data.bones:
        b.use_deform = saved[b.name]
    for b in rig.data.bones:                         # every bone gets a group, even if empty
        if mesh.vertex_groups.get(b.name) is None:
            mesh.vertex_groups.new(name=b.name)
    filled = fill_unweighted(mesh)
    REPORT["heat_unsolved_filled"] = filled
    for side, _ in SIDES:
        finger_weights(rig, mesh, side, H)
    REPORT["rebound"] = rebind_far(rig, mesh, 0.22 * H / 1.0)
    select(mesh)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)


def weights_of(mesh):
    gname = {g.index: g.name for g in mesh.vertex_groups}
    return [[(gname[g.group], g.weight) for g in v.groups if g.weight > 1e-4] for v in mesh.data.vertices]


def fill_unweighted(mesh):
    W = weights_of(mesh)
    weighted = [i for i, w in enumerate(W) if w]
    unw = [i for i, w in enumerate(W) if not w]
    if unw and weighted:
        kd = kdtree.KDTree(len(weighted))
        for i in weighted:
            kd.insert(mesh.data.vertices[i].co, i)
        kd.balance()
        for i in unw:
            _, j, _ = kd.find(mesh.data.vertices[i].co)
            for n, w in W[j]:
                mesh.vertex_groups[n].add([i], w, 'REPLACE')
    log(f"heat left {len(unw)} of {len(W)} verts unweighted (filled from neighbours)")
    return len(unw)


def seg_of(rig, n):
    b = rig.data.bones[n]
    return rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local


def dist_seg(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def rebind_far(rig, mesh, thresh):
    W = weights_of(mesh)
    seg = {b.name: seg_of(rig, b.name) for b in rig.data.bones}
    bad, good = [], []
    for i, w in enumerate(W):
        if not w:
            continue
        dom = max(w, key=lambda t: t[1])[0]
        p = mesh.matrix_world @ mesh.data.vertices[i].co
        (bad if dist_seg(p, *seg[dom]) > thresh else good).append(i)
    if bad:
        kd = kdtree.KDTree(len(good))
        for i in good:
            kd.insert(mesh.data.vertices[i].co, i)
        kd.balance()
        for i in bad:
            _, j, _ = kd.find(mesh.data.vertices[i].co)
            for n, _w in W[i]:
                mesh.vertex_groups[n].remove([i])
            for n, w in W[j]:
                mesh.vertex_groups[n].add([i], w, 'REPLACE')
    log(f"re-bound {len(bad)} verts that sat further than {thresh:.3f} from their dominant bone")
    return len(bad)


def hand_verts(rig, mesh, side):
    """Verts past the wrist plane, near the hand: the region the hand modes own."""
    wr, ht = seg_of(rig, MX + side + "Hand")
    fa, _ = seg_of(rig, MX + side + "ForeArm")
    u = (wr - fa).normalized()
    reach = max((seg_of(rig, n)[1] - wr).length for n in rig.data.bones.keys() if n.startswith(MX + side + "Hand"))
    out = []
    for v in mesh.data.vertices:
        p = mesh.matrix_world @ v.co
        d = p - wr
        t = d.dot(u)
        if 0.0 <= t <= reach * 1.15 and (d - u * t).length < reach * 0.9:
            out.append(v.index)
    return out, wr, u


def finger_weights(rig, mesh, side, H):
    """Each hand vertex goes to its nearest finger/thumb SEGMENT, or to the hand bone if the palm
    is nearer. Limited to the hand, so nothing bleeds up the forearm; then a light smooth across
    the knuckles."""
    idx, wr, u = hand_verts(rig, mesh, side)
    segs = [(n, *seg_of(rig, n)) for n in rig.data.bones.keys()
            if n.startswith(MX + side + "Hand") and n != MX + side + "Hand" and not n.endswith("4")]
    hand_seg = seg_of(rig, MX + side + "Hand")
    knuckles = [seg_of(rig, MX + side + f"Hand{f}1")[0] for f in FINGERS[1:]]
    kmid = sum(knuckles, Vector()) / len(knuckles)
    palm_end = (kmid - wr).dot(u)
    sigma = 0.006 * H
    for i in idx:
        p = mesh.matrix_world @ mesh.data.vertices[i].co
        for g in mesh.vertex_groups:
            if g.name.startswith(MX + side + "Hand") or g.name.startswith(MX + side + "ForeArm"):
                try:
                    g.remove([i])
                except RuntimeError:
                    pass
        t = (p - wr).dot(u)
        if t < palm_end * 0.8:                       # the palm proper: the hand bone alone
            mesh.vertex_groups[MX + side + "Hand"].add([i], 1.0, 'REPLACE')
            continue
        # soft: the two nearest finger segments and the hand, Gaussian in distance
        ds = sorted((dist_seg(p, sg[1], sg[2]), sg[0]) for sg in segs)[:2]
        ds.append((dist_seg(p, *hand_seg) + 0.5 * sigma, MX + side + "Hand"))
        ws = [(n, math.exp(-(d_ / sigma) ** 2)) for d_, n in ds]
        tot = sum(w for _, w in ws)
        if tot < 1e-12:
            ws, tot = [(ds[0][1], 1.0)], 1.0
        for n, w in ws:
            if w / tot > 0.02:
                mesh.vertex_groups[n].add([i], w / tot, 'REPLACE')
    smooth_groups(mesh, idx, [sg[0] for sg in segs] + [MX + side + "Hand"], iterations=4)


def smooth_groups(mesh, sel, groups, iterations=2):
    if not sel:
        return
    me = mesh.data
    nbr = {i: set() for i in sel}
    ss = set(sel)
    for e in me.edges:
        a, b = e.vertices
        if a in ss: nbr[a].add(b)
        if b in ss: nbr[b].add(a)
    gi = {g.name: g.index for g in mesh.vertex_groups}
    def wmap(i):
        return {mesh.vertex_groups[g.group].name: g.weight for g in me.vertices[i].groups if mesh.vertex_groups[g.group].name in groups}
    for _ in range(iterations):
        new = {}
        for i in sel:
            acc = dict(wmap(i))
            for j in nbr[i]:
                for n, w in wmap(j).items():
                    acc[n] = acc.get(n, 0) + w
            tot = sum(acc.values()) or 1
            new[i] = {n: w / tot for n, w in acc.items()}
        for i, ws in new.items():
            for n in groups:
                try:
                    mesh.vertex_groups[n].remove([i])
                except RuntimeError:
                    pass
            for n, w in ws.items():
                if w > 1e-3:
                    mesh.vertex_groups[n].add([i], w, 'REPLACE')


# =================================================================================================
# 5. posing helpers: rotate a pose bone about its head, in world space
# =================================================================================================
def rot_about_head(rig, n, R3):
    pb = rig.pose.bones[n]
    h = (rig.matrix_world @ pb.matrix).translation
    Mw = rig.matrix_world @ pb.matrix
    Mw = Matrix.Translation(h) @ R3.to_4x4() @ Matrix.Translation(-h) @ Mw
    pb.matrix = rig.matrix_world.inverted() @ Mw
    bpy.context.view_layer.update()


def aim(rig, n, target_dir):
    pb = rig.pose.bones[n]
    Mw = rig.matrix_world @ pb.matrix
    cur = (Mw.to_3x3() @ Vector((0, 1, 0))).normalized()
    rot_about_head(rig, n, cur.rotation_difference(target_dir.normalized()).to_matrix())


def bone_dir(rig, n):
    pb = rig.pose.bones[n]
    return ((rig.matrix_world @ pb.matrix).to_3x3() @ Vector((0, 1, 0))).normalized()


def bake_pose_as_rest(rig, mesh):
    """Mesh takes the current pose as its shape; the pose becomes the rest; weights are kept."""
    select(mesh)
    mod = next(m for m in mesh.modifiers if m.type == 'ARMATURE')
    name = mod.name
    bpy.ops.object.modifier_apply(modifier=name)
    select(rig)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    m = mesh.modifiers.new("Armature", 'ARMATURE'); m.object = rig


def repose_arms(rig, T):
    """Arms onto the TEMPLATE's rest directions (upper arm, then forearm), so every fighter ends
    on one rest pose. Returns the angles moved."""
    moved = {}
    for side, _ in SIDES:
        for n in (side + "Arm", side + "ForeArm"):
            tgt = (T[MX + n]["tail"] - T[MX + n]["head"]).normalized()
            before = bone_dir(rig, MX + n)
            aim(rig, MX + n, tgt)
            moved[n] = round(math.degrees(before.angle(tgt)), 1)
    return moved


def palm_axis(rig, side):
    pb = rig.pose.bones[MX + side + "Hand"]
    return ((rig.matrix_world @ pb.matrix).to_3x3() @ Vector((0, 0, 1))).normalized()


GRIP_TOTAL = {"Index": 80, "Middle": 85, "Ring": 85, "Pinky": 85, "Thumb": 60}
GRIP_SPLIT = (0.45, 0.35, 0.20)                     # MCP / PIP / DIP share of the remaining curl


def grip_pose(rig):
    """Each finger curled toward the palm until its tip segment sits GRIP_TOTAL deg from the
    hand's own direction (a relaxed Tripo hand is already part-way there, so only the remainder
    is added); the thumb wraps toward the middle knuckle. Reported per finger."""
    for side, _ in SIDES:
        palm = palm_axis(rig, side)
        hdir = bone_dir(rig, MX + side + "Hand")
        for f, total in GRIP_TOTAL.items():
            n3 = MX + side + f"Hand{f}3"
            if n3 not in rig.pose.bones:
                continue
            have = math.degrees(hdir.angle(bone_dir(rig, n3))) if f != "Thumb" else 0.0
            need = max(0.0, total - have)
            REPORT.setdefault("grip_curl", {})[side + f] = {"had": round(have, 1), "added": round(need, 1)}
            angs = [need * k for k in GRIP_SPLIT]
            for i, a in zip((1, 2, 3), angs):
                n = MX + side + f"Hand{f}{i}"
                if n not in rig.pose.bones:
                    continue
                d = bone_dir(rig, n)
                if f == "Thumb":
                    # wrap: toward the palm AND toward the middle finger's knuckle
                    mk = (rig.matrix_world @ rig.pose.bones[MX + side + "HandMiddle1"].head)
                    th = (rig.matrix_world @ rig.pose.bones[n].head)
                    tgt = (palm + (mk - th).normalized()).normalized()
                    axis = d.cross(tgt)
                else:
                    axis = d.cross(palm)
                if axis.length < 1e-6:
                    continue
                rot_about_head(rig, n, Matrix.Rotation(math.radians(a), 3, axis.normalized()))


def contain_hand_bones(rig, mesh, H):
    """Grip mode only, after the bake: the hand and finger bones carry no weight, so they can be
    moved freely - pull every one of their joints inside the curled flesh, and end the hand bone
    at the middle knuckle."""
    probe = Probe(mesh, evaluated=True)
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    moved = 0
    for side, _ in SIDES:
        hb = eb[MX + side + "Hand"]
        for _ in range(12):
            if probe.inside(hb.tail) >= 4:
                break
            hb.tail = hb.head + (hb.tail - hb.head) * 0.9
        order = [eb[f"{side[0]}_Meta{i}"] for i in (1, 2, 3, 4) if f"{side[0]}_Meta{i}" in eb]
        for f in FINGERS:
            order += [eb[MX + side + f"Hand{f}{i}"] for i in (1, 2, 3, 4) if MX + side + f"Hand{f}{i}" in eb]
        for b in order * 2:
            z = b.z_axis.copy()
            for end in ("head", "tail"):
                if end == "tail" and is_leaf(b.name):
                    continue
                p = getattr(b, end)
                q, m = probe.pull_inside(p.copy(), 0.002 * H)
                if m:
                    setattr(b, end, q)
                    moved += 1
            for _ in range(3):                    # a straight bone across a curl: fix its middle
                mid = (b.head + b.tail) / 2
                q, m = probe.pull_inside(mid.copy(), 0.002 * H)
                if m:
                    b.tail = b.tail + (q - mid) * 2
                    moved += 1
                q2, m2 = probe.pull_inside(b.tail.copy(), 0.002 * H)
                if m2:
                    b.tail = q2
                if not (m or m2):
                    break
            b.align_roll(z)
            nxt = eb.get(b.name[:-1] + str(int(b.name[-1]) + 1)) if b.name[-1].isdigit() and "Hand" in b.name else None
            if nxt:
                nxt.head = b.tail.copy()
    bpy.ops.object.mode_set(mode='OBJECT')
    REPORT["hand_joints_pulled_after_grip"] = moved


def contain_helpers(rig, mesh, H):
    """Head tail and the unweighted helper bones (heels, pecs, skirt) pulled inside the mesh;
    the head tail takes HeadTop_End's head with it."""
    probe = Probe(mesh, evaluated=True)
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    hb = eb[MX + "Head"]
    z = hb.z_axis.copy()
    q, m = probe.pull_inside(hb.tail.copy(), 0.01 * H)
    if m:
        hb.tail = q
    for _ in range(4):
        mid = (hb.head + hb.tail) / 2
        q, m = probe.pull_inside(mid.copy(), 0.01 * H)
        if m:
            hb.tail = hb.tail + (q - mid) * 2
        q2, m2 = probe.pull_inside(hb.tail.copy(), 0.01 * H)
        if m2:
            hb.tail = q2
        if not (m or m2):
            break
    hb.align_roll(z)
    top = eb.get(MX + "HeadTop_End")
    if top:
        top.head = hb.tail.copy()
    for b in [b for b in eb if any(k in b.name for k in ("_Heel", "_Pec", "_Skirt"))]:
        z = b.z_axis.copy()
        for end in ("head", "tail"):
            q, m = probe.pull_inside(getattr(b, end).copy(), 0.004 * H)
            if m:
                setattr(b, end, q)
        mid = (b.head + b.tail) / 2
        q, m = probe.pull_inside(mid.copy(), 0.004 * H)
        if m:
            b.tail = b.tail + (q - mid) * 2
        b.align_roll(z)
    bpy.ops.object.mode_set(mode='OBJECT')


# --- grip mode: a measured hand frame and a hierarchical bend --------------------------------------
GRIP_JOINTS = (0.46, 0.70, 0.85)        # knuckle / middle / end joint lines, fraction of wrist->tip
GRIP_CURL = (32, 28, 15)                # degrees added at each line (a relaxed hand already has ~15)
if opts.get("--curl"):                  # a flat hand (Richard's gauntlets) needs more: --curl 55,50,30
    GRIP_CURL = tuple(float(x) for x in opts["--curl"].split(","))
THUMB_WRAP, THUMB_CURL = 45, 25         # thumb rolled across the palm, then curled at its middle


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def rot_points(P, origin, axis, ang):
    """Rotate rows of P about the line (origin, axis) by per-row angles ang (radians)."""
    k = axis / np.linalg.norm(axis)
    d = P - origin
    c, s_ = np.cos(ang)[:, None], np.sin(ang)[:, None]
    return origin + d * c + np.cross(k, d) * s_ + np.outer(d @ k, k) * (1 - c)


def measure_hand(rig, mesh, side, H):
    """Wrist, u along the hand, w toward the palm (the side the fingertips already curl to),
    v across; the hand's length to the fingertip, its lateral extent at the knuckles, and which
    side of v the thumb is on (the side whose edge reaches further out mid-hand)."""
    V = mesh_np(mesh)
    wr, _ = seg_of(rig, MX + side + "Hand")
    fa, _ = seg_of(rig, MX + side + "ForeArm")
    u = (wr - fa).normalized()
    d = V - np.asarray(wr)
    t = d @ np.asarray(u)
    r = np.linalg.norm(d - np.outer(t, u), axis=1)
    near = (t > 0) & (t < 0.16 * H) & (r < 0.07 * H)
    tip_t = float(np.percentile(t[near], 99.7))
    hand = near & (t < tip_t * 1.02)
    P = V[hand]
    c = P.mean(axis=0)
    _, ev = np.linalg.eigh(np.cov((P - c).T))
    w = Vector(ev[:, 0])
    w = (w - u * w.dot(u)).normalized()
    tt = (P - np.asarray(wr)) @ np.asarray(u)
    outer = P[tt > 0.8 * tip_t]
    base = P[(tt > 0.3 * tip_t) & (tt < 0.45 * tip_t)]
    curl = 0.0
    if len(outer) and len(base):
        curl = (Vector(outer.mean(axis=0)) - Vector(base.mean(axis=0))).dot(w) / tip_t
        if curl < 0:
            w = -w
    # A hand modelled flat (Richard's gauntlets) gives no curl to read, and the sign above is a
    # coin toss - it put his right hand's grip on the BACK of the hand. In the rest pose the
    # arms are out to the sides, palms to the floor, so fall back on that.
    if abs(curl) < 0.08:
        w = w if w.z < 0 else -w
    REPORT.setdefault("palm", {})[side] = {"curl": round(abs(curl), 3), "palm_down_rule": abs(curl) < 0.08,
                                           "w": [round(x, 3) for x in w]}
    v = u.cross(w).normalized()
    # centre of the hand's thickness and lateral extent at the knuckle line
    kn = P[np.abs(tt - GRIP_JOINTS[0] * tip_t) < 0.03 * tip_t]
    kv = (kn - np.asarray(wr)) @ np.asarray(v)
    kw = (kn - np.asarray(wr)) @ np.asarray(w)
    mid = P[np.abs(tt - 0.35 * tip_t) < 0.08 * tip_t]
    mv = (mid - np.asarray(wr)) @ np.asarray(v)
    thumb_sign = 1.0 if mv.max() - np.median(kv) > np.median(kv) - mv.min() else -1.0
    return dict(wr=wr, u=u, v=v, w=w, L=tip_t, w_mid=float(np.median(kw)),
                v_lo=float(np.percentile(kv, 3)), v_hi=float(np.percentile(kv, 97)), thumb=thumb_sign)


def grip_deform(Pts, F):
    """The grip as a pure function of position, so verts and bone joints bend identically.
    Fingers: rotate about each joint line, distal first, with a smooth ramp across the line.
    Thumb: verts on the thumb side behind the knuckles roll across the palm about the hand axis
    through the thumb base, then curl at the thumb's middle."""
    wr, u, v, w = (np.asarray(F[k]) for k in ("wr", "u", "v", "w"))
    L = F["L"]
    d = Pts - wr
    t, lv, lw = d @ u, d @ v, d @ w
    span = F["v_hi"] - F["v_lo"]
    # how much of each vertex is THUMB: on the thumb side, beyond the knuckle edge, behind the knuckles
    edge = F["v_hi"] if F["thumb"] > 0 else F["v_lo"]
    beyond = (lv - edge) * F["thumb"]
    thumbness = smoothstep((beyond + 0.10 * span) / (0.20 * span)) * smoothstep((GRIP_JOINTS[0] * L - t) / (0.08 * L)) \
        * smoothstep((t - 0.08 * L) / (0.08 * L))
    fingerness = (1 - thumbness) * (t > 0)
    out = Pts.copy()
    for jt, ang in reversed(list(zip(GRIP_JOINTS, GRIP_CURL))):
        origin = wr + u * (jt * L) + w * F["w_mid"]
        a = np.radians(ang) * smoothstep((t - jt * L) / (0.06 * L) + 0.5) * fingerness
        out = rot_points(out, origin, v, a)
    # thumb: roll across the palm about the hand axis through its base
    base = wr + u * (0.15 * L) + v * (edge - 0.15 * span * F["thumb"]) + w * F["w_mid"]
    sgn = -F["thumb"]                        # rotating about +u carries +v toward -w
    out = rot_points(out, base, u, np.radians(THUMB_WRAP) * sgn * thumbness)
    tmid = wr + u * (0.35 * L) + w * F["w_mid"]
    a = np.radians(THUMB_CURL) * thumbness * smoothstep((t - 0.35 * L) / (0.1 * L) + 0.5)
    out = rot_points(out, tmid, v, a)
    return out


def finger_lanes(F):
    """Rest joints for each finger: four lanes across the knuckle line (index on the thumb side),
    the thumb along the thumb edge; joints at the grip lines, in the middle of the hand's thickness."""
    wr, u, v, w, L = F["wr"], F["u"], F["v"], F["w"], F["L"]
    lanes = np.linspace(F["v_hi"], F["v_lo"], 4) if F["thumb"] > 0 else np.linspace(F["v_lo"], F["v_hi"], 4)
    span = F["v_hi"] - F["v_lo"]
    lanes = lanes - np.sign(lanes - (F["v_hi"] + F["v_lo"]) / 2) * 0.08 * span
    out = {}
    length = {"Index": 0.90, "Middle": 1.0, "Ring": 0.93, "Pinky": 0.72}   # of the middle finger, past the knuckle
    for f, lv in zip(FINGERS[1:], lanes):
        k0 = GRIP_JOINTS[0]
        ks = [k0] + [k0 + (k - k0) * length[f] for k in GRIP_JOINTS[1:]] + [k0 + (0.96 - k0) * length[f]]
        out[f] = [wr + u * (k * L) + v * lv + w * F["w_mid"] for k in ks]
    edge = F["v_hi"] if F["thumb"] > 0 else F["v_lo"]
    tv = edge + 0.05 * span * F["thumb"]
    out["Thumb"] = [wr + u * (k * L) + v * (tv * (0.6 + 0.4 * k / 0.62)) + w * F["w_mid"] for k in (0.12, 0.30, 0.46, 0.62)]
    return out


GRIP_F = {}   # side -> the measured hand frame, kept for seating the weapon in the fist


def grip_bake(rig, mesh, side, H):
    """Bend the hand into the grip, write it to the mesh, and lay the finger bones along the
    same bent lanes so they end up inside the curled fingers."""
    F = measure_hand(rig, mesh, side, H)
    GRIP_F[side] = F
    me = mesh.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(mesh.matrix_world)
    Wco = co @ M[:3, :3].T + M[:3, 3]
    d = Wco - np.asarray(F["wr"])
    t = d @ np.asarray(F["u"])
    r = np.linalg.norm(d - np.outer(t, F["u"]), axis=1)
    sel = (t > -0.01 * H) & (t < 1.1 * F["L"]) & (r < 0.08 * H)
    Wco[sel] = grip_deform(Wco[sel], F)
    Minv = np.linalg.inv(M)
    co = Wco @ Minv[:3, :3].T + Minv[:3, 3]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    lanes = finger_lanes(F)
    bent = {f: [Vector(p) for p in grip_deform(np.array([list(q) for q in pts]), F)] for f, pts in lanes.items()}
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    palm = F["w"]
    for f, pts in bent.items():
        for i in range(3):
            b = eb[MX + side + f"Hand{f}{i + 1}"]
            b.use_connect = False
            b.head, b.tail = pts[i], pts[i + 1]
            b.align_roll(palm)
        end = eb.get(MX + side + f"Hand{f}4")
        if end:
            dd = pts[3] - pts[2]
            end.head, end.tail = pts[3], pts[3] + dd.normalized() * 0.003 * H
            end.align_roll(palm)
    for i, f in enumerate(FINGERS[1:]):                     # palm (Meta) bones: wrist -> knuckle
        b = eb.get(f"{side[0]}_Meta{i + 1}")
        if b:
            b.head, b.tail = F["wr"] + F["u"] * (0.1 * F["L"]) + (bent[f][0] - F["wr"]) * 0.2, bent[f][0]
            b.align_roll(palm)
    bpy.ops.object.mode_set(mode='OBJECT')
    REPORT.setdefault("grip_frame", {})[side] = {"hand_len_pct_H": round(F["L"] / H * 100, 2),
                                                  "thumb_side": F["thumb"], "verts_bent": int(sel.sum())}


def grip_weights(rig, mesh, side):
    """Everything past the wrist plane -> 100% hand bone; finger bones keep zero weight."""
    idx, wr, u = hand_verts(rig, mesh, side)
    names = [g.name for g in mesh.vertex_groups]
    for i in idx:
        for n in names:
            try:
                mesh.vertex_groups[n].remove([i])
            except RuntimeError:
                pass
        mesh.vertex_groups[MX + side + "Hand"].add([i], 1.0, 'REPLACE')
    return len(idx)


# =================================================================================================
# 6. markers mode
# =================================================================================================
def MK(side, n):
    return f"MK_{side[0]}_{n}"


def drop_markers(rig, mesh):
    """Run 1: 12 empties on the current guesses (wrist + 5 tips per hand), to be snapped by hand."""
    for side, _ in SIDES:
        wr, _ = seg_of(rig, MX + side + "Hand")
        pts = {"Wrist": wr}
        for f in FINGERS:
            pts[f] = seg_of(rig, MX + side + f"Hand{f}3")[1]
        for n, p in pts.items():
            e = bpy.data.objects.new(MK(side, n), None)
            e.empty_display_type = 'SPHERE'
            e.empty_display_size = 0.006
            e.location = p
            bpy.context.scene.collection.objects.link(e)
    log("markers dropped: snap MK_L_*/MK_R_* onto the wrist centre and each fingertip, save, "
        "then run with --markers-run 2")


# fractions of wrist->tip where the three joints sit (metacarpal ~46% of the hand)
FRACS = {"Thumb": (0.22, 0.50, 0.76), "Index": (0.47, 0.69, 0.85), "Middle": (0.46, 0.68, 0.85),
         "Ring": (0.47, 0.69, 0.86), "Pinky": (0.50, 0.72, 0.87)}


def build_from_markers(rig, mesh, H):
    """Run 2: three joints per finger between the wrist marker and its tip marker, each centred on
    the finger's own centreline: a vertex votes for the finger whose wrist->tip line is nearest,
    so neighbouring fingers never pull a joint between them."""
    V = mesh_np(mesh)
    probe = Probe(mesh)
    select(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    for side, _ in SIDES:
        W = bpy.data.objects[MK(side, "Wrist")].matrix_world.translation.copy()
        tips = {f: bpy.data.objects[MK(side, f)].matrix_world.translation.copy() for f in FINGERS}
        near = np.linalg.norm(V - np.asarray(W), axis=1) < max((t - W).length for t in tips.values()) * 1.1
        Vh = V[near]
        dists = np.stack([[dist_seg(Vector(p), W, tips[f]) for p in Vh] for f in FINGERS])
        owner = np.argmin(dists, axis=0)
        palm = eb[MX + side + "Hand"].z_axis.copy()
        eb[MX + side + "Hand"].head = W
        for fi, f in enumerate(FINGERS):
            P = tips[f]
            u = (P - W).normalized()
            mine = Vh[owner == fi]
            joints = []
            for fr in FRACS[f]:
                c = W.lerp(P, fr)
                s = slab(mine, c, np.asarray(u), 0.004 * H, 0.02 * H) if len(mine) else []
                p = Vector(np.median(s, axis=0)) if len(s) > 5 else c
                p, _ = probe.pull_inside(p, 0.0025 * H)
                joints.append(p)
            joints.append(P)
            chain = [MX + side + f"Hand{f}{i}" for i in (1, 2, 3, 4)]
            for i, n in enumerate(chain[:3]):
                b = eb[n]
                b.use_connect = False
                b.head, b.tail = joints[i], joints[i + 1]
                b.align_roll(palm)
            if chain[3] in eb:
                b = eb[chain[3]]
                d = joints[3] - joints[2]
                b.head, b.tail = joints[3], joints[3] + d.normalized() * max(d.length * 0.5, 0.002 * H)
                b.align_roll(palm)
    bpy.ops.object.mode_set(mode='OBJECT')
    for o in [o for o in bpy.data.objects if o.name.startswith("MK_")]:
        bpy.data.objects.remove(o)


# =================================================================================================
# 7. weapons
# =================================================================================================
def head_frame(armobj, bone):
    return armobj.matrix_world @ armobj.pose.bones[bone].matrix


def tail_frame(armobj, bone):
    pb = armobj.pose.bones[bone]
    return armobj.matrix_world @ pb.matrix @ Matrix.Translation(Vector((0, pb.bone.length, 0)))


def seat_weapons(rig):
    """The CURRENT body's weapons, re-seated with the same bone-local placement on the new hand.
    With --shield-blend, the shield's placement comes from that studio file instead."""
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=CURRENT_FBX)
    added = only_new(before)
    old_rig = next(o for o in added if o.type == 'ARMATURE')
    old_rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    weapons = [o for o in added if o.type == 'MESH' and not any(m.type == 'ARMATURE' for m in o.modifiers)]
    rel = {}
    for w in weapons:
        hand = MX + ("LeftHand" if w.name.endswith("_L") else "RightHand")
        rel[w.name] = (hand, head_frame(old_rig, hand).inverted() @ w.matrix_world)
    if opts["--shield-blend"]:
        with bpy.data.libraries.load(opts["--shield-blend"], link=False) as (src, dst):
            dst.objects = [n for n in src.objects if n.endswith("_Rig") or n.endswith("_Shield_L")]
        coll = bpy.data.collections.new("studio_tmp")
        bpy.context.scene.collection.children.link(coll)
        for o in dst.objects:
            coll.objects.link(o)
        srig = next(o for o in dst.objects if o.type == 'ARMATURE')
        sshield = next(o for o in dst.objects if o.name.startswith(NAME) and "_Shield_L" in o.name)
        srig.data.pose_position = 'REST'
        bpy.context.view_layer.update()
        key = next(n for n in rel if n.endswith("_Shield_L"))
        rel[key] = (MX + "LeftHand", head_frame(srig, sshield.parent_bone).inverted() @ sshield.matrix_world)
        REPORT["shield_from_studio"] = {"bone": sshield.parent_bone,
                                        "loc": [round(x, 4) for x in sshield.location],
                                        "rot": [round(math.degrees(x), 2) for x in sshield.rotation_euler]}
        for o in list(coll.objects):
            bpy.data.objects.remove(o)
        bpy.data.collections.remove(coll)
    for w in weapons:
        hand, m = rel[w.name]
        w.parent = rig
        w.parent_type = 'BONE'
        w.parent_bone = hand
        w.matrix_parent_inverse = Matrix.Identity(4)
        bpy.context.view_layer.update()
        w.matrix_world = head_frame(rig, hand) @ m
        bpy.context.view_layer.update()
    for o in added:
        if o not in weapons:
            bpy.data.objects.remove(o)
    REPORT["weapons"] = [w.name for w in weapons]
    return weapons


def weapon_grip(w, hint):
    """The weapon's GRIP, measured on its own mesh: along the weapon's long axis, the stretch
    next to the hand hint whose MEDIAN radius is smallest over a hand's width. Medians, and then
    a radius cut, so a tassel or feather hanging beside the hilt does not count as grip.
    Returns (centre, axis pointing grip->blade, grip length)."""
    co = np.array([list(w.matrix_world @ v.co) for v in w.data.vertices])
    c = co.mean(axis=0)
    _, ev = np.linalg.eigh(np.cov((co - c).T))
    ax = ev[:, 2]
    t = (co - c) @ ax
    rad = np.linalg.norm((co - c) - np.outer(t, ax), axis=1)
    th = float((np.asarray(hint) - c) @ ax)       # the hand on the current seat: the hilt is near it
    span = t.max() - t.min()
    blade_end = 1.0 if np.percentile(t, 95) - np.median(t) > np.median(t) - np.percentile(t, 5) else -1.0
    win = 0.09 * span / 0.55 if span > 0 else 0.09
    best = None
    for c0 in np.linspace(t.min() + win / 2, t.max() - win / 2, 120):
        m = np.abs(t - c0) < win / 2
        if m.sum() < 30 or abs(c0 - th) > 1.2 * win:
            continue
        # the grip sits on the HILT side of the guard: the dense part of the weapon
        r = float(np.median(rad[m]))
        dens = m.sum()
        score = r - 1e-6 * dens
        if best is None or score < best[0]:
            best = (score, c0, r)
    _, c0, r = best
    m = (np.abs(t - c0) < win / 2) & (rad < r * 1.6)
    g = co[m]
    gc = g.mean(axis=0)
    _, ev2 = np.linalg.eigh(np.cov((g - gc).T))
    gax = ev2[:, 2]
    if gax @ ax * blade_end < 0:
        gax = -gax
    return Vector(gc), Vector(gax), float(win), float(r)


def fist_channel(mesh, F, H):
    """The hollow inside the curled fingers: sample points between palm and fingertips that are
    OUTSIDE the flesh but enclosed by it (rays across the palm normal both hit the hand, and the
    fingers wrap in front), and keep the one farthest from any surface."""
    probe = Probe(mesh, evaluated=True)
    wr, u, v, w, L = F["wr"], F["u"], F["v"], F["w"], F["L"]
    mid_v = (F["v_lo"] + F["v_hi"]) / 2
    best = None
    for a in np.linspace(0.2, 0.75, 12):
        for b in np.linspace(-0.05, 0.6, 14):
            p = wr + u * (a * L) + v * mid_v + w * (F["w_mid"] + b * L)
            if probe.inside(p) >= 4:
                continue
            hits = 0
            for d in (w, -w, u):
                h = probe.bvh.ray_cast(p, d, 0.6 * L)
                hits += h[0] is not None
            if hits < 3:
                continue
            _, _, _, dist = probe.bvh.find_nearest(p)
            if best is None or dist > best[0]:
                best = (dist, p)
    if best is None:
        return wr + u * (GRIP_JOINTS[0] * L) + w * (F["w_mid"] + 0.3 * L), 0.0
    return best[1], best[0]


def palm_seat(mesh, F, grad):
    """Where a haft of radius grad lies when held: across the palm just behind the knuckle line,
    its axis one radius off the PALM SKIN (measured on the baked mesh, before the fingers' tips
    that now curl in front of it), centred across the hand."""
    V = mesh_np(mesh)
    wr, u, v, w = (np.asarray(F[k]) for k in ("wr", "u", "v", "w"))
    L = F["L"]
    d = V - wr
    t, lv, lw = d @ u, d @ v, d @ w
    at = GRIP_SEAT_T * L
    band = (np.abs(t - at) < 0.05 * L) & (lv > F["v_lo"]) & (lv < F["v_hi"]) & (lw > F["w_mid"] - 0.3 * L) & (lw < F["w_mid"] + 0.15 * L)
    # the palm skin: the palm-side surface of the hand's own thickness at that station
    palm_w = float(np.percentile(lw[band], 90)) if band.sum() > 10 else F["w_mid"] + 0.1 * L
    mid_v = (F["v_lo"] + F["v_hi"]) / 2
    centre = Vector(wr + u * at + v * mid_v + w * (palm_w + grad * 1.05))
    return centre, palm_w


GRIP_SEAT_T = 0.40       # haft station along wrist->fingertip: the base of the fingers


def index_loop(mesh, F, H):
    """The hollow the curled INDEX finger and the thumb close around: a cross-section of the
    hand through the index lane (a thin slab across v), and in that section the point farthest
    from the flesh that the flesh surrounds on at least 3/4 of the compass. Returns the point
    (t along u, w along the palm normal), its clearance radius, and the index lane's v."""
    V = mesh_np(mesh)
    wr, u, v, w = (np.asarray(F[k]) for k in ("wr", "u", "v", "w"))
    L = F["L"]
    lanes = finger_lanes(F)
    v_idx = float((np.asarray(lanes["Index"][0]) - wr) @ v)
    span = F["v_hi"] - F["v_lo"]
    d = V - wr
    t, lv, lw = d @ u, d @ v, d @ w
    sec = (np.abs(lv - v_idx) < span / 9) & (t > -0.05 * L) & (t < 1.1 * L) & (np.abs(lw - F["w_mid"]) < 0.8 * L)
    P2 = np.stack([t[sec], lw[sec]], axis=1)
    best = None
    for ct in np.linspace(0.2, 0.9, 36) * L:
        for cw in np.linspace(F["w_mid"] - 0.1 * L, F["w_mid"] + 0.7 * L, 36):
            q = np.array([ct, cw])
            dd = P2 - q
            dist = np.linalg.norm(dd, axis=1)
            r = float(dist.min())
            near = dist < 0.45 * L
            if near.sum() < 10:
                continue
            ang = np.sort(np.arctan2(dd[near, 1], dd[near, 0]))
            gap = np.max(np.diff(np.concatenate([ang, ang[:1] + 2 * np.pi])))
            if gap > np.pi / 2:                     # open on more than a quarter: not enclosed
                continue
            if best is None or r > best[0]:
                best = (r, ct, cw)
    return best, v_idx


def seat_in_loop(rig, mesh, weapon, side, H):
    """Keep the weapon's direction exactly as seated, and only MOVE it: the grip's axis through
    the hollow of the curled index finger and thumb, the grip running from the index side across
    the palm, its guard end just past the index finger."""
    F = GRIP_F[side]
    loop, v_idx = index_loop(mesh, F, H)
    gc, gax, glen, grad = weapon_grip(weapon, seg_of(rig, MX + side + "Hand")[0])
    if loop is None:
        log("no enclosed index loop found; weapon left where seat_in_fist put it")
        return
    r, ct, cw = loop
    wr, u, v, w = F["wr"], F["u"], F["v"], F["w"]
    # the grip's blade-side end sits a quarter finger-width past the index lane, toward the thumb
    span = F["v_hi"] - F["v_lo"]
    v_end = v_idx + F["thumb"] * span / 16
    blade_dir = gax                                  # grip -> blade
    target_end = wr + u * ct + w * cw + v * v_end
    grip_end = gc + blade_dir * (glen / 2)
    shift = target_end - grip_end
    weapon.matrix_world = Matrix.Translation(shift) @ weapon.matrix_world
    bpy.context.view_layer.update()
    REPORT.setdefault("weapon_seat", {}).setdefault(weapon.name, {}).update({
        "index_loop_clearance": round(r, 4), "grip_radius": round(grad, 4), "loop_shift": round(shift.length, 4)})
    log("in the index/thumb loop:", weapon.name, REPORT["weapon_seat"][weapon.name])


def seat_in_fist(rig, mesh, weapon, side, H):
    """Rotate and move the weapon so its GRIP runs across the palm through the fist's hollow,
    the blade leaving on the thumb side and a curved blade's edge facing the knuckles."""
    F = GRIP_F[side]
    gc, gax, glen, grad = weapon_grip(weapon, seg_of(rig, MX + side + "Hand")[0])
    centre, palm_w = palm_seat(mesh, F, grad)
    clearance = grad
    target_ax = (F["v"] * F["thumb"]).normalized()          # pommel -> blade, out past the thumb
    R1 = gax.rotation_difference(target_ax).to_matrix().to_4x4()
    # roll about the grip so the blade's curve puts the edge toward the knuckles (+u)
    co = np.array([list(weapon.matrix_world @ v.co) for v in weapon.data.vertices])
    tip = Vector(co[np.argmax((co - np.asarray(gc)) @ np.asarray(gax))])
    bend = (tip - gc) - gax * (tip - gc).dot(gax)            # toward the spine side
    bend = (R1.to_3x3() @ bend)
    roll = 0.0
    if bend.length > 1e-4:
        spine = bend.normalized()
        want = -F["u"]                                       # spine away from the knuckles
        want = (want - target_ax * want.dot(target_ax)).normalized()
        roll = spine.angle(want)
        if spine.cross(want).dot(target_ax) < 0:
            roll = -roll
    R2 = Matrix.Rotation(roll, 4, target_ax)
    M = Matrix.Translation(centre) @ R2 @ R1 @ Matrix.Translation(-gc)
    before = weapon.matrix_world.copy()
    weapon.matrix_world = M @ before
    bpy.context.view_layer.update()
    REPORT.setdefault("weapon_seat", {})[weapon.name] = {
        "grip_len": round(glen, 4), "grip_radius": round(grad, 4),
        "turned_deg": round(math.degrees(gax.angle(target_ax)), 1), "roll_deg": round(math.degrees(roll), 1),
        "moved": round((before.translation - weapon.matrix_world.translation).length, 4)}
    log("seated", weapon.name, REPORT["weapon_seat"][weapon.name])


# =================================================================================================
# 8. checks
# =================================================================================================
def checks(rig, mesh, H):
    W = weights_of(mesh)
    zero = [i for i, w in enumerate(W) if sum(x for _, x in w) < 1e-4]
    finger_w = sum(1 for w in W for n, x in w if is_finger(n) and x > 1e-4)
    probe = Probe(mesh, evaluated=True)
    outside = []
    for b in rig.data.bones:
        h, t = seg_of(rig, b.name)
        if b.name == MX + "HeadTop_End":
            continue
        pts = {"head": h, "mid": (h + t) / 2}
        if not is_leaf(b.name):
            pts["tail"] = t
        for k_, p in pts.items():
            v = probe.inside(p)
            if v < 4:
                _, _, _, d = probe.bvh.find_nearest(p)
                outside.append({"bone": b.name, "at": k_, "votes": v, "dist_pct_H": round(d / H * 100, 2)})
    REPORT["zero_weight_verts"] = len(zero)
    REPORT["verts_on_finger_bones"] = finger_w
    REPORT["outside"] = outside
    log(f"zero-weight verts {len(zero)}, verts weighted to finger bones {finger_w}, "
        f"bone points outside the mesh {len(outside)}")
    for o in outside:
        log("   OUTSIDE", o)


# =================================================================================================
# 9. renders
# =================================================================================================
COLOR = {"mode": 'TEXTURE'}


def setup_render():
    sc = bpy.context.scene
    try:
        sc.render.engine = 'BLENDER_WORKBENCH'
    except TypeError as e:
        log(e)
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = COLOR["mode"]
    cam = bpy.data.objects.get("rcam") or bpy.data.objects.new("rcam", bpy.data.cameras.new("rcam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'
    return sc, cam


def shot(name, center, size, view_dir, res=(720, 820), xray=False):
    sc, cam = setup_render()
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.display.shading.show_xray = xray
    sc.display.shading.xray_alpha = 0.35
    cam.data.ortho_scale = size
    cam.location = center - view_dir.normalized() * 10
    cam.rotation_euler = view_dir.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(RENDERS, name + ".png")
    bpy.ops.render.render(write_still=True)


def bone_sticks(rig, H):
    """A thin red stick per bone, so the skeleton shows in an x-ray render."""
    verts, faces = [], []
    for b in rig.data.bones:
        h, t = seg_of(rig, b.name)
        d = t - h
        if d.length < 1e-6:
            continue
        r = min(0.004 * H, d.length * 0.12)
        a = d.normalized().orthogonal().normalized() * r
        c = d.normalized().cross(a)
        m = h + d * 0.2
        base = len(verts)
        verts += [h, m + a, m + c, m - a, m - c, t]
        faces += [(base, base + 1 + i, base + 1 + (i + 1) % 4) for i in range(4)]
        faces += [(base + 5, base + 1 + (i + 1) % 4, base + 1 + i) for i in range(4)]
    me = bpy.data.meshes.new("sticks")
    me.from_pydata(verts, [], faces)
    o = bpy.data.objects.new("sticks", me)
    o.color = (1, 0.1, 0.1, 1)
    bpy.context.scene.collection.objects.link(o)
    return o


def reset_pose(rig):
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def body_box(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in objs:
        ev = o.evaluated_get(dg)
        for c in ev.bound_box:
            p = ev.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return lo, hi


def full_shots(prefix, objs, xray=False):
    lo, hi = body_box(objs)
    c = (lo + hi) / 2
    s = max(hi - lo) * 1.08
    shot(prefix + "_front", c, s, Vector((0, 1, 0)), xray=xray)
    shot(prefix + "_side", c, s, Vector((-1, 0, 0)), xray=xray)


def test_renders(rig, mesh, weapons, H):
    objs = [mesh] + weapons
    reset_pose(rig)
    sticks = bone_sticks(rig, H)
    COLOR["mode"] = 'OBJECT'
    mesh.color = (0.75, 0.75, 0.78, 1)
    full_shots("1_rest_xray_bones", [mesh, sticks], xray=True)
    COLOR["mode"] = 'TEXTURE'
    bpy.data.objects.remove(sticks)
    full_shots("1_rest", objs)
    # arms raised overhead
    for side, s in SIDES:
        aim(rig, MX + side + "Arm", Vector((0.25 * s, 0.0, 1.0)))
        aim(rig, MX + side + "ForeArm", Vector((0.15 * s, -0.1, 1.0)))
    full_shots("2_arms_raised", objs)
    reset_pose(rig)
    # deep squat: thighs forward, shins back, torso leaning in, hips lowered onto the feet
    ank0 = min(seg_of(rig, MX + s + "Foot")[0].z for s, _ in SIDES)
    for side, _ in SIDES:
        rot_about_head(rig, MX + side + "UpLeg", Matrix.Rotation(math.radians(-100), 3, 'X'))
        aim(rig, MX + side + "Leg", Matrix.Rotation(math.radians(25), 3, 'X') @ Vector((0, 0, -1)))
        aim(rig, MX + side + "Foot", Vector((0, -0.6, -0.8)))
    rot_about_head(rig, MX + "Spine", Matrix.Rotation(math.radians(30), 3, 'X'))
    for side, s in SIDES:
        aim(rig, MX + side + "Arm", Vector((0.2 * s, -1.0, -0.1)))
    for _ in range(3):
        pb = rig.pose.bones[MX + "Hips"]
        ank = min((rig.matrix_world @ rig.pose.bones[MX + s + "Foot"].head).z for s, _ in SIDES)
        Mw = Matrix.Translation(Vector((0, 0, -(ank - ank0)))) @ rig.matrix_world @ pb.matrix
        pb.matrix = rig.matrix_world.inverted() @ Mw
        bpy.context.view_layer.update()
    full_shots("3_deep_squat", objs)
    reset_pose(rig)
    # wrists rotated 90 degrees about the forearm axis
    for side, _ in SIDES:
        rot_about_head(rig, MX + side + "Hand", Matrix.Rotation(math.radians(90), 3, bone_dir(rig, MX + side + "ForeArm")))
    full_shots("4_wrists_90", objs)
    for side, s in SIDES:
        c = seg_of(rig, MX + side + "Hand")[0] + bone_dir(rig, MX + side + "ForeArm") * 0.05 * H
        shot(f"4_wrist90_{side}", c, 0.22 * H, Vector((0, 1, 0)), res=(600, 600))
    reset_pose(rig)
    if "Right" in GRIP_F:
        F = GRIP_F["Right"]
        c = F["wr"] + F["u"] * (0.45 * F["L"])
        for view, d in (("haft", F["v"] * F["thumb"]), ("haft_rev", -F["v"] * F["thumb"]),
                        ("fingers", -F["u"]), ("top", -F["w"])):
            shot(f"8_weapon_hand_{view}", c, 0.18 * H, d, res=(700, 700))
    # hands close up, front and side (and from below, where a grip shows)
    for side, s in SIDES:
        wr, _ = seg_of(rig, MX + side + "Hand")
        c = wr + bone_dir(rig, MX + side + "Hand") * 0.05 * H
        for view, d in (("front", Vector((0, 1, 0))), ("side", Vector((0, 0, -1))), ("below", Vector((0, 0, 1)))):
            shot(f"5_hand_{side}_{view}", c, 0.17 * H, d, res=(600, 600))
    reset_pose(rig)


def hand_debug(rig, mesh, H, prefix):
    sticks = bone_sticks(rig, H * 0.35)
    COLOR["mode"] = 'OBJECT'
    mesh.color = (0.75, 0.75, 0.78, 1)
    for side, s in SIDES:
        wr, _ = seg_of(rig, MX + side + "Hand")
        c = wr + bone_dir(rig, MX + side + "Hand") * 0.05 * H
        for view, d in (("front", Vector((0, 1, 0))), ("below", Vector((0, 0, 1)))):
            shot(f"{prefix}_{side}_{view}", c, 0.17 * H, d, res=(600, 600), xray=True)
    COLOR["mode"] = 'TEXTURE'
    bpy.data.objects.remove(sticks)


def shield_renders(rig, mesh, weapons, H):
    """Buwaka: the shield in a guard pose and an attack pose."""
    objs = [mesh] + weapons
    aim(rig, MX + "LeftArm", Vector((0.35, -1.0, -0.35)))
    aim(rig, MX + "LeftForeArm", Vector((-0.6, -0.5, 0.6)))
    aim(rig, MX + "RightArm", Vector((-0.5, -0.2, -0.8)))
    full_shots("6_shield_guard", objs)
    lo, hi = body_box(objs)
    shot("6_shield_guard_34", (lo + hi) / 2, max(hi - lo) * 1.08, Vector((0.6, 1, -0.15)))
    reset_pose(rig)
    aim(rig, MX + "RightArm", Vector((-0.15, -1.0, 0.05)))
    aim(rig, MX + "RightForeArm", Vector((0.0, -1.0, 0.0)))
    aim(rig, MX + "LeftArm", Vector((0.4, -0.5, -0.75)))
    aim(rig, MX + "LeftForeArm", Vector((0.1, -1.0, 0.2)))
    rot_about_head(rig, MX + "Spine", Matrix.Rotation(math.radians(20), 3, 'Z'))
    full_shots("7_shield_attack", objs)
    lo, hi = body_box(objs)
    shot("7_shield_attack_34", (lo + hi) / 2, max(hi - lo) * 1.08, Vector((0.6, 1, -0.15)))
    reset_pose(rig)


# =================================================================================================
# 10. export
# =================================================================================================
def export(rig, mesh, weapons):
    for o in [o for o in bpy.data.objects if o not in [rig, mesh] + weapons]:
        bpy.data.objects.remove(o)
    rig.data.pose_position = 'POSE'
    select(*([rig, mesh] + weapons), active=rig)
    bpy.ops.export_scene.fbx(
        filepath=STEM + ".fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
    )
    bpy.ops.wm.save_as_mainfile(filepath=STEM + ".blend")
    log("exported", STEM + ".fbx", f"({os.path.getsize(STEM + '.fbx') // 1024} KB) and .blend")


# =================================================================================================
# main
# =================================================================================================
def finish(rig, mesh, H):
    if HANDS == "grip":
        for side, _ in SIDES:
            REPORT.setdefault("grip_hand_verts", {})[side] = grip_weights(rig, mesh, side)
        if not KEEP:
            contain_hand_bones(rig, mesh, H)
    else:
        build_from_markers(rig, mesh, H)
        for side, _ in SIDES:
            finger_weights(rig, mesh, side, H)
        select(mesh)
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    if not KEEP:
        contain_helpers(rig, mesh, H)
    weapons = seat_weapons(rig)
    if HANDS == "grip" and not KEEP:
        for w in weapons:
            if w.name.endswith("_R"):
                seat_in_fist(rig, mesh, w, "Right", H)
                seat_in_loop(rig, mesh, w, "Right", H)
    checks(rig, mesh, H)
    test_renders(rig, mesh, weapons, H)
    if any(w.name.endswith("_Shield_L") for w in weapons):
        shield_renders(rig, mesh, weapons, H)
    json.dump(REPORT, open(os.path.join(RENDERS, "report.json"), "w"), indent=1, default=str)
    export(rig, mesh, weapons)
    log("HD-RIG-OK")


if HANDS == "markers" and opts["--markers-run"] == "2":
    bpy.ops.wm.open_mainfile(filepath=MARKER_BLEND)
    rig = bpy.data.objects[f"{NAME}_Rig"]
    mesh = bpy.data.objects[f"{NAME}_Body"]
    H = float(rig["hd_H"])
    REPORT.update(json.loads(rig["hd_report"]))
    finish(rig, mesh, H)
else:
    rig, T, dv = load_template()
    mesh = load_mesh(float(dv[:, 2].max() - dv[:, 2].min()))
    J, H = fit_joints(T, dv, mesh)
    apply_joints(rig, T, J)
    bind(rig, mesh, H)
    full_shots("0_before_native_pose", [mesh])
    hand_debug(rig, mesh, H, "0_dbg_fit")
    if KEEP:
        K, keep_k, keep_off = keep_targets(mesh)
        REPORT["conform"] = conform(rig, K)
        REPORT["arm_repose_deg"] = {n: REPORT["conform"].get(MX + n) for n in ("LeftArm", "RightArm", "LeftForeArm", "RightForeArm")}
    else:
        REPORT["arm_repose_deg"] = repose_arms(rig, T)
    bake_pose_as_rest(rig, mesh)
    if HANDS == "grip":
        hand_debug(rig, mesh, H, "0_dbg_open")
        for side, _ in SIDES:
            grip_bake(rig, mesh, side, H)
    full_shots("0_after_reference_rest", [mesh])
    hand_debug(rig, mesh, H, "0_dbg_grip")
    log("arms re-posed onto the template rest:", REPORT["arm_repose_deg"])
    if KEEP:
        rig, H = swap_rig(rig, mesh, H, keep_k, keep_off)
        log("skinned to the ORIGINAL skeleton (--keep-rig)")
    if HANDS == "markers":
        drop_markers(rig, mesh)
        rig["hd_H"] = H
        rig["hd_report"] = json.dumps(REPORT, default=str)
        bpy.ops.wm.save_as_mainfile(filepath=MARKER_BLEND)
        log("saved", MARKER_BLEND, "- snap the markers, then re-run with --markers-run 2")
    else:
        finish(rig, mesh, H)
