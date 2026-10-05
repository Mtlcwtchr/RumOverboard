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

