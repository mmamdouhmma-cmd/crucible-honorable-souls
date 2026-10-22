"""Characterise pack clips: how far the weapon hand travels, which way, and whether the body turns.

Picking an attack clip by its FILE NAME is how we ended up with a heavy nobody can read. This
measures the sword hand's path and the hips' yaw so a clip can be chosen on what it actually does.
"""
import bpy, sys, os, math
from mathutils import Vector

folder = sys.argv[sys.argv.index("--")+1]
names = sys.argv[sys.argv.index("--")+2:]

def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.fbx_import(filepath=path)
    arm = next((o for o in bpy.data.objects if o.type=='ARMATURE'), None)
    return arm

#   hiTop / hiLow  the sword hand's highest and lowest point, measured FROM THE HIPS, so +1.0 is a
#                  hand one hips-to-head above the belt (about head height) and -0.5 is knee level.
#   vert           drop / side. Above ~1.5 the swing is vertical (an overhead); below ~0.7 it is a
#                  horizontal sweep. This is what separates "chops down" from "cuts across", which
#                  drop alone cannot do - a big spin also drops a long way.
print(f"{'clip':34s} {'f':>4} {'hand path':>9} {'drop':>6} {'fwd':>6} {'side':>6} {'hipYaw':>7} {'shoulder':>8}"
      f" {'hiTop':>6} {'hiLow':>6} {'vert':>5}")
for n in names:
    path = os.path.join(folder, n + ".fbx")
    if not os.path.exists(path):
        print(f"{n:34s}  MISSING"); continue
    arm = load(path)
    if arm is None: print(f"{n:34s}  no armature"); continue
    act = arm.animation_data.action if arm.animation_data else None
    if act is None: print(f"{n:34s}  no action"); continue
    f0, f1 = (int(round(x)) for x in act.frame_range)
    def pb(short):
        return next((p for p in arm.pose.bones if p.name.split(":")[-1] == short), None)
    hand, hips, sh = pb("RightHand"), pb("Hips"), pb("RightShoulder")
    head = pb("Head")
    if not (hand and hips): print(f"{n:34s}  no bones"); continue
    # rig height ~ hips to head at rest, for normalising
    scale = 1.0
    if head:
        scale = abs((arm.matrix_world @ head.bone.head_local).z - (arm.matrix_world @ hips.bone.head_local).z)
        scale = max(scale, 1e-6)
    pts, yaws, shx, rel = [], [], [], []
    for f in range(f0, f1+1):
        bpy.context.scene.frame_set(f)
        pts.append((arm.matrix_world @ hand.matrix).translation.copy())
        rel.append(((arm.matrix_world @ hand.matrix).translation.z
                    - (arm.matrix_world @ hips.matrix).translation.z) / scale)
        m = (arm.matrix_world @ hips.matrix).to_euler()
        yaws.append(math.degrees(m.z))
        if sh: shx.append((arm.matrix_world @ sh.matrix).translation.copy())
    path_len = sum((pts[i]-pts[i-1]).length for i in range(1, len(pts))) / scale
    zs = [p.z for p in pts]; ys = [p.y for p in pts]; xs = [p.x for p in pts]
    drop = (max(zs)-min(zs)) / scale
    fwd  = (max(ys)-min(ys)) / scale
    side = (max(xs)-min(xs)) / scale
    # unwrap yaw so a full spin shows as ~360
    tot = 0.0
    for i in range(1, len(yaws)):
        d = yaws[i]-yaws[i-1]
        while d > 180: d -= 360
        while d < -180: d += 360
        tot += d
    shoulder = (max(p.y for p in shx)-min(p.y for p in shx))/scale if shx else 0
    print(f"{n:34s} {len(pts):4d} {path_len:9.2f} {drop:6.2f} {fwd:6.2f} {side:6.2f} {tot:7.0f} {shoulder:8.2f}"
          f" {max(rel):6.2f} {min(rel):6.2f} {drop/max(side,1e-6):5.2f}")
