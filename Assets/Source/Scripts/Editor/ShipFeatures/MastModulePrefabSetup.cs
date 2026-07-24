using System;
using System.Collections.Generic;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.Ocean.Features.Hull;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using RumOverboard.Gameplay.Ocean.Features.Wheel;
using UnityEditor;
using UnityEngine;

namespace Source.Scripts.Editor.ShipFeatures
{
    /// <summary>
    /// Extracts ship modules (masts + hull + wheel) from NetworkShip_Reference and reassembles NetworkShip from module prefabs.
    /// </summary>
    public static class MastModulePrefabSetup
    {
        private const string SourceNetworkShipPrefabPath = "Assets/Source/Prefabs/NetworkShip_Reference.prefab";
        private const string TargetNetworkShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";
        private const string ModulePrefabRootPath = "Assets/Source/Environment/Prefabs/ShipModules";
        private const string MastPrefabFolderPath = ModulePrefabRootPath + "/Masts";
        private const string HullPrefabFolderPath = ModulePrefabRootPath + "/Hull";
        private const string WheelPrefabFolderPath = ModulePrefabRootPath + "/Wheel";
        private const string HullPrefabName = "ShipHullModule";
        private const string WheelPrefabName = "ShipWheelModule";
        private const string HullPrefabPath = HullPrefabFolderPath + "/" + HullPrefabName + ".prefab";
        public const string WheelPrefabPath = WheelPrefabFolderPath + "/" + WheelPrefabName + ".prefab";

        /// <summary>Patterns for discovering wheel/helm visual nodes in the reference ship.</summary>
        private static readonly string[] WheelVisualPatterns = { "StylShip_Wheel", "StylShip_WheelStand*", "StylShip_Helm*" };
        private static readonly string[] RudderVisualPatterns = { "StylShip_Rudder*", "Rudder*" };

        private static readonly string[] HullAutoColliderRootNames = { "Decks", "Stairs", "Staris", "Walls", "Props" };
        private static readonly string[] ExcludedHullVisualTokens = { "wheel", "helm", "wing", "flag", "banner", "pennant", "mast", "rope", "wire", "sail" };

        private static readonly MastSpec[] Specs =
        {
            new MastSpec(
                "FrontMastModule",
                "StylShip_MastFront",
                new[] { "StylShip_MastFront", "StylShip_WireFront", "StylShip_SailFront*" },
                new[] { "StylShip_SailFront*" },
                new[] { "StylShip_MastFront_Climb", "StylShip_WireFront_Climb", "StylShip_WireFront_YardClimb" },
                new[] { "StylShip_MastFront_CrowNest", "StylShip_WireFront_Zone" },
                new[] { "StylShip_MastFront_Parts" }),
            new MastSpec(
                "MainMastModule",
                "StylShip_MastMid",
                new[] { "StylShip_MastMid", "StylShip_WireMid", "StylShip_SailMid*" },
                new[] { "StylShip_SailMid*" },
                new[] { "StylShip_MastMid_Climb", "StylShip_WireMid_Climb", "StylShip_WireMid_YardClimb" },
                new[] { "StylShip_MastMid_CrowNest", "StylShip_WireMid_Zone" },
                new[] { "StylShip_MastMid_Parts", "StylShip_Bracing_Parts" }),
            new MastSpec(
                "BackMastModule",
                "StylShip_MastBack",
                new[] { "StylShip_MastBack", "StylShip_WireBack", "StylShip_SailBack*" },
                new[] { "StylShip_SailBack*" },
                new[] { "StylShip_MastBack_Climb", "StylShip_WireBack_Climb", "StylShip_WireBack_YardClimb" },
                new[] { "StylShip_MastBack_CrowNest", "StylShip_WireBack_Zone" },
                new[] { "StylShip_MastBack_Parts" }),
        };

