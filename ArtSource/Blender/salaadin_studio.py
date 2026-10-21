"""Build Salaadin_Studio.blend - his corrected rig, weapons mounted, export wired.

Usage: blender -b --python salaadin_studio.py -- <Salaadin_Body.fbx> <out.blend> <Clips/Custom dir>

This is the file to ANIMATE IN. It opens on the armature in pose mode with the falchion and shield
already on his hands, the scene at 30 fps, and two text blocks: EXPORT_CLIP (run it to send the
current action to Unity) and README (the workflow and the traps).

Run this script again whenever the rig changes - it rebuilds the .blend from the body FBX, so any
animation saved in the old one is NOT carried over. Keep finished actions by exporting them.
"""
import bpy, sys, os

args = sys.argv[sys.argv.index("--") + 1:]
BODY, OUT_BLEND, CLIPS_DIR = args[:3]
os.makedirs(os.path.dirname(OUT_BLEND) or ".", exist_ok=True)

FPS = 30                              # what salaadin_clips.py exports at; Unity reads clip SECONDS
CLIP_ROOT = "Salaadin_ClipRoot"       # must match BODY_NODE in salaadin_clips.py
REST_LEAD_SCENE = "restlead"          # CrucibleMixamoSetup drops frame 0 from a take by this name

# ---------------------------------------------------------------------------------------------
# the file
# ---------------------------------------------------------------------------------------------

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BODY)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
arm.name = "Salaadin_Rig"
meshes = [o for o in bpy.data.objects if o.type == 'MESH']

sc = bpy.context.scene
sc.render.fps = FPS
sc.frame_start, sc.frame_end = 1, 48
sc.frame_set(1)
sc.name = "Scene"                     # NOT restlead: the export renames a throwaway copy's scene

# the empty the exporter needs beside the armature, so Unity does not make the armature the file
# root and land Blender's 100x scale on the avatar's root (see export() in salaadin_clips.py)
if CLIP_ROOT not in bpy.data.objects:
    tag = bpy.data.objects.new(CLIP_ROOT, None)
    sc.collection.objects.link(tag)
    tag.empty_display_size = 0.25

# the body and the weapons are reference, not things to grab while keying
for m in meshes:
    m.hide_select = True

# start him on an action so there is somewhere for the first key to go. Blender 4.4+ actions hold
# their curves in a SLOT, and an action without one takes no keys at all, so give it one.
arm.animation_data_create()
act = bpy.data.actions.new("Salaadin_NewClip")
arm.animation_data.action = act
if hasattr(act, "slots"):
    arm.animation_data.action_slot = act.slots.new(id_type='OBJECT', name="Salaadin_NewClip")

arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
arm.data.display_type = 'OCTAHEDRAL'
arm.show_in_front = True

# ---------------------------------------------------------------------------------------------
# EXPORT_CLIP, as a text block inside the file
# ---------------------------------------------------------------------------------------------

EXPORT_SRC = '''"""Send the current action to Unity.  Run it with the cursor in here: Alt+P.

Set NAME below first. It becomes the FBX name AND how Unity finds the clip, so it must start with
"Salaadin_" - CrucibleMixamoSetup keys off that to give the clip his body's avatar.

This does NOT touch your animation. It copies the rig, applies the rest-pose lead-in to the copy,
exports that, and throws it away.
"""
import bpy, os
from mathutils import Matrix

NAME = "Salaadin_NewClip"
OUT_DIR = r"{clips}"

CLIP_ROOT = "{clip_root}"
REST_LEAD_SCENE = "{rest_lead}"


def curves(obj):
    """The action's f-curves. Blender 4.4+ keeps them in a slot; older versions on the action."""
    ad = obj.animation_data
    try:
        return ad.action.layers[0].strips[0].channelbag(ad.action_slot).fcurves
    except (AttributeError, IndexError):
        return ad.action.fcurves


def main():
    src = bpy.data.objects.get("Salaadin_Rig")
    if src is None or src.animation_data is None or src.animation_data.action is None:
        raise RuntimeError("no action on Salaadin_Rig - key something first")
    if not NAME.startswith("Salaadin_"):
        raise RuntimeError("NAME must start with Salaadin_ or Unity gives the clip the wrong avatar")

    action = src.animation_data.action
    last = int(max((k.co.x for fc in curves(src) for k in fc.keyframe_points), default=0))
    if last < 1:
        raise RuntimeError("the action has no keys past frame 0")

    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    # work on a copy, so the lead-in never touches what you are animating
    rig = src.copy()
    rig.data = src.data.copy()
    rig.animation_data_clear()
    rig.animation_data_create()
    rig.animation_data.action = action.copy()
    if hasattr(rig.animation_data.action, "slots") and len(rig.animation_data.action.slots):
        rig.animation_data.action_slot = rig.animation_data.action.slots[0]
    bpy.context.scene.collection.objects.link(rig)

    # The copy is born "Salaadin_Rig.001", and that name goes into the FBX. Unity only validates a
    # COPIED avatar when the clip's node chain matches the body's exactly - root -> Salaadin_Rig ->
    # Hips - so a stray .001 makes the clip fall back to its own avatar and arrive yawed. Lend the
    # copy the real name for the export and give it back after.
    src.name = "Salaadin_Rig__editing"
    rig.name = "Salaadin_Rig"

    # ---- the rest-pose lead-in -------------------------------------------------------------
    # Blender writes the pose at the FIRST exported frame as the file's node transforms, and an
    # armature-only FBX has no skin to say otherwise, so Unity builds the clip's avatar from
    # exactly those. A clip that opens in the guard makes THE GUARD the T-pose and every frame
    # comes back as the body's T-pose plus a delta. Frame 0 therefore holds the real rest pose,
    # the clip runs 1.., and the scene is named restlead so CrucibleMixamoSetup drops frame 0.
    for fc in curves(rig):
        for k in fc.keyframe_points:
            k.co.x += 1
            k.handle_left.x += 1
            k.handle_right.x += 1
        fc.update()
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix()
        pb.keyframe_insert("rotation_quaternion", frame=0)
        if pb.name.split(":")[-1] == "Hips":
            pb.keyframe_insert("location", frame=0)

    scene_name = bpy.context.scene.name
    bpy.context.scene.name = REST_LEAD_SCENE
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, last + 1
    bpy.context.scene.frame_set(0)        # the exporter takes the bind pose from where it sits
    bpy.context.view_layer.update()

    tag = bpy.data.objects.get(CLIP_ROOT)
    if tag is None:
        tag = bpy.data.objects.new(CLIP_ROOT, None)
        bpy.context.scene.collection.objects.link(tag)

    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    tag.select_set(True)
    bpy.context.view_layer.objects.active = rig

    out = os.path.join(OUT_DIR, NAME + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, object_types={{'ARMATURE', 'EMPTY'}},
        add_leaf_bones=False, armature_nodetype='NULL', bake_anim=True,
        bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='STRIP',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y')

    # put everything back the way you left it
    src.name = "Salaadin_Rig"
    bpy.context.scene.name = scene_name
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, max(48, last)
    dead_action = rig.animation_data.action
    dead_data = rig.data
    bpy.data.objects.remove(rig)
    bpy.data.actions.remove(dead_action)
    bpy.data.armatures.remove(dead_data)
    bpy.context.scene.frame_set(1)

    print(f"EXPORTED {{out}} ({{os.path.getsize(out)//1024}} KB, frames 0..{{last+1}})")


main()
'''.format(clips=os.path.abspath(CLIPS_DIR), clip_root=CLIP_ROOT, rest_lead=REST_LEAD_SCENE)

