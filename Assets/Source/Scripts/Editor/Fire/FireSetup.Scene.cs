using System.Collections.Generic;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.Items;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Fire;
using RumOverboard.Gameplay.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Fire
{
    /// <summary>
    /// Fire feature setup — part 2: FireConfig, particle materials, fire item prefabs (bucket, lantern, water cask)
    /// and Assets/Scenes/FireScene.unity = the ship sandbox (calm) with a pile of flammables, a powder barrel,
    /// a lantern, buckets and a water cask on deck; F9 fire window open, F8 items, F6 attributes.
    /// </summary>
    public static partial class FireSetup
    {
        public const string ScenePath = "Assets/Scenes/FireScene.unity";
        public const string ConfigPath = "Assets/Source/Configs/Fire/FireConfig.asset";
        internal const string MaterialFolder = "Assets/Source/Environment/Materials/Fire";

        private static ItemsSetup.ItemSpec[] Specs => new ItemsSetup.ItemSpec[]
        {
            new() { Id = "Bucket", Profile = "Bucket", Shape = PrimitiveType.Cylinder, Scale = new(0.34f, 0.18f, 0.34f), Color = new(0.42f, 0.32f, 0.2f), Smoothness = 0.25f, Carriable = true },
            new() { Id = "Lantern", Profile = "Lantern", Shape = PrimitiveType.Cylinder, Scale = new(0.16f, 0.13f, 0.16f), Color = new(0.95f, 0.75f, 0.35f), Smoothness = 0.7f, Metallic = 0.6f, Carriable = true, Decorate = AddLanternLight },
            new() { Id = "WaterCask", Profile = "WaterCask", Shape = PrimitiveType.Cylinder, Scale = new(0.8f, 0.5f, 0.8f), Color = new(0.28f, 0.35f, 0.42f), Smoothness = 0.35f, Decorate = go => go.AddComponent<WaterSourceInteractable>() },
        };

        [MenuItem("RumOverboard/Fire/★ Build Fire Scene", priority = 0)]
        public static void BuildFireScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            CreateFireContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            ShipSandboxSetup.EnsureLayers();
            ShipSandboxSetup.PreparePlayerPrefab();
            ItemsSetup.BuildPrefabsAndCatalog(overwrite: false);
            BuildPrefabs(overwrite: false);
            FireConfig config = EnsureConfig();
            (Material flame, Material smoke) = EnsureMaterials();

            ShipSandboxSetup.BuildScene(ScenePath, "Calm", null);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // The sandbox build re-imports assets: re-load what we hold.
            ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemsSetup.CatalogPath);
            config = AssetDatabase.LoadAssetAtPath<FireConfig>(ConfigPath);
            flame = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/FireParticles.mat");
            smoke = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/SmokeParticles.mat");

            ItemsSetup.AddSpawner(catalog, SceneLayout());

            var fire = new GameObject("Fire");
            Set(fire.AddComponent<FireDirector>(), "config", config);
            var visuals = fire.AddComponent<FireVisuals>();
            Set(visuals, "config", config);
            Set(visuals, "flameMaterial", flame);
            Set(visuals, "smokeMaterial", smoke);
            fire.AddComponent<FireDebugWindow>();

            var debug = new GameObject("ItemsDebug");
            var items = debug.AddComponent<ItemsDebugWindow>();
            items.SetCatalog(catalog);
            var iso = new SerializedObject(items);
            iso.FindProperty("visible").boolValue = false;
            iso.FindProperty("catalog").objectReferenceValue = catalog;
            iso.ApplyModifiedPropertiesWithoutUndo();
            debug.AddComponent<AttributesDebugWindow>();
            debug.AddComponent<HeldItemPresenter>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Fire] Scene saved: {ScenePath}. Play → F9 Ignite, E take a bucket, Q splash, E on the water cask to refill.");
        }

        [MenuItem("RumOverboard/Fire/Rebuild Fire Prefabs (overwrite)", priority = 11)]
        public static void RebuildPrefabsMenu()
        {
            CreateFireContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            BuildPrefabs(overwrite: true);
        }

        public static void BuildPrefabs(bool overwrite) => ItemsSetup.BuildPrefabsAndCatalog(Specs, overwrite);

        /// <summary>Ship axes from the first crew spawn: x starboard, z bow.</summary>
        private static List<ItemSpawner.Placement> SceneLayout() => new()
        {
            // fire pile forward: crate + board + barrel close together, a lantern next to them
            new() { ItemId = "Crate", Where = ItemSpawner.Where.Deck, Offset = new(0.6f, 0.5f, 3.6f) },
            new() { ItemId = "Board", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.3f, 4.5f), Yaw = 90f },
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Deck, Offset = new(-0.55f, 0.6f, 3.7f) },
            new() { ItemId = "Lantern", Where = ItemSpawner.Where.Deck, Offset = new(0.05f, 0.35f, 3.25f) },
            // powder away from the pile (so the spread reaches it only if you let it)
            new() { ItemId = "PowderBarrel", Where = ItemSpawner.Where.Deck, Offset = new(0.8f, 0.6f, -5.5f) },
            // firefighting kit next to the crew spawn
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Deck, Offset = new(0.4f, 0.35f, 1.2f) },
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Deck, Offset = new(-0.4f, 0.35f, 1.2f) },
            new() { ItemId = "WaterCask", Where = ItemSpawner.Where.Deck, Offset = new(1.0f, 0.6f, -2.9f), AnchorToShip = true },
            new() { ItemId = "Chest", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.45f, -2.2f), AnchorToShip = true },
            new() { ItemId = "Bucket", Where = ItemSpawner.Where.Water, Offset = new(5.5f, 1.5f, 1f) },
        };

        private static void AddLanternLight(GameObject root)
        {
            var go = new GameObject("LanternLight");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * 0.05f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.38f);
            light.intensity = 1.6f;
            light.range = 4.5f;
            light.shadows = LightShadows.None;
        }

        public static FireConfig EnsureConfig()
        {
            AttributeRegistryBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(ConfigPath)?.Replace('\\', '/'));
            var config = AssetDatabase.LoadAssetAtPath<FireConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<FireConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        internal static (Material flame, Material smoke) EnsureMaterials()
        {
            AttributeRegistryBuilder.EnsureFolder(MaterialFolder);
            string texPath = $"{MaterialFolder}/SoftDot.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                Texture2D tex = FireVisuals.SoftTexture();
                System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                if (AssetImporter.GetAtPath(texPath) is TextureImporter ti)
                {
                    ti.alphaIsTransparency = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            }
            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            return (ParticleMaterial("FireParticles", soft, additive: true), ParticleMaterial("SmokeParticles", soft, additive: false));
        }

        private static Material ParticleMaterial(string name, Texture2D tex, bool additive)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null; // FireVisuals makes runtime materials instead
            mat = new Material(shader) { name = name };
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) mat.EnableKeyword("_BLENDMODE_ADD");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
