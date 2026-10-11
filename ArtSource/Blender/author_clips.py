"""Author one clip on a fighter's animation master (anim_rig.py) in world-space terms and export it.

Usage: blender -b --python author_clips.py -- <Name>_AnimMaster.blend <ClipName> <out_dir> [render_dir]

The clip definitions live in clips_<name>.py next to this file (clips_bjorn.py, clips_miyamoto.py) and
register themselves into CLIPS with @clip("Bjorn_Idle", frames=120, loop=True). A clip function receives
a Rig helper and keys poses with it:

  rig.hip(dx, dy, dz, yaw, pitch, roll)      Hip bone: world offset from rest (units) and rotation (deg)
  rig.spine(lean, side, twist)               Spine01+Spine02 share the angles (deg): lean +fwd, side +left, twist +left
  rig.neck(lean, side, twist) / rig.head(...)
  rig.clav(side, lift, fwd)                  clavicle lift/forward (deg)
  rig.foot(side, x, y, z, yaw, pitch, roll)  IK foot control, world position RELATIVE TO ITS REST, rotation (deg)
  rig.hand(side, x, y, z, yaw, pitch, roll)  IK hand control, world position relative to rest, rotation about world axes
  rig.arm(side, swing, raise_, twist, bend, fore_twist, wrist_flex, wrist_dev)   FK arm (deg), used when ik_arms=0
  rig.ik_arms(v)                             0 = FK arms, 1 = IK hands (the driver property)
  rig.key(frame, ease="sine")                write a key for everything set so far on this frame
  rig.pole(side, dx, dy, dz)                 nudge a knee pole (world offset)

World axes: +X forward (the fighter faces +X in Blender), +Y left, +Z up. All angles in degrees.
Sign conventions: yaw + = turn left; lean + = tip forward (body parts); side + = bend to the left; roll + = tilt
the top to the left; foot/hand pitch + = toes/thumb UP; foot/hand roll + = outer edge up (tilt left).
Rotations are applied as offsets on top of the rest pose, in the bone's rest frame mapped from world
axes (so "lean forward" is the same call on every rig).

Export: bake_and_export from the master's export_tools text (destructive, so this script handles ONE
clip per Blender run) to <out_dir>/<ClipName>.fbx. With render_dir, a filmstrip PNG is rendered too.
"""
import bpy, sys, os, math, importlib.util
from mathutils import Vector, Matrix, Euler, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
MASTER, CLIP, OUT_DIR = args[0], args[1], args[2]
RENDER_DIR = args[3] if len(args) > 3 else None
HERE = os.path.dirname(os.path.abspath(__file__))

bpy.ops.wm.open_mainfile(filepath=MASTER)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
scene = bpy.context.scene
exec(bpy.data.texts["export_tools.py"].as_string())

FWD, LEFT, UP = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

def action_fcurves(act, owner):
    """F-curves of an action, across the legacy and the layered (Blender 4.4+/5.x) action APIs."""
    if hasattr(act, "fcurves"):
        return act.fcurves
    slot = owner.animation_data.action_slot
    for layer in act.layers:
        for strip in layer.strips:
            cb = strip.channelbag(slot, ensure=True) if hasattr(strip, "channelbag") else None
            if cb is not None:
                return cb.fcurves
    return []

