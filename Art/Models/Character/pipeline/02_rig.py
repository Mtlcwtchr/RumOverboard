"""Шаг 2 пайплайна персонажа: чистая база -> Humanoid-скелет, автоматические веса, FBX для Unity.

Запуск (headless):
    blender -b --factory-startup --python 02_rig.py -- <base_clean.blend> <out_dir>

Ожидания к входу (их обеспечивает 01_cleanup.py): один меш "Body", A-поза, стопы на Z=0,
лицо смотрит в -Y, левая сторона персонажа — +X.

Суставы ищутся по горизонтальным срезам меша (пересечение рёбер с плоскостью):
промежность, подмышки, самое узкое место шеи, линия руки, центр ноги, лодыжка, носок.
Имена костей — как в Unity HumanBodyBones, чтобы Avatar размечался автоматически.

На выходе: base_rigged.blend, base_rigged.fbx, превью тестовых поз и отчёт по весам.
"""
import bpy, sys, os, math
from mathutils import Vector, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
src_blend, out_dir = argv[0], argv[1]
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=src_blend)
body = bpy.data.objects["Body"]
me = body.data
co = [body.matrix_world @ v.co for v in me.vertices]
# Наклейка лица (M_Face) — отдельная оболочка над кожей: в срезы не берём, веса задаём сами.
face_mat = next((i for i, m in enumerate(me.materials) if m and m.name.startswith("M_Face")), -1)
decal_verts = {v for p in me.polygons if p.material_index == face_mat for v in p.vertices}
edges = [(co[e.vertices[0]], co[e.vertices[1]]) for e in me.edges
         if e.vertices[0] not in decal_verts and e.vertices[1] not in decal_verts]
H = max(c.z for c in co)


# --- Срезы ---------------------------------------------------------------------------------

def section(z):
    pts = []
    for a, b in edges:
        if (a.z - z) * (b.z - z) < 0:
            pts.append(a.lerp(b, (z - a.z) / (b.z - a.z)))
    return pts


def clusters(pts, gap=0.02):
    out = []
    for p in sorted(pts, key=lambda p: p.x):
        if out and p.x - out[-1]["x1"] < gap:
            out[-1]["x1"] = p.x
            out[-1]["pts"].append(p)
        else:
            out.append({"x0": p.x, "x1": p.x, "pts": [p]})
    for c in out:
        c["cx"] = (c["x0"] + c["x1"]) / 2
        c["cy"] = sum(p.y for p in c["pts"]) / len(c["pts"])
        c["y0"] = min(p.y for p in c["pts"])
        c["y1"] = max(p.y for p in c["pts"])
    return out


def zrange(a, b, step=0.01):
    n = int(round((b - a) / step))
    return [a + i * step for i in range(n + 1)]


slices = {round(z, 3): clusters(section(z)) for z in zrange(0.005, H - 0.005)}

# Промежность: самый нижний срез (выше трети роста), где центральный кластер проходит через X=0.
crotch_z = min(z for z, cl in slices.items()
               if z > H * 0.3 and any(c["x0"] < 0 < c["x1"] for c in cl))

# Руки: кластеры целиком справа от X=0.2 (левая рука персонажа) между промежностью и плечами.
arm = {z: max((c for c in cl if c["x0"] > 0.2), key=lambda c: c["cx"])
       for z, cl in slices.items() if any(c["x0"] > 0.2 for c in cl) and z < H * 0.8}
hand_tip_z = min(arm)
armpit_z = max(arm) + 0.01

# Линия руки: регрессия x(z) по центрам кластеров на участке без кисти и без подмышки.
fit = [(z, c["cx"], c["cy"]) for z, c in arm.items() if hand_tip_z + 0.18 < z < armpit_z - 0.04]
mz = sum(f[0] for f in fit) / len(fit)
mx = sum(f[1] for f in fit) / len(fit)
arm_k = sum((f[0] - mz) * (f[1] - mx) for f in fit) / sum((f[0] - mz) ** 2 for f in fit)
arm_y = sum(f[2] for f in fit) / len(fit)


