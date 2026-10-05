# RumOverboard — Setup: Multiplayer (Photon Fusion 2) + Ragdoll

App Id (Fusion): `0d31993b-5624-4d9f-a496-73f5d0332a22` — уже прописан в коде
(`GameConfig.DefaultAppId`, применяется в рантайме через `ConnectionManager.ApplyAppId()`).

## Статус

| Компонент | Состояние |
|---|---|
| UniTask | ✅ поставлен (git-пакет) |
| R3 (реактивный UI) | ✅ работает — core-сборки вендорены в `Assets/Plugins/R3/` |
| NuGetForUnity | ✅ поставлен (меню **NuGet** доступно для будущих пакетов) |
| Photon Fusion 2 (core) | ✅ импортирован (`Assets/Photon/Fusion`) — `FUSION2` определён, сетевой код активен |
| Fusion Physics addon | ⚠️ опционально (см. §2) |
| Скрипты игры | ✅ в `Assets/Source/Scripts/` |

## Почему R3 вендорен, а не через NuGet

UPM-пакет `com.cysharp.r3` (Unity-интеграция) содержит только .cs и требует core-сборку `R3`
из NuGet. NuGet-restore не успевал отработать → R3.Unity не компилировался → ошибки компиляции
скрывали меню NuGet (дедлок). Решение: 4 нужные DLL сложены вручную в `Assets/Plugins/R3/`
(`R3.dll` + `Microsoft.Bcl.TimeProvider` + `Microsoft.Bcl.AsyncInterfaces` + `System.Threading.Channels`;
взяты netstandard2.1/2.0 сборки, из которых убрано то, что Unity 6 уже поставляет, — иначе
конфликт дублирующихся ассембли). R3 убран из `Assets/packages.config`, чтобы NuGetForUnity
не восстановил вторую копию. Меню NuGet снова доступно для остальных пакетов.

---

## 1. Слои (Layers)

Project Settings ▸ Tags and Layers — добавь:
- `Climbable` — на мачту/такелаж (вешаем `Climbable` + trigger-коллайдер).
- (опционально) `Ground` — для проверки «на земле» при прыжке.

На **префабе игрока** (`NetworkPlayer`): `Climb Mask` → `Climbable`; `Ground Mask` → земля/палуба.

---

## 2. Плавная репликация физики: чистый host authority (без предикции)

> Актуальная модель — см. `Assets/Source/Docs/ShipSandbox.md`. Ранее здесь описывалась
> клиентская предикция локального игрока; она **убрана**: предсказанный игрок стоял на
> интерполированном (отстающем) корабле — это и было дрожание.

- Хост симулирует ВСЁ (корабль, игроков, станции) в `FixedUpdateNetwork`; PhysX шагает аддон
  (`RunnerSimulatePhysics3D`, `ClientPhysicsSimulation = Disabled`).
- Клиенты шлют только ввод (`NetworkInputData`: оси, взгляд относительно корабля, кнопки, цель
  взаимодействия). Все сетевые тела у клиента — кинематические прокси, интерполируемые
  `NetworkRigidbody3D` в ОДНОМ таймфрейме → игрок всегда согласован с палубой под ним.
- `NetworkPlayer.Spawned` явно выводит локального игрока из клиентской симуляции
  (`Runner.SetIsSimulated(Object, false)`) — Fusion по умолчанию включает туда объект с input authority.
- Мышь применяется к камере локально и мгновенно; движение подтверждает хост (задержка = RTT).
- Косметическая физика (регдолл) у клиента шагается в `ConnectionManager.Update` только пока регдолл активен.

## 3. Регдол на модели PolyOne

1. Перетащи `Assets/PolyOne/Free Stickman/Prefabs/Free Pack - Stick Man.prefab` в сцену.
2. Рига = **Humanoid** (у модели уже так).
3. Выдели корень → меню **RumOverboard ▸ Ragdoll ▸ Build On Selected**.
   Создаст 11 тел (Rigidbody + коллайдеры + CharacterJoint) и добавит `RagdollController`.
4. На `RagdollController` → `Root Body` = главный Rigidbody персонажа (капсула из §4). Он исключается из регдола.
5. Откат: **RumOverboard ▸ Ragdoll ▸ Clear From Selected**.

