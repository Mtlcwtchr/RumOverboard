# Fire — огонь, порох, тушение

Объём первой версии: горят **предметы** (бочки, доски, ящики, сундуки), **порох** взрывается, **фонарь**
при разрушении поджигает всё вокруг, тушат **ведром воды**. Паруса, канаты и корпус пока не горят.
Host authority: всё решает хост (`FireDirector`), клиенты только показывают реплицированные флаги (`FireVisuals`).

## Управление

| Клавиша | Действие |
|---|---|
| `E` на ведро | взять (обычный предмет, 4 кг) |
| `Q` с ведром | выплеснуть воду на 1.6 м вперёд, радиус 1.6 м: всё, что там горит, гаснет; всё попавшее становится `Wet` |
| `E` на бочку с водой (с ведром) | набрать воды до полного (из запаса бочки) |
| ведро в море | наполняется само (погружено ≥ 30%) |

Ведро держит `Water` = 3, один всплеск тратит 1 (`FireConfig.WaterPerSplash`).

## Данные

| Что | Где | Смысл |
|---|---|---|
| `OnFireFx` | эффект, бесконечный, Ignore, даёт флаг `Burning` | «горит, пока не потушат». Урон — не periodic, а `FireConfig.BurnDamagePerSecond` (тюнится в F9) |
| `Burning` / `Wet` / `Flammable` | флаги (были) | `Wet` (эффект `WetFx`, 10 с) — не нагревается и не загорается |
| `Exploded` | флаг, новый | порох взорвался — все пиры показывают взрыв вместо обычных обломков |
| `Water` | Resource, реплицируется | вода в ведре / в бочке. Убыль на клиенте = анимация всплеска |
| `FlammableData` | Barrel, PowderBarrel (наследует), Board, Crate, Chest | `IgnitionTime` (сколько греть до возгорания), `SpreadRadius` (докуда греет сам), `FireVfx` (опционально своё пламя) |
| `ExplosiveData` | PowderBarrel | фитиль 2.5 с, радиус 4 м, урон 120, поджог ближе 0.6 R, толчок предметов 9 м/с, нокаут ближе 2.5 м, толчок экипажа 7 м/с |
| `LanternData` | Lantern | при разрушении поджигает горючее в радиусе 1.8 м |
| `BucketData` | Bucket | дальность и радиус всплеска |
| `WaterSourceData` | WaterCask (`Water` 40) | источник воды; `Infinite` — без расхода |

Профили `Bucket`, `Lantern`, `WaterCask` и всё выше создаёт **RumOverboard ▸ Fire ▸ Create Fire Content**
(идемпотентно, тюнинг не перезаписывает) + кодоген `Attr.Water`, `AttrFlag.Exploded`, `AttrEffect.OnFireFx`.

## Правила (хост, каждый тик — `FireDirector`)

```
горящий предмет: Integrity −= BurnDamagePerSecond·dt → на 0 ломается (сгорел)
нагрев соседа:   rate = Σ по горящим  (1 − зазор/SpreadRadius источника) × SpreadMultiplier / IgnitionTime цели
                 heat += rate·dt (без источников остывает HeatDecayPerSecond); heat ≥ 1 → загорелся
                 зазор = расстояние между bounds (вплотную = 0); Wet / в хранилище / сломан — не греется
вода:            Wet или погружён ≥ SubmergedToQuench → потух (+Wet); ведро в воде → полное
порох:           горит → фитиль FuseTime → взрыв: урон × (1 − d/R), поджог (порох в радиусе — всегда: цепная
                 реакция), толчок предметов, экипаж: ближе KnockoutRadius — нокаут, дальше — толчок + частичный ragdoll;
                 пороховая бочка, сломавшаяся пока горит, взрывается сразу
фонарь:          сломался → поджог горючего в LanternData.IgniteRadius
```

Формулы — в `FireRules` (чистые функции, покрыты self-test).

## Файлы (`Assets/Source/Scripts/Gameplay/Fire`)

