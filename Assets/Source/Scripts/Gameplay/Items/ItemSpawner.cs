#if FUSION2
using System;
using System.Collections.Generic;
using Fusion;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Host: once the session runs (and the ship is spawned, if any placement needs it), spawns the
    /// scene's items from the <see cref="ItemCatalog"/>:
    ///   • <see cref="Where.Deck"/> — from the centreline at the first crew spawn (its station + deck height), in ship
    ///     axes (x starboard, z bow);
    ///     <see cref="Placement.AnchorToShip"/> fixes it to the hull with a joint (chests);
    ///   • <see cref="Where.Water"/> — next to the ship, at its waterline (floating loot);
    ///   • <see cref="Where.Here"/> — relative to this transform (no ship needed).
    /// Items are ordinary networked objects afterwards; this only places them once.
    /// </summary>
    public sealed class ItemSpawner : MonoBehaviour
    {
        public enum Where : byte { Deck, Water, Here }

        [Serializable]
        public struct Placement
        {
            public string ItemId;
            public Where Where;
            public Vector3 Offset;
            public float Yaw;
            public bool AnchorToShip;
        }

        [SerializeField] private ItemCatalog catalog;
        [SerializeField] private List<Placement> placements = new();
        [Tooltip("Spawn the 'Here' items anyway after this long without a ship.")]
        [SerializeField] private float shipTimeout = 20f;

        private bool _done;
        private float _startedAt = -1f;

        public ItemCatalog Catalog => catalog;
        public bool Done => _done;

        private void Update()
        {
            if (_done || catalog == null) return;
            NetworkRunner runner = HostRunner();
            if (runner == null) return;
            if (_startedAt < 0f) _startedAt = Time.realtimeSinceStartup;

            NetworkShip ship = null;
            foreach (NetworkShip s in NetworkShip.All)
                if (s != null && s.Object != null && s.Object.IsValid) { ship = s; break; }

            bool needShip = placements.Exists(p => p.Where != Where.Here);
            if (needShip && ship == null && Time.realtimeSinceStartup - _startedAt < shipTimeout) return;

            _done = true;
            foreach (Placement p in placements)
                Spawn(runner, ship, p);
        }

        private void Spawn(NetworkRunner runner, NetworkShip ship, Placement p)
        {
            GameObject prefab = catalog.Get(p.ItemId);
            if (prefab == null)
            {
                Debug.LogWarning($"[ItemSpawner] Unknown item '{p.ItemId}'.", this);
                return;
            }

            Vector3 pos;
            Quaternion rot;
            switch (p.Where)
            {
                case Where.Deck when ship != null && ship.TryGetCrewSpawn(0, out Vector3 deck, out _):
                    // From the centreline at the first crew spawn's station and deck height: the spawn itself
                    // sits by the rail (port), so offsets measured from it put "x < 0" overboard.
                    Vector3 local = ship.transform.InverseTransformPoint(deck);
                    deck = ship.transform.TransformPoint(new Vector3(0f, local.y, local.z));
                    rot = Quaternion.Euler(0f, ship.transform.eulerAngles.y, 0f);
                    pos = deck + rot * p.Offset;
                    break;
                case Where.Water when ship != null:
                    rot = Quaternion.Euler(0f, ship.transform.eulerAngles.y, 0f);
                    pos = new Vector3(ship.transform.position.x, ship.transform.position.y, ship.transform.position.z) + rot * p.Offset;
                    break;
                default:
                    rot = transform.rotation;
                    pos = transform.TransformPoint(p.Offset);
                    break;
            }
            rot *= Quaternion.Euler(0f, p.Yaw, 0f);

            NetworkObject obj = runner.Spawn(prefab, pos, rot);
            if (obj == null) return;

            if (p.AnchorToShip && ship != null && ship.Body != null)
            {
                var joint = obj.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = ship.Body;
            }
        }

        private static NetworkRunner HostRunner()
        {
            foreach (NetworkRunner r in NetworkRunner.Instances)
                if (r != null && r.IsRunning && r.IsServer) return r;
            return null;
        }
    }
}
#endif