Регдол локальный, не реплицируется — реплицируется только флаг `IsRagdoll` (это «угар», не механика).

---

## 4. Префаб сетевого игрока

1. Пустой GameObject `Player`:
   - `NetworkObject` (Fusion).
   - `Rigidbody` (главный, `Root Body` для регдола) + `CapsuleCollider`.
   - **`NetworkRigidbody3D`** (аддон Fusion Physics — даёт клиентскую предикцию; `NetworkPlayer`
     включит предикцию сам, увидев этот компонент). `NetworkTransform` только если предикция не нужна.
   - `NetworkPlayer` — назначь `Config` (GameConfig), маски слоёв, `Camera Anchor` (голова).
   - **`JumpColliderDriver`** (на том же объекте, что и `CapsuleCollider`) — капсула поджимается
     за ногами в прыжке. `NetworkPlayer` находит его сам; `Tuck Shrink` = насколько укорачивается.
2. Внутрь — модель Stickman с регдолом из §3.
   - На модель (где `Animator`) повесь **`ProceduralClimbRig`** — IK-хваты рук/ног для мачты,
     лестницы и подъёма из воды за борт. Укажи `Climb Mask` (тот же слой `Climbable`).
   - В **Animator Controller** включи на слое галку **IK Pass** — иначе `OnAnimatorIK` не вызовется
     и процедурные хваты работать не будут. Рига должна быть **Humanoid**.
3. Fusion сам подхватывает `NetworkObject`-префабы (`Tools ▸ Fusion ▸ Rebuild Object Table` при необходимости).

---

## 5. Сцены и запуск (Boot → Menu → Gameplay через Fusion Menu)

Флоу построен на **Photon Fusion Menu** (bridge): меню само владеет `NetworkRunner`, а
геймплейную сцену грузит аддитивно из списка `FusionMenuConfig.AvailableScenes`.

Три сцены (уже в Build Settings в этом порядке):

| # | Сцена | Роль |
|---|---|---|
| 0 | `Assets/Scenes/Boot.unity` | `GameBootstrap` грузит меню (`LoadScene(FusionSampleMenu)`). Единая точка входа для one-time init. |
| 1 | `Assets/Photon/FusionMenu/FusionSampleMenu.unity` | Меню Fusion: **Create** (host / shared), **Join** по party-code, **Quick Join**. Владеет раннером. |
| 2 | `Assets/Scenes/Gameplay.unity` | Deck-плейсхолдер + `GameManager` (`ConnectionManager`) + камера + HUD. Грузится меню аддитивно. |

**Как это связано:**
- `ConnectionManager` больше **не** создаёт свой раннер в menu-флоу. При `Auto Start On Play = off`
  он в `Start()` находит уже запущенный раннер (тот, что создало меню), подписывается на колбэки и
  спавнит экипаж (в т.ч. хоста, чей `OnPlayerJoined` прошёл до загрузки сцены). Галка
  `Auto Start On Play = on` возвращает старый standalone-режим (свой раннер) для быстрых прогонов
  прямо из `Gameplay.unity`.
- `PlayerCameraRig` на Main Camera: **первое лицо** (как в PEAK), а при нокауте (полный регдол,
  `RagdollControl ≈ 1`) — переходит в орбитальный **третий вид**. Локального игрока ему сообщает
  `NetworkPlayer.Spawned` (у кого `InputAuthority`).
- `GameplayHud` — лёгкий оверлей (сессия / экипаж / пинг + Leave); если поля UI пустые, строит
  канвас в рантайме. Плюс у меню есть свой gameplay-overlay.

### Что ещё нужно сделать руками в редакторе (Fusion требует Editor-запекания)

1. **Импортировать TMP Essentials**, если Unity попросит при первом открытии меню
   (Window ▸ TextMeshPro ▸ Import TMP Essential Resources).
2. **Собрать префаб игрока** по §3–§4 (NetworkObject + Rigidbody + NetworkTransform + `NetworkPlayer`
   + модель Stickman + регдол). Выставить на `NetworkPlayer` поле **Camera Anchor** — точку головы
   для камеры от первого лица.
