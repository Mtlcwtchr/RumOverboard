# RumOverboard — правила проекта

Кооперативная пиратская игра на **Unity 6000.4 (URP)** + **Photon Fusion 2**.
Игровой код — в `Assets/Source/Scripts` (`Core`, `Networking`, `Gameplay`, `StateMachine`, `Editor`).
Редакторные инструменты — статические классы с `[MenuItem("RumOverboard/...")]` в `Assets/Source/Scripts/Editor`.

## Долговременная память (отложено)

Долговременная память агента через **Graphiti** пока **не подключена**. План на будущее и
шаги включения — в `Assets/Agents/AgentsTODO.md`. До активации никаких правил про память нет.

## Прочее

- Инструмент авто-коллайдеров: меню `RumOverboard → Colliders` и ПКМ по модели в Hierarchy
  (`Assets/Source/Scripts/Editor/ColliderTools`).
- Коммить/пушь только по явной просьбе.
