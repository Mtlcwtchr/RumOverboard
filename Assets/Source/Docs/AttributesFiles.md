# Attributes — справочник по файлам

Пофайловое описание системы атрибутов: что лежит в каждом файле, как оно работает и с чем связано.
Обзор и API «как пользоваться» — в [Attributes.md](Attributes.md); здесь — устройство.

## Карта

```
Assets/Source/Scripts/Gameplay/Attributes/          ← runtime (Assembly-CSharp)
├─ Runtime/
│  ├─ AttributeIds.cs        AttributeId, FlagId, EffectId, ModifierSource
│  ├─ AttributeLayout.cs     раскладка слотов профиля (id → слот), хэш
│  └─ AttributeSet.cs        значения одной сущности: модификаторы, флаги, эффекты
├─ Config/
│  ├─ AttributeDefinition.cs ассет «атрибут» (Stat / Resource)
│  ├─ FlagDefinition.cs      ассет «флаг»
│  ├─ EffectDefinition.cs    ассет «эффект»
│  ├─ EntityData.cs          IEntityData + примеры блоков статических данных
│  ├─ AttributeProfile.cs    ассет «тип сущности» (атрибуты + флаги + данные, наследование)
│  └─ AttributeRegistry.cs   ассет-реестр всех определений по id (Resources)
├─ AttributesComponent.cs    компонент на GameObject: профиль → AttributeSet, SimWorld
├─ EffectZone.cs             триггер-зона, держащая эффект
├─ Networking/
│  └─ NetworkAttributes.cs   репликация через Fusion (host authority)
├─ Debug/
│  ├─ AttributesDebugWindow.cs  окно F6
│  └─ AttributesNetSmoke.cs     сценарий сетевого теста (в сцене)
└─ Generated/
   └─ Attr.g.cs              константы Attr / AttrFlag / AttrEffect (кодоген)

Assets/Source/Scripts/Editor/Attributes/            ← редактор (Assembly-CSharp-Editor)
├─ AttributeRegistryBuilder.cs  выдача id, реестр, кодоген, автопересборка
├─ AttributesSelfTest.cs        тест ядра (меню + batch)
├─ AttributesSandboxSetup.cs    ★ Build Sandbox: примеры ассетов + сцена
├─ AttributesSandboxSmoke.cs    smoke в Play Mode (один редактор)
└─ AttributesNetworkSmoke.cs    smoke хост + клиент через MPPM

Assets/Source/Configs/Attributes/                   ← данные (примеры из ★ Build Sandbox)
├─ Definitions/  Stamina, Grog, Mass, Integrity
├─ Flags/        Flammable, Floating, CriticalItem, Burning, Wet
├─ Effects/      WetFx, BurningFx, ColdFx
├─ Profiles/     Player, Barrel, PowderBarrel, Board, Crate
└─ Resources/AttributeRegistry.asset

Assets/Scenes/AttributesScene.unity                 ← feature-сцена
```

## Поток данных

```
 ассеты (Definition / Flag / Effect)            RumOverboard ▸ Attributes ▸ Rebuild Registry
        │  id назначает AttributeRegistryBuilder ──────────► AttributeRegistry.asset + Attr.g.cs
        ▼
 AttributeProfile ──(Layout, один раз на профиль)──► AttributeLayout  (id → слот, базы, хэш)
        │                                                   │
        ▼                                                   ▼
 AttributesComponent (на объекте) ──── new AttributeSet(layout) + overrides
        │                                   ▲     │
        │ хост/офлайн: Tick, ApplyEffect,   │     │ Version / ChangedSlots
        │ модификаторы, EffectZone          │     ▼
        ▼                                   │  UI, системы (опрос раз в тик/кадр)
 NetworkAttributes ── хост: Write() ─► [Networked] Values / Flags / Effects ─► клиент: Read() в Render()
```

---

## Runtime

### `Runtime/AttributeIds.cs`
Типы-идентификаторы. Это `readonly struct` с `==`, `Equals` и `GetHashCode`, чтобы не было боксинга и
путаницы между видами id.

| Тип | Хранение | Смысл |
|---|---|---|
| `AttributeId` | `ushort Value`, 0 = нет | id атрибута (stat/resource) |
| `FlagId` | `byte Value` 1..64, 0 = нет | бит флага; `Mask` = `1UL << (Value-1)`; `MaxFlags = 64` |
| `EffectId` | `ushort Value`, 0 = нет | id эффекта |
| `ModifierSource` | `int Value`, 0 = нет | «владелец» модификаторов и выданных флагов; `New()` — счётчик процесса |

