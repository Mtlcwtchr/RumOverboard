using System.Collections.Generic;
using Fusion;
using Fusion.Addons.Physics;
using RumOverboard.EditorTools.Attributes;
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.Items
{
    /// <summary>
    /// Physical items feature setup — part 2: item prefabs, catalog, feature scene.
    ///   ★ Build Items Scene: content + registry + prefabs (created if missing — your edits survive) +
    ///   catalog + Assets/Scenes/ItemsScene.unity = the ship sandbox (brig, calm sea) with items on the
    ///   deck, a chest fixed to the hull, loot floating next to the ship, F8 items window open.
    ///   Rebuild Item Prefabs (overwrite) regenerates every prefab from code.
    /// </summary>
    public static partial class ItemsSetup
    {
        public const string ScenePath = "Assets/Scenes/ItemsScene.unity";
        public const string PrefabFolder = "Assets/Source/Prefabs/Items";
        public const string CatalogPath = "Assets/Source/Configs/Items/ItemCatalog.asset";
        private const string MaterialFolder = "Assets/Source/Environment/Materials/Items";

        internal struct ItemSpec
        {
            public string Id, Profile;
            public PrimitiveType Shape;
            public Vector3 Scale, Euler;
            public Color Color;
            public float Smoothness, Metallic;
            public bool Carriable, Storage;
            /// <summary>Extra components / children added by other features (fire: water source, lantern light…).</summary>
            public System.Action<GameObject> Decorate;
        }

        private static readonly ItemSpec[] Specs =
        {
            new() { Id = "Barrel", Profile = "Barrel", Shape = PrimitiveType.Cylinder, Scale = new(0.6f, 0.45f, 0.6f), Color = new(0.45f, 0.30f, 0.17f), Smoothness = 0.25f, Carriable = true },
            new() { Id = "PowderBarrel", Profile = "PowderBarrel", Shape = PrimitiveType.Cylinder, Scale = new(0.6f, 0.45f, 0.6f), Color = new(0.22f, 0.2f, 0.2f), Smoothness = 0.2f, Carriable = true },
            new() { Id = "Board", Profile = "Board", Shape = PrimitiveType.Cube, Scale = new(0.25f, 0.06f, 2.0f), Color = new(0.6f, 0.45f, 0.28f), Smoothness = 0.2f, Carriable = true },
            new() { Id = "Crate", Profile = "Crate", Shape = PrimitiveType.Cube, Scale = new(0.6f, 0.6f, 0.6f), Color = new(0.55f, 0.4f, 0.22f), Smoothness = 0.2f, Carriable = true },
            new() { Id = "GrogBottle", Profile = "GrogBottle", Shape = PrimitiveType.Cylinder, Scale = new(0.12f, 0.15f, 0.12f), Color = new(0.15f, 0.45f, 0.2f), Smoothness = 0.9f, Carriable = true },
            new() { Id = "Spyglass", Profile = "Spyglass", Shape = PrimitiveType.Cylinder, Scale = new(0.07f, 0.25f, 0.07f), Euler = new(90f, 0f, 0f), Color = new(0.78f, 0.6f, 0.25f), Smoothness = 0.65f, Metallic = 0.9f, Carriable = true },
            new() { Id = "Compass", Profile = "Compass", Shape = PrimitiveType.Cylinder, Scale = new(0.18f, 0.03f, 0.18f), Color = new(0.7f, 0.55f, 0.25f), Smoothness = 0.6f, Metallic = 0.8f, Carriable = true },
            new() { Id = "Chest", Profile = "Chest", Shape = PrimitiveType.Cube, Scale = new(1.0f, 0.6f, 0.6f), Color = new(0.3f, 0.18f, 0.1f), Smoothness = 0.3f, Storage = true },
        };

        [MenuItem("RumOverboard/Items/★ Build Items Scene", priority = 0)]
        public static void BuildItemsScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            CreateItemContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            ShipSandboxSetup.EnsureLayers();
            ShipSandboxSetup.PreparePlayerPrefab(); // crew attributes on the player prefab
            ItemCatalog catalog = BuildPrefabsAndCatalog(overwrite: false);

            ShipSandboxSetup.BuildScene(ScenePath, "Calm", null);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // Building the sandbox scene re-imports assets: re-load, an instance from before may be dead by now.
            catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
                Debug.LogError($"[Items] {CatalogPath} missing after the scene build.");

            AddSpawner(catalog, SceneLayout());

            var debug = new GameObject("ItemsDebug");
            var window = debug.AddComponent<ItemsDebugWindow>();
            window.SetCatalog(catalog);
            var wso = new SerializedObject(window);
            wso.FindProperty("visible").boolValue = true;
            wso.FindProperty("catalog").objectReferenceValue = catalog;
            wso.ApplyModifiedPropertiesWithoutUndo();
            debug.AddComponent<AttributesDebugWindow>(); // F6: item profiles (mass, integrity, slots, sips…)
            debug.AddComponent<HeldItemPresenter>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Items] Scene saved: {ScenePath}. Play → E pick up, G put down, LMB throw, Q use, E on the chest; F8 items window.");
        }

        /// <summary>An <see cref="ItemSpawner"/> in the open scene with <paramref name="layout"/>.</summary>
        internal static ItemSpawner AddSpawner(ItemCatalog catalog, List<ItemSpawner.Placement> layout)
        {
            var spawnerGo = new GameObject("ItemSpawner");
            var spawner = spawnerGo.AddComponent<ItemSpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            SerializedProperty list = so.FindProperty("placements");
            list.arraySize = layout.Count;
            for (int i = 0; i < layout.Count; i++)
            {
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("ItemId").stringValue = layout[i].ItemId;
                e.FindPropertyRelative("Where").enumValueIndex = (int)layout[i].Where;
                e.FindPropertyRelative("Offset").vector3Value = layout[i].Offset;
                e.FindPropertyRelative("Yaw").floatValue = layout[i].Yaw;
                e.FindPropertyRelative("AnchorToShip").boolValue = layout[i].AnchorToShip;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return spawner;
        }

        [MenuItem("RumOverboard/Items/Rebuild Item Prefabs (overwrite)", priority = 11)]
        public static void RebuildPrefabsMenu()
        {
            CreateItemContent();
            AttributeRegistryBuilder.Rebuild(log: true);
            BuildPrefabsAndCatalog(overwrite: true);
        }

        /// <summary>Deck items around the first crew spawn (ship axes: x starboard, z bow), a chest aft, loot in the water.</summary>
        private static List<ItemSpawner.Placement> SceneLayout() => new()
        {
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Deck, Offset = new(1.3f, 0.6f, 2.6f) },
            new() { ItemId = "PowderBarrel", Where = ItemSpawner.Where.Deck, Offset = new(-1.3f, 0.6f, 2.6f) },
            new() { ItemId = "Board", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.3f, 3.6f), Yaw = 90f },
            new() { ItemId = "Crate", Where = ItemSpawner.Where.Deck, Offset = new(1.3f, 0.5f, 4.2f) },
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Deck, Offset = new(0.6f, 0.35f, 1.8f) },
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Deck, Offset = new(-0.6f, 0.35f, 1.8f) },
            new() { ItemId = "Spyglass", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.35f, 2.0f) },
            new() { ItemId = "Compass", Where = ItemSpawner.Where.Deck, Offset = new(0.35f, 0.35f, 2.2f) },
            new() { ItemId = "Chest", Where = ItemSpawner.Where.Deck, Offset = new(0f, 0.45f, -2.2f), AnchorToShip = true },
            new() { ItemId = "Barrel", Where = ItemSpawner.Where.Water, Offset = new(6f, 1.5f, 0f) },
            new() { ItemId = "Board", Where = ItemSpawner.Where.Water, Offset = new(5.5f, 1.5f, 4f) },
            new() { ItemId = "GrogBottle", Where = ItemSpawner.Where.Water, Offset = new(6f, 1.5f, -3f) },
        };

        public static ItemCatalog BuildPrefabsAndCatalog(bool overwrite) => BuildPrefabsAndCatalog(Specs, overwrite);

        /// <summary>Prefabs for <paramref name="specs"/> (created if missing, or rebuilt) + their catalog entries; other entries are kept.</summary>
        internal static ItemCatalog BuildPrefabsAndCatalog(IEnumerable<ItemSpec> specs, bool overwrite)
        {
            AttributeRegistryBuilder.EnsureFolder(PrefabFolder);
            AttributeRegistryBuilder.EnsureFolder(MaterialFolder);
            AttributeRegistryBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            foreach (ItemSpec spec in specs)
            {
                string path = $"{PrefabFolder}/{spec.Id}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || overwrite)
                    prefab = BuildPrefab(spec, path);
                if (prefab == null) continue;

                int index = catalog.Items.FindIndex(e => e.Id == spec.Id);
                var entry = new ItemCatalog.Entry { Id = spec.Id, Prefab = prefab };
                if (index >= 0) catalog.Items[index] = entry;
                else catalog.Items.Add(entry);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // New network prefabs get Fusion's "FusionPrefab" label on import, but the prefab table in the
            // NetworkProjectConfig is rebuilt separately — without it Runner.Spawn can't resolve them.
            Fusion.Editor.NetworkProjectConfigUtilities.RebuildPrefabTable();

            catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            return catalog;
        }

        private static GameObject BuildPrefab(ItemSpec spec, string path)
        {
            var profile = AttributesSandboxSetup.Load<AttributeProfile>("Profiles", spec.Profile);
            if (profile == null)
            {
                Debug.LogError($"[Items] Profile '{spec.Profile}' missing — run Create Item Content.");
                return null;
            }

            var root = new GameObject(spec.Id);
            try
            {
                var rb = root.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.None; // NetworkRigidbody3D interpolates
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.angularDamping = 0.3f;

                GameObject body = GameObject.CreatePrimitive(spec.Shape);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = spec.Scale;
                body.transform.localEulerAngles = spec.Euler;
                body.GetComponent<Renderer>().sharedMaterial = ItemMaterial(spec);

                var attributes = root.AddComponent<AttributesComponent>();
                var aso = new SerializedObject(attributes);
                aso.FindProperty("profile").objectReferenceValue = profile;
                aso.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<NetworkObject>();
                root.AddComponent<NetworkRigidbody3D>();
                root.AddComponent<NetworkAttributes>();
                root.AddComponent<WorldItem>();
                if (spec.Carriable) root.AddComponent<ItemInteractable>();
                if (spec.Storage)
                {
                    root.AddComponent<ItemStorage>();
                    root.AddComponent<StorageInteractable>();
                }
                spec.Decorate?.Invoke(root);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Items] Prefab {path}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material ItemMaterial(ItemSpec spec)
        {
            string path = $"{MaterialFolder}/Item_{spec.Id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { name = $"Item_{spec.Id}" };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", spec.Color);
            mat.color = spec.Color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", spec.Smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", spec.Metallic);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
