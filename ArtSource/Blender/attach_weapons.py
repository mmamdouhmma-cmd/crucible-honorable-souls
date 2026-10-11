"""Attach prepared weapon meshes to a prepared body FBX by bone-parenting, then re-export the body FBX.

Usage: blender -b --python attach_weapons.py -- <clean_body.fbx> <out_body.fbx> <render_dir> <weapon.fbx>:<Hand>:<flip> [...]
  (<clean_body.fbx> is prep_body.py output kept in ArtSource/Characters/Prepped; the fists are baked into the mesh,
   so never feed an already-attached body back in. "none:<Hand>:0" bakes a fist without a weapon.)

  <Hand>  R_Hand or L_Hand
  <flip>  0 = as authored, 1 = mirror the mesh across its own X (a left-hand copy of a right-hand weapon)

Grip solve, in the hand bone's frame (Tripo rig, rest pose with palms down):
  - "along"  = wrist -> fingers (bone Y axis)
  - "palm"   = the side the palm faces, found from the mesh: the hand-weighted vertices' centroid
               relative to the bone line (the back of the hand is bonier, so the centroid sits palm-side)
  - "thumb"  = along x palm, right-handed, flipped for the left hand so the thumb is on the inside
  The handle (weapon +Z, grip at the weapon origin) runs along the thumb axis, head toward the thumb,
  the grip sits GRIP_ALONG of the way down the hand and HANDLE_R toward the palm. Weapon +Y (edge)
  points away from the fingers so a hanging axe/sword shows its edge forward of the fist.

A check render of each hand is written to <render_dir>/<Body>_<Hand>.png (camera looking at the hand).
"""
import bpy, sys, os
import numpy as np
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
BODY, OUT, RENDER_DIR = args[0], args[1], args[2]
WEAPONS = [a.rsplit(":", 2) for a in args[3:]]
HANDLE_R = 0.012       # handle half-thickness in file units (body is ~0.98 tall)
KNUCKLE = 0.55         # fingers start this far along the wrist->fingertip extent
BEND_R = 0.016         # curl radius for the fist: handle half-thickness + finger half-thickness
MAX_BEND = 3.4         # radians, ~195 degrees of wrap
import math

os.makedirs(RENDER_DIR, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BODY)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o is not body]:
    print('removing previously attached', o.name)
    bpy.data.objects.remove(o)
name = os.path.splitext(os.path.basename(OUT))[0].replace("_Body", "").replace("_clean", "")

def hand_frame(hand):
    """World-space grip matrix for a hand bone: columns = (thumb, palm, along) ... built as a Matrix."""
    b = arm.data.bones[hand]
    M = arm.matrix_world @ b.matrix_local            # bone rest matrix in world (Y = along the bone)
    head = arm.matrix_world @ b.head_local
    tail = arm.matrix_world @ b.tail_local
    along = (tail - head).normalized()
    # palm side from the skinned mesh: centroid of verts weighted mostly to this hand
    vg = body.vertex_groups.get(hand)
    pts = []
    if vg:
        gi = vg.index
        for v in body.data.vertices:
            for g in v.groups:
                if g.group == gi and g.weight > 0.6:
                    pts.append(body.matrix_world @ v.co)
                    break
    pts = np.array([p[:] for p in pts]) if pts else np.zeros((0, 3))
    c = Vector(pts.mean(0)) if len(pts) else (head + tail) / 2
    off = c - head
    off = off - along * off.dot(along)             # perpendicular offset of the centroid from the bone line
    # palm faces the way the centroid leans only weakly; fall back to world -Z (palms down in the Tripo A-pose)
    palm = off.normalized() if off.length > 1e-4 else Vector((0, 0, -1))
    if palm.dot(Vector((0, 0, -1))) < 0:
        palm = -palm
    # Tripo A-pose: palms down, so both thumbs point forward. Thumb = world forward made perpendicular to
    # the bone; palm = thumb x along, flipped if it points up. (The mesh-centroid estimate above only
    # seeds the fallback.)
    fwd = Vector((1, 0, 0))
    thumb = (fwd - along * fwd.dot(along)).normalized()
    palm = thumb.cross(along).normalized()
    if palm.z > 0:
        palm = -palm
    # fingertip extent from the mesh (the two hand bones are not the same length on a Tripo auto-rig)
    tip = max(((Vector(p) - head).dot(along) for p in pts), default=(tail - head).length * 1.4)
    knuckle = KNUCKLE * tip
    grip = head + along * (knuckle - 0.012) + palm * HANDLE_R
    return grip, thumb, palm, along, pts

