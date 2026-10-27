# Slide a hand-parented weapon so its haft runs through the HOLLOW of the fist, keeping its direction.
#
#   blender -b <Name>_HD_rigged.blend --python seat_haft.py -- <Weapon object> <Right|Left> <render dir> [--save]
#
# Why: hd_rig.py --keep-rig binds an HD mesh to a fighter's ORIGINAL skeleton and keeps the weapon where
# the old body held it. The skeleton is unchanged but the HD fist is a different shape, so the haft can end
# up outside the hand (Bjorn's axe ran under both palms). This only TRANSLATES the weapon:
#   1. the grip: along the weapon's long axis, the narrowest stretch near the hand (median radius,
#      so a tassel or a feather does not count);
#   2. the fist: every vertex whose dominant weight is the hand bone, cut to the grip's length;
#   3. in the section across the haft, the point farthest from that flesh that the flesh surrounds
#      on most of the compass - the hollow the curled fingers close round;
#   4. move the weapon perpendicular to its own axis so the grip's axis passes through that point.
# Writes before/after close-ups and prints the shift; with --save it saves the blend in place.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
WEAPON, SIDE, OUT = argv[0], argv[1], argv[2]
SAVE = "--save" in argv
# --along X: also slide the weapon X body-units along its own axis (+ = toward the blade), e.g. to
# clear a katana guard off the knuckles. Default 0: the weapon keeps its place along the haft.
ALONG = float(argv[argv.index("--along") + 1]) if "--along" in argv else 0.0
os.makedirs(OUT, exist_ok=True)

rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith("_Body"))
weapon = bpy.data.objects[WEAPON]
HAND = "mixamorig:" + SIDE + "Hand"


def world_verts(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    m = ev.to_mesh()
    co = np.array([list(obj.matrix_world @ v.co) for v in m.vertices])
    ev.to_mesh_clear()
    return co


def hand_verts():
    """Vertices whose DOMINANT group is the hand bone (grip mode puts the whole baked fist on it)."""
    gi = body.vertex_groups[HAND].index
    names = {g.index: g.name for g in body.vertex_groups}
    keep = []
    for v in body.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda x: x.weight)
        if g.group == gi:
            keep.append(v.index)
    return np.array(keep)


def grip(co, hint):
    """(centre, axis grip->blade, half length, radius) of the weapon's grip near the hand."""
    c = co.mean(axis=0)
    _, ev = np.linalg.eigh(np.cov((co - c).T))
    ax = ev[:, 2]
    t = (co - c) @ ax
    rad = np.linalg.norm((co - c) - np.outer(t, ax), axis=1)
    th = float((hint - c) @ ax)
    span = t.max() - t.min()
    win = 0.09 * span / 0.55
    best = None
    for c0 in np.linspace(t.min() + win / 2, t.max() - win / 2, 160):
        m = np.abs(t - c0) < win / 2
        if m.sum() < 30 or abs(c0 - th) > 2.0 * win:
            continue
        r = float(np.median(rad[m]))
        if best is None or r < best[0]:
            best = (r, c0)
    r, c0 = best
    m = (np.abs(t - c0) < win / 2) & (rad < r * 1.6)
    g = co[m]
    gc = g.mean(axis=0)
    _, ev2 = np.linalg.eigh(np.cov((g - gc).T))
    gax = ev2[:, 2]
    blade_end = 1.0 if np.percentile(t, 95) - np.median(t) > np.median(t) - np.percentile(t, 5) else -1.0
    if gax @ ax * blade_end < 0:
        gax = -gax
    return gc, gax / np.linalg.norm(gax), win / 2, r


def hollow(P, gc, gax, grad):
    """In the plane across the haft, the enclosed point of the fist farthest from its flesh."""
    a = np.cross(gax, [0, 0, 1.0])
    if np.linalg.norm(a) < 1e-3:
        a = np.cross(gax, [1.0, 0, 0])
    a /= np.linalg.norm(a)
    b = np.cross(gax, a)
    d = P - gc
    Q = np.stack([d @ a, d @ b], axis=1)
    lo, hi = Q.min(axis=0), Q.max(axis=0)
    size = float(np.max(hi - lo))
    best = None
    for x in np.linspace(lo[0], hi[0], 48):
        for y in np.linspace(lo[1], hi[1], 48):
            dd = Q - [x, y]
            dist = np.linalg.norm(dd, axis=1)
            r = float(dist.min())
            near = dist < 0.6 * size
            if near.sum() < 12:
                continue
            ang = np.sort(np.arctan2(dd[near, 1], dd[near, 0]))
            gap = np.max(np.diff(np.concatenate([ang, ang[:1] + 2 * np.pi])))
            if gap > math.radians(150):          # a half-closed claw is open on one side; not more
                continue
            if best is None or r > best[0]:
                best = (r, x, y, gap)
    if best is None:
        return None
    r, x, y, gap = best
    return gc + a * x + b * y, r, math.degrees(gap)


def report(tag):
    co = world_verts(weapon)
    hv = world_verts(body)[HV]
    hc = hv.mean(axis=0)
    gc, gax, half, grad = grip(co, hc)
    along_h = (hv - hc) @ gax
    sel = np.abs(along_h) < 0.35 * float(np.ptp(along_h))   # the fist's middle, across its whole width
    P = hv[sel]
    d = P - gc
    perp = np.linalg.norm(d - np.outer(d @ gax, gax), axis=1)
    print(f"SEAT {tag}: grip r {grad:.4f}  hand verts in grip span {sel.sum()}  "
          f"nearest flesh to haft axis {perp.min():.4f}  hand centroid off axis {np.linalg.norm((hc-gc)-((hc-gc)@gax)*gax):.4f}")
    return co, hv, hc, gc, gax, half, grad, P


def shots(tag, hc, gax):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.render.resolution_x, scn.render.resolution_y = 700, 600
    scn.display.shading.color_type = 'TEXTURE'
    cam_data = bpy.data.cameras.new("seatcam")
    cam = bpy.data.objects.new("seatcam", cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 0.32 * H
    hcv = Vector(hc)
    g = Vector(gax)
    side = g.cross(Vector((0, 0, 1))).normalized()
    for i, view in enumerate([g, side, -side, Vector((0, 0, -1)) if abs(g.z) < 0.9 else side.cross(g)]):
        cam.location = hcv - view * 2.0
        cam.rotation_euler = view.to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(OUT, f"{tag}_{i}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


H = float(np.ptp(world_verts(body)[:, 2]))
HV = hand_verts()
print(f"SEAT hand verts {len(HV)}  body height {H:.3f}")

co, hv, hc, gc, gax, half, grad, P = report("before")
shots("before", hc, gax)
h = hollow(P, gc, gax, grad)
if h is None:
    print("SEAT no enclosed hollow found - weapon not moved")
    sys.exit(0)
target, clear, gap = h
d = target - gc
shift = d - (d @ gax) * gax                      # perpendicular only: the grip stays where it is along the haft
along = ALONG
shift = shift + gax * along
print(f"SEAT hollow clearance {clear:.4f} (grip r {grad:.4f})  open {gap:.0f} deg  "
      f"shift perp {np.linalg.norm(shift - gax*along):.4f} along {along:.4f}")
weapon.matrix_world = Matrix.Translation(Vector(shift)) @ weapon.matrix_world
bpy.context.view_layer.update()
co, hv, hc, gc, gax, half, grad, P = report("after")
shots("after", hc, gax)
if SAVE:
    bpy.ops.wm.save_mainfile()
    print("SEAT saved", bpy.data.filepath)
