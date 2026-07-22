# Coop Sailing: план параллельной разработки без пересечения зон

Основано на `Coop_Sailing_GDD_TDD.md`.

## 1. Цель и ограничения

Цель: разложить разработку на независимые потоки так, чтобы команды могли работать параллельно, минимально блокируя друг друга.

Важно: полностью независимой вся разработка не будет. Базовые блоки (`Networking`, `Ship Physics`, `State/Events Contracts`) остаются критическими зависимостями.

## 2. Принцип декомпозиции

- **Вертикали по доменам**: каждая команда владеет своей подсистемой и ее внутренними данными.
- **Горизонтальные контракты**: интеграция только через интерфейсы/DTO/события, без прямого доступа к чужой логике.
- **Стабильные API-границы**: сначала фиксируем минимальные контракты, потом команды реализуют независимо.
- **Интеграция по графику**: ежедневная внутренняя интеграция в потоке, кросс-потоковая - по milestones.

## 3. Модульная карта (ownership)

## Tier 0 (Foundation)
- `Net Runtime` (Fusion lifecycle, authority, replication policy)
- `Sim Clock & Determinism` (tick/time contracts)
- `Campaign/Session State` (единый state snapshot)

## Tier 1 (Ship Core)
- `Hull Rigidbody Core`
- `Buoyancy & Hydro`
- `Deck as Moving Platform`

## Tier 2 (Sailing)
- `Sail Control`
- `Wind & Aero`
- `Rudder/Wheel`
- `Rope Runtime`

## Tier 3 (Damage)
- `Hull Breaches`
- `Flooding Volume`
- `Fire/Decay`

## Tier 4 (Player)
- `Character Locomotion`
- `Climb/Grab`
- `Ragdoll/Impulse`
- `Drowning/Rescue`

## Tier 5 (Content/Director)
- `Weather & Encounter Director`
- `Wildlife/Threats`
- `World Interactables`

## Tier 6 (Presentation)
- `HUD/UI`
- `Audio`
- `Cosmetics`
- `Save/Load UX`

## 4. Минимальные контракты между потоками

Ниже контракты, которые нужно зафиксировать в первую очередь (и не ломать без RFC).

```csharp
public interface IShipPhysics
{
    Vector3 Position { get; }
    Quaternion Rotation { get; }
    Vector3 LinearVelocity { get; }
    Vector3 AngularVelocity { get; }
    float HeelDeg { get; }     // крен
    float PitchDeg { get; }    // тангаж
}

public interface IShipControlInput
{
    float Rudder01 { get; }    // -1..1
    float SailTrim01 { get; }  // 0..1
    float Throttle01 { get; }  // если применимо
}

public interface IRopeRuntime
{
    bool TryAttach(int playerId, int ropeId, RopeAttachPoint point);
    bool TryDetach(int playerId, int ropeId);
    RopeStateSnapshot GetState(int ropeId);
}

public interface IDamageSystem
{
    void ReportHullImpact(Vector3 worldPos, float impulse);
    void ReportBreach(Vector3 worldPos, float area);
    DamageSnapshot GetSnapshot();
}

public interface IFloodingSystem
{
    float WaterVolume { get; }
    float IngressRate { get; }
    void AddWater(float amount);
    FloodingSnapshot GetSnapshot();
}

public interface IPlayerState
{
    bool IsGrounded { get; }
    bool IsSwimming { get; }
    bool IsDrowning { get; }
    float Stamina01 { get; }
}
```

События (шина):
- `ShipHeeled`
- `BreachCreated`
- `FloodingLevelChanged`
- `PlayerOverboard`
- `RescueStarted/RescueCompleted`

## 5. Потоки разработки (независимые команды)

## Stream A - Core Platform
- Зона: `Networking`, authority model, net snapshots, deterministic tick.
- Выход: стабильная среда для остальных потоков.
- Не лезет в геймдизайн механик.

## Stream B - Ship Physics
- Зона: hull rigidbody, buoyancy, damping/stability, deck motion provider.
- Контракт наружу: `IShipPhysics`.
- Не лезет в player input/network UI.

