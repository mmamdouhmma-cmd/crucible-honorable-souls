"""Al-Quds arena: a walled courtyard in the Old City, built procedurally and exported for Unity.

Usage: blender -b --python build_arena_alquds.py -- <export_dir> [render_dir]

Writes <export_dir>/AlQuds_Arena.fbx plus <export_dir>/Textures/*.png (baked stone textures).
Units: Unity world units. The fighters are ~2.9 units tall, so 1 unit ~ 0.6 m.
The fight happens around the origin; the camera looks along -Z from +Z (it re-frames at runtime,
so the arena is symmetric enough to read from any side). The far wall (the one the trailer
shows behind the fighters) is on -Z.

Pieces (one object per material so Unity can swap materials by name):
  Floor        flagstones, worn, slightly uneven tiling
  Walls        Jerusalem limestone ashlar: courtyard walls, crenellations, towers, arcade
  Dome         gilded dome + drum on the skyline behind the far wall
  Minaret      pale stone minaret with a balcony
  Backdrop     low distant buildings (simple blocks) that break the horizon
"""
import bpy, bmesh, sys, os, math, random
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
EXPORT_DIR = args[0]
RENDER_DIR = args[1] if len(args) > 1 else None
TEX_DIR = os.path.join(EXPORT_DIR, "Textures")
os.makedirs(TEX_DIR, exist_ok=True)
if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
random.seed(7)

H = 2.9                      # fighter height in units
FLOOR_W, FLOOR_D = 110.0, 90.0
WALL_H = 5.0 * H             # ~14.5
WALL_T = 2.2
ARENA_W, ARENA_D = 64.0, 44.0   # inner courtyard footprint (walls sit on its edge)
TEX_TILE = 6.0               # world units per texture repeat for stone

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.unit_settings.scale_length = 1.0