        [MenuItem("RumOverboard/Setup/Ship Features/Build Ship Modules (Masts + Hull + Wheel) And Apply To NetworkShip")]
        public static void BuildShipModulesAndApplyToNetworkShip()
        {
            ExtractMastModulePrefabs();
            ExtractHullModulePrefab();
            ExtractWheelModulePrefab();
            ApplyShipModulePrefabsToNetworkShip();
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Masts/Rebuild Front Mast Module (Extract + Apply)")]
        public static void RebuildFrontMastModule()
        {
            RebuildSingleMastModule(Specs[0]);
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Masts/Rebuild Mid Mast Module (Extract + Apply)")]
        public static void RebuildMidMastModule()
        {
            RebuildSingleMastModule(Specs[1]);
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Masts/Rebuild Back Mast Module (Extract + Apply)")]
        public static void RebuildBackMastModule()
        {
            RebuildSingleMastModule(Specs[2]);
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Extract Mast Module Prefabs From NetworkShip Reference")]
        public static void ExtractMastModulePrefabs()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Prefabs");
            EnsureFolder(ModulePrefabRootPath);
            EnsureFolder(MastPrefabFolderPath);

            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing source ship prefab at '{sourcePath}'.");
                return;
            }

            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                Transform hullRoot = FindChildByName(shipRoot.transform, "Hull");
                Transform mastsColliderRoot = FindChildByName(shipRoot.transform, "Masts");
                Transform mastVisualRoot = visualRoot != null ? visualRoot : (hullRoot != null ? hullRoot : shipRoot.transform);
                Transform climbRoot = FindChildByName(shipRoot.transform, "GameplayClimbZones");
                Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");

                if (climbRoot == null)
                    climbRoot = mastsColliderRoot != null ? mastsColliderRoot : mastVisualRoot;
                if (interactionRoot == null)
                    interactionRoot = mastsColliderRoot != null ? mastsColliderRoot : mastVisualRoot;

                if (mastVisualRoot == null)
                {
                    Debug.LogError("[MastModulePrefabSetup] Mast visual source root not found in NetworkShip prefab.");
                    return;
                }

                for (int i = 0; i < Specs.Length; i++)
                {
                    BuildSingleMastPrefab(Specs[i], mastVisualRoot, climbRoot, interactionRoot, mastsColliderRoot);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[MastModulePrefabSetup] Mast module prefabs extracted from '{sourcePath}' successfully.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Extract Hull Module Prefab From NetworkShip Reference")]
        public static void ExtractHullModulePrefab()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Prefabs");
            EnsureFolder(ModulePrefabRootPath);
            EnsureFolder(HullPrefabFolderPath);

            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing source ship prefab at '{sourcePath}'.");
                return;
            }

            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                Transform hullColliderRoot = FindChildByName(shipRoot.transform, "Hull");

                if (visualRoot == null)
                {
                    Debug.LogError("[MastModulePrefabSetup] VisualShip not found in NetworkShip prefab. Cannot extract hull visual.");
                    return;
                }

                Transform autoCollidersRoot = FindChildByName(shipRoot.transform, "AutoColliders");

                GameObject moduleRoot = new GameObject(HullPrefabName);
                try
                {
                    moduleRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    moduleRoot.transform.localScale = Vector3.one;

                    Transform hullVisualRoot = CreateChild(moduleRoot.transform, "Visual");
                    Transform hullSolidRoot = CreateChild(moduleRoot.transform, "GameplaySolidColliders");
                    Transform hullTraversalRoot = CreateChild(moduleRoot.transform, "GameplayTraversalTriggers");
                    Transform hullInteractionRoot = CreateChild(moduleRoot.transform, "GameplayInteractionZones");

                    // Visual: always clone VisualShip, then strip non-hull nodes (masts, sails, wheel, etc.)
                    Transform visualClone = UnityEngine.Object.Instantiate(visualRoot.gameObject).transform;
                    visualClone.name = visualRoot.name;
                    visualClone.SetParent(hullVisualRoot, false);
                    RemoveNodesByTokens(hullVisualRoot, ExcludedHullVisualTokens);

                    // Colliders: take from both Hull and AutoColliders
                    if (hullColliderRoot != null)
                    {
                        Transform hullColClone = UnityEngine.Object.Instantiate(hullColliderRoot.gameObject).transform;
                        hullColClone.name = hullColliderRoot.name;
                        hullColClone.SetParent(hullSolidRoot, false);
                    }

                    if (autoCollidersRoot != null)
                    {
                        CloneNamedChildren(autoCollidersRoot, hullSolidRoot, HullAutoColliderRootNames);
                    }

                    if (hullColliderRoot == null && autoCollidersRoot == null)
                    {
                        CloneSourceRootWithoutMasts(shipRoot.transform, "GameplaySolidColliders", hullSolidRoot);
                    }

                    // Strip non-hull nodes from colliders too.
                    RemoveNodesByTokens(hullSolidRoot, ExcludedHullVisualTokens);

                    // Hull module owns its buoyancy — add Rigidbody + ShipBuoyancyController directly on the module.
                    var hullRb = moduleRoot.AddComponent<Rigidbody>();
                    hullRb.mass = 8500f;
                    hullRb.linearDamping = 0.05f;
                    hullRb.angularDamping = 0.2f;
                    hullRb.centerOfMass = new Vector3(0f, -0.8f, 0f);
                    hullRb.interpolation = RigidbodyInterpolation.Interpolate;

                    moduleRoot.AddComponent<ShipBuoyancyController>();
                    moduleRoot.AddComponent<ShipDeckMotionProvider>();

                    var hullModule = moduleRoot.AddComponent<ShipHullModule>();
                    hullModule.Configure(HullPrefabName, hullVisualRoot, hullSolidRoot, hullTraversalRoot, hullInteractionRoot);

                    PrefabUtility.SaveAsPrefabAsset(moduleRoot, HullPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(moduleRoot);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[MastModulePrefabSetup] Hull module prefab extracted from '{sourcePath}' successfully.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Extract Wheel Module Prefab From NetworkShip Reference")]
        public static void ExtractWheelModulePrefab()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Prefabs");
            EnsureFolder(ModulePrefabRootPath);
            EnsureFolder(WheelPrefabFolderPath);

            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing source ship prefab at '{sourcePath}'.");
                return;
            }

            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                if (visualRoot == null)
                    visualRoot = shipRoot.transform;

                GameObject moduleRoot = new GameObject(WheelPrefabName);
                try
                {
                    moduleRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    moduleRoot.transform.localScale = Vector3.one;

                    Transform wheelVisualRoot = CreateChild(moduleRoot.transform, "Visual");
                    Transform wheelSolidRoot = CreateChild(moduleRoot.transform, "GameplaySolidColliders");
                    Transform wheelInteractionRoot = CreateChild(moduleRoot.transform, "GameplayInteractionZones");

                    // Clone wheel/helm visual parts
                    Transform wheelModel = null;
                    Transform standAnchor = null;
                    var wheelMatches = SelectPatternMatches(visualRoot, WheelVisualPatterns);
                    CloneSelected(wheelMatches, wheelVisualRoot);

                    // Find the wheel model inside the clone
                    wheelModel = FindChildByName(wheelVisualRoot, "StylShip_Wheel");
                    if (wheelModel == null)
                        wheelModel = FindFirstByPatterns(wheelVisualRoot, new[] { "StylShip_Wheel", "*Wheel*" });

                    standAnchor = FindChildByName(wheelVisualRoot, "StylShip_WheelStand")
                                  ?? FindFirstByPatterns(wheelVisualRoot, new[] { "StylShip_WheelStand*", "StylShip_Helm*", "*Stand*" });

                    // Clone rudder visual
                    Transform rudderBlade = null;
                    var rudderMatches = SelectPatternMatches(visualRoot, RudderVisualPatterns);
                    CloneSelected(rudderMatches, wheelVisualRoot);
                    rudderBlade = FindFirstByPatterns(wheelVisualRoot, RudderVisualPatterns);

                    // Copy colliders from solid root if present
                    Transform solidRoot = FindChildByName(shipRoot.transform, "GameplaySolidColliders")
                                          ?? FindChildByName(shipRoot.transform, "AutoColliders");
                    if (solidRoot != null)
                    {
                        var solidWheelMatches = SelectPatternMatches(solidRoot, WheelVisualPatterns);
                        CloneSelected(solidWheelMatches, wheelSolidRoot);
                        var solidRudderMatches = SelectPatternMatches(solidRoot, RudderVisualPatterns);
                        CloneSelected(solidRudderMatches, wheelSolidRoot);
                    }

                    // Copy interaction zones
                    Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");
                    if (interactionRoot != null)
                    {
                        var interactionWheelMatches = SelectPatternMatches(interactionRoot, new[] { "*Wheel*", "*Helm*" });
                        CloneSelected(interactionWheelMatches, wheelInteractionRoot);
                    }

                    // Also clone Helm_Zone if present at ship root level
                    Transform helmZone = FindChildByName(shipRoot.transform, "Helm_Zone");
                    if (helmZone != null)
                    {
                        Transform helmClone = UnityEngine.Object.Instantiate(helmZone.gameObject).transform;
                        helmClone.name = helmZone.name;
                        helmClone.SetParent(wheelInteractionRoot, false);
                        helmClone.position = helmZone.position;
                        helmClone.rotation = helmZone.rotation;
                    }

                    var wheelModule = moduleRoot.AddComponent<ShipWheelModule>();
                    wheelModule.Configure(WheelPrefabName, wheelVisualRoot, wheelModel, rudderBlade, standAnchor,
                        wheelSolidRoot, wheelInteractionRoot);

                    // Wheel module owns its steering system — all settings live here.
                    var wheelSystem = moduleRoot.AddComponent<WheelFeatureStandaloneSystem>();
                    wheelSystem.SetRigReferences(wheelModel, rudderBlade, standAnchor, null);

                    PrefabUtility.SaveAsPrefabAsset(moduleRoot, WheelPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(moduleRoot);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[MastModulePrefabSetup] Wheel module prefab extracted from '{sourcePath}' to '{WheelPrefabPath}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Apply Ship Module Prefabs (Masts + Hull) To NetworkShip")]
        [MenuItem("RumOverboard/Setup/Ship Features/Apply Mast Module Prefabs To Stylized Gameplay Ship")]
        public static void ApplyMastModulePrefabsToGameplayShip()
        {
            ApplyShipModulePrefabsToNetworkShip();
        }

        public static void ApplyShipModulePrefabsToNetworkShip()
        {
            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                Transform hullRoot = FindChildByName(shipRoot.transform, "Hull");
                Transform autoCollidersRoot = FindChildByName(shipRoot.transform, "AutoColliders");
                Transform mastsRoot = FindChildByName(shipRoot.transform, "Masts");
                Transform climbRoot = FindChildByName(shipRoot.transform, "GameplayClimbZones");
                Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");
                Transform solidRoot = FindChildByName(shipRoot.transform, "GameplaySolidColliders");
                Transform traversalRoot = FindChildByName(shipRoot.transform, "GameplayTraversalTriggers");
                Transform helmZoneRoot = FindChildByName(shipRoot.transform, "Helm_Zone");

                Transform modulesRoot = FindChildByName(shipRoot.transform, "ShipModules");
                if (modulesRoot == null)
                    modulesRoot = CreateChild(shipRoot.transform, "ShipModules");

                Transform hullModulesRoot = FindChildByName(modulesRoot, "HullModules");
                if (hullModulesRoot == null)
                    hullModulesRoot = CreateChild(modulesRoot, "HullModules");

                Transform mastModulesRoot = FindChildByName(modulesRoot, "MastModules");
                if (mastModulesRoot == null)
                    mastModulesRoot = CreateChild(modulesRoot, "MastModules");

                // Rebuild instances deterministically.
                ClearChildren(hullModulesRoot);
                ClearChildren(mastModulesRoot);

                GameObject hullPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HullPrefabPath);
                if (hullPrefab != null)
                {
                    var hullInstance = (GameObject)PrefabUtility.InstantiatePrefab(hullPrefab, shipRoot.scene);
                    hullInstance.name = HullPrefabName;
                    hullInstance.transform.SetParent(hullModulesRoot, false);
                }
                else
                {
                    Debug.LogWarning($"[MastModulePrefabSetup] Hull prefab missing: {HullPrefabPath}. Run hull extraction first.");
                }

                for (int i = 0; i < Specs.Length; i++)
                {
                    string path = GetMastPrefabPath(Specs[i]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[MastModulePrefabSetup] Prefab missing: {path}. Run extraction first.");
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, shipRoot.scene);
                    instance.name = Specs[i].modulePrefabName;
                    instance.transform.SetParent(mastModulesRoot, false);
                }

                SetActiveIfNotNull(visualRoot, false);
                SetActiveIfNotNull(hullRoot, false);
                SetActiveIfNotNull(autoCollidersRoot, false);
                SetActiveIfNotNull(mastsRoot, false);
                SetActiveIfNotNull(solidRoot, false);
                SetActiveIfNotNull(climbRoot, false);
                SetActiveIfNotNull(interactionRoot, false);
                SetActiveIfNotNull(traversalRoot, false);
                SetActiveIfNotNull(helmZoneRoot, false);
                modulesRoot.gameObject.SetActive(true);

                PrefabUtility.SaveAsPrefabAsset(shipRoot, TargetNetworkShipPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MastModulePrefabSetup] Ship module prefabs applied from '{sourcePath}' to '{TargetNetworkShipPrefabPath}'. Source roots disabled in target.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        private static void RebuildSingleMastModule(MastSpec spec)
        {
            ExtractSingleMastModulePrefab(spec);
            ApplySingleMastModulePrefabToNetworkShip(spec);
        }

        private static void ExtractSingleMastModulePrefab(MastSpec spec)
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Prefabs");
            EnsureFolder(ModulePrefabRootPath);
            EnsureFolder(MastPrefabFolderPath);

            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing source ship prefab at '{sourcePath}'.");
                return;
            }

            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                Transform hullRoot = FindChildByName(shipRoot.transform, "Hull");
                Transform mastsColliderRoot = FindChildByName(shipRoot.transform, "Masts");
                Transform mastVisualRoot = visualRoot != null ? visualRoot : (hullRoot != null ? hullRoot : shipRoot.transform);
                Transform climbRoot = FindChildByName(shipRoot.transform, "GameplayClimbZones");
                Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");

                if (climbRoot == null)
                    climbRoot = mastsColliderRoot != null ? mastsColliderRoot : mastVisualRoot;
                if (interactionRoot == null)
                    interactionRoot = mastsColliderRoot != null ? mastsColliderRoot : mastVisualRoot;

                if (mastVisualRoot == null)
                {
                    Debug.LogError("[MastModulePrefabSetup] Mast visual source root not found in NetworkShip prefab.");
                    return;
                }

                BuildSingleMastPrefab(spec, mastVisualRoot, climbRoot, interactionRoot, mastsColliderRoot);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[MastModulePrefabSetup] Single mast module '{spec.modulePrefabName}' extracted from '{sourcePath}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        private static void ApplySingleMastModulePrefabToNetworkShip(MastSpec spec)
        {
            GameObject targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TargetNetworkShipPrefabPath);
            if (targetPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing target ship prefab at '{TargetNetworkShipPrefabPath}'.");
                return;
            }

            string mastPrefabPath = GetMastPrefabPath(spec);
            GameObject mastPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mastPrefabPath);
            if (mastPrefab == null)
            {
                Debug.LogError($"[MastModulePrefabSetup] Missing mast prefab '{mastPrefabPath}'. Extract it first.");
                return;
            }

            GameObject shipRoot = PrefabUtility.LoadPrefabContents(TargetNetworkShipPrefabPath);
            try
            {
                Transform modulesRoot = FindChildByName(shipRoot.transform, "ShipModules");
                if (modulesRoot == null)
                    modulesRoot = CreateChild(shipRoot.transform, "ShipModules");

                Transform mastModulesRoot = FindChildByName(modulesRoot, "MastModules");
                if (mastModulesRoot == null)
                    mastModulesRoot = CreateChild(modulesRoot, "MastModules");

                RemoveDirectChildByName(mastModulesRoot, spec.modulePrefabName);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(mastPrefab, shipRoot.scene);
                instance.name = spec.modulePrefabName;
                instance.transform.SetParent(mastModulesRoot, false);

                Transform mastsRoot = FindChildByName(shipRoot.transform, "Masts");
                if (mastsRoot != null)
                    SetPatternMatchesActive(mastsRoot, spec.mastColliderRootPatterns, false);

                Transform climbRoot = FindChildByName(shipRoot.transform, "GameplayClimbZones");
                if (climbRoot != null)
                    SetPatternMatchesActive(climbRoot, spec.climbZonePatterns, false);

                Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");
                if (interactionRoot != null)
                    SetPatternMatchesActive(interactionRoot, spec.interactionZonePatterns, false);

                modulesRoot.gameObject.SetActive(true);

                PrefabUtility.SaveAsPrefabAsset(shipRoot, TargetNetworkShipPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MastModulePrefabSetup] Single mast module '{spec.modulePrefabName}' applied to '{TargetNetworkShipPrefabPath}' without touching other mast modules.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        [MenuItem("RumOverboard/Setup/Ship Features/Restore Original Ship Sources In NetworkShip")]
        [MenuItem("RumOverboard/Setup/Ship Features/Restore Original Mast Sources In Stylized Gameplay Ship")]
        public static void RestoreOriginalMastSourcesInGameplayShip()
        {
            string sourcePath = ResolveSourcePrefabPath();
            GameObject shipRoot = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                Transform visualRoot = FindChildByName(shipRoot.transform, "VisualShip");
                Transform hullRoot = FindChildByName(shipRoot.transform, "Hull");
                Transform autoCollidersRoot = FindChildByName(shipRoot.transform, "AutoColliders");
                Transform mastsRoot = FindChildByName(shipRoot.transform, "Masts");
                Transform solidRoot = FindChildByName(shipRoot.transform, "GameplaySolidColliders");
                Transform climbRoot = FindChildByName(shipRoot.transform, "GameplayClimbZones");
                Transform interactionRoot = FindChildByName(shipRoot.transform, "GameplayInteractionZones");
                Transform traversalRoot = FindChildByName(shipRoot.transform, "GameplayTraversalTriggers");
                Transform helmZoneRoot = FindChildByName(shipRoot.transform, "Helm_Zone");
                Transform modulesRoot = FindChildByName(shipRoot.transform, "ShipModules");

                SetActiveIfNotNull(visualRoot, true);
                SetActiveIfNotNull(hullRoot, true);
                SetActiveIfNotNull(autoCollidersRoot, true);
                SetActiveIfNotNull(mastsRoot, true);
                SetActiveIfNotNull(solidRoot, true);
                SetActiveIfNotNull(climbRoot, true);
                SetActiveIfNotNull(interactionRoot, true);
                SetActiveIfNotNull(traversalRoot, true);
                SetActiveIfNotNull(helmZoneRoot, true);

                if (modulesRoot != null)
                    modulesRoot.gameObject.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(shipRoot, TargetNetworkShipPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MastModulePrefabSetup] Original source hierarchy restored from '{sourcePath}' to '{TargetNetworkShipPrefabPath}' (modules root disabled in target if present).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(shipRoot);
            }
        }

        private static void BuildSingleMastPrefab(MastSpec spec, Transform visualRoot, Transform climbRoot, Transform interactionRoot, Transform mastColliderRoot)
        {
            GameObject moduleRoot = new GameObject(spec.modulePrefabName);
            moduleRoot.transform.position = Vector3.zero;
            moduleRoot.transform.rotation = Quaternion.identity;
            moduleRoot.transform.localScale = Vector3.one;

            Transform visualModule = CreateChild(moduleRoot.transform, "Visual");
            Transform climbModule = CreateChild(moduleRoot.transform, "GameplayClimbZones");
            Transform interactionModule = CreateChild(moduleRoot.transform, "GameplayInteractionZones");
            Transform anchorModule = CreateChild(moduleRoot.transform, "SailAnchors");

            var selectedVisual = SelectPatternMatches(visualRoot, spec.visualPatterns);
            var selectedClimb = SelectPatternMatches(climbRoot, spec.climbZonePatterns);
            var selectedInteraction = SelectPatternMatches(interactionRoot, spec.interactionZonePatterns);
            var selectedSails = SelectPatternMatches(visualRoot, spec.sailVisualPatterns);
            Transform mastAnchorSource = null;

            if (mastColliderRoot != null)
            {
                var mastColliders = SelectPatternMatches(mastColliderRoot, spec.mastColliderRootPatterns);
                AppendUnique(selectedClimb, mastColliders);
                mastAnchorSource = FindFirstByPatterns(mastColliderRoot, spec.mastColliderRootPatterns);
            }

            RemoveMatchesByPrefix(selectedVisual, "StylShip_Ropes");
            RemoveMatchesByPrefix(selectedClimb, "StylShip_Ropes");
            RemoveMatchesByPrefix(selectedInteraction, "StylShip_Ropes");

            CloneSelected(selectedVisual, visualModule);
            CloneSelected(selectedClimb, climbModule);
            CloneSelected(selectedInteraction, interactionModule);

            Transform mastVisualClone = FindChildByName(visualModule, spec.mastVisualName);
            Transform[] ropeClones = FindRopeVisuals(visualModule, spec);

            if (mastAnchorSource == null)
                mastAnchorSource = mastVisualClone != null
                    ? mastVisualClone
                    : FindFirstByPatterns(visualRoot, new[] { spec.mastVisualName });

            BuildSailAnchors(anchorModule, mastAnchorSource, selectedSails, moduleRoot.transform);

            var module = moduleRoot.AddComponent<ShipMastModule>();
            module.Configure(spec.modulePrefabName, mastVisualClone, ropeClones, anchorModule, climbModule, interactionModule);

            // ShipMast: fully self-contained sail/aero/stress system per mast module.
            var shipMast = moduleRoot.AddComponent<ShipMast>();
            ConfigureShipMastFromAnchors(shipMast, anchorModule, mastVisualClone, spec);

            string prefabPath = GetMastPrefabPath(spec);
            PrefabUtility.SaveAsPrefabAsset(moduleRoot, prefabPath);
            UnityEngine.Object.DestroyImmediate(moduleRoot);
        }

        /// <summary>
        /// Reads sail anchor points from the extracted SailAnchors child and builds
        /// MastDefinition + SailPanel[] so ShipMast has everything to run cloth sim + aero.
        /// </summary>
        private static void ConfigureShipMastFromAnchors(ShipMast shipMast, Transform anchorRoot, Transform mastVisual, MastSpec spec)
        {
            // --- Mast definition from MastBase/MastTop anchors ---
            Transform mastBaseAnchor = anchorRoot.Find("MastBase");
            Transform mastTopAnchor = anchorRoot.Find("MastTop");

            var mastDef = new ShipMast.MastDefinition
            {
                name = spec.modulePrefabName,
                baseLocal = mastBaseAnchor != null ? mastBaseAnchor.localPosition : Vector3.zero,
                topLocal = mastTopAnchor != null ? mastTopAnchor.localPosition : new Vector3(0f, 12f, 0f),
                radius = 0.22f,
            };
            shipMast.SetMast(mastDef);

            // --- Sail panels from quad anchors (prefix_TopLeft, prefix_TopRight, etc.) ---
            var sailNames = new List<string>();
            foreach (Transform anchor in anchorRoot)
            {
                if (anchor == null) continue;
                string n = anchor.name;
                int sep = n.LastIndexOf('_');
                if (sep <= 0) continue;
                string prefix = n.Substring(0, sep);
                string suffix = n.Substring(sep + 1);
                if (suffix == "TopLeft" && !sailNames.Contains(prefix))
                    sailNames.Add(prefix);
            }

            var panels = new ShipMast.SailPanel[sailNames.Count];
            for (int i = 0; i < sailNames.Count; i++)
            {
                string prefix = sailNames[i];
                Transform tl = anchorRoot.Find(prefix + "_TopLeft");
                Transform tr = anchorRoot.Find(prefix + "_TopRight");
                Transform bl = anchorRoot.Find(prefix + "_BottomLeft");
                Transform br = anchorRoot.Find(prefix + "_BottomRight");

                bool isTriangular = prefix.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    prefix.IndexOf("Tri", StringComparison.OrdinalIgnoreCase) >= 0;

                // Calculate area from quad corners
                float area = 12f;
                if (tl != null && tr != null && bl != null && br != null)
                {
                    float width = Vector3.Distance(tl.localPosition, tr.localPosition);
                    float height = Vector3.Distance(tl.localPosition, bl.localPosition);
                    area = width * height * (isTriangular ? 0.5f : 1f);
                }

                // Mast attach height = vertical fraction of sail center relative to mast axis
                float attachHeight = 0.5f;
                if (tl != null && mastBaseAnchor != null && mastTopAnchor != null)
                {
                    float mastHeight = (mastTopAnchor.localPosition - mastBaseAnchor.localPosition).magnitude;
                    if (mastHeight > 0.1f)
                    {
                        float sailMidY = (tl.localPosition.y + (bl != null ? bl.localPosition.y : tl.localPosition.y)) * 0.5f;
                        attachHeight = Mathf.Clamp01((sailMidY - mastBaseAnchor.localPosition.y) / mastHeight);
                    }
                }

                panels[i] = new ShipMast.SailPanel
                {
                    name = prefix,
                    enabled = true,
                    visualSailName = prefix,
                    triangularHint = isTriangular,
                    mastAttachHeight = attachHeight,
                    topLeftLocal = tl != null ? tl.localPosition : Vector3.zero,
                    topRightLocal = tr != null ? tr.localPosition : Vector3.zero,
                    bottomLeftLocal = bl != null ? bl.localPosition : Vector3.zero,
                    bottomRightLocal = br != null ? br.localPosition : Vector3.zero,
                    baseArea = area,
                    hoist01 = 1f,
                    extension01 = 1f,
                };
            }

            shipMast.SetSails(panels);
        }

        private static void BuildSailAnchors(Transform anchorRoot, Transform mastVisualClone, List<Transform> sailSources, Transform moduleRoot)
        {
            if (mastVisualClone != null && TryGetWorldBounds(mastVisualClone, out Bounds mastBounds))
            {
                CreateAnchor(anchorRoot, "MastBase", new Vector3(mastBounds.center.x, mastBounds.min.y, mastBounds.center.z), moduleRoot);
                CreateAnchor(anchorRoot, "MastTop", new Vector3(mastBounds.center.x, mastBounds.max.y, mastBounds.center.z), moduleRoot);
            }

            for (int i = 0; i < sailSources.Count; i++)
            {
                Transform sail = sailSources[i];
                if (sail == null)
                    continue;

                string prefix = sail.name + "_";
                if (TryGetSailAnchorWorldPointsFromMesh(sail, moduleRoot, out Vector3 tl, out Vector3 tr, out Vector3 bl, out Vector3 br, out Vector3 center))
                {
                    CreateAnchor(anchorRoot, prefix + "TopLeft", tl, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "TopRight", tr, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "BottomLeft", bl, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "BottomRight", br, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "Center", center, moduleRoot);
                    continue;
                }

                if (!TryGetWorldBounds(sail, out Bounds b))
                    continue;

                Vector3 top = b.center + sail.up * b.extents.y;
                Vector3 bottom = b.center - sail.up * b.extents.y;
                Vector3 right = sail.right.normalized;

                bool forceTriangular = sail.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       sail.name.IndexOf("Tri", StringComparison.OrdinalIgnoreCase) >= 0;

                if (forceTriangular)
                {
                    float apexHalf = Mathf.Max(0.02f, b.extents.x * 0.08f);
                    CreateAnchor(anchorRoot, prefix + "TopLeft", top - right * apexHalf, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "TopRight", top + right * apexHalf, moduleRoot);
                }
                else
                {
                    CreateAnchor(anchorRoot, prefix + "TopLeft", top - right * b.extents.x, moduleRoot);
                    CreateAnchor(anchorRoot, prefix + "TopRight", top + right * b.extents.x, moduleRoot);
                }

                CreateAnchor(anchorRoot, prefix + "BottomLeft", bottom - right * b.extents.x, moduleRoot);
                CreateAnchor(anchorRoot, prefix + "BottomRight", bottom + right * b.extents.x, moduleRoot);
                CreateAnchor(anchorRoot, prefix + "Center", b.center, moduleRoot);
            }
        }

        private static bool TryGetSailAnchorWorldPointsFromMesh(
            Transform sail,
            Transform moduleRoot,
            out Vector3 topLeft,
            out Vector3 topRight,
            out Vector3 bottomLeft,
            out Vector3 bottomRight,
            out Vector3 center)
        {
            topLeft = topRight = bottomLeft = bottomRight = center = default;
            if (sail == null || moduleRoot == null)
                return false;

            if (!sail.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
                return false;

            Vector3[] vertices = meshFilter.sharedMesh.vertices;
            if (vertices == null || vertices.Length < 3)
                return false;

            Vector3 meshSize = meshFilter.sharedMesh.bounds.size;
            int thinAxis = GetSmallestAxis(meshSize);
            int axisA = (thinAxis + 1) % 3;
            int axisB = (thinAxis + 2) % 3;

            Vector3 upInSail = sail.InverseTransformDirection(moduleRoot.up).normalized;
            Vector3 rightInSail = sail.InverseTransformDirection(moduleRoot.right).normalized;

            int verticalAxis = Mathf.Abs(GetAxisComponent(upInSail, axisA)) >= Mathf.Abs(GetAxisComponent(upInSail, axisB)) ? axisA : axisB;
            int horizontalAxis = verticalAxis == axisA ? axisB : axisA;

            float verticalSign = Mathf.Sign(GetAxisComponent(upInSail, verticalAxis));
            if (Mathf.Abs(verticalSign) < 0.5f) verticalSign = 1f;
            float horizontalSign = Mathf.Sign(GetAxisComponent(rightInSail, horizontalAxis));
            if (Mathf.Abs(horizontalSign) < 0.5f) horizontalSign = 1f;

            float minU = GetAxisComponent(vertices[0], horizontalAxis) * horizontalSign;
            float maxU = minU;
            float minV = GetAxisComponent(vertices[0], verticalAxis) * verticalSign;
            float maxV = minV;

            for (int i = 1; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, vv);
                maxV = Mathf.Max(maxV, vv);
            }

            float height = Mathf.Max(0.001f, maxV - minV);
            float topBand = maxV - height * 0.15f;
            float bottomBand = minV + height * 0.15f;

            float topMinU = float.MaxValue;
            float topMaxU = float.MinValue;
            float bottomMinU = float.MaxValue;
            float bottomMaxU = float.MinValue;
            bool hasTop = false;
            bool hasBottom = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;

                if (vv >= topBand)
                {
                    hasTop = true;
                    topMinU = Mathf.Min(topMinU, u);
                    topMaxU = Mathf.Max(topMaxU, u);
                }

                if (vv <= bottomBand)
                {
                    hasBottom = true;
                    bottomMinU = Mathf.Min(bottomMinU, u);
                    bottomMaxU = Mathf.Max(bottomMaxU, u);
                }
            }

            if (!hasTop)
            {
                topMinU = minU;
                topMaxU = maxU;
            }

            if (!hasBottom)
            {
                bottomMinU = minU;
                bottomMaxU = maxU;
            }

            float topWidth = Mathf.Max(0.001f, topMaxU - topMinU);
            float bottomWidth = Mathf.Max(0.001f, bottomMaxU - bottomMinU);
            bool triangular = topWidth <= bottomWidth * 0.55f ||
                              sail.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              sail.name.IndexOf("Tri", StringComparison.OrdinalIgnoreCase) >= 0;

            Vector3 topLeftMesh;
            Vector3 topRightMesh;
            Vector3 bottomLeftMesh;
            Vector3 bottomRightMesh;

            if (triangular)
            {
                Vector3 apex = vertices[0];
                float apexV = GetAxisComponent(apex, verticalAxis) * verticalSign;
                for (int i = 1; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                    if (vv > apexV)
                    {
                        apex = v;
                        apexV = vv;
                    }
                }

                bottomLeftMesh = vertices[0];
                bottomRightMesh = vertices[0];
                float leftU = GetAxisComponent(bottomLeftMesh, horizontalAxis) * horizontalSign;
                float rightU = leftU;
                bool foundBottom = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                    if (vv > bottomBand + height * 0.05f)
                        continue;

                    float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                    if (!foundBottom || u < leftU)
                    {
                        bottomLeftMesh = v;
                        leftU = u;
                    }
                    if (!foundBottom || u > rightU)
                    {
                        bottomRightMesh = v;
                        rightU = u;
                    }
                    foundBottom = true;
                }

                if (!foundBottom)
                {
                    bottomLeftMesh = FindNearestByUV(vertices, minU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                    bottomRightMesh = FindNearestByUV(vertices, maxU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                }

                float apexHalfWidth = Mathf.Max(bottomWidth * 0.03f, 0.02f);
                Vector3 horizontalDir = GetAxisVector(horizontalAxis) * horizontalSign;
                topLeftMesh = apex - horizontalDir * apexHalfWidth;
                topRightMesh = apex + horizontalDir * apexHalfWidth;
            }
            else
            {
                topLeftMesh = FindNearestByUV(vertices, minU, maxV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                topRightMesh = FindNearestByUV(vertices, maxU, maxV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                bottomLeftMesh = FindNearestByUV(vertices, minU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
                bottomRightMesh = FindNearestByUV(vertices, maxU, minV, horizontalAxis, horizontalSign, verticalAxis, verticalSign);
            }

            topLeft = sail.TransformPoint(topLeftMesh);
            topRight = sail.TransformPoint(topRightMesh);
            bottomLeft = sail.TransformPoint(bottomLeftMesh);
            bottomRight = sail.TransformPoint(bottomRightMesh);
            center = (topLeft + topRight + bottomLeft + bottomRight) * 0.25f;
            return true;
        }

        private static int GetSmallestAxis(Vector3 size)
        {
            if (size.x <= size.y && size.x <= size.z)
                return 0;
            if (size.y <= size.x && size.y <= size.z)
                return 1;
            return 2;
        }

        private static float GetAxisComponent(Vector3 v, int axis)
        {
            if (axis == 0) return v.x;
            if (axis == 1) return v.y;
            return v.z;
        }

        private static Vector3 GetAxisVector(int axis)
        {
            if (axis == 0) return Vector3.right;
            if (axis == 1) return Vector3.up;
            return Vector3.forward;
        }

        private static Vector3 FindNearestByUV(
            Vector3[] vertices,
            float targetU,
            float targetV,
            int horizontalAxis,
            float horizontalSign,
            int verticalAxis,
            float verticalSign)
        {
            Vector3 best = vertices[0];
            float bestSqr = float.MaxValue;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                float u = GetAxisComponent(v, horizontalAxis) * horizontalSign;
                float vv = GetAxisComponent(v, verticalAxis) * verticalSign;
                float du = u - targetU;
                float dv = vv - targetV;
                float sqr = du * du + dv * dv;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = v;
                }
            }

            return best;
        }

        private static void CreateAnchor(Transform root, string name, Vector3 worldPoint, Transform moduleRoot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.position = worldPoint;
            t.localPosition = moduleRoot.InverseTransformPoint(worldPoint);
        }

        private static void CloneSelected(List<Transform> sources, Transform targetParent)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                Transform src = sources[i];
                if (src == null)
                    continue;

                var clone = UnityEngine.Object.Instantiate(src.gameObject);
                clone.name = src.name;
                clone.transform.SetParent(targetParent, false);
                clone.transform.position = src.position;
                clone.transform.rotation = src.rotation;
            }
        }

        private static Transform[] FindRopeVisuals(Transform visualModule, MastSpec spec)
        {
            var ropes = new List<Transform>();
            for (int i = 0; i < spec.visualPatterns.Length; i++)
            {
                string p = spec.visualPatterns[i];
                if (!p.Contains("Wire", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("Ropes", StringComparison.OrdinalIgnoreCase))
                    continue;

                var found = FindByPattern(visualModule, p);
                for (int j = 0; j < found.Count; j++)
                    if (found[j] != null &&
                        !found[j].name.StartsWith("StylShip_Ropes", StringComparison.Ordinal) &&
                        !ropes.Contains(found[j]))
                        ropes.Add(found[j]);
            }

            return ropes.ToArray();
        }

        private static void TrimMastVisualsFromHullClone(Transform hullVisualClone)
        {
            if (hullVisualClone == null)
                return;

            var toRemove = new List<Transform>();
            for (int i = 0; i < Specs.Length; i++)
            {
                var visualPatterns = Specs[i].visualPatterns;
                for (int p = 0; p < visualPatterns.Length; p++)
                {
                    var matches = FindByPattern(hullVisualClone, visualPatterns[p]);
                    for (int m = 0; m < matches.Count; m++)
                    {
                        Transform match = matches[m];
                        if (match == null || match == hullVisualClone)
                            continue;
                        if (!toRemove.Contains(match))
                            toRemove.Add(match);
                    }
                }
            }

            // Delete deepest first so parents are removed once and children do not become orphan targets.
            toRemove.Sort((a, b) => GetDepth(b).CompareTo(GetDepth(a)));
            for (int i = 0; i < toRemove.Count; i++)
            {
                Transform t = toRemove[i];
                if (t != null)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        private static void CloneSourceRootWithoutMasts(Transform shipRoot, string sourceRootName, Transform targetParent)
        {
            if (shipRoot == null || targetParent == null)
                return;

            Transform sourceRoot = FindChildByName(shipRoot, sourceRootName);
            if (sourceRoot == null)
                return;

            Transform clone = UnityEngine.Object.Instantiate(sourceRoot.gameObject).transform;
            clone.name = sourceRoot.name;
            clone.SetParent(targetParent, false);

            var toRemove = new List<Transform>();
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == clone)
                    continue;

                if (!IsMastOwnedName(t.name))
                    continue;

                toRemove.Add(t);
            }

            toRemove.Sort((a, b) => GetDepth(b).CompareTo(GetDepth(a)));
            for (int i = 0; i < toRemove.Count; i++)
            {
                Transform t = toRemove[i];
                if (t != null)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        private static void CloneNamedChildren(Transform sourceRoot, Transform targetRoot, string[] childNames)
        {
            if (sourceRoot == null || targetRoot == null || childNames == null)
                return;

            for (int i = 0; i < childNames.Length; i++)
            {
                string name = childNames[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                Transform sourceChild = FindDirectChildByName(sourceRoot, name) ?? FindChildByName(sourceRoot, name);
                if (sourceChild == null)
                    continue;

                Transform clone = UnityEngine.Object.Instantiate(sourceChild.gameObject).transform;
                clone.name = sourceChild.name;
                clone.SetParent(targetRoot, false);
            }
        }

        private static Transform FindDirectChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name == name)
                    return child;
            }

            return null;
        }

        private static void AppendUnique(List<Transform> target, List<Transform> extra)
        {
            if (target == null || extra == null)
                return;

            for (int i = 0; i < extra.Count; i++)
            {
                Transform t = extra[i];
                if (t == null || target.Contains(t) || IsDescendantOfSelected(target, t))
                    continue;
                target.Add(t);
            }
        }

        private static void RemoveMatchesByPrefix(List<Transform> items, string prefix)
        {
            if (items == null || string.IsNullOrWhiteSpace(prefix))
                return;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                Transform t = items[i];
                if (t == null || t.name.StartsWith(prefix, StringComparison.Ordinal))
                    items.RemoveAt(i);
            }
        }

        private static void RemoveDirectChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child == null || !string.Equals(child.name, childName, StringComparison.Ordinal))
                    continue;

                UnityEngine.Object.DestroyImmediate(child.gameObject);
                return;
            }
        }

