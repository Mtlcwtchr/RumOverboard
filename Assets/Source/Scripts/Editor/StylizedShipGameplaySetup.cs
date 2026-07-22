using RumOverboard.Gameplay;
using RumOverboard.Gameplay.Ocean;
using Source.Scripts.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Source.Scripts.Editor
{
    /// <summary>
    /// Builds a gameplay-ready wrapper prefab around the Stylized Pirate Ship asset:
    /// climb triggers, interaction zones, ladder colliders and blocked/passable volumes.
    /// </summary>
    public static class StylizedShipGameplaySetup
    {
        private const string SourceShipPrefabPath = "Assets/Stylized_Pirate_Ship/StylShip_Unity.prefab";
        private const string StylizedMaterialsFolderPath = "Assets/Stylized_Pirate_Ship/StylShip_MatTextures";
        private const string GameplayShipPrefabPath = "Assets/Source/Environment/Prefabs/StylizedShipGameplay.prefab";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string EnvironmentRootName = "Environment";
        private const string ClimbLayerName = "Climbable";
        private const string NetworkPlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";
        private const string UrpLitShaderPath = "Universal Render Pipeline/Lit";
        private const string UrpSimpleLitShaderPath = "Universal Render Pipeline/Simple Lit";

        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int PropBumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int PropNormalMap = Shader.PropertyToID("_NormalMap");
        private static readonly int PropBumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int PropNormalScale = Shader.PropertyToID("_NormalScale");
        private static readonly int PropMetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int PropMetallic = Shader.PropertyToID("_Metallic");
        private static readonly int PropGlossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int PropSmoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int PropEmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int PropCutoff = Shader.PropertyToID("_Cutoff");
        private static readonly int PropAlphaClip = Shader.PropertyToID("_AlphaClip");

        private static readonly Vector3 DefaultShipPosition = new Vector3(56f, 1.1f, 8f);
        private static readonly Quaternion DefaultShipRotation = Quaternion.Euler(0f, 176f, 0f);

        [MenuItem("RumOverboard/Setup/Build or Update Stylized Gameplay Ship (Colliders & Zones)")]
        public static void BuildOrUpdateStylizedGameplayShip()
        {
            ConvertStylizedShipMaterialsToUrp();

            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Prefabs");

            int climbLayer = EnsureLayer(ClimbLayerName);

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceShipPrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"[StylizedShipGameplaySetup] Source ship prefab not found at '{SourceShipPrefabPath}'.");
                return;
            }

            var wrapperRoot = new GameObject("StylizedShipGameplay");
            try
            {
                var visualShip = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
                visualShip.name = "VisualShip";
                visualShip.transform.SetParent(wrapperRoot.transform, false);

                SyncVisualElementColliders(visualShip.transform);
                MakeDecorativePartsPassThrough(visualShip.transform);
                BuildGameplayVolumes(wrapperRoot.transform, visualShip.transform, climbLayer);
                EnsureShipPhysicsComponents(wrapperRoot);

                var prefab = PrefabUtility.SaveAsPrefabAsset(wrapperRoot, GameplayShipPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[StylizedShipGameplaySetup] Failed to save gameplay ship prefab at '{GameplayShipPrefabPath}'.");
                    return;
                }
            }
            finally
            {
                Object.DestroyImmediate(wrapperRoot);
            }

            EnsureNetworkPlayerClimbMask(climbLayer);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StylizedShipGameplaySetup] Gameplay ship prefab updated: '{GameplayShipPrefabPath}'.");
        }

        [MenuItem("RumOverboard/Setup/Convert Stylized Ship Materials To URP")]
        public static void ConvertStylizedShipMaterialsToUrp()
        {
            Shader targetShader = ResolveStylizedShipShader();
            if (targetShader == null)
            {
                Debug.LogWarning("[StylizedShipGameplaySetup] URP shader not found. Install/enable URP before conversion.");
                return;
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { StylizedMaterialsFolderPath });
            if (materialGuids == null || materialGuids.Length == 0)
            {
                Debug.LogWarning($"[StylizedShipGameplaySetup] No materials found in '{StylizedMaterialsFolderPath}'.");
                return;
            }

            int convertedCount = 0;
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;

                if (ConvertMaterialToUrp(material, targetShader))
                    convertedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StylizedShipGameplaySetup] Stylized ship materials converted to '{targetShader.name}': {convertedCount}/{materialGuids.Length}.");
        }

        [MenuItem("RumOverboard/Setup/Place Stylized Gameplay Ship In Scene")]
        public static void PlaceStylizedGameplayShipInScene()
        {
            var shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayShipPrefabPath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[StylizedShipGameplaySetup] Gameplay ship prefab not found at '{GameplayShipPrefabPath}'. Build it first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            var environmentRoot = FindOrCreate(EnvironmentRootName);

            var existingShip = GameObject.Find("MooredShipPlaceholder") ?? GameObject.Find("StylizedShipGameplay");

            Vector3 targetPosition = DefaultShipPosition;
            Quaternion targetRotation = DefaultShipRotation;
            Vector3 targetScale = Vector3.one;

            if (existingShip != null)
            {
                targetPosition = existingShip.transform.position;
                targetRotation = existingShip.transform.rotation;
                targetScale = existingShip.transform.localScale;
                Object.DestroyImmediate(existingShip);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab, scene);
            instance.name = "MooredShipPlaceholder";
            instance.transform.SetParent(environmentRoot.transform);
            instance.transform.SetPositionAndRotation(targetPosition, targetRotation);
            instance.transform.localScale = targetScale;

             WireShipRuntimeReferences(instance);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[StylizedShipGameplaySetup] Stylized gameplay ship placed in scene.");
        }

        private static void BuildGameplayVolumes(Transform wrapperRoot, Transform visualShip, int climbLayer)
        {
            var solidRoot = CreateChild(wrapperRoot, "GameplaySolidColliders");
            var climbRoot = CreateChild(wrapperRoot, "GameplayClimbZones");
            var interactionRoot = CreateChild(wrapperRoot, "GameplayInteractionZones");
            var traversalRoot = CreateChild(wrapperRoot, "GameplayTraversalTriggers");

            AddCoreSolidColliders(solidRoot, visualShip);
            AddMastClimbZones(climbRoot, interactionRoot, visualShip, climbLayer);
            AddRiggingClimbZones(climbRoot, visualShip, climbLayer);
            AddYardClimbZones(climbRoot, visualShip, climbLayer);
            AddLadderVolumes(solidRoot, climbRoot, interactionRoot, visualShip, climbLayer);

            AddInteractionZones(interactionRoot, visualShip);
            AddWalkableTriggers(traversalRoot, visualShip);
            AddLoopholeBlockers(solidRoot, traversalRoot, interactionRoot, visualShip);
        }

        private static void AddMastClimbZones(Transform climbRoot, Transform interactionRoot, Transform visualShip, int climbLayer)
        {
            string[] mastNames = { "StylShip_MastFront", "StylShip_MastMid", "StylShip_MastBack" };
            foreach (string mastName in mastNames)
            {
                var mast = FindChildByName(visualShip, mastName);
                if (mast == null)
                    continue;

                if (!TryGetWorldBounds(mast, out Bounds bounds))
                    continue;

                float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.45f, 0.14f, 0.42f);
                float height = Mathf.Max(bounds.size.y * 0.92f, radius * 4f);
                Vector3 center = new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z);

                CreateClimbCapsule(climbRoot, $"{mastName}_Climb", center, radius, height, climbLayer);

                Vector3 nestCenter = new Vector3(center.x, bounds.max.y - Mathf.Max(0.5f, bounds.size.y * 0.07f), center.z);
                CreateInteractionBox(
                    interactionRoot,
                    $"{mastName}_CrowNest",
                    nestCenter,
                    new Vector3(1.15f, 0.85f, 1.15f),
                    ShipInteractionZone.ZoneKind.CrowNest,
                    "Climb To Nest");
            }
        }

        private static void AddRiggingClimbZones(Transform climbRoot, Transform visualShip, int climbLayer)
        {
            string[] riggingNames = { "StylShip_WireFront", "StylShip_WireMid", "StylShip_WireBack", "StylShip_Ropes" };
            foreach (string rigName in riggingNames)
            {
                var rig = FindChildByName(visualShip, rigName);
                if (rig == null)
                    continue;

                if (!TryGetWorldBounds(rig, out Bounds bounds))
                    continue;

                Vector3 size = Vector3.Scale(bounds.size, new Vector3(0.88f, 0.92f, 0.88f));
                size.x = Mathf.Clamp(size.x, 0.20f, 2.2f);
                size.y = Mathf.Clamp(size.y, 0.45f, 9.5f);
                size.z = Mathf.Clamp(size.z, 0.20f, 2.2f);

                CreateClimbBox(climbRoot, $"{rigName}_Climb", bounds.center, size, climbLayer);
            }
        }

        private static void AddYardClimbZones(Transform climbRoot, Transform visualShip, int climbLayer)
        {
            string[] yardNames = { "StylShip_Bracing", "StylShip_WireFront", "StylShip_WireMid", "StylShip_WireBack" };
            foreach (string yardName in yardNames)
            {
                var yard = FindChildByName(visualShip, yardName);
                if (yard == null || !TryGetWorldBounds(yard, out Bounds bounds))
                    continue;

                Vector3 size = Vector3.Scale(bounds.size, new Vector3(0.9f, 0.6f, 0.9f));
                size.x = Mathf.Clamp(size.x, 0.25f, 14f);
                size.y = Mathf.Clamp(size.y, 0.20f, 1.25f);
                size.z = Mathf.Clamp(size.z, 0.25f, 14f);

                CreateClimbBox(climbRoot, $"{yardName}_YardClimb", bounds.center, size, climbLayer, Vector3.right);
            }
        }

        private static void AddCoreSolidColliders(Transform solidRoot, Transform visualShip)
        {
            // Important: avoid a single giant body collider in the center,
            // otherwise the interior volume becomes non-walkable.
            AddSolidForPart(visualShip, solidRoot, "StylShip_Prow", "Solid_Prow", new Vector3(0.9f, 0.9f, 0.85f), new Vector3(0f, 0.1f, 0f), new Vector3(0f, 1.0f, 0f));
            AddSolidForPart(visualShip, solidRoot, "StylShip_Rudder", "Solid_Rudder", new Vector3(0.9f, 0.95f, 0.9f), Vector3.zero, new Vector3(0.25f, 0.6f, 0.45f));

            var body = FindChildByName(visualShip, "StylShip_Body");
            if (body != null && TryGetWorldBounds(body, out Bounds bodyBounds))
            {
                float sideThickness = 0.28f;
                float sideHeight = Mathf.Max(2.0f, bodyBounds.size.y * 0.58f);
                float sideLength = bodyBounds.size.z * 0.88f;
                float sideY = bodyBounds.min.y + sideHeight * 0.52f;
                float centerInset = 0.18f;

                CreateSolidBox(
                    solidRoot,
                    "Solid_HullSide_Port",
                    new Vector3(bodyBounds.min.x + sideThickness * 0.5f + centerInset, sideY, bodyBounds.center.z),
                    new Vector3(sideThickness, sideHeight, sideLength));
                CreateSolidBox(
                    solidRoot,
                    "Solid_HullSide_Starboard",
                    new Vector3(bodyBounds.max.x - sideThickness * 0.5f - centerInset, sideY, bodyBounds.center.z),
                    new Vector3(sideThickness, sideHeight, sideLength));

                float sternThickness = 0.30f;
                float sternWidth = Mathf.Max(1.8f, bodyBounds.size.x * 0.72f);
                CreateSolidBox(
                    solidRoot,
                    "Solid_HullStern",
                    new Vector3(bodyBounds.center.x, sideY, bodyBounds.min.z + sternThickness * 0.5f),
                    new Vector3(sternWidth, sideHeight, sternThickness));

                float bowThickness = 0.24f;
                float bowWidth = Mathf.Max(1.4f, bodyBounds.size.x * 0.56f);
                CreateSolidBox(
                    solidRoot,
                    "Solid_HullBow",
                    new Vector3(bodyBounds.center.x, sideY, bodyBounds.max.z - bowThickness * 0.5f),
                    new Vector3(bowWidth, sideHeight, bowThickness));
            }
        }

        private static void AddLadderVolumes(
            Transform solidRoot,
            Transform climbRoot,
            Transform interactionRoot,
            Transform visualShip,
            int climbLayer)
        {
            string[] ladderNames = { "StylShip_StairsBot", "StylShip_StairsTop", "StylShip_StairsSmall" };
            foreach (string ladderName in ladderNames)
            {
                var stairs = FindChildByName(visualShip, ladderName);
                if (stairs == null)
                    continue;

                if (!TryGetWorldBounds(stairs, out Bounds bounds))
                    continue;

                Vector3 solidSize = Vector3.Scale(bounds.size, new Vector3(0.95f, 0.92f, 0.95f));
                solidSize.y = Mathf.Max(solidSize.y, 1.2f);
                CreateSolidBox(solidRoot, $"{ladderName}_Solid", bounds.center + Vector3.up * 0.08f, solidSize);

                Vector3 climbSize = Vector3.Scale(solidSize, new Vector3(0.95f, 1.05f, 0.95f));
                CreateClimbBox(climbRoot, $"{ladderName}_Climb", bounds.center + Vector3.up * 0.18f, climbSize, climbLayer);

                CreateInteractionBox(
                    interactionRoot,
                    $"{ladderName}_Interact",
                    bounds.center + Vector3.up * 0.25f,
                    new Vector3(0.95f, 1.15f, 0.95f),
                    ShipInteractionZone.ZoneKind.Ladder,
                    "Climb");
            }
        }

        private static void AddInteractionZones(Transform interactionRoot, Transform visualShip)
        {
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_Wheel", ShipInteractionZone.ZoneKind.Helm, "Steer", new Vector3(0.85f, 0.95f, 0.95f), new Vector3(0f, 0.1f, 0f));
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_Anchor", ShipInteractionZone.ZoneKind.Anchor, "Raise Anchor", new Vector3(0.90f, 0.95f, 1.0f), new Vector3(0f, 0.05f, 0f));
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_Door1", ShipInteractionZone.ZoneKind.Door, "Open Door", new Vector3(0.85f, 0.95f, 0.85f), Vector3.zero);
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_Door2", ShipInteractionZone.ZoneKind.Door, "Open Door", new Vector3(0.85f, 0.95f, 0.85f), Vector3.zero);

            AddInteractionForPart(visualShip, interactionRoot, "StylShip_WireFront", ShipInteractionZone.ZoneKind.Rope, "Grab Rope", new Vector3(0.75f, 0.75f, 0.75f), Vector3.zero);
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_WireMid", ShipInteractionZone.ZoneKind.Rope, "Grab Rope", new Vector3(0.75f, 0.75f, 0.75f), Vector3.zero);
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_WireBack", ShipInteractionZone.ZoneKind.Rope, "Grab Rope", new Vector3(0.75f, 0.75f, 0.75f), Vector3.zero);
            AddInteractionForPart(visualShip, interactionRoot, "StylShip_Ropes", ShipInteractionZone.ZoneKind.Rope, "Grab Rope", new Vector3(0.75f, 0.75f, 0.75f), Vector3.zero);
        }

        private static void AddWalkableTriggers(Transform traversalRoot, Transform visualShip)
        {
            string[] walkableParts =
            {
                "StylShip_FloorFront",
                "StylShip_FloorMid",
                "StylShip_FloorBackBottom",
                "StylShip_FloorBackTop",
                "StylShip_StairsBot",
                "StylShip_StairsTop",
                "StylShip_StairsSmall",
            };

            foreach (string partName in walkableParts)
            {
                var part = FindChildByName(visualShip, partName);
                if (part == null || !TryGetWorldBounds(part, out Bounds bounds))
                    continue;

                Vector3 size = Vector3.Scale(bounds.size, new Vector3(0.86f, 1.35f, 0.86f));
                size.y = Mathf.Max(size.y, 0.95f);
                CreateZoneTrigger(
                    traversalRoot,
                    $"Walkable_{partName}",
                    bounds.center + Vector3.up * 0.35f,
                    size);
            }
        }

        private static void AddLoopholeBlockers(Transform solidRoot, Transform traversalRoot, Transform interactionRoot, Transform visualShip)
        {
            var loopholes = FindChildByName(visualShip, "StylShip_Loopholes");
            if (loopholes == null || !TryGetWorldBounds(loopholes, out Bounds b))
                return;

            float wallThickness = 0.28f;
            float blockerHeight = Mathf.Max(1.4f, b.size.y * 1.02f);
            float blockerDepth = b.size.z * 0.86f;

            Vector3 portCenter = new Vector3(b.min.x + wallThickness * 0.5f, b.center.y, b.center.z);
            Vector3 starCenter = new Vector3(b.max.x - wallThickness * 0.5f, b.center.y, b.center.z);
            Vector3 blockerSize = new Vector3(wallThickness, blockerHeight, blockerDepth);

            CreateSolidBox(solidRoot, "Blocked_Loopholes_Port", portCenter, blockerSize);
            CreateSolidBox(solidRoot, "Blocked_Loopholes_Starboard", starCenter, blockerSize);

            CreateZoneTrigger(
                traversalRoot,
                "Blocked_Loopholes_Port_Zone",
                portCenter,
                blockerSize);
            CreateZoneTrigger(
                traversalRoot,
                "Blocked_Loopholes_Starboard_Zone",
                starCenter,
                blockerSize);

            CreateInteractionBox(
                interactionRoot,
                "OarPort_Left",
                portCenter + new Vector3(0.26f, 0.12f, 0f),
                new Vector3(0.72f, 0.92f, 1.7f),
                ShipInteractionZone.ZoneKind.OarPort,
                "Use Oars");
            CreateInteractionBox(
                interactionRoot,
                "OarPort_Right",
                starCenter + new Vector3(-0.26f, 0.12f, 0f),
                new Vector3(0.72f, 0.92f, 1.7f),
                ShipInteractionZone.ZoneKind.OarPort,
                "Use Oars");
        }

        private static void AddSolidForPart(
            Transform visualShip,
            Transform solidRoot,
            string partName,
            string solidName,
            Vector3 scale,
            Vector3 offset,
            Vector3 minSize)
        {
            var part = FindChildByName(visualShip, partName);
            if (part == null || !TryGetWorldBounds(part, out Bounds b))
                return;

            Vector3 size = Vector3.Scale(b.size, scale);
            size.x = Mathf.Max(size.x, minSize.x);
            size.y = Mathf.Max(size.y, minSize.y);
            size.z = Mathf.Max(size.z, minSize.z);

            CreateSolidBox(solidRoot, solidName, b.center + offset, size);
        }

        private static void AddInteractionForPart(
            Transform visualShip,
            Transform interactionRoot,
            string partName,
            ShipInteractionZone.ZoneKind kind,
            string prompt,
            Vector3 sizeMultiplier,
            Vector3 centerOffset)
        {
            var part = FindChildByName(visualShip, partName);
            if (part == null || !TryGetWorldBounds(part, out Bounds b))
                return;

            Vector3 size = Vector3.Scale(b.size, sizeMultiplier);
            size.x = Mathf.Clamp(size.x, 0.45f, 4.0f);
            size.y = Mathf.Clamp(size.y, 0.55f, 4.5f);
            size.z = Mathf.Clamp(size.z, 0.45f, 4.0f);
            CreateInteractionBox(interactionRoot, partName + "_Zone", b.center + centerOffset, size, kind, prompt);
        }

        private static void SyncVisualElementColliders(Transform visualShip)
        {
            if (visualShip == null)
                return;

            foreach (Transform t in visualShip.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == visualShip)
                    continue;

                bool ropeLike = IsRopeLikePart(t.name);
                Collider[] existing = t.GetComponents<Collider>();

                if (ropeLike)
                {
                    for (int i = 0; i < existing.Length; i++)
                    {
                        Collider c = existing[i];
                        if (c != null)
                            c.enabled = false;
                    }

                    continue;
                }

                for (int i = 0; i < existing.Length; i++)
                {
                    Collider c = existing[i];
                    if (c == null)
                        continue;
                    c.isTrigger = false;
                    c.enabled = true;
                }

                bool mastLike = IsMastLikePart(t.name);
                bool sailLike = IsSailLikePart(t.name);
                if (!mastLike && !sailLike)
                    continue;
                if (!TryGetLocalBoundsFromMesh(t, out Bounds localBounds))
                    continue;

                EnsureInteractiveHelperCollider(t, localBounds, mastLike);
            }
        }

        private static void EnsureInteractiveHelperCollider(Transform target, Bounds localBounds, bool mastLike)
        {
            string helperName = mastLike ? "Interactive_MastCollider" : "Interactive_SailCollider";
            Transform helper = target.Find(helperName);
            if (helper == null)
            {
                helper = new GameObject(helperName).transform;
                helper.SetParent(target, false);
            }

            Vector3 size = localBounds.size;
            Vector3 center = localBounds.center;
            if (mastLike)
            {
                if (!helper.TryGetComponent(out CapsuleCollider capsule))
                {
                    Collider other = helper.GetComponent<Collider>();
                    if (other != null)
                        Object.DestroyImmediate(other);
                    capsule = helper.gameObject.AddComponent<CapsuleCollider>();
                }

                capsule.center = new Vector3(0f, center.y, 0f);
                capsule.direction = 1;
                capsule.radius = Mathf.Max(0.03f, Mathf.Min(size.x, size.z) * 0.5f);
                capsule.radius = Mathf.Clamp(capsule.radius, 0.08f, 0.32f);
                capsule.height = Mathf.Max(size.y, capsule.radius * 2.05f);

                capsule.isTrigger = false;
                capsule.enabled = true;
                return;
            }

            if (!helper.TryGetComponent(out BoxCollider box))
            {
                Collider other = helper.GetComponent<Collider>();
                if (other != null)
                    Object.DestroyImmediate(other);
                box = helper.gameObject.AddComponent<BoxCollider>();
            }

            box.center = center;

            int thinAxis = 2;
            float minAxis = size.z;
            if (size.x < minAxis) { minAxis = size.x; thinAxis = 0; }
            if (size.y < minAxis) { thinAxis = 1; }
            const float minThickness = 0.05f;
            if (thinAxis == 0) size.x = Mathf.Max(size.x, minThickness);
            else if (thinAxis == 1) size.y = Mathf.Max(size.y, minThickness);
            else size.z = Mathf.Max(size.z, minThickness);

            box.size = new Vector3(
                Mathf.Max(0.02f, size.x),
                Mathf.Max(0.02f, size.y),
                Mathf.Max(0.02f, size.z));
            box.isTrigger = false;
            box.enabled = true;
        }

        private static bool TryGetLocalBoundsFromMesh(Transform target, out Bounds localBounds)
        {
            localBounds = default;

            if (target.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                return true;
            }

            if (target.TryGetComponent(out SpriteRenderer spriteRenderer))
            {
                localBounds = spriteRenderer.localBounds;
                return true;
            }

            if (target.TryGetComponent(out Renderer renderer))
            {
                Vector3 localCenter = target.InverseTransformPoint(renderer.bounds.center);
                Vector3 lossy = target.lossyScale;
                Vector3 localSize = new Vector3(
                    renderer.bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
                    renderer.bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
                    renderer.bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
                localBounds = new Bounds(localCenter, localSize);
                return true;
            }

            return false;
        }

        private static bool IsRopeLikePart(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.StartsWith("StylShip_Wire") ||
                   name.StartsWith("StylShip_Ropes") ||
                   name.IndexOf("rope", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("wire", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMastLikePart(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("mast", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("crow", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("climb", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsSailLikePart(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("sail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("spanker", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("jib", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void MakeDecorativePartsPassThrough(Transform visualShip)
        {
            string[] passThroughPrefixes =
            {
                "StylShip_Wire",
                "StylShip_Ropes",
            };

            foreach (Transform t in visualShip.GetComponentsInChildren<Transform>(true))
            {
                for (int i = 0; i < passThroughPrefixes.Length; i++)
                {
                    if (!t.name.StartsWith(passThroughPrefixes[i]))
                        continue;

                    foreach (Collider col in t.GetComponents<Collider>())
                        col.enabled = false;

                    break;
                }
            }
        }

        private static void CreateClimbCapsule(Transform parent, string name, Vector3 worldCenter, float radius, float height, int climbLayer)
        {
            var go = new GameObject(name);
            go.layer = climbLayer >= 0 ? climbLayer : 0;
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;

            var col = go.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.direction = 1;
            col.radius = radius;
            col.height = Mathf.Max(height, radius * 2.05f);

            var climbable = go.AddComponent<Climbable>();
            climbable.ClimbAxisLocal = Vector3.up;
        }

        private static void CreateClimbBox(Transform parent, string name, Vector3 worldCenter, Vector3 size, int climbLayer, Vector3 climbAxisLocal = default)
        {
            var go = new GameObject(name);
            go.layer = climbLayer >= 0 ? climbLayer : 0;
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;

            var climbable = go.AddComponent<Climbable>();
            climbable.ClimbAxisLocal = climbAxisLocal == default ? Vector3.up : climbAxisLocal;
        }

        private static void CreateSolidBox(Transform parent, string name, Vector3 worldCenter, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size = size;
        }

        private static void CreateInteractionBox(
            Transform parent,
            string name,
            Vector3 worldCenter,
            Vector3 size,
            ShipInteractionZone.ZoneKind kind,
            string prompt)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;

            var zone = go.AddComponent<ShipInteractionZone>();
            zone.Configure(kind, prompt);
        }

        private static void CreateZoneTrigger(
            Transform parent,
            string name,
            Vector3 worldCenter,
            Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;
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
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }

            return null;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static GameObject FindOrCreate(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go : new GameObject(name);
        }

        private static int EnsureLayer(string layerName)
        {
            int existingLayer = LayerMask.NameToLayer(layerName);
            if (existingLayer >= 0)
                return existingLayer;

            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                return -1;

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                var layerProp = layers.GetArrayElementAtIndex(i);
                if (layerProp.stringValue == layerName)
                    return i;
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                var layerProp = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layerProp.stringValue))
                    continue;

                layerProp.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            Debug.LogWarning($"[StylizedShipGameplaySetup] No free user layer slot for '{layerName}'.");
            return -1;
        }

        private static void EnsureNetworkPlayerClimbMask(int climbLayer)
        {
            if (climbLayer < 0)
                return;

            var prefabRoot = PrefabUtility.LoadPrefabContents(NetworkPlayerPrefabPath);
            if (prefabRoot == null)
                return;

            try
            {
                MonoBehaviour networkPlayer = null;
                foreach (var mono in prefabRoot.GetComponents<MonoBehaviour>())
                {
                    if (mono != null && mono.GetType().Name == "NetworkPlayer")
                    {
                        networkPlayer = mono;
                        break;
                    }
                }

                if (networkPlayer == null)
                    return;

                var so = new SerializedObject(networkPlayer);
                var climbMaskProp = so.FindProperty("climbMask");
                if (climbMaskProp == null)
                    return;

                var bitsProp = climbMaskProp.FindPropertyRelative("m_Bits");
                int currentBits = bitsProp != null ? bitsProp.intValue : climbMaskProp.intValue;
                int updatedBits = currentBits | (1 << climbLayer);

                if (bitsProp != null)
                    bitsProp.intValue = updatedBits;
                else
                    climbMaskProp.intValue = updatedBits;

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, NetworkPlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Shader ResolveStylizedShipShader()
        {
            var shader = Shader.Find(UrpLitShaderPath);
            if (shader != null)
                return shader;

            return Shader.Find(UrpSimpleLitShaderPath);
        }

        private static bool ConvertMaterialToUrp(Material material, Shader targetShader)
        {
            Color baseColor = ReadColor(material, PropColor, PropBaseColor, Color.white);
            Texture baseMap = ReadTexture(material, PropMainTex, PropBaseMap);
            Vector2 baseScale = ReadTextureScale(material, PropMainTex, PropBaseMap);
            Vector2 baseOffset = ReadTextureOffset(material, PropMainTex, PropBaseMap);

            Texture normalMap = ReadTexture(material, PropBumpMap, PropNormalMap);
            float normalScale = ReadFloat(material, PropBumpScale, PropNormalScale, 1f);

            Texture metallicGlossMap = ReadTexture(material, PropMetallicGlossMap, PropMetallicGlossMap);
            float metallic = ReadFloat(material, PropMetallic, PropMetallic, 0f);
            float smoothness = ReadFloat(material, PropGlossiness, PropSmoothness, 0.5f);

            Texture emissionMap = ReadTexture(material, PropEmissionMap, PropEmissionMap);
            Color emissionColor = ReadColor(material, PropEmissionColor, PropEmissionColor, Color.black);
            bool emissionEnabled = material.IsKeywordEnabled("_EMISSION") || emissionColor.maxColorComponent > 0.001f || emissionMap != null;

            float cutoff = ReadFloat(material, PropCutoff, PropCutoff, 0.5f);
            bool alphaClipEnabled = material.IsKeywordEnabled("_ALPHATEST_ON");

            bool shaderChanged = material.shader != targetShader;
            if (shaderChanged)
                material.shader = targetShader;

            if (material.HasProperty(PropBaseColor))
                material.SetColor(PropBaseColor, baseColor);
            if (material.HasProperty(PropColor))
                material.SetColor(PropColor, baseColor);

            if (material.HasProperty(PropBaseMap))
            {
                material.SetTexture(PropBaseMap, baseMap);
                material.SetTextureScale(PropBaseMap, baseScale);
                material.SetTextureOffset(PropBaseMap, baseOffset);
            }

            if (material.HasProperty(PropBumpMap))
                material.SetTexture(PropBumpMap, normalMap);
            if (material.HasProperty(PropBumpScale))
                material.SetFloat(PropBumpScale, normalScale);

            if (normalMap != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");

            if (material.HasProperty(PropMetallicGlossMap))
                material.SetTexture(PropMetallicGlossMap, metallicGlossMap);
            if (material.HasProperty(PropMetallic))
                material.SetFloat(PropMetallic, metallic);
            if (material.HasProperty(PropSmoothness))
                material.SetFloat(PropSmoothness, smoothness);

            if (material.HasProperty(PropAlphaClip))
                material.SetFloat(PropAlphaClip, alphaClipEnabled ? 1f : 0f);
            if (material.HasProperty(PropCutoff))
                material.SetFloat(PropCutoff, cutoff);

            if (material.HasProperty(PropEmissionColor))
                material.SetColor(PropEmissionColor, emissionEnabled ? emissionColor : Color.black);
            if (material.HasProperty(PropEmissionMap))
                material.SetTexture(PropEmissionMap, emissionEnabled ? emissionMap : null);

            if (emissionEnabled)
                material.EnableKeyword("_EMISSION");
            else
                material.DisableKeyword("_EMISSION");

            EditorUtility.SetDirty(material);
            return true;
        }

        private static Color ReadColor(Material material, int primary, int fallback, Color defaultValue)
        {
            if (material.HasProperty(primary))
                return material.GetColor(primary);
            if (material.HasProperty(fallback))
                return material.GetColor(fallback);
            return defaultValue;
        }

        private static Texture ReadTexture(Material material, int primary, int fallback)
        {
            if (material.HasProperty(primary))
                return material.GetTexture(primary);
            if (material.HasProperty(fallback))
                return material.GetTexture(fallback);
            return null;
        }

        private static Vector2 ReadTextureScale(Material material, int primary, int fallback)
        {
            if (material.HasProperty(primary))
                return material.GetTextureScale(primary);
            if (material.HasProperty(fallback))
                return material.GetTextureScale(fallback);
            return Vector2.one;
        }

        private static Vector2 ReadTextureOffset(Material material, int primary, int fallback)
        {
            if (material.HasProperty(primary))
                return material.GetTextureOffset(primary);
            if (material.HasProperty(fallback))
                return material.GetTextureOffset(fallback);
            return Vector2.zero;
        }

        private static float ReadFloat(Material material, int primary, int fallback, float defaultValue)
        {
            if (material.HasProperty(primary))
                return material.GetFloat(primary);
            if (material.HasProperty(fallback))
                return material.GetFloat(fallback);
            return defaultValue;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void EnsureShipPhysicsComponents(GameObject shipRoot)
        {
            var rb = shipRoot.GetComponent<Rigidbody>();
            if (rb == null)
                rb = shipRoot.AddComponent<Rigidbody>();

            rb.mass = Mathf.Max(1800f, rb.mass);
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var buoyancy = shipRoot.GetComponent<ShipBuoyancyController>();
            if (buoyancy == null)
                buoyancy = shipRoot.AddComponent<ShipBuoyancyController>();

            var deckMotion = shipRoot.GetComponent<ShipDeckMotionProvider>();
            if (deckMotion == null)
                deckMotion = shipRoot.AddComponent<ShipDeckMotionProvider>();

            buoyancy.ConfigureReferences(null, rb);
            deckMotion.ConfigureReferences(buoyancy, rb);
        }

        private static void WireShipRuntimeReferences(GameObject shipRoot)
        {
            var rb = shipRoot.GetComponent<Rigidbody>();
            var buoyancy = shipRoot.GetComponent<ShipBuoyancyController>();
            var deckMotion = shipRoot.GetComponent<ShipDeckMotionProvider>();
            var fx = shipRoot.GetComponent<ShipWaterFxController>();
            var waveField = Object.FindAnyObjectByType<OceanWaveField>();

            if (rb != null && buoyancy != null)
            {
                buoyancy.ConfigureReferences(waveField, rb);
                EditorUtility.SetDirty(buoyancy);
            }

            if (deckMotion != null && buoyancy != null && rb != null)
            {
                deckMotion.ConfigureReferences(buoyancy, rb);
                EditorUtility.SetDirty(deckMotion);
            }

            if (fx != null && buoyancy != null)
            {
                fx.ConfigureReferences(buoyancy, null);
                EditorUtility.SetDirty(fx);
            }
        }
    }
}