# ---------------------------------------------------------------- materials (procedural, baked later)
def stone_material(name, base_a, base_b, mortar, scale, rows=1.0, bump=0.35, rough=0.85):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tex = nt.nodes.new("ShaderNodeTexCoord")
    brick = nt.nodes.new("ShaderNodeTexBrick")
    brick.inputs["Scale"].default_value = scale
    brick.inputs["Mortar Size"].default_value = 0.012
    brick.inputs["Mortar Smooth"].default_value = 0.35
    brick.inputs["Bias"].default_value = 0.0
    brick.inputs["Brick Width"].default_value = 1.0
    brick.inputs["Row Height"].default_value = rows
    brick.inputs["Color1"].default_value = (*base_a, 1)
    brick.inputs["Color2"].default_value = (*base_b, 1)
    brick.inputs["Mortar"].default_value = (*mortar, 1)
    nt.links.new(tex.outputs["UV"], brick.inputs["Vector"])
    # Grime / variation
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 9.0
    noise.inputs["Detail"].default_value = 6.0
    noise.inputs["Roughness"].default_value = 0.6
    nt.links.new(tex.outputs["UV"], noise.inputs["Vector"])
    mixc = nt.nodes.new("ShaderNodeMix")
    mixc.data_type = 'RGBA'
    mixc.blend_type = 'MULTIPLY'
    mixc.inputs["Factor"].default_value = 0.55
    nt.links.new(brick.outputs["Color"], mixc.inputs[6])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[0].color = (0.55, 0.5, 0.42, 1)
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], mixc.inputs[7])
    nt.links.new(mixc.outputs[2], bsdf.inputs["Base Color"])
    # Bump from mortar + fine noise
    fine = nt.nodes.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 60.0
    fine.inputs["Detail"].default_value = 8.0
    nt.links.new(tex.outputs["UV"], fine.inputs["Vector"])
    mixh = nt.nodes.new("ShaderNodeMath")
    mixh.operation = 'MULTIPLY_ADD'
    mixh.inputs[1].default_value = 0.25
    nt.links.new(fine.outputs["Fac"], mixh.inputs[0])
    nt.links.new(brick.outputs["Fac"], mixh.inputs[2])   # mortar lines = 1
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(mixh.outputs[0], inv.inputs[1])
    bumpn = nt.nodes.new("ShaderNodeBump")
    bumpn.inputs["Strength"].default_value = bump
    bumpn.inputs["Distance"].default_value = 0.08
    nt.links.new(inv.outputs[0], bumpn.inputs["Height"])
    nt.links.new(bumpn.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = rough
    return mat

MAT_WALL = stone_material("JerusalemStone", (0.86, 0.78, 0.62), (0.74, 0.64, 0.48), (0.58, 0.52, 0.42), scale=4.0, rows=0.5, bump=0.4)
MAT_FLOOR = stone_material("Flagstone", (0.72, 0.64, 0.50), (0.60, 0.53, 0.41), (0.46, 0.40, 0.31), scale=2.6, rows=0.9, bump=0.5, rough=0.92)
MAT_DOME = bpy.data.materials.new("GildedDome")
MAT_DOME.use_nodes = True
b = MAT_DOME.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (1.0, 0.72, 0.28, 1)
b.inputs["Metallic"].default_value = 1.0
b.inputs["Roughness"].default_value = 0.35
MAT_MINARET = stone_material("MinaretStone", (0.90, 0.84, 0.70), (0.80, 0.72, 0.56), (0.64, 0.58, 0.46), scale=5.0, rows=0.6, bump=0.3)
MAT_BACKDROP = stone_material("Backdrop", (0.80, 0.72, 0.58), (0.70, 0.62, 0.48), (0.60, 0.54, 0.44), scale=3.0, rows=0.6, bump=0.2)

# ---------------------------------------------------------------- mesh helpers
class Builder:
    """Accumulates boxes/arches/etc. into one bmesh per object, with box-projected UVs."""
    def __init__(self, name, mat, tile=TEX_TILE):
        self.name, self.mat, self.tile = name, mat, tile
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def box(self, center, size, rot_z=0.0):
        cx, cy, cz = center
        sx, sy, sz = size
        verts = []
        for dx in (-0.5, 0.5):
            for dy in (-0.5, 0.5):
                for dz in (-0.5, 0.5):
                    p = Vector((dx * sx, dy * sy, dz * sz))
                    if rot_z:
                        c, s = math.cos(rot_z), math.sin(rot_z)
                        p = Vector((p.x * c - p.y * s, p.x * s + p.y * c, p.z))
                    verts.append(self.bm.verts.new((cx + p.x, cy + p.y, cz + p.z)))
        # faces by index: verts ordered x,y,z nested
        def v(ix, iy, iz): return verts[ix * 4 + iy * 2 + iz]
        faces = [
            (v(0,0,0), v(0,1,0), v(0,1,1), v(0,0,1)),   # -x
            (v(1,0,0), v(1,0,1), v(1,1,1), v(1,1,0)),   # +x
            (v(0,0,0), v(0,0,1), v(1,0,1), v(1,0,0)),   # -y
            (v(0,1,0), v(1,1,0), v(1,1,1), v(0,1,1)),   # +y
            (v(0,0,0), v(1,0,0), v(1,1,0), v(0,1,0)),   # -z
            (v(0,0,1), v(0,1,1), v(1,1,1), v(1,0,1)),   # +z
        ]
        for f in faces:
            try:
                self.bm.faces.new(f)
            except ValueError:
                pass

    def cylinder(self, center, radius, height, segments=24, top=True, bottom=True, radius_top=None):
        cx, cy, cz = center
        rt = radius if radius_top is None else radius_top
        ring0, ring1 = [], []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring0.append(self.bm.verts.new((cx + radius * math.cos(a), cy + radius * math.sin(a), cz)))
            ring1.append(self.bm.verts.new((cx + rt * math.cos(a), cy + rt * math.sin(a), cz + height)))
        for i in range(segments):
            j = (i + 1) % segments
            self.bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i]))
        if top and rt > 0.001:
            self.bm.faces.new(tuple(reversed(ring1)))
        if bottom:
            self.bm.faces.new(tuple(ring0))

    def dome(self, center, radius, segments=32, rings=12, squash=1.0):
        cx, cy, cz = center
        grid = []
        for r in range(rings + 1):
            phi = (math.pi / 2) * r / rings            # 0 = equator, pi/2 = pole
            z = cz + radius * squash * math.sin(phi)
            rr = radius * math.cos(phi)
            row = []
            for i in range(segments):
                a = 2 * math.pi * i / segments
                row.append(self.bm.verts.new((cx + rr * math.cos(a), cy + rr * math.sin(a), z)))
            grid.append(row)
        for r in range(rings):
            for i in range(segments):
                j = (i + 1) % segments
                a, b2, c, d = grid[r][i], grid[r][j], grid[r + 1][j], grid[r + 1][i]
                if r == rings - 1:
                    self.bm.faces.new((a, b2, c))
                else:
                    self.bm.faces.new((a, b2, c, d))

    def pointed_arch_wall(self, x0, x1, y, z0, z1, thickness, bays, pillar_w=1.4, segs=10):
        """A wall segment along X at depth y, from z0 (floor) to z1 (top), with `bays` pointed-arch
        openings. Built as pillars + spandrel strips so the openings are real holes."""
        span = (x1 - x0) / bays
        for bi in range(bays):
            bx0 = x0 + bi * span
            bx1 = bx0 + span
            # pillars (shared between bays is fine: duplicates are harmless)
            self.box(((bx0 + pillar_w / 2), y, (z0 + z1) / 2), (pillar_w, thickness, z1 - z0))
            if bi == bays - 1:
                self.box(((bx1 - pillar_w / 2), y, (z0 + z1) / 2), (pillar_w, thickness, z1 - z0))
            # opening: from inner pillar faces, arch springs at 55% height, apex at 85%
            ix0, ix1 = bx0 + pillar_w, bx1 - pillar_w
            w = ix1 - ix0
            spring = z0 + (z1 - z0) * 0.55
            apex = z0 + (z1 - z0) * 0.86
            # intrados: two circular arcs meeting at the apex (pointed arch, centres offset by w/4)
            pts = []
            for s in range(segs + 1):
                t = s / segs
                if t <= 0.5:
                    a = math.pi - (t * 2) * (math.pi / 3)       # left arc from spring up to apex
                    cxl = ix0 + w * 0.75
                    rad = w * 0.75
                    px, pz = cxl + rad * math.cos(a), spring + rad * math.sin(a) * ((apex - spring) / (rad * math.sin(math.pi / 3)))
                else:
                    a = (math.pi / 3) * (1 - (t - 0.5) * 2)  # right arc from apex down to spring (mirrored)
                    cxr = ix1 - w * 0.75
                    rad = w * 0.75
                    px, pz = cxr + rad * math.cos(a), spring + rad * math.sin(a) * ((apex - spring) / (rad * math.sin(math.pi / 3)))
                pts.append((px, pz))
            # spandrel: quads between intrados points and the wall top edge, front and back, plus the intrados band
            for side in (-1, 1):
                yy = y + side * thickness / 2
                front = [self.bm.verts.new((px, yy, pz)) for px, pz in pts]
                top = [self.bm.verts.new((px, yy, z1)) for px, _ in pts]
                for i in range(segs):
                    quad = (front[i], front[i + 1], top[i + 1], top[i])
                    self.bm.faces.new(quad if side > 0 else tuple(reversed(quad)))
                # wall above the spring line next to the pillars is covered by the strips above
            # intrados band (inside surface of the arch) and top cap
            fa = [self.bm.verts.new((px, y - thickness / 2, pz)) for px, pz in pts]
            fb = [self.bm.verts.new((px, y + thickness / 2, pz)) for px, pz in pts]
            for i in range(segs):
                self.bm.faces.new((fa[i], fb[i], fb[i + 1], fa[i + 1]))
            ta = [self.bm.verts.new((px, y - thickness / 2, z1)) for px, _ in pts]
            tb = [self.bm.verts.new((px, y + thickness / 2, z1)) for px, _ in pts]
            for i in range(segs):
                self.bm.faces.new((ta[i], ta[i + 1], tb[i + 1], tb[i]))

    def crenellations(self, x0, x1, y, z, thickness, merlon=1.6, gap=1.2, height=1.5, along_x=True):
        pos = x0
        while pos + merlon <= x1 + 0.01:
            if along_x:
                self.box(((pos + merlon / 2), y, z + height / 2), (merlon, thickness, height))
            else:
                self.box((y, (pos + merlon / 2), z + height / 2), (thickness, merlon, height))
            pos += merlon + gap

    def finish(self):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.0005)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        # Box-projected UVs: pick the dominant normal axis per face.
        for f in self.bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                p = loop.vert.co
                if ax == 0:   u, v = p.y, p.z
                elif ax == 1: u, v = p.x, p.z
                else:         u, v = p.x, p.y
                loop[self.uv].uv = (u / self.tile, v / self.tile)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(self.name, me)
        sc.collection.objects.link(ob)
        me.materials.append(self.mat)
        for p in me.polygons:
            p.use_smooth = False
        return ob