- `FireData.cs` — блоки данных профилей: `ExplosiveData`, `LanternData`, `BucketData`, `WaterSourceData`.
- `FireRules.cs` — нагрев, остывание, затухание взрыва, точка/зона всплеска.
- `FireConfig.cs` — ScriptableObject глобального тюнинга (`Assets/Source/Configs/Fire/FireConfig.asset`).
- `FireDirector.cs` — `SimulationBehaviour`, сам регистрируется на хостовом раннере (`AddGlobal`) и работает в его
  тике. API хоста: `Ignite`, `Extinguish(item, soak)`, `IgniteAround`, `Explode`, статические `Splash(bucket, origin,
  forward)` и `Fill(bucket, source)`; счётчики и `HeatOf` / `FuseLeft` для отладки. Конфиг — рантайм-копия ассета.
- `FireVisuals.cs` — на всех пирах, только по реплицированному состоянию (без RPC): `Burning` → процедурное пламя,
  дым и мерцающий свет (не больше 8 источников света); `Burning` пропал у целого предмета → пар; `Broken` +
  `Exploded` → огненный шар, дым, вспышка; `Water` ведра уменьшилась → всплеск. Счётчики для тестов.
- `FadeLight.cs` — гаснущий одноразовый свет вспышки.
- `WaterSourceInteractable.cs` — `E` на бочку с водой (`InteractionKind.WaterSource`); доступно только с неполным ведром.
- `Debug/FireDebugWindow.cs` — **F9**.

Изменения вне фичи: `NetworkPlayer.HandleItems` — `Q` с ведром → `FireDirector.Splash`, `E` на источник →
`FireDirector.Fill`; `NetworkPlayer.Shove(Δv, ragdoll)` — толчок взрывом; `InteractionKind.WaterSource = 8`;
подсказка `Q` в `ControlHints`; `ItemsSetup.ItemSpec.Decorate` + `BuildPrefabsAndCatalog(specs, …)` + `AddSpawner`.

## Сцена, отладка, тесты

- **RumOverboard ▸ Fire ▸ ★ Build Fire Scene** → `Assets/Scenes/FireScene.unity`: бриг в штиль; на носу куча
  (ящик, доска, бочка) и фонарь, у точек спавна два ведра; на корме сундук, закреплённая бочка
  с водой и пороховая бочка (≥ 4 м от спавнов), ведро в воде. Объект `Fire`: `FireDirector` + `FireVisuals` + `FireDebugWindow`; F8 и F6 тоже есть.
  *Rebuild Fire Prefabs (overwrite)* — перезаписать префабы Bucket / Lantern / WaterCask.
- **F9** — список горючих/огненных предметов (состояние, hp, вода, нагрев, фитиль) с кнопками Ignite / Out /
  Boom / Break; Ignite all, Put out all, Refill water; тюнинг `FireConfig` вживую, *Reset to config*, *Save to config*.
- **Fire ▸ Run Self-Test** — правила, данные профилей, префабы и каталог.
- **Fire ▸ Run Smoke (Play Mode)** — настоящий игрок на замороженном бриге: огонь переходит на ящик в 0.3 м и не
  трогает дальний → `Q` ведром тушит оба (Wet, −1 вода, всплеск и пар показаны) → мокрый не загорается → `E` на
  бочку наполняет ведро → ведро в море наполняется, горящая доска в море гаснет → разбитый фонарь поджигает ящик →
  ящик сгорает → порох взрывается после фитиля: ящик повреждён, подожжён и отброшен, игрок в нокауте.
- **Attributes ▸ Run Network Smoke** — на клиенте: ящик горит (флаг + пламя), после тушения на хосте — мокрый и пар,
  взрыв пороха показан.

## Дальше

- Горение парусов, канатов, палубы/корпуса (через атрибуты частей корабля), дым, мешающий обзору.
- Ожоги экипажа (горящий предмет в руках, стоять в огне), «сбить пламя» без ведра.
- Звук огня/взрыва (поля под клипы в данных), арт-VFX вместо процедурного (`FlammableData.FireVfx` уже работает).
- Интеграция в gameplay-корабль через агрегатор — после стабилизации в FireScene.