        private static void SetPatternMatchesActive(Transform root, string[] patterns, bool active)
        {
            if (root == null || patterns == null)
                return;

            for (int i = 0; i < patterns.Length; i++)
            {
                List<Transform> matches = FindByPattern(root, patterns[i]);
                for (int j = 0; j < matches.Count; j++)
                {
                    Transform t = matches[j];
                    if (t != null)
                        t.gameObject.SetActive(active);
                }
            }
        }

        private static void RemoveNodesByTokens(Transform root, string[] tokens)
        {
            if (root == null || tokens == null || tokens.Length == 0)
                return;

            var toRemove = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root)
                    continue;

                if (ContainsAnyToken(t.name, tokens))
                    toRemove.Add(t);
            }

            toRemove.Sort((a, b) => GetDepth(b).CompareTo(GetDepth(a)));
            for (int i = 0; i < toRemove.Count; i++)
            {
                Transform t = toRemove[i];
                if (t != null)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        private static Transform FindFirstByPatterns(Transform root, string[] patterns)
        {
            if (root == null || patterns == null)
                return null;

            for (int i = 0; i < patterns.Length; i++)
            {
                List<Transform> matches = FindByPattern(root, patterns[i]);
                if (matches.Count > 0)
                    return matches[0];
            }

            return null;
        }

        private static bool ContainsAnyToken(string value, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null)
                return false;

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsMastOwnedName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            for (int i = 0; i < Specs.Length; i++)
            {
                if (MatchesAnyPattern(objectName, Specs[i].visualPatterns))
                    return true;
                if (MatchesAnyPattern(objectName, Specs[i].climbZonePatterns))
                    return true;
                if (MatchesAnyPattern(objectName, Specs[i].interactionZonePatterns))
                    return true;
                if (MatchesAnyPattern(objectName, Specs[i].mastColliderRootPatterns))
                    return true;
            }

            return false;
        }

