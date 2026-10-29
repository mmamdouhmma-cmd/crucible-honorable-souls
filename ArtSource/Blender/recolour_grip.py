"""Recolour a katana's GRIP (tsuka) in its texture atlas (Kojiro's red-hafted katana, 2026-10-29).

    blender -b --python recolour_grip.py -- <katana.fbx> <basecolor> <mask.png> <out_basecolor.png> <render_dir>
            [--hue 0.0] [--sat 0.78] [--lift 0.30] [--brass-sat 0.28]

Tripo atlases are scattered charts, so the grip cannot be painted by eye. The grip is found on the
MESH instead: the tsuba is the slice of the long axis with the widest cross-section, the grip is the
shorter side of it, and only faces within 1.6x the grip's own radius of the axis count (so the tsuba
disc is left out). Those triangles are rasterized into UV space as a mask, grown a few texels into
the chart padding, and every masked texel that is not metal (mask R = metallic) is re-hued red while
keeping its own light and dark, so the wrap pattern survives. Writes a new basecolor; the source
texture is never touched.
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
FBX, BASE, MASK, OUT, RDIR = argv[:5]
opts = {"--hue": "0.0", "--sat": "0.78", "--lift": "0.30", "--brass-sat": "0.28"}
for i in range(5, len(argv), 2):
    opts[argv[i]] = argv[i + 1]
os.makedirs(RDIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=FBX)
ob = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
me = ob.data
V = np.array([v.co[:] for v in me.vertices])

# long axis = largest PCA component
c = V.mean(0)
w, vec = np.linalg.eigh(np.cov((V - c).T))
ax = vec[:, np.argmax(w)]
t = (V - c) @ ax
L = t.max() - t.min()
perp = (V - c) - np.outer(t, ax)
# cross-section width per slice: the tsuba is the widest slice
bins = np.linspace(t.min(), t.max(), 81)
width = np.zeros(80)
for i in range(80):
    m = (t >= bins[i]) & (t < bins[i + 1])
    if m.sum() > 3:
        p = perp[m]
        width[i] = np.linalg.norm(p - p.mean(0), axis=1).max()
ti = int(np.argmax(width))
tsuba_lo, tsuba_hi = bins[max(ti - 1, 0)], bins[min(ti + 2, 80)]
grip_low_side = (tsuba_lo - t.min()) < (t.max() - tsuba_hi)
gm = (t < tsuba_lo) if grip_low_side else (t > tsuba_hi)
axis_pt = perp[gm].mean(0)
rad = np.linalg.norm(perp[gm] - axis_pt, axis=1)
grip_r = np.median(rad)
print(f"[grip] length {L:.4f}, tsuba slice {ti}/80 width {width[ti]:.4f}, grip on the {'low' if grip_low_side else 'high'} side, "
      f"grip radius {grip_r:.4f}, grip verts {gm.sum()}")

uvl = me.uv_layers.active.data
img = bpy.data.images.load(BASE)
W, Hh = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(Hh, W, 4)
mimg = bpy.data.images.load(MASK)
mpx = np.array(mimg.pixels[:], dtype=np.float32).reshape(mimg.size[1], mimg.size[0], 4)
if mpx.shape[:2] != px.shape[:2]:
    ys = (np.arange(Hh) * mpx.shape[0] // Hh); xs = (np.arange(W) * mpx.shape[1] // W)
    mpx = mpx[ys][:, xs]
sel = np.zeros((Hh, W), dtype=bool)
nf = 0
for poly in me.polygons:
    ids = list(poly.vertices)
    ct = t[ids].mean(); cp = perp[ids].mean(0)
    inside = (ct < tsuba_lo) if grip_low_side else (ct > tsuba_hi)
    if not inside or np.linalg.norm(cp - axis_pt) > 1.6 * grip_r:
        continue
    nf += 1
    uv = np.array([uvl[li].uv[:] for li in poly.loop_indices]) * [W, Hh]
    for k in range(1, len(uv) - 1):
        tri = uv[[0, k, k + 1]]
        x0, y0 = np.floor(tri.min(0)).astype(int); x1, y1 = np.ceil(tri.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, Hh - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        a, b, cc = tri
        d = (b[1] - cc[1]) * (a[0] - cc[0]) + (cc[0] - b[0]) * (a[1] - cc[1])
        if abs(d) < 1e-9:
            continue
        l1 = ((b[1] - cc[1]) * (gx - cc[0]) + (cc[0] - b[0]) * (gy - cc[1])) / d
        l2 = ((cc[1] - a[1]) * (gx - cc[0]) + (a[0] - cc[0]) * (gy - cc[1])) / d
        l3 = 1 - l1 - l2
        inn = (l1 >= -0.01) & (l2 >= -0.01) & (l3 >= -0.01)
        sel[y0:y1 + 1, x0:x1 + 1] |= inn
# grow 3 texels into padding
for _ in range(3):
    g = sel.copy()
    g[1:] |= sel[:-1]; g[:-1] |= sel[1:]; g[:, 1:] |= sel[:, :-1]; g[:, :-1] |= sel[:, 1:]
    sel = g
# metal: by the mask where it says so, and by COLOUR for brass fittings (fuchi, kashira, menuki), which
# Miyamoto's mask does not flag - warm hue with some saturation, against a near-black / grey wrap
r_, g_, b_ = px[..., 0], px[..., 1], px[..., 2]
mx_, mn_ = px[..., :3].max(-1), px[..., :3].min(-1)
sat_ = np.where(mx_ > 1e-4, (mx_ - mn_) / np.maximum(mx_, 1e-4), 0)
warm = (r_ >= g_) & (g_ >= b_) & (sat_ > float(opts["--brass-sat"])) & (mx_ > 0.12)
for _ in range(2):                                   # take in the dark pits of the fittings too
    g = warm.copy()
    g[1:] |= warm[:-1]; g[:-1] |= warm[1:]; g[:, 1:] |= warm[:, :-1]; g[:, :-1] |= warm[:, 1:]
    warm = g
metal = (mpx[..., 0] > 0.5) | warm
tint = sel & ~metal
print(f"[grip] {nf} grip faces, {sel.sum()} texels, {tint.sum()} recoloured ({(sel & metal).sum()} metal kept)")

rgb = px[..., :3]
v = rgb.max(-1)
lift = float(opts["--lift"])
v2 = np.clip(lift * 0.5 + v * (1.0 + lift), 0, 1)       # the wrap is near-black; lift it so red reads
h, s = float(opts["--hue"]), float(opts["--sat"])
# HSV -> RGB at a fixed hue, per texel value v2 and saturation s
i6 = int(h * 6) % 6; f = h * 6 - math.floor(h * 6)
p_ = v2 * (1 - s); q_ = v2 * (1 - s * f); t_ = v2 * (1 - s * (1 - f))
table = [(v2, t_, p_), (q_, v2, p_), (p_, v2, t_), (p_, q_, v2), (t_, p_, v2), (v2, p_, q_)][i6]
new = np.stack(table, -1)
rgb[tint] = new[tint]
px[..., :3] = rgb
out = bpy.data.images.new("grip_out", W, Hh, alpha=False)
out.pixels[:] = px.ravel()
out.filepath_raw = OUT; out.file_format = 'PNG'; out.save()
m = bpy.data.images.new("grip_mask", W, Hh, alpha=False)
mm = np.zeros((Hh, W, 4), np.float32); mm[..., 3] = 1; mm[sel, 0] = 1; mm[tint, 1] = 1
m.pixels[:] = mm.ravel(); m.filepath_raw = os.path.join(RDIR, "grip_uv_mask.png"); m.file_format = 'PNG'; m.save()

# render with the new texture
mat = bpy.data.materials.new("KatanaRed"); mat.use_nodes = True
bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
tex = mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(OUT)
mat.node_tree.links.new(tex.outputs[0], bsdf.inputs[0])
me.materials.clear(); me.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
mw = ob.matrix_world
cw = mw @ Vector(c)
axw = (mw.to_3x3() @ Vector(ax)).normalized()
side = axw.cross(Vector((0, 0, 1)) if abs(axw.z) < 0.9 else Vector((1, 0, 0))).normalized()
Lw = L * mw.to_scale()[0]
gc = mw @ Vector(c + ax * ((t.min() + tsuba_lo) / 2 if grip_low_side else (tsuba_hi + t.max()) / 2) + axis_pt)
for tag, ctr, size in (("whole", cw, Lw * 1.1), ("grip", gc, Lw * 0.4)):
    for k, d in enumerate((side, -side)):
        cam.data.ortho_scale = size
        cam.location = ctr + d * 10
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x, sc.render.resolution_y = (1400, 500) if tag == "whole" else (900, 600)
        sc.render.filepath = os.path.join(RDIR, f"katana_red_{tag}_{k}.png")
        bpy.ops.render.render(write_still=True)
print("[grip] RECOLOUR-OK", OUT)
