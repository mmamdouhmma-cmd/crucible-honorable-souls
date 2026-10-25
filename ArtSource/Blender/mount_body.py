"""Build a Unity body FBX for Genghis or Buwaka: the metarig-rigged body with the prepped
weapons bone-parented to the hands. Generalised from salaadin_body.py; same hand-frame
conventions (metarig_to_mixamo.py sets both hand bones' roll so bone-Z points at the floor):

    along  = the hand bone's own Y, wrist -> knuckles
    palm   = the hand bone's Z; in the T-pose rest it points DOWN, the way the palms face
    thumb  = the hand bone's X for the right hand, mirrored for the left

Usage: blender -b --python mount_body.py -- <Name> <rigged.fbx> <weapons_dir> <out_fbx> <render_dir>

NOTE: the final say on a mount is Unity's ShieldWeaponMount in CrucibleCharacterSetup.cs,
re-applied after every Rebuild Player<N>. Get it close here; tune it there.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
NAME, RIG, WEAPONS_DIR, OUT, RENDER_DIR = args[:5]
os.makedirs(RENDER_DIR, exist_ok=True)
os.makedirs(os.path.dirname(OUT), exist_ok=True)

# position: along the bone / off its axis toward palm / thumb, in file units (bodies are 1.0 tall).
# axis_z / axis_y: which hand-frame axis the weapon's local +Z (handle) and +Y map to.
MOUNT = {
    "Genghis": [
        # Saber: handle along the thumb axis, edge out along the knuckles - Salaadin's falchion
        # mount minus his -20 deg blade-lean tweak (his falchion is mounted canted; this one is not).
        dict(file="Genghis_Saber.fbx", name="Genghis_Saber_R", hand="mixamorig:RightHand",
             along=0.030, palm=0.006, thumb=0.0,
             axis_z="thumb", axis_y="along", tweak=(0.0, 0.0, 0.0)),
    ],
    "Buwaka": [
        # Spear: mid-haft grip, haft along the thumb axis like any one-handed weapon.
        dict(file="Buwaka_Spear.fbx", name="Buwaka_Spear_R", hand="mixamorig:RightHand",
             along=0.030, palm=0.006, thumb=0.0,
             axis_z="thumb", axis_y="along", tweak=(0.0, 0.0, 0.0)),
        # Round centre-grip shield, same source model and prep as Salaadin's: +Y is the boss,
        # the fist sits at the disc centre, 0.042 below the origin at height 0.35.
        dict(file="Buwaka_Shield.fbx", name="Buwaka_Shield_L", hand="mixamorig:LeftHand",
             along=0.034, palm=-0.022, thumb=0.042,
             axis_z="thumb", axis_y="-palm", tweak=(0.0, 0.0, 0.0)),
    ],
}[NAME]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=RIG)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
arm.data.pose_position = 'REST'
bpy.context.view_layer.update()
arm.name = arm.data.name = NAME + "_Rig"
body.name = body.data.name = NAME + "_Body"
if body.data.materials:
    body.data.materials[0].name = NAME + "_Body"
print(f"rig {len(arm.data.bones)} bones, body {len(body.data.vertices)} verts")


def hand_frame(bone_name):
    b = arm.data.bones[bone_name]
    M = (arm.matrix_world @ b.matrix_local).to_3x3()
    head = arm.matrix_world @ b.head_local
    x = (M @ Vector((1, 0, 0))).normalized()
    along = (M @ Vector((0, 1, 0))).normalized()
    palm = (M @ Vector((0, 0, 1))).normalized()
    thumb = x if "Right" in bone_name else -x
    return head, thumb, palm, along


def mount(spec):
    path = os.path.join(WEAPONS_DIR, spec["file"])
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=path)
    obj = next(o for o in set(bpy.data.objects) - before if o.type == 'MESH')
    for o in set(bpy.data.objects) - before - {obj}:
        bpy.data.objects.remove(o)
    obj.name = obj.data.name = spec["name"]
    if obj.data.materials:
        obj.data.materials[0].name = os.path.splitext(spec["file"])[0]
    head, thumb, palm, along = hand_frame(spec["hand"])
    axes = {"thumb": thumb, "-thumb": -thumb, "palm": palm, "-palm": -palm,
            "along": along, "-along": -along}
    zax = axes[spec["axis_z"]]
    yax = axes[spec["axis_y"]]
    yax = (yax - zax * yax.dot(zax)).normalized()
    xax = yax.cross(zax).normalized()
    R = Matrix((xax, yax, zax)).transposed().to_4x4()
    R = R @ Matrix.Rotation(math.radians(spec["tweak"][0]), 4, 'X') \
          @ Matrix.Rotation(math.radians(spec["tweak"][1]), 4, 'Y') \
          @ Matrix.Rotation(math.radians(spec["tweak"][2]), 4, 'Z')
    grip = head + along * spec["along"] + palm * spec["palm"] + thumb * spec["thumb"]
    want = Matrix.Translation(grip) @ R
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = spec["hand"]
    obj.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    pb = arm.pose.bones[spec["hand"]]
    bone_space = arm.matrix_world @ pb.matrix @ Matrix.Translation(Vector((0, pb.bone.length, 0)))
    obj.matrix_basis = bone_space.inverted() @ want
    bpy.context.view_layer.update()
    print(f"{spec['name']}: on {spec['hand']} at {tuple(round(v,3) for v in grip)}")
    return obj


weapons = [mount(s) for s in MOUNT]

# --- check renders -----------------------------------------------------------------------------
scn = bpy.context.scene
scn.render.resolution_x, scn.render.resolution_y = 640, 760
try:
    scn.render.engine = 'BLENDER_WORKBENCH'
except TypeError as e:
    print(e)
scn.display.shading.light = 'STUDIO'
scn.display.shading.color_type = 'OBJECT'
body.color = (0.55, 0.55, 0.58, 1)
tints = [(0.85, 0.75, 0.25, 1), (0.8, 0.2, 0.2, 1)]
for i, w in enumerate(weapons):
    w.color = tints[i % len(tints)]
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scn.collection.objects.link(cam)
scn.camera = cam
cam.data.type = 'ORTHO'


def render(tag):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in [body] + weapons:
        ev = o.evaluated_get(dg)
        for c in ev.bound_box:
            w = ev.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    ctr = (lo + hi) / 2
    size = hi - lo
    for view, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0)))):
        cam.location = ctr + d * max(size) * 3
        cam.data.ortho_scale = max(size) * 1.1
        cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(RENDER_DIR, f"{NAME}_{tag}_{view}.png")
        bpy.ops.render.render(write_still=True)
        print("render:", scn.render.filepath)


render("rest")

# pose check: borrow the pack guard (same mixamorig names)
GUARD = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(OUT)))),
                     "Animation/Clips/Mixamo/SwordShieldPack/sword and shield block idle.fbx")
if os.path.exists(GUARD):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=GUARD)
    added = set(bpy.data.objects) - before
    src = next((o for o in added if o.type == 'ARMATURE'), None)
    act = src.animation_data.action if src and src.animation_data else None
    if act:
        arm.animation_data_create()
        arm.animation_data.action = act
        if hasattr(act, "slots") and act.slots:
            arm.animation_data.action_slot = act.slots[0]
        arm.data.pose_position = 'POSE'
        bpy.context.scene.frame_set(20)
        bpy.context.view_layer.update()
    for o in added:
        bpy.data.objects.remove(o)
    bpy.context.view_layer.update()
    render("guard")

arm.data.pose_position = 'REST'
bpy.context.view_layer.update()

# --- export ------------------------------------------------------------------------------------
if arm.animation_data:
    arm.animation_data_clear()
bpy.ops.object.select_all(action='DESELECT')
for o in [arm, body] + weapons:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.data.objects.remove(cam)
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", OUT, f"({os.path.getsize(OUT)//1024} KB)")
print("MOUNT-OK")
