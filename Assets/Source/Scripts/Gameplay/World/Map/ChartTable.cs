#if FUSION2
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.World.Building;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Map
{
    /// <summary>
    /// Chart table on the ship's deck: the voyage chart lies on it (live texture from
    /// <see cref="NetworkWorld.Map"/>); E opens the close-up (<see cref="ChartViewer"/>, local only).
    /// Built at runtime by <see cref="Attach"/> so the ship prefab stays untouched.
    /// </summary>
    public sealed class ChartTable : Interactable
    {
        private const float TopHeight = 0.9f;
        private const float TopX = 1.15f;   // across the ship
        private const float TopZ = 0.85f;   // along the ship

        // Ship-local deck spots tried in order: aft of the main mast, beside the helm path, then midships.
        private static readonly Vector2[] Spots =
        {
            new(1.2f, -5.5f), new(-1.2f, -5.5f), new(1.25f, -1.0f), new(-1.25f, -1.0f), new(0f, -5.5f), new(0f, 1.2f),
        };

        public NetworkWorld World;
        private Renderer _chart;
        private Texture _applied;
        private Material _material;
        private Transform _marker;
        private Renderer _markerRenderer;
        private Material _markerMaterial;
        private NetworkShip _ship;

        /// <summary>The little ship on the table's chart (null for tables built before it existed).</summary>
        public Transform Marker => _marker;

        public override InteractionKind Kind => InteractionKind.Generic;

        public override string GetPrompt(in InteractorInfo who) =>
            World != null && World.Map != null ? "Рассмотреть карту" : "Карта ещё не готова";

        public override bool IsAvailable(in InteractorInfo who) => World != null && World.Map != null;

        private void LateUpdate()
        {
            Texture tex = World != null && World.Map != null ? World.Map.Texture : null;
            UpdateMarker(tex);
            if (tex == _applied || _chart == null)
                return;
            _applied = tex;
            if (_material.HasProperty("_BaseMap"))
                _material.SetTexture("_BaseMap", tex);
            _material.mainTexture = tex;
            _chart.enabled = tex != null;
            if (tex != null)
            {
                // Fit the chart's aspect inside the table top.
                float aspect = tex.width / (float)Mathf.Max(1, tex.height);
                float w = TopX - 0.1f, h = TopZ - 0.1f;
                if (w / h > aspect) w = h * aspect;
                else h = w / aspect;
                _chart.transform.localScale = new Vector3(w, h, 1f);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
                WorldGeometryBuilder.DestroySafe(_material);
            if (_markerMaterial != null)
                WorldGeometryBuilder.DestroySafe(_markerMaterial);
        }

        // The chart quad lies with texture-up along table +Z: put the marker at the ship's UV inside it.
        private void UpdateMarker(Texture tex)
        {
            if (_marker == null || _chart == null)
                return;
            if (_ship == null)
                _ship = GetComponentInParent<NetworkShip>();
            bool show = tex != null && _ship != null && World != null && World.Map != null;
            if (_markerRenderer.enabled != show)
                _markerRenderer.enabled = show;
            if (!show)
                return;
            Vector3 pos = _ship.transform.position;
            Vector2 uv = World.Map.WorldToUv(new Vector2(pos.x, pos.z));
            uv.x = Mathf.Clamp01(uv.x);
            uv.y = Mathf.Clamp01(uv.y);
            Vector3 scale = _chart.transform.localScale;
            Vector3 c = _chart.transform.localPosition;
            _marker.localPosition = new Vector3(c.x + (uv.x - 0.5f) * scale.x, c.y + 0.004f, c.z + (uv.y - 0.5f) * scale.y);
            Vector3 fwd = _ship.transform.forward;
            float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            _marker.localRotation = Quaternion.Euler(0f, heading, 0f);
        }

        private static Mesh _markerMesh;

        // Flat top-down hull (bow +Z), ~1 unit long; scaled down on the table.
        private static Mesh MarkerMesh()
        {
            if (_markerMesh != null)
                return _markerMesh;
            var v = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0.5f), new Vector3(0.22f, 0f, 0.18f), new Vector3(0.22f, 0f, -0.3f),
                new Vector3(0.13f, 0f, -0.48f), new Vector3(-0.13f, 0f, -0.48f), new Vector3(-0.22f, 0f, -0.3f),
                new Vector3(-0.22f, 0f, 0.18f),
            };
            var tris = new int[(v.Length - 1) * 3];
            for (int i = 1; i < v.Length; i++)
            {
                int j = i == v.Length - 1 ? 1 : i + 1;
                tris[(i - 1) * 3] = 0;
                tris[(i - 1) * 3 + 1] = i;
                tris[(i - 1) * 3 + 2] = j;
            }
            _markerMesh = new Mesh { name = "ChartShipMarker", vertices = v, triangles = tris, hideFlags = HideFlags.DontSave };
            _markerMesh.RecalculateNormals();
            _markerMesh.RecalculateBounds();
            return _markerMesh;
        }

        /// <summary>Builds a chart table on the ship's deck (once per ship; identical on every peer).</summary>
        public static ChartTable Attach(NetworkShip ship, NetworkWorld world, WorldGenConfig cfg)
        {
            if (ship == null)
                return null;
            ChartTable existing = ship.GetComponentInChildren<ChartTable>(true);
            if (existing != null)
            {
                existing.World = world;
                return existing;
            }

            Transform root = ship.transform;
            if (!FindSpot(ship, out Vector3 deckLocal, out int layer))
            {
                Debug.LogWarning("[World] No free deck spot for the chart table — using midships.");
                deckLocal = new Vector3(0f, 1.3f, 1.2f);
                layer = ship.gameObject.layer;
            }

            Rigidbody rb = ship.Body != null ? ship.Body : ship.GetComponent<Rigidbody>();
            Vector3 inertia = rb != null ? rb.inertiaTensor : Vector3.zero;
            Quaternion inertiaRot = rb != null ? rb.inertiaTensorRotation : Quaternion.identity;
            Vector3 com = rb != null ? rb.centerOfMass : Vector3.zero;

            var go = new GameObject("ChartTable") { layer = layer };
            go.SetActive(false); // bind colliders on enable, after they exist
            go.transform.SetParent(root, false);
            go.transform.localPosition = deckLocal;
            go.transform.localRotation = Quaternion.identity;

            Material wood = cfg != null && cfg.WoodMaterial != null ? cfg.WoodMaterial : WorldGeometryBuilder.FallbackMaterial(new Color(0.42f, 0.28f, 0.16f));
            Box(go.transform, "Top", wood, new Vector3(0f, TopHeight - 0.03f, 0f), new Vector3(TopX, 0.06f, TopZ), layer);
            Box(go.transform, "Rim", wood, new Vector3(0f, TopHeight - 0.12f, 0f), new Vector3(TopX - 0.08f, 0.12f, TopZ - 0.08f), layer);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(go.transform, $"Leg_{sx}{sz}", wood, new Vector3(sx * (TopX * 0.5f - 0.08f), (TopHeight - 0.06f) * 0.5f, sz * (TopZ * 0.5f - 0.08f)),
                        new Vector3(0.08f, TopHeight - 0.06f, 0.08f), layer);

            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, TopHeight * 0.5f, 0f);
            box.size = new Vector3(TopX, TopHeight, TopZ);

            var chartGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            chartGo.name = "Chart";
            chartGo.layer = layer;
            WorldGeometryBuilder.DestroySafe(chartGo.GetComponent<Collider>());
            chartGo.transform.SetParent(go.transform, false);
            chartGo.transform.localPosition = new Vector3(0f, TopHeight + 0.004f, 0f);
            // Quad faces -Z; lay it flat facing up with chart north along ship-forward.
            chartGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            chartGo.transform.localScale = new Vector3(TopX - 0.1f, TopZ - 0.1f, 1f);

            Material chartMat = cfg != null && cfg.ChartMaterial != null
                ? new Material(cfg.ChartMaterial)
                : WorldGeometryBuilder.UnlitMaterial(Color.white);
            chartMat.name = "ChartTable_Chart";
            var mr = chartGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = chartMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false;

            var table = go.AddComponent<ChartTable>();
            table.World = world;
            table._chart = mr;
            table._material = chartMat;

            var markerGo = new GameObject("ShipMarker") { layer = layer };
            markerGo.transform.SetParent(go.transform, false);
            markerGo.transform.localPosition = chartGo.transform.localPosition;
            markerGo.transform.localScale = Vector3.one * 0.07f;
            markerGo.AddComponent<MeshFilter>().sharedMesh = MarkerMesh();
            var markerMr = markerGo.AddComponent<MeshRenderer>();
            table._markerMaterial = WorldGeometryBuilder.UnlitMaterial(new Color(0.6f, 0.1f, 0.06f));
            table._markerMaterial.name = "ChartTable_ShipMarker";
            markerMr.sharedMaterial = table._markerMaterial;
            markerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerMr.enabled = false;
            table._marker = markerGo.transform;
            table._markerRenderer = markerMr;
            table._ship = ship;
            table.Prompt = "Рассмотреть карту";
            table.MaxDistance = 2.6f;
            table.SetColliders(new Collider[] { box });

            // Last child of the ship: earlier interactables keep their InteractableIndex slots.
            go.transform.SetAsLastSibling();
            go.SetActive(true);
            if (ship.Object != null)
                InteractableIndex.Forget(ship.Object);

            if (rb != null)
            {
                rb.centerOfMass = com;
                rb.inertiaTensorRotation = inertiaRot;
                rb.inertiaTensor = inertia;
            }
            return table;
        }

        private static void Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 size, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            WorldGeometryBuilder.DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static bool FindSpot(NetworkShip ship, out Vector3 deckLocal, out int layer)
        {
            Transform t = ship.transform;
            Physics.SyncTransforms();
            PhysicsScene physics = ship.gameObject.scene.GetPhysicsScene();
            var hits = new RaycastHit[16];
            var overlaps = new Collider[16];
            foreach (Vector2 s in Spots)
            {
                Vector3 from = t.TransformPoint(new Vector3(s.x, 3.4f, s.y));
                int n = physics.Raycast(from, -t.up, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                RaycastHit deck = default;
                for (int i = 0; i < n; i++)
                {
                    if (hits[i].collider.GetComponentInParent<NetworkShip>() != ship || hits[i].distance >= best)
                        continue;
                    best = hits[i].distance;
                    deck = hits[i];
                }
                if (best == float.MaxValue || Vector3.Dot(deck.normal, t.up) < 0.85f)
                    continue;

                Vector3 local = t.InverseTransformPoint(deck.point);
                if (local.y < 0.6f || local.y > 2.2f)
                    continue;

                // Clearance: nothing of the ship (masts, guns, hatch, helm) inside the table volume.
                Vector3 center = t.TransformPoint(local + new Vector3(0f, TopHeight * 0.5f + 0.08f, 0f));
                Vector3 half = new Vector3(TopX * 0.5f + 0.15f, TopHeight * 0.5f, TopZ * 0.5f + 0.15f);
                int m = physics.OverlapBox(center, half, overlaps, t.rotation, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < m; i++)
                    if (overlaps[i].GetComponentInParent<NetworkShip>() == ship && overlaps[i] != deck.collider)
                    {
                        blocked = true;
                        break;
                    }
                if (blocked)
                    continue;

                deckLocal = local;
                layer = deck.collider.gameObject.layer;
                return true;
            }
            deckLocal = default;
            layer = 0;
            return false;
        }
    }
}
#endif
