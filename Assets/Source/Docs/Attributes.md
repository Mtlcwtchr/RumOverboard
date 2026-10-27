# Attributes — система атрибутов сущностей

Data-driven атрибуты по мотивам After Dark (сущность собирается из данных, всё о типе сущности —
в одном ассете), но без класса на каждый атрибут, без поиска O(n), с репликацией Fusion
(host authority, клиенты ничего не считают). Прогрессии/формул нет — модификаторы только аддитивные.

Код: `Assets/Source/Scripts/Gameplay/Attributes` (runtime, config, networking, debug),
редактор: `Assets/Source/Scripts/Editor/Attributes`, ассеты: `Assets/Source/Configs/Attributes`.
Пофайловое устройство — [AttributesFiles.md](AttributesFiles.md).

## Данные (ScriptableObject, меню Create ▸ RumOverboard ▸ Attributes)

| Ассет | Что это |
|---|---|
| `AttributeDefinition` | Числовой атрибут. `Stat` — value = clamp(base + Σмодификаторов). `Resource` — то же value как максимум + `Current` в [Min, value] (стамина, грог, целостность). Флаг `Replicated`. |
| `FlagDefinition` | Булев признак (Flammable, Floating, CriticalItem, Burning, Wet…). До 64 на игру, у сущности — один `ulong`. |
| `EffectDefinition` | Временное состояние: длительность (0 = бессрочно), стаки (Refresh/Stack/Ignore), аддитивные модификаторы × стаки, выдаваемые флаги, периодическое изменение ресурса в секунду. |
| `AttributeProfile` | Тип сущности целиком: атрибуты с базовыми значениями + стартовые флаги + блоки статических данных `IEntityData` (`[SerializeReference]`, выпадающий список Alchemy: префабы, VFX, тексты, тюнинг). Есть `Parent` — наследование (Barrel → PowderBarrel). |
| `AttributeRegistry` | `Configs/Attributes/Resources/AttributeRegistry.asset` — все определения по id + хэш. Генерируется. |

ID назначаются **автоматически** при создании/переименовании/удалении ассетов определений
(или вручную: `RumOverboard ▸ Attributes ▸ Rebuild Registry + Codegen`). Уже выданные id
не меняются. Кодоген пишет `Generated/Attr.g.cs`:

```csharp
Attr.Stamina        // AttributeId
AttrFlag.Flammable  // FlagId
AttrEffect.WetFx    // EffectId
```

Переименование ассета = переименование константы (код, который её использует, перестанет компилироваться — это намеренно).

## Runtime

- `AttributesComponent` — на GameObject (игрок, лут, физический объект, статичный разрушаемый).
  Профиль + per-instance overrides базовых значений. Регистрируется в `SimWorld`:
  `AttributesComponent.TryGet(collider, out var attrs)` — O(1) от хита рейкаста/триггера.
