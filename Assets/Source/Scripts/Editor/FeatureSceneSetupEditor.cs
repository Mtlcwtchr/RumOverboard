using System.Reflection;
using Fusion;
using Fusion.Editor;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.Ocean.FeatureScenes;
using RumOverboard.Gameplay.Ocean.FeatureScenes.Sails;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Source.Scripts.Editor
{
    /// <summary>
    /// Editor tooling that populates feature test scenes with the full hierarchy
    /// using real module prefabs extracted from NetworkShip_Reference.
    /// Each feature is isolated on a static platform — no falling.
    /// Menu: RumOverboard → Feature Scenes → Setup *.
    /// </summary>
    public static class FeatureSceneSetupEditor
    {
        private const string HullPrefabPath = "Assets/Source/Environment/Prefabs/ShipModules/Hull/ShipHullModule.prefab";
        private const string WheelPrefabPath = "Assets/Source/Environment/Prefabs/ShipModules/Wheel/ShipWheelModule.prefab";
        private const string FrontMastPrefabPath = "Assets/Source/Environment/Prefabs/ShipModules/Masts/FrontMastModule.prefab";
        private const string MainMastPrefabPath = "Assets/Source/Environment/Prefabs/ShipModules/Masts/MainMastModule.prefab";
        private const string BackMastPrefabPath = "Assets/Source/Environment/Prefabs/ShipModules/Masts/BackMastModule.prefab";

        private const string ConfigAssetPath = "Assets/Source/Configs/GameConfig.asset";
        private const string PlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";

        // ─────────────────────────────────────────────────────────────
        //  Hull — on water with ocean + buoyancy
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/Setup HullScene (current scene)")]
        public static void SetupHullScene()
        {
            if (!ConfirmActiveScene("HullScene")) return;
            ClearScene();

            EnsureLight();
            EnsureMainCamera(new Vector3(0f, 7f, -18f), Quaternion.Euler(16f, 0f, 0f));
            var (waveField, windSystem, surfaceRenderer) = EnsureOceanSystem();

            // Hull ship floating freely — module owns Rigidbody + Buoyancy
            var ship = new GameObject("HullFeatureShip");
            ship.transform.position = new Vector3(0f, 0.8f, 0f);
            Undo.RegisterCreatedObjectUndo(ship, "Create HullFeatureShip");

            InstantiateModulePrefab(HullPrefabPath, "ShipHullModule", ship);
            ship.AddComponent<ShipModuleJoint>();
            var aggregator = ship.AddComponent<ShipFeatureAggregator>();

            // Networking
            EnsureConnectionManager(new Vector3(0f, 3f, 0f));

            // Debug
            var gizmosGo = CreateGO("OceanFieldDebugGizmos");
            var gizmos = gizmosGo.AddComponent<OceanFieldDebugGizmos>();
            CreateGO("HullFeatureDebugWindow").AddComponent<HullFeatureDebugWindow>();

            var root = CreateGO("HullFeatureSceneRoot");
            var controller = root.AddComponent<HullFeatureSceneController>();
            WireHullController(controller, waveField, windSystem, surfaceRenderer, aggregator);
            WireGizmos(gizmos, waveField, windSystem);

            MarkDirtyAndSave();
            Debug.Log("[FeatureSceneSetup] HullScene complete.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Wheel — on fixed platform, ocean for water flow only
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/Setup WheelScene (current scene)")]
        public static void SetupWheelScene()
        {
            if (!ConfirmActiveScene("WheelScene")) return;
            ClearScene();

            EnsureLight();
            EnsureMainCamera(new Vector3(0f, 2.2f, -8f), Quaternion.Euler(10f, 0f, 0f));

            // Ocean for water flow data on rudder (no buoyancy needed)
            var (waveField, windSystem, surfaceRenderer) = EnsureOceanSystem();

            // Static platform is VISUAL/COLLISION only; modules are parented to a separate scale=1 root.
            CreatePlatform(new Vector3(0f, 0f, 0f), new Vector3(8f, 0.3f, 12f));
            var rigRoot = CreateGO("WheelFeatureRig");
            rigRoot.transform.position = new Vector3(0f, 0.15f, 0f);

            // Wheel module on rig root — no inherited non-uniform scale from platform.
            var wheelGo = InstantiateModulePrefab(WheelPrefabPath, "ShipWheelModule", rigRoot);
            if (wheelGo != null)
                wheelGo.transform.localPosition = Vector3.zero;

            // Networking
            EnsureConnectionManager(new Vector3(0f, 1.5f, -3f));

            // Debug
            var gizmosGo = CreateGO("OceanFieldDebugGizmos");
            var gizmos = gizmosGo.AddComponent<OceanFieldDebugGizmos>();
            CreateGO("WheelFeatureDebugWindow").AddComponent<WheelFeatureDebugWindow>();

            var root = CreateGO("WheelFeatureSceneRoot");
            var controller = root.AddComponent<WheelFeatureSceneController>();

            // Aggregator must be on the same GameObject as WheelFeatureStandaloneSystem/Rigidbody.
            ShipFeatureAggregator aggregator = null;
            if (wheelGo != null)
                aggregator = wheelGo.AddComponent<ShipFeatureAggregator>();

            WireWheelController(controller, waveField, windSystem, surfaceRenderer, aggregator);
            WireGizmos(gizmos, waveField, windSystem);

            MarkDirtyAndSave();
            Debug.Log("[FeatureSceneSetup] WheelScene complete.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Sail — masts on fixed platform, wind only (no ocean, no hull)
        // ─────────────────────────────────────────────────────────────

        [MenuItem("RumOverboard/Feature Scenes/Setup SailScene (current scene)")]
        public static void SetupSailScene()
        {
            if (!ConfirmActiveScene("SailScene")) return;
            ClearScene();

            EnsureLight();
            EnsureMainCamera(new Vector3(0f, 8f, -20f), Quaternion.Euler(18f, 0f, 0f));

            // Wind only — no ocean needed for sails
            var windGo = CreateGO("WindSystem");
            var windSystem = windGo.AddComponent<OceanWindSystem>();
            var windSo = new SerializedObject(windSystem);
            var windEnabledProp = windSo.FindProperty("windEnabled");
            if (windEnabledProp != null) windEnabledProp.boolValue = true;
            var baseStrProp = windSo.FindProperty("baseStrength");
            if (baseStrProp != null) baseStrProp.floatValue = 12f;
            windSo.ApplyModifiedProperties();

            // Static platform is VISUAL/COLLISION only; mast modules go under scale=1 root.
            CreatePlatform(new Vector3(0f, 0f, 0f), new Vector3(16f, 0.3f, 24f));
            var mastRigRoot = CreateGO("SailMastRigRoot");
            mastRigRoot.transform.position = new Vector3(0f, 0.15f, 0f);

            // All 3 mast modules on scale=1 parent.
            var front = InstantiateModulePrefab(FrontMastPrefabPath, "FrontMastModule", mastRigRoot);
            var main = InstantiateModulePrefab(MainMastPrefabPath, "MainMastModule", mastRigRoot);
            var back = InstantiateModulePrefab(BackMastPrefabPath, "BackMastModule", mastRigRoot);

            if (front != null) front.transform.localPosition = new Vector3(0f, 0f, 7f);
            if (main != null) main.transform.localPosition = new Vector3(0f, 0f, 0f);
            if (back != null) back.transform.localPosition = new Vector3(0f, 0f, -6f);

            // Networking
            EnsureConnectionManager(new Vector3(2f, 1.5f, 0f));

            // Debug
            CreateGO("SailFeatureDebugWindow").AddComponent<SailFeatureDebugWindow>();

            var root = CreateGO("SailFeatureSceneRoot");
            var controller = root.AddComponent<SailFeatureSceneController>();
            var ctrlSo = new SerializedObject(controller);
            ctrlSo.FindProperty("windSystem").objectReferenceValue = windSystem;
            ctrlSo.ApplyModifiedProperties();

            MarkDirtyAndSave();
            Debug.Log("[FeatureSceneSetup] SailScene complete.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Infrastructure
        // ─────────────────────────────────────────────────────────────

        private static GameObject CreatePlatform(Vector3 position, Vector3 size)
        {
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Platform";
            platform.transform.position = position;
            platform.transform.localScale = size;
            platform.isStatic = true;

            // Make it a nice dark wood color
            var renderer = platform.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.35f, 0.22f, 0.12f);
                renderer.sharedMaterial = mat;
            }

            Undo.RegisterCreatedObjectUndo(platform, "Create Platform");
            return platform;
        }

        private static GameObject InstantiateModulePrefab(string prefabPath, string instanceName, GameObject parent)
        {
            var existing = parent.transform.Find(instanceName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[FeatureSceneSetup] Module prefab not found at '{prefabPath}'. " +
                                 "Run RumOverboard → Setup → Ship Features to extract it first.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = instanceName;
            instance.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {instanceName}");
            return instance;
        }

        // ─────────────────────────────────────────────────────────────
        //  ConnectionManager
        // ─────────────────────────────────────────────────────────────

        private static void EnsureConnectionManager(Vector3 spawnPointPosition)
        {
            var cmGo = CreateGO("ConnectionManager");
            var cm = cmGo.AddComponent<ConnectionManager>();

            var so = new SerializedObject(cm);

            var configAsset = AssetDatabase.LoadAssetAtPath<RumOverboard.Core.GameConfig>(ConfigAssetPath);
            if (configAsset != null)
                so.FindProperty("config").objectReferenceValue = configAsset;

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab != null)
            {
                var networkObj = playerPrefab.GetComponent<NetworkObject>();
                if (networkObj != null)
                {
                    var prefabProp = so.FindProperty("playerPrefab");
                    if (prefabProp != null)
                        SetNetworkPrefabRef(prefabProp, networkObj);
                }
            }

            so.FindProperty("autoStartOnPlay").boolValue = true;
            so.FindProperty("autoStartMode").enumValueIndex = (int)GameMode.AutoHostOrClient;
            so.FindProperty("spawnShipOnSessionStart").boolValue = false;
            so.ApplyModifiedProperties();

            // Spawn point
            var spawnGo = CreateGO("FeatureSpawnPoint");
            spawnGo.transform.position = spawnPointPosition;

            so = new SerializedObject(cm);
            var spawnsProp = so.FindProperty("spawnPoints");
            spawnsProp.arraySize = 1;
            spawnsProp.GetArrayElementAtIndex(0).objectReferenceValue = spawnGo.transform;
            so.ApplyModifiedProperties();
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────

        private static void ClearScene()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            for (int i = roots.Length - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(roots[i]);
        }

        private static bool ConfirmActiveScene(string expectedName)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name == expectedName) return true;
            return EditorUtility.DisplayDialog("Feature Scene Setup",
                $"Active scene is \"{scene.name}\", expected \"{expectedName}\".\nProceed anyway?",
                "Yes", "Cancel");
        }

        private static GameObject CreateGO(string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            return go;
        }

        private static void EnsureLight()
        {
            var lightGo = CreateGO("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void EnsureMainCamera(Vector3 pos, Quaternion rot)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.transform.position = pos;
            cam.transform.rotation = rot;
            Undo.RegisterCreatedObjectUndo(camGo, "Create Main Camera");

#if FUSION2
            camGo.AddComponent<RumOverboard.Gameplay.PlayerCameraRig>();
#endif
        }

        private static (OceanWaveField, OceanWindSystem, OceanSurfaceRenderer) EnsureOceanSystem()
        {
            var oceanRoot = CreateGO("OceanSystem");
            var waveField = oceanRoot.AddComponent<OceanWaveField>();
            oceanRoot.AddComponent<OceanCurrentSystem>();
            oceanRoot.AddComponent<OceanDepthProvider>();
            var windSystem = oceanRoot.AddComponent<OceanWindSystem>();
            oceanRoot.AddComponent<OceanSeaStateController>();
            var surfaceRenderer = oceanRoot.AddComponent<OceanSurfaceRenderer>();
            return (waveField, windSystem, surfaceRenderer);
        }

        // ─────────────────────────────────────────────────────────────
        //  Wiring
        // ─────────────────────────────────────────────────────────────

        private static void WireHullController(HullFeatureSceneController ctrl,
            OceanWaveField waveField, OceanWindSystem wind, OceanSurfaceRenderer surface,
            ShipFeatureAggregator aggregator)
        {
            var so = new SerializedObject(ctrl);
            so.FindProperty("waveField").objectReferenceValue = waveField;
            so.FindProperty("windSystem").objectReferenceValue = wind;
            so.FindProperty("surfaceRenderer").objectReferenceValue = surface;
            so.FindProperty("shipAggregator").objectReferenceValue = aggregator;
            so.ApplyModifiedProperties();
        }

        private static void WireWheelController(WheelFeatureSceneController ctrl,
            OceanWaveField waveField, OceanWindSystem wind, OceanSurfaceRenderer surface,
            ShipFeatureAggregator aggregator)
        {
            var so = new SerializedObject(ctrl);
            so.FindProperty("waveField").objectReferenceValue = waveField;
            so.FindProperty("windSystem").objectReferenceValue = wind;
            so.FindProperty("surfaceRenderer").objectReferenceValue = surface;
            so.FindProperty("shipAggregator").objectReferenceValue = aggregator;
            so.ApplyModifiedProperties();
        }

        private static void WireGizmos(OceanFieldDebugGizmos gizmos,
            OceanWaveField waveField, OceanWindSystem wind)
        {
            var so = new SerializedObject(gizmos);
            so.FindProperty("waveField").objectReferenceValue = waveField;
            so.FindProperty("windSystem").objectReferenceValue = wind;
            so.ApplyModifiedProperties();
        }

        private static void SetNetworkPrefabRef(SerializedProperty prefabRefProperty, NetworkObject prefab)
        {
            var prefabGuid = NetworkObjectEditor.GetPrefabGuid(prefab);
            var fusionEditorAssembly = typeof(NetworkObjectEditor).Assembly;
            var guidDrawerType = fusionEditorAssembly.GetType("Fusion.Editor.NetworkObjectGuidDrawer");
            var setValueMethod = guidDrawerType?.GetMethod("SetValue",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (setValueMethod != null)
                setValueMethod.Invoke(null, new object[] { prefabRefProperty, prefabGuid });
        }

        private static void MarkDirtyAndSave()
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }
}