`ModifierSource` существует только на хосте и не реплицируется, поэтому глобального счётчика хватает.
Эффект при наложении получает свой `ModifierSource`, и `RemoveSource` снимает всё, что он дал.

### `Runtime/AttributeLayout.cs`
Неизменяемая раскладка одного `AttributeProfile`, общая для всех экземпляров этого профиля.

- **Build(profile).** Собирает цепочку родителей от корня к ребёнку. Записи ребёнка перекрывают базу
  того же атрибута на его месте, так что порядок слотов стабилен. Флаги объединяются по OR.
  Атрибут без id даёт ошибку «run Rebuild Registry» и пропускается. Больше `MaxSlots = 64` — ошибка,
  лишнее отбрасывается: маски изменений хранятся в одном `ulong`.
- **Поиск слота за O(1).** `short[] _slotById`, индекс — `AttributeId.Value`, `-1` значит «нет».
  Массив размером с максимальный id в профиле.
- **Поля:**
  - `Definitions[]`, `BaseValues[]` (уже обрезаны по Min/Max), `BaseFlags`;
  - `ReplicatedSlots[]` — слоты с `Replicated`, в порядке передачи по сети;
  - `ReplicatedFloatCount` — по 1 float на stat и по 2 на resource;
  - `Hash` — FNV-1a по (id, kind, replicated) каждого слота. Хост и клиент сверяют его в `NetworkAttributes`.

### `Runtime/AttributeSet.cs`
Значения одной сущности. Чистый C#, без UnityEngine.Object и без Fusion.

**Модель:**
```
value[slot]   = clamp(base + Σ аддитивных модификаторов, Min, Max)   // для Resource это максимум
current[slot] = clamp(current, Min, value)                            // только Resource
flags         = baseFlags | OR(флаги, выданные источниками)
```

**Хранение.** Массивы `_base`, `_modSum`, `_value`, `_current` (по слоту), `_baseFlags`, `_flags`.
Ещё три списка: `_modifiers` (slot, value, source), `_grants` (mask, source) и `_effects` (`ActiveEffect`).

**Чтение без аллокаций, O(1):** `Get`, `GetBase`, `GetCurrent` (у stat возвращает value),
`GetNormalized` (current/max), `HasFlag`, `Has`, `SlotOf`, а также `GetSlot*` по номеру слота.

**Запись (только хост или офлайн):**
- `SetBase` / `SetSlotBase` — клампит и пересчитывает value. У ресурса заодно клампится current.
- `SetCurrent` / `SetSlotCurrent`, `AddCurrent` (возвращает фактически применённую дельту),
  `TrySpend` (тратит, только если хватает всей суммы).
- `SetFlag` меняет базовый флаг.

**Модификаторы.**
- `AddModifier(id, value, source)` — если атрибута нет, модификатор молча игнорируется.
- `GrantFlag(flag, source)`.
- `RemoveSource(source)` удаляет все модификаторы и флаги источника. Для затронутых слотов сумма
  пересчитывается с нуля по оставшимся модификаторам, а не вычитанием, чтобы не копился float-дрейф.

**Эффекты:**
- `ApplyEffect(def, tick, tickRate, stacks)`. `EndTick = tick + max(1, round(Duration × tickRate))`,
  при `Duration = 0` — `int.MaxValue` (бессрочно). Новый эффект получает свой `ModifierSource`,
  модификаторы `ValuePerStack × Stacks` и выданные флаги. Повторное наложение:
  - `Refresh` — перезапускает таймер;
  - `Stack` — прибавляет стаки до `MaxStacks`, перезапускает таймер и переналагает модификаторы;
  - `Ignore` — возвращает false.
- `Tick(tick, dt)` идёт по эффектам с конца. Сначала периодика:
  `AddCurrent(resource, PerSecondPerStack × Stacks × dt)` (у stat-атрибута ничего не меняет).
  Затем при `tick >= EndTick` эффект снимается вместе с его источником.
- `RemoveEffect`, `ClearEffects`, `HasEffect(def | id)`, `GetStacks`, `IndexOfEffect`.
- `ActiveEffect`: `Definition`, `Stacks`, `StartTick`, `EndTick`, `Source`, `IsInfinite`, `RemainingSeconds(now, rate)`.

**Отслеживание изменений.** C#-событий нет. Каждое изменение увеличивает `Version` и ставит бит в
`ChangedSlots`, либо `FlagsChanged` / `EffectsChanged`. `ConsumeChanges(out…)` отдаёт изменения и
сбрасывает их. Системы и UI опрашивают это раз в тик или кадр.