def arm_x(z):
    return mx + arm_k * (z - mz)


# Шея: самый узкий срез между подмышками и макушкой (по всем точкам с |x| < 0.3).
def width(z):
    xs = [p.x for c in slices[z] for p in c["pts"] if abs(p.x) < 0.3]
    return max(xs) - min(xs) if xs else 9


neck_candidates = [z for z in slices if armpit_z + 0.08 < z < H - 0.3]
neck_z = min(neck_candidates, key=width)
neck_y = sum(c["cy"] for c in slices[neck_z]) / len(slices[neck_z])

# Ноги: центр левой ноги на середине голени/бедра; лодыжка — где кончается стопа впереди.
leg = [max(cl, key=lambda c: c["cx"]) for z, cl in slices.items() if 0.15 < z < crotch_z - 0.1]
leg_x = sum(c["cx"] for c in leg) / len(leg)
leg_y = sum((c["y0"] + c["y1"]) / 2 for c in leg) / len(leg)
foot_front = min(co[i].y for i in range(len(co)) if co[i].z < 0.05 and i not in decal_verts)
ankle_z = min(z for z, cl in slices.items()
              if z > 0.03 and all(c["y0"] > leg_y - 0.09 for c in cl if c["x0"] > 0))

torso_y = sum(c["cy"] for z in zrange(crotch_z + 0.05, armpit_z - 0.05, 0.03)
              for c in slices[round(z, 3)] if c["x0"] < 0 < c["x1"]) or 0
torso_n = sum(1 for z in zrange(crotch_z + 0.05, armpit_z - 0.05, 0.03)
              for c in slices[round(z, 3)] if c["x0"] < 0 < c["x1"])
torso_y = torso_y / torso_n if torso_n else neck_y

L = dict(H=H, crotch_z=crotch_z, armpit_z=armpit_z, neck_z=neck_z, hand_tip_z=hand_tip_z,
         arm_k=arm_k, arm_y=arm_y, leg_x=leg_x, leg_y=leg_y, ankle_z=ankle_z,
         foot_front=foot_front, torso_y=torso_y)
print("LANDMARKS", {k: round(v, 3) for k, v in L.items()})

# --- Суставы (левая сторона, +X) -----------------------------------------------------------

hand_len = 0.14
# Плечевой сустав — на радиус руки ниже верха плеча (луч вниз от уровня шеи, мимо головы и ушей);
# от «подмышка + константа» отказались: подмышка гуляет вместе с разбиением сетки.
arm_radius = 0.045
shoulder_x = arm_x(armpit_z + 0.05)
hit, top_loc, _, _ = body.ray_cast(Vector((shoulder_x, arm_y, neck_z)), Vector((0, 0, -1)))
shoulder_z = (top_loc.z - arm_radius) if hit else armpit_z + 0.05
# Кисть загнута внутрь относительно предплечья: запястье — на линии руки,
# кончик — по реальному центру варежки чуть выше самой нижней точки.
wrist_z = hand_tip_z + hand_len
wrist = Vector((arm_x(wrist_z), arm_y, wrist_z))
tip_c = arm[min(arm, key=lambda z: abs(z - (hand_tip_z + 0.02)))]
tip = Vector((tip_c["cx"], tip_c["cy"], hand_tip_z + 0.02))
upper_arm = Vector((arm_x(shoulder_z), arm_y, shoulder_z))
elbow = (upper_arm + wrist) / 2 + Vector((0, 0.02, 0))  # чуть назад: локоть гнётся вперёд

hip_joint = Vector((leg_x, leg_y, crotch_z - 0.02))
ankle = Vector((leg_x, leg_y, ankle_z))
knee = (hip_joint + ankle) / 2 + Vector((0, -0.02, 0))  # чуть вперёд: колено гнётся назад
# Носок — по оси стопы, не у самой подошвы и не в кончике (там стопа тонкая).
toes = Vector((leg_x, (leg_y + foot_front) / 2, 0.04))
toe_end = Vector((leg_x, foot_front + 0.025, 0.035))

