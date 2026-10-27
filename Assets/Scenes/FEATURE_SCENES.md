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

## AttributesScene

- Scene file: `Assets/Scenes/AttributesScene.unity` (no water; in Build Settings).
- Rebuild: **RumOverboard → Attributes → ★ Build Sandbox** (sample assets + registry + scene).
- Focus: entity attributes, flags, timed effects, effect zones (Cold / Wet), replication via `NetworkAttributes`.
- Networking: `ConnectionManager` auto host-or-client, no crew/ship prefabs; entities are scene NetworkObjects.
- Tests: **Run Sandbox Smoke** (one editor) and **Run Network Smoke (MPPM host + client)**.
- Debug UI: `AttributesDebugWindow` (F6) — edit base/resources/flags, apply/remove effects, reset, save to profile.
- Config assets: `Assets/Source/Configs/Attributes/`. Docs: `Assets/Source/Docs/Attributes.md`.

## StaminaScene

- Scene file: `Assets/Scenes/StaminaScene.unity` — the ship sandbox (brig, calm sea) built by
  **RumOverboard → Stamina → ★ Build Stamina Scene**.
- Focus: crew stamina — sprint / jump on deck, climb a mast, haul a line, swim; exhaustion and recovery.
- Debug UI: `StaminaDebugWindow` (F7, open) — live tuning, Fill/Empty, reset, save to the Player profile; F6 attributes.
- Tests: **Stamina → Run Self-Test**, **Stamina → Run Smoke (Play Mode)**. Docs: `Assets/Source/Docs/Stamina.md`.

## CrewRagdollScene — rum / intoxication

- Built by Ship Sandbox → Build Feature Scenes. Debug UI: `CrewRagdollDebugWindow` (F5) — sips, ragdoll,
  GameConfig tuning + sober-up rate (Player profile attribute).
- Intoxication is the crew attribute `Drunkenness`. Tests: **Intoxication → Run Self-Test / Run Smoke (Play Mode)**.
  Docs: `Assets/Source/Docs/Intoxication.md`.

## ItemsScene

- Scene file: `Assets/Scenes/ItemsScene.unity` — ship sandbox (calm) built by **RumOverboard → Items → ★ Build Items Scene**.
- Focus: physical items — pick up / carry / put down / throw, chest storage, breaking, floating, bottle / spyglass / compass.
- Debug UI: `ItemsDebugWindow` (F8, open) — spawn, give, damage, break, refill; item profiles in F6.
- Tests: **Items → Run Self-Test**, **Items → Run Smoke (Play Mode)**. Docs: `Assets/Source/Docs/Items.md`.

## FireScene

- Scene file: `Assets/Scenes/FireScene.unity` — ship sandbox (calm) built by **RumOverboard → Fire → ★ Build Fire Scene**.
- Focus: burning items and spread, powder explosions, lanterns, bucket firefighting (water cask / sea refill).
- Debug UI: `FireDebugWindow` (F9, open) — ignite / put out / explode, FireConfig live tuning, reset, save to config; F8 items, F6 attributes.
- Config asset: `Assets/Source/Configs/Fire/FireConfig.asset`.
- Tests: **Fire → Run Self-Test**, **Fire → Run Smoke (Play Mode)**. Docs: `Assets/Source/Docs/Fire.md`.

## PlaytestScene — everything together

- Scene file: `Assets/Scenes/PlaytestScene.unity` — built by **RumOverboard → Playtest → ★ Build Playtest Scene**:
  the ship sandbox (calm) + items, chest, loot in the water, fire pile, powder, lantern, buckets, water cask.
- All debug windows F2–F9 (closed), F1 help + network status / reconnect (`PlaytestBoot`).
- Player build: **Playtest → Build Windows Player** → `Builds/Playtest/RumOverboard.exe` (starts in this scene) + .bat launchers
  incl. an automated two-window network self-test. Docs: `Assets/Source/Docs/Playtest.md`.

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
