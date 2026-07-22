# Ocean System Plan (RumOverboard)

Этот документ фиксирует архитектуру и текущую реализацию системы океана для кооперативного корабельного геймплея.

## 1) Архитектура слоев

1. Визуальная поверхность океана
   - `OceanSurfaceRenderer`
2. Аналитическая модель волн
   - `OceanWaveDefinition`, `OceanWaveMath`
3. API сэмплинга воды
   - `IOceanSampler`, `OceanSample`, `OceanWaveField.Sample(...)`
4. Плавучесть и гидродинамика
   - `ShipBuoyancyController`
5. Течения
   - `OceanCurrentSystem` + зоны `DirectionalCurrentZone`, `VortexCurrentZone`, `SplineCurrentZone`
6. Контактные VFX и события
   - `ShipWaterFxController` + `ShipWaterContactEvent`
7. Профили моря
   - `OceanSeaStateProfile`, `OceanSeaStateController`
8. Подготовка к сетевой симуляции
   - `NetworkOceanState` (Fusion2)

## 2) Render Pipeline

Проект на URP. Вода использует тот же аналитический wave-model на CPU для физики и для визуальной деформации геометрии.

## 3) Что уже реализовано

### Data/config
- `OceanSimulationConfig`
- `OceanQualityPreset` (Low/Medium/High)
- `OceanSeaStateProfile`
- `OceanDepthProfile`

### Runtime
- `OceanWaveField`
  - детерминированный расчет крупных/средних/мелких волн
  - горизонтальное смещение (итеративная коррекция)
  - API сэмплинга: высота, нормаль, смещение, вертикальная/горизонтальная скорость, течение, глубина
- `OceanCurrentSystem` + зональные течения
- `OceanDepthProvider`
- `OceanSurfaceRenderer`
  - LOD-поверхность (near/mid/far)
  - следование за камерой/кораблем
  - CPU-деформация поверхности по тем же волнам
  - прокидывание wave-параметров в URP-материал (без чтения из GPU)
- `ShipBuoyancyController`
  - многоточечная плавучесть
  - демпфирование/сопротивления: вертикальное, продольное, боковое, угловое, относительно течения
  - защита от экстремумов (clamp силы/скорости)
  - события контактов (enter/exit/impact)
- `ShipDeckMotionProvider`
  - линейное/угловое ускорение, крен/тангаж, apparent-force в точке палубы
- `ShipWaterFxController`
  - пороги/cooldown/pool-ish схема для брызг

### Editor tooling
- `ShipBuoyancyControllerEditor` (Handles + визуализация точек)
- `OceanSystemSetupMenu`
  - генерация дефолтных профилей/пресетов
  - setup `OceanSystem` в `Gameplay`
  - attach buoyancy-компонентов к выбранному кораблю
  - wiring ссылок между океаном/кораблем
  - создание/обновление материала воды URP

### Shaders
- `Assets/Source/Environment/Shaders/OceanStylizedURP.shader`
  - стилизованный URP шейдер воды
  - цвет по глубине, гребневая/береговая пена, блеск, прозрачность
  - использование wave arrays из CPU-данных

## 4) Быстрый setup тестовой сцены

1. Меню: `RumOverboard/Setup/Ocean/Create Default Ocean Assets`
2. Меню: `RumOverboard/Setup/Ocean/Setup Gameplay Scene Ocean Root`
3. Выбрать корень корабля в `Gameplay` и выполнить:
   - `RumOverboard/Setup/Ocean/Attach Buoyancy To Selected Ship`
4. Убедиться, что у океан-LOD-объектов назначен материал воды.
5. Запустить сцену и настроить коэффициенты `ShipBuoyancyController`.

## 5) Ключевые параметры для стабильной физики

1. `ShipBuoyancyController.buoyancyForce`
2. `ShipBuoyancyController.verticalDamping`
3. `ShipBuoyancyController.lateralDrag`
4. `ShipBuoyancyController.longitudinalDrag`
5. `ShipBuoyancyController.angularDrag`
6. Количество и размещение `ShipBuoyancyPoint`
7. Переходы профилей (`OceanWaveField` + `OceanSeaStateController.transitionSeconds`)

## 6) Обновление: острые волны, swell, шейдер, дебаг-графика

### Симуляция волн
- `OceanSeaStateProfile`: добавлены `choppiness` (глобальный множитель steepness → острые гребни),
  и доминирующий **swell** (`swellDirectionDegrees/Amplitude/Wavelength/Speed/Steepness`) — большие
  направленные валы. Корабль, идущий в swell, принимает высокие волны на нос («бьют спереди»).
  Свойства-хелперы: `SwellDirection2D`, `HasSwell`, `BuildSwellWave()`, `DominantWaveDirection2D`,
  `EstimatedWaveHeight()`.
- `OceanWaveField`: инъекция swell вне band-cap; применение choppiness к steepness; публичные
  геттеры для HUD/дебага — `DominantWaveDirection`, `EstimatedWaveHeight`, `ActiveProfileName`,
  `SampleHeight(pos)`. Константа `SwellWaveIndex` (публичная) — общий jitter-индекс swell для CPU и GPU.
- Профили Calm/Moderate/Storm перетюнены (круче steepness, больше полос/направлений, добавлен swell).
  Генератор `OceanSystemSetupMenu` синхронизирован — повторная генерация даёт те же значения.

### Синхронизация CPU↔GPU
- `OceanSurfaceRenderer.BuildShaderWaveBuffers` теперь **запекает** в шейдерные uniform'ы те же
  CPU-модификаторы, что использует физика/меш: directional jitter (`OceanWaveMath.JitteredDirection`),
  choppiness, runtime amplitude/speed, и swell-волну. Фрагментные нормали/пена больше не «плывут»
  относительно поверхности. Не запекается: позиционный depth-damping и переход профилей (тонкий дрейф).

### Шейдер `OceanStylizedURP` (реализм + cozy)
Добавлено: отражения reflection-probe (кубмапа, fresnel-взвешенно), SSS-просвет гребней по солнцу,
detail-нормали (2 скролл-слоя, без тангентов), лёгкая рефракция сцены (opaque texture), ambient из SH,
богаче пена. Cozy deep/shallow-рамп сохранён.

⚠️ **Требования URP-ассета:** включить **Opaque Texture** (для рефракции `SampleSceneColor`) и
**Depth Texture** (уже используется). По желанию — назначить материалу воды карту в слот
`_DetailNormal` (по умолчанию `bump`=плоская, шейдер работает и без неё). Reflection Probe в сцене
улучшит отражения (иначе берётся скайбокс).

### Дебаг-графика (в существующих окнах)
`OceanDebugGizmos` — миникарта top-down + стрелки направлений (Handles в GUI-пространстве):
- **Ocean Runtime Window** — миникарта: доминирующая волна, течение, ветер, курс корабля + оценка высоты волны.
- **Wind Runtime Window** — компас ветра + курс корабля + точка галфвинда (into wind / beam / running).
- **Sail Runtime Window** — диаграмма тяги: стрелки по парусам, суммарная тяга, ветер, курс.

## 7) Следующие итерации

- Событийная система пены у форштевня/кильватера.
- Spline-authoring editor для течений.
- Перенос сил буйанси в `FixedUpdateNetwork` авторитета (детерминизм с Fusion Physics addon).
- Сериализация state-профилей для матчевых этапов/чекпоинтов.