**Reset()** возвращает к профилю: базы, ресурсы полные или пустые (по `StartFull`), базовые флаги.
Модификаторы и эффекты удаляются, помечаются все слоты.

**Репликация на клиенте** (вызывает `NetworkAttributes`). `ApplyReplicated(slot, value, current)` и
`ApplyReplicatedFlags(mask)` пишут значения напрямую, минуя модификаторы.
`BeginReplicatedEffects` / `AddReplicatedEffect` / `EndReplicatedEffects` строят зеркало списка эффектов
для UI. Модификаторы на клиенте не накладываются.

---

## Config (ScriptableObject)

### `Config/AttributeDefinition.cs`
Ассет «атрибут» (меню Create ▸ RumOverboard ▸ Attributes ▸ Attribute). Определён `enum AttributeKind`:
- `Stat` — просто число;
- `Resource` — число как максимум плюс `Current`.

| Поле | Назначение |
|---|---|
| `id` (ReadOnly) | выдаёт `AttributeRegistryBuilder`; `Id` → `AttributeId` |
| `Kind` | Stat / Resource |
| `DefaultValue` | ⚠ сейчас **не используется** рантаймом: база всегда берётся из `AttributeEntry.BaseValue` профиля |
| `Min`, `Max` (по умолчанию 0 и 1e6) | клампинг value и current; `Clamp(v)` |
| `StartFull` | ресурс стартует с current = max, иначе с Min. Также влияет на overrides в `AttributesComponent` |
| `Replicated` | передавать ли клиентам (value, а для ресурса и current) |
| `Description` | для дизайнера и debug |

`EditorSetId(ushort)` — только для инструментов (`#if UNITY_EDITOR`).

### `Config/FlagDefinition.cs`
Ассет «флаг». Поля: `id` (byte 1..64, ReadOnly), `Description`. `Id` → `FlagId`. Есть `EditorSetId(byte)`.

### `Config/EffectDefinition.cs`
Ассет «эффект». Содержит `enum EffectStacking { Refresh, Stack, Ignore }` и структуры
`EffectModifier { Attribute, ValuePerStack }` и `EffectPeriodic { Resource, PerSecondPerStack }`.

| Поле | Назначение |
|---|---|
| `Duration` | секунды, 0 = пока не снимут |
| `Stacking`, `MaxStacks` | правило повторного наложения |
| `Replicated` | попадает ли в сетевой список эффектов (иконки, VFX, таймер на клиенте) |
| `Modifiers` | аддитивные модификаторы × стаки |
| `GrantedFlags` | флаги, пока эффект активен |
| `Periodic` | изменение current ресурса в секунду × стаки |

`GrantedMask` — OR масок выдаваемых флагов. Есть `EditorSetId(ushort)`.

### `Config/EntityData.cs`
`interface IEntityData` — маркер блоков статических данных профиля: ссылки, тексты, тюнинг.
Блоки не копируются на экземпляр и не реплицируются. Новый блок — это `[Serializable]` класс с этим
интерфейсом; Alchemy сам покажет его в выпадающем списке поля `data` профиля.

Примеры:
- `DisplayData` (DisplayName, Description);
- `BreakableData` (DebrisPrefab, BreakSound);
- `FlammableData` (IgnitionTime, SpreadRadius, FireVfx).

Геймплей их пока не читает, они заготовлены под разрушение и огонь.

### `Config/AttributeProfile.cs`
Ассет «тип сущности». Всё о типе собрано в одном месте.

- `AttributeEntry { Attribute, BaseValue }`.
- Поля:
  - `parent` — наследование;
  - `attributes` — список записей;
  - `flags` — стартовые флаги;
  - `[SerializeReference] data` — список `IEntityData`.
- `Layout` строится лениво при первом обращении и кэшируется (`[NonSerialized]`). `OnValidate`
  сбрасывает кэш: правка в инспекторе пересоберёт раскладку при следующем обращении. Уже созданные
  `AttributeSet` продолжают жить со старой раскладкой.
- `GetData<T>()` / `TryGetData<T>()` — первый блок типа T, сначала свой, потом по цепочке родителей.
- Для инструментов: `EditorSetup(...)` заменяет содержимое; `EditorSetBaseValue(def, v)` правит базу
  или добавляет override унаследованной записи; `EditorInvalidateLayout()`.