# ---------------------------------------------------------------- build
# Floor
floor = Builder("Floor", MAT_FLOOR, tile=5.0)
floor.box((0, 0, -0.5), (FLOOR_W, FLOOR_D, 1.0))
floor.finish()

walls = Builder("Walls", MAT_WALL)
hx, hy = ARENA_W / 2, ARENA_D / 2
# Far wall (-Y in Blender = -Z in Unity after export): an arcade of pointed arches with a parapet.
walls.pointed_arch_wall(-hx, hx, -hy, 0.0, WALL_H * 0.78, WALL_T, bays=7)
walls.box((0, -hy, WALL_H * 0.78 + (WALL_H * 0.22) / 2), (ARENA_W + WALL_T, WALL_T, WALL_H * 0.22))   # parapet band
walls.crenellations(-hx, hx, -hy, WALL_H, WALL_T)
# Near wall (behind the camera most of the time): plain with crenellations, lower.
walls.box((0, hy, WALL_H * 0.6 / 2), (ARENA_W + WALL_T, WALL_T, WALL_H * 0.6))
walls.crenellations(-hx, hx, hy, WALL_H * 0.6, WALL_T)
# Side walls with a shallow arcade of blind arches (niches) and crenellations.
for sx in (-1, 1):
    x = sx * hx
    walls.box((x, 0, WALL_H * 0.7 / 2), (WALL_T, ARENA_D, WALL_H * 0.7))
    walls.crenellations(-hy, hy, x, WALL_H * 0.7, WALL_T, along_x=False)
