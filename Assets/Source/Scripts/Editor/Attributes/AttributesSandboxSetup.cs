using System.Collections.Generic;
using Fusion;
using RumOverboard.Core;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Attributes
{
    /// <summary>
    /// Attributes feature sandbox (no water — attributes don't depend on it):
    ///   • sample definitions / flags / effects / profiles in Assets/Source/Configs/Attributes
    ///     (created only if missing — your edits are never overwritten),
    ///   • registry rebuild + codegen,
    ///   • scene Assets/Scenes/AttributesScene.unity: player capsule, barrel, powder barrel, board,
    ///     static crate (all scene NetworkObjects with NetworkAttributes), a Cold zone and a Wet zone,
    ///     ConnectionManager (auto host-or-client), AttributesNetSmoke, F6 debug window.
    /// Menu: RumOverboard ▸ Attributes ▸ ★ Build Sandbox.
    /// </summary>
    public static class AttributesSandboxSetup
    {
        private const string Root = AttributeRegistryBuilder.ConfigFolder;
        public const string ScenePath = "Assets/Scenes/AttributesScene.unity";
        private const string GameConfigPath = "Assets/Source/Configs/GameConfig.asset";
        private const string PlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";

        [MenuItem("RumOverboard/Attributes/★ Build Sandbox", priority = 40)]
        public static void BuildSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateSampleContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            BuildScene();
        }

        [MenuItem("RumOverboard/Attributes/Create Sample Content", priority = 41)]
        public static void CreateSampleContent()
        {
            // ── attributes ──
            var stamina = Attribute("Stamina", AttributeKind.Resource, 100f, "Action resource: running, swimming, climbing, hauling.");
            var grog = Attribute("Grog", AttributeKind.Resource, 100f, "Sanity reserve; below the campaign threshold hallucinations start.");
            var mass = Attribute("Mass", AttributeKind.Stat, 10f, "Physical mass (kg) applied to the Rigidbody.");
            var integrity = Attribute("Integrity", AttributeKind.Resource, 50f, "Breakable objects are destroyed at 0.");

            // ── flags ──
            var flammable = Flag("Flammable", "Can catch fire.");
            var floating = Flag("Floating", "Floats in water.");
            var critical = Flag("CriticalItem", "Map / compass / spyglass / boards / cloth / lights — permanent loss matters.");
            var burning = Flag("Burning", "On fire right now.");
            var wet = Flag("Wet", "Soaked: heavier, doesn't ignite.");

            // ── effects ──
            Effect("WetFx", 10f, EffectStacking.Refresh, 1, e =>
            {
                e.Modifiers.Add(new EffectModifier { Attribute = mass, ValuePerStack = 2f });
                e.GrantedFlags.Add(wet);
            });
            Effect("BurningFx", 8f, EffectStacking.Stack, 3, e =>
            {
                e.GrantedFlags.Add(burning);
                e.Periodic.Add(new EffectPeriodic { Resource = integrity, PerSecondPerStack = -5f });
            });
            // ── crew stamina tuning (host-only stats: not replicated, editable live in F6 / F7) ──
            var regen = Tuning("StaminaRegen", 12f, "Stamina restored per second once the regen delay has passed.");
            var regenDelay = Tuning("StaminaRegenDelay", 1f, "Seconds without spending before stamina starts to regenerate.");
            var recover = Tuning("StaminaRecoverThreshold", 30f, "Exhausted (stamina hit zero) until stamina is back to this amount.");
            var sprintCost = Tuning("StaminaSprintCost", 10f, "Per second while sprinting (deck or rail).");
            var jumpCost = Tuning("StaminaJumpCost", 8f, "Per jump / swim lunge. Can't jump with less than this.");
            var climbCost = Tuning("StaminaClimbCost", 6f, "Per second while moving on a rail (x2 when sprint-climbing).");
            var swimCost = Tuning("StaminaSwimCost", 5f, "Per second while swimming at full effort (30% treading water).");
            var haulCost = Tuning("StaminaHaulCost", 8f, "Per second while hauling, or walking with a loaded line, × line load.");
            var exhaustedSpeed = Tuning("StaminaExhaustedSpeed", 0.5f, "Climb / swim speed multiplier while exhausted.", max: 1f);
            var exhausted = Flag("Exhausted", "Stamina ran out: no sprint, no jump, slower climbing/swimming until recovered.");

            // ── crew intoxication (was PlayerNetState.Drunkenness) ──
            var drunkenness = CreateIfMissing<AttributeDefinition>("Definitions", "Drunkenness", d =>
            {
                d.Kind = AttributeKind.Resource; // value = cap (1), current = intoxication 0..1
                d.DefaultValue = 1f;
                d.Max = 1f;
                d.StartFull = false;              // sober at spawn
                d.Replicated = true;              // camera sway / HUD on every peer
                d.Description = "Intoxication 0..1 (current). Raised by sips of rum (GameConfig.RumSipIntoxication); " +
                                "drives walk wobble, the ragdoll baseline, camera sway and the HUD meter.";
            });
            var soberRate = Tuning("DrunkSoberRate", 0f, "Intoxication lost per second while not drinking (0 = stays drunk, as before).", max: 1f);

            // Cold: GDD §20.1 "increased stamina pressure" — slower recovery, not a drain at rest.
            var cold = Effect("ColdFx", 2f, EffectStacking.Refresh, 1, e =>
            {
                e.Modifiers.Add(new EffectModifier { Attribute = regen, ValuePerStack = -6f });
            });
            MigrateColdFx(cold, stamina, regen);

            // ── profiles ──
            var player = Profile("Player", null,
                new() { new(stamina, 100f), new(grog, 100f), new(mass, 80f) },
                new(),
                new() { new DisplayData { DisplayName = "Sailor" } });
            EnsureEntries(player, new()
            {
                new(regen, 12f), new(regenDelay, 1f), new(recover, 30f), new(sprintCost, 10f), new(jumpCost, 8f),
                new(climbCost, 6f), new(swimCost, 5f), new(haulCost, 8f), new(exhaustedSpeed, 0.5f),
                new(drunkenness, 1f), new(soberRate, 0f),
            });
            _ = exhausted;

            var barrel = Profile("Barrel", null,
                new() { new(mass, 40f), new(integrity, 30f) },
                new() { flammable, floating },
                new()
                {
                    new DisplayData { DisplayName = "Barrel" },
                    new FlammableData(),
                    new BreakableData(),
                });

            Profile("PowderBarrel", barrel,
                new() { new(mass, 60f), new(integrity, 15f) },
                new(),
                new() { new DisplayData { DisplayName = "Powder barrel", Description = "Explodes when it burns." } });

            Profile("Board", null,
                new() { new(mass, 5f), new(integrity, 10f) },
                new() { flammable, floating, critical },
                new() { new DisplayData { DisplayName = "Board" } });

            Profile("Crate", null,
                new() { new(integrity, 80f) },
                new() { flammable },
                new() { new DisplayData { DisplayName = "Crate (static)" }, new BreakableData() });

            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── scene ─────────────────────────

        private static void BuildScene()
        {
            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (GameObject go in scene.GetRootGameObjects())
                Object.DestroyImmediate(go);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.SetPositionAndRotation(new Vector3(0f, 6f, -12f), Quaternion.Euler(22f, 0f, 0f));

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            Entity("Player", PrimitiveType.Capsule, new Vector3(-6f, 1f, 0f), Vector3.one, "Player", physical: true);
            Entity("Barrel", PrimitiveType.Cylinder, new Vector3(-2f, 1f, 0f), new Vector3(0.8f, 0.6f, 0.8f), "Barrel", physical: true);
            Entity("PowderBarrel", PrimitiveType.Cylinder, new Vector3(0.5f, 1f, 0f), new Vector3(0.8f, 0.6f, 0.8f), "PowderBarrel", physical: true);
            Entity("Board", PrimitiveType.Cube, new Vector3(3f, 0.5f, 0f), new Vector3(2f, 0.1f, 0.3f), "Board", physical: true);
            Entity("Crate (static)", PrimitiveType.Cube, new Vector3(6f, 0.5f, 0f), Vector3.one, "Crate", physical: false);

            Zone("ColdZone", "ColdFx", new Vector3(-6f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
            Zone("WetZone", "WetFx", new Vector3(-0.75f, 1.5f, 0f), new Vector3(4.5f, 3f, 3f)); // barrels only, not the board

            var debug = new GameObject("AttributesDebugWindow").AddComponent<AttributesDebugWindow>();
            var so = new SerializedObject(debug);
            so.FindProperty("visible").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            CreateConnectionManager();
            var netSmoke = new GameObject("AttributesNetSmoke").AddComponent<AttributesNetSmoke>();
            var nso = new SerializedObject(netSmoke);
            nso.FindProperty("itemCatalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<RumOverboard.Gameplay.Items.ItemCatalog>("Assets/Source/Configs/Items/ItemCatalog.asset");
            nso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();
            Debug.Log($"[Attributes] Sandbox scene saved: {ScenePath}. Play hosts a Fusion session (2nd peer joins as client) → F6 window.");
        }

        /// <summary>
        /// Host authority like the rest of the game: Play starts AutoHostOrClient (offline Single if
        /// Photon is unreachable). No crew/ship prefabs — the scene is only about attributes.
        /// </summary>
        private static void CreateConnectionManager()
        {
            var cm = new GameObject("ConnectionManager").AddComponent<ConnectionManager>();
            var so = new SerializedObject(cm);
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            so.FindProperty("autoStartOnPlay").boolValue = true;
            so.FindProperty("autoStartMode").intValue = (int)GameMode.AutoHostOrClient;
            so.FindProperty("spawnShipOnSessionStart").boolValue = false;

            // Real crew members (NetworkPlayer prefab with stamina attributes), spawned in front of the
            // entities and away from the zones — the network smoke checks their stamina replication.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            NetworkObject prefabObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (prefabObject != null)
                global::Source.Scripts.Editor.FeatureSceneSetupEditor.SetNetworkPrefabRef(so.FindProperty("playerPrefab"), prefabObject);
            SerializedProperty points = so.FindProperty("spawnPoints");
            points.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var spawn = new GameObject($"CrewSpawn{i}");
                spawn.transform.position = new Vector3(-1f + 2f * i, 1f, -4f);
                points.GetArrayElementAtIndex(i).objectReferenceValue = spawn.transform;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Fusion syncs the active scene by build index, so the scene must be enabled in Build Settings.</summary>
        private static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == ScenePath);
            if (index >= 0 && scenes[index].enabled) return;
            if (index >= 0) scenes[index].enabled = true;
            else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets(); // writes ProjectSettings/EditorBuildSettings.asset
        }

        private static void Entity(string name, PrimitiveType shape, Vector3 position, Vector3 scale, string profileName, bool physical)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            if (physical)
            {
                // Kinematic: positions aren't replicated in this sandbox (no NetworkRigidbody),
                // bodies only exist so the host's trigger zones see them.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }

            var attributes = go.AddComponent<AttributesComponent>();
            var so = new SerializedObject(attributes);
            so.FindProperty("profile").objectReferenceValue = Load<AttributeProfile>("Profiles", profileName);
            so.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkAttributes>();
        }

        private static void Zone(string name, string effectName, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<EffectZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("effect").objectReferenceValue = Load<EffectDefinition>("Effects", effectName);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── assets (create if missing) ─────────────────────────

        internal static T Load<T>(string folder, string name) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>($"{Root}/{folder}/{name}.asset");

        internal static T CreateIfMissing<T>(string folder, string name, System.Action<T> init) where T : ScriptableObject
        {
            T existing = Load<T>(folder, name);
            if (existing != null) return existing;

            AttributeRegistryBuilder.EnsureFolder($"{Root}/{folder}");
            var asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, $"{Root}/{folder}/{name}.asset");
            return asset;
        }

        internal static AttributeDefinition Attribute(string name, AttributeKind kind, float defaultValue, string description) =>
            CreateIfMissing<AttributeDefinition>("Definitions", name, d =>
            {
                d.Kind = kind;
                d.DefaultValue = defaultValue;
                d.Description = description;
            });

        internal static AttributeDefinition Tuning(string name, float value, string description, float max = 1000f) =>
            CreateIfMissing<AttributeDefinition>("Definitions", name, d =>
            {
                d.Kind = AttributeKind.Stat;
                d.DefaultValue = value;
                d.Max = max;
                d.Replicated = false; // host-only: clients never simulate stamina
                d.Description = description;
            });

        /// <summary>Adds entries the profile doesn't list yet (existing base values are kept).</summary>
        internal static void EnsureEntries(AttributeProfile profile, List<AttributeEntry> wanted)
        {
            var entries = new List<AttributeEntry>(profile.Attributes);
            int added = 0;
            foreach (AttributeEntry w in wanted)
            {
                if (entries.Exists(e => e.Attribute == w.Attribute)) continue;
                entries.Add(w);
                added++;
            }
            if (added == 0) return;
            profile.EditorSetup(profile.Parent, entries, new List<FlagDefinition>(profile.Flags), new List<IEntityData>(profile.Data));
            EditorUtility.SetDirty(profile);
            Debug.Log($"[Attributes] Profile '{profile.name}': added {added} attribute(s).", profile);
        }

        /// <summary>
        /// The first sample ColdFx drained stamina directly (−4/s). Stamina design: cold slows recovery
        /// instead. Converts only that original, unedited shape.
        /// </summary>
        private static void MigrateColdFx(EffectDefinition cold, AttributeDefinition stamina, AttributeDefinition regen)
        {
            bool original = cold.Modifiers.Count == 0 && cold.Periodic.Count == 1 &&
                            cold.Periodic[0].Resource == stamina && Mathf.Approximately(cold.Periodic[0].PerSecondPerStack, -4f);
            if (!original) return;
            cold.Periodic.Clear();
            cold.Modifiers.Add(new EffectModifier { Attribute = regen, ValuePerStack = -6f });
            cold.Description = "Cold region: stamina regenerates half as fast.";
            EditorUtility.SetDirty(cold);
            Debug.Log("[Attributes] ColdFx migrated: stamina drain → StaminaRegen −6.", cold);
        }

        internal static FlagDefinition Flag(string name, string description) =>
            CreateIfMissing<FlagDefinition>("Flags", name, f => f.Description = description);

        internal static EffectDefinition Effect(string name, float duration, EffectStacking stacking, int maxStacks,
                                               System.Action<EffectDefinition> setup) =>
            CreateIfMissing<EffectDefinition>("Effects", name, e =>
            {
                e.Duration = duration;
                e.Stacking = stacking;
                e.MaxStacks = maxStacks;
                setup(e);
            });

        internal static AttributeProfile Profile(string name, AttributeProfile parent, List<AttributeEntry> entries,
                                                List<FlagDefinition> flags, List<IEntityData> data) =>
            CreateIfMissing<AttributeProfile>("Profiles", name, p => p.EditorSetup(parent, entries, flags, data));
    }
}