- `AttributeSet` — значения одной сущности (чистый C#): чтение O(1) без аллокаций.

```csharp
attrs.Get(Attr.Mass);                 // value (для ресурса — максимум)
attrs.GetCurrent(Attr.Stamina);       // текущее значение ресурса
attrs.HasFlag(AttrFlag.Flammable);
attrs.GetData<FlammableData>();       // статические данные профиля
attrs.ApplyEffect(AttrEffect.WetFx);  // только хост; на клиенте игнорируется
attrs.Set.TrySpend(Attr.Stamina, 10f);

var src = ModifierSource.New();       // свой источник модификаторов (зона, предмет в руках…)
attrs.Set.AddModifier(Attr.Mass, 5f, src);
attrs.Set.GrantFlag(AttrFlag.Wet, src);
attrs.Set.RemoveSource(src);          // снять всё, что дал источник
```

- Изменения: событий на каждое значение нет. Любое изменение увеличивает `Set.Version` и
  выставляет биты в `ChangedSlots / FlagsChanged / EffectsChanged`; системы и UI проверяют их
  раз в тик/кадр (`Set.ConsumeChanges(...)` — забрать и сбросить).
- `EffectZone` — триггер, держит эффект на всём, что внутри (холод, дождь, брызги, дым).

## Сеть — `NetworkAttributes` (рядом с `AttributesComponent` на NetworkObject)

- Хост тикает эффекты в `FixedUpdateNetwork` (время эффектов — тики Fusion) и при изменении пишет:
  итоговые значения реплицируемых атрибутов (+ current у ресурсов) как `float`, маску флагов (`ulong`),
  активные эффекты `(id, stacks, startTick, endTick)` — до 8 штук.
- Модификаторы не реплицируются: клиент статы не считает, только показывает. Остаток времени эффекта клиент считает сам по `endTick`.
- Клиенты в `Render()` копируют состояние в свой `AttributeSet` → тот же API на всех пирах.
- Fusion шлёт только изменённые слова — «тихие» атрибуты после спавна бесплатны.
- `LayoutHash` сверяется на клиенте (несовпадение профиля/реестра между билдами → ошибка в лог).
- Лимиты: 32 float на сущность (`NetworkAttributes.MaxValues`), 8 эффектов, 64 атрибута в профиле.
- Объектам, у которых значения не меняются в рантайме, `NetworkAttributes` не нужен: ассет профиля есть у всех.
- До `Spawned` сущность с `NetworkAttributes` не авторитетна и не тикает сама (сценовые объекты существуют
  раньше раннера; на клиенте триггер-зоны иначе успели бы навесить эффект локально). Поэтому сцене с такими
  объектами нужен `ConnectionManager` — без раннера они так и останутся «только для чтения».
- Проверено на двух пирах (хост + клиент через Photon) — см. `Run Network Smoke` ниже.

## Песочница и тесты

- `RumOverboard ▸ Attributes ▸ ★ Build Sandbox` — примеры ассетов (создаются только если их нет),
  реестр, сцена `Assets/Scenes/AttributesScene.unity` (добавляется в Build Settings — Fusion синхронизирует
  сцену по build-индексу): Player в зоне холода (ColdFx снижает `StaminaRegen`), Barrel/PowderBarrel в мокрой зоне
  (WetFx: +2 Mass, флаг Wet), Board, статичный Crate. Все сущности — сценовые NetworkObject с
  `NetworkAttributes` (тела kinematic: позиции тут не синхронизируются). `ConnectionManager` с
  автостартом AutoHostOrClient, спавнит настоящих игроков (`NetworkPlayer` со стаминой) на точках
  CrewSpawn0/1, корабля нет; без Photon — офлайн Single. Стамина экипажа — [Stamina.md](Stamina.md).
- Debug-окно **F6** (`AttributesDebugWindow`): все сущности, base → value | current, флаги, эффекты с
  остатком времени; правка base/ресурсов, флаги, apply/remove эффектов, Reset to profile,
  Save base → profile (только в редакторе). На клиенте — только чтение.
- `RumOverboard ▸ Attributes ▸ Run Self-Test` — проверки ядра (агрегация, клампы, ресурсы, эффекты,
  стаки, наследование, 0 байт аллокаций в горячем пути). Batch:
  `Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.Attributes.AttributesSelfTest.RunBatch`
- `RumOverboard ▸ Attributes ▸ Run Sandbox Smoke (Play Mode)` — один редактор (хост или Single):
  входит в Play на AttributesScene, ждёт спавна, проверяет действие зон, пишет `[AttributesSmoke] PASS/FAIL`.
- `RumOverboard ▸ Attributes ▸ Run Network Smoke (MPPM host + client)` — хост + клиент:
  перезапускает виртуального игрока MPPM «Player 2» (клон редактора), входит в Play — главный редактор
  хостит приватную сессию `attr-smoke-<run>`, клон подключается клиентом. Сценарий —
  `AttributesNetSmoke` (в сцене, спит без запроса): хост меняет базу/флаг у Board и поджигает Crate,
  клиент проверяет репликацию значений, флагов, эффектов (стаки, остаток времени, истечение), эффекты
  зон, отсутствие у себя авторитета и ошибок хэша. Итог — `[AttributesNetSmoke] PASS/FAIL` с отчётами
  обоих пиров (файлы в `%TEMP%/RumOverboard/`). Занимает ~1–1.5 мин; Player 2 остаётся включённым.
- **MPPM + Fusion:** клон, подхвативший изменения скриптов, работает на read-only AssetDatabase и может
  не загрузить `NetworkProjectConfig` (`Failed to load NetworkProjectConfigAsset` в логе клона) — раннер
  не стартует. Network Smoke поэтому всегда перезапускает клон; при ручной игре через MPPM после правок
  кода перезапускайте Player 2. Лог клона: `Library/VP/<id>/Logs/Editor.log`.

## Дальше (не сделано)

- Миграция `PlayerNetState.Drunkenness` и стамины игрока на атрибуты.
- Применение `Mass` к Rigidbody, Integrity → разрушение (`BreakableData`), огонь по флагам.
- Квантование значений (сейчас полный float) — если упрёмся в трафик.
- Проверка общего хэша реестра при подключении (сейчас — только хэш профиля на объект).
