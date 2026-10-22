"""Give a Tripo hand real finger bones, traced through the hand mesh it already has.

Bjorn and Miyamoto came off Tripo with 41 bones that stop at `L_Hand`: a rigid mitt. Both
carry a two-handed weapon, so the off hand has to close on the haft and could not.

The fingers ARE modelled - they only fuse into the palm below the last knuckle - so the bones
are traced from the geometry rather than guessed: find the fingertip clusters where the mesh
separates, give each finger the lateral band it owns, walk that band's centroid from the palm
out to the tip, and lay three phalanges along that path by arc length. Because the path is the
flesh's own centre line, a curled finger gets curled bones and they stay inside the mesh.

Weights are redistributed from `*_Hand`, never rebuilt: every vertex keeps the total weight it
had, split between the two nearest bones, so the hand deforms as before until something bends
a finger.

  blender -b --python add_fingers.py -- <in.fbx> <out.fbx> <L|R|LR> [apply]
"""
import bpy, sys, math, itertools
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, DST, SIDES = argv[0], argv[1], argv[2]
APPLY = "apply" in argv[3:]

PALM_FRAC = 0.55                      # knuckles sit this far along wrist->fingertip
PHAL      = (0.00, 0.45, 0.75, 1.00)  # proximal / intermediate / distal stations
SLICES    = 14
NAMES     = ("Pinky", "Ring", "Middle", "Index")   # ordered away from the thumb

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm  = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.data.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
M, mw = arm.matrix_world, mesh.matrix_world
Mi = M.inverted()
zs = [(M @ b.head_local).z for b in arm.data.bones] + [(M @ b.tail_local).z for b in arm.data.bones]
H = max(zs) - min(zs)

bvh = BVHTree.FromObject(mesh, bpy.context.evaluated_depsgraph_get())
mwi = mw.inverted()
DIRS = [Vector(d) for d in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))]

def inside(p):
    q, v = mwi @ p, 0
    for d in DIRS:
        n, o = 0, q + d * 1e-5
        while True:
            h = bvh.ray_cast(o, d)
            if h[0] is None: break
            n += 1; o = h[0] + d * 1e-5
            if n > 60: break
        v += n % 2
    return v

def trace(points, t_of, lo, hi, slices):
    """centroid path through a vertex cloud, sliced along a scalar t"""
    path = []
    for s in range(slices + 1):
        a = lo + (hi - lo) * s / slices
        b = lo + (hi - lo) * (s + 1) / slices
        sl = [p for p in points if a <= t_of(p) < b + 1e-9]
        if sl: path.append(sum(sl, Vector()) / len(sl))
    return path

def pull_inside(p, depth):
    """Move a joint to a point that is provably inside the mesh.

    Tracing gets the shape right but cannot guarantee containment on a curled finger, and
    containment is the whole point of this pass: a bone outside the flesh drives it from
    nowhere. The nearest surface point gives an inward normal; step along it until six
    rays agree we are in, and keep the deepest such point.
    """
    if inside(p) >= 4: return p, True
    loc, nor, _, _ = bvh.find_nearest(mwi @ p)
    if loc is None: return p, False
    s = mw @ loc
    n = (mw.to_3x3() @ nor).normalized()
    best = None
    for sgn in (-1.0, 1.0):
        for f in (0.25, 0.5, 0.8, 1.2, 1.8):
            q = s + n * (sgn * depth * f)
            if inside(q) >= 4: best = q
        if best is not None: break
    return (best, True) if best is not None else (p, False)

def track(tip_pts, cloud, t_of, t_lo, steps, grab=1.7):
    """Walk a finger from its tip down to the knuckles, following its own flesh.

    These hands are curled claws, so a slice taken at one station along the hand axis
    holds the finger AND the palm underneath it, and their joint centroid lands in the
    gap between the two - which is how the first attempt put bones outside the mesh.
    Anchoring each step on the previous one keeps the path on the finger: only vertices
    within one finger-radius of where we already are can contribute.
    """
    c = sum(tip_pts, Vector()) / len(tip_pts)
    rad = grab * (sum((p - c).length for p in tip_pts) / len(tip_pts))
    t_hi = t_of(c)
    if t_hi <= t_lo: return [c]
    path, slab = [c], (t_hi - t_lo) / steps
    for s in range(1, steps + 1):
        tt = t_hi + (t_lo - t_hi) * s / steps
        prev = path[-1]
        sl = [p for p in cloud
              if abs(t_of(p) - tt) < slab and (p - prev).length < rad]
        if len(sl) >= 3:
            path.append(sum(sl, Vector()) / len(sl))
    path.reverse()                      # knuckle -> tip
    return path

