"""Prepare a Tripo environment FBX (arena) for Unity: slim it, copy its maps, export.

Usage: blender -b --python prep_env.py -- <tripo.fbx> <out_dir> <Name> <target_verts>

Writes <out_dir>/<Name>.fbx (mesh only, origin kept) and <out_dir>/<Name>.fbm/<name>_basecolor.*,
<name>_normal.png, <name>_mask.png (R metallic, G AO=1, A smoothness) from the Tripo .fbm maps.
Prints the mesh bounds so the placement script can scale it to the fight space.
"""
import bpy, sys, os, glob, shutil
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_DIR, NAME, TARGET = args[0], args[1], args[2], int(args[3])
os.makedirs(OUT_DIR, exist_ok=True)
fbm = os.path.join(OUT_DIR, NAME + ".fbm"); os.makedirs(fbm, exist_ok=True)
lname = NAME.lower()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC)
mesh = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
for o in list(bpy.data.objects):
    if o is not mesh: bpy.data.objects.remove(o)
mesh.parent = None
bpy.context.view_layer.objects.active = mesh; mesh.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
before = len(mesh.data.vertices)
ratio = min(1.0, TARGET / max(before, 1))
dec = mesh.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = ratio; dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier="Decimate")
mesh.name = NAME; mesh.data.name = NAME
mat = mesh.data.materials[0] if mesh.data.materials else None
if mat: mat.name = NAME
co = np.array([v.co[:] for v in mesh.data.vertices])
print(f"{NAME}: {before} -> {len(mesh.data.vertices)} verts; bounds min {tuple(np.round(co.min(0),3))} max {tuple(np.round(co.max(0),3))} size {tuple(np.round(co.max(0)-co.min(0),3))}")

# textures from the .fbm next to the source
imgs = {}
for d in glob.glob(os.path.join(os.path.dirname(SRC), "*.fbm")):
    for fp in os.listdir(d):
        lo = fp.lower()
        key = "color" if "basecolor" in lo else "normal" if "normal" in lo else "metal" if "metallic" in lo else "rough" if "roughness" in lo else None
        if key: imgs[key] = os.path.join(d, fp)
if "color" in imgs:
    shutil.copy2(imgs["color"], os.path.join(fbm, lname + "_basecolor" + os.path.splitext(imgs["color"])[1].lower().replace(".jpeg", ".jpg")))
if "normal" in imgs:
    shutil.copy2(imgs["normal"], os.path.join(fbm, lname + "_normal" + os.path.splitext(imgs["normal"])[1].lower()))
if "metal" in imgs and "rough" in imgs:
    met = bpy.data.images.load(imgs["metal"]); rough = bpy.data.images.load(imgs["rough"])
    met.colorspace_settings.name = 'Non-Color'; rough.colorspace_settings.name = 'Non-Color'
    w, h = met.size
    if rough.size[0] != w or rough.size[1] != h: rough.scale(w, h)
    m = np.array(met.pixels[:], dtype=np.float32).reshape(h, w, 4); r = np.array(rough.pixels[:], dtype=np.float32).reshape(h, w, 4)
    out = np.empty_like(m); out[..., 0] = m[..., 0]; out[..., 1] = 1.0; out[..., 2] = 0.0; out[..., 3] = 1.0 - r[..., 0]
    mk = bpy.data.images.new(lname + "_mask", w, h, alpha=True); mk.colorspace_settings.name = 'Non-Color'
    mk.pixels[:] = out.ravel().tolist(); mk.filepath_raw = os.path.join(fbm, lname + "_mask.png"); mk.file_format = 'PNG'; mk.save()
print("textures:", sorted(os.listdir(fbm)))

out_fbx = os.path.join(OUT_DIR, NAME + ".fbx")
bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'MESH'}, mesh_smooth_type='FACE',
    bake_anim=False, path_mode='STRIP', embed_textures=False, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
print("exported:", out_fbx, f"({os.path.getsize(out_fbx)//(1024*1024)} MB)")
print("ENV-OK")
