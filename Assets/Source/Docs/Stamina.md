# Stamina — стамина экипажа (GDD §13.4)

Первая механика игрока на системе атрибутов ([Attributes.md](Attributes.md)). Host authority, клиенты
стамину не считают — видят реплицированный ресурс `Stamina` и флаг `Exhausted`.

## Правила

| Что | Как |
|---|---|
| Тратят | рывок на палубе (`StaminaSprintCost`/с), прыжок и рывок из воды (`StaminaJumpCost` разово), движение по рейке (`StaminaClimbCost`/с, ×2 рывком), плавание (`StaminaSwimCost`/с при полном гребке, 30% — держаться на воде), выбирание каната или ходьба с нагруженным концом (`StaminaHaulCost`/с × нагрузка 0.25…2), движение с предметом в руках (`StaminaCarryCost`/с × масса/`CarryCapacity`, см. Items.md) |
| Бесплатно | ходьба, стояние, штурвал, лазание без движения, отпустить рейку прыжком |
| Восстановление | автоматически, через `StaminaRegenDelay` с без трат, со скоростью `StaminaRegen`/с |
| Истощение | на нуле ставится флаг `Exhausted` до набора `StaminaRecoverThreshold`: нет рывка и прыжка, лазание и плавание × `StaminaExhaustedSpeed` |
| Холод (`ColdFx`) | модификатор `StaminaRegen −6` (вдвое медленнее восстановление), GDD §20.1 «increased stamina pressure» |

Все числа — **атрибуты профиля `Player`** (`Configs/Attributes/Profiles/Player.asset`), host-only (не
реплицируются). Их можно крутить в рантайме (F7 / F6), менять эффектами (мокрый, пьяный, холод…) и
сохранять в профиль. Дефолты: регенерация 12/с, задержка 1 с, порог 30, рывок 10/с, прыжок 8, лазание 6/с,
плавание 5/с, канат 8/с, скорость в истощении ×0.5, максимум 100.

## Как устроено

```
NetworkPlayer.FixedUpdateNetwork (хост)
  FillContext  → CrewStamina.GatesFor(set) → ctx.CanSprint / CanJump / StaminaSpeedFactor
  state machine → состояния уважают гейты и сообщают ctx.Sprinting / ctx.Jumped
  HandleRigging
  StepStamina  → StaminaActivity (что реально было за тик) → CrewStamina.Step: трата / реген / Exhausted
NetworkAttributes → Stamina (value+current) + флаги → клиенты → HUD (полоса «СИЛЫ», «ВЫДОХСЯ»)
```

- `Gameplay/Crew/Stamina/CrewStamina.cs` — модель (чистый C#, без аллокаций): `StaminaActivity`,
  `StaminaGates`, `GatesFor`, `DrainPerSecond`, `Step`; отладочные счётчики `Jumps`, `SprintSeconds`,
  `TotalSpent`, `LastSpent`, `SinceSpend`. Без атрибута `Stamina` всё открыто и ничего не тратится.
- `StateMachine/StateDefinitions.cs` (`StateContext`) — входы `CanSprint`, `CanJump`, `StaminaSpeedFactor`,
  выходы `Sprinting`, `Jumped`.
- `Core/Character/States/LocomotionStates.cs` — Grounded/InAir: рывок и прыжок по гейтам; Climbing:
  скорость × фактор, рывок по гейту; Swimming: скорость × фактор, рывок из воды по гейту.
- `Networking/NetworkPlayer.cs` — гейты в `FillContext`, `StepStamina` после канатов; публично:
  `Attributes`, `Stamina01`, `IsExhausted`, `Stamina` (модель), `LastStaminaActivity`.
- `Gameplay/UI/HintOverlay.cs` — `SetStamina(value, exhausted, visible)`, полоса внизу по центру;
  `Gameplay/PlayerCameraRig.cs` кормит её для отслеживаемого игрока.
- `NetworkPlayer.prefab` — `AttributesComponent` (профиль Player) + `NetworkAttributes`.

## Сцена, debug, тесты

- **RumOverboard ▸ Stamina ▸ ★ Build Stamina Scene** → `Assets/Scenes/StaminaScene.unity`: песочница
  корабля (бриг, штиль: палуба, мачты, канаты, море) + окно **F7** открыто, F6 (атрибуты) скрыто.
  Также: `Add Stamina to Player Prefab` (то же делает Ship Sandbox ▸ Prepare Player Prefab).
- **F7 `StaminaDebugWindow`**: по каждому игроку полоса, Exhausted, на что сейчас тратится, реген
  (с модификаторами) и эффекты; на хосте — слайдеры всех параметров для всего экипажа, Fill/Empty/Half,
  Reset to profile, Save to profile.
- **Stamina ▸ Run Self-Test** (`StaminaSelfTest`, batch `…Stamina.StaminaSelfTest.RunBatch`) — модель:
  траты по видам, задержка и скорость регена, истощение с гистерезисом, гейт прыжка, холод, 0 аллокаций.
- **Stamina ▸ Run Smoke (Play Mode)** (`StaminaSmoke`) — настоящий игрок на палубе, ввод через
  `InputOverride`: расход рывка = 10/с × фактическое время рывка, нет регена во время задержки, реген после,
  истощение → скорость ходьбы, прыжок заблокирован, восстановление на пороге, прыжок стоит 8.
- Сеть: **Attributes ▸ Run Network Smoke** теперь спавнит настоящих игроков в AttributesScene и проверяет на
  клиенте стамину и `Exhausted` хостового игрока (истощение → восстановление).
- Регрессия корабля: **Ship Sandbox ▸ Run Smoke Test (in editor)** — прежний сценарий песочницы в открытом
  редакторе (раньше только batch с выходом из редактора). С стаминой — ALL PASSED.

## Наблюдения / дальше

- При беге рывком туда-обратно по палубе брига ~35–45% времени игрок «в воздухе» из-за подскоков
  `StepAhead` на ступеньках/комингсах (это поведение локомоции, не стамины; в воздухе стамина не тратится).
- Анимации усталости нет (флаг `Exhausted` реплицируется — можно повесить отдышку/звук).
- Падения с высоты, удары и т.п. стамину не трогают; ragdoll и стамина не связаны.
- `Drunkenness` перенесён в атрибуты — см. [Intoxication.md](Intoxication.md).