3. На `GameManager` в `Gameplay.unity` назначить **Player Prefab** = этот префаб (поле `playerPrefab`
   оставлено пустым — его нельзя проставить без Editor). При желании — `Config` (GameConfig-ассет).
4. **Tools ▸ Fusion ▸ Rebuild Object Table**, чтобы Fusion увидел префаб игрока.
5. (Опц.) Заменить материал плейсхолдера `Deck` на нормальный URP-материал — сейчас стоит
   встроенный default (может выглядеть маджентой под URP). Коллайдер для проверки «земли» уже есть.

Тест: Window ▸ Multiplayer ▸ **Multiplayer Play Mode**, либо два билда. Оба стартуют с `Boot`.

### Программные точки входа `ConnectionManager` (для standalone-режима)
   - `StartHost()` — стать сервером-хостом (модель PEAK).
   - `StartClient()` — подключиться к хосту.
   - `StartAuto()` — первый вошедший = хост (drop-in).
   - `StartShared()` — P2P-режим Shared.

---

## 6. Рефактор нетворкинга: host migration, lag compensation, гибридный корабль

Модель осталась **host-authoritative** (хост = сервер). Ниже — что уже сделано в коде/конфиге и
что осталось сделать руками в редакторе (Fusion требует запекания `NetworkObject`).

### 6.1 Гибридный `NetworkShip` (транспорт → `NetworkRigidbody3D`)

Раньше `NetworkShip` **сам** гнал по сети позицию/поворот/скорости корабля (`ShipNetState`) и
двигал прокси в `Render()`. Это дублировало то, что делает аддон Physics, и **конфликтовало** с ним
(аддон сам держит прокси kinematic и интерполирует их в своём `Render()`).

Теперь `NetworkShip` реплицирует только производные значения, которые прокси не могут вычислить из
трансформа: `LastImpactStrength` (событие удара волны) и `OceanTime` (часы океана). Всё геометрическое
(pos/rot/velocity/kinematic/constraints) отдано `NetworkRigidbody3D`.

**Сделать в редакторе (обязательно, иначе корабль не будет реплицироваться — `NetworkShip` кинет
`LogError` в `Spawned`):**
1. Открыть префаб `Assets/Source/Prefabs/NetworkShip.prefab`.
2. Добавить на корень компонент **`NetworkRigidbody3D`** (Fusion Physics addon). `NetworkTransform`
   на корабле НЕ нужен (и не должен стоять одновременно).
3. Убедиться, что на корне есть `Rigidbody` (не kinematic на исходнике — режимом рулит аддон).
4. **Tools ▸ Fusion ▸ Rebuild Object Table**.

> Roll/pitch больше не в сети — `ShipDeckMotionProvider` берёт их из локального `ShipBuoyancyController`,
> который читает синхронизированный трансформ, поэтому на прокси они корректны и так.

⚠️ **Тайминг физики буйанси (на заметку, не блокер):** `ShipBuoyancyController` прикладывает силы в
Unity-`FixedUpdate`, а с аддоном Physics авторитетная симуляция шагается Fusion'ом (`Physics.Simulate`
внутри тика). Для 100% детерминизма форс-логику стоит перенести в `FixedUpdateNetwork` авторитета
(вызывать из `NetworkShip`), но это отдельная задача — текущий вариант работает, пока Unity fixed-rate
совпадает с тик-рейтом.

### 6.2 Дедуп часов океана

`NetworkOceanState` дублировал `OceanTime`/`ApplyTimeCorrection` с `NetworkShip` (и в сцене сейчас не
используется). Теперь он **уступает** кораблю: если в сцене есть `NetworkShip`, `NetworkOceanState`
перестаёт писать/корректировать `OceanTime` (иначе две системы дёргали бы коррекцию времени
одновременно). Seed/sea-state он реплицирует всегда. Для сцен без корабля он остаётся авторитетом часов.

### 6.3 Lag compensation

Включено в `NetworkProjectConfig.fusion` (`LagCompensation.Enabled = true`, буфер 200 мс).

**Сделать в редакторе (нужно для попаданий по игрокам — волны/летучая рыба/падающая мачта):**
1. На префабе `NetworkPlayer.prefab` добавить **`HitboxRoot`** на корень.
2. Добавить один-несколько **`Hitbox`** (капсула на тело; можно на кости) со ссылкой на этот `Root`.
3. **Rebuild Object Table**.