        private static bool MatchesAnyPattern(string objectName, string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(objectName) || patterns == null)
                return false;

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (string.IsNullOrWhiteSpace(pattern))
                    continue;

                bool prefix = pattern.EndsWith("*", StringComparison.Ordinal);
                string token = prefix ? pattern.Substring(0, pattern.Length - 1) : pattern;
                if (prefix)
                {
                    if (objectName.StartsWith(token, StringComparison.Ordinal))
                        return true;
                }
                else if (string.Equals(objectName, token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetActiveIfNotNull(Transform root, bool active)
        {
            if (root != null)
                root.gameObject.SetActive(active);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
                return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child != null)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static List<Transform> SelectPatternMatches(Transform searchRoot, string[] patterns)
        {
            var result = new List<Transform>();
            if (searchRoot == null || patterns == null)
                return result;

            for (int i = 0; i < patterns.Length; i++)
            {
                var matches = FindByPattern(searchRoot, patterns[i]);
                for (int j = 0; j < matches.Count; j++)
                {
                    Transform t = matches[j];
                    if (t == null)
                        continue;
                    if (IsDescendantOfSelected(result, t))
                        continue;
                    result.Add(t);
                }
            }

            result.Sort((a, b) => GetDepth(a).CompareTo(GetDepth(b)));
            return result;
        }

        private static bool IsDescendantOfSelected(List<Transform> selected, Transform candidate)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                if (candidate.IsChildOf(selected[i]))
                    return true;
            }
            return false;
        }

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            Transform p = t;
            while (p != null)
            {
                depth++;
                p = p.parent;
            }
            return depth;
        }

        private static List<Transform> FindByPattern(Transform root, string pattern)
        {
            var result = new List<Transform>();
            if (root == null || string.IsNullOrWhiteSpace(pattern))
                return result;

            bool prefix = pattern.EndsWith("*", StringComparison.Ordinal);
            string token = prefix ? pattern.Substring(0, pattern.Length - 1) : pattern;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                bool match = prefix
                    ? t.name.StartsWith(token, StringComparison.Ordinal)
                    : t.name.Equals(token, StringComparison.Ordinal);
                if (match)
                    result.Add(t);
            }

            return result;
        }

        private static bool TryGetWorldBounds(Transform target, out Bounds bounds)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    bounds.Encapsulate(colliders[i].bounds);
                return true;
            }

            bounds = new Bounds(target.position, Vector3.one);
            return false;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name)
                    return t;
            return null;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static string GetMastPrefabPath(MastSpec spec)
        {
            return MastPrefabFolderPath + "/" + spec.modulePrefabName + ".prefab";
        }

        private static string ResolveSourcePrefabPath()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SourceNetworkShipPrefabPath) != null)
                return SourceNetworkShipPrefabPath;

            Debug.LogWarning($"[MastModulePrefabSetup] Source reference prefab not found at '{SourceNetworkShipPrefabPath}'. Falling back to '{TargetNetworkShipPrefabPath}'.");
            return TargetNetworkShipPrefabPath;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private readonly struct MastSpec
        {
            public readonly string modulePrefabName;
            public readonly string mastVisualName;
            public readonly string[] visualPatterns;
            public readonly string[] sailVisualPatterns;
            public readonly string[] climbZonePatterns;
            public readonly string[] interactionZonePatterns;
            public readonly string[] mastColliderRootPatterns;

            public MastSpec(
                string modulePrefabName,
                string mastVisualName,
                string[] visualPatterns,
                string[] sailVisualPatterns,
                string[] climbZonePatterns,
                string[] interactionZonePatterns,
                string[] mastColliderRootPatterns)
            {
                this.modulePrefabName = modulePrefabName;
                this.mastVisualName = mastVisualName;
                this.visualPatterns = visualPatterns;
                this.sailVisualPatterns = sailVisualPatterns;
                this.climbZonePatterns = climbZonePatterns;
                this.interactionZonePatterns = interactionZonePatterns;
                this.mastColliderRootPatterns = mastColliderRootPatterns;
            }
        }
    }
}

