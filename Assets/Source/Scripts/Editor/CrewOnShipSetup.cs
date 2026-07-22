#if FUSION2
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools
{
    /// <summary>
    /// Puts the crew on the ship: creates spawn points on the deck (derived from the ship's walkable
    /// colliders / helm) and wires them to the ConnectionManager, so pressing Play drops the player
    /// onto the deck instead of the origin ring. Also drops an EditorOnly stickman so you can SEE a
    /// figure on the deck in edit mode. Best-effort placement — nudge the "CrewSpawns" points if the
    /// deck height is off (I can't see the scene).
    ///
    /// Menu: RumOverboard ▸ Setup ▸ Place Crew On Ship (Gameplay). Also auto-runs once if the
    /// Gameplay scene is the active scene.
    /// </summary>
    public static class CrewOnShipSetup
    {
        private const string Menu = "RumOverboard/Setup/Place Crew On Ship (Gameplay)";
        private const string SpawnsRootName = "CrewSpawns";
        private const string PreviewName = "CrewPreview (EditorOnly)";
        private const string PlayerModelPath = "Assets/PolyOne/Free Stickman/Prefabs/Free Pack - Stick Man.prefab";
        private const string PlaceholderName = "MooredShipPlaceholder";

        [InitializeOnLoadMethod]
        private static void AutoRunOnce()
        {
            string key = "RumOverboard.CrewOnShipSetup.v1:" + Application.dataPath;
            if (EditorPrefs.GetBool(key, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(key, false))
                    return;
                if (Application.isPlaying)
                    return; // don't mutate the scene during play
                // Only touch the scene if the Gameplay scene is the one currently open.
                Scene active = SceneManager.GetActiveScene();
                if (!active.IsValid() || !active.name.Contains("Gameplay"))
                    return;
                if (Run(out string summary))
                {
                    EditorPrefs.SetBool(key, true);
                    Debug.Log($"[CrewOnShipSetup] {summary}");
                }
            };
        }

        [MenuItem(Menu)]
        private static void RunFromMenu()
        {
            Run(out string summary);
            EditorUtility.DisplayDialog("Place Crew On Ship", summary, "OK");
        }

        public static bool Run(out string summary)
        {
            var manager = Object.FindAnyObjectByType<ConnectionManager>();
            if (manager == null)
            {
                summary = "ConnectionManager not found in the open scene. Open Gameplay.unity first.";
                return false;
            }

            GameObject ship = ResolveShip();
            if (ship == null)
            {
                summary = "No ship found in the scene (looked for MooredShipPlaceholder / NetworkShip / ShipBuoyancyController).";
                return false;
            }

            Vector3 deckAnchor = ResolveDeckAnchor(ship, out string how);
            Vector3 fwd = ship.transform.forward;
            Vector3 right = ship.transform.right;

            // A small cluster of spawn points on the deck.
            var root = GameObject.Find(SpawnsRootName);
            if (root == null)
                root = new GameObject(SpawnsRootName);
            root.transform.position = deckAnchor;
            Undo.RegisterCreatedObjectUndo(root, "Create CrewSpawns");

            // Clear old points.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            Vector3[] offsets =
            {
                Vector3.zero,
                -fwd * 1.6f,
                -fwd * 1.6f + right * 1.2f,
                -fwd * 1.6f - right * 1.2f,
            };
            var points = new Transform[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                var p = new GameObject($"Spawn_{i}");
                p.transform.SetParent(root.transform, false);
                p.transform.position = deckAnchor + offsets[i];
                p.transform.rotation = ship.transform.rotation;
                points[i] = p.transform;
            }

            // Wire ConnectionManager.spawnPoints.
            var so = new SerializedObject(manager);
            var arr = so.FindProperty("spawnPoints");
            if (arr != null)
            {
                arr.arraySize = points.Length;
                for (int i = 0; i < points.Length; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Visible edit-mode preview figure on the deck (stripped from builds via EditorOnly tag).
            string previewNote = PlacePreview(deckAnchor, ship.transform.rotation);

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            summary = $"Crew placed on '{ship.name}'. Deck anchor {deckAnchor} ({how}); " +
                      $"{points.Length} spawn points wired to ConnectionManager. {previewNote}\n" +
                      "Press Play to spawn the crew on the deck. Nudge 'CrewSpawns' if the height looks off.";
            return true;
        }

        private static GameObject ResolveShip()
        {
            var placeholder = GameObject.Find(PlaceholderName);
            if (placeholder != null)
                return placeholder;

            var buoyancy = Object.FindAnyObjectByType<RumOverboard.Gameplay.Ocean.ShipBuoyancyController>();
            if (buoyancy != null)
                return buoyancy.gameObject;

            var helm = Object.FindAnyObjectByType<RumOverboard.Gameplay.Ocean.ShipHelm>();
            return helm != null ? helm.gameObject : null;
        }

        // Best-effort deck point: top of the highest "Walkable_" collider, else the helm stand,
        // else a heuristic from renderer bounds. +1.1 up so the capsule drops onto the deck.
        private static Vector3 ResolveDeckAnchor(GameObject ship, out string how)
        {
            Collider best = null;
            foreach (var col in ship.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || !col.name.StartsWith("Walkable_"))
                    continue;
                if (best == null || col.bounds.max.y > best.bounds.max.y)
                    best = col;
            }
            if (best != null)
            {
                how = $"top of {best.name}";
                Vector3 c = best.bounds.center;
                return new Vector3(c.x, best.bounds.max.y + 1.1f, c.z);
            }

            var helm = ship.GetComponentInChildren<RumOverboard.Gameplay.Ocean.ShipHelm>();
            if (helm != null)
            {
                how = "helm stand";
                return helm.StandPosition + Vector3.up * 1.1f;
            }

            var renderers = ship.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);
                how = "renderer-bounds heuristic (verify!)";
                return new Vector3(b.center.x, b.min.y + b.size.y * 0.55f, b.center.z);
            }

            how = "ship origin (no colliders/renderers found — verify!)";
            return ship.transform.position + Vector3.up * 2f;
        }

        private static string PlacePreview(Vector3 pos, Quaternion rot)
        {
            var existing = GameObject.Find(PreviewName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null)
                return "(preview model not found — spawn points still wired.)";

            var preview = (GameObject)PrefabUtility.InstantiatePrefab(model);
            preview.name = PreviewName;
            preview.transform.SetPositionAndRotation(pos, rot);
            preview.tag = "EditorOnly"; // stripped from builds
            Undo.RegisterCreatedObjectUndo(preview, "Create CrewPreview");
            return "Edit-mode preview figure placed (EditorOnly).";
        }
    }
}
#endif