**Использование из кода (когда появятся хазарды):** на авторитете в `FixedUpdateNetwork`:
```csharp
if (Runner.LagCompensation.Raycast(origin, dir, length, Object.InputAuthority,
        out LagCompensatedHit hit, layerMask, HitOptions.IncludePhysX))
{
    if (hit.Hitbox != null && hit.Hitbox.Root.GetComponent<NetworkPlayer>() is { } p)
        p.Knockout();
}
```
> Сейчас потребителей рейкастов нет (`Knockout()` никто не вызывает), так что lag comp — это
> инфраструктура на будущее: конфиг включён, но без хитбоксов и хазард-кода эффекта ещё нет.

### 6.4 Host migration

Включено (`HostMigration.EnableAutoUpdate = true`). `ConnectionManager.OnHostMigration` теперь:
гасит мёртвый раннер (`Shutdown(destroyGameObject:false)` — раннер живёт на GameObject менеджера),
поднимает новый раннер с `HostMigrationToken` + `HostMigrationResume`, и на новом хосте пересоздаёт
каждый объект из снапшота через `Spawn(..., onBeforeSpawned: o => o.CopyStateFrom(resumeObject))`,
восстанавливая карты `_players`/`_ship`. `OnShutdown` не трогает состояние при причине `HostMigration`.

**Тест:** Multiplayer Play Mode на 3 пира → убить пир-хост → у выжившего должен подняться новый раннер
(лог `Host migration complete`), корабль и игроки — на месте с их состоянием.

⚠️ **Требует прогонки в MPPM** — точная семантика мульти-пирного resume в Fusion 2 проверяется только
в рантайме; я следовал каноничному паттерну Photon, но это единственный кусок, который стоит
протестировать первым. Мелочь: после миграции на GameObject менеджера остаются инертные компоненты
старого раннера (новый — на отдельном GO); безвредно, но можно почистить.

---

## Troubleshooting

- **`InvalidOperationException: You are trying to read Input using the UnityEngine.Input class`**
  — проект на New Input System (`activeInputHandler = 1`), а какой-то `EventSystem` создан с
  легаси-модулем `StandaloneInputModule`. Уже исправлено в двух местах:
  `FusionSampleMenu.unity` (EventSystem → `InputSystemUIInputModule`) и
  `Assets/Photon/Fusion/Runtime/Statistics/FusionStatistics.cs` (стат-панель добавляет
  `InputSystemUIInputModule` через рефлексию под `#if ENABLE_INPUT_SYSTEM`). **Правка в
  FusionStatistics.cs — в core-файле Fusion; при переимпорте Fusion её нужно вернуть**
  (или отключить панель статистики).

- **`Fusion.Addons.Physics` / `NetworkRigidbody3D` не найдены**: аддон Physics не импортирован —
  либо не используй его (`NetworkTransform`, §2), либо импортируй аддон и вешай `NetworkRigidbody3D`.
- **Дублирующиеся ассембли после ручного добавления NuGet-пакетов**: не добавляй DLL, которые
  Unity уже поставляет (Unsafe/Memory/Buffers и т.п.) — см. `Assembly-CSharp.csproj` как источник правды.
- **DOTween**: ставится отдельно (Asset Store, бесплатный) → Import → `Tools ▸ Demigiant ▸ DOTween
  Utility Panel ▸ Setup`. В коде пока не используется.

---

## Что дальше

- Камера от третьего лица + прокидывание её yaw в `InputReader.CameraYaw`.
- Триггер падения за борт → авто-`IsRagdoll` + механика спасения.
- Накопление опьянения от рома (`NetworkPlayer.Drunkenness`) + визуал.
- Реактивный UI на R3 (MVVM): здоровье экипажа, ветер, роли.
- Судно: сетевой корпус — сделано (гибрид `NetworkRigidbody3D`, см. §6.1). Осталось: парус/руль/вёсла
  как сетевые интеракции; при желании — перенос сил буйанси в `FixedUpdateNetwork` (см. §6.1).
- Хазарды с попаданиями по игрокам через lag-compensated рейкасты (§6.3): волна, летучая рыба, мачта.
