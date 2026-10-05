using System.Collections.Generic;
using RumOverboard.Gameplay.Interaction;
using RumOverboard.Gameplay.Ocean;
using RumOverboard.Gameplay.Ocean.Features.Masts;
using Source.Scripts.Editor;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// Builds the network ship as a flush-decked brig FROM PARTS (no Stylized_Pirate_Ship asset):
    ///
    ///   HullModule   — procedural hull + deck meshes (ProceduralBrigShipMeshBuilder shapes), and
    ///                  colliders that follow the VISIBLE surfaces exactly: deck segments along the
    ///                  real sheer/camber curve, bulwark segments along the real sheer line (the same
    ///                  boxes render and collide), hull volume, bowsprit, rudder, guns, hatch.
    ///   ForeMast / MainMast modules — ShipMastModule + ShipMast: lower mast, top (platform),
    ///                  topmast, yards, cloth course + topsail on SailAnchors, shrouds with ratlines
    ///                  that are climbable (ClimbSurface) up to the top.
    ///   HelmModule   — wheel with spokes on a stand at the raised stern, helm zone, stand anchor.
    ///
    /// Ship space: waterline ≈ y 0 (buoyancy points are placed for that), bow +Z.
    /// The deck rises gently toward bow and stern (sheer), so the helm is reached on foot.
    /// </summary>
    public static class BrigShipBuilder
    {
        private const string HullMeshPath = "Assets/Source/Environment/Meshes/Brig/BrigHull.asset";
        private const string DeckMeshPath = "Assets/Source/Environment/Meshes/Brig/BrigDeck.asset";
        private const string BoxMeshPath = "Assets/Source/Environment/Meshes/Brig/UnitBox.asset";
        private const string CylinderMeshPath = "Assets/Source/Environment/Meshes/Brig/UnitCylinder.asset";

        // Hull frame (must match ProceduralBrigShipMeshBuilder).
        private const float SternZ = -9.4f;
        private const float BowZ = 10.9f;
        private const float DeckInset = 0.8f;   // hull top edge = 0.8 × half-beam (tumblehome)
        private const float BulwarkHeight = 0.95f;
        private const float BulwarkThickness = 0.14f;

        private struct MastSpec
        {
            public string Module;   // module object name (also drives Russian labels in the setup tool)
            public string Prefix;   // sail visual-name prefix
            public float Z;
            public float TopY;      // platform top
            public float LowerRadius;
        }

        private static readonly MastSpec[] Masts =
        {
            new MastSpec { Module = "ForeMastModule", Prefix = "Fore", Z = 2.0f, TopY = 7.9f, LowerRadius = 0.21f },
            new MastSpec { Module = "MainMastModule", Prefix = "Main", Z = -3.9f, TopY = 8.3f, LowerRadius = 0.23f },
        };

        private static Mesh _box, _cylinder;
        private static Material _wood, _darkWood, _hull, _rigging, _metal, _sail;

        // ---- Hull curves (copied from ProceduralBrigShipMeshBuilder so colliders match the mesh) ----
        public static float HalfBeam(float t)
        {
            float centered = Mathf.Clamp01(1f - Mathf.Pow((t - 0.48f) / 0.52f, 2f));
            float beam = 0.68f + 1.95f * centered;
            float sternTaper = Mathf.Lerp(0.58f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.24f, t)));
            float bowTaper = Mathf.Lerp(1f, 0.42f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 1f, t)));
            return beam * sternTaper * bowTaper;
        }

        public static float Sheer(float t)
        {
            float sternRise = Mathf.Lerp(0.55f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.32f, t)));
            float bowRise = Mathf.Lerp(0f, 0.66f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, t)));
            return 1.05f + sternRise + bowRise;
        }

        public static float T(float z) => Mathf.InverseLerp(SternZ, BowZ, z);

        /// <summary>Hull top edge (x) at z — where the bulwark stands.</summary>
        public static float EdgeX(float z)
        {
            float t = T(z);
            float bowFlare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, t));
            return HalfBeam(t) * DeckInset * (1f + bowFlare * 0.08f);
        }

        /// <summary>Walkable deck height (centre line, incl. camber) at z.</summary>
        public static float DeckY(float z) => Sheer(T(z)) - 0.03f + 0.08f;

        // =========================================================================================
        public static void BuildInto(GameObject root)
        {
            LoadAssets();

            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject, true);
