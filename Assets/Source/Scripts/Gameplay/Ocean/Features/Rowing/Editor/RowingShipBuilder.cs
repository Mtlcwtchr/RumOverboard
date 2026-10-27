#if UNITY_EDITOR && FUSION2
using RumOverboard.EditorTools.ShipSandbox;
using RumOverboard.Networking;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rowing.Editor
{
    public static class RowingShipBuilder
    {
        public const string ConfigPath = "Assets/Source/Configs/FeatureScenes/RowingConfig.asset";
        public static RowingConfig EnsureConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<RowingConfig>(ConfigPath);
            if (cfg != null) return cfg;
            cfg = ScriptableObject.CreateInstance<RowingConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            return cfg;
        }

        public static void Build(GameObject root)
        {
            var previous = root.transform.Find("RowingModule");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            Transform module = Child(root.transform, "RowingModule", Vector3.zero);
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Source/Environment/Materials/Wood.mat");
            // Alternate with the three cannon positions; clear of pin rails at the mast bases.
            float[] stations = { -4.05f, 2.55f, 8.55f };
            int index = 0;
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in stations)
                {
                    float edge = BrigShipBuilder.EdgeX(z);
                    float deck = BrigShipBuilder.DeckY(z);
                    Transform stationRoot = Child(module, $"Sweep_{index}_{(side < 0 ? "Port" : "Starboard")}", Vector3.zero);
                    Transform pivot = Child(stationRoot, "Thole", new Vector3(side * edge, deck + 0.72f, z));
                    pivot.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                    // A 5.9m sweep: 1.2m inboard handle, shaft outboard, broad blade at the tip.
                    Part(pivot, "Shaft", new Vector3(0f, 0f, 1.75f), new Vector3(0.095f, 0.095f, 5.9f), wood);
                    Part(pivot, "Blade", new Vector3(0f, 0f, 4.35f), new Vector3(0.4f, 0.075f, 1.1f), wood);
                    Part(pivot, "Grip", new Vector3(0f, 0f, -1.08f), new Vector3(0.15f, 0.15f, 0.24f), wood);
                    // Fixed trigger at the handle; animated visuals never change the compound hull collider.
                    Transform zone = Child(stationRoot, "HandleZone", new Vector3(side * (edge - 0.85f), deck + 0.72f, z));
                    var trigger = zone.gameObject.AddComponent<BoxCollider>();
                    trigger.isTrigger = true;
                    trigger.size = new Vector3(1.1f, 0.65f, 0.7f);
                    Transform stand = Child(stationRoot, "RowerStand", new Vector3(side * (edge - 0.85f), deck + 0.03f, z + 0.72f));
                    stand.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    var station = stationRoot.gameObject.AddComponent<OarStation>();
                    station.Configure(index++, side, pivot, stand, 30f);
                    station.Prompt = "Взяться за весло";
                    station.MaxDistance = 2.6f;
                    station.SetColliders(new Collider[] { trigger });
                    CutPort(root, side, z, deck + 0.72f);
                }
            var ship = root.GetComponent<NetworkShip>();
            if (ship != null)
            {
                var so = new SerializedObject(ship);
                so.FindProperty("rowingConfig").objectReferenceValue = EnsureConfig();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform Child(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        private static void CutPort(GameObject root, float side, float z, float portY)
        {
            Transform walls = root.transform.Find("HullModule/Colliders");
            if (walls == null) return;
            foreach (Transform wall in walls)
            {
                if (!wall.name.StartsWith(side < 0f ? "Bulwark_P_" : "Bulwark_S_") ||
                    Mathf.Abs(wall.localPosition.z - z) > 0.12f) continue;
                if (walls.Find(wall.name + "_PortTop") != null) return;
                float bottom = wall.localPosition.y - wall.localScale.y * 0.5f;
                float top = wall.localPosition.y + wall.localScale.y * 0.5f;
                float holeBottom = portY - 0.14f;
                float holeTop = portY + 0.14f;
                var upper = Object.Instantiate(wall.gameObject, walls).transform;
                upper.name = wall.name + "_PortTop";
                upper.localPosition = new Vector3(wall.localPosition.x, (holeTop + top) * 0.5f, wall.localPosition.z);
                upper.localScale = new Vector3(wall.localScale.x, Mathf.Max(0.03f, top - holeTop), wall.localScale.z);
                wall.localPosition = new Vector3(wall.localPosition.x, (bottom + holeBottom) * 0.5f, wall.localPosition.z);
                wall.localScale = new Vector3(wall.localScale.x, holeBottom - bottom, wall.localScale.z);
                break;
            }
        }

        private static void Part(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
#endif
