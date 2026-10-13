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

## SailFurlScene / ShipStatusScene

- Scene files: `Assets/Scenes/SailFurlScene.unity` (calm, F7 open), `Assets/Scenes/ShipStatusScene.unity`
  (moderate sea, F8 open) — menu **RumOverboard → Ship Sandbox → Build Feature Scenes**.
- Focus: furling / casting loose sails on the yards (E from the top or while climbing), the ship status
  HUD widget (wind vs bow, rudder, sails, lines; Tab toggles). Rope tension visuals are tuned in F2.
- Aggregator: `NetworkShip` (furl arrays + API; widget reads replicated state only).
- Debug UI: `SailFurlDebugWindow` (F7), `ShipStatusDebugWindow` (F8).
- Config assets: `Assets/Source/Configs/FeatureScenes/SailFurlConfig.asset`, `ShipStatusConfig.asset`.
- Details: `Assets/Source/Docs/ShipSandbox.md`.

## WorldMapScene

- Scene file: `Assets/Scenes/WorldMapScene.unity` (menu **RumOverboard → World → Build World Map Scene**).
- Focus: procedural world (checkpoint islands + clustered ocean of rising difficulty), start moored at
  the first island, chart table with fog-of-war map, mooring bollards.
- Aggregator: `NetworkWorld` (scene NetworkObject; seed/progress/mooring replicated, generation local).
- Debug UI: `WorldDebugWindow` (F6).
- Config asset: `Assets/Source/Configs/FeatureScenes/WorldGenConfig.asset`.
- Details: `Assets/Source/Docs/WorldMap.md`.

## MainMenu / Lobby (frontend)

- Scene files: `Assets/Scenes/MainMenu.unity`, `Assets/Scenes/Lobby.unity`
  (menu **RumOverboard → Frontend → Build Menu Scenes**; also sets Boot → MainMenu and build order).
- Focus: boot menu (new / load voyage as host, join by code or list, offline solo), lobby room with
  ready flags, voyage saves, pause menu in the world.
- Session owner: `GameSession` (DontDestroyOnLoad), UI: `FrontendUI` (built in code), lobby state:
  `NetworkLobby` (scene NetworkObject), lobby backend: `ILobbyDirectory` (Photon now, Steam later).
- Details: `Assets/Source/Docs/Frontend.md`.

## Workflow reminder

1. Build mechanic in dedicated feature scene.
2. Add runtime debug menu (controls + reset + save config).
3. Bind dependencies through ship aggregator(s).
4. Integrate into gameplay ship only after validation in feature scene.