#pragma warning disable CS0618
            foreach (ShipSailSystem legacy in root.GetComponents<ShipSailSystem>())
                Object.DestroyImmediate(legacy, true);
#pragma warning restore CS0618

            BuildHull(root.transform);
            var mastModules = new List<ShipMast>();
            foreach (MastSpec spec in Masts)
                mastModules.Add(BuildMast(root.transform, spec));
            BuildHelm(root);
            ConfigureBody(root);
            ConfigureBuoyancy(root);

            var sails = root.GetComponent<ShipSailsAggregator>();
            if (sails != null) sails.RefreshMasts();
            Debug.Log($"[BrigShipBuilder] Built brig from parts: hull, {mastModules.Count} masts, helm.");
        }

        private static void LoadAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(HullMeshPath) == null || AssetDatabase.LoadAssetAtPath<Mesh>(DeckMeshPath) == null)
            {
                // Generate the procedural brig meshes once (temporary object; we only keep the assets).
                var temp = new GameObject("TempBrig");
                ProceduralBrigShipMeshBuilder.BuildShipMesh(temp.transform, null, null, null, null, null, true);
                Object.DestroyImmediate(temp);
            }
            _box = AssetDatabase.LoadAssetAtPath<Mesh>(BoxMeshPath);
            _cylinder = AssetDatabase.LoadAssetAtPath<Mesh>(CylinderMeshPath);
            _wood = Load("Assets/Source/Environment/Materials/Wood.mat") ?? SandboxMaterials.Wood();
            _hull = SandboxMaterials.HullPaint(); // tarred underbody (URP Lit)
            _rigging = Load("Assets/Source/Environment/Materials/Rigging.mat") ?? SandboxMaterials.Rope();
            _metal = Load("Assets/Source/Environment/Materials/Metal.mat") ?? SandboxMaterials.Brass();
            _sail = Load("Assets/Source/Environment/Materials/Sail.mat");
            _darkWood = SandboxMaterials.DarkWood();
        }

        private static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        // =========================================================================================
        // Hull: visuals + colliders that match them
        // =========================================================================================
        private static void BuildHull(Transform root)
        {
            Transform module = Child(root, "HullModule");
            Transform visual = Child(module, "Visual");
            Transform colliders = Child(module, "Colliders");

            MeshPart(visual, "Hull", AssetDatabase.LoadAssetAtPath<Mesh>(HullMeshPath), _hull, Vector3.zero, Vector3.one, Quaternion.identity);
            // The procedural deck is 0.93 × beam; scale it in to sit inside the hull's top edge.
            MeshPart(visual, "Deck", AssetDatabase.LoadAssetAtPath<Mesh>(DeckMeshPath), _wood, Vector3.zero,
                new Vector3(DeckInset / 0.93f * 1.01f, 1f, 1f), Quaternion.identity);

            // --- Deck collider: segments following sheer (pitch) and beam ------------------------
            const float seg = 0.7f;
            for (float z0 = SternZ + 0.35f; z0 < BowZ - 0.6f; z0 += seg)
            {
                float z1 = Mathf.Min(z0 + seg, BowZ - 0.6f);
                float zm = (z0 + z1) * 0.5f;
                float y0 = DeckY(z0), y1 = DeckY(z1);
                float width = 2f * Mathf.Min(EdgeX(z0), EdgeX(z1)) - 0.02f;
                if (width < 0.4f) continue;
                Vector3 a = new Vector3(0f, y0, z0), b = new Vector3(0f, y1, z1);
                Quaternion pitch = Quaternion.LookRotation(b - a, Vector3.up);
                var go = new GameObject($"DeckCol_{zm:F1}");
                go.transform.SetParent(colliders, false);
                go.transform.localRotation = pitch;
                go.transform.localPosition = (a + b) * 0.5f + pitch * new Vector3(0f, -0.15f, 0f);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(width, 0.3f, Vector3.Distance(a, b) + 0.06f);
            }

            // --- Bulwarks: render + collide, along the real hull edge -------------------------------
            const float bseg = 0.6f;
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z0 = SternZ + 0.15f; z0 < BowZ - 0.25f; z0 += bseg)
                {
                    float z1 = Mathf.Min(z0 + bseg, BowZ - 0.25f);
                    Vector3 a = new Vector3(side * EdgeX(z0), Sheer(T(z0)), z0);
                    Vector3 b = new Vector3(side * EdgeX(z1), Sheer(T(z1)), z1);
                    Vector3 dir = b - a;
                    Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    var plank = MeshPart(colliders, $"Bulwark_{(side < 0 ? "P" : "S")}_{z0:F1}", _box, _wood,
                        (a + b) * 0.5f + Vector3.up * (BulwarkHeight * 0.5f - 0.05f),
                        new Vector3(BulwarkThickness, BulwarkHeight + 0.1f, dir.magnitude + 0.05f), rot);
                    plank.AddComponent<BoxCollider>();
                    // Cap rail (visual).
                    MeshPart(visual, $"CapRail_{(side < 0 ? "P" : "S")}_{z0:F1}", _box, _darkWood,
                        (a + b) * 0.5f + Vector3.up * BulwarkHeight + rot * new Vector3(-side * 0.02f, 0f, 0f),
                        new Vector3(BulwarkThickness + 0.1f, 0.07f, dir.magnitude + 0.06f), rot);
                }
            }
            // Bow and stern closures.
            float bowZ = BowZ - 0.25f;
            MeshPart(colliders, "Bulwark_Bow", _box, _wood, new Vector3(0f, Sheer(T(bowZ)) + BulwarkHeight * 0.5f, bowZ),
                new Vector3(2f * EdgeX(bowZ) + 0.1f, BulwarkHeight + 0.1f, BulwarkThickness), Quaternion.identity).AddComponent<BoxCollider>();
            float sternZ = SternZ + 0.15f;
            MeshPart(colliders, "Transom", _box, _hull, new Vector3(0f, Sheer(T(sternZ)) + BulwarkHeight * 0.5f - 0.3f, sternZ),
                new Vector3(2f * EdgeX(sternZ) + 0.12f, BulwarkHeight + 0.7f, BulwarkThickness + 0.04f), Quaternion.identity).AddComponent<BoxCollider>();

            // --- Hull volume below deck (swimmers, debris) — tapered boxes -----------------------
            foreach ((float z0, float z1) in new[] { (-8.6f, -4f), (-4f, 4f), (4f, 9.6f) })
            {
                float zm = (z0 + z1) * 0.5f;
                var go = new GameObject($"HullVolume_{zm:F0}");
                go.transform.SetParent(colliders, false);
                float top = Mathf.Min(DeckY(z0), DeckY(z1)) - 0.35f;
                go.transform.localPosition = new Vector3(0f, (top - 1.6f) * 0.5f, zm);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(2f * Mathf.Min(EdgeX(z0), EdgeX(z1)), top + 1.6f, z1 - z0);
            }

            // --- Fittings ------------------------------------------------------------------------
            Line(visual, "Bowsprit", _wood, new Vector3(0f, DeckY(9.8f) + 0.35f, 9.8f), new Vector3(0f, 3.4f, 14.6f), 0.15f);
            MeshPart(visual, "Rudder", _hull, new Vector3(0f, -0.9f, -9.65f), new Vector3(0.18f, 2.2f, 0.7f), Quaternion.Euler(6f, 0f, 0f), _box);
            MeshPart(colliders, "Hatch", _darkWood, new Vector3(0f, DeckY(-0.8f) + 0.1f, -0.8f), new Vector3(1.3f, 0.2f, 1.3f),
                Quaternion.identity, _box).AddComponent<BoxCollider>();
            foreach (float z in new[] { -1.8f, 0.6f, 4.4f })
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * (EdgeX(z) - 0.55f);
                    var carriage = MeshPart(colliders, $"Gun_{(side < 0 ? "P" : "S")}_{z:F1}", _darkWood,
                        new Vector3(x, DeckY(z) + 0.15f, z), new Vector3(0.6f, 0.3f, 0.7f), Quaternion.identity, _box);
                    carriage.AddComponent<BoxCollider>();
                    Line(visual, $"Barrel_{(side < 0 ? "P" : "S")}_{z:F1}", _metal,
                        new Vector3(x - side * 0.1f, DeckY(z) + 0.42f, z), new Vector3(x + side * 0.75f, DeckY(z) + 0.45f, z), 0.13f);
                }
        }

        // =========================================================================================
        // Masts
        // =========================================================================================
        private static ShipMast BuildMast(Transform root, MastSpec spec)
        {
            Transform modules = Child(root, "MastModules");
            Transform module = Child(modules, spec.Module);
            Transform visual = Child(module, "Visual");
            Transform colliders = Child(module, "Colliders");
            Transform anchors = Child(module, "SailAnchors");
            Transform climb = Child(module, "Climb");

            float deck = DeckY(spec.Z);
            float z = spec.Z;
            float lowerTop = spec.TopY + 0.5f;
            float topmastTop = spec.TopY + 4.6f;

            // Spars (visual).
            Line(visual, "LowerMast", _wood, new Vector3(0f, deck - 0.2f, z), new Vector3(0f, lowerTop, z), spec.LowerRadius * 2f);
            Line(visual, "TopMast", _wood, new Vector3(0f, spec.TopY - 0.3f, z), new Vector3(0f, topmastTop, z), 0.26f);
            Line(visual, "TopGallantPole", _wood, new Vector3(0f, topmastTop - 0.4f, z), new Vector3(0f, topmastTop + 1.6f, z), 0.16f);
            float courseYardY = spec.TopY - 0.7f;
            float topsailYardY = topmastTop - 0.5f;
            Line(visual, "CourseYard", _wood, new Vector3(-3.2f, courseYardY, z + 0.25f), new Vector3(3.2f, courseYardY, z + 0.25f), 0.16f);
            Line(visual, "TopsailYard", _wood, new Vector3(-2.6f, topsailYardY, z + 0.18f), new Vector3(2.6f, topsailYardY, z + 0.18f), 0.13f);

            // Top (platform) — walkable, the exit of the climbs.
            var top = MeshPart(colliders, "Top", _darkWood, new Vector3(0f, spec.TopY - 0.06f, z), new Vector3(2.0f, 0.12f, 2.4f), Quaternion.identity, _box);
            top.AddComponent<BoxCollider>();
            foreach (float side in new[] { -1f, 1f })
                MeshPart(visual, $"TopRail_{side}", _darkWood, new Vector3(side * 1.0f, spec.TopY + 0.35f, z),
                    new Vector3(0.05f, 0.05f, 2.4f), Quaternion.identity, _box);

            // Trunk collider (view ray / climb rail detection).
            var trunkGo = new GameObject("Trunk");
            trunkGo.transform.SetParent(colliders, false);
            trunkGo.transform.localPosition = new Vector3(0f, (deck + topmastTop) * 0.5f, z);
            var trunk = trunkGo.AddComponent<CapsuleCollider>();
            trunk.direction = 1;
            trunk.radius = spec.LowerRadius;
            trunk.height = topmastTop - deck;

            // Anchors for the sail system / rigging generator.
            Anchor(anchors, "MastBase", new Vector3(0f, deck, z));
            Anchor(anchors, "MastTop", new Vector3(0f, topmastTop, z));
            var sails = new List<ShipMast.SailPanel>
            {
                Sail(anchors, $"{spec.Prefix}Course", z + 0.3f,
                    new Vector2(3.0f, courseYardY - 0.1f), new Vector2(3.3f, deck + 2.4f), 8, 6),
                Sail(anchors, $"{spec.Prefix}Topsail", z + 0.22f,
                    new Vector2(2.45f, topsailYardY - 0.1f), new Vector2(2.9f, spec.TopY + 0.4f), 8, 6),
            };

            // Shrouds + ratlines each side, climbable to the top.
            foreach (float side in new[] { -1f, 1f })
                BuildShrouds(visual, climb, spec, side, deck);

            var shipMast = module.gameObject.AddComponent<ShipMast>();
            shipMast.SetMast(new ShipMast.MastDefinition
            {
                name = spec.Prefix,
                baseLocal = new Vector3(0f, deck, z),
                topLocal = new Vector3(0f, topmastTop, z),
                radius = spec.LowerRadius,
            });
            shipMast.SetSails(sails.ToArray());
            var so = new SerializedObject(shipMast);
            so.FindProperty("standalone").boolValue = false;
            if (_sail != null) so.FindProperty("sailMaterial").objectReferenceValue = _sail;
            so.FindProperty("drawDebugGizmos").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            var mastModule = module.gameObject.AddComponent<ShipMastModule>();
            mastModule.Configure(spec.Module, visual, new Transform[0], anchors, climb, null);
            return shipMast;
        }

        private static ShipMast.SailPanel Sail(Transform anchors, string name, float z, Vector2 topCorner, Vector2 bottomCorner, int w, int h)
        {
            Vector3 tl = new Vector3(-topCorner.x, topCorner.y, z);
            Vector3 tr = new Vector3(topCorner.x, topCorner.y, z);
            Vector3 bl = new Vector3(-bottomCorner.x, bottomCorner.y, z);
            Vector3 br = new Vector3(bottomCorner.x, bottomCorner.y, z);
            Anchor(anchors, $"{name}_TopLeft", tl);
            Anchor(anchors, $"{name}_TopRight", tr);
            Anchor(anchors, $"{name}_BottomLeft", bl);
            Anchor(anchors, $"{name}_BottomRight", br);
            Anchor(anchors, $"{name}_Center", (tl + tr + bl + br) * 0.25f);
            float area = (topCorner.x + bottomCorner.x) * (topCorner.y - bottomCorner.y);
            return new ShipMast.SailPanel
            {
                name = name,
                visualSailName = name,
                topLeftLocal = tl,
                topRightLocal = tr,
                bottomLeftLocal = bl,
                bottomRightLocal = br,
                gridWidth = w,
                gridHeight = h,
                baseArea = Mathf.Max(1f, area),
                hoist01 = 0f,
                extension01 = 1f,
                clothColor = new Color(0.94f, 0.91f, 0.84f, 0.97f),
            };
        }

        private static void BuildShrouds(Transform visual, Transform climb, MastSpec spec, float side, float deck)
        {
            const int lines = 4;
            float chainY = Sheer(T(spec.Z)) + BulwarkHeight;      // on the cap rail
            float topY = spec.TopY - 0.15f;
            Vector3 topCentre = new Vector3(side * 0.85f, topY, spec.Z);
            var bottoms = new List<Vector3>();
            for (int i = 0; i < lines; i++)
            {
                float dz = -0.75f + i * 0.4f;
                Vector3 bottom = new Vector3(side * (EdgeX(spec.Z + dz) + 0.06f), chainY, spec.Z + dz);
                bottoms.Add(bottom);
                Line(visual, $"Shroud_{side}_{i}", _rigging, bottom, topCentre + new Vector3(0f, 0f, dz * 0.25f), 0.045f);
            }
            // Ratlines: horizontal rungs across the shrouds.
            for (float y = chainY + 0.45f; y < topY - 0.2f; y += 0.42f)
            {
                float k = Mathf.InverseLerp(chainY, topY, y);
                Vector3 a = Vector3.Lerp(bottoms[0], topCentre + new Vector3(0f, 0f, -0.75f * 0.25f), k);
                Vector3 b = Vector3.Lerp(bottoms[lines - 1], topCentre + new Vector3(0f, 0f, 0.45f * 0.25f), k);
                Line(visual, $"Ratline_{side}_{y:F1}", _rigging, a, b, 0.025f);
            }

            // Climb rail along the shroud plane (climber on the inboard side, facing outboard).
            Vector3 bottomMid = (bottoms[1] + bottoms[2]) * 0.5f;
            Vector3 axis = (topCentre - bottomMid).normalized;
            Vector3 gripBottom = bottomMid + axis * ((deck + 1.15f + 0.05f - bottomMid.y) / Mathf.Max(0.1f, axis.y));
            Vector3 gripTop = topCentre + axis * 0.0f + Vector3.up * 1.0f;
            var go = new GameObject($"ShroudClimb_{(side < 0 ? "Port" : "Starboard")}");
            go.transform.SetParent(climb, false);
            go.transform.localPosition = Vector3.zero;

            var exit = new GameObject("TopExit").transform;
            exit.SetParent(go.transform, false);
            exit.localPosition = new Vector3(side * 0.5f, spec.TopY + 0.02f, spec.Z);

            // Look-at zone: a thin slab in the shroud plane.
            var zone = new GameObject("LookZone");
            zone.transform.SetParent(go.transform, false);
            zone.transform.localPosition = (bottomMid + topCentre) * 0.5f;
            zone.transform.localRotation = Quaternion.LookRotation(Vector3.forward, axis);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.35f, Vector3.Distance(bottomMid, topCentre) + 0.4f, 1.4f);

            var surface = go.AddComponent<ClimbSurface>();
            surface.Configure(gripBottom, gripTop, new Vector3(-side, 0f, 0f), 0.4f, 0.36f, exit, 0f);
            surface.Prompt = "Лезть по вантам";
            surface.MaxDistance = 2.4f;
            surface.SetColliders(new Collider[] { box });
        }

        // =========================================================================================
        // Helm
        // =========================================================================================
        private static void BuildHelm(GameObject root)
        {
            const float z = -7.1f;
            float deck = DeckY(z);
            Transform module = Child(root.transform, "HelmModule");
            var stand = MeshPart(module, "WheelStand", _darkWood, new Vector3(0f, deck + 0.5f, z - 0.05f),
                new Vector3(0.24f, 1.0f, 0.24f), Quaternion.identity, _box);
            stand.AddComponent<BoxCollider>();

            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(module, false);
            wheel.localPosition = new Vector3(0f, deck + 1.12f, z - 0.2f);
            wheel.localRotation = Quaternion.Euler(90f, 0f, 0f); // local Y = fore-aft axle
            // Rim: a ring of short segments (a solid disc read as a grey plate).
            const int rimSegments = 16;
            for (int i = 0; i < rimSegments; i++)
            {
                float a = i * Mathf.PI * 2f / rimSegments;
                MeshPart(wheel, $"Rim_{i}", _darkWood, new Vector3(Mathf.Cos(a) * 0.48f, 0f, Mathf.Sin(a) * 0.48f),
                    new Vector3(0.07f, 0.07f, 0.2f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), _box);
            }
            MeshPart(wheel, "Hub", _metal, Vector3.zero, new Vector3(0.18f, 0.08f, 0.18f), Quaternion.identity, _cylinder);
            for (int i = 0; i < 8; i++)
            {
                var spoke = MeshPart(wheel, $"Spoke_{i}", _wood, Vector3.zero, new Vector3(0.045f, 0.045f, 1.25f),
                    Quaternion.Euler(0f, i * 22.5f, 0f), _box);
                spoke.name = $"Spoke_{i}";
            }

            var anchor = new GameObject("StandAnchor").transform;
            anchor.SetParent(module, false);
            anchor.localPosition = new Vector3(0f, DeckY(z - 0.75f), z - 0.75f);

            var zoneGo = new GameObject("Helm_Zone");
            zoneGo.transform.SetParent(module, false);
            zoneGo.transform.localPosition = wheel.localPosition;
            var zone = zoneGo.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(1.4f, 1.4f, 0.6f);

            var helm = root.GetComponent<ShipHelm>();
            if (helm != null)
            {
                var so = new SerializedObject(helm);
                so.FindProperty("wheelModel").objectReferenceValue = wheel;
                so.FindProperty("wheelSpinAxis").vector3Value = Vector3.up;
                so.FindProperty("standAnchor").objectReferenceValue = anchor;
                so.FindProperty("autoFlipStandToAft").boolValue = false;
                so.FindProperty("rudderLocal").vector3Value = new Vector3(0f, -1.1f, -9.6f);
                so.ApplyModifiedPropertiesWithoutUndo();

                var station = zoneGo.AddComponent<HelmStation>();
                station.Configure(helm);
                station.Prompt = "Встать за штурвал";
                station.MaxDistance = 2.6f;
                station.SetColliders(new Collider[] { zone });
            }
        }

        // =========================================================================================
        // Body + buoyancy
        // =========================================================================================
        private static void ConfigureBody(GameObject root)
        {
            var rb = root.GetComponent<Rigidbody>();
            if (rb == null) return;
            rb.mass = 12000f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, -0.7f, 0.2f);
            rb.interpolation = RigidbodyInterpolation.None;
        }

        private static void ConfigureBuoyancy(GameObject root)
        {
            var buoyancy = root.GetComponent<ShipBuoyancyController>();
            if (buoyancy == null) return;
            var so = new SerializedObject(buoyancy);
            SerializedProperty points = so.FindProperty("points");
            float[] zs = { -6.8f, -2.8f, 1.6f, 6.2f };
            points.arraySize = zs.Length * 2;
            int k = 0;
            foreach (float z in zs)
                foreach (float side in new[] { -1f, 1f })
                {
                    SerializedProperty p = points.GetArrayElementAtIndex(k++);
                    p.FindPropertyRelative("name").stringValue = $"{(side < 0 ? "Port" : "Star")}_{z:F1}";
                    // Equilibrium (45% of 1.6 m) with radius 0.4 puts the waterline at ship y ≈ 0.
                    p.FindPropertyRelative("localPosition").vector3Value = new Vector3(side * EdgeX(z) * 0.6f, -0.32f, z);
                    p.FindPropertyRelative("radius").floatValue = 0.4f;
                    p.FindPropertyRelative("maxSubmersionDepth").floatValue = 1.6f;
                    p.FindPropertyRelative("buoyancy").floatValue = 1f;
                    p.FindPropertyRelative("damping").floatValue = 1f;
                    p.FindPropertyRelative("emitContacts").boolValue = z > 5f || z < -6f;
                }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // =========================================================================================
        private static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Anchor(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
        }

        private static GameObject MeshPart(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject MeshPart(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale, Quaternion rot, Mesh mesh) =>
            MeshPart(parent, name, mesh, mat, pos, scale, rot);

        /// <summary>A cylinder spar/rope from a to b (unit cylinder is 2 tall along Y, diameter 1).</summary>
        private static void Line(Transform parent, string name, Material mat, Vector3 a, Vector3 b, float diameter)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            MeshPart(parent, name, _cylinder, mat, (a + b) * 0.5f, new Vector3(diameter, d.magnitude * 0.5f, diameter),
                Quaternion.FromToRotation(Vector3.up, d.normalized));
        }
    }
}
