"""Build <Name>_Studio.blend - a character's rig, weapons mounted, ready to animate, export wired.

Usage: blender -b --python character_studio.py -- <Name> <Body.fbx> <out.blend> <Clips/Custom dir>

This is the file to ANIMATE IN. It opens on the armature in pose mode with the weapons already on
his hands, the scene at 30 fps, and two text blocks: EXPORT_CLIP (run it to send the current action
to Unity) and README (the workflow and the traps).

It replaces the Salaadin-only salaadin_studio.py. Every character whose body FBX has the standard
chain - root -> <Name>_Rig -> Hips - works here; that is Richard and Salaadin today. Bjorn and
Miyamoto are Tripo rigs with different bone names and have not been tried.

Run this again whenever the rig changes: it rebuilds the .blend from the body FBX, so any animation
saved in the old one is NOT carried over. Keep finished actions by exporting them.
"""
import bpy, sys, os

args = sys.argv[sys.argv.index("--") + 1:]
NAME, BODY, OUT_BLEND, CLIPS_DIR = args[:4]
os.makedirs(os.path.dirname(OUT_BLEND) or ".", exist_ok=True)

FPS = 30                              # what the *_clips.py generators export at; Unity reads SECONDS
RIG = f"{NAME}_Rig"                   # must match the body FBX's armature node, or the avatar fails
CLIP_ROOT = f"{NAME}_ClipRoot"        # must match BODY_NODE in that character's *_clips.py
REST_LEAD_SCENE = "restlead"          # CrucibleMixamoSetup drops frame 0 from a take by this name

# What to tell the animator about THIS character, beyond the shared workflow.
NOTES = {
    "Salaadin": """HIS STYLE
  He strikes with the falchion and nothing else - no thrusts, no shield bashes, no kicks. Where the
  other three answer a parry with a boot, his riposte is a saber counter. The shield blocks; it
  never strikes. Keep that when you add moves.

WHAT NOT TO COPY FROM HIS GENERATED CLIPS
  salaadin_clips.py builds his current clips by copying the PACK's bone rotations for the legs.
  That is what put him in the splits, and it is why LEG_DAMP = 0.55 exists in that script. Keying
  the legs yourself in here has none of that problem - it is the whole reason to animate by hand.""",

    "Richard": """HIS STYLE
  Sword and kite shield, the tank. He thrusts, he bashes with the shield and he charges behind it -
  the Pro Sword and Shield pack has none of those, which is why four of his clips are hand-made
  already (thrust, shield charge, shield bash, double-high) in ArtSource/Blender/richard_clips.py.

HE IS THE CLEANEST FIGHTER IN THE GAME
  Only 3 of his 22 states are played faster than 1.8x, against 16 for Bjorn and 12 for Miyamoto,
  because the Sword and Shield pack happens to be brisk. His one bad state is Twin Hew at x2.36.""",
}

# ---------------------------------------------------------------------------------------------
# the file
# ---------------------------------------------------------------------------------------------

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BODY)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
arm.name = RIG
meshes = [o for o in bpy.data.objects if o.type == 'MESH']

sc = bpy.context.scene
sc.render.fps = FPS
sc.frame_start, sc.frame_end = 1, 48
sc.frame_set(1)
sc.name = "Scene"                     # NOT restlead: the export renames a throwaway copy's scene

# the empty the exporter needs beside the armature, so Unity does not make the armature the file
# root and land Blender's 100x scale on the avatar's root (see export() in the *_clips.py scripts)
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
act = bpy.data.actions.new(f"{NAME}_NewClip")
arm.animation_data.action = act
if hasattr(act, "slots"):
    arm.animation_data.action_slot = act.slots.new(id_type='OBJECT', name=f"{NAME}_NewClip")

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
"{name}_" - CrucibleMixamoSetup keys off that to give the clip his body's avatar.

