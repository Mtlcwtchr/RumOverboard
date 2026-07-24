# Feature Scenes

## Setup

Scenes are **pre-configured in the editor** — all GameObjects, components, and serialized references
are part of the scene file. No runtime assembly happens at play-mode start.

To (re)build a scene's hierarchy from scratch, use the menu:

- **RumOverboard → Feature Scenes → Setup HullScene (current scene)**
- **RumOverboard → Feature Scenes → Setup WheelScene (current scene)**
- **RumOverboard → Feature Scenes → Setup SailScene (current scene)**

Each menu item creates the full hierarchy (camera, ocean system, test rig, debug window, gizmos,
scene controller, **standalone player**) and wires all serialized references. **Save the scene after running Setup.**

### Multiplayer player

Each scene includes a `ConnectionManager` with `autoStartOnPlay = true`.
On Play the scene instantly starts a Fusion host session and spawns a real `NetworkPlayer` —
no standalone stubs, full multiplayer from the start. A second editor/build can join
the same session for co-op testing.

## HullScene

- Scene file: `Assets/Scenes/HullScene.unity`
- Focus: hull-only buoyancy and ocean coupling.
- Controller: `HullFeatureSceneController` (validates references and binds aggregator in Awake).
- Debug UI: `HullFeatureDebugWindow` (button in top-left, then window controls).
- Config asset: `Assets/Source/Configs/FeatureScenes/HullFeatureConfig.asset`.

## WheelScene

- Scene file: `Assets/Scenes/WheelScene.unity`
- Focus: wheel + rudder dynamics and optional water disturbance.
- Controller: `WheelFeatureSceneController` (validates references and binds aggregator in Awake).
- Debug UI: `WheelFeatureDebugWindow`.
- Config asset: `Assets/Source/Configs/FeatureScenes/WheelFeatureConfig.asset`.

## SailScene

- Scene file: `Assets/Scenes/SailScene.unity`
- Focus: static mast rigs (single/double/triangular) with runtime wind-load visualization.
- Controller: `SailFeatureSceneController` (validates references and binds aggregator in Awake).
- Debug UI: `SailFeatureDebugWindow` (mast count/types + force/stress tuning).
- Config asset: `Assets/Source/Configs/FeatureScenes/SailFeatureConfig.asset`.

## Workflow reminder

1. Build mechanic in dedicated feature scene.
2. Add runtime debug menu (controls + reset + save config).
3. Bind dependencies through ship aggregator(s).
4. Integrate into gameplay ship only after validation in feature scene.
