"""Prepare a Tripo environment PIECE (one prop of a multi-object arena) for Unity, with LODs.

Usage: blender -b --python prep_env_lod.py -- <tripo.fbx> <out_dir> <Name> <json options>

The sibling of prep_env.py, which slims a whole one-mesh arena. This one is for arenas assembled
from several Tripo models (Musashi Chapter One: terrain + dojo + house + pine), and writes
<out_dir>/<Name>.fbx holding:
  <Name>_LOD0, <Name>_LOD1, ...   one decimation per entry of "lods" (ratios of the ORIGINAL
                                  triangle count). Unity builds a LODGroup from the _LODn names.
  <Name>_COL                      optional ("collider" ratio): a coarse copy for a MeshCollider.
  <Name>_SHD                      optional ("shadow" ratio): a lighter copy that casts the shadows
                                  (Unity renders it ShadowsOnly), so a 1M-triangle hero mesh is not
                                  drawn again into every shadow cascade.
and <out_dir>/<Name>.fbm/<name>_basecolor.jpg, _normal.png, _mask.png (R metallic, G AO=1,
A smoothness = 1 - roughness), the same map convention as prep_env.py / BuildMaterial.

Options (JSON):
  scale      world units per Tripo unit (baked into the mesh, so the Unity transform stays 1)
  pivot      [x, y, z] in Tripo/Blender world space that becomes the origin
  level      [nx, ny, nz] a floor normal to rotate onto +Z about the pivot (the dojo's courtyard
             was authored 4.4 deg off level)
  flatten    [cx, cy, radius, falloff, z] clamp every vertex inside radius of (cx, cy) down to z,
             blending out over falloff (Blender space, before scale) - room for a building
  plateau    list of [cx, cy, radius, falloff, z]: like flatten, but moves vertices BOTH ways onto z,
             so a gently rolling patch becomes a level fight space (the boss plaza)
  lods       list of decimation ratios, LOD0 first
  collider   decimation ratio for the _COL mesh (omit for none)
  shadow     decimation ratio for the _SHD mesh (omit for none)

The source FBX and its maps are never modified.
"""
import bpy, sys, os, glob, shutil, json, math
import numpy as np
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_DIR, NAME, OPT = args[0], args[1], args[2], json.loads(args[3])
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
me = mesh.data
tris0 = sum(len(p.vertices) - 2 for p in me.polygons)

co = np.empty(len(me.vertices) * 3, dtype=np.float64); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)

if "flatten" in OPT:
    cx, cy, r, fall, z = OPT["flatten"]
    d = np.hypot(co[:, 0] - cx, co[:, 1] - cy)
    w = np.clip((r + fall - d) / max(fall, 1e-6), 0.0, 1.0)          # 1 inside r, 0 beyond r + falloff
    over = co[:, 2] > z
    co[over, 2] = co[over, 2] + (z - co[over, 2]) * w[over]
    print(f"{NAME}: flattened {int((over & (w > 0)).sum())} verts to z <= {z}")

for cx, cy, r, fall, z in OPT.get("plateau", []):
    d = np.hypot(co[:, 0] - cx, co[:, 1] - cy)
    w = np.clip((r + fall - d) / max(fall, 1e-6), 0.0, 1.0)
    w = w * w * (3.0 - 2.0 * w)                                       # smoothstep: no crease at the rim
    co[:, 2] = co[:, 2] + (z - co[:, 2]) * w
    print(f"{NAME}: plateau at ({cx}, {cy}) moved {int((w > 0).sum())} verts")

pivot = np.array(OPT.get("pivot", [0, 0, 0]), dtype=np.float64)
co -= pivot
if "level" in OPT:
    n = Vector(OPT["level"]).normalized()
    R = np.array(n.rotation_difference(Vector((0, 0, 1))).to_matrix())
    co = co @ R.T
    print(f"{NAME}: levelled by {math.degrees(n.angle(Vector((0, 0, 1)))):.2f} deg")
co *= float(OPT.get("scale", 1.0))
me.vertices.foreach_set("co", co.ravel()); me.update()
print(f"{NAME}: {len(me.vertices)} verts / {tris0} tris; bounds min {tuple(np.round(co.min(0), 3))} max {tuple(np.round(co.max(0), 3))}")

mat = me.materials[0] if me.materials else None
if mat: mat.name = NAME

# LOD chain. Each level is decimated from the previous one (cheaper than from the original every
# time) to the ratio of the ORIGINAL count it asks for.
def decimated(src_obj, ratio_of_src, name):
    ob = src_obj.copy(); ob.data = src_obj.data.copy(); ob.name = name; ob.data.name = name
    bpy.context.scene.collection.objects.link(ob)
    if ratio_of_src < 0.999:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        dec = ob.modifiers.new("Decimate", 'DECIMATE'); dec.ratio = ratio_of_src; dec.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier="Decimate")
    return ob

out_objs, prev, prev_ratio = [], mesh, 1.0
for i, ratio in enumerate(OPT.get("lods", [1.0])):
    ob = decimated(prev, ratio / prev_ratio, f"{NAME}_LOD{i}")
    out_objs.append(ob); prev, prev_ratio = ob, ratio
    print(f"  LOD{i}: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris (ratio {ratio})")
if "shadow" in OPT:
    ob = decimated(prev, OPT["shadow"] / prev_ratio, f"{NAME}_SHD")
    out_objs.append(ob)
    print(f"  SHD: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
if "collider" in OPT:
    ob = decimated(prev, OPT["collider"] / prev_ratio, f"{NAME}_COL") if OPT["collider"] < prev_ratio else decimated(prev, 1.0, f"{NAME}_COL")
    out_objs.append(ob)
    print(f"  COL: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
bpy.data.objects.remove(mesh)

# textures from the .fbm next to the source (same convention as prep_env.py)
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
    m = np.empty(w * h * 4, dtype=np.float32); met.pixels.foreach_get(m); m = m.reshape(h, w, 4)
    r = np.empty(w * h * 4, dtype=np.float32); rough.pixels.foreach_get(r); r = r.reshape(h, w, 4)
    out = np.empty_like(m); out[..., 0] = m[..., 0]; out[..., 1] = 1.0; out[..., 2] = 0.0; out[..., 3] = 1.0 - r[..., 0]
    mk = bpy.data.images.new(lname + "_mask", w, h, alpha=True); mk.colorspace_settings.name = 'Non-Color'
    mk.pixels.foreach_set(out.ravel()); mk.filepath_raw = os.path.join(fbm, lname + "_mask.png"); mk.file_format = 'PNG'; mk.save()
print("textures:", sorted(os.listdir(fbm)))

out_fbx = os.path.join(OUT_DIR, NAME + ".fbx")
bpy.ops.object.select_all(action='DESELECT')
for ob in out_objs: ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'MESH'}, mesh_smooth_type='FACE',
    bake_anim=False, path_mode='STRIP', embed_textures=False, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y')
print("exported:", out_fbx, f"({os.path.getsize(out_fbx) // (1024 * 1024)} MB)")
print("ENV-OK")
