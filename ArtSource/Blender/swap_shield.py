"""Swap Buwaka's shield mesh for the Tripo tribal shield, keeping the mount untouched.

    blender -b --python swap_shield.py -- <new_shield.glb> <Buwaka_Body.fbx> <basecolor_out.jpg>

The new disc is fitted INTO the old shield object's local frame - thin axis to thin axis,
boss side to boss side, diameter matched, centre on centre - and then becomes the object's
mesh data. The object itself (name, bone parent, matrix, material slot) is untouched, so the
hand placement and `ShieldWeaponMount` keep working. The glb's basecolor is saved over the
texture the material GUID already points at.
"""
import bpy, sys, os
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
GLB, FBX, TEX_OUT = args[0], args[1], args[2]
TARGET_VERTS = 8000

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=FBX)
body_objs = list(bpy.data.objects)
old = bpy.data.objects["Buwaka_Shield_L"]

bpy.ops.import_scene.gltf(filepath=GLB)
new_objs = [o for o in bpy.data.objects if o not in body_objs and o.type == 'MESH']
assert new_objs, "glb brought no mesh"
# join if several pieces
bpy.ops.object.select_all(action='DESELECT')
for o in new_objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = new_objs[0]
if len(new_objs) > 1:
    bpy.ops.object.join()
new = bpy.context.view_layer.objects.active
# bake the import transform into the verts so .co is the real shape
new.data.transform(new.matrix_world)
new.matrix_world = Matrix.Identity(4)
print(f"new shield: {len(new.data.vertices)} verts")

if len(new.data.vertices) > TARGET_VERTS * 1.3:
    mod = new.modifiers.new("dec", 'DECIMATE')
    mod.ratio = TARGET_VERTS / len(new.data.vertices)
    bpy.context.view_layer.objects.active = new
    bpy.ops.object.modifier_apply(modifier="dec")
    print(f"decimated to {len(new.data.vertices)} verts")


def disc_frame(mesh):
    """(centre, [axis0, axis1, thin_axis], dims) from the local bounds; boss side = +thin."""
    vs = [v.co for v in mesh.vertices]
    lo = Vector((min(v[i] for v in vs) for i in range(3)))
    hi = Vector((max(v[i] for v in vs) for i in range(3)))
    dims = hi - lo
    centre = (lo + hi) / 2
    order = sorted(range(3), key=lambda i: -dims[i])  # two big in-plane, last is thin
    axes = []
    for i in order:
        a = Vector((0, 0, 0)); a[i] = 1.0
        axes.append(a)
    # which side of the mid-plane holds the boss: the side reaching further out
    thin = axes[2]
    plus = max((v - centre).dot(thin) for v in vs)
    minus = max(-(v - centre).dot(thin) for v in vs)
    if minus > plus:
        axes[2] = -axes[2]
        axes[1] = -axes[1]  # keep the frame right-handed
    return centre, axes, Vector((dims[order[0]], dims[order[1]], dims[order[2]]))


oc, oa, od = disc_frame(old.data)
nc, na, nd = disc_frame(new.data)
print(f"old disc: dia {max(od[0], od[1]):.3f} thick {od[2]:.3f}; new: dia {max(nd[0], nd[1]):.3f} thick {nd[2]:.3f}")

scale = max(od[0], od[1]) / max(nd[0], nd[1])
old_basis = Matrix((oa[0], oa[1], oa[2])).transposed()  # columns = old local axes
new_basis = Matrix((na[0], na[1], na[2])).transposed()
# new local coords -> disc coords -> scaled -> old local coords
xform = Matrix.Translation(oc) @ (old_basis.to_4x4() @ Matrix.Scale(scale, 4) @ new_basis.to_4x4().inverted() @ Matrix.Translation(-nc))
new.data.transform(xform)

# carry the OLD object's material onto the new mesh so Unity's material remap holds
new.data.materials.clear()
for s in old.material_slots:
    new.data.materials.append(s.material)

old_mesh = old.data
new.data.name = "Buwaka_Shield_L"
old.data = new.data
bpy.data.objects.remove(new)
bpy.data.meshes.remove(old_mesh)

# save the glb's basecolor over the texture the material GUID points at
img = None
for m in bpy.data.materials:
    if not m.node_tree:
        continue
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and 'Buwaka' not in (n.image.name or ''):
            for l in m.node_tree.links:
                if l.from_node is n and 'Base Color' in l.to_socket.name:
                    img = n.image
if img is None:  # fallback: biggest packed image that came from the glb
    cand = [i for i in bpy.data.images if i.packed_file]
    img = max(cand, key=lambda i: i.packed_file.size) if cand else None
assert img, "no basecolor image found in the glb"
scene = bpy.context.scene
scene.render.image_settings.file_format = 'JPEG'
scene.render.image_settings.quality = 92
scene.render.image_settings.color_mode = 'RGB'
img.save_render(TEX_OUT, scene=scene)
print("texture ->", TEX_OUT, os.path.getsize(TEX_OUT) // 1024, "KB")

rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH'):
        o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
    mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
    armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
)
print("exported:", FBX, f"({os.path.getsize(FBX) // 1024} KB)")
print("SWAP-OK")