def make_fist(hand, grip, thumb, palm, along):
    """Bake a closed grip: every vertex weighted to the hand that lies beyond the knuckle line is wrapped
    around the handle axis (through `grip`, direction along x palm) like a bend deformer, so the fingers
    curl from the back of the hand, forward, to under the handle. The thumb mostly sits before the
    knuckle line and is left lying along the haft."""
    b = arm.data.bones[hand]
    head = arm.matrix_world @ b.head_local
    hand_len = (arm.matrix_world @ b.tail_local - head).length
    knuckle = (grip - head).dot(along) + 0.012          # same knuckle line hand_frame used for the grip
    axis = along.cross(palm).normalized()
    vg = body.vertex_groups.get(hand)
    if vg is None:
        print(f"{hand}: no vertex group, fist skipped"); return 0
    gi = vg.index
    mw = body.matrix_world; inv = mw.inverted()
    n = 0
    for v in body.data.vertices:
        w = 0.0
        for g in v.groups:
            if g.group == gi: w = g.weight; break
        if w < 0.12: continue
        p = mw @ v.co
        s = (p - head).dot(along) - knuckle
        if s <= 0: continue
        theta = min(s / BEND_R, MAX_BEND)
        q = p - along * s
        new = grip + Matrix.Rotation(theta, 4, axis) @ (q - grip)
        v.co = inv @ new
        n += 1
    body.data.update()
    print(f"{hand}: fist baked on {n} finger verts (knuckle at {knuckle:.3f}, hand len {hand_len:.3f})")
    return n

for wpath, hand, flip in WEAPONS:
    flip = int(flip)
    if wpath.lower() == "none":
        grip, thumb, palm, along, pts = hand_frame(hand)
        make_fist(hand, grip, thumb, palm, along)
        continue
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=wpath)
    wobj = next(o for o in set(bpy.data.objects) - before if o.type == 'MESH')
    for o in set(bpy.data.objects) - before - {wobj}:
        bpy.data.objects.remove(o)
    wobj.parent = None
    wobj.matrix_world = Matrix.Identity(4)
    if flip:
        wobj.scale.x = -1
        bpy.context.view_layer.objects.active = wobj
        wobj.select_set(True)
        bpy.ops.object.transform_apply(scale=True)
        # flip normals back
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.flip_normals(); bpy.ops.object.mode_set(mode='OBJECT')
        wobj.select_set(False)
    wobj.name = f"{name}_{os.path.splitext(os.path.basename(wpath))[0].split('_',1)[-1]}_{hand[0]}"
    grip, thumb, palm, along, pts = hand_frame(hand)
    make_fist(hand, grip, thumb, palm, along)
    # weapon local axes: +Z handle -> thumb direction (head toward the thumb), +Y edge -> +along, the way the
    # knuckles point (a blade or axe bit cuts in the direction the fingers point, not toward the wrist)
    zax = thumb
    yax = along
    xax = yax.cross(zax).normalized()
    R = Matrix((xax, yax, zax)).transposed()   # columns = local axes in world
    M = Matrix.Translation(grip) @ R.to_4x4()
    wobj.matrix_world = M
    # bone parenting keeps the world transform
    wobj.parent = arm
    wobj.parent_type = 'BONE'
    wobj.parent_bone = hand
    # with parent_type BONE Blender measures from the bone TAIL; matrix_parent_inverse fixes the offset
    bpy.context.view_layer.update()
    wobj.matrix_parent_inverse = (arm.matrix_world @ arm.pose.bones[hand].matrix).inverted() if False else wobj.matrix_parent_inverse
    wobj.matrix_world = M
    bpy.context.view_layer.update()
    print(f"{wobj.name}: parented to {hand}; grip {tuple(round(v,3) for v in grip)} thumb {tuple(round(v,2) for v in thumb)} palm {tuple(round(v,2) for v in palm)} along {tuple(round(v,2) for v in along)}; hand verts {len(pts)}")

    # check render: camera orbiting the hand
    scene = bpy.context.scene
    cam = bpy.data.objects.new("CheckCam", bpy.data.cameras.new("CheckCam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    look = grip
    side = Vector((0, 1 if hand.startswith("R_") else -1, 0))
    cam.location = look + Vector((0.32, 0, 0.14)) + side * -0.18
    cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 50
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    body.color = (0.6, 0.6, 0.6, 1.0)
    wobj.color = (0.9, 0.1, 0.1, 1.0)
    light = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    scene.collection.objects.link(light)
    light.rotation_euler = (0.9, 0.3, 0.6)
    light.data.energy = 3
    for eng in ('BLENDER_WORKBENCH',):
        try:
            scene.render.engine = eng; break
        except TypeError:
            pass
    scene.render.resolution_x = 900; scene.render.resolution_y = 700
    scene.render.filepath = os.path.join(RENDER_DIR, f"{name}_{hand}.png")
    bpy.ops.render.render(write_still=True)
    print("render:", scene.render.filepath)
    bpy.data.objects.remove(cam); bpy.data.objects.remove(light)

# export body + armature + weapons
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('MESH', 'ARMATURE'):
        o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", OUT, f"({os.path.getsize(BODY)//(1024*1024)} MB) objects {[o.name for o in bpy.data.objects if o.type=='MESH']}")
print("ATTACH-OK")