README_SRC = '''SALAADIN - ANIMATION STUDIO
===========================

WHAT IS IN HERE
  Salaadin_Rig        his corrected skeleton, in pose mode, ready to key
  Salaadin_Body + the falchion and shield   reference only, locked so you cannot grab them
  Salaadin_ClipRoot   an empty the FBX export needs. Leave it alone, do not key it.
  Scene is 30 fps. Keep it there - Unity reads a clip's length in SECONDS, so changing the frame
  rate silently re-times every move.

THE LOOP
  1. Key a pose on Salaadin_Rig. Start at frame 1, not 0 - frame 0 is reserved (see below).
  2. Open EXPORT_CLIP in the text editor, set NAME (it must start with "Salaadin_"), Alt+P.
  3. In Unity: Crucible > Animation > Use Mixamo packs, then point a state at the new clip.
     For a move that already exists, the name is enough - re-running the menu item picks it up.

NAMING
  The name must start with "Salaadin_". CrucibleMixamoSetup uses that to give the clip HIS avatar
  (Based Upon: Original, Copy From Other). A clip named anything else gets the generic treatment
  and arrives yawed and stretched.

FRAME 0 IS NOT YOURS
  EXPORT_CLIP keys his rest pose at frame 0 and shifts your animation to 1.. on a throwaway copy.
  Blender writes the pose at the first exported frame as the file's bind pose, and Unity builds
  the clip's avatar from it - so a clip that opens in the guard makes THE GUARD his T-pose and
  everything comes back as a delta from the wrong place. Your own file is left untouched.

HOW LONG A MOVE SHOULD BE
  Attacks are frame data at 60 fps in the game (startup / active / recovery on each AttackData),
  and Unity stretches your clip to fit that window. So a clip that is much longer than its move
  gets sped up - his Medium and Heavy currently run at about x2.7, which is most of why they look
  clumsy. At 30 fps here, one game frame is half a Blender frame: a 24-frame move in the game is
  about 12 frames in here. Key to that and the speed-up goes away.

WHAT NOT TO COPY FROM THE OTHER CLIPS
  salaadin_clips.py builds his current clips by copying the PACK's bone rotations for the legs.
  That is what put him in the splits, and it is why LEG_DAMP exists. Keying the legs yourself
  here has none of that problem - it is the whole reason to animate by hand.

IF THE RIG CHANGES
  This file is built from Salaadin_Body.fbx by ArtSource/Blender/salaadin_studio.py. Re-running
  that rebuilds it and does NOT carry actions over, so export anything you want to keep.
'''

for name, body in (("EXPORT_CLIP", EXPORT_SRC), ("README", README_SRC)):
    txt = bpy.data.texts.new(name)
    txt.write(body)

bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(OUT_BLEND))
print(f"STUDIO-OK {OUT_BLEND} ({os.path.getsize(OUT_BLEND)//1024} KB), "
      f"rig {len(arm.data.bones)} bones, {len(meshes)} meshes, {FPS} fps")