# Corner towers: octagonal-ish cylinders with crenellated tops.
for sx in (-1, 1):
    for sy in (-1, 1):
        cx, cy = sx * (hx + 1.0), sy * (hy + 1.0)
        th = WALL_H * (1.25 if sy < 0 else 1.0)
        walls.cylinder((cx, cy, 0), 4.2, th, segments=8)
        walls.cylinder((cx, cy, th), 4.6, 0.8, segments=8)      # cornice
        for i in range(8):
            a = 2 * math.pi * (i + 0.5) / 8
            walls.box((cx + 4.0 * math.cos(a), cy + 4.0 * math.sin(a), th + 0.8 + 0.7), (1.6, 1.0, 1.4), rot_z=a)
# Gate in the far wall centre bay is implied by the arcade; add a flight of steps up to it.
for i in range(3):
    walls.box((0, -hy + WALL_T / 2 + 1.2 + i * 1.2, 0.3 * (3 - i) - 0.15), (12.0 - i * 1.5, 1.2, 0.3 * (3 - i)))
walls.finish()

# Skyline behind the far wall: the dome on its drum, a minaret, and low buildings.
dome = Builder("Dome", MAT_DOME, tile=8.0)
dome.cylinder((8.0, -hy - 38.0, 0), 13.0, 16.0, segments=24)              # drum
dome.dome((8.0, -hy - 38.0, 16.0), 13.0, segments=40, rings=14, squash=0.9)
dome.cylinder((8.0, -hy - 38.0, 16.0 + 13.0 * 0.9 - 0.2), 0.9, 4.0, segments=12, radius_top=0.0)  # finial
dome.finish()

minaret = Builder("Minaret", MAT_MINARET, tile=5.0)
mx, my = -34.0, -hy - 30.0
minaret.cylinder((mx, my, 0), 3.2, 30.0, segments=12)
minaret.cylinder((mx, my, 30.0), 4.4, 1.2, segments=12)                     # balcony
minaret.cylinder((mx, my, 31.2), 2.4, 12.0, segments=12)
minaret.cylinder((mx, my, 43.2), 3.0, 0.8, segments=12)
minaret.dome((mx, my, 44.0), 2.6, segments=16, rings=8, squash=1.2)
minaret.finish()

back = Builder("Backdrop", MAT_BACKDROP, tile=7.0)
for i in range(22):
    bx = -90 + i * 8.5 + random.uniform(-2, 2)
    by = -hy - random.uniform(16, 60)
    bw, bd, bh = random.uniform(7, 14), random.uniform(7, 14), random.uniform(8, 20)
    back.box((bx, by, bh / 2), (bw, bd, bh))
    if random.random() < 0.5:
        back.dome((bx, by, bh), min(bw, bd) * 0.42, segments=16, rings=6, squash=0.8)
