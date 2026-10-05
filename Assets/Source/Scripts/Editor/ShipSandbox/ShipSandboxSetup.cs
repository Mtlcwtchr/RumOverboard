using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using RumOverboard.Gameplay.Ocean.Features.Rigging;
using RumOverboard.Gameplay.UI;
using RumOverboard.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// One-click setup for the playable "ship sandbox": a moored, floating NetworkShip with crew,
    /// host-authoritative physics, climbable masts, the helm and rope stations, plus hints.
    ///
    ///   RumOverboard ▸ Ship Sandbox ▸ ★ Build Everything
    ///     1. Layers: Player / Ragdoll (+ collision matrix: a body never hits its own ragdoll).
    ///     2. NetworkShip prefab: single dynamic body (nested hull Rigidbody + its buoyancy removed),
    ///        obsolete ShipSailSystem removed, NetworkRigidbody3D interpolates the whole root,
    ///        wheel module installed + HelmStation, climb rails per mast, rope stations per sail,
    ///        crew spawn points — all derived from the actual collider geometry.
    ///     3. NetworkPlayer prefab: layers, no Unity interpolation.
    ///     4. Assets/Scenes/ShipSandbox.unity: ocean, light, camera rig, HUD + hints, GameManager
    ///        (auto-starts Host/Client, falls back to offline Single), added to Build Settings.
    ///
    /// Idempotent: re-running replaces everything it generated (objects named "SB_*").
    /// </summary>
    public static class ShipSandboxSetup
    {
        public const string ScenePath = "Assets/Scenes/ShipSandbox.unity";
        private const string ShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";
        private const string PlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";
        private const string WheelModulePath = "Assets/Source/Environment/Prefabs/ShipModules/Wheel/ShipWheelModule.prefab";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";
        private const string HintsAssetPath = "Assets/Source/Configs/ControlHints.asset";
        private const string Generated = "SB_";

        private const float CapsuleRadius = 0.28f;
        private const float GripAboveFeet = 1.15f;
        private const float MaxDeckY = 4.5f; // ship-local: main/quarter/forecastle decks are below this

        // =========================================================================================
        // Menu
        // =========================================================================================
        [MenuItem("RumOverboard/Ship Sandbox/★ Build Everything (Prefabs + Scene)", priority = 0)]
        public static void BuildEverything()
        {
            if (!ConfirmScenes()) return;
            EnsureLayers();
            PrepareShipPrefab();
            PreparePlayerPrefab();
            OceanLookSetup.ApplyMaterial();
            BuildScene();
            BuildScene(RiggingScenePath, "Calm", riggingDebugOpen: true);
            Debug.Log("[ShipSandbox] Done. Open Assets/Scenes/ShipSandbox.unity and press Play.");
        }

        [MenuItem("RumOverboard/Ship Sandbox/Prepare Ship + Player Prefabs", priority = 20)]
        public static void PreparePrefabs()
        {
            if (!ConfirmScenes()) return;
            EnsureLayers();
            PrepareShipPrefab();
            PreparePlayerPrefab();
        }

        [MenuItem("RumOverboard/Ship Sandbox/Build Sandbox Scene", priority = 21)]
        public static void BuildSceneMenu()
        {
            if (!ConfirmScenes()) return;
            EnsureLayers();
            BuildScene();
        }

        [MenuItem("RumOverboard/Ship Sandbox/Open Sandbox Scene", priority = 40)]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        private static bool ConfirmScenes() =>
            Application.isBatchMode || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        /// <summary>Batch entry: -executeMethod RumOverboard.EditorTools.ShipSandbox.ShipSandboxSetup.BuildEverythingBatch</summary>
        public static void BuildEverythingBatch()
        {
            try
            {
                BuildEverything();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // =========================================================================================
        // Layers
        // =========================================================================================
        public static void EnsureLayers()
        {
            int player = EnsureLayer("Player", 9);
            int ragdoll = EnsureLayer("Ragdoll", 10);
            if (player >= 0 && ragdoll >= 0)
                Physics.IgnoreLayerCollision(player, ragdoll, true);
            AssetDatabase.SaveAssets();
        }

        private static int EnsureLayer(string name, int preferred)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0)
                return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            int slot = -1;
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferred).stringValue))
                slot = preferred;
            else
                for (int i = 8; i < 32; i++)
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { slot = i; break; }

            if (slot < 0)
            {
                Debug.LogError($"[ShipSandbox] No free layer for '{name}'.");
                return -1;
            }
            layers.GetArrayElementAtIndex(slot).stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[ShipSandbox] Layer '{name}' = {slot}");
            return slot;
        }

        // =========================================================================================
        // Ship prefab
        // =========================================================================================
        private sealed class RailPlan
        {
            public string ModulePath;
            public Vector3 Axis;          // ship-local point on the mast axis (y ignored)
            public Vector3 Outward;       // ship-local, horizontal
            public float TrunkRadius;
            public float BottomGripY;
            public float TopGripY;
            public Vector3? Exit;         // ship-local feet position on the platform
            public int Level;
        }

        private sealed class PinRailPlan
        {
            public string ModulePath;
            public string Label;
            public Vector3 Center;   // ship-local, rail top centre
            public Vector3 Along;    // ship-local direction of the rail
            public Vector3 Outward;  // ship-local: from the mast toward the crew side
            public int Pins;
            public float Deck;
        }

        private sealed class LinePlan
        {
            public int Line;
            public RigLineKind Kind;
            public int Sail;
            public string Name;
            public string ModulePath;
            public int Rail;
            public int Pin;
            public Vector3 Block;
            public Vector3 Aloft;
            public float Initial;
        }

        private sealed class ShipPlan
        {
            public readonly List<RailPlan> Rails = new();
            public readonly List<PinRailPlan> PinRails = new();
            public readonly List<LinePlan> Lines = new();
            public readonly List<Vector3> Spawns = new();
            public readonly List<StairPlan> Stairs = new();
            public Vector3? HelmStand;
        }

        private sealed class StairPlan
        {
            public Vector3 Top;   // ship-local: centre of the top edge (on the upper deck lip)
            public Vector3 Foot;  // ship-local: centre of the bottom edge (on the lower deck)
            public float Width;
        }

        /// <summary>Replace the ship's content with the brig built from parts (no stylized asset).</summary>
        public static void RebuildShipFromParts()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            try
            {
                BrigShipBuilder.BuildInto(root);
                PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void PrepareShipPrefab()
        {
            RebuildShipFromParts();

            // Pass 1 — structure: one body, no legacy sails, helm in place.
            GameObject root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            try
            {
                InstallHelm(root);
                RemoveNestedBodies(root);
                RemoveLegacySailSystem(root);
                OceanLookSetup.ApplySeakeeping(root.GetComponent<ShipBuoyancyController>());
                ConfigureShipNetworking(root);
                ClearGenerated(root);
                PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // Pass 2 — derive gameplay data from the real colliders, then write it.
            ShipPlan plan = AnalyzeShip();
            root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            try
            {
                ApplyHelmStand(root, plan);
                ApplyRails(root, plan);
                ApplyStairs(root, plan);
                ApplyRigging(root, plan);
                ApplySpawns(root, plan);
                PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
                Debug.Log($"[ShipSandbox] NetworkShip prepared: climbRails={plan.Rails.Count} stairs={plan.Stairs.Count} " +
                          $"lines={plan.Lines.Count} pinRails={plan.PinRails.Count} spawns={plan.Spawns.Count} helmStand={(plan.HelmStand.HasValue ? plan.HelmStand.Value.ToString() : "-")}");
                foreach (StairPlan st in plan.Stairs)
                    Debug.Log($"[ShipSandbox]   stairs {st.Foot} → {st.Top}");
                foreach (RailPlan r in plan.Rails)
                    Debug.Log($"[ShipSandbox]   rail {r.ModulePath}#{r.Level}: grip {r.BottomGripY:F2}→{r.TopGripY:F2} out={r.Outward} r={r.TrunkRadius:F2} exit={(r.Exit.HasValue ? r.Exit.Value.ToString() : "-")}");
                foreach (PinRailPlan r in plan.PinRails)
                    Debug.Log($"[ShipSandbox]   pin rail '{r.Label}' at {r.Center} pins={r.Pins}");
                foreach (LinePlan l in plan.Lines)
                    Debug.Log($"[ShipSandbox]   line {l.Line} {l.Kind} sail#{l.Sail} '{l.Name}' block={l.Block} home=rail{l.Rail}/pin{l.Pin}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- Analysis: instantiate in a temp scene and probe real colliders -------------------
        private static ShipPlan AnalyzeShip()
        {
            Scene previous = SceneManager.GetActiveScene();
            string previousPath = previous.path;
            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var plan = new ShipPlan();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
            ship.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(Generated))
                    t.gameObject.SetActive(false); // ignore our own previous output
            Physics.SyncTransforms();

            int lineIndex = 0;
            int flatSail = 0;
            foreach (ShipMast mast in ship.GetComponentsInChildren<ShipMast>(true))
            {
                var module = mast.GetComponent<ShipMastModule>() ?? mast.GetComponentInParent<ShipMastModule>();
                Transform moduleT = module != null ? module.transform : mast.transform;
                string modulePath = PathOf(moduleT, ship.transform);

                Transform mastBase = module != null ? module.FindAnchor("MastBase") : null;
                Transform mastTop = module != null ? module.FindAnchor("MastTop") : null;

                if (mastBase != null && mastTop != null)
                    PlanRails(plan, moduleT, modulePath, mastBase.position, mastTop.position);

                // Running rigging: halyards to a pin rail on the port side, sheets to starboard.
                var so = new SerializedObject(mast);
                SerializedProperty sails = so.FindProperty("sails");
                int count = sails != null ? sails.arraySize : 0;
                if (mastBase != null && mastTop != null && count > 0)
                    PlanMastRigging(plan, module, modulePath, mastBase.position, mastTop.position, sails, ref lineIndex, ref flatSail);
                else
                    flatSail += count;
            }

            PlanSpawns(plan, ship);
            PlanDeckStairs(plan, ship);

            ShipHelm helm = ship.GetComponentInChildren<ShipHelm>(true);
            if (helm != null)
            {
                Vector3 anchor = helm.StandPosition; // already mirrored to the aft side of the wheel
                float deck = DeckHeightAt(anchor, anchor.y - 3f);
                if (!float.IsNaN(deck))
                    plan.HelmStand = new Vector3(anchor.x, deck + 0.02f, anchor.z);
            }

            Object.DestroyImmediate(ship);
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            return plan;
        }

        private static void PlanRails(ShipPlan plan, Transform module, string modulePath, Vector3 mastBase, Vector3 mastTop)
        {
            Vector3 axis = new Vector3(mastBase.x, 0f, mastBase.z);
            float deckGuess = DeckHeightAt(axis + Vector3.back * 1f, mastBase.y);
            float radius = TrunkRadius(module, axis, deckGuess);

            Vector3[] candidates = { Vector3.back, Vector3.left, Vector3.right, Vector3.forward };
            Vector3 bestOut = Vector3.back;
            float bestDeck = deckGuess;
            List<float> bestPlatforms = null;
            int bestScore = -1;

            foreach (Vector3 dir in candidates)
            {
                Vector3 stand = axis + dir * (radius + 0.45f);
                float deck = DeckHeightAt(stand, mastBase.y);
                if (float.IsNaN(deck))
                    continue;
                List<float> platforms = PlatformsAbove(module, stand, deck + 3.5f, mastTop.y);
                int score = (IsStandFree(new Vector3(stand.x, deck, stand.z)) ? 2 : 0);
                foreach (float p in platforms)
                    if (IsStandFree(new Vector3(stand.x, p, stand.z)))
                        score += 1;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOut = dir;
                    bestDeck = deck;
                    bestPlatforms = platforms;
                }
            }

            if (float.IsNaN(bestDeck))
                return;
            bestPlatforms ??= new List<float>();

            Vector3 standXZ = axis + bestOut * (radius + 0.45f);
            float bottomFeet = bestDeck;
            int level = 0;
            foreach (float platform in bestPlatforms.Take(3))
            {
                plan.Rails.Add(new RailPlan
                {
                    ModulePath = modulePath, Axis = axis, Outward = bestOut, TrunkRadius = radius,
                    BottomGripY = bottomFeet + GripAboveFeet,
                    TopGripY = platform + GripAboveFeet + 0.1f,
                    Exit = new Vector3(standXZ.x, platform + 0.02f, standXZ.z),
                    Level = level++,
                });
                bottomFeet = platform;
            }

            float top = mastTop.y - 1.5f;
            if (top - (bottomFeet + GripAboveFeet) > 2f)
            {
                plan.Rails.Add(new RailPlan
                {
                    ModulePath = modulePath, Axis = axis, Outward = bestOut, TrunkRadius = radius,
                    BottomGripY = bottomFeet + GripAboveFeet,
                    TopGripY = top,
                    Exit = null,
                    Level = level,
                });
            }
        }

        private const float PinSpacing = 0.32f;
        private const float RailHeight = 0.85f;

        private static void PlanMastRigging(ShipPlan plan, ShipMastModule module, string modulePath, Vector3 mastBase,
            Vector3 mastTop, SerializedProperty sails, ref int lineIndex, ref int flatSail)
        {
            Vector3 axis = new Vector3(mastBase.x, 0f, mastBase.z);
            float mastDeck = DeckHeightAt(axis + Vector3.back * 1f, mastBase.y);
            if (float.IsNaN(mastDeck)) { flatSail += sails.arraySize; return; }
            float radius = TrunkRadius(module.transform, axis, mastDeck);
            int count = sails.arraySize;
            string mastName = PrettyMastName(module.name);

            int portRail = PlanPinRail(plan, modulePath, axis, radius, mastDeck, -1f, count + 2, $"{mastName}: фалы");
            int starRail = PlanPinRail(plan, modulePath, axis, radius, mastDeck, +1f, count + 2, $"{mastName}: шкоты");

            for (int s = 0; s < count; s++, flatSail++)
            {
                SerializedProperty sail = sails.GetArrayElementAtIndex(s);
                string visual = sail.FindPropertyRelative("visualSailName").stringValue;
                string sailName = sail.FindPropertyRelative("name").stringValue;
                string label = PrettySailName(string.IsNullOrEmpty(visual) ? sailName : visual);
                Transform topLeft = module.FindAnchor($"{visual}_TopLeft") ?? module.FindAnchor("MastTop");
                Transform bottomRight = module.FindAnchor($"{visual}_BottomRight") ?? module.FindAnchor("MastTop");

                if (portRail >= 0 && lineIndex < NetworkShip.MaxLines)
                    plan.Lines.Add(new LinePlan
                    {
                        Line = lineIndex++, Kind = RigLineKind.Halyard, Sail = flatSail, Name = $"фал ({label})",
                        ModulePath = modulePath, Rail = portRail, Pin = s,
                        Block = axis + Vector3.left * (radius + 0.07f) + Vector3.up * (mastDeck + 3.0f + s * 0.35f),
                        Aloft = topLeft != null ? topLeft.position : mastTop, Initial = 0f,
                    });
                if (starRail >= 0 && lineIndex < NetworkShip.MaxLines)
                    plan.Lines.Add(new LinePlan
                    {
                        Line = lineIndex++, Kind = RigLineKind.Sheet, Sail = flatSail, Name = $"шкот ({label})",
                        ModulePath = modulePath, Rail = starRail, Pin = s,
                        Block = axis + Vector3.right * (radius + 0.07f) + Vector3.up * (mastDeck + 2.4f + s * 0.35f),
                        Aloft = bottomRight != null ? bottomRight.position : mastTop, Initial = 0.5f,
                    });
            }
        }

        // A pin rail beside the mast: clear of geometry, on the mast's deck, room to stand outboard of it.
        private static int PlanPinRail(ShipPlan plan, string modulePath, Vector3 axis, float radius, float mastDeck,
            float side, int pins, string label)
        {
            Vector3 sideDir = Vector3.right * side;
            float length = pins * PinSpacing + 0.2f;
            foreach (float zShift in new[] { 0f, 0.6f, -0.6f, 1.2f, -1.2f, 1.8f, -1.8f })
            foreach (float dist in new[] { radius + 0.8f, radius + 1.1f, radius + 1.4f, radius + 1.8f, radius + 2.1f })
            {
                Vector3 c = axis + sideDir * dist + Vector3.forward * zShift;
                float deck = DeckHeightAt(c, mastDeck - 3f);
                if (float.IsNaN(deck) || Mathf.Abs(deck - mastDeck) > 0.45f)
                    continue;
                // Starts 0.3 m above the deck so a cambered / sheered deck never counts as an obstacle.
                Vector3 boxCentre = new Vector3(c.x, deck + 0.7f, c.z);
                if (Physics.CheckBox(boxCentre, new Vector3(0.12f, 0.4f, length * 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                Vector3 foot = new Vector3(c.x, deck, c.z) + Vector3.up * 0.02f;
                if (!IsStandFree(foot + sideDir * 0.6f) && !IsStandFree(foot - sideDir * 0.6f))
                    continue; // nowhere to stand next to it

                plan.PinRails.Add(new PinRailPlan
                {
                    ModulePath = modulePath, Label = label, Center = new Vector3(c.x, deck + RailHeight, c.z),
                    Along = Vector3.forward, Outward = sideDir, Pins = pins, Deck = deck,
                });
                return plan.PinRails.Count - 1;
            }
            Debug.LogWarning($"[ShipSandbox] No clear deck spot for pin rail '{label}'.");
            return -1;
        }

        // Decks joined by a sheer wall (e.g. waist → quarterdeck) get a walkable staircase on each
        // side: a ramp collider (smooth for the capsule) under visual treads. No clicks — just walk.
        private const float StairSlopeDeg = 37f;
        private const float StairWidth = 1.1f;

        private static void PlanDeckStairs(ShipPlan plan, GameObject ship)
        {
            var bases = ship.GetComponentsInChildren<ShipMastModule>(true).Select(m => m.FindAnchor("MastBase")).Where(t => t != null).ToList();
            float cx = bases.Count > 0 ? bases.Average(t => t.position.x) : 0f;
            Bounds b = new Bounds(ship.transform.position, Vector3.zero);
            foreach (Collider c in ship.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) b.Encapsulate(c.bounds);

            foreach (float sideOffset in new[] { -1.8f, 1.8f })
            {
                float prev = float.NaN;
                for (float z = b.min.z + 1f; z < b.max.z - 1f; z += 0.25f)
                {
                    float y = DeckHeightAt(new Vector3(cx + sideOffset, 0f, z), -20f);
                    if (!float.IsNaN(prev) && !float.IsNaN(y) && Mathf.Abs(y - prev) > 0.5f && Mathf.Abs(y - prev) < 3.0f)
                        TryPlanStair(plan, cx, sideOffset, z, prev, y);
                    prev = y;
                }
            }
        }

        private static void TryPlanStair(ShipPlan plan, float cx, float sideOffset, float z, float prev, float y)
        {
            bool upForward = y > prev;
            float low = Mathf.Min(prev, y), high = Mathf.Max(prev, y);
            Vector3 outward = upForward ? Vector3.back : Vector3.forward; // from the wall toward the lower deck
            float lowZ = upForward ? z - 0.25f : z;
            float rise = high - low;
            float run = rise / Mathf.Tan(StairSlopeDeg * Mathf.Deg2Rad);

            // Slide the stair sideways until its whole footprint is clear.
            foreach (float shift in new[] { 0f, -0.6f, 0.6f, -1.2f, 1.2f })
            {
                float x = cx + sideOffset + shift;
                Vector3 from = new Vector3(x, low + 0.4f, lowZ + outward.z);
                if (!Physics.Raycast(from, -outward, out RaycastHit wall, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(wall.normal.z) < 0.7f)
                    continue; // not facing along the deck

                Vector3 top = new Vector3(x, high, wall.point.z);
                Vector3 foot = top + outward * run;
                foot.y = low;
                bool clear = true;
                for (float t = 0.15f; t <= 1.01f && clear; t += 0.2f)
                {
                    Vector3 p = Vector3.Lerp(foot, top, t);
                    foreach (float lat in new[] { -StairWidth * 0.35f, StairWidth * 0.35f })
                        clear &= !Physics.CheckSphere(p + Vector3.right * lat + Vector3.up * 0.55f, 0.28f, ~0, QueryTriggerInteraction.Ignore);
                }
                if (!clear || !IsStandFree(foot + outward * 0.5f + Vector3.up * 0.02f) || !IsStandFree(top - outward * 0.6f + Vector3.up * 0.02f))
                    continue;

                plan.Stairs.Add(new StairPlan { Top = top, Foot = foot, Width = StairWidth });
                return;
            }
            Debug.Log($"[ShipSandbox] (skip) no clear spot for a staircase near x={cx + sideOffset:F1} z={z:F1}.");
        }

        private static void PlanSpawns(ShipPlan plan, GameObject ship)
        {
            // Main deck, either side of the centre line, away from masts/hatches.
            float cx = 1.04f;
            var mast = ship.GetComponentsInChildren<ShipMastModule>(true).Select(m => m.FindAnchor("MastBase")).Where(t => t != null).ToList();
            if (mast.Count > 0) cx = mast.Average(t => t.position.x);

            var candidates = new List<Vector3>();
            foreach (float z in new[] { 0f, -1.5f, 1.5f, 7f, -3f, 8.5f, 6f })
            foreach (float x in new[] { -1.6f, 1.6f, -2.6f, 2.6f })
                candidates.Add(new Vector3(cx + x, 0f, z));

            foreach (Vector3 c in candidates)
            {
                if (plan.Spawns.Count >= 4) break;
                float deck = DeckHeightAt(c, -20f);
                if (float.IsNaN(deck)) continue;
                Vector3 p = new Vector3(c.x, deck + 0.05f, c.z);
                if (!IsStandFree(p)) continue;
                if (plan.Spawns.Any(s => (s - p).sqrMagnitude < 1.2f * 1.2f)) continue;
                plan.Spawns.Add(p);
            }
        }

        // ---- Geometry helpers (temp scene = ship-local space) ----------------------------------
        private static float DeckHeightAt(Vector3 xz, float minY)
        {
            Vector3 origin = new Vector3(xz.x, MaxDeckY + 0.5f, xz.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, MaxDeckY + 0.5f - minY + 1f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (RaycastHit h in hits)
                if (h.normal.y > 0.7f && (float.IsNaN(best) || h.point.y > best))
                    best = h.point.y;
            return best;
        }

        private static List<float> PlatformsAbove(Transform module, Vector3 xz, float fromY, float toY)
        {
            var result = new List<float>();
            Vector3 origin = new Vector3(xz.x, toY + 0.5f, xz.z);
            foreach (RaycastHit h in Physics.RaycastAll(origin, Vector3.down, toY + 0.5f - fromY, ~0, QueryTriggerInteraction.Ignore))
                if (h.normal.y > 0.8f && h.collider.transform.IsChildOf(module) && IsFlatFloor(h.point))
                    result.Add(h.point.y);
            result.Sort();
            // Merge near-duplicates (stacked boxes of one platform).
            var merged = new List<float>();
            foreach (float y in result)
                if (merged.Count == 0 || y - merged[^1] > 1.5f)
                    merged.Add(y);
                else
                    merged[^1] = Mathf.Max(merged[^1], y);
            return merged;
        }

        // A real platform: solid, level ground under the whole footprint (not the top of a round yard).
        private static bool IsFlatFloor(Vector3 point)
        {
            const float r = 0.32f;
            foreach (Vector3 d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                Vector3 o = point + d * r + Vector3.up * 0.3f;
                if (!Physics.Raycast(o, Vector3.down, out RaycastHit h, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                    return false;
                if (h.normal.y < 0.8f || Mathf.Abs(h.point.y - point.y) > 0.12f)
                    return false;
            }
            return IsStandFree(point + Vector3.up * 0.02f);
        }

        private static float TrunkRadius(Transform module, Vector3 axis, float deck)
        {
            float best = 0.2f;
            foreach (float h in new[] { 1.5f, 3f, 5f })
            foreach (Vector3 dir in new[] { Vector3.back, Vector3.left, Vector3.right })
            {
                Vector3 from = axis + dir * 3f + Vector3.up * (deck + h);
                foreach (RaycastHit hit in Physics.RaycastAll(from, -dir, 3f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.IsChildOf(module))
                        best = Mathf.Max(best, 3f - hit.distance);
            }
            return Mathf.Clamp(best, 0.15f, 0.8f);
        }

        private static bool IsStandFree(Vector3 feet)
        {
            Vector3 a = feet + Vector3.up * (CapsuleRadius + 0.25f);
            Vector3 b = feet + Vector3.up * (1.8f - CapsuleRadius);
            return !Physics.CheckCapsule(a, b, CapsuleRadius, ~0, QueryTriggerInteraction.Ignore);
        }

        // ---- Apply to prefab contents ----------------------------------------------------------
        private static void RemoveNestedBodies(GameObject root)
        {
            Rigidbody rootBody = root.GetComponent<Rigidbody>();
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == rootBody)
                    continue;
                GameObject go = body.gameObject;
                string path = PathOf(go.transform, root.transform);
                if (!TryStripBody(go))
                {
                    GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                    if (outer != null)
                    {
                        PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                        TryStripBody(go);
                    }
                }
                Debug.Log($"[ShipSandbox] Removed nested Rigidbody + buoyancy at '{path}' (ship is one body now).");
            }
        }

        private static bool TryStripBody(GameObject go)
        {
            try
            {
                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    bool needsBody = mb.GetType().GetCustomAttributes(typeof(RequireComponent), true)
                        .Cast<RequireComponent>()
                        .Any(rc => rc.m_Type0 == typeof(Rigidbody) || rc.m_Type1 == typeof(Rigidbody) || rc.m_Type2 == typeof(Rigidbody));
                    if (needsBody || mb is ShipDeckMotionProvider)
                        Object.DestroyImmediate(mb, true);
                }
                foreach (ShipDeckMotionProvider d in go.GetComponents<ShipDeckMotionProvider>())
                    Object.DestroyImmediate(d, true);
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                    Object.DestroyImmediate(rb, true);
                return go.GetComponent<Rigidbody>() == null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShipSandbox] Could not strip '{go.name}' in place ({e.Message}); unpacking.");
                return false;
            }
        }

        private static void RemoveLegacySailSystem(GameObject root)
        {
#pragma warning disable CS0618
            foreach (ShipSailSystem legacy in root.GetComponentsInChildren<ShipSailSystem>(true))
            {
                Object.DestroyImmediate(legacy, true);
                Debug.Log("[ShipSandbox] Removed obsolete ShipSailSystem (double sail forces from FixedUpdate).");
            }
#pragma warning restore CS0618
        }

        private static void ConfigureShipNetworking(GameObject root)
        {
            var rb = root.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            var nrb = root.GetComponent<Fusion.Addons.Physics.NetworkRigidbody3D>();
            if (nrb != null)
            {
                var so = new SerializedObject(nrb);
                SerializedProperty target = so.FindProperty("_interpolationTarget");
                if (target != null)
                    target.objectReferenceValue = null;
                SerializedProperty precise = so.FindProperty("UsePreciseRotation");
                if (precise != null)
                    precise.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void InstallHelm(GameObject root)
        {
            var helm = root.GetComponentInChildren<ShipHelm>(true);
            if (helm == null)
            {
                Debug.LogWarning("[ShipSandbox] NetworkShip has no ShipHelm — helm skipped.");
                return;
            }

            if (root.GetComponentInChildren<HelmStation>(true) != null &&
                new SerializedObject(helm).FindProperty("wheelModel").objectReferenceValue != null)
                return; // built from parts: wheel, stand and helm zone already wired

            Transform wheelModule = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "ShipWheelModule" || t.GetComponent("ShipWheelModule") != null);
            if (wheelModule == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WheelModulePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[ShipSandbox] Wheel module prefab missing at {WheelModulePath}.");
                    return;
                }
                Transform modules = root.transform.Find("ShipModules") ?? new GameObject("ShipModules").transform;
                modules.SetParent(root.transform, false);
                Transform wheelParent = modules.Find("WheelModules");
                if (wheelParent == null)
                {
                    wheelParent = new GameObject("WheelModules").transform;
                    wheelParent.SetParent(modules, false);
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, wheelParent);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                wheelModule = instance.transform;
                Debug.Log("[ShipSandbox] Installed ShipWheelModule (wheel, stand, rudder, helm zone).");
            }

            Transform wheel = Find(wheelModule, "StylShip_Wheel");
            Transform stand = Find(wheelModule, "Anchor");
            var helmSo = new SerializedObject(helm);
            if (wheel != null) helmSo.FindProperty("wheelModel").objectReferenceValue = wheel;
            if (stand != null) helmSo.FindProperty("standAnchor").objectReferenceValue = stand;
            helmSo.FindProperty("autoFlipStandToAft").boolValue = true;
            helmSo.ApplyModifiedPropertiesWithoutUndo();

            Transform zone = Find(wheelModule, "Helm_Zone");
            if (zone == null && wheel != null)
            {
                zone = new GameObject("Helm_Zone").transform;
                zone.SetParent(wheelModule, false);
                zone.position = wheel.position;
                var box = zone.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(1.6f, 1.6f, 0.8f);
            }
            if (zone == null)
                return;

            zone.gameObject.SetActive(true);
            foreach (Collider c in zone.GetComponents<Collider>())
                c.isTrigger = true;
            var station = zone.GetComponent<HelmStation>() ?? zone.gameObject.AddComponent<HelmStation>();
            station.Configure(helm);
            station.Prompt = "Встать за штурвал";
            station.MaxDistance = 2.6f;
            station.SetColliders(zone.GetComponents<Collider>());
        }

        private static void ApplyHelmStand(GameObject root, ShipPlan plan)
        {
            var helm = root.GetComponentInChildren<ShipHelm>(true);
            if (helm == null || !plan.HelmStand.HasValue)
                return;
            var stand = new GameObject($"{Generated}HelmStand").transform;
            stand.SetParent(helm.transform, false);
            stand.position = root.transform.TransformPoint(plan.HelmStand.Value);
            stand.rotation = root.transform.rotation;
            var so = new SerializedObject(helm);
            so.FindProperty("standAnchor").objectReferenceValue = stand;
            so.FindProperty("autoFlipStandToAft").boolValue = false; // already on the aft side
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ClearGenerated(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && t != root.transform && t.name.StartsWith(Generated))
                    Object.DestroyImmediate(t.gameObject, true);
        }

        private static void ApplyRails(GameObject root, ShipPlan plan)
        {
            foreach (RailPlan rail in plan.Rails)
            {
                Transform module = root.transform.Find(rail.ModulePath) ?? root.transform;
                bool deckLadder = rail.Level >= 100;
                var go = new GameObject(deckLadder ? $"{Generated}DeckLadder_{rail.Level - 100}" : $"{Generated}ClimbRail_{module.name}_{rail.Level}");
                go.transform.SetParent(module, false);
                go.transform.position = root.transform.TransformPoint(new Vector3(rail.Axis.x, 0f, rail.Axis.z));
                go.transform.rotation = root.transform.rotation;

                Vector3 bottom = new Vector3(0f, rail.BottomGripY, 0f);
                Vector3 top = new Vector3(0f, rail.TopGripY, 0f);

                // Trigger around the trunk for the view ray (the climber stands outside it).
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.direction = 1;
                capsule.radius = rail.TrunkRadius + 0.25f;
                float lo = rail.BottomGripY - 1.0f;
                float hi = rail.TopGripY + 0.2f;
                capsule.center = new Vector3(0f, (lo + hi) * 0.5f, 0f);
                capsule.height = Mathf.Max(capsule.radius * 2f, hi - lo);

                Transform exit = null;
                if (rail.Exit.HasValue)
                {
                    exit = new GameObject("TopExit").transform;
                    exit.SetParent(go.transform, false);
                    exit.position = root.transform.TransformPoint(rail.Exit.Value);
                }

                var surface = go.AddComponent<ClimbSurface>();
                surface.Configure(bottom, top, go.transform.InverseTransformDirection(root.transform.TransformDirection(rail.Outward)),
                    0.25f, rail.TrunkRadius + 0.26f, exit, rail.TrunkRadius);
                // Body hugs the mast (chest ~10 cm off the wood); collisions are relaxed while climbing.
                surface.Prompt = deckLadder ? "Подняться по трапу" : rail.Level == 0 ? "Лезть на мачту" : "Лезть выше";
                surface.MaxDistance = 2.4f;
            }
        }

        private static void ApplyStairs(GameObject root, ShipPlan plan)
        {
            Material wood = SandboxMaterials.Wood();
            int index = 0;
            foreach (StairPlan st in plan.Stairs)
            {
                var go = new GameObject($"{Generated}Stairs_{index++}");
                go.transform.SetParent(root.transform, false);
                Vector3 foot = st.Foot, top = st.Top;
                Vector3 along = top - foot;              // up the stairs
                Vector3 flat = new Vector3(along.x, 0f, along.z).normalized;
                go.transform.localPosition = foot;
                go.transform.localRotation = Quaternion.LookRotation(flat, Vector3.up);

                float run = new Vector2(along.x, along.z).magnitude;
                float rise = along.y;
                float length = along.magnitude;
                float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

                // Walkable ramp: its TOP face is the slope from foot to lip (starts a bit into the deck).
                var ramp = new GameObject("Ramp");
                ramp.transform.SetParent(go.transform, false);
                const float thick = 0.3f;
                ramp.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
                ramp.transform.localPosition = new Vector3(0f, rise * 0.5f, run * 0.5f) +
                                               ramp.transform.localRotation * new Vector3(0f, -thick * 0.5f, 0f);
                var box = ramp.AddComponent<BoxCollider>();
                box.size = new Vector3(st.Width, thick, length + 0.1f);

                // Visual treads + stringers (no colliders).
                int steps = Mathf.Max(3, Mathf.RoundToInt(rise / 0.2f));
                for (int i = 0; i < steps; i++)
                {
                    float h = rise * (i + 1) / steps;
                    float d = run * (i + 0.5f) / steps;
                    var tread = Visual(PrimitiveType.Cube, go.transform, wood,
                        new Vector3(0f, h - 0.03f, d), new Vector3(st.Width, 0.06f, run / steps + 0.04f));
                    tread.name = $"Tread_{i}";
                }
                foreach (float side in new[] { -0.5f, 0.5f })
                {
                    var stringer = Visual(PrimitiveType.Cube, go.transform, wood,
                        new Vector3(side * (st.Width + 0.08f), rise * 0.5f - 0.05f, run * 0.5f), new Vector3(0.08f, 0.22f, length));
                    stringer.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
                    stringer.name = "Stringer";
                    // Hand rail.
                    var rail = Visual(PrimitiveType.Cube, go.transform, wood,
                        new Vector3(side * (st.Width + 0.08f), rise * 0.5f + 0.85f, run * 0.5f), new Vector3(0.05f, 0.05f, length));
                    rail.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
                    rail.name = "HandRail";
                }
            }
        }

        /// <summary>A render-only primitive (collider stripped).</summary>
        private static GameObject Visual(PrimitiveType type, Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.layer = 2; // Ignore Raycast: decoration never blocks the view ray
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private const string RiggingConfigPath = "Assets/Source/Configs/FeatureScenes/RiggingFeatureConfig.asset";

        public static RiggingConfig EnsureRiggingConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<RiggingConfig>(RiggingConfigPath);
            if (cfg != null) return cfg;
            if (!AssetDatabase.IsValidFolder("Assets/Source/Configs/FeatureScenes"))
                AssetDatabase.CreateFolder("Assets/Source/Configs", "FeatureScenes");
            cfg = ScriptableObject.CreateInstance<RiggingConfig>();
            AssetDatabase.CreateAsset(cfg, RiggingConfigPath);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        // Pin rails (visible wood + pins), lead blocks on the masts, the lines and their free-end handles.
        private static void ApplyRigging(GameObject root, ShipPlan plan)
        {
            Material wood = SandboxMaterials.Wood();
            Material dark = SandboxMaterials.DarkWood();
            Material brass = SandboxMaterials.Brass();
            Material rope = SandboxMaterials.Rope();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var railPins = new List<BelayPin[]>();
            for (int r = 0; r < plan.PinRails.Count; r++)
            {
                PinRailPlan rail = plan.PinRails[r];
                Transform module = root.transform.Find(rail.ModulePath) ?? root.transform;
                var go = new GameObject($"{Generated}PinRail_{r}");
                go.transform.SetParent(module, false);
                go.transform.position = root.transform.TransformPoint(rail.Center);
                go.transform.rotation = root.transform.rotation * Quaternion.LookRotation(rail.Along, Vector3.up);

                float length = rail.Pins * PinSpacing + 0.2f;
                Visual(PrimitiveType.Cube, go.transform, dark, new Vector3(0f, -0.04f, 0f), new Vector3(0.14f, 0.09f, length)).name = "Rail";
                foreach (float end in new[] { -0.5f, 0.5f })
                    Visual(PrimitiveType.Cube, go.transform, dark,
                        new Vector3(0f, -RailHeight * 0.5f, end * (length - 0.1f)), new Vector3(0.1f, RailHeight, 0.1f)).name = "Post";

                // Name board with the label on its deck-side face (3D text has no depth test, so a
                // second label on the back would show through mirrored).
                Vector3 outwardW = root.transform.TransformDirection(rail.Outward);
                var board = Visual(PrimitiveType.Cube, go.transform, dark, Vector3.zero, Vector3.one);
                board.name = "NameBoard";
                board.transform.position = go.transform.position + Vector3.up * 0.45f;
                board.transform.rotation = root.transform.rotation * Quaternion.LookRotation(rail.Outward, Vector3.up);
                board.transform.localScale = new Vector3(0.95f, 0.16f, 0.03f);
                foreach (float face in new[] { -1f })
                {
                    var labelGo = new GameObject("Label");
                    labelGo.transform.SetParent(go.transform, false);
                    labelGo.transform.position = go.transform.position + Vector3.up * 0.45f + outwardW * (0.022f * face);
                    labelGo.transform.rotation = root.transform.rotation * Quaternion.LookRotation(-rail.Outward * face, Vector3.up);
                    labelGo.layer = 2;
                    var text = labelGo.AddComponent<TextMesh>();
                    text.text = rail.Label.ToUpperInvariant();
                    text.font = font;
                    text.fontSize = 48;
                    text.characterSize = 0.0105f;
                    text.anchor = TextAnchor.MiddleCenter;
                    text.color = new Color(1f, 0.92f, 0.7f);
                    Material labelMat = SandboxMaterials.LabelText(font);
                    if (labelMat != null) labelGo.GetComponent<MeshRenderer>().sharedMaterial = labelMat;
                }

                var pins = new BelayPin[rail.Pins];
                for (int k = 0; k < rail.Pins; k++)
                {
                    var pinGo = new GameObject($"Pin_{k}");
                    pinGo.transform.SetParent(go.transform, false);
                    pinGo.transform.localPosition = new Vector3(0f, 0f, (k - (rail.Pins - 1) * 0.5f) * PinSpacing);
                    Visual(PrimitiveType.Cylinder, pinGo.transform, wood, new Vector3(0f, 0.04f, 0f), new Vector3(0.045f, 0.17f, 0.045f)).name = "Pin";
                    Visual(PrimitiveType.Sphere, pinGo.transform, brass, new Vector3(0f, 0.22f, 0f), Vector3.one * 0.06f).name = "Knob";
                    var tie = new GameObject("Tie").transform;
                    tie.SetParent(pinGo.transform, false);
                    tie.localPosition = new Vector3(0f, 0.12f, 0f);
                    var trigger = pinGo.AddComponent<BoxCollider>();
                    trigger.isTrigger = true;
                    trigger.center = new Vector3(0f, 0.05f, 0f);
                    trigger.size = new Vector3(0.22f, 0.5f, 0.26f);
                    var pin = pinGo.AddComponent<BelayPin>();
                    pin.Configure($"нагель «{rail.Label}»", tie);
                    pin.MaxDistance = 2.4f;
                    pins[k] = pin;
                }
                railPins.Add(pins);
            }

            foreach (LinePlan l in plan.Lines)
            {
                if (l.Rail < 0 || l.Rail >= railPins.Count) continue;
                BelayPin home = railPins[l.Rail][Mathf.Clamp(l.Pin, 0, railPins[l.Rail].Length - 1)];
                Transform module = root.transform.Find(l.ModulePath) ?? root.transform;

                var go = new GameObject($"{Generated}Line_{l.Line}_{l.Kind}");
                go.transform.SetParent(module, false);
                go.transform.position = root.transform.TransformPoint(l.Block);
                go.transform.rotation = root.transform.rotation;

                // Lead block (pulley) on the mast.
                var block = Visual(PrimitiveType.Cylinder, go.transform, dark, Vector3.zero, new Vector3(0.16f, 0.04f, 0.16f));
                block.name = "Block";
                block.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Visual(PrimitiveType.Sphere, go.transform, brass, new Vector3(0f, 0.1f, 0f), Vector3.one * 0.05f).name = "Shackle";

                var aloft = new GameObject("Aloft").transform;
                aloft.SetParent(go.transform, false);
                aloft.position = root.transform.TransformPoint(l.Aloft);

                var endGo = new GameObject("End");
                endGo.transform.SetParent(go.transform, false);
                endGo.transform.position = home.transform.position;
                var endCol = endGo.AddComponent<SphereCollider>();
                endCol.isTrigger = true;
                endCol.radius = 0.22f;
                var end = endGo.AddComponent<RopeEnd>();
                end.MaxDistance = 2.4f;

                float outMin = Vector3.Distance(go.transform.position, home.TiePoint) + 0.25f;
                var line = go.AddComponent<RigLine>();
                line.Configure(l.Line, l.Kind, l.Sail, l.Name, go.transform, aloft, home, end, outMin, 5f, l.Initial, rope);
                end.Configure(line);
            }

            var ship = root.GetComponent<NetworkShip>();
            if (ship != null)
            {
                var so = new SerializedObject(ship);
                SerializedProperty cfg = so.FindProperty("riggingConfig");
                if (cfg != null) cfg.objectReferenceValue = EnsureRiggingConfig();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ApplySpawns(GameObject root, ShipPlan plan)
        {
            var parent = new GameObject($"{Generated}CrewSpawns").transform;
            parent.SetParent(root.transform, false);
            for (int i = 0; i < plan.Spawns.Count; i++)
            {
                var t = new GameObject($"CrewSpawn_{i}").transform;
                t.SetParent(parent, false);
                t.position = root.transform.TransformPoint(plan.Spawns[i]);
                t.rotation = root.transform.rotation; // facing the bow
            }

            var ship = root.GetComponent<NetworkShip>();
            if (ship != null)
            {
                var so = new SerializedObject(ship);
                SerializedProperty spawns = so.FindProperty("crewSpawns");
                spawns.arraySize = parent.childCount;
                for (int i = 0; i < parent.childCount; i++)
                    spawns.GetArrayElementAtIndex(i).objectReferenceValue = parent.GetChild(i);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // =========================================================================================
        // Player prefab
        // =========================================================================================
        private const string PlayerControllerPath = "Assets/Source/Animation/NetworkPlayer.controller";
        private const string IdleClipPath = "Assets/PolyOne/Free Stickman/Animation/Idle.anim";

        /// <summary>
        /// The pack has no climb clip: climbing used the WALK cycle (legs walking on air). Use the
        /// idle pose instead — ProceduralClimbRig puts hands/feet on the mast — and enable the IK
        /// pass on the base layer (without it OnAnimatorIK never ran: no grips, no head look).
        /// </summary>
        public static void ConfigurePlayerAnimator()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(PlayerControllerPath);
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
            if (controller == null || idle == null)
            {
                Debug.LogWarning("[ShipSandbox] Player animator or Idle clip not found — climb pose skipped.");
                return;
            }

            UnityEditor.Animations.AnimatorControllerLayer[] layers = controller.layers;
            for (int l = 0; l < layers.Length; l++)
            {
                if (l == 0)
                    layers[l].iKPass = true;
                foreach (UnityEditor.Animations.ChildAnimatorState child in layers[l].stateMachine.states)
                    if (child.state.name == "Climbing" || child.state.name == "UpperClimb")
                        child.state.motion = idle;
            }
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[ShipSandbox] Animator: climb states use the idle pose, IK pass on.");
        }

        public static void PreparePlayerPrefab()
        {
            ConfigurePlayerAnimator();
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                int player = LayerMask.NameToLayer("Player");
                int ragdoll = LayerMask.NameToLayer("Ragdoll");
                var rootBody = root.GetComponent<Rigidbody>();
                if (player >= 0) root.layer = player;
                if (rootBody != null)
                {
                    rootBody.interpolation = RigidbodyInterpolation.None;
                    rootBody.constraints = RigidbodyConstraints.FreezeRotation;
                    rootBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
                if (ragdoll >= 0)
                    foreach (Rigidbody bone in root.GetComponentsInChildren<Rigidbody>(true))
                        if (bone != rootBody)
                        {
                            bone.gameObject.layer = ragdoll;
                            bone.interpolation = RigidbodyInterpolation.None;
                        }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[ShipSandbox] NetworkPlayer prepared (layers, interpolation).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =========================================================================================
        // Scene
        // =========================================================================================
        public const string RiggingScenePath = "Assets/Scenes/RiggingScene.unity";

        public static void BuildScene() => BuildScene(ScenePath, "Moderate", riggingDebugOpen: false);

        /// <param name="seaState">OceanSeaStateProfile asset name fragment the scene starts in.</param>
        public static void BuildScene(string scenePath, string seaState, bool riggingDebugOpen)
        {
            ControlHintsConfig hints = EnsureHintsAsset();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Scene gameplay = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);

            GameObject light = CopyRoot(gameplay, "Directional Light", scene);
            GameObject ocean = CopyRoot(gameplay, "OceanSystem", scene);
            GameObject manager = CopyRoot(gameplay, "GameManager", scene);
            // The brig's waterline is ship-local y ≈ 0: spawn it at sea level (slightly high, it settles).
            float shipY = 0.25f;
            if (ocean != null) shipY += ocean.transform.position.y;
            EditorSceneManager.CloseScene(gameplay, true);
            SceneManager.SetActiveScene(scene);

            if (light == null)
            {
                light = new GameObject("Directional Light");
                var l = light.AddComponent<Light>();
                l.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // Camera + rig.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 75f;
            camGo.AddComponent<AudioListener>();
            AddUrpCameraData(camGo);
            camGo.AddComponent<RumOverboard.Gameplay.PlayerCameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(-14f, shipY + 7f, -16f), Quaternion.Euler(18f, 40f, 0f));

            // Ship spawn pose (the host spawns NetworkShip here).
            var shipSpawn = new GameObject("ShipSpawn");
            SceneManager.MoveGameObjectToScene(shipSpawn, scene);
            shipSpawn.transform.SetPositionAndRotation(new Vector3(0f, shipY, 0f), Quaternion.identity);

            if (ocean != null)
            {
                var surface = ocean.GetComponent<OceanSurfaceRenderer>();
                if (surface != null)
                {
                    var so = new SerializedObject(surface);
                    SetRef(so, "followCamera", cam);
                    SetRef(so, "followTarget", null);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var depth = ocean.GetComponent<OceanDepthProvider>();
                if (depth != null)
                {
                    var so = new SerializedObject(depth);
                    SetRef(so, "terrain", null);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            else
            {
                Debug.LogWarning("[ShipSandbox] Gameplay scene has no OceanSystem — run RumOverboard ▸ Setup ▸ Ocean first.");
            }

            if (manager == null)
            {
                manager = new GameObject("GameManager");
                SceneManager.MoveGameObjectToScene(manager, scene);
                manager.AddComponent<ConnectionManager>();
                Debug.LogWarning("[ShipSandbox] Created a fresh GameManager — assign Player/Ship prefabs on it.");
            }
            var cm = manager.GetComponent<ConnectionManager>();
            {
                var so = new SerializedObject(cm);
                so.FindProperty("autoStartOnPlay").boolValue = true;
                so.FindProperty("autoStartMode").intValue = (int)GameMode.AutoHostOrClient;
                so.FindProperty("fallbackToSingle").boolValue = true;
                SetRef(so, "shipSpawnPoint", shipSpawn.transform);
                so.FindProperty("sceneShipPlaceholderName").stringValue = string.Empty;
                so.FindProperty("spawnShipOnSessionStart").boolValue = true;
                so.FindProperty("spawnPoints").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // HUD (session/ping) + hint overlay.
            var hud = new GameObject("HUD");
            SceneManager.MoveGameObjectToScene(hud, scene);
            hud.AddComponent<RumOverboard.Gameplay.GameplayHud>();

            var hintGo = new GameObject("HintOverlay");
            SceneManager.MoveGameObjectToScene(hintGo, scene);
            var overlay = hintGo.AddComponent<HintOverlay>();
            var hso = new SerializedObject(overlay);
            SetRef(hso, "config", hints);
            hso.ApplyModifiedPropertiesWithoutUndo();

            // Sea: start state, haze, sound. Rigging debug window (F2).
            if (ocean != null)
                OceanLookSetup.SetStartSeaState(ocean.GetComponent<OceanSeaStateController>(), seaState);
            OceanLookSetup.ApplySceneAtmosphere();

            var ambience = new GameObject("OceanAmbience");
            SceneManager.MoveGameObjectToScene(ambience, scene);
            ambience.AddComponent<AudioSource>();
            ambience.AddComponent<OceanAmbience>();

            var debugGo = new GameObject("RiggingDebug");
            SceneManager.MoveGameObjectToScene(debugGo, scene);
            var debugWindow = debugGo.AddComponent<RiggingDebugWindow>();
            var dso = new SerializedObject(debugWindow);
            dso.FindProperty("visible").boolValue = riggingDebugOpen;
            dso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, scenePath);
            AddToBuildSettings(scenePath);
            Debug.Log($"[ShipSandbox] Scene saved: {scenePath} (sea: {seaState})");
        }

        private static ControlHintsConfig EnsureHintsAsset()
        {
            var hints = AssetDatabase.LoadAssetAtPath<ControlHintsConfig>(HintsAssetPath);
            if (hints != null)
            {
                hints.ApplyDefaults(); // controls changed with the rigging rework — keep the asset in sync
                EditorUtility.SetDirty(hints);
                AssetDatabase.SaveAssets();
                return hints;
            }
            hints = ScriptableObject.CreateInstance<ControlHintsConfig>();
            hints.ApplyDefaults();
            AssetDatabase.CreateAsset(hints, HintsAssetPath);
            AssetDatabase.SaveAssets();
            return hints;
        }

        private static void AddUrpCameraData(GameObject cam)
        {
            Type t = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (t != null && cam.GetComponent(t) == null)
                cam.AddComponent(t);
        }

        private static GameObject FindRoot(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        private static GameObject CopyRoot(Scene from, string name, Scene to)
        {
            GameObject src = FindRoot(from, name);
            if (src == null)
                return null;
            GameObject copy = Object.Instantiate(src);
            copy.name = name;
            SceneManager.MoveGameObjectToScene(copy, to);
            return copy;
        }

        private static void SetRef(SerializedObject so, string prop, Object value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null)
                p.objectReferenceValue = value;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path))
                return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // =========================================================================================
        private static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root) return string.Empty;
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        private static string PrettyMastName(string moduleName)
        {
            if (moduleName.StartsWith("Front") || moduleName.StartsWith("Fore")) return "Фок-мачта";
            if (moduleName.StartsWith("Main")) return "Грот-мачта";
            if (moduleName.StartsWith("Back")) return "Бизань-мачта";
            return moduleName;
        }

        private static string PrettySailName(string raw)
        {
            string n = raw.Replace("StylShip_", string.Empty).Replace("Sail", string.Empty);
            return n switch
            {
                "Front" => "фок",
                "Mid1" => "грот",
                "Mid2" => "грот-марсель",
                "Mid" => "грот",
                "Back" => "бизань",
                "ForeCourse" => "фок",
                "ForeTopsail" => "фор-марсель",
                "MainCourse" => "грот",
                "MainTopsail" => "грот-марсель",
                _ => string.IsNullOrEmpty(n) ? raw : n,
            };
        }
    }
}