## Stream C - Sailing Mechanics
- Зона: sail/wind/rudder/rope behavior.
- Контракты: `IShipPhysics`, `IShipControlInput`, `IRopeRuntime`.
- Не меняет net authority политику.

## Stream D - Damage & Flooding
- Зона: hull breaches, ingress, flooding simulation.
- Контракты: `IDamageSystem`, `IFloodingSystem`.
- Не меняет физическое ядро корабля, только использует его данные.

## Stream E - Player Physical Gameplay
- Зона: move/climb/ragdoll/overboard/rescue loop.
- Контракты: `IShipPhysics`, `IRopeRuntime`, `IPlayerState`.
- Не модифицирует ship hydro модели.

## Stream F - Director & Content
- Зона: weather pacing, threats, encounter orchestration.
- Контракты: event bus + read-only snapshots.
- Не владеет low-level physics/network.

## Stream G - UI/Audio/Polish
- Зона: HUD, feedback, telemetry, UX.
- Контракты: read-only snapshots + events.
- Не владеет sim state mutation.

## 6. Зависимости и что блокирует что

- **A -> все**: без net/runtime contracts остальные потоки рискуют делать несовместимую логику.
- **B -> C/E/D**: sail, player-on-deck, flooding зависят от ship motion.
- **C + B -> D**: damage/flooding должны учитывать нагрузку от sailing dynamics.
- **E + D -> F/G**: director и UI зависят от зрелых gameplay-состояний.

## 7. Пошаговый план (milestones)

## M0 (1 неделя): Contracts First
- Зафиксировать интерфейсы, DTO, события, code owners.
- Создать тестовые заглушки (mock ship, mock flooding).

## M1 (2 недели): Runtime Foundation
- Stream A и B стартуют параллельно.
- Выход: сетевой корабль стабильно живет в сессии, deck motion валиден.

## M2 (2 недели): Sailing + Player Base
- Stream C и E параллельно поверх контрактов M1.
- Выход: базовая навигация + игроки на палубе/за бортом.

## M3 (2 недели): Damage/Flooding
- Stream D поверх M1/M2.
- Выход: breach -> flooding -> влияние на ship behavior.

## M4 (2 недели): Rescue Loop
- Stream E + D интеграция.
- Выход: полный цикл overboard -> rescue/fail.

## M5 (2 недели): Director Slice
- Stream F собирает первый региональный gameplay slice.

## M6 (2+ недели): Presentation/Polish
- Stream G и стабилизация интеграции всех потоков.

## 8. Правила, чтобы не пересекаться по коду

- Каждый stream имеет свой набор папок и владельцев (CODEOWNERS).
- Cross-stream PR только через:
  - изменение контрактов,
  - интеграционный glue-код,
  - фикс совместимости.
- Запрет прямых ссылок на внутренние классы другого stream; только интерфейсы.
- Любая правка контракта -> RFC + changelog + миграционный PR.

## 9. Тестовая стратегия для независимой работы

- Unit: на каждый stream отдельно (логика изолирована).
- Contract tests: проверка, что реализации соответствуют интерфейсам.
- Simulation tests: headless интеграционные тесты по milestones.
- Network replay tests: для A/B/C/E (authority + prediction consistency).

## 10. Основные риски пересечений

- Rope/Player sync на движущемся корабле (C x E x A).
- Flooding влияет на buoyancy и heel (D x B).
- Ragdoll + loose objects может взрывать физику (E x B).
- Director может ломать причинность физики (F x B/C/D).

## 11. Что можно делать полностью независимо уже сейчас

- Stream G (HUD scaffolding на mock snapshots).
- Stream F (правила директора на mock events).
- Stream D (чистая модель flooding/damage без live physics, через mock `IShipPhysics`).

## 12. Рекомендуемая структура поставки

- Каждая команда поставляет:
  - `Runtime` код модуля,
  - `Tests` (unit + contract),
  - `Debug Views` (гизмо/оверлеи),
  - `README` модуля (входы/выходы/ограничения).

Это позволит вести разработку параллельно с минимальными блокировками и понятными зонами ответственности.

