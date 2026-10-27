# Intoxication — опьянение экипажа

Опьянение перенесено из `PlayerNetState.Drunkenness` в систему атрибутов ([Attributes.md](Attributes.md)).
Поведение то же, что было; добавилась только скорость протрезвления (по умолчанию 0 — как раньше).

## Данные (профиль `Player`)

| Атрибут | Вид | Репликация | Смысл |
|---|---|---|---|
| `Drunkenness` | Resource, cap 1, стартует с 0 | да | current = опьянение 0..1 |
| `DrunkSoberRate` | Stat, 0..1, по умолчанию 0 | нет (host-only) | сколько опьянения уходит в секунду, пока не пьёшь |

Чувство опьянения (кривая `DrunkControlLoss`, раскачка походки, влияние на ragdoll, качание камеры и тела)
и длительность глотка (`RumSipDuration`) остались в `GameConfig` — это свойства подачи. Сила глотка теперь у бутылки
(`DrinkData.IntoxicationPerSip`, `GameConfig.RumSipIntoxication` — запасное значение).

## Поток

```
Q с бутылкой в руках (ctx.CanDrink) → DrinkingRumState поднимает ctx.Drunkenness на DrinkData.IntoxicationPerSip
  (−1 Sips у бутылки, +GrogPerSip грога) — см. Items.md; без бутылки Q ничего не делает
NetworkPlayer (хост, после тика):
  CrewIntoxication.Set(attributes, ctx.Drunkenness)        — записать в атрибут
  CrewIntoxication.Step(attributes, drinking, dt)          — протрезвление (не во время глотка)
  UpdateRagdollControl: baseline = Drunkenness × DrunkRagdollInfluence
NetworkAttributes → клиенты: NetworkPlayer.Drunkenness → камера (качание), HUD («НАБУХАННОСТЬ»)
```

- `Gameplay/Crew/Intoxication/CrewIntoxication.cs` — `Has`, `Get` (0 без атрибута), `Set` (кламп 0..1), `Step`.
- `NetworkPlayer`: поле удалено из `PlayerNetState`; `Drunkenness` читает атрибут на любом пире;
  `DebugSetDrunkRagdoll` пишет атрибут; в `FillContext`/`Render` контекст берёт значение из атрибута.
  Если у префаба нет атрибута — предупреждение в лог при спавне, ром не действует.
- Состояния (`DrinkingRumState`, раскачка в `StateMovement`) не менялись — работают с `StateContext.Drunkenness`.

## Debug и тесты

- **F5 `CrewRagdollDebugWindow`** (сцена `CrewRagdollScene`): как раньше (Sip / Sober / Ragdoll / Knockout,
  тюнинг GameConfig) + слайдер **«Sober up /s (profile)»** для всего экипажа и кнопка
  *Save sober rate → Player profile*. Сам атрибут виден и в F6.
- **RumOverboard ▸ Intoxication ▸ Run Self-Test** — `Get/Set/Step`, клампы, протрезвление 0.1/с, пауза во
  время глотка, модификатор от эффекта.
- **RumOverboard ▸ Intoxication ▸ Run Smoke (Play Mode)** — CrewRagdollScene, настоящий игрок: клавиша Drink →
  ровно `RumSipIntoxication` в атрибуте, ragdoll baseline = опьянение × влияние, протрезвление по ставке,
  `DebugSetDrunkRagdoll` пишет атрибут.
- **Attributes ▸ Run Network Smoke** — на клиенте опьянение 0.6 игрока хоста и его ragdoll baseline 0.30.
- Регрессия: Ship Sandbox smoke (in editor) — ALL PASSED.

## Дальше

- Связать с грогом/рассудком (GDD §15): `Grog` уже есть как ресурс; порог рассудка и галлюцинации — отдельная механика.
- Похмелье/эффекты (например, `HangoverFx` с модификатором `DrunkSoberRate` или стамины) — данными, без кода.
