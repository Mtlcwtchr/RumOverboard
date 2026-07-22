#if FUSION2
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Editor;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Networking;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    /// <summary>
    /// One-click networking setup for the ship prefab, matching the hybrid model in
    /// <c>NetworkShip</c>: the Fusion Physics addon's <see cref="NetworkRigidbody3D"/> owns the
    /// transform + velocities (replicated and interpolated by the addon), and NetworkShip only
    /// replicates the derived ocean/impact values.
    ///
    /// What it does on <c>Assets/Source/Prefabs/NetworkShip.prefab</c> (idempotent):
    ///   • ensures a NetworkObject on the root (NetworkRigidbody3D must live on one);
    ///   • ensures a dynamic (non-kinematic) Rigidbody with Fusion-driven interpolation (None);
    ///   • removes any NetworkTransform (mutually exclusive with NetworkRigidbody3D);
    ///   • adds NetworkRigidbody3D if missing;
    ///   • saves the prefab (Fusion re-bakes the NetworkObject on import) and rebuilds the
    ///     prefab table so Fusion registers the changed behaviour list.
    ///
    /// Menu: RumOverboard ▸ Networking ▸ Setup Ship Networking
    /// Batch: -executeMethod RumOverboard.EditorTools.ShipNetworkingSetup.RunFromCommandLine
    /// </summary>
    public static class ShipNetworkingSetup
    {
        private const string ShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";
        private const string Menu = "RumOverboard/Networking/Setup Ship Networking";
        private const string RebuildVisualMenu = "RumOverboard/Networking/Rebuild NetworkShip Visual Colliders + Sail Anchors";
        private const string VisualShipName = "VisualShip";

        // Runs the setup automatically once (per project) the first time this script loads, so the
        // ship is configured without a manual menu click. Deferred to idle so it doesn't fight the
        // importer; the guard is only set on success, so a failed/too-early run retries next reload.
        [InitializeOnLoadMethod]
        private static void AutoRunOnce()
        {
            string key = "RumOverboard.ShipNetworkingSetup.Done.v6:" + Application.dataPath;
            if (EditorPrefs.GetBool(key, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(key, false))
                    return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath) == null)
                    return; // prefab not imported yet — retry on the next domain reload
                if (Setup(out string summary))
                {
                    EditorPrefs.SetBool(key, true);
                    Debug.Log($"[ShipNetworkingSetup] Auto-setup ran. {summary}");
                }
            };
        }

        [MenuItem(Menu, true)]
        private static bool Validate() => AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath) != null;

        [MenuItem(RebuildVisualMenu, true)]
        private static bool ValidateRebuildVisual() => AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath) != null;

        [MenuItem(Menu)]
        private static void SetupFromMenu()
        {
            Setup(out string summary);
            EditorUtility.DisplayDialog("Ship Networking", summary, "OK");
        }

        [MenuItem(RebuildVisualMenu)]
        private static void RebuildVisualFromMenu()
        {
            RebuildVisualCollidersAndSails(out string summary);
            EditorUtility.DisplayDialog("NetworkShip Visual Sync", summary, "OK");
        }

        /// <summary>Entry point for headless runs (Unity -batchmode -executeMethod ...).</summary>
        public static void RunFromCommandLine()
        {
            bool ok = Setup(out string summary);
            Debug.Log($"[ShipNetworkingSetup] {summary}");
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Performs the setup on the ship prefab. Returns true on success; <paramref name="summary"/>
        /// describes what changed (or why it failed).
        /// </summary>
        public static bool Setup(out string summary)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (asset == null)
            {
                summary = $"Ship prefab not found at '{ShipPrefabPath}'.";
                Debug.LogError($"[ShipNetworkingSetup] {summary}");
                return false;
            }

            // Edit the prefab asset in isolation (safe for prefab-asset mutation from code).
            GameObject root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            bool changed = false;
            var log = new System.Text.StringBuilder();

            try
            {
                // 1) NetworkObject on the root (NetworkRigidbody3D requires one in the hierarchy).
                if (!root.TryGetComponent(out NetworkObject _))
                {
                    root.AddComponent<NetworkObject>();
                    changed = true;
                    log.AppendLine("• added NetworkObject");
                }

                // 2) Rigidbody: dynamic + Fusion-driven interpolation (the addon interpolates proxies).
                if (!root.TryGetComponent(out Rigidbody rb))
                {
                    rb = root.AddComponent<Rigidbody>();
                    changed = true;
                    log.AppendLine("• added Rigidbody");
                }
                if (rb.isKinematic)
                {
                    rb.isKinematic = false; // host simulates; the addon sets proxies kinematic at runtime
                    changed = true;
                    log.AppendLine("• Rigidbody.isKinematic → false");
                }
                if (rb.interpolation != RigidbodyInterpolation.None)
                {
                    rb.interpolation = RigidbodyInterpolation.None; // NetworkRigidbody3D drives interpolation
                    changed = true;
                    log.AppendLine("• Rigidbody.interpolation → None");
                }

                // Heavier, more realistic hull. Auto-buoyancy on ShipBuoyancyController scales the
                // float force to this mass, so raising it stays seaworthy. Don't clobber a heavier
                // mass a designer may have already dialed in.
                if (rb.mass < 5000f)
                {
                    rb.mass = 12000f;
                    changed = true;
                    log.AppendLine("• Rigidbody.mass → 12000");
                }
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.35f;
                Vector3 targetCom = new Vector3(0f, -0.8f, 0f); // low COM → self-rights, resists capsize
                if ((rb.centerOfMass - targetCom).sqrMagnitude > 1e-4f)
                {
                    rb.centerOfMass = targetCom;
                    changed = true;
                    log.AppendLine("• Rigidbody.centerOfMass lowered");
                }

                // 3) NetworkTransform is mutually exclusive with NetworkRigidbody3D — remove it.
                if (root.TryGetComponent(out NetworkTransform legacy))
                {
                    Object.DestroyImmediate(legacy, true);
                    changed = true;
                    log.AppendLine("• removed NetworkTransform");
                }

                // 4) NetworkRigidbody3D owns the transform. [DisallowMultipleComponent] → add once.
                if (!root.TryGetComponent(out NetworkRigidbody3D _))
                {
                    root.AddComponent<NetworkRigidbody3D>();
                    changed = true;
                    log.AppendLine("• added NetworkRigidbody3D");
                }

                // 5) Ensure sail system exists on the ship root.
                if (!root.TryGetComponent(out ShipSailSystem sailSystem))
                {
                    sailSystem = root.AddComponent<ShipSailSystem>();
                    changed = true;
                    log.AppendLine("• added ShipSailSystem");
                }

                // Keep sail system bound to the same Rigidbody the ship uses.
                var sailSo = new SerializedObject(sailSystem);
                var sailBodyProp = sailSo.FindProperty("shipBody");
                if (sailBodyProp != null && sailBodyProp.objectReferenceValue != rb)
                {
                    sailBodyProp.objectReferenceValue = rb;
                    sailSo.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                    log.AppendLine("• ShipSailSystem.shipBody wired");
                }

                // 6) Wire NetworkShip reference to sail system so it's visible in inspector.
                if (root.TryGetComponent(out NetworkShip networkShip))
                {
                    var netSo = new SerializedObject(networkShip);
                    var sailRef = netSo.FindProperty("sailSystem");
                    if (sailRef != null && sailRef.objectReferenceValue != sailSystem)
                    {
                        sailRef.objectReferenceValue = sailSystem;
                        netSo.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                        log.AppendLine("• NetworkShip.sailSystem wired");
                    }
                }

                // 6b) Helm: wheel + rudder steering component on the ship root.
                if (!root.TryGetComponent(out ShipHelm helm))
                {
                    helm = root.AddComponent<ShipHelm>();
                    changed = true;
                    log.AppendLine("• added ShipHelm");
                }

                // 7) Operate on the NetworkShip's own visual subtree, not the standalone stylized prefab.
                Transform visualRoot = FindVisualShipRoot(root.transform);
                if (visualRoot != null)
                {
                    int colliderOps = EnsureInteractiveVisualColliders(visualRoot);
                    if (colliderOps > 0)
                    {
                        changed = true;
                        log.AppendLine($"• visual colliders kept + interactive helpers synced ({colliderOps} ops)");
                    }

                    if (sailSystem != null)
                    {
                        sailSystem.SyncAnchorPointsFromCurrentShipVisual(syncMasts: true, rebuildSails: true);
                        changed = true;
                        log.AppendLine("• sail anchor points synced from NetworkShip visual");
                    }

                    if (WireHelm(helm, root.transform, visualRoot, log))
                        changed = true;
                }

                // Collision authoring safety: the ship is a DYNAMIC rigidbody, so all its walkable
                // geometry must be solid PRIMITIVE colliders (box) — walkable decks + stairs solid so
                // the crew can't fall through and can climb to the helm, while interact/climb/zone
                // markers stay triggers so they don't block or break detection.
                if (EnsureWalkableAndZoneColliders(root.transform, log))
                    changed = true;

                // The visual model's deck/hull/stairs use non-convex MeshColliders, which PhysX
                // rejects on the DYNAMIC ship rigidbody (crew falls through). Bake them CONVEX here
                // (cooked at import — no runtime mesh-readability needed), disable decorative meshes.
                if (EnsureHullMeshCollidersConvex(root.transform, log))
                    changed = true;
                else
                {
                    log.AppendLine("• VisualShip child not found on NetworkShip (skipped sail/collider sync)");
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // Saving reimports the prefab → Fusion's edit-time baker re-bakes the NetworkObject.
            // RebuildPrefabTable re-registers it (and refreshes) so the changed behaviour list sticks.
            AssetDatabase.SaveAssets();
            NetworkProjectConfigUtilities.RebuildPrefabTable();

            summary = changed
                ? $"Ship networking set up on '{ShipPrefabPath}':\n{log}\nPrefab re-baked and table rebuilt."
                : $"Ship networking already correct on '{ShipPrefabPath}' — no changes. Table rebuilt.";
            return true;
        }

        // Wire the ShipHelm to the wheel mesh / stand / rudder found in the visual subtree, and mark
        // the wheel's interaction zone as a Helm so players can take it.
        private static bool WireHelm(ShipHelm helm, Transform shipRoot, Transform visualRoot, System.Text.StringBuilder log)
        {
            if (helm == null || visualRoot == null)
                return false;

            bool changed = false;

            Transform wheel = FindDescendant(visualRoot, n => n == "StylShip_Wheel")
                              ?? FindDescendant(visualRoot, n => n.Contains("Wheel") && !n.Contains("Stand") && !n.Contains("Zone"));
            Transform stand = FindDescendant(visualRoot, n => n.Contains("WheelStand"))
                              ?? FindDescendant(visualRoot, n => n.Contains("Stand"));
            Transform rudder = FindDescendant(visualRoot, n => n.Contains("Rudder"));

            var so = new SerializedObject(helm);
            if (wheel != null && SetRef(so, "wheelModel", wheel)) { changed = true; log.AppendLine("• ShipHelm.wheelModel wired"); }
            Transform anchor = stand != null ? stand : wheel;
            if (anchor != null && SetRef(so, "standAnchor", anchor)) { changed = true; log.AppendLine("• ShipHelm.standAnchor wired"); }
            if (rudder != null)
            {
                var rp = so.FindProperty("rudderLocal");
                if (rp != null)
                {
                    Vector3 local = shipRoot.InverseTransformPoint(rudder.position);
                    if ((rp.vector3Value - local).sqrMagnitude > 1e-4f)
                    {
                        rp.vector3Value = local;
                        changed = true;
                        log.AppendLine("• ShipHelm.rudderLocal from rudder mesh");
                    }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Find an existing wheel zone, else CREATE a Helm trigger at the wheel stand so the crew
            // can actually take the wheel (without it, Interact near the wheel falls through to
            // drinking rum → the player just gets drunk/ragdolls instead of steering).
            Transform zoneT = FindDescendant(visualRoot, n => n.Contains("Wheel_Zone"))
                              ?? FindDescendant(visualRoot, n => n.Contains("Wheel") && n.Contains("Zone"))
                              ?? FindDescendant(shipRoot, n => n == "Helm_Zone");
            if (zoneT == null)
            {
                Transform zoneAnchor = stand != null ? stand : wheel;
                if (zoneAnchor != null)
                {
                    var zoneGo = new GameObject("Helm_Zone");
                    zoneGo.transform.SetParent(shipRoot, true);
                    zoneGo.transform.SetPositionAndRotation(zoneAnchor.position, zoneAnchor.rotation);
                    var box = zoneGo.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = Vector3.zero;
                    box.size = new Vector3(2.6f, 2.6f, 2.6f); // covers where the helmsman stands
                    zoneT = zoneGo.transform;
                    changed = true;
                    log.AppendLine("• created Helm_Zone trigger at the wheel stand");
                }
            }

            if (zoneT != null)
            {
                var zone = zoneT.GetComponent<Source.Scripts.Gameplay.ShipInteractionZone>();
                if (zone == null)
                    zone = zoneT.gameObject.AddComponent<Source.Scripts.Gameplay.ShipInteractionZone>();
                if (zone.Zone != Source.Scripts.Gameplay.ShipInteractionZone.ZoneKind.Helm)
                {
                    zone.Configure(Source.Scripts.Gameplay.ShipInteractionZone.ZoneKind.Helm, "Take the wheel");
                    changed = true;
                    log.AppendLine($"• '{zoneT.name}' set to Helm interaction zone");
                }
                if (zoneT.TryGetComponent(out Collider zc))
                    zc.isTrigger = true;
            }
            else
            {
                log.AppendLine("• could not create Helm zone — no wheel/stand transform found in the model");
            }

            return changed;
        }

        // Names starting Walkable_/Solid_/Blocked_ or ending _Solid are solid collision; names ending
        // _Zone or containing _Interact/_Climb are gameplay triggers. Force each collider accordingly.
        private static bool EnsureWalkableAndZoneColliders(Transform root, System.Text.StringBuilder log)
        {
            int solidOps = 0;
            int triggerOps = 0;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                bool zoneMarker = n.EndsWith("_Zone") || n.Contains("_Interact") || n.Contains("_Climb");
                bool solid = !zoneMarker &&
                             (n.StartsWith("Walkable_") || n.StartsWith("Solid_") || n.StartsWith("Blocked_") || n.EndsWith("_Solid"));
                if (!zoneMarker && !solid)
                    continue;

                var cols = t.GetComponents<Collider>();
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null)
                        continue;

                    if (!c.enabled)
                    {
                        c.enabled = true;
                        if (zoneMarker) triggerOps++; else solidOps++;
                    }

                    if (zoneMarker && !c.isTrigger)
                    {
                        c.isTrigger = true;
                        triggerOps++;
                    }
                    else if (solid && c.isTrigger)
                    {
                        c.isTrigger = false;
                        solidOps++;
                    }
                }
            }

            if (solidOps > 0 || triggerOps > 0)
            {
                log.AppendLine($"• collision fixed: {solidOps} solid (walkable/hull/stairs), {triggerOps} triggers (interact/climb/zone)");
                return true;
            }
            return false;
        }

        // Bakes the ship's mesh colliders for a dynamic rigidbody: walkable/hull/stairs meshes →
        // convex (valid + solid to stand/climb on); rigging/cloth/masts → collision disabled.
        private static bool EnsureHullMeshCollidersConvex(Transform root, System.Text.StringBuilder log)
        {
            int convexified = 0;
            int disabled = 0;

            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc == null)
                    continue;

                string n = mc.name;
                bool decorative = n.Contains("Sail") || n.Contains("Wire") || n.Contains("Rope")
                                  || n.Contains("Flag") || n.Contains("Cloth") || n.Contains("Mast")
                                  || n.Contains("Rigging");

                if (decorative)
                {
                    if (mc.enabled)
                    {
                        mc.enabled = false;
                        disabled++;
                    }
                    continue;
                }

                bool touched = false;
                if (!mc.convex) { mc.convex = true; touched = true; }
                if (!mc.enabled) { mc.enabled = true; touched = true; }
                if (touched)
                    convexified++;
            }

            if (convexified > 0 || disabled > 0)
            {
                log.AppendLine($"• hull mesh colliders baked: {convexified} → convex (walkable), {disabled} decorative disabled");
                return true;
            }
            return false;
        }

        private static Transform FindDescendant(Transform root, System.Func<string, bool> match)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (match(all[i].name))
                    return all[i];
            return null;
        }

        private static bool SetRef(SerializedObject so, string property, Object value)
        {
            var p = so.FindProperty(property);
            if (p == null || p.objectReferenceValue == value)
                return false;
            p.objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// Rebuild only visual helper colliders and sail anchors on NetworkShip,
        /// preserving existing source mesh colliders.
        /// </summary>
        public static bool RebuildVisualCollidersAndSails(out string summary)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (asset == null)
            {
                summary = $"Ship prefab not found at '{ShipPrefabPath}'.";
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ShipPrefabPath);
            bool changed = false;
            var log = new System.Text.StringBuilder();

            try
            {
                Transform visualRoot = FindVisualShipRoot(root.transform);
                if (visualRoot == null)
                {
                    summary = "VisualShip child not found on NetworkShip.";
                    return false;
                }

                int colliderOps = EnsureInteractiveVisualColliders(visualRoot);
                if (colliderOps > 0)
                {
                    changed = true;
                    log.AppendLine($"• visual colliders kept + interactive helpers synced ({colliderOps} ops)");
                }

                if (root.TryGetComponent(out ShipSailSystem sailSystem) && sailSystem != null)
                {
                    sailSystem.SyncAnchorPointsFromCurrentShipVisual(syncMasts: true, rebuildSails: true);
                    changed = true;
                    log.AppendLine("• sail anchor points synced from current NetworkShip Visual");
                }
                else
                {
                    log.AppendLine("• ShipSailSystem not found on root");
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, ShipPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();

            summary = changed
                ? $"NetworkShip visual rebuild complete:\n{log}"
                : "No visual changes were required.";

            return true;
        }

        private static Transform FindVisualShipRoot(Transform root)
        {
            if (root == null)
                return null;

            Transform exact = root.Find(VisualShipName);
            if (exact != null)
                return exact;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == VisualShipName)
                    return t;
            }

            return null;
        }

        private static int EnsureInteractiveVisualColliders(Transform visualRoot)
        {
            int operations = 0;
            if (visualRoot == null)
                return operations;

            foreach (Transform t in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == visualRoot)
                    continue;

                bool ropeLike = IsRopeLikeName(t.name);
                bool mastLike = IsMastLikeName(t.name);
                bool sailLike = IsSailLikeName(t.name);
                bool hasRenderableShape = TryGetLocalBoundsForCollider(t, out Bounds localBounds);

                Collider[] existing = t.GetComponents<Collider>();

                if (ropeLike)
                {
                    for (int i = 0; i < existing.Length; i++)
                    {
                        Collider c = existing[i];
                        if (c != null && c.enabled)
                        {
                            c.enabled = false;
                            operations++;
                        }
                    }

                    continue;
                }

                for (int i = 0; i < existing.Length; i++)
                {
                    Collider c = existing[i];
                    if (c == null)
                        continue;

                    if (c.isTrigger)
                    {
                        c.isTrigger = false;
                        operations++;
                    }

                    if (!c.enabled)
                    {
                        c.enabled = true;
                        operations++;
                    }
                }

                if (!hasRenderableShape)
                    continue;

                if (mastLike)
                {
                    Bounds mastBounds = localBounds;
                    if (TryGetMastLocalBounds(t, out Bounds preciseMastBounds))
                        mastBounds = preciseMastBounds;

                    operations += EnsureMastInteractiveCollider(t, mastBounds);
                }
                else if (sailLike)
                {
                    operations += EnsureSailInteractiveCollider(t, localBounds);
                }
            }

            return operations;
        }

        private static int EnsureMastInteractiveCollider(Transform mastTransform, Bounds localBounds)
        {
            const string helperName = "Interactive_MastCollider";
            int operations = 0;
            Transform helper = mastTransform.Find(helperName);
            if (helper == null)
            {
                var go = new GameObject(helperName);
                go.transform.SetParent(mastTransform, false);
                helper = go.transform;
                operations++;
            }

            if (!helper.TryGetComponent(out CapsuleCollider capsule))
            {
                Collider wrong = helper.GetComponent<Collider>();
                if (wrong != null)
                {
                    Object.DestroyImmediate(wrong, true);
                    operations++;
                }

                capsule = helper.gameObject.AddComponent<CapsuleCollider>();
                operations++;
            }

            Vector3 size = localBounds.size;
            // Keep mast helper centered on mast beam (pivot axis) to avoid drift from crows nests/gaffs.
            Vector3 center = new Vector3(0f, localBounds.center.y, 0f);
            float radius = Mathf.Max(0.03f, Mathf.Min(size.x, size.z) * 0.5f);
            if (TryEstimateMastRadiusFromMesh(mastTransform, out float estimatedRadius))
                radius = Mathf.Max(0.03f, estimatedRadius);
            radius = Mathf.Clamp(radius, 0.08f, 0.32f);
            float height = Mathf.Max(size.y, radius * 2.05f);

            if (capsule.direction != 1) { capsule.direction = 1; operations++; }
            if (capsule.center != center) { capsule.center = center; operations++; }
            if (!Mathf.Approximately(capsule.radius, radius)) { capsule.radius = radius; operations++; }
            if (!Mathf.Approximately(capsule.height, height)) { capsule.height = height; operations++; }
            if (capsule.isTrigger) { capsule.isTrigger = false; operations++; }
            if (!capsule.enabled) { capsule.enabled = true; operations++; }

            return operations;
        }

        private static bool TryEstimateMastRadiusFromMesh(Transform mastRoot, out float radius)
        {
            radius = 0f;
            if (mastRoot == null)
                return false;

            MeshFilter[] meshFilters = mastRoot.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters == null || meshFilters.Length == 0)
                return false;

            Matrix4x4 worldToMast = mastRoot.worldToLocalMatrix;
            bool hasAny = false;
            float maxRadSqr = 0f;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;
                if (!IsMastLikeName(mf.transform.name))
                    continue;

                Vector3[] verts = mf.sharedMesh.vertices;
                if (verts == null || verts.Length == 0)
                    continue;

                Matrix4x4 toMast = worldToMast * mf.transform.localToWorldMatrix;

                float minY = float.MaxValue;
                float maxY = float.MinValue;
                Vector3[] mastVerts = new Vector3[verts.Length];
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 p = toMast.MultiplyPoint3x4(verts[v]);
                    mastVerts[v] = p;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }

                float lowerCut = minY + (maxY - minY) * 0.35f;
                for (int v = 0; v < mastVerts.Length; v++)
                {
                    Vector3 p = mastVerts[v];
                    if (p.y > lowerCut)
                        continue;

                    float rSqr = p.x * p.x + p.z * p.z;
                    if (rSqr > maxRadSqr)
                        maxRadSqr = rSqr;
                    hasAny = true;
                }
            }

            if (!hasAny)
                return false;

            radius = Mathf.Sqrt(maxRadSqr);
            return radius > 0.001f;
        }

        private static int EnsureSailInteractiveCollider(Transform sailTransform, Bounds localBounds)
        {
            const string helperName = "Interactive_SailCollider";
            int operations = 0;
            Transform helper = sailTransform.Find(helperName);
            if (helper == null)
            {
                var go = new GameObject(helperName);
                go.transform.SetParent(sailTransform, false);
                helper = go.transform;
                operations++;
            }

            if (!helper.TryGetComponent(out BoxCollider box))
            {
                Collider wrong = helper.GetComponent<Collider>();
                if (wrong != null)
                {
                    Object.DestroyImmediate(wrong, true);
                    operations++;
                }

                box = helper.gameObject.AddComponent<BoxCollider>();
                operations++;
            }

            Vector3 size = localBounds.size;
            int thinAxis = 2;
            float minAxis = size.z;
            if (size.x < minAxis) { minAxis = size.x; thinAxis = 0; }
            if (size.y < minAxis) { thinAxis = 1; }

            const float minThickness = 0.05f;
            if (thinAxis == 0) size.x = Mathf.Max(size.x, minThickness);
            else if (thinAxis == 1) size.y = Mathf.Max(size.y, minThickness);
            else size.z = Mathf.Max(size.z, minThickness);

            if (box.center != localBounds.center) { box.center = localBounds.center; operations++; }
            if (box.size != size) { box.size = size; operations++; }
            if (box.isTrigger) { box.isTrigger = false; operations++; }
            if (!box.enabled) { box.enabled = true; operations++; }

            return operations;
        }

        private static void AddBestPrimitiveCollider(GameObject go, Bounds localBounds)
        {
            Vector3 size = localBounds.size;
            Vector3 center = localBounds.center;

            float minAxis = Mathf.Max(0.02f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
            float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float axisRatio = maxAxis / minAxis;

            if (axisRatio >= 3.2f)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = center;
                if (size.y >= size.x && size.y >= size.z)
                {
                    capsule.direction = 1;
                    capsule.radius = Mathf.Max(0.025f, Mathf.Min(size.x, size.z) * 0.5f);
                    capsule.height = Mathf.Max(size.y, capsule.radius * 2.02f);
                }
                else if (size.x >= size.z)
                {
                    capsule.direction = 0;
                    capsule.radius = Mathf.Max(0.025f, Mathf.Min(size.y, size.z) * 0.5f);
                    capsule.height = Mathf.Max(size.x, capsule.radius * 2.02f);
                }
                else
                {
                    capsule.direction = 2;
                    capsule.radius = Mathf.Max(0.025f, Mathf.Min(size.x, size.y) * 0.5f);
                    capsule.height = Mathf.Max(size.z, capsule.radius * 2.02f);
                }

                capsule.isTrigger = false;
                capsule.enabled = true;
                return;
            }

            float ratioXY = Mathf.Max(size.x, size.y) / Mathf.Max(0.001f, Mathf.Min(size.x, size.y));
            float ratioYZ = Mathf.Max(size.y, size.z) / Mathf.Max(0.001f, Mathf.Min(size.y, size.z));
            float ratioXZ = Mathf.Max(size.x, size.z) / Mathf.Max(0.001f, Mathf.Min(size.x, size.z));
            float maxRatio = Mathf.Max(ratioXY, Mathf.Max(ratioYZ, ratioXZ));

            if (maxRatio <= 1.28f)
            {
                var sphere = go.AddComponent<SphereCollider>();
                sphere.center = center;
                sphere.radius = Mathf.Max(0.025f, minAxis * 0.5f);
                sphere.isTrigger = false;
                sphere.enabled = true;
                return;
            }

            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = new Vector3(
                Mathf.Max(0.02f, size.x),
                Mathf.Max(0.02f, size.y),
                Mathf.Max(0.02f, size.z));
            box.isTrigger = false;
            box.enabled = true;
        }

        private static void AddMastCapsuleCollider(GameObject go, Bounds localBounds)
        {
            Vector3 size = localBounds.size;
            var capsule = go.AddComponent<CapsuleCollider>();

            capsule.direction = 1;
            capsule.center = localBounds.center;
            capsule.radius = Mathf.Max(0.03f, Mathf.Min(size.x, size.z) * 0.5f);
            capsule.height = Mathf.Max(size.y, capsule.radius * 2.05f);
            capsule.isTrigger = false;
            capsule.enabled = true;
        }

        private static void AddSailBoxCollider(GameObject go, Bounds localBounds)
        {
            Vector3 size = localBounds.size;

            int thinAxis = 0;
            float minAxis = size.x;
            if (size.y < minAxis)
            {
                minAxis = size.y;
                thinAxis = 1;
            }
            if (size.z < minAxis)
            {
                thinAxis = 2;
            }

            const float minThickness = 0.05f;
            if (thinAxis == 0) size.x = Mathf.Max(size.x, minThickness);
            else if (thinAxis == 1) size.y = Mathf.Max(size.y, minThickness);
            else size.z = Mathf.Max(size.z, minThickness);

            var box = go.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = new Vector3(
                Mathf.Max(0.02f, size.x),
                Mathf.Max(0.02f, size.y),
                Mathf.Max(0.02f, size.z));
            box.isTrigger = false;
            box.enabled = true;
        }

        private static bool TryGetLocalBoundsForCollider(Transform t, out Bounds localBounds)
        {
            localBounds = default;

            if (t.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                return true;
            }

            if (t.TryGetComponent(out Renderer renderer))
            {
                Vector3 localCenter = t.InverseTransformPoint(renderer.bounds.center);
                Vector3 lossy = t.lossyScale;
                Vector3 localSize = new Vector3(
                    renderer.bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
                    renderer.bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
                    renderer.bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
                localBounds = new Bounds(localCenter, localSize);
                return true;
            }

            return false;
        }

        private static bool TryGetMastLocalBounds(Transform mastRoot, out Bounds localBounds)
        {
            localBounds = default;
            bool hasBounds = false;

            MeshFilter[] meshFilters = mastRoot.GetComponentsInChildren<MeshFilter>(true);
            Matrix4x4 worldToMast = mastRoot.worldToLocalMatrix;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;
                if (!IsMastLikeName(mf.transform.name))
                    continue;

                Matrix4x4 toMast = worldToMast * mf.transform.localToWorldMatrix;
                Bounds b = TransformBounds(mf.sharedMesh.bounds, toMast);

                if (!hasBounds)
                {
                    localBounds = b;
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(b.min);
                    localBounds.Encapsulate(b.max);
                }
            }

            if (hasBounds)
                return true;

            return TryGetLocalBoundsForCollider(mastRoot, out localBounds);
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 c = localBounds.center;
            Vector3 e = localBounds.extents;

            Vector3[] points =
            {
                new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
            };

            Vector3 p0 = matrix.MultiplyPoint3x4(points[0]);
            Bounds transformed = new Bounds(p0, Vector3.zero);
            for (int i = 1; i < points.Length; i++)
                transformed.Encapsulate(matrix.MultiplyPoint3x4(points[i]));

            return transformed;
        }

        private static bool IsRopeLikeName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            return objectName.StartsWith("StylShip_Wire") ||
                   objectName.StartsWith("StylShip_Ropes") ||
                   objectName.IndexOf("rope", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("wire", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("stay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("shroud", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("ratline", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("bracing", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMastLikeName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            return objectName.IndexOf("mast", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                   objectName.IndexOf("crow", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                   objectName.IndexOf("climb", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsSailLikeName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            return objectName.IndexOf("sail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("spanker", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("jib", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
