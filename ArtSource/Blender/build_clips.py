"""Build the Tripo animation master and author/export reaction clips.

Usage (headless):
  blender -b --python build_clips.py -- <rig.fbx> <base.blend> <master.blend> <export_dir> <render_dir>

- If <base.blend> is missing, imports the rig FBX, decimates the mesh to a light proxy and saves it.
- Opens the base, authors every clip in CLIPS as its own action on the armature, renders check
  frames with Workbench into <render_dir>, exports each clip as armature-only FBX into <export_dir>,
  and saves everything as <master.blend> (actions kept with fake users).

Bone-space rotation conventions on this Tripo rig (character faces +X, left side +Y):
  spine/neck/head : +X = lean back,  -X = lean forward ; +Z = lean left, -Z = lean right ; Y = twist
  upper arms      : +X = swing forward ; L +Z / R -Z = raise outward
  forearms        : +X = bend forward (hand comes up/forward)
"""
import bpy, sys, os, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
RIG_FBX, BASE_BLEND, MASTER_BLEND, EXPORT_DIR, RENDER_DIR = args
os.makedirs(os.path.dirname(BASE_BLEND), exist_ok=True)
os.makedirs(EXPORT_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)

FPS = 60
PROXY_RATIO = 0.03  # ~30k verts from ~1M

# ---------------------------------------------------------------- clips
# Each clip: list of (frame, {bone: (rx, ry, rz) degrees}) keys. Unlisted bones hold rest (0).
# Every clip starts and ends at rest unless 'loop' is set (hold poses).
SP = ["Waist", "Spine01", "Spine02", "NeckTwist01", "Head"]

def spine(x=0, y=0, z=0, scale=(0.4, 1.0, 1.0, 0.8, 1.2)):
    """Distribute a lean over the spine chain: Waist, Spine01, Spine02, Neck, Head."""
    return {b: (x * s, y * s, z * s) for b, s in zip(SP, scale)}

def merge(*dicts):
    out = {}
    for d in dicts:
        out.update(d)
    return out

def add(*dicts):
    """Sum rotations bone-wise (offsets stack on the stance instead of replacing it)."""
    out = {}
    for d in dicts:
        for b, r in d.items():
            o = out.get(b, (0, 0, 0))
            out[b] = (o[0] + r[0], o[1] + r[1], o[2] + r[2])
    return out

# Neutral fighting stance every clip starts from and returns to. The rig rests in a T-pose,
# so the arms are brought down to the sides and a little forward, elbows softly bent,
# a slight forward lean and a soft knee. Unity blends from the Mixamo idle into this.
STANCE = add(
    spine(x=-3, scale=(0.5, 1, 1, 0.3, 0.2)),
    {"L_Upperarm": (18, 0, -55), "R_Upperarm": (18, 0, 55),
     "L_Forearm": (35, 0, 0), "R_Forearm": (35, 0, 0),
     "L_Thigh": (4, 0, 0), "R_Thigh": (4, 0, 0), "L_Calf": (-8, 0, 0), "R_Calf": (-8, 0, 0)},
)
REST = {}  # offsets of zero: the stance itself