hips = Vector((0, torso_y, crotch_z + 0.02))
spine = Vector((0, torso_y, crotch_z + (neck_z - crotch_z) * 0.22))
chest = Vector((0, torso_y, crotch_z + (neck_z - crotch_z) * 0.55))
neck = Vector((0, neck_y, neck_z - 0.06))
head = Vector((0, neck_y, neck_z + 0.04))
head_end = Vector((0, neck_y, H))
clavicle = Vector((0.04, torso_y, shoulder_z - 0.01))

# --- Скелет --------------------------------------------------------------------------------

arm_data = bpy.data.armatures.new("Armature")
rig = bpy.data.objects.new("Armature", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones


def bone(name, h, t, parent=None, connect=False):
    b = eb.new(name)
    b.head, b.tail = h, t
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b


bone("Hips", hips, spine)
bone("Spine", spine, chest, "Hips", True)
bone("Chest", chest, neck, "Spine", True)
bone("Neck", neck, head, "Chest", True)
bone("Head", head, head_end, "Neck", True)
for side, s in (("Left", 1), ("Right", -1)):
    m = lambda v: Vector((v.x * s, v.y, v.z))
    bone(side + "Shoulder", m(clavicle), m(upper_arm), "Chest")
    bone(side + "UpperArm", m(upper_arm), m(elbow), side + "Shoulder", True)
    bone(side + "LowerArm", m(elbow), m(wrist), side + "UpperArm", True)
    bone(side + "Hand", m(wrist), m(tip), side + "LowerArm", True)
    bone(side + "UpperLeg", m(hip_joint), m(knee), "Hips")
    bone(side + "LowerLeg", m(knee), m(ankle), side + "UpperLeg", True)
    bone(side + "Foot", m(ankle), m(toes), side + "LowerLeg", True)
    bone(side + "Toes", m(toes), m(toe_end), side + "Foot", True)

# Ролл: ось Z костей смотрит вперёд (-Y) — одинаково для обеих сторон.
for b in eb:
    b.align_roll(Vector((0, -1, 0)))
bpy.ops.object.mode_set(mode='OBJECT')

# Проверка: точки вдоль каждой кости лежат внутри меша. Тест чётности пересечений лучей (3 направления,
# большинство): «нормаль ближайшей точки» ошибается у вогнутостей вроде подмышки.
def inside(p):
    votes = 0
    for d in (Vector((0, 0, 1)), Vector((1, 0.13, 0.07)).normalized(), Vector((-0.11, 1, 0.05)).normalized()):
        hits, origin = 0, p.copy()
        for _ in range(64):
            ok, loc, _, _ = body.ray_cast(origin, d)
            if not ok:
                break
            hits += 1
            origin = loc + d * 1e-4
        votes += hits % 2
    return votes >= 2


outside = {}
for b in rig.data.bones:
    h, t = b.head_local, b.tail_local
    for i in range(1, 10):
        p = h.lerp(t, i / 10)
        if not inside(p):
            outside.setdefault(b.name, []).append(f"t={i / 10:.1f} {tuple(round(x, 3) for x in p)}")
print("BONES_OUTSIDE", outside or "none")

# --- Веса ----------------------------------------------------------------------------------

bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')

# Наклейка лица едет строго с головой, иначе при наклоне шеи она отстаёт от кожи и мерцает.
if decal_verts:
    ids = list(decal_verts)
    for g in body.vertex_groups:
        g.remove(ids)
    body.vertex_groups["Head"].add(ids, 1.0, 'REPLACE')
print("DECAL_VERTS", len(decal_verts), "-> Head")

groups = {g.index: g.name for g in body.vertex_groups}
unweighted = sum(1 for v in me.vertices if not any(g.weight > 1e-4 for g in v.groups))
dominant = {}
for v in me.vertices:
    if v.groups:
        g = max(v.groups, key=lambda g: g.weight)
        dominant[groups[g.group]] = dominant.get(groups[g.group], 0) + 1
print("WEIGHTS unweighted=%d of %d" % (unweighted, len(me.vertices)))
print("DOMINANT", dict(sorted(dominant.items())))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "base_rigged.blend"))