def along(path, f):
    """point at arc-length fraction f of a polyline"""
    seg = [(path[i+1] - path[i]).length for i in range(len(path) - 1)]
    tot = sum(seg)
    if tot < 1e-9: return path[0]
    want, acc = f * tot, 0.0
    for i, s in enumerate(seg):
        if acc + s >= want or i == len(seg) - 1:
            r = (want - acc) / s if s > 1e-9 else 0.0
            return path[i].lerp(path[i+1], min(max(r, 0.0), 1.0))
        acc += s
    return path[-1]

print("FINGERS", SRC)
plan = {}
for SIDE in (["L", "R"] if SIDES == "LR" else [SIDES]):
    hname = f"{SIDE}_Hand"
    if hname not in arm.data.bones:
        print(f"  {SIDE}: no hand bone"); continue
    hb = arm.data.bones[hname]
    wrist, tipb = M @ hb.head_local, M @ hb.tail_local
    ax = (tipb - wrist).normalized(); L = (tipb - wrist).length

    gi = mesh.vertex_groups[hname].index
    idx = [v.index for v in mesh.data.vertices
           for g in v.groups if g.group == gi and g.weight >= 0.5]
    P = {i: mw @ mesh.data.vertices[i].co for i in idx}
    t = {i: (P[i] - wrist).dot(ax) / L for i in idx}

    nbr = {i: set() for i in idx}; S_ = set(idx)
    for e in mesh.data.edges:
        a, b = e.vertices
        if a in S_ and b in S_: nbr[a].add(b); nbr[b].add(a)
    def comps(sub):
        seen, out = set(), []
        for s in sub:
            if s in seen: continue
            st, c = [s], []; seen.add(s)
            while st:
                u = st.pop(); c.append(u)
                for v in nbr[u]:
                    if v in sub and v not in seen: seen.add(v); st.append(v)
            out.append(c)
        return sorted(out, key=len, reverse=True)

    best = None
    for lo in [x / 100 for x in range(60, 99)]:
        cs = comps({i for i in idx if t[i] >= lo})
        big = [c for c in cs if len(c) >= 20]
        if len(big) >= 4: best = (lo, big[:4])
    if not best:
        print(f"  {SIDE}: fingers never separate - skipped"); continue
    lo, fingers = best

    cen = [sum((P[i] for i in c), Vector()) / len(c) for c in fingers]
    mid = sum(cen, Vector()) / len(cen)
    lat = max(((a - b) for a, b in itertools.combinations(cen, 2)), key=lambda v: v.length)
    lat = (lat - ax * lat.dot(ax)).normalized()

    thumb = None
    for tl in [x / 100 for x in range(35, 75)]:
        cs = comps({i for i in idx if t[i] >= tl})
        if len(cs) >= 2 and 20 <= len(cs[1]) <= 400: thumb = cs[1]; break
    if thumb:
        tc = sum((P[i] for i in thumb), Vector()) / len(thumb)
        if (tc - mid).dot(lat) < 0: lat = -lat           # lateral points toward the thumb
    palm = ax.cross(lat).normalized()

    t_tip = sum((c - wrist).dot(ax) / L for c in cen) / 4.0
    t_kn  = t_tip * PALM_FRAC

    latc = sorted(((cen[k] - mid).dot(lat), k) for k in range(4))
    bounds = [-1e9] + [(latc[i][0] + latc[i+1][0]) / 2 for i in range(3)] + [1e9]

    tof = lambda p: (p - wrist).dot(ax) / L
    out = []
    bylat = {k: n for n, (_, k) in enumerate(latc)}
    for n in range(4):
        blo, bhi = bounds[n], bounds[n+1]
        k = latc[n][1]                                  # the tip cluster in this band
        tip_pts = [P[i] for i in fingers[k]]
        own = [P[i] for i in idx
               if t[i] >= t_kn and blo <= (P[i] - mid).dot(lat) < bhi]
        if len(own) < 12:
            print(f"  {SIDE} {NAMES[n]}: only {len(own)} verts - skipped"); continue
        tipc = sum(tip_pts, Vector()) / len(tip_pts)
        rad  = sum((p - tipc).length for p in tip_pts) / len(tip_pts)
        path = track(tip_pts, own, tof, t_kn, SLICES)
        span = sum((path[i+1] - path[i]).length for i in range(len(path) - 1)) if len(path) > 1 else 0.0
        if len(path) < 4 or span < 0.020 * H:
            # the trace degenerated (it can, where a finger fuses early) - fall back to the
            # straight run from this finger's knuckle station to its measured tip
            d = tipc - wrist
            knee = wrist + ax * (t_kn * L) + lat * d.dot(lat) + palm * (d.dot(palm) * 0.5)
            path = [knee.lerp(tipc, s / 8.0) for s in range(9)]
            print(f"  {SIDE} {NAMES[n]}: trace degenerated, straight fallback")
        pts = [along(path, f) for f in PHAL]
        fixed = []
        for p in pts:
            q, ok = pull_inside(p, rad * 0.8)
            fixed.append(q)
        out.append((NAMES[n], fixed))

    if thumb:
        tverts = [P[i] for i in thumb]
        tc = sum(tverts, Vector()) / len(tverts)
        d = tc - wrist
        base = wrist + ax * (d.dot(ax) * 0.45) + lat * (d.dot(lat) * 0.45) + palm * (d.dot(palm) * 0.45)
        tax = (tc - base).normalized()
        tmax = max((v - base).dot(tax) for v in tverts)
        far = sorted(tverts, key=lambda v: -(v - base).dot(tax))[:max(8, len(tverts)//8)]
        farc = sum(far, Vector()) / len(far)
        trad = sum((p - farc).length for p in far) / len(far)
        path = track(far, tverts, lambda p: (p - base).dot(tax), 0.0, SLICES)
        if len(path) >= 2 and (path[0] - base).length > 1e-6:
            path = [base] + path
        span = sum((path[i+1] - path[i]).length for i in range(len(path) - 1)) if len(path) > 1 else 0.0
        if len(path) < 4 or span < 0.020 * H:
            path = [base.lerp(farc, s / 8.0) for s in range(9)]
            print(f"  {SIDE} Thumb: trace degenerated, straight fallback")
        out.append(("Thumb", [pull_inside(along(path, f), trad * 0.8)[0] for f in PHAL]))

    plan[SIDE] = (out, palm, lat, ax)
    print(f"  {SIDE}: knuckles at t={t_kn:.2f}, tips at t={t_tip:.2f}, split found at t>={lo:.2f}")
    for name, pts in out:
        ok = [inside(p) for p in pts]
        ln = sum((pts[i+1] - pts[i]).length for i in range(3)) / H * 100
        print(f"     {name:<7} len {ln:5.2f}%H  inside/6 {ok}")

if not APPLY:
    print("  (dry run - pass 'apply' to build the bones and write the FBX)")
    sys.exit(0)

# ---------------- build the bones ----------------
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
created = {}
for SIDE, (out, palm, lat, ax) in plan.items():
    hand = eb[f"{SIDE}_Hand"]
    for name, pts in out:
        parent = hand
        chain = []
        for j in range(3):
            bn = f"{SIDE}_{name}{j+1}"
            b = eb.new(bn)
            b.head = Mi @ pts[j]
            b.tail = Mi @ pts[j+1]
            b.parent = parent
            b.use_connect = (j > 0)
            # roll so local z is the palm normal: bending about local x then curls the finger
            b.align_roll(Mi.to_3x3() @ palm)
            parent = b
            chain.append(bn)
        created.setdefault(SIDE, []).append(chain)
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- redistribute the hand's weights ----------------
for SIDE in plan:
    hname = f"{SIDE}_Hand"
    for chain in created[SIDE]:
        for bn in chain:
            if bn not in mesh.vertex_groups: mesh.vertex_groups.new(name=bn)
    segs = []
    for chain in created[SIDE]:
        for bn in chain:
            b = arm.data.bones[bn]
            segs.append((bn, M @ b.head_local, M @ b.tail_local))
    gi = mesh.vertex_groups[hname].index
    hv = [(v.index, g.weight) for v in mesh.data.vertices
          for g in v.groups if g.group == gi and g.weight > 0.0]
    def dist(p, a, b):
        ab = b - a; L2 = ab.length_squared
        if L2 < 1e-12: return (p - a).length
        s = max(0.0, min(1.0, (p - a).dot(ab) / L2))
        return (p - (a + ab * s)).length
    moved = 0
    for vi, w in hv:
        p = mw @ mesh.data.vertices[vi].co
        # the palm itself competes, as the segment from the wrist to the knuckles
        hb = arm.data.bones[hname]
        cand = [(hname, dist(p, M @ hb.head_local, M @ hb.tail_local))]
        cand += [(bn, dist(p, a, b)) for bn, a, b in segs]
        cand.sort(key=lambda c: c[1])
        top = cand[:2]
        inv = [1.0 / (d * d + 1e-9) for _, d in top]
        s = sum(inv)
        for (bn, _), iw in zip(top, inv):
            share = w * iw / s
            if share <= 1e-5: continue
            mesh.vertex_groups[bn].add([vi], share, 'REPLACE')
            if bn != hname: moved += 1
        if top[0][0] == hname and len(top) == 1:
            continue
        if not any(bn == hname for bn, _ in top):
            mesh.vertex_groups[hname].add([vi], 0.0, 'REPLACE')
    print(f"  {SIDE}: {len(hv)} hand vertices re-weighted, {moved} finger assignments")

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=DST, use_selection=True, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, armature_nodetype='NULL', bake_anim=False,
    path_mode='COPY', embed_textures=True,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y')
import os
print(f"FINGERS-OK {DST} ({os.path.getsize(DST)//1024} KB)  bones now {len(arm.data.bones)}")