### `Config/AttributeRegistry.cs`
Реестр всех определений. Ассет лежит в `Configs/Attributes/Resources/AttributeRegistry.asset`,
грузится через `Resources.Load("AttributeRegistry")`.

- Поля (ReadOnly): `attributes`, `flags`, `effects`, `hash`. Заполняет только `AttributeRegistryBuilder`
  через `EditorSet(...)`.
- `Instance` загружается лениво. Если ассета нет, в лог пишется ошибка. Статика сбрасывается на
  `SubsystemRegistration`, что важно при выключенном domain reload.
- `Get(AttributeId | FlagId | EffectId)` — O(1) через массивы, построенные при первом вызове.
- `Hash` — хэш пар «id ↔ GUID ассета». ⚠ Пока нигде не сверяется при подключении.
- Нужен в рантайме для двух вещей: клиент по `EffectId` из сети находит `EffectDefinition`, debug-окно
  строит списки. Геймплей обращается к атрибутам через сгенерированные константы.

---

## Компоненты

### `AttributesComponent.cs`
`MonoBehaviour`, наследник `SimComponent<AttributesComponent>`. Регистрируется в `SimWorld` и
привязывает все дочерние коллайдеры, поэтому `AttributesComponent.TryGet(collider, out a)` работает за O(1).

- **Поля в инспекторе:**
  - `profile`;
  - `overrides` — базы на этот экземпляр;
  - `selfTick` — тикать эффекты в `FixedUpdate` (`NetworkAttributes` выключает).
- **Жизненный цикл.** `Awake` вызывает `EnsureBuilt()`: создаёт `Set` из `profile.Layout` и применяет
  overrides (ресурс со `StartFull` после override заполняется до нового max). Без профиля пишет
  warning, `Set` остаётся `null`, а все удобные методы возвращают значения по умолчанию.
- **Авторитет и часы:**
  - `IsAuthority` — false на сетевом клиенте и до сетевого спавна;
  - `Tick` / `TickRate` — часы для эффектов. Офлайн это собственный счётчик FixedUpdate,
    в сети `NetworkAttributes` выставляет их из `Runner.Tick`.
  - `SetNetworkDriven(authority)` и `SetClock(tick, rate)` вызывает `NetworkAttributes`.
- `ResetToProfile()` — `Set.Reset()` плюс overrides.
- **Удобные методы:** `Has`, `Get`, `GetCurrent`, `HasFlag`, `HasEffect(EffectId)`, `GetData<T>()`.
  `ApplyEffect` / `RemoveEffect` принимают `EffectId` (через реестр) или `EffectDefinition`. На клиенте
  `ApplyEffect` пишет warning и возвращает false, `RemoveEffect` — false.

### `EffectZone.cs`
Триггер (`RequireComponent(Collider)`, `Reset` ставит `isTrigger`), который держит эффект на всём внутри.

- `Dictionary<AttributesComponent, int>` считает перекрывающиеся коллайдеры одной сущности, поэтому
  объект из нескольких коллайдеров входит и выходит один раз.
- Вход: эффект накладывается сразу. Внутри: повторно раз в `refreshInterval` (0.5 с), так таймер не
  истекает. Выход: при `removeOnExit` эффект снимается, иначе доживает свою длительность.
- Работает только при `IsAuthority`. На клиентах триггеры тоже срабатывают, но ничего не меняют.
- Сущность ищется сначала через `SimWorld` (O(1)), затем `GetComponentInParent`.
- `OnDrawGizmos` рисует голубой полупрозрачный куб для `BoxCollider`.
- ⚠ `removeOnExit` снимает эффект, даже если сущность ещё стоит в другой зоне с тем же эффектом.

---

## Сеть

### `Networking/NetworkAttributes.cs` (`#if FUSION2`)
`NetworkBehaviour`, `RequireComponent(AttributesComponent)`. Нужен там, где значения меняются в
рантайме и клиенты должны их видеть.

- **Состояние в сети:**
  - `NetworkArray<float> Values` (`MaxValues = 32`);
  - `NetworkArray<EffectNetEntry> Effects` (`MaxEffects = 8`);
  - `ulong Flags`;
  - `byte EffectCount`;
  - `int LayoutHash`.

  `EffectNetEntry { ushort Effect; byte Stacks; int StartTick; int EndTick; }`, метод `SameAs`.
- **Awake.** `SetNetworkDriven(false)`: до спавна нет ни авторитета, ни самостоятельного тика.
  Сценовые объекты существуют раньше runner'а, и без этого на клиенте зоны успевали бы навесить
  эффект локально.
