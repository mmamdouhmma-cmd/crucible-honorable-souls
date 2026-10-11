"""Prepare a Tripo RIGGED body FBX (from rig_model, textures embedded) for Unity.

Usage: blender -b --python prep_body.py -- <rigged.fbx> <out_dir> <Name> <target_verts>

- Imports the rigged FBX (armature + skinned mesh, embedded textures).
- Decimates to about <target_verts> keeping the skin weights.
- Renames armature "<Name>_Rig", mesh "<Name>_Body", material "<Name>_Body".
- Writes <out_dir>/<Name>_Body.fbx and textures into <out_dir>/<Name>_Body.fbm/:
    <name>_basecolor.jpg, <name>_normal.png,
    <name>_mask.png  (R metallic, G AO=1, B 0, A smoothness = 1 - roughness)  for URP/HDRP mask slots.
- Prints bone count and rest-pose hand heights so the A/T pose can be checked.
"""
import bpy, sys, os
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_DIR, NAME, TARGET = args[0], args[1], args[2], int(args[3])
os.makedirs(OUT_DIR, exist_ok=True)
fbm = os.path.join(OUT_DIR, NAME + "_Body.fbm")
os.makedirs(fbm, exist_ok=True)
lname = NAME.lower()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
print(f"{NAME}: armature {len(arm.data.bones)} bones; mesh {len(mesh.data.vertices)} verts")
for bn in ("R_Hand", "L_Hand", "R_Upperarm", "Head"):
    b = arm.data.bones.get(bn)
    if b:
        print(f"  rest {bn}: head z {b.head_local.z:.3f}  y {b.head_local.y:.3f}")

# --- decimate (keeps vertex groups) --------------------------------------------
before = len(mesh.data.vertices)
ratio = min(1.0, TARGET / max(before, 1))
bpy.context.view_layer.objects.active = mesh
mesh.select_set(True)
dec = mesh.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = ratio
dec.use_collapse_triangulate = True
bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)
bpy.ops.object.modifier_apply(modifier="Decimate")
print(f"{NAME}: decimated {before} -> {len(mesh.data.vertices)} verts (ratio {ratio:.3f})")

# --- names ---------------------------------------------------------------------------
arm.name = NAME + "_Rig"; arm.data.name = NAME + "_Rig"
mesh.name = NAME + "_Body"; mesh.data.name = NAME + "_Body"
mat = mesh.data.materials[0]
mat.name = NAME + "_Body"

# --- textures: find by the socket they feed ------------------------------------
imgs = {}
for node in mat.node_tree.nodes:
    if node.type == 'TEX_IMAGE' and node.image:
        for link in node.outputs[0].links:
            sock = link.to_socket.name
            tgt = link.to_node
            if sock == 'Base Color': imgs['color'] = node.image
            elif sock == 'Roughness': imgs['rough'] = node.image
            elif sock == 'Metallic': imgs['metal'] = node.image
            elif tgt.type == 'NORMAL_MAP': imgs['normal'] = node.image
        nm = node.image.name.lower()
        if 'normal' in nm: imgs['normal'] = node.image
print("  textures found:", {k: v.name for k, v in imgs.items()})

def save(img, path, colorspace, fmt):
    img.colorspace_settings.name = colorspace
    img.filepath_raw = path
    img.file_format = fmt
    img.save()
    return path

written = []
if 'color' in imgs:
    written.append(save(imgs['color'], os.path.join(fbm, lname + "_basecolor.jpg"), 'sRGB', 'JPEG'))
if 'normal' in imgs:
    written.append(save(imgs['normal'], os.path.join(fbm, lname + "_normal.png"), 'Non-Color', 'PNG'))
if 'metal' in imgs and 'rough' in imgs:
    met, rough = imgs['metal'], imgs['rough']
    met.colorspace_settings.name = 'Non-Color'; rough.colorspace_settings.name = 'Non-Color'
    w, h = met.size
    if rough.size[0] != w or rough.size[1] != h:
        rough.scale(w, h)
    m = np.array(met.pixels[:], dtype=np.float32).reshape(h, w, 4)
    r = np.array(rough.pixels[:], dtype=np.float32).reshape(h, w, 4)
    out = np.empty((h, w, 4), dtype=np.float32)
    out[..., 0] = m[..., 0]
    out[..., 1] = 1.0
    out[..., 2] = 0.0
    out[..., 3] = 1.0 - r[..., 0]
    mk = bpy.data.images.new(lname + "_mask", w, h, alpha=True)
    mk.colorspace_settings.name = 'Non-Color'
    mk.pixels[:] = out.ravel().tolist()
    written.append(save(mk, os.path.join(fbm, lname + "_mask.png"), 'Non-Color', 'PNG'))
print("  wrote:", [os.path.basename(p) for p in written])

# --- export -----------------------------------------------------------------------
out_fbx = os.path.join(OUT_DIR, NAME + "_Body.fbx")
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True); arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", out_fbx, f"({os.path.getsize(out_fbx)//(1024*1024)} MB)")
print("PREP-OK")
