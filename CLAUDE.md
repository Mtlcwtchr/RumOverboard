# RumOverboard — правила проекта

Кооперативная пиратская игра на **Unity 6000.4 (URP)** + **Photon Fusion 2**.
Игровой код — в `Assets/Source/Scripts` (`Core`, `Networking`, `Gameplay`, `StateMachine`, `Editor`).
Редакторные инструменты — статические классы с `[MenuItem("RumOverboard/...")]` в `Assets/Source/Scripts/Editor`.

## Долговременная память (отложено)

Долговременная память агента через **Graphiti** пока **не подключена**. План на будущее и
шаги включения — в `Assets/Agents/AgentsTODO.md`. До активации никаких правил про память нет.

## Прочее

- Песочница корабля (host authority, лазание, штурвал, канаты, хинты): `RumOverboard ▸ Ship Sandbox ▸
  ★ Build Everything`, сцена `Assets/Scenes/ShipSandbox.unity`. Архитектура и автотесты —
  `Assets/Source/Docs/ShipSandbox.md`. Клиенты НЕ предсказывают физику — не возвращать prediction.

- Атрибуты сущностей (профили, флаги, эффекты, `NetworkAttributes`): `RumOverboard ▸ Attributes`,
  константы `Attr.* / AttrFlag.* / AttrEffect.*` — генерируемые, руками не править. Описание —
  `Assets/Source/Docs/Attributes.md`.
- Стамина экипажа (на атрибутах, host-only модель `CrewStamina`): `RumOverboard ▸ Stamina`, сцена
  `Assets/Scenes/StaminaScene.unity`, окно F7. Описание — `Assets/Source/Docs/Stamina.md`.
- Опьянение экипажа — атрибут `Drunkenness` (не `PlayerNetState`), модель `CrewIntoxication`, окно F5,
  `RumOverboard ▸ Intoxication`. Описание — `Assets/Source/Docs/Intoxication.md`.
- Предметы (один в руках, сундуки, разрушение, ром только из бутылки): `RumOverboard ▸ Items`, сцена
  `Assets/Scenes/ItemsScene.unity`, окно F8. Описание — `Assets/Source/Docs/Items.md`.
- Огонь (горение предметов, порох, фонарь, ведро воды; host-only `FireDirector`, визуал по флагам `FireVisuals`):
  `RumOverboard ▸ Fire`, сцена `Assets/Scenes/FireScene.unity`, окно F9. Описание — `Assets/Source/Docs/Fire.md`.
- Playtest (все механики в одной сцене + Windows-билд, стартующий в ней; мультиплеер с нескольких экземпляров):
  `RumOverboard ▸ Playtest`, сцена `Assets/Scenes/PlaytestScene.unity`, билд `Builds/Playtest`, F1 — справка/сеть.
  Описание и автопроверка (`-probe`) — `Assets/Source/Docs/Playtest.md`.
- Персонаж (арт-стиль, слоты кастомизации, требования к модели под рагдолл и first-person): концепты в
  `Art/Concepts/Character/`, описание — `Assets/Source/Docs/Character.md`. Модель: пайплайн Blender
  (`Art/Models/Character/pipeline/`, Blender 5.2 в `W:\Blender`) → `RumOverboard ▸ Character ▸ Import Base Model`
  → `Assets/Source/Art/Character/SailorBase.fbx` (Humanoid). Bake Axis Conversion не включать.
- Инструмент авто-коллайдеров: меню `RumOverboard → Colliders` и ПКМ по модели в Hierarchy
  (`Assets/Source/Scripts/Editor/ColliderTools`).
- Коммить/пушь только по явной просьбе.

## Graphify

Graphify подключён в проектном режиме (`.claude/`, `.agents/skills/graphify`).

Правила использования:
- Если есть `graphify-out/graph.json`, сначала отвечать через `graphify query "<вопрос>"`.
- Для связей между сущностями использовать `graphify path "<A>" "<B>"`.
- Для фокусного объяснения узла использовать `graphify explain "<concept>"`.
- После изменений в коде обновлять граф: `graphify update .` (или `graphify extract . --code-only`, если графа ещё нет).

## Workflow разработки механик

Для новых механик корабля соблюдать порядок:
- Сначала механика в изоляции.
- Отдельная тестовая сцена под механику с нужными условиями (в т.ч. вода/без воды).
- Обязательное debug-меню с runtime-настройками, reset и save-to-config.
- Все зависимости биндятся через агрегатор корабля (или несколько агрегаторов по подсистемам).
- Код каждой механики хранится в отдельной feature-директории (runtime, debug UI, config, setup сцены).
- Только после стабилизации в отдельной сцене — интеграция в gameplay-корабль.

