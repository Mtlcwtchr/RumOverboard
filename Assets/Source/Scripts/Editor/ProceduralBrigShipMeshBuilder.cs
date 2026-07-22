using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Source.Scripts.Editor
{
    internal static class ProceduralBrigShipMeshBuilder
    {
        private const string MeshRootFolder = "Assets/Source/Environment/Meshes";
        private const string ShipMeshFolder = "Assets/Source/Environment/Meshes/Brig";

        private sealed class MeshLibrary
        {
            public Mesh Hull;
            public Mesh Deck;
            public Mesh Box;
            public Mesh Cylinder;
            public Mesh SquareSail;
            public Mesh JibSail;
        }

        public static void BuildShipMesh(
            Transform ship,
            Material hull,
            Material wood,
            Material sail,
            Material rigging,
            Material metal,
            bool highDetail)
        {
            ClearChildren(ship);
            EnsureFolder(MeshRootFolder);
            EnsureFolder(ShipMeshFolder);

            MeshLibrary meshes = BuildOrUpdateMeshes(highDetail);

            BuildHullAndDeck(ship, meshes, hull, wood, metal);
            BuildRig(ship, meshes, wood);
            BuildSails(ship, meshes, sail);
            BuildRigging(ship, meshes, rigging, highDetail);
            BuildCrewStations(ship);

            if (highDetail)
                BuildHighDetail(ship, meshes, wood, metal, rigging);
        }

        private static MeshLibrary BuildOrUpdateMeshes(bool highDetail)
        {
            int hullLengthSegments = highDetail ? 64 : 36;
            int hullSectionSegments = highDetail ? 24 : 14;
            int deckLengthSegments = highDetail ? 56 : 30;
            int deckBeamSegments = highDetail ? 14 : 8;

            var meshes = new MeshLibrary
            {
                Hull = SaveOrUpdateMesh($"{ShipMeshFolder}/BrigHull.asset", GenerateHullMesh(hullLengthSegments, hullSectionSegments)),
                Deck = SaveOrUpdateMesh($"{ShipMeshFolder}/BrigDeck.asset", GenerateDeckMesh(deckLengthSegments, deckBeamSegments)),
                Box = SaveOrUpdateMesh($"{ShipMeshFolder}/UnitBox.asset", GenerateBoxMesh()),
                Cylinder = SaveOrUpdateMesh($"{ShipMeshFolder}/UnitCylinder.asset", GenerateCylinderMesh(14, 1, true)),
                SquareSail = SaveOrUpdateMesh($"{ShipMeshFolder}/SquareSail.asset", GenerateCurvedSailMesh(10, 12, 0.18f)),
                JibSail = SaveOrUpdateMesh($"{ShipMeshFolder}/JibSail.asset", GenerateJibSailMesh(8, 10, 0.16f)),
            };

            return meshes;
        }

        private static void BuildHullAndDeck(Transform ship, MeshLibrary meshes, Material hull, Material wood, Material metal)
        {
            CreateMeshPart(ship, "Hull", meshes.Hull, hull, Vector3.zero, Vector3.one, Vector3.zero, addMeshCollider: true);
            CreateMeshPart(ship, "MainDeck", meshes.Deck, wood, Vector3.zero, Vector3.one, Vector3.zero, addMeshCollider: true);

            CreateBoxPart(ship, meshes.Box, "ForecastleDeck", wood, new Vector3(0f, 1.62f, 4.8f), new Vector3(3.9f, 0.24f, 4.5f), Vector3.zero, addBoxCollider: true);
            CreateBoxPart(ship, meshes.Box, "QuarterDeck", wood, new Vector3(0f, 1.90f, -5.2f), new Vector3(3.5f, 0.24f, 5.8f), Vector3.zero, addBoxCollider: true);
            CreateBoxPart(ship, meshes.Box, "PoopDeck", wood, new Vector3(0f, 2.32f, -7.5f), new Vector3(2.7f, 0.22f, 2.7f), Vector3.zero, addBoxCollider: true);

            CreateBoxPart(ship, meshes.Box, "PortBulwark", wood, new Vector3(-2.48f, 1.06f, -0.4f), new Vector3(0.28f, 0.82f, 14.5f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "StarboardBulwark", wood, new Vector3(2.48f, 1.06f, -0.4f), new Vector3(0.28f, 0.82f, 14.5f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "SternTransom", wood, new Vector3(0f, 1.58f, -9.25f), new Vector3(3.1f, 1.25f, 0.28f), Vector3.zero);

            CreateBoxPart(ship, meshes.Box, "AftCabin", wood, new Vector3(0f, 2.18f, -6.5f), new Vector3(2.3f, 0.95f, 2.9f), Vector3.zero, addBoxCollider: true);
            CreateBoxPart(ship, meshes.Box, "AftCabinRoof", wood, new Vector3(0f, 2.86f, -6.5f), new Vector3(2.8f, 0.14f, 3.1f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "Companionway", wood, new Vector3(0f, 1.62f, -3.9f), new Vector3(1.2f, 0.54f, 1.0f), Vector3.zero, addBoxCollider: true);

            // Bow structure helps visually seat the bowsprit into the hull.
            CreateBoxPart(ship, meshes.Box, "StemPost", wood, new Vector3(0f, 1.25f, 10.35f), new Vector3(0.18f, 2.4f, 0.35f), new Vector3(-18f, 0f, 0f));
            CreateBoxPart(ship, meshes.Box, "BeakheadKnee", wood, new Vector3(0f, 1.95f, 10.55f), new Vector3(0.35f, 0.35f, 1.10f), new Vector3(-24f, 0f, 0f));

            CreateCylinderPart(ship, meshes.Cylinder, "Bowsprit", wood, new Vector3(0f, 1.95f, 10.35f), new Vector3(0.14f, 3.25f, 0.14f), new Vector3(78f, 0f, 0f));
            CreateCylinderPart(ship, meshes.Cylinder, "Jibboom", wood, new Vector3(0f, 2.30f, 13.25f), new Vector3(0.10f, 2.05f, 0.10f), new Vector3(78f, 0f, 0f));

            CreateBoxPart(ship, meshes.Box, "Rudder", wood, new Vector3(0f, -0.56f, -9.55f), new Vector3(0.58f, 1.95f, 0.24f), new Vector3(6f, 0f, 0f), addBoxCollider: true);
            CreateBoxPart(ship, meshes.Box, "RudderHinge", metal, new Vector3(0f, -0.28f, -9.30f), new Vector3(0.7f, 0.1f, 0.05f), Vector3.zero);

            CreateCylinderPart(ship, meshes.Cylinder, "AnchorPort", metal, new Vector3(-2.58f, 0.9f, 7.5f), new Vector3(0.16f, 0.58f, 0.16f), new Vector3(0f, 0f, 90f));
            CreateCylinderPart(ship, meshes.Cylinder, "AnchorStarboard", metal, new Vector3(2.58f, 0.9f, 7.5f), new Vector3(0.16f, 0.58f, 0.16f), new Vector3(0f, 0f, 90f));

            CreateCylinderPart(ship, meshes.Cylinder, "HelmWheel", wood, new Vector3(0f, 2.48f, -7.2f), new Vector3(0.50f, 0.06f, 0.50f), new Vector3(90f, 0f, 0f));
            CreateCylinderPart(ship, meshes.Cylinder, "Capstan", wood, new Vector3(0f, 1.74f, 0.2f), new Vector3(0.50f, 0.48f, 0.50f), Vector3.zero);
        }

        private static void BuildRig(Transform ship, MeshLibrary meshes, Material wood)
        {
            // Sections intentionally overlap slightly to avoid visual gaps between mast parts.
            CreateCylinderPart(ship, meshes.Cylinder, "ForeMast", wood, new Vector3(0f, 4.75f, 2.0f), new Vector3(0.22f, 3.9f, 0.22f), Vector3.zero);
            CreateCylinderPart(ship, meshes.Cylinder, "ForeTopMast", wood, new Vector3(0f, 7.95f, 2.0f), new Vector3(0.14f, 2.5f, 0.14f), Vector3.zero);
            CreateCylinderPart(ship, meshes.Cylinder, "ForeTopGallant", wood, new Vector3(0f, 10.20f, 2.0f), new Vector3(0.10f, 2.0f, 0.10f), Vector3.zero);

            CreateCylinderPart(ship, meshes.Cylinder, "MainMast", wood, new Vector3(0f, 4.95f, -3.9f), new Vector3(0.24f, 4.1f, 0.24f), Vector3.zero);
            CreateCylinderPart(ship, meshes.Cylinder, "MainTopMast", wood, new Vector3(0f, 8.45f, -3.9f), new Vector3(0.15f, 2.9f, 0.15f), Vector3.zero);
            CreateCylinderPart(ship, meshes.Cylinder, "MainTopGallant", wood, new Vector3(0f, 11.00f, -3.9f), new Vector3(0.10f, 2.2f, 0.10f), Vector3.zero);

            CreateCylinderPart(ship, meshes.Cylinder, "ForeCourseYard", wood, new Vector3(0f, 5.1f, 2.0f), new Vector3(0.08f, 2.5f, 0.08f), new Vector3(0f, 0f, 90f));
            CreateCylinderPart(ship, meshes.Cylinder, "ForeTopsailYard", wood, new Vector3(0f, 7.1f, 2.0f), new Vector3(0.07f, 2.15f, 0.07f), new Vector3(0f, 0f, 90f));
            CreateCylinderPart(ship, meshes.Cylinder, "ForeTopgallantYard", wood, new Vector3(0f, 9.35f, 2.0f), new Vector3(0.06f, 1.55f, 0.06f), new Vector3(0f, 0f, 90f));

            CreateCylinderPart(ship, meshes.Cylinder, "MainCourseYard", wood, new Vector3(0f, 5.45f, -3.9f), new Vector3(0.08f, 2.8f, 0.08f), new Vector3(0f, 0f, 90f));
            CreateCylinderPart(ship, meshes.Cylinder, "MainTopsailYard", wood, new Vector3(0f, 7.85f, -3.9f), new Vector3(0.07f, 2.35f, 0.07f), new Vector3(0f, 0f, 90f));
            CreateCylinderPart(ship, meshes.Cylinder, "MainTopgallantYard", wood, new Vector3(0f, 10.35f, -3.9f), new Vector3(0.06f, 1.7f, 0.06f), new Vector3(0f, 0f, 90f));

            CreateCylinderPart(ship, meshes.Cylinder, "MainGaff", wood, new Vector3(0.95f, 6.25f, -4.2f), new Vector3(0.06f, 2.2f, 0.06f), new Vector3(90f, 0f, -16f));
            CreateCylinderPart(ship, meshes.Cylinder, "MainBoom", wood, new Vector3(0.55f, 3.95f, -4.1f), new Vector3(0.08f, 2.5f, 0.08f), new Vector3(90f, 0f, -8f));
        }

        private static void BuildSails(Transform ship, MeshLibrary meshes, Material sail)
        {
            // Square sails should hang under yards (X axis), so their sheet plane is XY (Y rotation near 0).
            CreateMeshPart(ship, "ForeCourse", meshes.SquareSail, sail, new Vector3(0f, 4.35f, 2.0f), new Vector3(4.2f, 2.2f, 0.5f), new Vector3(0f, 0f, 0f));
            CreateMeshPart(ship, "ForeTopsail", meshes.SquareSail, sail, new Vector3(0f, 6.8f, 2.0f), new Vector3(3.6f, 2.0f, 0.45f), new Vector3(0f, 0f, 0f));
            CreateMeshPart(ship, "ForeTopgallant", meshes.SquareSail, sail, new Vector3(0f, 9.1f, 2.0f), new Vector3(2.6f, 1.4f, 0.4f), new Vector3(0f, 0f, 0f));

            CreateMeshPart(ship, "MainCourse", meshes.SquareSail, sail, new Vector3(0f, 4.85f, -3.9f), new Vector3(4.5f, 2.5f, 0.55f), new Vector3(0f, 0f, 0f));
            CreateMeshPart(ship, "MainTopsail", meshes.SquareSail, sail, new Vector3(0f, 7.65f, -3.9f), new Vector3(3.8f, 2.1f, 0.45f), new Vector3(0f, 0f, 0f));
            CreateMeshPart(ship, "MainTopgallant", meshes.SquareSail, sail, new Vector3(0f, 10.1f, -3.9f), new Vector3(2.8f, 1.5f, 0.40f), new Vector3(0f, 0f, 0f));
            // Spanker remains fore-and-aft and stays in the YZ family.
            CreateMeshPart(ship, "Spanker", meshes.SquareSail, sail, new Vector3(1.20f, 4.8f, -4.3f), new Vector3(4.0f, 3.0f, 0.50f), new Vector3(0f, 90f, -14f));

            // Headsails are centered on their corresponding stays.
            CreateMeshPart(ship, "InnerJib", meshes.JibSail, sail, new Vector3(0.06f, 4.75f, 7.2f), new Vector3(2.3f, 1.8f, 0.36f), new Vector3(-11f, 90f, 5f));
            CreateMeshPart(ship, "OuterJib", meshes.JibSail, sail, new Vector3(0.08f, 5.5f, 8.65f), new Vector3(2.45f, 1.9f, 0.36f), new Vector3(-10f, 90f, 4f));
            CreateMeshPart(ship, "FlyingJib", meshes.JibSail, sail, new Vector3(0.10f, 6.25f, 10.1f), new Vector3(2.3f, 1.7f, 0.33f), new Vector3(-9f, 90f, 4f));
            CreateMeshPart(ship, "ForeTopmastStaysail", meshes.JibSail, sail, new Vector3(0.05f, 7.35f, 0.45f), new Vector3(2.2f, 1.9f, 0.34f), new Vector3(-8f, 90f, 4f));
        }

        private static void BuildRigging(Transform ship, MeshLibrary meshes, Material rigging, bool highDetail)
        {
            CreateLinePart(ship, meshes.Cylinder, "ForeStay", rigging, new Vector3(0f, 8.0f, 2.0f), new Vector3(0f, 2.15f, 12.25f), 0.028f);
            CreateLinePart(ship, meshes.Cylinder, "MainStay", rigging, new Vector3(0f, 9.0f, -3.9f), new Vector3(0f, 6.0f, 2.0f), 0.028f);
            CreateLinePart(ship, meshes.Cylinder, "BackstayPort", rigging, new Vector3(0f, 10.1f, -3.9f), new Vector3(-2.2f, 2.2f, -8.5f), 0.026f);
            CreateLinePart(ship, meshes.Cylinder, "BackstayStarboard", rigging, new Vector3(0f, 10.1f, -3.9f), new Vector3(2.2f, 2.2f, -8.5f), 0.026f);
            CreateLinePart(ship, meshes.Cylinder, "Bobstay", rigging, new Vector3(0f, 2.25f, 12.4f), new Vector3(0f, -0.95f, 10.3f), 0.028f);

            // Dedicated stays for headsails so sails and lines share the same span.
            CreateLinePart(ship, meshes.Cylinder, "InnerJibStay", rigging, new Vector3(0f, 6.2f, 3.8f), new Vector3(0f, 3.3f, 10.6f), 0.020f);
            CreateLinePart(ship, meshes.Cylinder, "OuterJibStay", rigging, new Vector3(0f, 7.1f, 4.7f), new Vector3(0f, 3.9f, 12.6f), 0.018f);
            CreateLinePart(ship, meshes.Cylinder, "FlyingJibStay", rigging, new Vector3(0f, 8.0f, 5.6f), new Vector3(0f, 4.5f, 14.6f), 0.016f);
            CreateLinePart(ship, meshes.Cylinder, "ForeTopmastStaysailStay", rigging, new Vector3(0f, 8.9f, -3.9f), new Vector3(0f, 5.8f, 4.8f), 0.018f);

            BuildShroudSet(ship, meshes.Cylinder, meshes.Box, rigging, "Fore", 2.45f, 2.0f, 8.5f, highDetail ? 6 : 4);
            BuildShroudSet(ship, meshes.Cylinder, meshes.Box, rigging, "Main", 2.50f, -3.9f, 9.1f, highDetail ? 6 : 4);
        }

        private static void BuildShroudSet(
            Transform ship,
            Mesh cylinder,
            Mesh box,
            Material rigging,
            string prefix,
            float halfWidth,
            float mastZ,
            float topY,
            int lineCount)
        {
            for (int i = 0; i < lineCount; i++)
            {
                float zOffset = -0.70f + i * 0.28f;
                float sideX = halfWidth + 0.05f * i;

                Vector3 top = new Vector3(0f, topY - i * 0.28f, mastZ + zOffset * 0.28f);
                Vector3 portBottom = new Vector3(-sideX, 1.35f, mastZ + zOffset);
                Vector3 starboardBottom = new Vector3(sideX, 1.35f, mastZ + zOffset);

                CreateLinePart(ship, cylinder, $"{prefix}ShroudPort_{i + 1}", rigging, top, portBottom, 0.022f);
                CreateLinePart(ship, cylinder, $"{prefix}ShroudStarboard_{i + 1}", rigging, top, starboardBottom, 0.022f);
            }

            float minOffset = -0.70f;
            float maxOffset = -0.70f + (lineCount - 1) * 0.28f;
            float ratlineCenterZ = mastZ + (minOffset + maxOffset) * 0.5f;
            float ratlineSpan = (maxOffset - minOffset) + 0.20f;
            float sideBottomX = halfWidth + 0.05f * (lineCount - 1) * 0.5f;
            float sideTopY = topY - (lineCount - 1) * 0.28f * 0.5f;

            for (int i = 0; i < 7; i++)
            {
                float y = 3.0f + i * 0.48f;
                float climb = Mathf.InverseLerp(sideTopY, 1.35f, y);
                float sideCenterX = Mathf.Lerp(0f, sideBottomX, climb);
                // Ratlines should bridge shrouds fore-aft (Z axis), not be rotated sideways.
                CreateBoxPart(ship, box, $"{prefix}RatlinePort_{i + 1}", rigging, new Vector3(-sideCenterX, y, ratlineCenterZ), new Vector3(0.02f, 0.02f, ratlineSpan), Vector3.zero);
                CreateBoxPart(ship, box, $"{prefix}RatlineStarboard_{i + 1}", rigging, new Vector3(sideCenterX, y, ratlineCenterZ), new Vector3(0.02f, 0.02f, ratlineSpan), Vector3.zero);
            }
        }

        private static void BuildCrewStations(Transform ship)
        {
            CreateStation(ship, "Station_Helm", new Vector3(0f, 2.0f, -6.8f));
            CreateStation(ship, "Station_Sails", new Vector3(0f, 1.7f, -0.2f));
            CreateStation(ship, "Station_Sails_Fore", new Vector3(0f, 1.7f, 2.0f));
            CreateStation(ship, "Station_Sails_Main", new Vector3(0f, 1.7f, -2.5f));
            CreateStation(ship, "Station_Anchor", new Vector3(0f, 1.7f, 7.0f));
            CreateStation(ship, "Station_Repairs", new Vector3(-1.3f, 1.6f, 0.8f));
            CreateStation(ship, "Station_Guns", new Vector3(1.4f, 1.6f, -0.3f));
        }

        private static void BuildHighDetail(Transform ship, MeshLibrary meshes, Material wood, Material metal, Material rigging)
        {
            for (int i = 0; i < 13; i++)
            {
                float z = -7.3f + i * 1.2f;
                CreateBoxPart(ship, meshes.Box, $"RailPostPort_{i + 1}", wood, new Vector3(-2.43f, 1.63f, z), new Vector3(0.08f, 0.52f, 0.08f), Vector3.zero);
                CreateBoxPart(ship, meshes.Box, $"RailPostStarboard_{i + 1}", wood, new Vector3(2.43f, 1.63f, z), new Vector3(0.08f, 0.52f, 0.08f), Vector3.zero);
            }

            CreateBoxPart(ship, meshes.Box, "TopRailPort", wood, new Vector3(-2.43f, 1.88f, -0.2f), new Vector3(0.07f, 0.08f, 14.4f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "TopRailStarboard", wood, new Vector3(2.43f, 1.88f, -0.2f), new Vector3(0.07f, 0.08f, 14.4f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "SafetyLinePort", rigging, new Vector3(-2.37f, 1.67f, -0.2f), new Vector3(0.03f, 0.03f, 14.3f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "SafetyLineStarboard", rigging, new Vector3(2.37f, 1.67f, -0.2f), new Vector3(0.03f, 0.03f, 14.3f), Vector3.zero);

            for (int i = 0; i < 4; i++)
            {
                float z = -4.8f + i * 2.8f;
                CreateBoxPart(ship, meshes.Box, $"CannonBasePort_{i + 1}", wood, new Vector3(-1.56f, 1.33f, z), new Vector3(0.55f, 0.16f, 0.75f), Vector3.zero);
                CreateCylinderPart(ship, meshes.Cylinder, $"CannonBarrelPort_{i + 1}", metal, new Vector3(-1.96f, 1.48f, z), new Vector3(0.10f, 0.50f, 0.10f), new Vector3(0f, 0f, 90f));
                CreateBoxPart(ship, meshes.Box, $"CannonBaseStarboard_{i + 1}", wood, new Vector3(1.56f, 1.33f, z), new Vector3(0.55f, 0.16f, 0.75f), Vector3.zero);
                CreateCylinderPart(ship, meshes.Cylinder, $"CannonBarrelStarboard_{i + 1}", metal, new Vector3(1.96f, 1.48f, z), new Vector3(0.10f, 0.50f, 0.10f), new Vector3(0f, 0f, 90f));
            }

            CreateBoxPart(ship, meshes.Box, "LongboatHull", wood, new Vector3(0f, 2.12f, 0.9f), new Vector3(2.1f, 0.45f, 0.9f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "LongboatKeel", wood, new Vector3(0f, 1.9f, 0.9f), new Vector3(0.55f, 0.2f, 1.0f), Vector3.zero);

            CreateBoxPart(ship, meshes.Box, "QuarterGalleryPort", wood, new Vector3(-1.85f, 2.25f, -8.4f), new Vector3(0.45f, 0.85f, 0.9f), Vector3.zero);
            CreateBoxPart(ship, meshes.Box, "QuarterGalleryStarboard", wood, new Vector3(1.85f, 2.25f, -8.4f), new Vector3(0.45f, 0.85f, 0.9f), Vector3.zero);

            for (int i = 0; i < 3; i++)
            {
                float x = -0.75f + i * 0.75f;
                CreateBoxPart(ship, meshes.Box, $"SternWindow_{i + 1}", metal, new Vector3(x, 2.15f, -9.18f), new Vector3(0.42f, 0.38f, 0.03f), Vector3.zero);
            }
        }

        private static Mesh GenerateHullMesh(int lengthSegments, int sectionSegments)
        {
            float sternZ = -9.4f;
            float bowZ = 10.9f;

            var vertices = new List<Vector3>((lengthSegments + 1) * (sectionSegments + 1) + 2);
            var uvs = new List<Vector2>((lengthSegments + 1) * (sectionSegments + 1) + 2);
            var triangles = new List<int>(lengthSegments * sectionSegments * 6 + sectionSegments * 6);

            int ringSize = sectionSegments + 1;

            for (int i = 0; i <= lengthSegments; i++)
            {
                float t = i / (float)lengthSegments;
                float z = Mathf.Lerp(sternZ, bowZ, t);
                float halfBeam = EvaluateHalfBeam(t);
                float sheer = EvaluateSheer(t);
                float draft = EvaluateDraft(t);
                float bowFlare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, t));

                for (int j = 0; j <= sectionSegments; j++)
                {
                    float u = j / (float)sectionSegments;
                    float angle = Mathf.PI * u;
                    float side = Mathf.Cos(angle);
                    // Clamp to avoid tiny negative values near PI that produce NaN in fractional Pow.
                    float depthMask = Mathf.Clamp01(Mathf.Sin(angle));
                    if (j == 0 || j == sectionSegments)
                        depthMask = 0f;

                    float tumble = 1f - 0.20f * Mathf.Pow(1f - depthMask, 1.3f);
                    float x = halfBeam * side * tumble;
                    x *= 1f + bowFlare * 0.08f * (1f - depthMask);

                    float y = sheer - draft * Mathf.Pow(depthMask, 0.80f);

                    if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                    {
                        x = 0f;
                        y = sheer;
                    }

                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(u, t));
                }
            }

            for (int i = 0; i < lengthSegments; i++)
            {
                int current = i * ringSize;
                int next = (i + 1) * ringSize;

                for (int j = 0; j < sectionSegments; j++)
                {
                    int a = current + j;
                    int b = next + j;
                    int c = next + j + 1;
                    int d = current + j + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            int sternCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0.35f, sternZ - 0.22f));
            uvs.Add(new Vector2(0.5f, 0f));

            for (int j = 0; j < sectionSegments; j++)
            {
                triangles.Add(sternCenter);
                triangles.Add(j + 1);
                triangles.Add(j);
            }

            int bowRingStart = lengthSegments * ringSize;
            int bowCenter = vertices.Count;
            vertices.Add(new Vector3(0f, 0.48f, bowZ + 0.14f));
            uvs.Add(new Vector2(0.5f, 1f));

            for (int j = 0; j < sectionSegments; j++)
            {
                triangles.Add(bowCenter);
                triangles.Add(bowRingStart + j);
                triangles.Add(bowRingStart + j + 1);
            }

            // Parametric strip is built with inward winding by default; flip once for correct exterior culling.
            ReverseTriangleWinding(triangles);

            var mesh = new Mesh { name = "BrigHull" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh GenerateDeckMesh(int lengthSegments, int beamSegments)
        {
            const float hullSternZ = -9.4f;
            const float hullBowZ = 10.9f;
            float sternZ = -9.0f;
            float bowZ = 10.72f;

            var vertices = new List<Vector3>((lengthSegments + 1) * (beamSegments + 1));
            var uvs = new List<Vector2>((lengthSegments + 1) * (beamSegments + 1));
            var triangles = new List<int>(lengthSegments * beamSegments * 6);

            int row = beamSegments + 1;

            for (int i = 0; i <= lengthSegments; i++)
            {
                float t = i / (float)lengthSegments;
                float z = Mathf.Lerp(sternZ, bowZ, t);
                float hullT = Mathf.InverseLerp(hullSternZ, hullBowZ, z);
                float halfBeam = EvaluateHalfBeam(hullT) * 0.93f;
                float deckY = EvaluateSheer(hullT) - 0.03f;

                for (int j = 0; j <= beamSegments; j++)
                {
                    float u = j / (float)beamSegments;
                    float side = Mathf.Lerp(-1f, 1f, u);
                    float x = side * halfBeam;

                    // Slight deck camber improves silhouette and drains toward scuppers visually.
                    float camber = 0.12f * (1f - side * side);
                    float y = deckY + camber;

                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(u, t));
                }
            }

            for (int i = 0; i < lengthSegments; i++)
            {
                int current = i * row;
                int next = (i + 1) * row;

                for (int j = 0; j < beamSegments; j++)
                {
                    int a = current + j;
                    int b = next + j;
                    int c = next + j + 1;
                    int d = current + j + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "BrigDeck" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh GenerateCurvedSailMesh(int xSegments, int ySegments, float belly)
        {
            var vertices = new List<Vector3>((xSegments + 1) * (ySegments + 1));
            var uvs = new List<Vector2>((xSegments + 1) * (ySegments + 1));
            var triangles = new List<int>(xSegments * ySegments * 6);

            int row = xSegments + 1;

            for (int y = 0; y <= ySegments; y++)
            {
                float v = y / (float)ySegments;
                float py = v - 0.5f;
                float verticalBelly = Mathf.Sin(v * Mathf.PI);

                for (int x = 0; x <= xSegments; x++)
                {
                    float u = x / (float)xSegments;
                    float px = u - 0.5f;
                    float edgeFade = 1f - Mathf.Pow(Mathf.Abs(px) * 2f, 1.5f);
                    float pz = belly * edgeFade * verticalBelly;

                    vertices.Add(new Vector3(px, py, pz));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int y = 0; y < ySegments; y++)
            {
                int current = y * row;
                int next = (y + 1) * row;

                for (int x = 0; x < xSegments; x++)
                {
                    int a = current + x;
                    int b = next + x;
                    int c = next + x + 1;
                    int d = current + x + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "SquareSail" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh GenerateJibSailMesh(int xSegments, int ySegments, float belly)
        {
            var vertices = new List<Vector3>((xSegments + 1) * (ySegments + 1));
            var uvs = new List<Vector2>((xSegments + 1) * (ySegments + 1));
            var triangles = new List<int>(xSegments * ySegments * 6);

            int row = xSegments + 1;

            for (int y = 0; y <= ySegments; y++)
            {
                float v = y / (float)ySegments;
                float py = v - 0.5f;
                float width = Mathf.Lerp(1f, 0.08f, v);
                float verticalBelly = Mathf.Sin(v * Mathf.PI * 0.9f);

                for (int x = 0; x <= xSegments; x++)
                {
                    float u = x / (float)xSegments;
                    float px = (u - 0.5f) * width;
                    float edgeFade = 1f - Mathf.Pow(Mathf.Abs(u * 2f - 1f), 1.5f);
                    float pz = belly * edgeFade * verticalBelly;

                    vertices.Add(new Vector3(px, py, pz));
                    uvs.Add(new Vector2(u, v));
                }
            }

            for (int y = 0; y < ySegments; y++)
            {
                int current = y * row;
                int next = (y + 1) * row;

                for (int x = 0; x < xSegments; x++)
                {
                    int a = current + x;
                    int b = next + x;
                    int c = next + x + 1;
                    int d = current + x + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "JibSail" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh GenerateBoxMesh()
        {
            var mesh = new Mesh { name = "UnitBox" };

            Vector3[] vertices =
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            };

            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                1, 2, 6, 1, 6, 5,
                5, 6, 7, 5, 7, 4,
                4, 7, 3, 4, 3, 0,
                3, 7, 6, 3, 6, 2,
                4, 0, 1, 4, 1, 5,
            };

            Vector2[] uvs = new Vector2[vertices.Length];
            for (int i = 0; i < uvs.Length; i++)
                uvs[i] = new Vector2(vertices[i].x + 0.5f, vertices[i].z + 0.5f);

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Mesh GenerateCylinderMesh(int radialSegments, int heightSegments, bool capped)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int y = 0; y <= heightSegments; y++)
            {
                float v = y / (float)heightSegments;
                // Unity primitive cylinder is 2 units tall (from -1 to +1 on local Y).
                float py = (v - 0.5f) * 2f;

                for (int i = 0; i <= radialSegments; i++)
                {
                    float u = i / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * 0.5f;
                    float z = Mathf.Sin(angle) * 0.5f;

                    vertices.Add(new Vector3(x, py, z));
                    uvs.Add(new Vector2(u, v));
                }
            }

            int row = radialSegments + 1;
            for (int y = 0; y < heightSegments; y++)
            {
                int current = y * row;
                int next = (y + 1) * row;

                for (int i = 0; i < radialSegments; i++)
                {
                    int a = current + i;
                    int b = next + i;
                    int c = next + i + 1;
                    int d = current + i + 1;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            if (capped)
            {
                int bottomCenter = vertices.Count;
                vertices.Add(new Vector3(0f, -1f, 0f));
                uvs.Add(new Vector2(0.5f, 0.5f));

                int topCenter = vertices.Count;
                vertices.Add(new Vector3(0f, 1f, 0f));
                uvs.Add(new Vector2(0.5f, 0.5f));

                int bottomRow = 0;
                int topRow = heightSegments * row;

                for (int i = 0; i < radialSegments; i++)
                {
                    triangles.Add(bottomCenter);
                    triangles.Add(bottomRow + i + 1);
                    triangles.Add(bottomRow + i);

                    triangles.Add(topCenter);
                    triangles.Add(topRow + i);
                    triangles.Add(topRow + i + 1);
                }
            }

            var mesh = new Mesh { name = "UnitCylinder" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static float EvaluateHalfBeam(float t)
        {
            float centered = Mathf.Clamp01(1f - Mathf.Pow((t - 0.48f) / 0.52f, 2f));
            float beam = 0.68f + 1.95f * centered;

            float sternTaper = Mathf.Lerp(0.58f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.24f, t)));
            float bowTaper = Mathf.Lerp(1f, 0.42f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 1f, t)));

            return beam * sternTaper * bowTaper;
        }

        private static float EvaluateSheer(float t)
        {
            float sternRise = Mathf.Lerp(0.55f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.32f, t)));
            float bowRise = Mathf.Lerp(0f, 0.66f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, t)));
            return 1.05f + sternRise + bowRise;
        }

        private static float EvaluateDraft(float t)
        {
            float midBoost = Mathf.Lerp(0.84f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.50f, t)));
            float bowShallow = Mathf.Lerp(1f, 0.70f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 1f, t)));
            return 2.35f * midBoost * bowShallow;
        }

        private static void ReverseTriangleWinding(List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Mesh SaveOrUpdateMesh(string path, Mesh generated)
        {
            generated.name = Path.GetFileNameWithoutExtension(path);

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            CopyMeshData(generated, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
            return existing;
        }

        private static void CopyMeshData(Mesh source, Mesh destination)
        {
            destination.Clear();
            destination.indexFormat = source.indexFormat;

            destination.vertices = source.vertices;
            destination.normals = source.normals;
            destination.tangents = source.tangents;
            destination.uv = source.uv;
            destination.uv2 = source.uv2;
            destination.colors = source.colors;

            destination.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
                destination.SetTriangles(source.GetTriangles(i), i);

            destination.bounds = source.bounds;
        }

        private static void CreateMeshPart(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            bool addMeshCollider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            if (addMeshCollider)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }
        }

        private static void CreateBoxPart(
            Transform parent,
            Mesh box,
            string name,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            bool addBoxCollider = false)
        {
            CreateMeshPart(parent, name, box, material, localPosition, localScale, localEuler);

            if (!addBoxCollider)
                return;

            var go = parent.Find(name);
            if (go == null)
                return;

            go.gameObject.AddComponent<BoxCollider>();
        }

        private static void CreateCylinderPart(
            Transform parent,
            Mesh cylinder,
            string name,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler)
        {
            CreateMeshPart(parent, name, cylinder, material, localPosition, localScale, localEuler);
        }

        private static void CreateLinePart(
            Transform parent,
            Mesh cylinder,
            string name,
            Material material,
            Vector3 localStart,
            Vector3 localEnd,
            float radius)
        {
            Vector3 delta = localEnd - localStart;
            float length = delta.magnitude;
            if (length < 0.001f)
                return;

            Vector3 mid = (localStart + localEnd) * 0.5f;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            // Cylinder mesh has local height 2, so Y scale is half target segment length.
            Vector3 scale = new Vector3(radius, length * 0.5f, radius);

            CreateMeshPart(parent, name, cylinder, material, mid, scale, rotation.eulerAngles);
        }

        private static void CreateStation(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

