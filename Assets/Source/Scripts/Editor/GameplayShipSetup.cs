using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Source.Scripts.Editor
{
    /// <summary>
    /// Builds and updates the moored ship placeholder independently from the rest of environment setup.
    /// </summary>
    public static class GameplayShipSetup
    {
        private enum ShipDetailLevel
        {
            Standard,
            High,
        }

        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string EnvironmentRootName = "Environment";
        private const string PrefabsFolderPath = "Assets/Source/Environment/Prefabs";
        private const string ShipPrefabPath = "Assets/Source/Environment/Prefabs/MooredShipPlaceholder.prefab";
        private const string WoodMaterialPath = "Assets/Source/Environment/Materials/Wood.mat";
        private const string HullMaterialPath = "Assets/Source/Environment/Materials/Hull.mat";
        private const string RiggingMaterialPath = "Assets/Source/Environment/Materials/Rigging.mat";
        private const string MetalMaterialPath = "Assets/Source/Environment/Materials/Metal.mat";
        private const string SailMaterialPath = "Assets/Source/Environment/Materials/Sail.mat";

        private static readonly Vector3 ShipScenePosition = new Vector3(56f, 1.1f, 8f);
        private static readonly Quaternion ShipSceneRotation = Quaternion.Euler(0f, 176f, 0f);

        [MenuItem("RumOverboard/Setup/Build or Update Gameplay Ship")]
        public static void BuildOrUpdateGameplayShip()
        {
            BuildOrUpdateGameplayShipInternal(ShipDetailLevel.High);
        }

        [MenuItem("RumOverboard/Setup/Build or Update Gameplay Ship (Standard Detail)")]
        public static void BuildOrUpdateGameplayShipStandard()
        {
            BuildOrUpdateGameplayShipInternal(ShipDetailLevel.Standard);
        }

        [MenuItem("RumOverboard/Setup/Build or Update Gameplay Ship (High Detail)")]
        public static void BuildOrUpdateGameplayShipHighDetail()
        {
            BuildOrUpdateGameplayShipInternal(ShipDetailLevel.High);
        }

        [MenuItem("RumOverboard/Setup/Place Gameplay Ship Prefab In Scene")]
        public static void PlaceGameplayShipPrefabInScene()
        {
            var shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (shipPrefab == null)
            {
                Debug.LogError($"[GameplayShipSetup] Ship prefab not found at '{ShipPrefabPath}'. Build the ship prefab first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            var environmentRoot = FindOrCreate(EnvironmentRootName);

            var existingShip = GameObject.Find("MooredShipPlaceholder");
            Vector3 targetPosition = ShipScenePosition;
            Quaternion targetRotation = ShipSceneRotation;
            Vector3 targetScale = Vector3.one;

            if (existingShip != null)
            {
                targetPosition = existingShip.transform.position;
                targetRotation = existingShip.transform.rotation;
                targetScale = existingShip.transform.localScale;
            }

            GameObject shipInstance;
            if (existingShip != null && IsConnectedToPrefab(existingShip, shipPrefab))
            {
                shipInstance = existingShip;
            }
            else
            {
                if (existingShip != null)
                    Object.DestroyImmediate(existingShip);

                shipInstance = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab, scene);
                shipInstance.name = "MooredShipPlaceholder";
            }

            shipInstance.transform.SetParent(environmentRoot.transform);
            shipInstance.transform.SetPositionAndRotation(targetPosition, targetRotation);
            shipInstance.transform.localScale = targetScale;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[GameplayShipSetup] Gameplay ship prefab has been placed in scene.");
        }

        private static void BuildOrUpdateGameplayShipInternal(ShipDetailLevel detailLevel)
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Materials");
            EnsureFolder(PrefabsFolderPath);

            Material wood = EnsureMaterial(WoodMaterialPath, new Color(0.36f, 0.24f, 0.14f));
            Material hull = EnsureMaterial(HullMaterialPath, new Color(0.20f, 0.13f, 0.09f));
            Material sail = EnsureSailMaterial(new Color(0.93f, 0.93f, 0.90f));
            Material rigging = EnsureMaterial(RiggingMaterialPath, new Color(0.10f, 0.08f, 0.07f));
            Material metal = EnsureMaterial(MetalMaterialPath, new Color(0.33f, 0.36f, 0.40f));

            var shipRoot = new GameObject("MooredShipPlaceholder");
            shipRoot.transform.position = Vector3.zero;
            shipRoot.transform.rotation = Quaternion.identity;
            shipRoot.transform.localScale = Vector3.one;

            BuildShipGeometry(shipRoot.transform, hull, wood, sail, rigging, metal, detailLevel);

            var prefab = PrefabUtility.SaveAsPrefabAsset(shipRoot, ShipPrefabPath);
            Object.DestroyImmediate(shipRoot);

            if (prefab == null)
            {
                Debug.LogError($"[GameplayShipSetup] Failed to build ship prefab at '{ShipPrefabPath}'.");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[GameplayShipSetup] Gameplay ship prefab has been built or updated at '{ShipPrefabPath}'.");
        }

        public static void BuildOrUpdateGameplayShipFromCommandLine()
        {
            BuildOrUpdateGameplayShip();
        }

        public static void PlaceGameplayShipPrefabInSceneFromCommandLine()
        {
            PlaceGameplayShipPrefabInScene();
        }

        public static void BuildShip(GameObject root, Material hull, Material wood)
        {
            BuildShip(root, hull, wood, ShipDetailLevel.High);
        }

        private static void BuildShip(GameObject root, Material hull, Material wood, ShipDetailLevel detailLevel)
        {
            var ship = FindOrCreateChild(root.transform, "MooredShipPlaceholder");
            ship.transform.SetParent(root.transform);
            ship.transform.position = ShipScenePosition;
            ship.transform.rotation = ShipSceneRotation;
            ship.transform.localScale = Vector3.one;

            ClearChildren(ship.transform);

            Material sail = EnsureSailMaterial(new Color(0.93f, 0.93f, 0.90f));
            Material rigging = EnsureMaterial(RiggingMaterialPath, new Color(0.10f, 0.08f, 0.07f));
            Material metal = EnsureMaterial(MetalMaterialPath, new Color(0.33f, 0.36f, 0.40f));

            BuildShipGeometry(ship.transform, hull, wood, sail, rigging, metal, detailLevel);
        }

        private static void BuildShipGeometry(
            Transform ship,
            Material hull,
            Material wood,
            Material sail,
            Material rigging,
            Material metal,
            ShipDetailLevel detailLevel)
        {
            ProceduralBrigShipMeshBuilder.BuildShipMesh(
                ship,
                hull,
                wood,
                sail,
                rigging,
                metal,
                detailLevel == ShipDetailLevel.High);
        }

        private static void BuildHull(Transform ship, Material hull, Material wood)
        {
            // Mercuriy-like brig profile: fuller midship, finer bow entry, and elevated stern counter.
            CreatePart(ship, "HullMidLower", PrimitiveType.Cube, hull, new Vector3(0f, -0.35f, -0.2f), new Vector3(5.2f, 1.7f, 12.8f), Vector3.zero);
            CreatePart(ship, "HullMidUpper", PrimitiveType.Cube, hull, new Vector3(0f, 0.55f, -0.2f), new Vector3(4.6f, 1.4f, 11.4f), Vector3.zero);

            CreatePart(ship, "BowEntryLower", PrimitiveType.Cube, hull, new Vector3(0f, -0.25f, 8.9f), new Vector3(3.2f, 1.4f, 4.0f), new Vector3(7f, 0f, 0f));
            CreatePart(ship, "BowEntryUpper", PrimitiveType.Cube, hull, new Vector3(0f, 0.85f, 8.5f), new Vector3(2.7f, 1.0f, 2.9f), new Vector3(10f, 0f, 0f));

            CreatePart(ship, "SternRunLower", PrimitiveType.Cube, hull, new Vector3(0f, -0.2f, -8.0f), new Vector3(3.8f, 1.5f, 3.2f), new Vector3(-8f, 0f, 0f));
            CreatePart(ship, "SternRunUpper", PrimitiveType.Cube, hull, new Vector3(0f, 0.95f, -8.0f), new Vector3(3.4f, 1.1f, 2.5f), new Vector3(-6f, 0f, 0f));

            CreatePart(ship, "Keel", PrimitiveType.Cube, hull, new Vector3(0f, -1.2f, -0.1f), new Vector3(0.92f, 0.62f, 17.4f), Vector3.zero);
            CreatePart(ship, "StemPost", PrimitiveType.Cylinder, wood, new Vector3(0f, 0.15f, 10.9f), new Vector3(0.12f, 1.25f, 0.12f), new Vector3(-20f, 0f, 0f), false);
            CreatePart(ship, "SternPost", PrimitiveType.Cylinder, wood, new Vector3(0f, -0.1f, -9.45f), new Vector3(0.12f, 1.1f, 0.12f), new Vector3(14f, 0f, 0f), false);

            CreatePart(ship, "PortBulwark", PrimitiveType.Cube, wood, new Vector3(-2.42f, 1.0f, -0.4f), new Vector3(0.30f, 0.72f, 14.4f), Vector3.zero);
            CreatePart(ship, "StarboardBulwark", PrimitiveType.Cube, wood, new Vector3(2.42f, 1.0f, -0.4f), new Vector3(0.30f, 0.72f, 14.4f), Vector3.zero);
            CreatePart(ship, "SternTransom", PrimitiveType.Cube, wood, new Vector3(0f, 1.45f, -9.2f), new Vector3(3.2f, 1.3f, 0.32f), Vector3.zero);
            CreatePart(ship, "TumblehomePort", PrimitiveType.Cube, wood, new Vector3(-2.16f, 1.43f, -0.3f), new Vector3(0.18f, 0.46f, 13.2f), new Vector3(0f, 0f, 8f), false);
            CreatePart(ship, "TumblehomeStarboard", PrimitiveType.Cube, wood, new Vector3(2.16f, 1.43f, -0.3f), new Vector3(0.18f, 0.46f, 13.2f), new Vector3(0f, 0f, -8f), false);

            CreatePart(ship, "BeakheadPlatform", PrimitiveType.Cube, wood, new Vector3(0f, 1.9f, 10.35f), new Vector3(2.0f, 0.16f, 1.25f), Vector3.zero);
            CreatePart(ship, "FigureheadStub", PrimitiveType.Cylinder, wood, new Vector3(0f, 1.6f, 11.2f), new Vector3(0.10f, 0.42f, 0.10f), new Vector3(0f, 0f, 90f), false);
        }

        private static void BuildDeckAndFittings(Transform ship, Material wood, Material metal)
        {
            CreatePart(ship, "MainDeck", PrimitiveType.Cube, wood, new Vector3(0f, 1.20f, -0.4f), new Vector3(4.4f, 0.22f, 13.5f), Vector3.zero);
            CreatePart(ship, "ForecastleDeck", PrimitiveType.Cube, wood, new Vector3(0f, 1.62f, 4.8f), new Vector3(3.8f, 0.22f, 4.2f), Vector3.zero);
            CreatePart(ship, "QuarterDeck", PrimitiveType.Cube, wood, new Vector3(0f, 1.86f, -5.0f), new Vector3(3.4f, 0.22f, 5.7f), Vector3.zero);
            CreatePart(ship, "PoopDeck", PrimitiveType.Cube, wood, new Vector3(0f, 2.30f, -7.5f), new Vector3(2.6f, 0.20f, 2.6f), Vector3.zero);

            CreatePart(ship, "AftCabin", PrimitiveType.Cube, wood, new Vector3(0f, 2.22f, -6.4f), new Vector3(2.4f, 0.90f, 2.9f), Vector3.zero);
            CreatePart(ship, "AftCabinRoof", PrimitiveType.Cube, wood, new Vector3(0f, 2.88f, -6.4f), new Vector3(2.8f, 0.16f, 3.1f), Vector3.zero);
            CreatePart(ship, "Companionway", PrimitiveType.Cube, wood, new Vector3(0f, 1.62f, -3.9f), new Vector3(1.2f, 0.54f, 1.0f), Vector3.zero);

            CreatePart(ship, "Hatch_Main", PrimitiveType.Cube, wood, new Vector3(0f, 1.47f, -0.9f), new Vector3(1.45f, 0.34f, 1.25f), Vector3.zero);
            CreatePart(ship, "Hatch_Fore", PrimitiveType.Cube, wood, new Vector3(0f, 1.50f, 2.6f), new Vector3(1.25f, 0.32f, 1.1f), Vector3.zero);

            CreatePart(ship, "Bowsprit", PrimitiveType.Cylinder, wood, new Vector3(0f, 2.15f, 10.9f), new Vector3(0.13f, 3.3f, 0.13f), new Vector3(82f, 0f, 0f));
            CreatePart(ship, "Jibboom", PrimitiveType.Cylinder, wood, new Vector3(0f, 2.45f, 13.9f), new Vector3(0.10f, 2.1f, 0.10f), new Vector3(82f, 0f, 0f));

            CreatePart(ship, "Rudder", PrimitiveType.Cube, wood, new Vector3(0f, -0.58f, -9.55f), new Vector3(0.58f, 1.95f, 0.23f), new Vector3(6f, 0f, 0f));
            CreatePart(ship, "RudderHinge", PrimitiveType.Cube, metal, new Vector3(0f, -0.28f, -9.30f), new Vector3(0.70f, 0.10f, 0.05f), Vector3.zero);

            CreatePart(ship, "Anchor_Port", PrimitiveType.Cylinder, metal, new Vector3(-2.58f, 0.9f, 7.5f), new Vector3(0.16f, 0.58f, 0.16f), new Vector3(0f, 0f, 90f));
            CreatePart(ship, "Anchor_Starboard", PrimitiveType.Cylinder, metal, new Vector3(2.58f, 0.9f, 7.5f), new Vector3(0.16f, 0.58f, 0.16f), new Vector3(0f, 0f, 90f));

            CreatePart(ship, "HelmWheel", PrimitiveType.Cylinder, wood, new Vector3(0f, 2.48f, -7.1f), new Vector3(0.48f, 0.06f, 0.48f), new Vector3(90f, 0f, 0f));
            CreatePart(ship, "Capstan", PrimitiveType.Cylinder, wood, new Vector3(0f, 1.73f, 0.1f), new Vector3(0.48f, 0.46f, 0.48f), Vector3.zero);
            CreatePart(ship, "Belfry", PrimitiveType.Cube, wood, new Vector3(0f, 2.02f, 3.2f), new Vector3(0.52f, 0.90f, 0.52f), Vector3.zero, false);
        }

        private static void BuildRigAndSails(Transform ship, Material wood, Material sail, Material rigging)
        {
            // Brig-brigantine rig inspired by the drawing: two square-rig masts + fore-and-aft after sail plan.
            CreatePart(ship, "ForeMast", PrimitiveType.Cylinder, wood, new Vector3(0f, 4.7f, 2.0f), new Vector3(0.21f, 3.4f, 0.21f), Vector3.zero);
            CreatePart(ship, "ForeTopMast", PrimitiveType.Cylinder, wood, new Vector3(0f, 8.2f, 2.0f), new Vector3(0.13f, 2.6f, 0.13f), Vector3.zero, false);
            CreatePart(ship, "ForeTopGallant", PrimitiveType.Cylinder, wood, new Vector3(0f, 10.7f, 2.0f), new Vector3(0.09f, 1.6f, 0.09f), Vector3.zero, false);

            CreatePart(ship, "MainMast", PrimitiveType.Cylinder, wood, new Vector3(0f, 5.0f, -3.8f), new Vector3(0.24f, 3.9f, 0.24f), Vector3.zero);
            CreatePart(ship, "MainTopMast", PrimitiveType.Cylinder, wood, new Vector3(0f, 9.0f, -3.8f), new Vector3(0.14f, 3.0f, 0.14f), Vector3.zero, false);
            CreatePart(ship, "MainTopGallant", PrimitiveType.Cylinder, wood, new Vector3(0f, 11.8f, -3.8f), new Vector3(0.10f, 1.8f, 0.10f), Vector3.zero, false);

            CreatePart(ship, "ForeLowerYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 5.0f, 2.0f), new Vector3(0.08f, 2.5f, 0.08f), new Vector3(0f, 0f, 90f), false);
            CreatePart(ship, "ForeTopsailYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 7.0f, 2.0f), new Vector3(0.07f, 2.1f, 0.07f), new Vector3(0f, 0f, 90f), false);
            CreatePart(ship, "ForeTopgallantYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 9.2f, 2.0f), new Vector3(0.06f, 1.5f, 0.06f), new Vector3(0f, 0f, 90f), false);

            CreatePart(ship, "MainLowerYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 5.4f, -3.8f), new Vector3(0.08f, 2.8f, 0.08f), new Vector3(0f, 0f, 90f), false);
            CreatePart(ship, "MainTopsailYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 7.8f, -3.8f), new Vector3(0.07f, 2.3f, 0.07f), new Vector3(0f, 0f, 90f), false);
            CreatePart(ship, "MainTopgallantYard", PrimitiveType.Cylinder, wood, new Vector3(0f, 10.2f, -3.8f), new Vector3(0.06f, 1.7f, 0.06f), new Vector3(0f, 0f, 90f), false);

            CreatePart(ship, "MainGaff", PrimitiveType.Cylinder, wood, new Vector3(0.95f, 6.2f, -4.2f), new Vector3(0.06f, 2.2f, 0.06f), new Vector3(90f, 0f, -16f), false);
            CreatePart(ship, "MainBoom", PrimitiveType.Cylinder, wood, new Vector3(0.55f, 3.9f, -4.1f), new Vector3(0.08f, 2.5f, 0.08f), new Vector3(90f, 0f, -8f), false);

            // Sail groups kept gameplay-friendly: fore square group, main square/gaff group, and headsails.
            CreatePart(ship, "ForeCourse", PrimitiveType.Cube, sail, new Vector3(0f, 4.3f, 2.1f), new Vector3(4.2f, 2.2f, 0.06f), new Vector3(0f, 90f, 0f));
            CreatePart(ship, "ForeTopsail", PrimitiveType.Cube, sail, new Vector3(0f, 6.8f, 2.1f), new Vector3(3.5f, 1.9f, 0.06f), new Vector3(0f, 90f, 0f));
            CreatePart(ship, "ForeTopgallant", PrimitiveType.Cube, sail, new Vector3(0f, 9.1f, 2.1f), new Vector3(2.4f, 1.3f, 0.06f), new Vector3(0f, 90f, 0f));

            CreatePart(ship, "MainCourse", PrimitiveType.Cube, sail, new Vector3(0f, 4.8f, -3.8f), new Vector3(4.5f, 2.4f, 0.06f), new Vector3(0f, 90f, 0f));
            CreatePart(ship, "MainTopsail", PrimitiveType.Cube, sail, new Vector3(0f, 7.6f, -3.8f), new Vector3(3.7f, 2.0f, 0.06f), new Vector3(0f, 90f, 0f));
            CreatePart(ship, "MainTopgallant", PrimitiveType.Cube, sail, new Vector3(0f, 10.1f, -3.8f), new Vector3(2.7f, 1.4f, 0.06f), new Vector3(0f, 90f, 0f));
            CreatePart(ship, "Spanker", PrimitiveType.Cube, sail, new Vector3(1.20f, 4.8f, -4.3f), new Vector3(4.0f, 3.0f, 0.06f), new Vector3(0f, 90f, -14f));

            CreatePart(ship, "InnerJib", PrimitiveType.Cube, sail, new Vector3(0.65f, 4.5f, 10.1f), new Vector3(2.9f, 1.8f, 0.06f), new Vector3(-16f, 90f, 12f));
            CreatePart(ship, "OuterJib", PrimitiveType.Cube, sail, new Vector3(0.9f, 5.7f, 12.8f), new Vector3(3.1f, 2.0f, 0.06f), new Vector3(-15f, 90f, 11f));
            CreatePart(ship, "FlyingJib", PrimitiveType.Cube, sail, new Vector3(1.05f, 6.7f, 15.3f), new Vector3(2.7f, 1.8f, 0.06f), new Vector3(-14f, 90f, 10f));
            CreatePart(ship, "ForeTopmastStaysail", PrimitiveType.Cube, sail, new Vector3(0.45f, 7.3f, 6.2f), new Vector3(2.6f, 2.0f, 0.06f), new Vector3(-10f, 90f, 8f));

            CreatePart(ship, "ForeStay", PrimitiveType.Cube, rigging, new Vector3(0f, 6.2f, 7.2f), new Vector3(0.05f, 0.05f, 6.0f), new Vector3(44f, 90f, 0f), false);
            CreatePart(ship, "MainStay", PrimitiveType.Cube, rigging, new Vector3(0f, 7.3f, 0.9f), new Vector3(0.05f, 0.05f, 7.7f), new Vector3(35f, 90f, 0f), false);
            CreatePart(ship, "MizzenStay", PrimitiveType.Cube, rigging, new Vector3(0f, 7.9f, -6.6f), new Vector3(0.05f, 0.05f, 4.0f), new Vector3(38f, 90f, 0f), false);
        }

        private static void BuildCrewStations(Transform ship)
        {
            CreateStationMarker(ship, "Station_Helm", new Vector3(0f, 2.0f, -6.8f));
            CreateStationMarker(ship, "Station_Sails", new Vector3(0f, 1.7f, -0.2f));
            CreateStationMarker(ship, "Station_Sails_Fore", new Vector3(0f, 1.7f, 2.0f));
            CreateStationMarker(ship, "Station_Sails_Main", new Vector3(0f, 1.7f, -2.5f));
            CreateStationMarker(ship, "Station_Anchor", new Vector3(0f, 1.7f, 7.0f));
            CreateStationMarker(ship, "Station_Repairs", new Vector3(-1.3f, 1.6f, 0.8f));
            CreateStationMarker(ship, "Station_Guns", new Vector3(1.4f, 1.6f, -0.3f));
        }

        private static void BuildHighDetailPass(
            Transform ship,
            Material hull,
            Material wood,
            Material sail,
            Material rigging,
            Material metal)
        {
            BuildHullPlanking(ship, hull, wood);
            BuildRailings(ship, wood, rigging);
            BuildGunDeckDetails(ship, wood, metal);
            BuildRiggingDetails(ship, rigging);
            BuildMastTopDetails(ship, wood, metal);
            BuildDeckDetails(ship, wood, metal, sail);
            BuildSternDetails(ship, wood, metal);
        }

        private static void BuildHullPlanking(Transform ship, Material hull, Material wood)
        {
            for (int i = 0; i < 8; i++)
            {
                float z = -7.6f + i * 2.2f;
                CreatePart(ship, $"Plank_Port_{i + 1}", PrimitiveType.Cube, hull, new Vector3(-2.68f, 0.25f, z), new Vector3(0.12f, 0.24f, 2.0f), Vector3.zero, false);
                CreatePart(ship, $"Plank_Starboard_{i + 1}", PrimitiveType.Cube, hull, new Vector3(2.68f, 0.25f, z), new Vector3(0.12f, 0.24f, 2.0f), Vector3.zero, false);
            }

            for (int i = 0; i < 5; i++)
            {
                float z = -5.6f + i * 2.8f;
                CreatePart(ship, $"Gunport_Frame_Port_{i + 1}", PrimitiveType.Cube, wood, new Vector3(-2.56f, 1.0f, z), new Vector3(0.07f, 0.55f, 0.55f), Vector3.zero, false);
                CreatePart(ship, $"Gunport_Frame_Starboard_{i + 1}", PrimitiveType.Cube, wood, new Vector3(2.56f, 1.0f, z), new Vector3(0.07f, 0.55f, 0.55f), Vector3.zero, false);
            }

            CreatePart(ship, "Wale_Port", PrimitiveType.Cube, wood, new Vector3(-2.54f, 0.76f, -0.2f), new Vector3(0.13f, 0.22f, 14.6f), Vector3.zero, false);
            CreatePart(ship, "Wale_Starboard", PrimitiveType.Cube, wood, new Vector3(2.54f, 0.76f, -0.2f), new Vector3(0.13f, 0.22f, 14.6f), Vector3.zero, false);
        }

        private static void BuildRailings(Transform ship, Material wood, Material rigging)
        {
            for (int i = 0; i < 13; i++)
            {
                float z = -7.2f + i * 1.2f;
                CreatePart(ship, $"RailPost_Port_{i + 1}", PrimitiveType.Cube, wood, new Vector3(-2.42f, 1.52f, z), new Vector3(0.09f, 0.55f, 0.09f), Vector3.zero, false);
                CreatePart(ship, $"RailPost_Starboard_{i + 1}", PrimitiveType.Cube, wood, new Vector3(2.42f, 1.52f, z), new Vector3(0.09f, 0.55f, 0.09f), Vector3.zero, false);
            }

            CreatePart(ship, "TopRail_Port", PrimitiveType.Cube, wood, new Vector3(-2.42f, 1.78f, -0.3f), new Vector3(0.08f, 0.08f, 14.1f), Vector3.zero, false);
            CreatePart(ship, "TopRail_Starboard", PrimitiveType.Cube, wood, new Vector3(2.42f, 1.78f, -0.3f), new Vector3(0.08f, 0.08f, 14.1f), Vector3.zero, false);

            CreatePart(ship, "SafetyLine_Port", PrimitiveType.Cube, rigging, new Vector3(-2.36f, 1.6f, -0.3f), new Vector3(0.03f, 0.03f, 14.0f), Vector3.zero, false);
            CreatePart(ship, "SafetyLine_Starboard", PrimitiveType.Cube, rigging, new Vector3(2.36f, 1.6f, -0.3f), new Vector3(0.03f, 0.03f, 14.0f), Vector3.zero, false);
        }

        private static void BuildGunDeckDetails(Transform ship, Material wood, Material metal)
        {
            for (int i = 0; i < 4; i++)
            {
                float z = -4.8f + i * 2.8f;

                CreatePart(ship, $"CannonBase_Port_{i + 1}", PrimitiveType.Cube, wood, new Vector3(-1.55f, 1.33f, z), new Vector3(0.55f, 0.16f, 0.75f), Vector3.zero);
                CreatePart(ship, $"CannonBarrel_Port_{i + 1}", PrimitiveType.Cylinder, metal, new Vector3(-1.95f, 1.48f, z), new Vector3(0.10f, 0.50f, 0.10f), new Vector3(0f, 0f, 90f), false);

                CreatePart(ship, $"CannonBase_Starboard_{i + 1}", PrimitiveType.Cube, wood, new Vector3(1.55f, 1.33f, z), new Vector3(0.55f, 0.16f, 0.75f), Vector3.zero);
                CreatePart(ship, $"CannonBarrel_Starboard_{i + 1}", PrimitiveType.Cylinder, metal, new Vector3(1.95f, 1.48f, z), new Vector3(0.10f, 0.50f, 0.10f), new Vector3(0f, 0f, 90f), false);
            }

            for (int i = 0; i < 6; i++)
            {
                float z = -6.0f + i * 2.1f;
                CreatePart(ship, $"Cleat_Port_{i + 1}", PrimitiveType.Cube, wood, new Vector3(-2.16f, 1.42f, z), new Vector3(0.24f, 0.08f, 0.10f), Vector3.zero, false);
                CreatePart(ship, $"Cleat_Starboard_{i + 1}", PrimitiveType.Cube, wood, new Vector3(2.16f, 1.42f, z), new Vector3(0.24f, 0.08f, 0.10f), Vector3.zero, false);
            }
        }

        private static void BuildRiggingDetails(Transform ship, Material rigging)
        {
            BuildShroudSet(ship, "Fore", -2.42f, 2.0f, 8.6f, rigging);
            BuildShroudSet(ship, "Main", -2.48f, -3.8f, 9.2f, rigging);

            CreatePart(ship, "Backstay_Port", PrimitiveType.Cube, rigging, new Vector3(-1.7f, 6.2f, -7.1f), new Vector3(0.03f, 0.03f, 6.6f), new Vector3(-30f, 90f, 0f), false);
            CreatePart(ship, "Backstay_Starboard", PrimitiveType.Cube, rigging, new Vector3(1.7f, 6.2f, -7.1f), new Vector3(0.03f, 0.03f, 6.6f), new Vector3(-30f, 90f, 0f), false);
            CreatePart(ship, "BowspritStay", PrimitiveType.Cube, rigging, new Vector3(0f, 3.6f, 11.5f), new Vector3(0.03f, 0.03f, 9.2f), new Vector3(20f, 90f, 0f), false);
            CreatePart(ship, "Bobstay", PrimitiveType.Cube, rigging, new Vector3(0f, 1.0f, 11.2f), new Vector3(0.03f, 0.03f, 3.6f), new Vector3(65f, 90f, 0f), false);
        }

        private static void BuildMastTopDetails(Transform ship, Material wood, Material metal)
        {
            CreatePart(ship, "ForeTop", PrimitiveType.Cube, wood, new Vector3(0f, 7.0f, 2.0f), new Vector3(1.8f, 0.14f, 1.5f), Vector3.zero, false);
            CreatePart(ship, "MainTop", PrimitiveType.Cube, wood, new Vector3(0f, 7.8f, -3.8f), new Vector3(2.0f, 0.14f, 1.7f), Vector3.zero, false);

            CreatePart(ship, "ForeTopCap", PrimitiveType.Cube, metal, new Vector3(0f, 8.0f, 2.0f), new Vector3(0.32f, 0.18f, 0.32f), Vector3.zero, false);
            CreatePart(ship, "MainTopCap", PrimitiveType.Cube, metal, new Vector3(0f, 8.8f, -3.8f), new Vector3(0.34f, 0.18f, 0.34f), Vector3.zero, false);
        }

        private static void BuildShroudSet(Transform ship, string prefix, float x, float z, float topY, Material rigging)
        {
            for (int i = 0; i < 5; i++)
            {
                float offset = -0.56f + i * 0.28f;
                float lineTopY = topY - i * 0.3f;

                CreatePart(ship, $"{prefix}Shroud_Port_{i + 1}", PrimitiveType.Cube, rigging, new Vector3(x, 4.2f, z + offset), new Vector3(0.03f, lineTopY - 2.8f, 0.03f), new Vector3(0f, 0f, 14f), false);
                CreatePart(ship, $"{prefix}Shroud_Starboard_{i + 1}", PrimitiveType.Cube, rigging, new Vector3(-x, 4.2f, z + offset), new Vector3(0.03f, lineTopY - 2.8f, 0.03f), new Vector3(0f, 0f, -14f), false);
            }

            for (int i = 0; i < 7; i++)
            {
                float y = 3.0f + i * 0.46f;
                CreatePart(ship, $"{prefix}Ratline_Port_{i + 1}", PrimitiveType.Cube, rigging, new Vector3(x + 0.28f, y, z), new Vector3(0.03f, 0.02f, 1.28f), new Vector3(0f, 90f, 0f), false);
                CreatePart(ship, $"{prefix}Ratline_Starboard_{i + 1}", PrimitiveType.Cube, rigging, new Vector3(-x - 0.28f, y, z), new Vector3(0.03f, 0.02f, 1.28f), new Vector3(0f, 90f, 0f), false);
            }
        }

        private static void BuildDeckDetails(Transform ship, Material wood, Material metal, Material sail)
        {
            for (int i = 0; i < 4; i++)
            {
                float z = -4.0f + i * 2.3f;
                CreatePart(ship, $"DeckCrate_{i + 1}", PrimitiveType.Cube, wood, new Vector3(-0.9f, 1.45f, z), new Vector3(0.6f, 0.5f, 0.6f), Vector3.zero);
            }

            CreatePart(ship, "SpareSailBundle", PrimitiveType.Cylinder, sail, new Vector3(1.15f, 1.52f, 0.8f), new Vector3(0.38f, 0.7f, 0.38f), new Vector3(90f, 0f, 0f));
            CreatePart(ship, "LanternAft_Port", PrimitiveType.Cylinder, metal, new Vector3(-0.9f, 2.55f, -7.5f), new Vector3(0.10f, 0.16f, 0.10f), Vector3.zero, false);
            CreatePart(ship, "LanternAft_Starboard", PrimitiveType.Cylinder, metal, new Vector3(0.9f, 2.55f, -7.5f), new Vector3(0.10f, 0.16f, 0.10f), Vector3.zero, false);
            CreatePart(ship, "CompanionLadder", PrimitiveType.Cube, wood, new Vector3(0f, 1.8f, -4.0f), new Vector3(1.0f, 0.9f, 0.2f), new Vector3(48f, 0f, 0f), false);

            CreatePart(ship, "LongboatHull", PrimitiveType.Cube, wood, new Vector3(0f, 2.12f, 0.9f), new Vector3(2.1f, 0.45f, 0.9f), Vector3.zero, false);
            CreatePart(ship, "LongboatKeel", PrimitiveType.Cube, wood, new Vector3(0f, 1.9f, 0.9f), new Vector3(0.55f, 0.2f, 1.0f), Vector3.zero, false);

            for (int i = 0; i < 6; i++)
            {
                float x = -1.2f + i * 0.48f;
                CreatePart(ship, $"Pinrail_Peg_{i + 1}", PrimitiveType.Cylinder, wood, new Vector3(x, 1.98f, -1.1f), new Vector3(0.04f, 0.09f, 0.04f), Vector3.zero, false);
            }
        }

        private static void BuildSternDetails(Transform ship, Material wood, Material metal)
        {
            for (int i = 0; i < 3; i++)
            {
                float x = -0.75f + i * 0.75f;
                CreatePart(ship, $"SternWindow_{i + 1}", PrimitiveType.Cube, metal, new Vector3(x, 2.15f, -9.18f), new Vector3(0.42f, 0.38f, 0.03f), Vector3.zero, false);
            }

            CreatePart(ship, "NamePlate", PrimitiveType.Cube, metal, new Vector3(0f, 1.55f, -9.17f), new Vector3(1.3f, 0.20f, 0.03f), Vector3.zero, false);
            CreatePart(ship, "Taffrail", PrimitiveType.Cube, wood, new Vector3(0f, 1.95f, -8.92f), new Vector3(3.0f, 0.10f, 0.16f), Vector3.zero, false);
            CreatePart(ship, "QuarterGallery_Port", PrimitiveType.Cube, wood, new Vector3(-1.85f, 2.25f, -8.4f), new Vector3(0.45f, 0.85f, 0.9f), Vector3.zero, false);
            CreatePart(ship, "QuarterGallery_Starboard", PrimitiveType.Cube, wood, new Vector3(1.85f, 2.25f, -8.4f), new Vector3(0.45f, 0.85f, 0.9f), Vector3.zero, false);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void CreatePart(
            Transform parent,
            string name,
            PrimitiveType type,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            if (!keepCollider)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }
        }

        private static void CreateStationMarker(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
        }

        private static bool IsConnectedToPrefab(GameObject instanceRoot, GameObject prefabAsset)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(instanceRoot))
                return false;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            return source == prefabAsset;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                return child.gameObject;

            var created = new GameObject(name);
            created.transform.SetParent(parent);
            return created;
        }

        private static GameObject FindOrCreate(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go : new GameObject(name);
        }

        private static Material EnsureSailMaterial(Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SailMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, SailMaterialPath);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
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
    }
}

