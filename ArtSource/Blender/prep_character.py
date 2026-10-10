"""Prepare a Tripo rigged character for HDRP: slim the mesh and pack an HDRP mask map.

Usage: blender -b --python prep_character.py -- <rigged.fbx> <backup.fbx> <target_verts>

- Backs up the original FBX to <backup.fbx> (if the backup doesn't exist yet; always slims from the backup).
- Imports it, decimates the mesh to about <target_verts> (vertex groups / skin weights preserved),
  exports mesh + armature back over <rigged.fbx> with the same material name so Unity keeps its links.
- Finds the metallic and roughness textures beside the FBX (the .fbm folder) and writes
  <base>_mask.png there: R = metallic, G = ambient occlusion (1), B = 0, A = smoothness (1 - roughness).
"""
import bpy, sys, os, shutil
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
SRC, BACKUP, TARGET = args[0], args[1], int(args[2])

if not os.path.exists(BACKUP):
    os.makedirs(os.path.dirname(BACKUP), exist_ok=True)
    shutil.copy2(SRC, BACKUP)
    print("backed up original to", BACKUP)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=BACKUP)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
before = len(mesh.data.vertices)
ratio = min(1.0, TARGET / max(before, 1))

bpy.context.view_layer.objects.active = mesh
mesh.select_set(True)
dec = mesh.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = ratio
dec.use_collapse_triangulate = True
bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)
bpy.ops.object.modifier_apply(modifier="Decimate")
print(f"mesh: {before} -> {len(mesh.data.vertices)} verts (ratio {ratio:.3f}); materials: {[m.name for m in mesh.data.materials if m]}")

# Export mesh + armature over the source path.
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=SRC, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported slim FBX:", SRC, f"({os.path.getsize(SRC)//(1024*1024)} MB)")

# Mask map from the textures next to the FBX.
fbm = None
for d in os.listdir(os.path.dirname(SRC)):
    if d.endswith(".fbm") and os.path.isdir(os.path.join(os.path.dirname(SRC), d)):
        fbm = os.path.join(os.path.dirname(SRC), d)
        break
if fbm:
    files = os.listdir(fbm)
    def find(tag):
        for f in files:
            if tag in f.lower() and f.lower().endswith((".png", ".jpg", ".jpeg")) and "_mask" not in f.lower():
                return os.path.join(fbm, f)
        return None
    met_p, rough_p = find("metallic"), find("roughness")
    if met_p and rough_p:
        met = bpy.data.images.load(met_p); met.colorspace_settings.name = 'Non-Color'
        rough = bpy.data.images.load(rough_p); rough.colorspace_settings.name = 'Non-Color'
        w, h = met.size
        if rough.size[0] != w or rough.size[1] != h:
            rough.scale(w, h)
        m = np.array(met.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r = np.array(rough.pixels[:], dtype=np.float32).reshape(h, w, 4)
        out = np.empty((h, w, 4), dtype=np.float32)
        out[..., 0] = m[..., 0]              # metallic
        out[..., 1] = 1.0                    # ambient occlusion (none baked yet)
        out[..., 2] = 0.0                    # detail mask
        out[..., 3] = 1.0 - r[..., 0]        # smoothness
        base = os.path.basename(met_p).lower().split("_metallic")[0]
        img = bpy.data.images.new(base + "_mask", w, h, alpha=True, float_buffer=False)
        img.colorspace_settings.name = 'Non-Color'
        img.pixels[:] = out.ravel().tolist()
        img.filepath_raw = os.path.join(fbm, base + "_mask.png")
        img.file_format = 'PNG'
        img.save()
        print("wrote mask map:", img.filepath_raw, f"{w}x{h}")
    else:
        print("mask map skipped: metallic/roughness textures not found in", fbm)
else:
    print("mask map skipped: no .fbm folder next to", SRC)
print("PREP-OK")