- **Spawned.** Ставит авторитет по `HasStateAuthority`. Если `ReplicatedFloatCount > 32`, пишет ошибку.
  Хост записывает `LayoutHash` и делает первый `Write`. Клиент сверяет хэш (при расхождении — ошибка
  `[Attributes] … layout hash mismatch`) и делает первый `Read`.
- **FixedUpdateNetwork (хост).** `SetClock(Runner.Tick, 1/DeltaTime)`, затем `Set.Tick(...)`. `Write()`
  вызывается, только если `Set.Version` изменился с прошлой записи.
- **Render (клиент).** `SetClock` и `Read()`.
- **Write.** По `ReplicatedSlots` пишет value, а у ресурса ещё current. Затем `Flags`. Затем до 8
  эффектов с `Replicated`.
- **Read.** То же в обратном порядке через `ApplyReplicated*`. Список эффектов пересобирается, только
  если он отличается от закэшированного `_seenEffects`.
- `IsReady` — объект заспавнен и валиден. Используется в тестах.
- Модификаторы не передаются: клиент статы не считает, только показывает результат.

---

## Debug

### `Debug/AttributesDebugWindow.cs`
IMGUI-окно, открывается и закрывается по **F6**, пока открыто — курсор свободен. Сделано в стиле
`CrewRagdollDebugWindow`, `DefaultExecutionOrder(1300)`.

- **Список сущностей** из `SimWorld.All<AttributesComponent>()`, клик выбирает.
- **Атрибуты:** `base → value | current`.
  - Кнопки `−`/`+` меняют базу на шаг `max(1, 10% базы)`.
  - У ресурсов есть `−10%` (current) и `Fill`.
- **Флаги:** все флаги реестра с текущим состоянием; переключение меняет базовый флаг.
- **Эффекты:**
  - активные со стаками и оставшимся временем, кнопка `Remove`;
  - кнопки `Apply` на каждый эффект реестра.
- **Reset to profile**, **Save base → profile** (только в редакторе). Сохранение записывает в ассет
  профиля те базы, что отличаются от раскладки, и сохраняет ассет.
  ⚠ Overrides экземпляра тоже попадут в профиль. Живой `Set` работает со старой раскладкой до
  следующего Play.
- На клиенте (`!IsAuthority`) окно только читает данные.

### `Debug/AttributesNetSmoke.cs` (`#if FUSION2`)
Сценарий сетевого теста, лежит в `AttributesScene`. Без запроса ничего не делает.

- **Запрос:**
  - файл `%TEMP%/RumOverboard/attributes_netsmoke.request` с id прогона, младше 10 минут;
  - либо `runAlways` в инспекторе.

  `Awake` (раньше `ConnectionManager.Start`) переопределяет автостарт на приватную сессию
  `attr-smoke-<run>`, чтобы не попасть в чужую сессию `rum-overboard`. Роли заданы жёстко:
  главный редактор — `Host`, клон — `Client`. Два AutoHostOrClient, стартовавших одновременно, оба
  создавали сессию.
- **Start.** Ждёт запущенный `NetworkRunner` до 90 с. Клон, если подключился раньше, чем хост создал
  сессию, повторяет `StartClient` каждые 3 с. Режим `Single` (нет Photon) — провал теста.
- **Хост.** Ждёт клиента (до 240 с) и спавна. Затем:
  - Board: база Mass = 7, флаг Floating выключается;
  - Crate: Integrity = 200 (база и current), `BurningFx` ×2.

  После этого держит сессию ещё 20 с.
- **Клиент:**
  1. Runner — чистый клиент; все `NetworkAttributes` заспавнены; авторитета нет ни на одной сущности.
  2. Crate: Burning ×2, флаг, остаток времени в (0, 8.5] с, целостность падает.
  3. Board: правки хоста дошли, прочие флаги не тронуты.
  4. Зоны: ColdFx у игрока (эффект; `StaminaRegen` host-only); Wet у бочек, масса 42 и 62.
  4a. Настоящий игрок хоста (префаб `NetworkPlayer`): стамина 10% + `Exhausted` доходят, на клиенте только чтение; после возврата регенерации на хосте — восстановление (> 30%, флаг снят). Опьянение 0.6 и ragdoll baseline ≥ 0.3 у того же игрока.
  5. Свой `ApplyEffect` клиента отклонён и состояние не изменилось.
  6. Burning истёк, флаг снят, целостность остановилась около 120.
  7. В логе нет ошибок `[Attributes]` (перехват `logMessageReceived`).
