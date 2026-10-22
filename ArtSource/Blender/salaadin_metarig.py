"""Turn the user's hand-built Rigify METARIG of Salaadin into a Mixamo-named rigged FBX that the
rest of the pipeline (salaadin_body.py -> Unity Rebuild Player4 -> Use Mixamo packs) can eat.

    blender -b <salahtest_rigged.blend> --python salaadin_metarig.py -- <out.fbx> <render_dir>

The source is `ArtSource/Blender/salahtest_rigged.blend`: one armature called `metarig`, 67 bones
with Rigify metarig names, and the knight mesh skinned to it. It is a good rig - perfectly
symmetric, hips at 51.5% of height, ankles at 5.5%, a 163 deg knee break and a 165 deg elbow break
for the retargeter to read - but four things stop Unity from using it:

1. **The bone names are Rigify's, not Mixamo's.** Nothing in the C# resolves a bone by name (it
   all goes through `Animator.GetBoneTransform(HumanBodyBones.*)`), but that only works once
   Unity's humanoid AUTO-MAPPER has filled the slots, and the mapper is a name heuristic. Mixamo
   names are the one convention this project has proved fills 15/15 finger slots unaided.
   Everything Unity must map gets a `mixamorig:` name; every extra bone gets a short neutral name
   with no humanoid keyword in it (`L_Meta1`, `L_Heel`, `L_Skirt`, `L_Pec`, `N_Mid`), because a
   bone called "Pelvis" or "Palm" invites the mapper to put it in the wrong slot.

2. **The armature object carries an unapplied scale of 0.48.** Baked into the bone data here so
   the exported FBX has the root(1) -> Rig(100) -> Hips shape the other three bodies have.

3. **384 verts (7.3%) carry no weight at all** - a band down the front of the coat, z 0.36-0.81.
   They would have stayed in the bind pose while the torso moved. Each one borrows the weights of
   its nearest weighted neighbour, so the skin keeps its local behaviour.

4. **No leaf bones.** An FBX does not store bone TAILS (learned the hard way on his feet and
   head), so a bone with no child has its direction INVENTED on re-import. Mixamo rigs carry real
   `*_End` / `*4` leaves for exactly this reason; they are added here for the head, both toes and
   all ten finger tips.

The two hand bones also get their roll set so bone-Z points at the floor in the rest pose, which
is the convention `salaadin_body.py`'s `hand_frame()` reads ("palm = the hand bone's Z; in the
T-pose rest it points DOWN"). Without it the falchion and the shield mount against an arbitrary
roll. The final say on the mount is still Unity's `ShieldWeaponMount`.
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix, kdtree

args = sys.argv[sys.argv.index("--") + 1:]
OUT = args[0]
RENDER_DIR = args[1] if len(args) > 1 else ""
os.makedirs(os.path.dirname(OUT), exist_ok=True)
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
print(f"source: armature {arm.name!r} {len(arm.data.bones)} bones, mesh {body.name!r} "
      f"{len(body.data.vertices)} verts, {len(body.vertex_groups)} groups")

# --- 1. weights: give the orphans their nearest neighbour's -----------------------------------
weighted = [v for v in body.data.vertices if sum(g.weight for g in v.groups) > 1e-5]
orphans = [v for v in body.data.vertices if sum(g.weight for g in v.groups) <= 1e-5]
if orphans:
    kd = kdtree.KDTree(len(weighted))
    for i, v in enumerate(weighted):
        kd.insert(v.co, i)
    kd.balance()
    gname = {g.index: g.name for g in body.vertex_groups}
    moved = 0
    for v in orphans:
        _, idx, _ = kd.find(v.co)
        src = weighted[idx]
        for g in src.groups:
            if g.weight > 1e-4:
                body.vertex_groups[gname[g.group]].add([v.index], g.weight, 'REPLACE')
        moved += 1
    print(f"weights: {moved} orphan verts took their nearest weighted neighbour's weights")
else:
    print("weights: no orphans")

# --- 2. bake the object transforms into the data ----------------------------------------------
# ArmatureData.transform / Mesh.transform move the rest data itself, so afterwards both objects
# sit at the identity and the bone rest positions are the world ones the audit measured.
# ORDER MATTERS: matrix_world is a derived property, so assigning it solves for matrix_basis
# against the parent inverse as it stands. Clear the parent inverse FIRST or the 1/0.48 hiding in
# it comes straight back into the basis and the body re-imports at half the skeleton's size.
Mw_arm = arm.matrix_world.copy()
Mw_body = body.matrix_world.copy()
arm.data.transform(Mw_arm)
arm.matrix_world = Matrix.Identity(4)
body.data.transform(Mw_body)
body.matrix_parent_inverse = Matrix.Identity(4)
body.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()
h_body = max((body.matrix_world @ v.co).z for v in body.data.vertices) \
    - min((body.matrix_world @ v.co).z for v in body.data.vertices)
h_rig = max(b.head_local.z for b in arm.data.bones) - min(b.head_local.z for b in arm.data.bones)
assert abs(h_body - 0.9995) < 1e-2, f"body is {h_body:.4f} tall, expected ~1.0 - the bake went wrong"
assert abs(arm.matrix_world.to_scale()[0] - 1.0) < 1e-4, "armature still carries a scale"
print(f"scale: baked armature {tuple(round(v,3) for v in Mw_arm.to_scale())} into the bone data")

# --- 3. dissolve the spare neck bone ----------------------------------------------------------
# The metarig's spine is SEVEN bones and Unity's humanoid spine has SIX slots (Hips, Spine, Chest,
# UpperChest, Neck, Head). Leaving the spare in with a neutral name does not work: Unity's
# auto-mapper walks the chain greedily, so it took the spare for Head and put the REAL head bone
# in the RIGHT EYE slot - verified on the first import, 53 bones mapped with `Head <- N_Mid` and
# `RightEye <- mixamorig:Head`. Head rotation would have driven the lower neck while the head
# itself counter-rotated as an eye. So spine.005 is dissolved into spine.004: its weights move to
# its parent and the parent's tail extends to spine.006's head, giving one neck bone the way a
# Mixamo rig has one.
SPARE, KEEP = "spine.005", "spine.004"
gk = body.vertex_groups.get(KEEP) or body.vertex_groups.new(name=KEEP)
gs = body.vertex_groups.get(SPARE)
if gs:
    moved = 0
    for v in body.data.vertices:
        w = next((g.weight for g in v.groups if g.group == gs.index), 0.0)
        if w > 1e-4:
            have = next((g.weight for g in v.groups if g.group == gk.index), 0.0)
            gk.add([v.index], min(1.0, have + w), 'REPLACE')
            moved += 1
    body.vertex_groups.remove(gs)
    print(f"neck: {moved} verts moved from {SPARE} to {KEEP}")

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
_eb = arm.data.edit_bones
_spare, _keep = _eb[SPARE], _eb[KEEP]
_kids = [b for b in _eb if b.parent == _spare]
_keep.tail = _spare.tail.copy()
for b in _kids:
    b.parent = _keep
    b.use_connect = True
_eb.remove(_spare)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()
print(f"neck: dissolved {SPARE}; {KEEP} now spans to {tuple(round(v,3) for v in arm.data.bones[KEEP].tail_local)}, "
      f"children {[b.name for b in _kids]}")

# --- 4. the name map --------------------------------------------------------------------------
M = "mixamorig:"
NAMES = {
    # six spine bones, six Unity slots, nothing spare for the mapper to grab
    "spine": M + "Hips", "spine.001": M + "Spine", "spine.002": M + "Spine1",
    "spine.003": M + "Spine2", "spine.004": M + "Neck",
    "spine.006": M + "Head",
    "breast.L": "L_Pec", "breast.R": "R_Pec",
    "pelvis.L": "L_Skirt", "pelvis.R": "R_Skirt",
}
for s, S in (("L", "Left"), ("R", "Right")):
    NAMES.update({
        f"shoulder.{s}":  M + S + "Shoulder",
        f"upper_arm.{s}": M + S + "Arm",
        f"forearm.{s}":   M + S + "ForeArm",
        f"hand.{s}":      M + S + "Hand",
        f"thigh.{s}":     M + S + "UpLeg",
        f"shin.{s}":      M + S + "Leg",
        f"foot.{s}":      M + S + "Foot",
        f"toe.{s}":       M + S + "ToeBase",
        f"heel.02.{s}":   f"{s}_Heel",
    })
    for n, (meta, finger) in enumerate(
            (("palm.01", "Index"), ("palm.02", "Middle"), ("palm.03", "Ring"), ("palm.04", "Pinky")), 1):
        NAMES[f"{meta}.{s}"] = f"{s}_Meta{n}"
    for src, finger in (("f_index", "Index"), ("f_middle", "Middle"),
                        ("f_ring", "Ring"), ("f_pinky", "Pinky"), ("thumb", "Thumb")):
        for i in (1, 2, 3):
            NAMES[f"{src}.0{i}.{s}"] = f"{M}{S}Hand{finger}{i}"

missing = [b.name for b in arm.data.bones if b.name not in NAMES]
extra = [k for k in NAMES if k not in arm.data.bones]
if missing or extra:
    raise SystemExit(f"NAME MAP MISMATCH\n  unmapped bones: {missing}\n  map keys with no bone: {extra}")

# Rename through a temporary prefix: some targets could otherwise collide with a source name.
for b in arm.data.bones:
    b.name = "~" + b.name
for src, dst in NAMES.items():
    arm.data.bones["~" + src].name = dst
# vertex groups follow the bones automatically; make sure of it
vg = {g.name for g in body.vertex_groups}
bad = [b.name for b in arm.data.bones if b.name not in vg]
print(f"renamed {len(NAMES)} bones; bones with no matching vertex group: {bad}")

# --- 5. leaf bones, and the hand roll ---------------------------------------------------------
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones

LEAVES = [(M + "Head", M + "HeadTop_End"),
          (M + "LeftToeBase", M + "LeftToe_End"),
          (M + "RightToeBase", M + "RightToe_End")]
for S in ("Left", "Right"):
    for finger in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
        LEAVES.append((f"{M}{S}Hand{finger}3", f"{M}{S}Hand{finger}4"))

for parent, leaf in LEAVES:
    p = eb[parent]
    n = eb.new(leaf)
    n.head = p.tail.copy()
    n.tail = p.tail + (p.tail - p.head).normalized() * max(p.length * 0.25, 1e-3)
    n.roll = p.roll
    n.parent = p
    n.use_connect = True
    n.use_deform = False
print(f"leaves: added {len(LEAVES)} end bones so the FBX cannot invent their parents' directions")

for hand in (M + "LeftHand", M + "RightHand"):
    eb[hand].align_roll(Vector((0, 0, -1)))     # bone Z -> floor, the hand_frame() convention
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()
for hand in (M + "LeftHand", M + "RightHand"):
    b = arm.data.bones[hand]
    mz = b.matrix_local.to_3x3() @ Vector((0, 0, 1))
    print(f"  {hand}: bone Z . down = {mz.dot(Vector((0,0,-1))):+.3f}")

arm.name = arm.data.name = "Salaadin_Rig"
body.name = body.data.name = "Salaadin_Body"
if body.data.materials:
    body.data.materials[0].name = "Salaadin_Body"

# --- 6. report ---------------------------------------------------------------------------------
zs = [v.co.z for v in body.data.vertices]
H = max(zs) - min(zs)


def head(n):
    return arm.data.bones[n].head_local


print(f"\nrig: {len(arm.data.bones)} bones, body {H:.4f} tall")
for S in ("Left", "Right"):
    sh, el, wr = head(M + S + "Arm"), head(M + S + "ForeArm"), head(M + S + "Hand")
    hip, kn, an = head(M + S + "UpLeg"), head(M + S + "Leg"), head(M + S + "Foot")
    print(f"  {S:5s} elbow {100*(el-sh).length/(wr-sh).length:4.1f}% along, "
          f"knee {100*(kn-hip).length/(an-hip).length:4.1f}% along, "
          f"hip {100*(hip.z-min(zs))/H:4.1f}% up, ankle {100*(an.z-min(zs))/H:4.1f}% up")

# --- 7. export, with the project's exact options ----------------------------------------------
if arm.animation_data:
    arm.animation_data_clear()
arm.data.pose_position = 'REST'
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", OUT, f"({os.path.getsize(OUT)//1024} KB)")
print("METARIG-OK")