class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.pb = arm.pose.bones
        bpy.ops.object.mode_set(mode='POSE')
        for b in self.pb:
            b.rotation_mode = 'QUATERNION'
        self.rest_world = {b.name: (arm.matrix_world @ b.bone.matrix_local).copy() for b in self.pb}
        self.pending = {}
        self.ease_default = "sine"

    # ---- generic: world-axis rotation offset applied to a bone ------------------------------------
    def _rot(self, name, yaw, pitch, roll, order=("yaw", "pitch", "roll")):
        """yaw about world UP (+left), pitch about world LEFT (+nose down... we use +fwd lean = pitch down), roll about world FWD."""
        q = Quaternion()
        for k in order:
            if k == "yaw": q = q @ Quaternion(UP, math.radians(yaw))
            elif k == "pitch": q = q @ Quaternion(LEFT, math.radians(pitch))
            elif k == "roll": q = q @ Quaternion(FWD, math.radians(roll))
        b = self.pb[name]
        R = self.rest_world[name].to_3x3()
        local = (R.inverted() @ q.to_matrix() @ R).to_quaternion()
        b.rotation_quaternion = local
        self.pending[name] = True

    def _loc(self, name, dx, dy, dz):
        b = self.pb[name]
        R = self.rest_world[name].to_3x3()
        b.location = R.inverted() @ Vector((dx, dy, dz))
        self.pending[name] = True

    # ---- body ---------------------------------------------------------------------------------------
    def hip(self, dx=0, dy=0, dz=0, yaw=0, pitch=0, roll=0):
        self._loc("Hip", dx, dy, dz); self._rot("Hip", yaw, pitch, -roll)
    def pelvis(self, yaw=0, pitch=0, roll=0):
        self._rot("Pelvis", yaw, pitch, -roll)
    def spine(self, lean=0, side=0, twist=0):
        for n in ("Spine01", "Spine02"):
            self._rot(n, twist, lean, -side)
    def waist(self, lean=0, side=0, twist=0):
        self._rot("Waist", twist, lean, -side)
    def neck(self, lean=0, side=0, twist=0):
        for n in ("NeckTwist01", "NeckTwist02"):
            self._rot(n, twist, lean, -side)
    def head(self, lean=0, side=0, twist=0):
        self._rot("Head", twist, lean, -side)
    def clav(self, side, lift=0, fwd=0):
        s = 1 if side == "L" else -1
        self._rot(f"{side}_Clavicle", fwd * s, 0, -lift * s)
    def thigh(self, side, swing=0, spread=0, twist=0):
        s = 1 if side == "L" else -1
        self._rot(f"{side}_Thigh", twist * s, -swing, -spread * s)
    def calf(self, side, bend=0):
        self._rot(f"{side}_Calf", 0, bend, 0)

    # ---- arms (FK) -----------------------------------------------------------------------------------
    def arm(self, side, swing=0, raise_=0, twist=0, bend=0, fore_twist=0, wrist_flex=0, wrist_dev=0):
        s = 1 if side == "L" else -1
        self._rot(f"{side}_Upperarm", -swing * s * 0 + twist * s, -swing, raise_ * s)
        self._rot(f"{side}_Forearm", fore_twist * s, -bend, 0)
        self._rot(f"{side}_Hand", wrist_dev * s, -wrist_flex, 0)

    # ---- IK controls -------------------------------------------------------------------------------
    def _ctrl(self, name, x, y, z, yaw, pitch, roll):
        b = self.pb[name]
        R = self.rest_world[name].to_3x3()
        b.location = R.inverted() @ Vector((x, y, z))
        q = Quaternion(UP, math.radians(yaw)) @ Quaternion(LEFT, math.radians(pitch)) @ Quaternion(FWD, math.radians(roll))
        b.rotation_quaternion = (R.inverted() @ q.to_matrix() @ R).to_quaternion()
        self.pending[name] = True
    def foot(self, side, x=0, y=0, z=0, yaw=0, pitch=0, roll=0):
        self._ctrl(f"IK_{side}_Foot", x, y, z, yaw, -pitch, -roll)
    def hand(self, side, x=0, y=0, z=0, yaw=0, pitch=0, roll=0):
        self._ctrl(f"IK_{side}_Hand", x, y, z, yaw, -pitch, -roll)
    def pole(self, side, dx=0, dy=0, dz=0):
        self._loc(f"POLE_{side}_Knee", dx, dy, dz)
    def elbow(self, side, dx=0, dy=0, dz=0):
        self._loc(f"POLE_{side}_Elbow", dx, dy, dz)
    def ik_arms(self, v):
        self.arm["ik_arms"] = float(v)
        self.arm.keyframe_insert('["ik_arms"]', frame=scene.frame_current)

    # ---- keys ----------------------------------------------------------------------------------------
    def key(self, frame, ease=None):
        # NOTE: no scene.frame_set here: evaluating the action would overwrite the pose just set
        for name in list(self.pending):
            b = self.pb[name]
            b.keyframe_insert("location", frame=frame)
            b.keyframe_insert("rotation_quaternion", frame=frame)
        self._ease(frame, ease or self.ease_default)
        self.pending.clear()

    def _ease(self, frame, ease):
        act = self.arm.animation_data.action if self.arm.animation_data else None
        if not act: return
        interp = {"sine": ('SINE', 'EASE_IN_OUT'), "linear": ('LINEAR', 'EASE_IN_OUT'), "in": ('EXPO', 'EASE_IN'),
                  "out": ('SINE', 'EASE_OUT'), "over": ('BACK', 'EASE_OUT'), "snap": ('CONSTANT', 'EASE_IN_OUT'),
                  "bezier": ('BEZIER', 'AUTO')}[ease]
        for fc in action_fcurves(act, self.arm):
            for kp in fc.keyframe_points:
                if abs(kp.co.x - frame) < 0.01:
                    kp.interpolation, kp.easing = interp
                    if interp[0] == 'BEZIER':
                        kp.handle_left_type = kp.handle_right_type = 'AUTO_CLAMPED'

    def reset(self):
        for b in self.pb:
            b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0)