- **Отчёт.** `[AttributesNetSmoke:<role>@<main|clone>] run <id>: PASS / N FAILED` в лог и в файл
  `attributes_netsmoke.<main|clone>.txt` (пир — по `CurrentPlayer.IsMainEditor`).

---

## Generated

### `Generated/Attr.g.cs`
Генерирует `AttributeRegistryBuilder`, руками не править. Классы:
- `Attr` — `AttributeId` на каждый атрибут;
- `AttrFlag` — `FlagId`;
- `AttrEffect` — `EffectId`.

Имя константы — имя ассета в PascalCase (не буквы и не цифры убираются; если начинается с цифры,
спереди ставится `_`; при совпадении имён добавляется `_<id>`). Сейчас там Grog=1, Integrity=2,
Mass=3, Stamina=4; флаги Burning=1 … Wet=5; эффекты BurningFx=1, ColdFx=2, WetFx=3.
Переименовал ассет — переименовалась константа, и использующий её код перестанет компилироваться.

---

## Editor

### `Editor/Attributes/AttributeRegistryBuilder.cs`
Меню **RumOverboard ▸ Attributes ▸ Rebuild Registry + Codegen**. Пути заданы константами:
`ConfigFolder`, `RegistryPath`, `GeneratedPath`.

- **Rebuild:**
  1. Находит все ассеты трёх типов.
  2. Назначает id. Уже выданные уникальные id сохраняются. Новым ассетам и дубликатам (например,
     после Ctrl+D) достаётся следующий свободный id, кандидаты перебираются по пути ассета.
     Флагам — не больше 64.
  3. Создаёт или обновляет реестр в Resources, считает хэш по (id, GUID).
  4. Пишет `Attr.g.cs`, только если текст изменился (лишних перекомпиляций нет).
- **`AttributeDefinitionPostprocessor` (AssetPostprocessor).** Пересобирает реестр через
  `delayCall`, когда ассеты определений создаются, переименовываются или переносятся. Удаления
  отслеживаются только в `Configs/Attributes`: тип удалённого ассета уже не узнать. В клонах MPPM
  (`!CurrentPlayer.IsMainEditor`) не работает: у них Assets только для чтения.
- `EnsureFolder(path)` — рекурсивно создаёт папки. Используется в setup-скрипте.

### `Editor/Attributes/AttributesSelfTest.cs`
Меню **Run Self-Test** и batch-запуск `-executeMethod RumOverboard.EditorTools.Attributes.AttributesSelfTest.RunBatch`
(код выхода 0 или 1). Определения создаются в памяти (`HideAndDontSave`) и удаляются в `finally`.
Ассеты проекта тесту не нужны.

Проверяет:
1. базовые значения и раскладку;
2. аддитивные модификаторы, кламп, `RemoveSource`, модификатор на отсутствующий атрибут;
3. ресурсы: `AddCurrent`, кламп при уменьшении max, `TrySpend`, `GetNormalized`;
4. эффект с таймером: модификатор, флаг, истечение ровно на тике 50;
5. стаки, `MaxStacks`, периодику, `Ignore`, бессрочный эффект;
6. маски изменений;
7. наследование профиля и `GetData`;
8. **0 байт аллокаций** на 10 000 итераций горячего пути (`GC.GetAllocatedBytesForCurrentThread`).

Итог — `[AttributesTest] ALL PASSED` или `N FAILED`.

### `Editor/Attributes/AttributesSandboxSetup.cs`
**★ Build Sandbox** = Create Sample Content + Rebuild Registry + сцена.

- **CreateSampleContent** создаёт ассеты только если их ещё нет, поэтому правки дизайнера не
  затираются. Состав — в разделе «Ассеты» ниже.
- **BuildScene** открывает или создаёт `AttributesScene.unity`, очищает корень и собирает сцену: свет,
  камеру, пол, 5 сущностей, 2 зоны, debug-окно (сразу видимое), `ConnectionManager` и `AttributesNetSmoke`.
  Затем сохраняет сцену; Fusion запекает сценовые NetworkObject'ы при сохранении.
- **Entity** создаёт примитив, kinematic `Rigidbody` (если физический), `AttributesComponent` с
  профилем, `NetworkObject` и `NetworkAttributes`.
- **CreateConnectionManager:** `config = GameConfig.asset`, автостарт `AutoHostOrClient`, без префабов
  корабля и игрока.
- **EnsureInBuildSettings** включает сцену в Build Settings и сохраняет `EditorBuildSettings.asset`:
  `ConnectionManager` синхронизирует сцену по build-индексу.
