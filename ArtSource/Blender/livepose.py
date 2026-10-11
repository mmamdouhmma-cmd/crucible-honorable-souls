"""Live posing helpers for the Blender MCP session (exec this file inside the open Tripo_AnimMaster.blend).

apply(pose)          static preview of STANCE + pose on a rig (same offsets/conventions as build_clips.py)
report()             world positions of hands/elbows/head
reach(rig, side, target, base)   grid-search upper arm + forearm so the hand lands on a world point
aim(rig, hand, BLADE_REST[...], yaw, pitch, forearm_bend)   grid-search forearm twist + wrist so the weapon points there

Usage from the MCP:  exec(open(r"<repo>/ArtSource/Blender/livepose.py").read())
To see Miyamoto, import Assets/_project/Characters/Miyamoto/Model/miyamoto rigged.fbx (same 41-bone skeleton),
rename the armature MiyaRig and move it to y=0.6; do not save it into the master.
"""
import bpy, math
from mathutils import Vector

SP = ["Waist", "Spine01", "Spine02", "NeckTwist01", "Head"]
def spine(x=0, y=0, z=0, scale=(0.4, 1.0, 1.0, 0.8, 1.2)):
    return {b: (x * s, y * s, z * s) for b, s in zip(SP, scale)}
def head(x=0, y=0, z=0): return {"Head": (x, y, z)}
def add(*dicts):
    out = {}
    for d in dicts:
        for b, r in d.items():
            o = out.get(b, (0, 0, 0)); out[b] = (o[0]+r[0], o[1]+r[1], o[2]+r[2])
    return out
def arms(l=(0,0,0), r=(0,0,0), lf=0, rf=0):
    return {"L_Upperarm": l, "R_Upperarm": r, "L_Forearm": (lf,0,0), "R_Forearm": (rf,0,0)}
def hands(l=(0,0,0), r=(0,0,0)): return {"L_Hand": l, "R_Hand": r}
def legs(lt=0, rt=0, lc=0, rc=0, lf=0, rf=0):
    return {"L_Thigh": (lt,0,0), "R_Thigh": (rt,0,0), "L_Calf": (lc,0,0), "R_Calf": (rc,0,0), "L_Foot": (lf,0,0), "R_Foot": (rf,0,0)}
def pelvis(y=0, z=0, x=0): return {"Pelvis": (x, y, z)}
HIP = "__hip__"
def hip(fwd=0.0, left=0.0, up=0.0): return {HIP: (fwd, left, up)}
def clav(l=(0,0,0), r=(0,0,0)): return {"L_Clavicle": l, "R_Clavicle": r}

STANCE = add(spine(x=-3, scale=(0.5,1,1,0.3,0.2)), arms(l=(18,0,-55), r=(18,0,55), lf=35, rf=35), legs(lt=4, rt=4, lc=-8, rc=-8))

def apply(pose, rig_name="TripoRig", base=None):
    """Static preview: clear animation, reset the pose, apply STANCE + pose."""
    rig = bpy.data.objects[rig_name]
    if rig.animation_data: rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0,0,0); pb.location = (0,0,0); pb.scale = (1,1,1)
    full = add(STANCE if base is None else base, pose)
    for b, (rx, ry, rz) in full.items():
        if b == HIP: continue
        rig.pose.bones[b].rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    fwd, left, up = full.get(HIP, (0,0,0))
    hip_inv = (rig.matrix_world @ rig.data.bones["Hip"].matrix_local).to_3x3().inverted()
    rig.pose.bones["Hip"].location = hip_inv @ Vector((fwd, left, up))
    bpy.context.view_layer.update()
    return rig

def report(rig_name="TripoRig"):
    """World positions of hands, elbows, shoulders, head (metres) for sanity."""
    rig = bpy.data.objects[rig_name]
    out = {}
    for n in ("L_Hand","R_Hand","L_Forearm","R_Forearm","L_Upperarm","Head","Hip"):
        pb = rig.pose.bones[n]
        w = rig.matrix_world @ pb.head
        out[n] = tuple(round(v,3) for v in w)
    return out

# ---- weapon aiming -------------------------------------------------------
# Rest-space weapon directions measured from the meshes (armature space, rest pose):
BLADE_REST = {"katana_R": Vector((0.0, -0.04, 1.0)),      # Miyamoto: katana points straight up from the right hand
              "axe_R": Vector((-0.08, -0.93, -0.35)),      # Bjorn: axe handles continue the arm line
              "axe_L": Vector((-0.02, 0.93, -0.37))}

def blade_local(rig_name, bone, rest_dir):
    rot = bpy.data.objects[rig_name].data.bones[bone].matrix_local.to_3x3()
    return rot.inverted() @ rest_dir.normalized()

def blade_world(rig_name, bone, rest_dir):
    rig = bpy.data.objects[rig_name]; pb = rig.pose.bones[bone]
    return ((rig.matrix_world.to_3x3() @ pb.matrix.to_3x3()) @ blade_local(rig_name, bone, rest_dir)).normalized()

def yawpitch(v):
    v = v.normalized()
    return (round(math.degrees(math.atan2(v.y, v.x)), 1), round(math.degrees(math.asin(max(-1, min(1, v.z)))), 1))

