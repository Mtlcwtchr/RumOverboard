using System.IO;
using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// "Make the sea feel like a sea": a generated tileable ripple normal map (the material had no
    /// detail normal at all), realistic material settings for OceanStylizedURP, scene haze, and
    /// sea-keeping values that let the hull actually ride the swell (the old stability assist held
    /// it within ~3°).
    /// </summary>
    public static class OceanLookSetup
    {
        private const string MaterialPath = "Assets/Source/Environment/Materials/Ocean/OceanStylizedURP.mat";
        private const string NormalPath = "Assets/Source/Environment/Materials/Ocean/OceanDetailNormal.png";
        private const string FoamTexturePath = "Assets/Source/Environment/Textures/Ocean/External/foam3.png";
        private const string PackNormalPath = "Assets/Source/Environment/Textures/Ocean/External/normal_map_02.png";
        private const int Size = 512;

        [MenuItem("RumOverboard/Ship Sandbox/Ocean/Apply Realistic Ocean Look", priority = 60)]
        public static void ApplyMaterial()
        {
            Texture2D normal = EnsureDetailNormal();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Debug.LogWarning($"[OceanLook] Material not found: {MaterialPath}");
                return;
            }

            Set(mat, "_BaseColorDeep", new Color(0.012f, 0.085f, 0.15f));
            Set(mat, "_BaseColorShallow", new Color(0.04f, 0.30f, 0.34f));
            Set(mat, "_SubsurfaceColor", new Color(0.06f, 0.50f, 0.46f));
            Set(mat, "_FoamColor", new Color(0.92f, 0.96f, 1f));
            Set(mat, "_HorizonColor", new Color(0.52f, 0.64f, 0.74f));
            Set(mat, "_TransparencyMin", 0.62f);
            Set(mat, "_TransparencyMax", 0.985f); // open water is opaque — no sky showing through the sea
            Set(mat, "_DepthFadeDistance", 9f);
            Set(mat, "_FoamIntensity", 0.55f);
            Set(mat, "_CrestFoamThreshold", 0.58f);
            Set(mat, "_Gloss", 0.65f);
            Set(mat, "_SpecularPower", 140f);
            Set(mat, "_Roughness", 0.05f);
            Set(mat, "_ReflectionStrength", 0.7f);
            Set(mat, "_SubsurfaceStrength", 1.1f);
            Set(mat, "_RippleScale", 1.6f);
            Set(mat, "_RippleSpeed", 0.8f);
            Set(mat, "_DetailNormalScale", 0.55f);
            Set(mat, "_DetailTiling", 0.075f);
            Set(mat, "_DetailScroll", 0.035f);
            Set(mat, "_RefractionStrength", 0.03f);
            Set(mat, "_GlitterPower", 1100f);
            Set(mat, "_GlitterStrength", 7f);
            Set(mat, "_CrestLift", 0.8f);
            Set(mat, "_CrestHeight", 1.4f);
            Set(mat, "_FoamNoiseScale", 0.32f);
            if (normal != null && mat.HasProperty("_DetailNormal"))
                mat.SetTexture("_DetailNormal", normal);

            // Foam texture + hand-made ripple normals from the imported water packs.
            var foamTex = AssetDatabase.LoadAssetAtPath<Texture2D>(FoamTexturePath);
            if (foamTex != null && mat.HasProperty("_FoamTex"))
                mat.SetTexture("_FoamTex", foamTex);
            var packNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(PackNormalPath);
            if (packNormal != null && mat.HasProperty("_DetailNormal"))
            {
                mat.SetTexture("_DetailNormal", packNormal);
                Set(mat, "_DetailTiling", 0.045f);
                Set(mat, "_DetailNormalScale", 0.75f);
            }
            Set(mat, "_FoamTiling", 0.11f);
            Set(mat, "_FoamSharpness", 4f);
            Set(mat, "_InteractionFoamStrength", 1f);
            Set(mat, "_IntersectionFoamDepth", 0.55f);
            Set(mat, "_IntersectionFoamStrength", 0.75f);
            Set(mat, "_AeratedTint", 0.35f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[OceanLook] Ocean material updated (detail normals, opaque deep water, glitter, horizon haze).");
        }

        /// <summary>Sea haze for the active scene.</summary>
        public static void ApplySceneAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.8f);
            RenderSettings.fogDensity = 0.0024f;
        }

        /// <summary>Let the hull follow the sea: soft righting assist, light damping (prefab values).</summary>
        public static void ApplySeakeeping(ShipBuoyancyController buoyancy)
        {
            if (buoyancy == null) return;
            var so = new SerializedObject(buoyancy);
            void F(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
            void B(string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
            F("rollStability", 5f);       // was 28: held the deck level against the waves
            F("pitchStability", 4f);      // was 22
            F("rollDamping", 2.6f);       // was 10
            F("pitchDamping", 2.6f);      // was 8
            F("surfaceNormalInfluence", 0.85f); // the "up" it rights toward follows the swell
            F("angularDrag", 0.9f);       // was 3.8
            F("verticalDamping", 7f);     // a little more heave
            B("debugDrawForces", false);
            B("debugDrawResultants", false);
            B("debugDrawNormals", false);
            B("debugDrawAlways", false);
            B("debugLabels", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Picks the sea state the scene starts in (OceanSeaStateController progression[0]).</summary>
        public static void SetStartSeaState(OceanSeaStateController controller, string profileName)
        {
            if (controller == null) return;
            string[] guids = AssetDatabase.FindAssets($"{profileName} t:OceanSeaStateProfile");
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[OceanLook] Sea state profile '{profileName}' not found.");
                return;
            }
            var profile = AssetDatabase.LoadAssetAtPath<OceanSeaStateProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var so = new SerializedObject(controller);
            SerializedProperty list = so.FindProperty("progression");
            if (list == null) return;
            if (list.arraySize == 0) list.arraySize = 1;
            // Put the wanted profile first (Awake applies progression[0]), keep the rest after it.
            var existing = new System.Collections.Generic.List<Object>();
            for (int i = 0; i < list.arraySize; i++)
                existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            existing.Remove(profile);
            existing.Insert(0, profile);
            list.arraySize = existing.Count;
            for (int i = 0; i < existing.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = existing[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Detail normal: tileable capillary ripples -----------------------------------------
        public static Texture2D EnsureDetailNormal()
        {
            if (!File.Exists(NormalPath))
            {
                var heights = new float[Size * Size];
                var rng = new System.Random(4242);
                // Integer wave vectors → perfectly tileable; many directions, falling amplitude.
                const int waves = 48;
                var kx = new int[waves];
                var ky = new int[waves];
                var amp = new float[waves];
                var ph = new float[waves];
                for (int w = 0; w < waves; w++)
                {
                    int freq = 2 + rng.Next(0, 22);
                    double angle = rng.NextDouble() * System.Math.PI * 2.0;
                    kx[w] = Mathf.RoundToInt((float)(System.Math.Cos(angle) * freq));
                    ky[w] = Mathf.RoundToInt((float)(System.Math.Sin(angle) * freq));
                    if (kx[w] == 0 && ky[w] == 0) kx[w] = 1;
                    amp[w] = 1f / Mathf.Pow(freq, 1.15f);
                    ph[w] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                }
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size * Mathf.PI * 2f;
                    float v = y / (float)Size * Mathf.PI * 2f;
                    float h = 0f;
                    for (int w = 0; w < waves; w++)
                    {
                        // Sharpened crests (1 - |sin|) read like real wind ripples.
                        float s = Mathf.Sin(kx[w] * u + ky[w] * v + ph[w]);
                        h += amp[w] * (1f - Mathf.Abs(s));
                    }
                    heights[y * Size + x] = h;
                }

                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                var px = new Color32[Size * Size];
                const float strength = 6f;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float l = heights[y * Size + (x + Size - 1) % Size];
                    float r = heights[y * Size + (x + 1) % Size];
                    float d = heights[((y + Size - 1) % Size) * Size + x];
                    float t = heights[((y + 1) % Size) * Size + x];
                    Vector3 n = new Vector3((l - r) * strength, (d - t) * strength, 1f).normalized;
                    px[y * Size + x] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                }
                tex.SetPixels32(px);
                tex.Apply();
                File.WriteAllBytes(NormalPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(NormalPath);
            }

            if (AssetImporter.GetAtPath(NormalPath) is TextureImporter importer &&
                (importer.textureType != TextureImporterType.NormalMap || importer.wrapMode != TextureWrapMode.Repeat))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        }

        private static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        private static void Set(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }
    }
}