# --- Превью: rest-поза и тестовые позы -----------------------------------------------------

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.render.resolution_x, scene.render.resolution_y = 600, 600
scene.world = bpy.data.worlds.new("Preview")
scene.world.color = (0.78, 0.78, 0.78)
cam_data = bpy.data.cameras.new("Preview")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = H * 1.25
cam = bpy.data.objects.new("Preview", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

# Кости в превью — тонкие красные меши поверх тела (рендер костей Workbench не рисует).
bone_mat = bpy.data.materials.new("BoneViz")
bone_mat.diffuse_color = (0.9, 0.1, 0.1, 1)
viz = []
for b in rig.data.bones:
    h, t = rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local
    bpy.ops.mesh.primitive_cylinder_add(radius=0.008, depth=(t - h).length, vertices=6,
                                        location=(h + t) / 2)
    c = bpy.context.active_object
    c.rotation_euler = (t - h).to_track_quat('Z', 'Y').to_euler()
    c.data.materials.append(bone_mat)
    c.show_in_front = True
    viz.append(c)


def shoot(name, views=("front", "left", "three_quarter")):
    dirs = {"front": Vector((0, -1, 0)), "left": Vector((1, 0, 0)),
            "three_quarter": Vector((0.7, -0.7, 0.25)).normalized()}
    for v in views:
        d = dirs[v]
        cam.location = Vector((0, 0, H / 2)) + d * 5
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(out_dir, f"{name}_{v}.png")
        bpy.ops.render.render(write_still=True)


scene.display.shading.show_xray = True
scene.display.shading.xray_alpha = 0.45
shoot("rest")
scene.display.shading.show_xray = False
for c in viz:
    c.hide_render = True

# Тестовая поза в мировых осях (rest-ориентация кости R: локальный поворот = R^-1 * Q * R).
pose = [
    ("LeftUpperArm", 'Y', -70),   # поднять руку в сторону
    ("LeftLowerArm", 'X', -80),   # согнуть локоть, предплечье вперёд
    ("RightUpperArm", 'X', -70),  # правую руку вперёд
    ("LeftUpperLeg", 'X', -55),   # бедро вперёд
    ("LeftLowerLeg", 'X', 85),    # колено согнуто, голень назад
    ("Spine", 'X', 12),           # наклон вперёд
    ("Head", 'Z', 25),            # поворот головы
]
for name, axis, deg in pose:
    pb = rig.pose.bones[name]
    r = rig.data.bones[name].matrix_local.to_3x3().to_quaternion()
    q = Quaternion(Vector({'X': (1, 0, 0), 'Y': (0, 1, 0), 'Z': (0, 0, 1)}[axis]), math.radians(deg))
    pb.rotation_mode = 'QUATERNION'
    pb.rotation_quaternion = r.inverted() @ q @ r
bpy.context.view_layer.update()
shoot("pose")

# --- FBX для Unity -------------------------------------------------------------------------
# Проверено импортом (CharacterModelImport): персонаж, смотрящий в Blender в -Y, в Unity смотрит
# в +Z при Bake Axis Conversion = off. С bake = on Humanoid разворачивает таз на 180° при
# пересчёте позы (Get/SetHumanPose), поэтому запекание осей не используем.
for c in viz:
    bpy.data.objects.remove(c)
for pb in rig.pose.bones:
    pb.rotation_quaternion = (1, 0, 0, 0)
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(out_dir, "base_rigged.fbx"),
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y',
    use_armature_deform_only=True,
    add_leaf_bones=False,
    primary_bone_axis='Y', secondary_bone_axis='X',
    mesh_smooth_type='FACE',
    bake_anim=False,
    path_mode='AUTO',
)
print("DONE")