def aim(rig_name, bone, rest_dir, yaw, pitch, forearm_bend, twist_range=range(-60, 61, 15),
        hx_range=range(-70, 71, 10), hz_range=range(-80, 81, 10), penalty=0.0015):
    """Grid-search forearm twist (Y) and wrist X/Z so the weapon points at (yaw, pitch) in degrees.
    forearm_bend is the forearm X in degrees (already including STANCE). Leaves the best pose applied."""
    rig = bpy.data.objects[rig_name]
    side = bone[0]
    fa = rig.pose.bones[side + "_Forearm"]; hd = rig.pose.bones[bone]
    t = Vector((math.cos(math.radians(pitch)) * math.cos(math.radians(yaw)),
                math.cos(math.radians(pitch)) * math.sin(math.radians(yaw)), math.sin(math.radians(pitch))))
    best = None
    for fy in twist_range:
        for hx in hx_range:
            for hz in hz_range:
                fa.rotation_euler = (math.radians(forearm_bend), math.radians(fy), 0)
                hd.rotation_euler = (math.radians(hx), 0, math.radians(hz))
                bpy.context.view_layer.update()
                d = blade_world(rig_name, bone, rest_dir)
                err = (d - t).length + penalty * (abs(hx) + abs(hz) + 0.5 * abs(fy)) / 10
                if best is None or err < best[0]:
                    best = (err, fy, hx, hz, yawpitch(d))
    _, fy, hx, hz, yp = best
    fa.rotation_euler = (math.radians(forearm_bend), math.radians(fy), 0)
    hd.rotation_euler = (math.radians(hx), 0, math.radians(hz))
    bpy.context.view_layer.update()
    return {"forearm_twist": fy, "hand_x": hx, "hand_z": hz, "got": yp, "err": round(best[0], 3)}

def reach(rig_name, side, target, base_pose, ux=range(-10, 71, 10), uy=range(-40, 41, 20), uz=range(-20, 71, 10), fb=range(-20, 71, 10)):
    """Grid-search upper arm (x,y,z) and forearm bend (offsets from STANCE) so the hand head lands near target (world).
    Applies base_pose (which must not already set this arm) each try. Returns (dist, ux, uy, uz, fb)."""
    rig = bpy.data.objects[rig_name]
    mesh = next((o for o in bpy.data.objects if o.type == 'MESH' and o.parent == rig), None)
    mod = mesh.modifiers[0] if mesh else None
    if mod: mod.show_viewport = False
    apply(base_pose, rig_name)
    ua = rig.pose.bones[side + "_Upperarm"]; fa = rig.pose.bones[side + "_Forearm"]; hd = rig.pose.bones[side + "_Hand"]
    sx, sy, sz = STANCE[side + "_Upperarm"]; sf = STANCE[side + "_Forearm"][0]
    best = None
    for x in ux:
        for y in uy:
            for z in uz:
                zz = z if side == "R" else -z   # z>0 = arm comes inward/across for both sides
                ua.rotation_euler = (math.radians(sx + x), math.radians(sy + y), math.radians(sz + zz))
                for f in fb:
                    fa.rotation_euler = (math.radians(sf + f), 0, 0)
                    bpy.context.view_layer.update()
                    d = ((rig.matrix_world @ hd.head) - Vector(target)).length
                    if best is None or d < best[0]:
                        best = (round(d, 3), x, y, zz, f)
    if mod: mod.show_viewport = True
    return best

# ---- preview retarget -------------------------------------------------------
# The Bjorn and Miyamoto FBX skeletons share bone names and a T-pose but NOT bone rolls (R_Hand differs by 52 deg),
# so an action authored on TripoRig cannot be assigned to MiyaRig directly. Unity's Humanoid retarget hides this;
# for a Blender preview bake the per-bone rotation through the rest frames instead.
def retarget_action(action_name, src="TripoRig", dst="MiyaRig", out_name=None):
    s = bpy.data.objects[src]; d = bpy.data.objects[dst]
    if not s.animation_data: s.animation_data_create()
    if not d.animation_data: d.animation_data_create()
    act = bpy.data.actions[action_name]
    s.animation_data.action = act
    try: s.animation_data.action_slot = act.slots[0]
    except Exception: pass
    out_name = out_name or (action_name + "_miya")
    old = bpy.data.actions.get(out_name)
    if old: bpy.data.actions.remove(old)
    d.animation_data.action = None
    for pb in d.pose.bones:
        pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
    scale = d.dimensions.z / s.dimensions.z if s.dimensions.z else 1.0
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    sc = bpy.context.scene
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        for pb in s.pose.bones:
            Rt = s.data.bones[pb.name].matrix_local.to_3x3()
            Rm = d.data.bones[pb.name].matrix_local.to_3x3()
            Bt = pb.matrix_basis.to_3x3()
            Bm = Rm.inverted() @ Rt @ Bt @ Rt.inverted() @ Rm
            dpb = d.pose.bones[pb.name]
            dpb.rotation_euler = Bm.to_euler('XYZ')
            dpb.keyframe_insert("rotation_euler", frame=f)
            if pb.name == "Hip":
                dpb.location = Rm.inverted() @ (Rt @ pb.location) * scale
                dpb.keyframe_insert("location", frame=f)
    d.animation_data.action.name = out_name
    sc.frame_set(f0)
    return out_name