# low buildings down the sides too, so the camera never sees empty horizon
for sx in (-1, 1):
    for i in range(8):
        by = -hy + 10 + i * 11 + random.uniform(-2, 2)
        bx = sx * (hx + random.uniform(14, 32))
        bw, bd, bh = random.uniform(7, 12), random.uniform(7, 12), random.uniform(6, 14)
        back.box((bx, by, bh / 2), (bw, bd, bh))
back.finish()

# ---------------------------------------------------------------- bake stone textures (Cycles, flat plane)
def bake_material(mat, basename, size=2048):
    """Bakes colour, normal and roughness of a procedural material to PNGs using a UV plane."""
    bpy.ops.mesh.primitive_plane_add(size=1)
    plane = bpy.context.active_object
    plane.name = "BakePlane"
    plane.data.materials.append(mat)
    nt = mat.node_tree
    for ob in sc.objects:
        ob.select_set(ob is plane)
    bpy.context.view_layer.objects.active = plane
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 16
    sc.cycles.device = 'CPU'
    sc.render.bake.margin = 8
    sc.render.bake.use_selected_to_active = False
    sc.render.bake.use_pass_direct = False
    sc.render.bake.use_pass_indirect = False
    sc.render.bake.use_pass_color = True
    out = {}
    for pass_name, suffix, colorspace in (("DIFFUSE", "_BaseColor", "sRGB"), ("NORMAL", "_Normal", "Non-Color"), ("ROUGHNESS", "_Mask", "Non-Color")):
        img = bpy.data.images.new(basename + suffix, size, size, alpha=False)
        img.colorspace_settings.name = colorspace
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        nt.nodes.active = node
        bpy.ops.object.bake(type=pass_name, use_clear=True)
        if suffix == "_Mask":
            # HDRP mask: R metallic (0), G AO (1), B detail (0), A smoothness (1 - roughness)
            import numpy as np
            px = np.array(img.pixels[:], dtype=np.float32).reshape(size, size, 4)
            m = np.empty_like(px)
            m[..., 0] = 0.0
            m[..., 1] = 1.0
            m[..., 2] = 0.0
            m[..., 3] = 1.0 - px[..., 0]
            mask = bpy.data.images.new(basename + "_Mask", size, size, alpha=True)
            mask.colorspace_settings.name = 'Non-Color'
            mask.pixels[:] = m.ravel().tolist()
            mask.filepath_raw = os.path.join(TEX_DIR, basename + "_Mask.png")
            mask.file_format = 'PNG'
            mask.save()
            out[suffix] = mask.filepath_raw
        else:
            img.filepath_raw = os.path.join(TEX_DIR, basename + suffix + ".png")
            img.file_format = 'PNG'
            img.save()
            out[suffix] = img.filepath_raw
        nt.nodes.remove(node)
    bpy.data.objects.remove(plane)
    print("baked", basename, [os.path.basename(v) for v in out.values()])

for mat, base in ((MAT_WALL, "JerusalemStone"), (MAT_FLOOR, "Flagstone"), (MAT_MINARET, "MinaretStone"), (MAT_BACKDROP, "Backdrop")):
    bake_material(mat, base)

# ---------------------------------------------------------------- check render
if RENDER_DIR:
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 960, 540
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    for i, (pos, look) in enumerate(((Vector((0, 40, 9)), Vector((0, -10, 8))), (Vector((60, 70, 40)), Vector((0, -10, 8))))):
        cam.location = pos
        cam.rotation_euler = (look - pos).to_track_quat('-Z', 'Y').to_euler()
        cam_data.lens = 28
        sc.camera = cam
        sc.render.filepath = os.path.join(RENDER_DIR, f"arena_{i}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)

# ---------------------------------------------------------------- export
for ob in sc.objects:
    ob.select_set(ob.type == 'MESH')
path = os.path.join(EXPORT_DIR, "AlQuds_Arena.fbx")
bpy.ops.export_scene.fbx(
    filepath=path, use_selection=True, object_types={'MESH'}, mesh_smooth_type='FACE',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
    path_mode='STRIP', embed_textures=False, bake_anim=False,
)
print("exported", path, f"({os.path.getsize(path)//1024} KB)")
print("ARENA-OK")