- `ScenePath` — публичная константа, её используют smoke-тесты.

### `Editor/Attributes/AttributesSandboxSmoke.cs`
Меню **Run Sandbox Smoke (Play Mode)** — один редактор, он хост или Single.

- Открывает сцену, ставит флаг в `SessionState` (он переживает domain reload при входе в Play) и входит в Play.
- `EditorApplication.update` ждёт, пока все сущности получат авторитет (до 30 с, это время на
  подключение к Photon), затем ещё 4 с.
- Проверяет: спавн с авторитетом, ColdFx у игрока и сниженный им `StaminaRegen`, Wet у бочек (42 и 62),
  наследование Flammable, отсутствие Wet у доски, флаг CriticalItem, `BreakableData` у ящика, реестр.
- Пишет `[AttributesSmoke] PASS/FAIL` и выходит из Play.

### `Editor/Attributes/AttributesNetworkSmoke.cs`
Меню **Run Network Smoke (MPPM host + client)** — оркестратор для двух пиров. Это машина состояний
в `SessionState` + `EditorApplication.update`:

| Stage | Что происходит |
|---|---|
| Run() | открыть сцену, записать запрос с id прогона, удалить старые отчёты main и clone |
| 3 | Player 2 уже запущен → `Deactivate`, ждём `NotLaunched` (до 60 с) |
| 1 | `Activate`, ждём `Launched` (до 600 с; при первом запуске клон импортирует Library) + 3 с |
| 2 | Play; ждём оба отчёта (main и clone), после первого — не больше 40 с; общий лимит 300 с |

- **Finish.** PASS, только если оба отчёта PASS и роли — один хост и один клиент. Затем выход из Play
  и удаление запроса, чтобы обычный Play сразу после теста не запускал сценарий.
- **Почему клон всегда перезапускается.** Клон, подхвативший изменения скриптов, работает на
  read-only AssetDatabase и не может загрузить `NetworkProjectConfig` Fusion.
- **MPPM через внутреннее API (рефлексия).** Сборка берётся через
  `typeof(EditorMultiplayerRolesManager).Assembly`, далее `MultiplayerPlaymode.PlayerTwo`,
  `UnityPlayer.PlayerState` и `Activate` / `Deactivate`. После обновления Unity это может сломаться:
  тогда тест напишет понятную ошибку, и Player 2 можно включить вручную.

---

## Ассеты (`Assets/Source/Configs/Attributes`)

Созданы **★ Build Sandbox** как примеры. Их можно свободно править: повторный Build их не перезапишет.

**Definitions:**

| Ассет | Kind | DefaultValue | Назначение |
|---|---|---|---|
| Stamina | Resource | 100 | стамина действий (GDD §13.4) |
| Grog | Resource | 100 | запас рассудка (GDD §15) |
| Mass | Stat | 10 | масса, кг (пока не применяется к Rigidbody) |
| Integrity | Resource | 50 | целостность разрушаемых объектов |

У всех `Min = 0`, `Max = 1e6`, `StartFull`, `Replicated`.

Статы стамины (создаёт тот же `CreateSampleContent`, Kind Stat, **не** реплицируются, `Max = 1000`): StaminaRegen 12, StaminaRegenDelay 1, StaminaRecoverThreshold 30, StaminaSprintCost 10, StaminaJumpCost 8, StaminaClimbCost 6, StaminaSwimCost 5, StaminaHaulCost 8, StaminaExhaustedSpeed 0.5 (`Max = 1`). Подробно — [Stamina.md](Stamina.md).

Опьянение (тот же `CreateSampleContent`): `Drunkenness` — Resource, cap 1 (`Max = 1`), `StartFull = false`, реплицируется; `DrunkSoberRate` — Stat 0..1, по умолчанию 0, host-only. Подробно — [Intoxication.md](Intoxication.md).

**Flags:** Flammable, Floating, CriticalItem (критичные предметы из GDD §16.5), Burning, Wet, Exhausted (стамина).

**Effects:**

| Ассет | Duration | Stacking | Что делает |
|---|---|---|---|
| WetFx | 10 с | Refresh | Mass +2, флаг Wet |
| BurningFx | 8 с | Stack, макс. 3 | флаг Burning, Integrity −5/с за стак |
| ColdFx | 2 с (зона обновляет) | Refresh | StaminaRegen −6 (было Stamina −4/с; старый ассет мигрирует `CreateSampleContent`) |

**Profiles:**