This does NOT touch your animation. It copies the rig, applies the rest-pose lead-in to the copy,
exports that, and throws it away.
"""
import bpy, os
from mathutils import Matrix

NAME = "{name}_NewClip"
OUT_DIR = r"{clips}"

RIG = "{rig}"
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
    src = bpy.data.objects.get(RIG)
    if src is None or src.animation_data is None or src.animation_data.action is None:
        raise RuntimeError(f"no action on {{RIG}} - key something first")
    if not NAME.startswith("{name}_"):
        raise RuntimeError("NAME must start with {name}_ or Unity gives the clip the wrong avatar")

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

    # The copy is born "{rig}.001", and that name goes into the FBX. Unity only validates a COPIED
    # avatar when the clip's node chain matches the body's exactly - root -> {rig} -> Hips - so a
    # stray .001 makes the clip fall back to its own avatar and arrive yawed. Lend the copy the
    # real name for the export and give it back after.
    src.name = RIG + "__editing"
    rig.name = RIG

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

    # Put everything back the way you left it. The throwaway copy has to GO first: while it still
    # holds the name, handing it back to src only gets src "{rig}.001", and the next export would
    # not find the rig at all.
    dead_action = rig.animation_data.action
    dead_data = rig.data
    bpy.data.objects.remove(rig)
    bpy.data.actions.remove(dead_action)
    bpy.data.armatures.remove(dead_data)
    src.name = RIG
    assert src.name == RIG, f"could not give {{RIG}} back to the rig you animate on"

    bpy.context.scene.name = scene_name
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, max(48, last)
    bpy.context.scene.frame_set(1)

    print(f"EXPORTED {{out}} ({{os.path.getsize(out)//1024}} KB, frames 0..{{last+1}})")


main()
'''.format(name=NAME, rig=RIG, clips=os.path.abspath(CLIPS_DIR),
           clip_root=CLIP_ROOT, rest_lead=REST_LEAD_SCENE)

README_SRC = '''{upper} - ANIMATION STUDIO
{rule}

WHAT IS IN HERE
  {rig}        his skeleton, in pose mode, ready to key
  his body and weapons   reference only, locked so you cannot grab them
  {clip_root}   an empty the FBX export needs. Leave it alone, do not key it.
  Scene is 30 fps. Keep it there - Unity reads a clip's length in SECONDS, so changing the frame
  rate silently re-times every move.

THE LOOP
  1. Key a pose on {rig}. Start at frame 1, not 0 - frame 0 is reserved (see below).
  2. Open EXPORT_CLIP in the text editor, set NAME (it must start with "{name}_"), Alt+P.
  3. In Unity: Crucible > Animation > Use Mixamo packs, then point a state at the new clip.
     For a move that already exists, the name is enough - re-running the menu item picks it up.

NAMING
  The name must start with "{name}_". CrucibleMixamoSetup uses that to give the clip HIS avatar
  (Based Upon: Original, Copy From Other). A clip named anything else gets the generic treatment
  and arrives yawed and stretched.

FRAME 0 IS NOT YOURS
  EXPORT_CLIP keys his rest pose at frame 0 and shifts your animation to 1.. on a throwaway copy.
  Blender writes the pose at the first exported frame as the file's bind pose, and Unity builds
  the clip's avatar from it - so a clip that opens in the guard makes THE GUARD his T-pose and
  everything comes back as a delta from the wrong place. Your own file is left untouched.

NO ROOT MOTION. ANIMATE IN PLACE.
  applyRootMotion is false on every fighter. Walking, dash distance, attack lunge and knockback
  are all driven by the game's code in world units. A clip that travels is ignored at best and
  fights the code at worst. If a move reads as a lunge, the travel is already in the game's lunge
  values - your clip supplies the lean and the footwork only.

HOW LONG A MOVE SHOULD BE
  Attacks are frame data at 60 fps in the game (startup / active / recovery on each AttackData).
  Use Mixamo packs then sets the animator state's speed to clip length / move length, so a clip
  longer than its move is simply played faster. At 30 fps in here, one game frame is half a
  Blender frame: a 24-frame move in the game is about 12 frames to key.

  Design/Animation_Handoff.pdf has the exact frame budget for every move in the game - total
  frames, the contact frame, and the Blender frame count to key to. Work from that table.

{notes}

IF THE RIG CHANGES
  This file is built from his body FBX by ArtSource/Blender/character_studio.py. Re-running that
  rebuilds it and does NOT carry actions over, so export anything you want to keep.
'''.format(upper=NAME.upper(), rule="=" * (len(NAME) + 19), name=NAME, rig=RIG,
           clip_root=CLIP_ROOT, notes=NOTES.get(NAME, "").strip())

for name, body in (("EXPORT_CLIP", EXPORT_SRC), ("README", README_SRC)):
    txt = bpy.data.texts.new(name)
    txt.write(body)

bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(OUT_BLEND))
print(f"STUDIO-OK {OUT_BLEND} ({os.path.getsize(OUT_BLEND)//1024} KB), "
      f"rig {RIG} {len(arm.data.bones)} bones, {len(meshes)} meshes, {FPS} fps")
