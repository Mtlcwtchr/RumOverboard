"""Шаг 1 пайплайна персонажа: GLB из генератора -> чистая база под риг.

Запуск (headless):
    blender -b --factory-startup --python 01_cleanup.py -- <in.glb> <out_dir> [target_tris] [quadriflow|collapse]

Что делает:
- склеивает вершины, расщеплённые импортёром glTF по швам UV;
- центрирует по X, ставит стопы на пол (Z=0), лицом к -Y (вперёд в Blender);
- доводит до бюджета (по умолчанию 7000 треугольников):
  quadriflow (по умолчанию) — равномерная квадовая сетка с кольцами на суставах, UV и текстура
  переносятся с исходника; collapse — упрощение исходной сетки (быстрее, но хуже гнётся);
- пишет base_clean.blend, base_clean.glb и превью спереди / сбоку / сзади.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src_glb, out_dir = argv[0], argv[1]
target_tris = int(argv[2]) if len(argv) > 2 else 7000
mode = argv[3] if len(argv) > 3 else "quadriflow"
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src_glb)
obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
obj.name = obj.data.name = "Body"
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def topology(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    boundary = sum(1 for e in bm.edges if e.is_boundary)
    seen, islands = set(), 0
    for v in bm.verts:
        if v in seen: continue
        islands += 1
        stack = [v]
        while stack:
            x = stack.pop()
            if x in seen: continue
            seen.add(x)
            stack.extend(e.other_vert(x) for e in x.link_edges)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    bm.free()
    return dict(verts=len(o.data.vertices), tris=tris, islands=islands, boundary=boundary)


print("BEFORE", topology(obj))

# Склейка швов
bm = bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(obj.data); bm.free()
print("MERGED", topology(obj))


def assign_palette(dst, src):
    """Материалы-слоты по цвету текстуры исходника: кожа, майка, шорты, нос.

    Цвет грани — пиксель текстуры src в ближайшей к центру грани точке. Нос отделяется
    геометрией (выступ на лице), остальное — по тону и яркости с ограничением по высоте,
    затем шум сглаживается голосованием соседних граней. Глаз среди слотов нет — это декаль.
    Цвет материала — светлая четверть выборки класса (ближе к альбедо, чем среднее
    с запечённой тенью), переведённая из sRGB в линейный.
    """
    import numpy as np
    from mathutils.bvhtree import BVHTree
    from mathutils.geometry import barycentric_transform

    img = next(n.image for m in src.data.materials if m and m.use_nodes
               for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)

    smesh = src.data
    smesh.calc_loop_triangles()
    tris = smesh.loop_triangles
    uv = smesh.uv_layers.active.data
    bvh = BVHTree.FromPolygons([v.co for v in smesh.vertices], [t.vertices for t in tris])

    top = max(v.co.z for v in dst.data.vertices)
    head_z = top * 0.76      # ниже — тело, выше — голова
    shorts_max_z = top * 0.5  # шорты не выше середины роста
    classes = []
    colors = []
    for poly in dst.data.polygons:
        c = poly.center
        loc, _, ti, _ = bvh.find_nearest(c)
        t = tris[ti]
        p = [smesh.vertices[i].co for i in t.vertices]
        u = [uv[l].uv for l in t.loops]
        uvp = barycentric_transform(loc, p[0], p[1], p[2],
                                    u[0].to_3d(), u[1].to_3d(), u[2].to_3d())
        col = px[min(h - 1, max(0, int(uvp.y * h))), min(w - 1, max(0, int(uvp.x * w))), :3]
        r, g, b = (float(x) for x in col)
        mx, mn = max(r, g, b), min(r, g, b)
        sat = (mx - mn) / mx if mx > 1e-4 else 0
        warm = r - b > 0.06
        if c.z > head_z and c.y < -0.19 and abs(c.x) < 0.09:
            k = "Nose"
        elif c.z < shorts_max_z and not warm and mx < 0.5:
            k = "Shorts"
        elif head_z * 0.55 < c.z < head_z and sat < 0.2 and mx > 0.45:
            k = "Shirt"
        else:
            k = "Skin"
        classes.append(k)
        colors.append((r, g, b))

    # Сглаживание: грань принимает класс большинства соседей по рёбрам (нос не трогаем).
    edge_faces = {}
    for poly in dst.data.polygons:
        for ek in poly.edge_keys:
            edge_faces.setdefault(ek, []).append(poly.index)
    neighbors = [set() for _ in dst.data.polygons]
    for fs in edge_faces.values():
        for a in fs:
            neighbors[a].update(f for f in fs if f != a)
    for _ in range(4):
        new = list(classes)
        for i, nb in enumerate(neighbors):
            if classes[i] == "Nose" or not nb:
                continue
            votes = {}
            for j in nb:
                votes[classes[j]] = votes.get(classes[j], 0) + 1
            best = max(votes, key=votes.get)
            if votes[best] > len(nb) / 2:
                new[i] = best
        classes = new

    def to_linear(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    names = ["Skin", "Shirt", "Shorts", "Nose"]
    dst.data.materials.clear()
    for n in names:
        sel = sorted((colors[i] for i, k in enumerate(classes) if k == n), key=sum)
        sel = sel[len(sel) * 3 // 4:] or [(0.5, 0.5, 0.5)]
        avg = [to_linear(sum(c[j] for c in sel) / len(sel)) for j in range(3)]
        m = bpy.data.materials.new("M_" + n)
        m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*avg, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
        m.diffuse_color = (*avg, 1)
        dst.data.materials.append(m)
    for poly, k in zip(dst.data.polygons, classes):
        poly.material_index = names.index(k)
    print("PALETTE", {n: classes.count(n) for n in names})

# Пол и центр: стопы на Z=0, центр по X/Y
co = [v.co.copy() for v in obj.data.vertices]
lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
for v in obj.data.vertices:
    v.co += shift

if mode == "quadriflow":
    # Ремеш копии, исходник остаётся источником UV/текстуры для переноса.
    src = obj.copy(); src.data = obj.data.copy(); src.name = "Source"
    bpy.context.scene.collection.objects.link(src)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.quadriflow_remesh(mode='FACES', target_faces=target_tris // 2,
                                     use_mesh_symmetry=True, use_preserve_sharp=False,
                                     use_preserve_boundary=False, smooth_normals=False, seed=1)
    # QuadriFlow стирает UV. Перенос UV даёт трещины на швах, а текстура по стилю всё равно
    # заменяется плоской палитрой — поэтому раскладываем грани по материалам по цвету исходника.
    assign_palette(obj, src)
    bpy.data.objects.remove(src)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
else:
    tris_now = topology(obj)["tris"]
    dec = obj.modifiers.new("Decimate", 'DECIMATE')
    dec.decimate_type = 'COLLAPSE'
    dec.ratio = min(1.0, target_tris / tris_now)
    dec.use_symmetry = True
    dec.symmetry_axis = 'X'
    bpy.ops.object.modifier_apply(modifier=dec.name)
bpy.ops.object.shade_smooth()
print("REDUCED", mode, topology(obj), "height", round(hi.z - lo.z, 3))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "base_clean.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, "base_clean.glb"), use_selection=True)

# Превью
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL' if mode == "quadriflow" else 'TEXTURE'
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 600, 600
scene.world = bpy.data.worlds.new("Preview")
scene.world.color = (0.78, 0.78, 0.78)
cam_data = bpy.data.cameras.new("Preview"); cam_data.type = 'ORTHO'
cam_data.ortho_scale = (hi.z - lo.z) * 1.1
cam = bpy.data.objects.new("Preview", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
target = Vector((0, 0, (hi.z - lo.z) / 2))
for name, d in {"front": Vector((0, -1, 0)), "left": Vector((1, 0, 0)), "back": Vector((0, 1, 0))}.items():
    cam.location = target + d * 5
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(out_dir, f"preview_{name}.png")
    bpy.ops.render.render(write_still=True)
print("DONE")