| Ассет | Parent | Атрибуты | Флаги | Данные |
|---|---|---|---|---|
| Player | — | Stamina 100, Grog 100, Mass 80, Drunkenness (cap 1), DrunkSoberRate 0 + 9 статов стамины (см. Stamina.md) | — | DisplayData |
| Barrel | — | Mass 40, Integrity 30 | Flammable, Floating | Display, Flammable, Breakable |
| PowderBarrel | Barrel | Mass 60, Integrity 15 | (наследует) | DisplayData (своё описание) |
| Board | — | Mass 5, Integrity 10 | Flammable, Floating, CriticalItem | DisplayData |
| Crate | — | Integrity 80 | Flammable | Display, Breakable |

**`Resources/AttributeRegistry.asset`** генерирует Rebuild Registry, руками не править.

## Сцена `Assets/Scenes/AttributesScene.unity`

Собирается **★ Build Sandbox**. Воды нет: атрибуты от неё не зависят.

| Объект | Позиция | Состав |
|---|---|---|
| Player (капсула) | x −6 | kinematic RB, Attributes(Player), NetworkObject, NetworkAttributes |
| Barrel / PowderBarrel (цилиндры) | x −2 / 0.5 | то же, профили Barrel / PowderBarrel |
| Board | x 3 | то же, Board |
| Crate (static) | x 6 | без RB, Attributes(Crate), NetworkObject, NetworkAttributes |
| ColdZone | x −6, 3×3×3 | BoxCollider (trigger) + EffectZone(ColdFx) |
| WetZone | x −0.75, 4.5×3×3 | EffectZone(WetFx) — бочки внутри, доска снаружи |
| ConnectionManager | | автостарт AutoHostOrClient, `playerPrefab` = NetworkPlayer, точки спавна CrewSpawn0/1 (z −4, вне зон) |
| AttributesDebugWindow, AttributesNetSmoke | | |

Тела kinematic, потому что в этой сцене позиции по сети не синхронизируются. Rigidbody нужен только
для того, чтобы триггеры на хосте видели объекты.

---

## Изменённые существующие файлы

| Файл | Изменение |
|---|---|
| `Scripts/Networking/ConnectionManager.cs` | `OnPlayerJoined`: если `playerPrefab` не задан, экипаж не спавнится (раньше 8 с ретраев и ошибка). Нужно для сцен без игрока |
| `ProjectSettings/EditorBuildSettings.asset` | в конец добавлена `AttributesScene` (enabled) |
| `Assets/Scenes/FEATURE_SCENES.md` | раздел AttributesScene |
| `CLAUDE.md` | строка о системе атрибутов в «Прочее» |
| `Assets/Source/Docs/Attributes.md` | обзор системы (новый файл) |
| `Scripts/Editor/FeatureSceneSetupEditor.cs` | `SetNetworkPrefabRef` стал `internal` (его использует AttributesSandboxSetup) |
| Стамина (см. [Stamina.md](Stamina.md)) | `StateDefinitions.cs`, `LocomotionStates.cs`, `NetworkPlayer.cs`, `HintOverlay.cs`, `PlayerCameraRig.cs`, `NetworkPlayer.prefab`, `ShipSandboxSetup.PreparePlayerPrefab` |
| Опьянение (см. [Intoxication.md](Intoxication.md)) | `NetworkPlayer.cs` (поле убрано из `PlayerNetState`), `CrewRagdollDebugWindow.cs` (слайдер протрезвления) |
| `Gameplay/Diagnostics/SandboxSmokeTest.cs`, `Editor/ShipSandbox/ShipSandboxSmokeTest.cs` | smoke корабля можно запускать в открытом редакторе (меню *Run Smoke Test (in editor)*): `EditorApplication.Exit` только в batch-режиме |

## Известные ограничения (сводка)

- `AttributeDefinition.DefaultValue` пока не используется: база всегда из профиля.
- `EffectZone.removeOnExit` не учитывает другие зоны с тем же эффектом.
- После «Save base → profile» живые объекты работают со старой раскладкой до следующего Play.
- `AttributeRegistry.Hash` не сверяется при подключении. Сверяется только хэш раскладки каждого объекта.
- Значения передаются полным float, без квантования. Лимиты: 32 float и 8 эффектов на сущность,
  64 атрибута в профиле, 64 флага в игре.
- Модификатор или эффект на атрибут, которого нет у сущности, молча игнорируется.
- Блоки `IEntityData` (`BreakableData`, `FlammableData`) и `Mass` геймплей пока не использует.
- Network Smoke зависит от внутреннего API MPPM и от Photon cloud.
