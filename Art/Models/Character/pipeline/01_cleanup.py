"""Шаг 1 пайплайна персонажа: GLB из генератора -> чистая база под риг.

Запуск (headless):
    blender -b --factory-startup --python 01_cleanup.py -- <in.glb> <out_dir> [target_tris] [quadriflow|collapse]

Что делает:
- склеивает вершины, расщеплённые импортёром glTF по швам UV;
- центрирует по X, ставит стопы на пол (Z=0), лицом к -Y (вперёд в Blender);
- доводит до бюджета (по умолчанию ~7.4k треугольников + наклейка лица, итог в пределах 8k):
  quadriflow (по умолчанию) — равномерная квадовая сетка с кольцами на суставах;
  collapse — упрощение исходной сетки с её текстурой (быстрее, но хуже гнётся, без палитры и лица);
- quadriflow дальше:
  * глаза и рот ищутся по тёмным пикселям текстуры исходника; выпуклости глаз в геометрии заглаживаются;
  * грани раскладываются по материалам палитры эталона (кожа, майка, шорты, нос); подолы майки и шорт
    прорезаются ровной петлёй рёбер, мелкие островки цвета поглощаются соседями;
  * поверх лица — наклейка (дубликат граней лица на 1.5 мм над кожей, материал M_Face) с атласом
    начертаний глаз (eyes_atlas.png, 3x2 ячейки; рот общий для всех);
- пишет base_clean.blend, base_clean.glb, eyes_atlas.png и превью спереди / сбоку / сзади.
"""
import bpy, bmesh, sys, os, math, json
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src_glb, out_dir = argv[0], argv[1]
target_tris = int(argv[2]) if len(argv) > 2 else 6800
mode = argv[3] if len(argv) > 3 else "quadriflow"
os.makedirs(out_dir, exist_ok=True)

# Палитра эталона (meme-moryak-varianty / разворот), sRGB. Меняется параметрами материалов.
PALETTE = {
    "Skin": (230, 190, 165),
    "Shirt": (232, 231, 228),
    "Shorts": (30, 32, 52),
    "Nose": (222, 140, 122),
}
# Атлас глаз: порядок ячеек = enum EyeStyle в Unity (SailorEyes). Ячейка i: колонка i % 3,
# ряд 1 - i // 3 (первые три — верхний ряд). Рот рисуется во всех ячейках одинаково.
EYE_STYLES = ["Dots", "Sleepy", "Angry", "Bulging", "Patch", "Crosses"]
ATLAS_COLS, ATLAS_ROWS, ATLAS_CELL = 3, 2, 512
DECAL_SIZE = 0.32     # сторона квадрата UV наклейки, м
DECAL_OFFSET = 0.0015  # наклейка над кожей, м

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


def to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


print("BEFORE", topology(obj))

# Склейка швов
bm = bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(obj.data); bm.free()
print("MERGED", topology(obj))


