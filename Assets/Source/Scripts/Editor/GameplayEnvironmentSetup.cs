using System.Collections.Generic;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Source.Scripts.Editor
{
    /// <summary>
    /// Builds a simple playable island setup for Gameplay scene: terrain island,
    /// beach spawn area, pier, moored ship placeholder and water trigger volume.
    /// </summary>
    public static class GameplayEnvironmentSetup
    {
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

        private const string EnvironmentRootName = "Environment";
        private const string TerrainDataPath = "Assets/Source/Environment/Terrain/StarterIslandTerrain.asset";
        private const string SandMaterialPath = "Assets/Source/Environment/Materials/Sand.mat";
        private const string WoodMaterialPath = "Assets/Source/Environment/Materials/Wood.mat";
        private const string WaterMaterialPath = "Assets/Source/Environment/Materials/WaterPlaceholder.mat";
        private const string TerrainLitShaderPath = "Universal Render Pipeline/Terrain/Lit";

        [MenuItem("RumOverboard/Setup/Build Gameplay Environment")]
        public static void BuildGameplayEnvironment()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Terrain");
            EnsureFolder("Assets/Source/Environment/Materials");
            EnsureTag("Water");

            var scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);

            Material sand = EnsureTerrainMaterial(SandMaterialPath, new Color(0.87f, 0.78f, 0.57f));
            Material wood = EnsureMaterial(WoodMaterialPath, new Color(0.36f, 0.24f, 0.14f));
            Material water = EnsureMaterial(WaterMaterialPath, new Color(0.10f, 0.38f, 0.58f));

            var environmentRoot = FindOrCreate(EnvironmentRootName);

            RemoveOldDeck();
            BuildTerrain(environmentRoot, sand);
            BuildBeach(environmentRoot, sand);
            BuildPier(environmentRoot, wood);
            BuildWater(environmentRoot, water);
            SetupSpawnPoints();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GameplayEnvironmentSetup] Gameplay scene environment has been built.");
        }

        public static void BuildGameplayEnvironmentFromCommandLine()
        {
            BuildGameplayEnvironment();
        }

        private static void RemoveOldDeck()
        {
            GameObject oldDeck = GameObject.Find("Deck (placeholder)");
            if (oldDeck != null)
                Object.DestroyImmediate(oldDeck);
        }

        private static void BuildTerrain(GameObject root, Material sand)
        {
            TerrainData terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (terrainData == null)
            {
                terrainData = new TerrainData();
                AssetDatabase.CreateAsset(terrainData, TerrainDataPath);
            }

            terrainData.heightmapResolution = 257;
            terrainData.size = new Vector3(220f, 24f, 220f);
            ApplyIslandHeights(terrainData);

            Terrain terrain = Object.FindAnyObjectByType<Terrain>(FindObjectsInactive.Include);
            GameObject terrainGo;
            if (terrain == null)
            {
                terrainGo = Terrain.CreateTerrainGameObject(terrainData);
                terrain = terrainGo.GetComponent<Terrain>();
                terrainGo.name = "IslandTerrain";
                terrainGo.transform.SetParent(root.transform);
            }
            else
            {
                terrainGo = terrain.gameObject;
                terrain.terrainData = terrainData;
                var col = terrainGo.GetComponent<TerrainCollider>();
                if (col != null)
                    col.terrainData = terrainData;
                terrainGo.name = "IslandTerrain";
                terrainGo.transform.SetParent(root.transform);
            }

            terrain.materialTemplate = sand;
            terrain.drawInstanced = true;
            terrainGo.transform.position = new Vector3(-110f, -2f, -110f);
        }

        private static void ApplyIslandHeights(TerrainData terrainData)
        {
            int res = terrainData.heightmapResolution;
            var heights = new float[res, res];

            for (int z = 0; z < res; z++)
            {
                float v = (float)z / (res - 1);
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / (res - 1);
                    float dx = u - 0.48f;
                    float dz = v - 0.52f;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);

                    float island = Mathf.Clamp01(1f - dist / 0.45f);
                    float mountain = Mathf.Pow(island, 1.9f) * 0.38f;

                    // Carve a flatter beach patch on the east side for start + pier.
                    float beachMask = Mathf.Clamp01((u - 0.60f) / 0.18f) *
                                      Mathf.Clamp01(1f - Mathf.Abs(v - 0.52f) / 0.17f);
                    float beachHeight = Mathf.Lerp(mountain, 0.03f, beachMask);

                    heights[z, x] = Mathf.Clamp01(0.01f + beachHeight);
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

        private static void BuildBeach(GameObject root, Material sand)
        {
            var beach = EnsurePrimitive("StartBeach", PrimitiveType.Cube, root.transform);
            beach.transform.position = new Vector3(37f, 0.35f, 0f);
            beach.transform.localScale = new Vector3(26f, 0.7f, 18f);

            var renderer = beach.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = sand;

            var col = beach.GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = false;
        }

        private static void BuildPier(GameObject root, Material wood)
        {
            var pierRoot = FindOrCreate("Pier");
            pierRoot.transform.SetParent(root.transform);
            pierRoot.transform.position = new Vector3(48f, 0.9f, 3f);
            pierRoot.transform.rotation = Quaternion.Euler(0f, -8f, 0f);

            var deck = EnsurePrimitiveChild(pierRoot.transform, "Deck", PrimitiveType.Cube);
            deck.transform.localPosition = Vector3.zero;
            deck.transform.localScale = new Vector3(5f, 0.4f, 22f);
            deck.GetComponent<MeshRenderer>().sharedMaterial = wood;

            for (int i = 0; i < 6; i++)
            {
                float z = -9f + i * 3.6f;

                var postL = EnsurePrimitiveChild(pierRoot.transform, $"Post_L_{i + 1}", PrimitiveType.Cube);
                postL.transform.localPosition = new Vector3(-2f, -1.45f, z);
                postL.transform.localScale = new Vector3(0.35f, 2.8f, 0.35f);
                postL.GetComponent<MeshRenderer>().sharedMaterial = wood;

                var postR = EnsurePrimitiveChild(pierRoot.transform, $"Post_R_{i + 1}", PrimitiveType.Cube);
                postR.transform.localPosition = new Vector3(2f, -1.45f, z);
                postR.transform.localScale = new Vector3(0.35f, 2.8f, 0.35f);
                postR.GetComponent<MeshRenderer>().sharedMaterial = wood;
            }
        }

        private static void BuildWater(GameObject root, Material waterMaterial)
        {
            var water = EnsurePrimitive("Water (placeholder)", PrimitiveType.Cube, root.transform);
            water.transform.position = new Vector3(0f, -2.5f, 0f);
            water.transform.localScale = new Vector3(520f, 5f, 520f);

            var renderer = water.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = waterMaterial;

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0)
                water.layer = waterLayer;

            water.tag = "Water";

            var col = water.GetComponent<BoxCollider>();
            if (col != null)
                col.isTrigger = true;
        }

        private static void SetupSpawnPoints()
        {
            var spawnRoot = FindOrCreate("SpawnPoints");

            var points = new[]
            {
                new Vector3(32f, 1.2f, -4f),
                new Vector3(34f, 1.2f, -1f),
                new Vector3(36f, 1.2f, 2f),
                new Vector3(38f, 1.2f, 5f),
            };

            var list = new List<Transform>(4);
            for (int i = 0; i < 4; i++)
            {
                var go = FindOrCreate($"Spawn {i + 1}");
                go.transform.SetParent(spawnRoot.transform);
                go.transform.position = points[i];
                go.transform.rotation = Quaternion.LookRotation((Vector3.zero - points[i]).normalized, Vector3.up);
                list.Add(go.transform);
            }

            var manager = Object.FindAnyObjectByType<ConnectionManager>(FindObjectsInactive.Include);
            if (manager == null)
                return;

            var so = new SerializedObject(manager);
            var prop = so.FindProperty("spawnPoints");
            prop.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
        }

        private static GameObject EnsurePrimitive(string name, PrimitiveType type, Transform parent)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(type);
                go.name = name;
            }

            if (parent != null)
                go.transform.SetParent(parent);

            return go;
        }

        private static GameObject EnsurePrimitiveChild(Transform parent, string name, PrimitiveType type)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(parent);
                return go;
            }

            return child.gameObject;
        }

        private static GameObject FindOrCreate(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go : new GameObject(name);
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

        private static Material EnsureTerrainMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = Shader.Find(TerrainLitShaderPath);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                // Terrain expects a terrain-compatible shader instead of generic Lit.
                material.shader = shader;
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureTag(string tag)
        {
            var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAsset == null || tagManagerAsset.Length == 0)
                return;

            var tagManager = new SerializedObject(tagManagerAsset[0]);
            var tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tag)
                    return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
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