# ---- clip registry ---------------------------------------------------------------------------------------
CLIPS = {}
def clip(name, frames, loop=False):
    def deco(fn):
        CLIPS[name] = (fn, frames, loop); return fn
    return deco

char = os.path.basename(MASTER).split("_")[0].lower()
spec = importlib.util.spec_from_file_location(f"clips_{char}", os.path.join(HERE, f"clips_{char}.py"))
mod = importlib.util.module_from_spec(spec)
mod.clip = clip; mod.Rig = Rig; mod.math = math
spec.loader.exec_module(mod)
if CLIP not in CLIPS:
    raise SystemExit(f"unknown clip {CLIP}; known: {sorted(CLIPS)}")
fn, frames, loop = CLIPS[CLIP]

rig = Rig(arm)
if arm.animation_data and arm.animation_data.action:
    arm.animation_data.action = None
act = bpy.data.actions.new(CLIP)
arm.animation_data_create(); arm.animation_data.action = act
scene.frame_start, scene.frame_end = 0, frames
fn(rig, frames)
if loop:
    # copy frame 0 to the last frame for every keyed curve so the loop closes
    for fc in action_fcurves(act, arm):
        v0 = fc.evaluate(0)
        fc.keyframe_points.insert(frames, v0, options={'FAST'})
        fc.update()
print(f"{CLIP}: {len(action_fcurves(act, arm))} curves, {frames} frames, loop={loop}")

if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    cam = bpy.data.objects.new("StripCam", bpy.data.cameras.new("StripCam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.location = (1.45, -1.75, 0.62); cam.rotation_euler = (math.radians(82), 0, math.radians(40))
    cam.data.lens = 50
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'OBJECT'
    for o in bpy.data.objects:
        if o.type == 'MESH': o.color = (0.9, 0.2, 0.2, 1) if "Body" not in o.name else (0.65, 0.65, 0.65, 1)
    scene.render.resolution_x, scene.render.resolution_y = 480, 720
    n = 8
    for i in range(n):
        fr = int(round(i * frames / (n - 1))) if n > 1 else 0
        scene.frame_set(min(fr, frames))
        scene.render.filepath = os.path.join(RENDER_DIR, f"{CLIP}_{i:02d}_f{fr:03d}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    print("strip rendered to", RENDER_DIR)

os.makedirs(OUT_DIR, exist_ok=True)
out = os.path.join(OUT_DIR, CLIP + ".fbx")
bake_and_export(out, 0, frames, arm)
print("exported:", out)
print("CLIP-OK")