class SourceTexture:
    """Цвет текстуры исходника в ближайшей точке его поверхности."""

    def __init__(self, src):
        import numpy as np
        from mathutils.bvhtree import BVHTree
        img = next(n.image for m in src.data.materials if m and m.use_nodes
                   for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
        self.w, self.h = img.size
        px = np.empty(self.w * self.h * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        self.px = px.reshape(self.h, self.w, 4)
        self.mesh = src.data
        self.mesh.calc_loop_triangles()
        self.tris = self.mesh.loop_triangles
        self.uv = self.mesh.uv_layers.active.data
        self.bvh = BVHTree.FromPolygons([v.co for v in self.mesh.vertices], [t.vertices for t in self.tris])

    def texel(self, uv):
        return tuple(float(x) for x in self.px[min(self.h - 1, max(0, int(uv.y * self.h))),
                                                min(self.w - 1, max(0, int(uv.x * self.w))), :3])

    def color_at(self, point):
        from mathutils.geometry import barycentric_transform
        loc, _, ti, _ = self.bvh.find_nearest(point)
        t = self.tris[ti]
        p = [self.mesh.vertices[i].co for i in t.vertices]
        u = [self.uv[l].uv for l in t.loops]
        return self.texel(barycentric_transform(loc, p[0], p[1], p[2], u[0].to_3d(), u[1].to_3d(), u[2].to_3d()))


def find_face_marks(tex, top):
    """Глаза и рот — тёмные пятна текстуры на лице. Возвращает центры (левый глаз +X, правый -X, рот)."""
    marks = []  # (центр, площадь)
    for t in tex.tris:
        p = [tex.mesh.vertices[i].co for i in t.vertices]
        c = (p[0] + p[1] + p[2]) / 3
        if c.z < top * 0.76 or c.y > -0.05:
            continue
        uv = (tex.uv[t.loops[0]].uv + tex.uv[t.loops[1]].uv + tex.uv[t.loops[2]].uv) / 3
        if max(tex.texel(uv)) < 0.2:
            marks.append((c, t.area))

    def centroid(sel):
        a = sum(m[1] for m in sel)
        return sum((m[0] * m[1] for m in sel), Vector()) / a, math.sqrt(a / math.pi)

    eyes = [m for m in marks if abs(m[0].x) > 0.03]
    left, lr = centroid([m for m in eyes if m[0].x > 0])
    right, rr = centroid([m for m in eyes if m[0].x < 0])
    # Персонаж симметричен: глаза зеркально, рот по оси. Размеры — пропорции эталона (разворот):
    # диаметр точки = 0.16 межглазья, ширина рта = 0.2 межглазья.
    ex, ez, ey = (left.x - right.x) / 2, (left.z + right.z) / 2, (left.y + right.y) / 2
    left, right = Vector((ex, ey, ez)), Vector((-ex, ey, ez))
    mouth_marks = [m for m in marks if abs(m[0].x) <= 0.03 and m[0].z < ez - 0.03]
    mouth, _ = centroid(mouth_marks) if mouth_marks else (Vector((0, ey, ez - 0.12)), 0)
    mouth.x = 0
    spacing = 2 * ex
    print("FACE_MARKS texel eye radius", round((lr + rr) / 2, 4), "-> concept", round(0.08 * spacing, 4))
    return dict(left=left, right=right, eye_r=0.08 * spacing, mouth=mouth, mouth_w=0.2 * spacing)


def flatten_eyes(o, marks):
    """Заглаживает вылепленные точки глаз: лапласово сглаживание с затуханием вокруг центров."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    radius = max(0.03, marks["eye_r"] * 2.5)
    weights = {}
    for v in bm.verts:
        for c in (marks["left"], marks["right"]):
            d = (v.co - c).length
            if d < radius:
                weights[v] = max(weights.get(v, 0.0), 1.0 - d / radius)
    for _ in range(20):
        moved = {}
        for v, w in weights.items():
            nb = [e.other_vert(v).co for e in v.link_edges]
            avg = sum(nb, Vector()) / len(nb)
            moved[v] = v.co + (avg - v.co) * 0.5 * w
        for v, co in moved.items():
            v.co = co
    bm.to_mesh(o.data); bm.free()
    print("EYES_FLATTENED", len(weights), "verts within", round(radius, 3))


def classify(o, tex):
    """Класс палитры по цвету исходника + ограничения по высоте (нос — геометрией)."""
    top = max(v.co.z for v in o.data.vertices)
    head_z = top * 0.76
    shorts_max_z = top * 0.5
    classes = []
    for poly in o.data.polygons:
        c = poly.center
        r, g, b = tex.color_at(c)
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
    return classes


def face_neighbors(o):
    edge_faces = {}
    for poly in o.data.polygons:
        for ek in poly.edge_keys:
            edge_faces.setdefault(ek, []).append(poly.index)
    nbs = [set() for _ in o.data.polygons]
    for fs in edge_faces.values():
        for a in fs:
            nbs[a].update(f for f in fs if f != a)
    return edge_faces, nbs


def hem_heights(o, classes):
    """Высоты подолов: граница майка/шорты и шорты/кожа на ногах (медиана по граничным рёбрам)."""
    edge_faces, _ = face_neighbors(o)
    verts = o.data.vertices
    shirt, legs = [], []
    for (a, b), fs in edge_faces.items():
        if len(fs) != 2:
            continue
        pair = {classes[fs[0]], classes[fs[1]]}
        mid = (verts[a].co + verts[b].co) / 2
        if pair == {"Shirt", "Shorts"}:
            shirt.append(mid.z)
        elif pair == {"Shorts", "Skin"} and abs(mid.x) < 0.25:
            legs.append(mid.z)
    shirt_z = sorted(shirt)[len(shirt) // 2]
    legs = [z for z in legs if z < shirt_z - 0.1]
    return shirt_z, sorted(legs)[len(legs) // 2]


def cut_hems(o, heights):
    bm = bmesh.new(); bm.from_mesh(o.data)
    for z in heights:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=0.004, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    bm.to_mesh(o.data); bm.free()


def enforce_hems(o, classes, shirt_z, leg_z):
    """Подолы по прорезанным петлям: между ними — шорты, сразу над/под — майка/кожа."""
    body_half = 0.27  # дальше от оси по X на этих высотах — руки
    for i, poly in enumerate(o.data.polygons):
        c = poly.center
        if classes[i] == "Nose" or abs(c.x) >= body_half:
            if abs(c.x) >= body_half and c.z < shirt_z and classes[i] in ("Shirt", "Shorts"):
                classes[i] = "Skin"
            continue
        if leg_z < c.z < shirt_z:
            classes[i] = "Shorts"
        elif shirt_z < c.z < shirt_z + 0.04:
            classes[i] = "Shirt"
        elif leg_z - 0.04 < c.z < leg_z:
            classes[i] = "Skin"
    return classes


def smooth_shirt_edge(o, classes, shirt_z):
    """Горловина и проймы: граница майка/кожа по изолинии сглаженного поля «майковости» вершин
    (вместо ступенек по граням) — рёбра режутся на уровне 0.5 и соединяются внутри граней."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    region = lambda f: f.calc_center_median().z > shirt_z + 0.03 and classes[f.index] in ("Shirt", "Skin")
    field = {}
    for v in bm.verts:
        fs = [f for f in v.link_faces if region(f)]
        if fs:
            field[v] = sum(1.0 for f in fs if classes[f.index] == "Shirt") / len(fs)
    for _ in range(6):
        field = {v: 0.5 * s + 0.5 * (sum(field.get(e.other_vert(v), s) for e in v.link_edges) / len(v.link_edges))
                 for v, s in field.items()}
    cut_verts = set()
    for e in list(bm.edges):
        a, b = e.verts
        if a not in field or b not in field:
            continue
        fa, fb = field[a], field[b]
        if (fa - 0.5) * (fb - 0.5) < 0:
            t = (0.5 - fa) / (fb - fa)
            if 0.08 < t < 0.92:
                _, nv = bmesh.utils.edge_split(e, a, t)
                field[nv] = 0.5
                cut_verts.add(nv)
            else:
                cut_verts.add(a if t <= 0.08 else b)  # почти в вершине: режем через неё
    for f in list(bm.faces):
        on = [v for v in f.verts if v in cut_verts]
        if len(on) == 2 and not any(e for e in f.edges if set(e.verts) == set(on)):
            bmesh.ops.connect_verts(bm, verts=on)
    # Грань — майка, если среднее поле её вершин выше 0.5 (иначе кожа); вне зоны класс не трогаем.
    old_class = {f: classes[f.index] for f in bm.faces if f.index < len(classes)}
    bm.faces.index_update()
    bm.to_mesh(o.data)
    new_classes = []
    for f in bm.faces:
        k = old_class.get(f)
        c = f.calc_center_median()
        if k is None or (c.z > shirt_z + 0.03 and k in ("Shirt", "Skin")):
            vals = [field.get(v) for v in f.verts if v in field]
            if vals and c.z > shirt_z + 0.03:
                k = "Shirt" if sum(vals) / len(vals) > 0.5 else "Skin"
            elif k is None:
                k = "Skin"
        new_classes.append(k)
    bm.free()
    return new_classes


def refine_classes(o, classes, shirt_z, leg_z):
    """Ровные подолы по прорезанным петлям, сглаживание голосованием, поглощение мелких островков."""
    classes = enforce_hems(o, classes, shirt_z, leg_z)
    _, nbs = face_neighbors(o)
    for _ in range(4):
        new = list(classes)
        for i, nb in enumerate(nbs):
            if classes[i] == "Nose" or not nb:
                continue
            votes = {}
            for j in nb:
                votes[classes[j]] = votes.get(classes[j], 0) + 1
            best = max(votes, key=votes.get)
            if votes[best] > len(nb) / 2:
                new[i] = best
        classes = new

    for _ in range(3):
        seen = set()
        for start in range(len(classes)):
            if start in seen or classes[start] == "Nose":
                continue
            comp, stack = [], [start]
            while stack:
                f = stack.pop()
                if f in seen or classes[f] != classes[start]:
                    continue
                seen.add(f); comp.append(f)
                stack.extend(nbs[f])
            if len(comp) >= 24:
                continue
            votes = {}
            for f in comp:
                for j in nbs[f]:
                    if classes[j] != classes[start]:
                        votes[classes[j]] = votes.get(classes[j], 0) + 1
            if votes:
                best = max(votes, key=votes.get)
                for f in comp:
                    classes[f] = best

    # Симметрия: сетка QuadriFlow зеркальна по X — правая сторона берёт класс зеркальной левой грани.
    from mathutils.kdtree import KDTree
    polys = o.data.polygons
    kd = KDTree(len(polys))
    for p in polys:
        kd.insert(p.center, p.index)
    kd.balance()
    for p in polys:
        if p.center.x < -0.002:
            mirror, j, dist = kd.find(Vector((-p.center.x, p.center.y, p.center.z)))
            if dist < 0.02:
                classes[p.index] = classes[j]
    return enforce_hems(o, classes, shirt_z, leg_z)


def apply_palette(o, classes):
    o.data.materials.clear()
    for n, srgb in PALETTE.items():
        col = tuple(to_linear(c / 255) for c in srgb)
        m = bpy.data.materials.new("M_" + n)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*col, 1)
        bsdf.inputs["Roughness"].default_value = 0.85
        m.diffuse_color = (*col, 1)
        o.data.materials.append(m)
    names = list(PALETTE)
    for poly, k in zip(o.data.polygons, classes):
        poly.material_index = names.index(k)
    print("PALETTE", {n: classes.count(n) for n in names})


def draw_atlas(marks, cz, path):
    """Атлас начертаний глаз (RGBA, сглаженные края по расстоянию). Координаты ячейки — UV наклейки."""
    import numpy as np
    cell = ATLAS_CELL
    ppm = cell / DECAL_SIZE
    W, H = ATLAS_COLS * cell, ATLAS_ROWS * cell
    rgba = np.zeros((H, W, 4), dtype=np.float32)

    # Центры в метрах в плоскости наклейки (x вправо = +X персонажа, z вверх).
    L = (marks["left"].x, marks["left"].z - cz)
    R = (marks["right"].x, marks["right"].z - cz)
    M = (marks["mouth"].x, marks["mouth"].z - cz)
    r = marks["eye_r"]
    ink = (0.10, 0.09, 0.09)
    white = (0.96, 0.96, 0.94)
    skin = tuple(c / 255 for c in PALETTE["Skin"])
    brow = (0.27, 0.19, 0.15)

    ys, xs = np.mgrid[0:cell, 0:cell]
    X = (xs + 0.5) / ppm - DECAL_SIZE / 2
    Z = (ys + 0.5) / ppm - DECAL_SIZE / 2

    def circle(c, rad):
        return np.hypot(X - c[0], Z - c[1]) - rad

    def ellipse(c, rx, rz):
        k = np.hypot((X - c[0]) / rx, (Z - c[1]) / rz)
        return (k - 1) * min(rx, rz)

    def segment(a, b, half_w):
        ax, az = X - a[0], Z - a[1]
        bx, bz = b[0] - a[0], b[1] - a[1]
        t = np.clip((ax * bx + az * bz) / (bx * bx + bz * bz), 0, 1)
        return np.hypot(ax - bx * t, az - bz * t) - half_w

    def paint(img, sdf, color):
        a = np.clip(0.5 - sdf * ppm, 0, 1)[..., None]
        img[..., :3] = img[..., :3] * (1 - a) + np.array(color) * a
        img[..., 3:] = img[..., 3:] * (1 - a) + a

    for i, style in enumerate(EYE_STYLES):
        img = np.zeros((cell, cell, 4), dtype=np.float32)
        for eye, side in ((L, 1), (R, -1)):
            if style == "Dots" or (style == "Patch" and side == 1):
                paint(img, circle(eye, r), ink)
            elif style == "Sleepy":
                paint(img, circle(eye, r * 1.6), white)
                paint(img, circle((eye[0], eye[1] - r * 0.45), r * 0.75), ink)
                lid = np.maximum(circle(eye, r * 1.6 + 0.0008), -(Z - (eye[1] - r * 0.15)))
                paint(img, lid, skin)
                paint(img, segment((eye[0] - r * 1.6, eye[1] - r * 0.15), (eye[0] + r * 1.6, eye[1] - r * 0.15), 0.0012), ink)
            elif style == "Angry":
                paint(img, circle(eye, r * 1.05), ink)
                outer = (eye[0] + side * r * 2.3, eye[1] + r * 3.0)
                inner = (eye[0] - side * r * 1.3, eye[1] + r * 1.5)
                paint(img, segment(outer, inner, r * 0.38), brow)
            elif style == "Bulging":
                paint(img, circle(eye, r * 2.25), ink)
                paint(img, circle(eye, r * 2.25 - 0.0018), white)
                paint(img, circle(eye, r * 0.9), ink)
            elif style == "Patch":
                # Ремешок уходит к краям головы (туда, где лицо заворачивает и наклейка кончается).
                # Концы — на сантиметр внутри ячейки, иначе фильтрация текстуры тянет их в соседнюю.
                edge = DECAL_SIZE / 2 - 0.01
                paint(img, segment((eye[0] - r * 2.0, eye[1] + r * 0.6), (-edge, eye[1] + r * 1.4), 0.0016), ink)
                paint(img, segment((eye[0] + r * 1.6, eye[1] + r * 1.4), (edge, min(Z1_OFFSET, edge)), 0.0016), ink)
                paint(img, ellipse(eye, r * 2.4, r * 2.0), ink)
            elif style == "Crosses":
                d = r * 1.25
                paint(img, segment((eye[0] - d, eye[1] - d), (eye[0] + d, eye[1] + d), r * 0.32), ink)
                paint(img, segment((eye[0] - d, eye[1] + d), (eye[0] + d, eye[1] - d), r * 0.32), ink)
        half = marks["mouth_w"] / 2
        paint(img, segment((M[0] - half, M[1]), (M[0] + half, M[1]), 0.0014), ink)
        col, row = i % ATLAS_COLS, ATLAS_ROWS - 1 - i // ATLAS_COLS
        rgba[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell] = img

    atlas = bpy.data.images.new("SailorEyes", W, H, alpha=True)
    atlas.pixels.foreach_set(rgba.ravel())
    atlas.filepath_raw = path
    atlas.file_format = 'PNG'
    atlas.save()
    return atlas


def add_face_decal(o, marks, atlas_path):
    """Наклейка лица: грани кожи вокруг глаз и рта дублируются на 1.5 мм наружу, UV — фронтальная
    проекция в квадрат DECAL_SIZE; материал M_Face с атласом (ячейка выбирается тайлингом)."""
    global Z1_OFFSET
    eye_z = marks["left"].z
    z0, z1 = marks["mouth"].z - 0.025, eye_z + 0.11
    cz = (z0 + z1) / 2
    Z1_OFFSET = z1 - cz  # верхний край наклейки в её координатах (для ремешка повязки)
    atlas = draw_atlas(marks, cz, atlas_path)

    m = bpy.data.materials.new("M_Face")
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = atlas
    tex.extension = 'CLIP'
    mapping = nt.nodes.new("ShaderNodeMapping")
    coord = nt.nodes.new("ShaderNodeTexCoord")
    mapping.inputs["Scale"].default_value = (1 / ATLAS_COLS, 1 / ATLAS_ROWS, 1)
    mapping.inputs["Location"].default_value = (0, (ATLAS_ROWS - 1) / ATLAS_ROWS, 0)  # ячейка Dots
    nt.links.new(coord.outputs["UV"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.85
    # Workbench (превью в шаге 2) рисует цвет вьюпорта: делаем его цветом кожи, чтобы наклейка не мешала.
    m.diffuse_color = (*[to_linear(c / 255) for c in PALETTE["Skin"]], 1)
    o.data.materials.append(m)
    face_index = len(o.data.materials) - 1
    nose_index = list(PALETTE).index("Nose")

    bm = bmesh.new(); bm.from_mesh(o.data)
    bm.normal_update()
    faces = [f for f in bm.faces
             if f.material_index != nose_index and abs(f.calc_center_median().x) < DECAL_SIZE / 2
             and z0 < f.calc_center_median().z < z1 and f.normal.y < -0.2]
    normals = {v: v.normal.copy() for f in faces for v in f.verts}
    dup = bmesh.ops.duplicate(bm, geom=faces + list({e for f in faces for e in f.edges}) + list(normals))
    vmap = dup["vert_map"]
    for v, n in normals.items():
        vmap[v].co = v.co + n * DECAL_OFFSET
    uv_layer = bm.loops.layers.uv.active
    new_faces = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
    for f in new_faces:
        f.material_index = face_index
        for loop in f.loops:
            loop[uv_layer].uv = (loop.vert.co.x / DECAL_SIZE + 0.5, (loop.vert.co.z - cz) / DECAL_SIZE + 0.5)
    bm.to_mesh(o.data); bm.free()
    print("FACE_DECAL", len(new_faces), "faces; eyes",
          tuple(round(x, 3) for x in marks["left"]), tuple(round(x, 3) for x in marks["right"]),
          "r", round(marks["eye_r"], 4), "mouth", tuple(round(x, 3) for x in marks["mouth"]))
    return dict(center_z=cz, size=DECAL_SIZE, styles=EYE_STYLES, cols=ATLAS_COLS, rows=ATLAS_ROWS,
                left_eye=list(marks["left"]), right_eye=list(marks["right"]), eye_radius=marks["eye_r"],
                mouth=list(marks["mouth"]))


# Пол и центр: стопы на Z=0, центр по X/Y
co = [v.co.copy() for v in obj.data.vertices]
lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
for v in obj.data.vertices:
    v.co += shift

if mode == "quadriflow":
    # Ремеш копии, исходник остаётся источником цвета и меток лица.
    src = obj.copy(); src.data = obj.data.copy(); src.name = "Source"
    bpy.context.scene.collection.objects.link(src)
    tex = SourceTexture(src)
    marks = find_face_marks(tex, hi.z - lo.z)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    # QuadriFlow попадает в число граней неточно (и нелинейно) — подбираем цель, пока тело не ляжет
    # в коридор; петли подолов и наклейка лица добавят ещё ~0.4k.
    original = obj.data.copy()
    faces_goal = target_tris // 2
    for attempt in range(6):
        obj.data = original.copy()
        bpy.ops.object.quadriflow_remesh(mode='FACES', target_faces=faces_goal,
                                         use_mesh_symmetry=True, use_preserve_sharp=False,
                                         use_preserve_boundary=False, smooth_normals=False, seed=1)
        tris = topology(obj)["tris"]
        print("QUADRIFLOW target", faces_goal, "->", tris, "tris")
        if 6600 <= tris <= 7500:
            break
        faces_goal = int(faces_goal * (7050 / tris) ** 0.8)
    obj.data.name = "Body"
    flatten_eyes(obj, marks)
    # QuadriFlow стирает UV. Перенос UV даёт трещины на швах, а текстура по стилю заменяется плоской
    # палитрой — поэтому раскладываем грани по материалам по цвету исходника.
    shirt_z, leg_z = hem_heights(obj, classify(obj, tex))
    cut_hems(obj, (shirt_z, leg_z))
    classes = refine_classes(obj, classify(obj, tex), shirt_z, leg_z)
    classes = smooth_shirt_edge(obj, classes, shirt_z)
    print("HEMS shirt", round(shirt_z, 3), "legs", round(leg_z, 3))
    apply_palette(obj, classes)
    bpy.data.objects.remove(src)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    decal = add_face_decal(obj, marks, os.path.join(out_dir, "eyes_atlas.png"))
    with open(os.path.join(out_dir, "face_decal.json"), "w", encoding="utf-8") as f:
        json.dump(decal, f, indent=2)
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

# Превью: EEVEE (видна наклейка лица с альфой), иначе Workbench.
scene = bpy.context.scene
try:
    scene.render.engine = 'BLENDER_EEVEE'
except TypeError:
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x, scene.render.resolution_y = 600, 600
scene.view_settings.view_transform = 'Standard'
scene.world = bpy.data.worlds.new("Preview")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.6, 0.6, 1)
scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
sun.data.energy = 2.5
sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
scene.collection.objects.link(sun)
cam_data = bpy.data.cameras.new("Preview"); cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("Preview", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
height = hi.z - lo.z
shots = {"front": (Vector((0, -1, 0)), height / 2, height * 1.1),
         "left": (Vector((1, 0, 0)), height / 2, height * 1.1),
         "back": (Vector((0, 1, 0)), height / 2, height * 1.1),
         "face": (Vector((0, -1, 0)), height * 0.86, 0.42)}
for name, (d, z, scale) in shots.items():
    cam_data.ortho_scale = scale
    cam.location = Vector((0, 0, z)) + d * 5
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(out_dir, f"preview_{name}.png")
    bpy.ops.render.render(write_still=True)
print("DONE")