CLIPS = {
    # Struck from the front: head and chest snap back, arms thrown up and out, settle.
    "React_Front": {
        "length": 20,
        "keys": [
            (0, REST),
            (3, add(spine(x=16), {"L_Upperarm": (-12, 0, 22), "R_Upperarm": (-12, 0, -22),
                                   "L_Forearm": (25, 0, 0), "R_Forearm": (25, 0, 0)})),
            (8, add(spine(x=10), {"L_Upperarm": (-7, 0, 14), "R_Upperarm": (-7, 0, -14),
                                   "L_Forearm": (16, 0, 0), "R_Forearm": (16, 0, 0)})),
            (20, REST),
        ],
    },
    # Struck on the LEFT side: body bends away to the right, head whips right, left arm kicks out.
    "React_Left": {
        "length": 20,
        "keys": [
            (0, REST),
            (3, add(spine(x=4, y=-12, z=-15), {"L_Upperarm": (-5, 0, 30), "L_Forearm": (15, 0, 0),
                                                "R_Upperarm": (5, 0, -5)})),
            (8, add(spine(x=2, y=-8, z=-10), {"L_Upperarm": (-3, 0, 18), "L_Forearm": (10, 0, 0),
                                               "R_Upperarm": (3, 0, -3)})),
            (20, REST),
        ],
    },
    # Struck on the RIGHT side: mirror of React_Left.
    "React_Right": {
        "length": 20,
        "keys": [
            (0, REST),
            (3, add(spine(x=4, y=12, z=15), {"R_Upperarm": (-5, 0, -30), "R_Forearm": (15, 0, 0),
                                              "L_Upperarm": (5, 0, 5)})),
            (8, add(spine(x=2, y=8, z=10), {"R_Upperarm": (-3, 0, -18), "R_Forearm": (10, 0, 0),
                                             "L_Upperarm": (3, 0, 3)})),
            (20, REST),
        ],
    },
}
# Guard held up: arms forward and bent, hands high in front, slight crouch. Loops with a breath.
BLOCK = add(spine(x=-4, scale=(0.5, 1, 1, 0.5, 0.3)),
            {"L_Upperarm": (30, 0, 60), "R_Upperarm": (30, 0, -60),
             "L_Forearm": (60, 0, 0), "R_Forearm": (60, 0, 0)})
BREATH = add(spine(x=-1, scale=(0.5, 1, 1, 0.5, 0.3)),
             {"L_Upperarm": (2, 0, 1), "R_Upperarm": (2, 0, -1), "L_Forearm": (2, 0, 0), "R_Forearm": (2, 0, 0)})
CLIPS["Block_Hold"] = {
    "length": 30, "loop": True,
    "keys": [(0, BLOCK), (15, add(BLOCK, BREATH)), (30, BLOCK)],
}
# Guard takes a hit: shoved back from the block pose and recovers into it.
CLIPS["Block_Impact"] = {
    "length": 14,
    "keys": [
        (0, BLOCK),
        (2, add(BLOCK, spine(x=8, scale=(0.5, 1, 1, 0.5, 0.3)),
                {"L_Upperarm": (-12, 0, 0), "R_Upperarm": (-12, 0, 0),
                 "L_Forearm": (10, 0, 0), "R_Forearm": (10, 0, 0)})),
        (14, BLOCK),
    ],
}
# Stance reference, for checking the neutral pose on its own.
CLIPS["Stance_Idle"] = {"length": 2, "loop": True, "keys": [(0, REST), (2, REST)]}

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
    # Decimate must sit before the armature modifier to be applied.
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

    # Ground plane for shadows, camera at a front 3/4 view (character faces +X, left = +Y).
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

bpy.ops.wm.open_mainfile(filepath=BASE_BLEND)
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

def author(name, spec):
    """Create a fresh action for this clip on the armature and key every listed bone."""
    arm.animation_data_clear()
    reset_pose()
    touched = set(STANCE.keys())
    for _, pose in spec["keys"]:
        touched.update(pose.keys())
    for frame, pose in spec["keys"]:
        full = add(STANCE, pose)  # every key is an offset from the stance
        for b in touched:
            pb = arm.pose.bones.get(b)
            if pb is None:
                raise KeyError(f"{name}: bone {b} not in rig")
            rx, ry, rz = full.get(b, (0, 0, 0))
            pb.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
            pb.keyframe_insert("rotation_euler", frame=frame)
    act = arm.animation_data.action
    act.name = name
    act.use_fake_user = True
    sc.frame_start = 0
    sc.frame_end = spec["length"]
    # Smooth-but-snappy easing: Bezier auto-clamped is Blender's default; keep it.
    return act

def render_checks(name, spec):
    frames = sorted({f for f, _ in spec["keys"]} | {min(spec["length"], 3)})
    for f in frames:
        sc.frame_set(f)
        sc.render.filepath = os.path.join(RENDER_DIR, f"{name}_f{f:02d}.png")
        bpy.ops.render.render(write_still=True)

def export(name, spec):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(EXPORT_DIR, f"{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'ARMATURE'},
        add_leaf_bones=False,
        armature_nodetype='NULL',
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        path_mode='AUTO',
    )
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
