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

## 6) Следующие итерации

- URP water shader (crest foam, shoreline foam, depth color, stylized specular)
- событийная система пены у форштевня/кильватера
- spline-authoring editor для течений
- явная сериализация state-профилей для матчевых этапов/чекпоинтов
- сетевой authoritative `NetworkShip` + интеграция с `ConnectionManager`

