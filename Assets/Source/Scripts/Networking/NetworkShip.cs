#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Authoritative gameplay wrapper for the ship. The host simulates the buoyant
    /// Rigidbody; everyone else sees an interpolated proxy.
    ///
    /// HYBRID REPLICATION — the transform is NOT hand-rolled here anymore:
    ///   • Pose + velocities (position, rotation, linear/angular velocity, kinematic,
    ///     constraints) are owned by the Fusion Physics addon's <b>NetworkRigidbody3D</b>.
    ///     It replicates them and interpolates proxies in its own Render(); proxies are
    ///     forced kinematic by the addon. Writing the pose ourselves would fight it.
    ///   • This component only replicates the two authority-only DERIVED values a proxy
    ///     cannot recompute from the synced transform:
    ///       - <see cref="LastImpactStrength"/> — a transient wave-impact event (buoyancy
    ///         runs on the authority only, so proxies would otherwise never see it);
    ///       - <see cref="OceanTime"/> — the authoritative ocean clock, so every peer
    ///         samples the same waves. Single source of truth for ocean time (do NOT also
    ///         put a NetworkOceanState in the scene, or the two will fight over the clock).
    ///
    /// Roll/pitch are intentionally not networked: they are pure functions of the replicated
    /// rotation, so ShipDeckMotionProvider reads them straight off the buoyancy component
    /// (which reads the synced transform) on every peer.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkShip : NetworkBehaviour
    {
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private ShipSailSystem sailSystem;

        [Header("Collider safety")]
        [SerializeField] private bool disableNonConvexVisualMeshColliders = true;

        [Header("Ocean time correction (proxies)")]
        [SerializeField] private float oceanTimeCorrectionThreshold = 0.1f;
        [SerializeField] private float oceanTimeCorrectionGain = 0.1f;

        // Authority-only derived state. Everything geometric lives on NetworkRigidbody3D.
        [Networked] public float LastImpactStrength { get; set; }
        [Networked] public float OceanTime { get; set; }

        private Rigidbody _rb;
        private bool _hasNetworkRigidbody;

        /// <summary>Latest wave-impact strength, valid on every peer (deck FX / camera shake).</summary>
        public float NetworkedImpactStrength => LastImpactStrength;

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();
            if (buoyancy == null)
                buoyancy = GetComponent<ShipBuoyancyController>();
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (sailSystem == null)
                sailSystem = GetComponent<ShipSailSystem>();
            if (sailSystem == null)
                sailSystem = gameObject.AddComponent<ShipSailSystem>();

            if (sailSystem != null && sailSystem.WindSystem == null)
                sailSystem.WindSystem = FindAnyObjectByType<OceanWindSystem>();

            // Cosmetic bow/wake foam on every peer (derives speed from the synced transform).
            if (GetComponent<ShipWakeFoam>() == null)
                gameObject.AddComponent<ShipWakeFoam>();

            SanitizeVisualColliders();

            // The Physics addon must own the transform; without it the ship won't replicate.
            _hasNetworkRigidbody = TryGetComponent(out Fusion.Addons.Physics.NetworkRigidbody3D _);
            if (!_hasNetworkRigidbody)
            {
                Debug.LogError($"[NetworkShip] '{name}' has no NetworkRigidbody3D — the ship transform " +
                               "will NOT replicate. Add the Fusion Physics addon's NetworkRigidbody3D to the " +
                               "ship prefab and re-bake the NetworkObject (see SETUP_MULTIPLAYER.md).");
            }

            if (buoyancy != null)
            {
                buoyancy.ConfigureReferences(waveField, _rb);
                if (HasStateAuthority)
                {
                    // Drive buoyancy from FixedUpdateNetwork (inside Fusion's physics tick), not from
                    // Unity's FixedUpdate — deterministic timing with the Physics addon.
                    buoyancy.SetExternallyDriven(true);
                    buoyancy.enabled = true;
                }
                else
                {
                    // Proxies are kinematic (driven by NetworkRigidbody3D) and ride the snapshot.
                    buoyancy.enabled = false;
                }
            }

            if (HasStateAuthority && waveField != null)
                OceanTime = waveField.OceanTimeNow;
        }

        public override void FixedUpdateNetwork()
        {
            if (HasStateAuthority)
            {
                if (waveField != null)
                    OceanTime = waveField.OceanTimeNow;

                // Step buoyancy inside the network tick so PhysX (stepped by the Fusion addon)
                // sees the forces on the same tick and resimulation stays deterministic.
                if (buoyancy != null && waveField != null)
                {
                    buoyancy.Step(Runner.DeltaTime, waveField.OceanTimeNow);
                    LastImpactStrength = buoyancy.LastImpactStrength;
                }

                return;
            }

            // Proxy: buoyancy stays off; nudge the local ocean clock toward the authority's.
            if (buoyancy != null && buoyancy.enabled)
                buoyancy.enabled = false;

            if (waveField != null)
            {
                float error = OceanTime - waveField.OceanTimeNow;
                if (Mathf.Abs(error) > Mathf.Max(0.01f, oceanTimeCorrectionThreshold))
                    waveField.ApplyTimeCorrection(error * Mathf.Clamp01(oceanTimeCorrectionGain));
            }
        }

        private void SanitizeVisualColliders()
        {
            if (!disableNonConvexVisualMeshColliders || _rb == null)
                return;

            // The hull is a DYNAMIC rigidbody, and PhysX rejects non-convex MeshColliders on dynamic
            // bodies (they collide with nothing → the crew falls through the deck). So convert the
            // walkable/structural mesh colliders to CONVEX (valid on a dynamic body, and a per-part
            // convex hull of a deck/stair/hull piece is still solid to stand and climb on), and just
            // disable decorative meshes (sails/rigging/flags/masts — masts are climbed via their own
            // capsule colliders, not the mesh).
            int convexified = 0;
            int disabled = 0;
            MeshCollider[] colliders = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider meshCollider = colliders[i];
                if (meshCollider == null || meshCollider.convex)
                    continue;

                if (IsDecorativeColliderName(meshCollider.name))
                {
                    if (meshCollider.enabled)
                    {
                        meshCollider.enabled = false;
                        disabled++;
                    }
                    continue;
                }

                meshCollider.convex = true;
                if (!meshCollider.enabled)
                    meshCollider.enabled = true;
                convexified++;
            }

            if (convexified > 0 || disabled > 0)
                Debug.Log($"[NetworkShip] Collision sanitized on '{name}': {convexified} mesh colliders → convex (walkable/hull), {disabled} decorative disabled.");
        }

        // Meshes that should NOT be walkable collision on the ship (rigging/cloth/masts).
        private static bool IsDecorativeColliderName(string n)
        {
            return n.Contains("Sail") || n.Contains("Wire") || n.Contains("Rope")
                   || n.Contains("Flag") || n.Contains("Cloth") || n.Contains("Mast")
                   || n.Contains("Rigging");
        }
    }
}
#endif
