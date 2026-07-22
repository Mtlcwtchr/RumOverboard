using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Source.Scripts.Editor
{
    public static class OceanSystemSetupMenu
    {
        private const string DataFolder = "Assets/Source/Environment/OceanData";
        private const string OceanMaterialsFolder = "Assets/Source/Environment/Materials/Ocean";
        private const string OceanMaterialPath = "Assets/Source/Environment/Materials/Ocean/OceanStylizedURP.mat";
        private const string OceanShaderPath = "Source/Gameplay/Ocean/StylizedURP";

        [MenuItem("RumOverboard/Setup/Ocean/Create Default Ocean Assets")]
        public static void CreateDefaultOceanAssets()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder(DataFolder);

            var low = CreateQuality("OceanQuality_Low.asset", 48, 36, 28, 2, 4, 4);
            var medium = CreateQuality("OceanQuality_Medium.asset", 80, 52, 40, 3, 6, 6);
            var high = CreateQuality("OceanQuality_High.asset", 112, 72, 56, 4, 8, 8);

            var calm = CreateSeaStateCalm("SeaState_Calm.asset");
            var moderate = CreateSeaStateModerate("SeaState_Moderate.asset");
            var storm = CreateSeaStateStorm("SeaState_Storm.asset");

            var configPath = DataFolder + "/OceanSimulationConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<OceanSimulationConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<OceanSimulationConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            config.defaultProfile = calm;
            config.availableProfiles = new[] { calm, moderate, storm };
            config.low = low;
            config.medium = medium;
            config.high = high;
            config.transitionSeconds = 8f;
            config.sharedSeed = 1337;
            config.seaLevel = 0f;

            if (config.depthProfile == null)
            {
                string depthPath = DataFolder + "/OceanDepthProfile.asset";
                var depth = AssetDatabase.LoadAssetAtPath<OceanDepthProfile>(depthPath);
                if (depth == null)
                {
                    depth = ScriptableObject.CreateInstance<OceanDepthProfile>();
                    depth.fallbackDepth = 40f;
                    depth.shallowDepth = 5f;
                    AssetDatabase.CreateAsset(depth, depthPath);
                }

                config.depthProfile = depth;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[OceanSystemSetupMenu] Created default ocean assets in '{DataFolder}'.");
        }

        [MenuItem("RumOverboard/Setup/Ocean/Setup Gameplay Scene Ocean Root")]
        public static void SetupGameplaySceneOceanRoot()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Gameplay.unity", OpenSceneMode.Single);

            var oceanRoot = GameObject.Find("OceanSystem");
            if (oceanRoot == null)
                oceanRoot = new GameObject("OceanSystem");

            var waveField = oceanRoot.GetComponent<OceanWaveField>();
            if (waveField == null)
                waveField = oceanRoot.AddComponent<OceanWaveField>();

            var currentSystem = oceanRoot.GetComponent<OceanCurrentSystem>();
            if (currentSystem == null)
                currentSystem = oceanRoot.AddComponent<OceanCurrentSystem>();

            var depthProvider = oceanRoot.GetComponent<OceanDepthProvider>();
            if (depthProvider == null)
                depthProvider = oceanRoot.AddComponent<OceanDepthProvider>();

            var surface = oceanRoot.GetComponent<OceanSurfaceRenderer>();
            if (surface == null)
                surface = oceanRoot.AddComponent<OceanSurfaceRenderer>();

            var seaStateController = oceanRoot.GetComponent<OceanSeaStateController>();
            if (seaStateController == null)
                seaStateController = oceanRoot.AddComponent<OceanSeaStateController>();

            var config = AssetDatabase.LoadAssetAtPath<OceanSimulationConfig>(DataFolder + "/OceanSimulationConfig.asset");
            var oceanMaterial = CreateOrUpdateOceanMaterialAsset();
            var ship = GameObject.Find("MooredShipPlaceholder");
            if (config != null)
            {
                var so = new SerializedObject(waveField);
                so.FindProperty("config").objectReferenceValue = config;
                so.FindProperty("currentSystem").objectReferenceValue = currentSystem;
                so.FindProperty("depthProvider").objectReferenceValue = depthProvider;
                so.ApplyModifiedPropertiesWithoutUndo();

                Vector3 p = oceanRoot.transform.position;
                oceanRoot.transform.position = new Vector3(p.x, config.seaLevel, p.z);

                var depthSo = new SerializedObject(depthProvider);
                depthSo.FindProperty("config").objectReferenceValue = config;
                depthSo.FindProperty("terrain").objectReferenceValue = Terrain.activeTerrain;
                depthSo.ApplyModifiedPropertiesWithoutUndo();

                var seaSo = new SerializedObject(seaStateController);
                seaSo.FindProperty("waveField").objectReferenceValue = waveField;
                seaSo.FindProperty("progression").arraySize = config.availableProfiles != null ? config.availableProfiles.Length : 0;
                if (config.availableProfiles != null)
                {
                    for (int i = 0; i < config.availableProfiles.Length; i++)
                        seaSo.FindProperty("progression").GetArrayElementAtIndex(i).objectReferenceValue = config.availableProfiles[i];
                }
                seaSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var surfaceSo = new SerializedObject(surface);
            surfaceSo.FindProperty("waveField").objectReferenceValue = waveField;
            surfaceSo.FindProperty("followCamera").objectReferenceValue = Camera.main;
            if (ship != null)
                surfaceSo.FindProperty("followTarget").objectReferenceValue = ship.transform;
            if (oceanMaterial != null)
                surfaceSo.FindProperty("oceanMaterial").objectReferenceValue = oceanMaterial;
            surfaceSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[OceanSystemSetupMenu] Ocean root has been configured in Gameplay scene.");
        }

        [MenuItem("RumOverboard/Setup/Ocean/Create or Update Ocean Material (URP)")]
        public static void CreateOrUpdateOceanMaterial()
        {
            var material = CreateOrUpdateOceanMaterialAsset();
            if (material == null)
            {
                Debug.LogWarning("[OceanSystemSetupMenu] Ocean material was not created. Check URP shader availability.");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OceanSystemSetupMenu] Ocean material ready: '{OceanMaterialPath}'.");
        }

        [MenuItem("RumOverboard/Setup/Ocean/Attach Buoyancy To Selected Ship")]
        public static void AttachBuoyancyToSelectedShip()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[OceanSystemSetupMenu] Select a ship root GameObject first.");
                return;
            }

            var rb = selected.GetComponent<Rigidbody>();
            if (rb == null)
                rb = selected.AddComponent<Rigidbody>();

            rb.mass = Mathf.Max(rb.mass, 1800f);
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var buoyancy = selected.GetComponent<ShipBuoyancyController>();
            if (buoyancy == null)
                buoyancy = selected.AddComponent<ShipBuoyancyController>();

            var motion = selected.GetComponent<ShipDeckMotionProvider>();
            if (motion == null)
                motion = selected.AddComponent<ShipDeckMotionProvider>();

            var fx = selected.GetComponent<ShipWaterFxController>();
            if (fx == null)
                fx = selected.AddComponent<ShipWaterFxController>();

            WireShipScriptReferences(selected, buoyancy, motion, fx, rb);

            EditorUtility.SetDirty(selected);
            Debug.Log($"[OceanSystemSetupMenu] Buoyancy components attached to '{selected.name}'.");
        }

        [MenuItem("RumOverboard/Setup/Ocean/Wire Selected Ship Script References")]
        public static void WireSelectedShipScriptReferences()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[OceanSystemSetupMenu] Select a ship root GameObject first.");
                return;
            }

            var rb = selected.GetComponent<Rigidbody>();
            var buoyancy = selected.GetComponent<ShipBuoyancyController>();
            var motion = selected.GetComponent<ShipDeckMotionProvider>();
            var fx = selected.GetComponent<ShipWaterFxController>();

            if (rb == null || buoyancy == null)
            {
                Debug.LogWarning("[OceanSystemSetupMenu] Ship must have Rigidbody and ShipBuoyancyController first.");
                return;
            }

            WireShipScriptReferences(selected, buoyancy, motion, fx, rb);
            Debug.Log($"[OceanSystemSetupMenu] Ocean references wired on '{selected.name}'.");
        }

        private static OceanQualityPreset CreateQuality(string fileName, int nearRes, int midRes, int farRes, int iterations, int largeWaves, int mediumWaves)
        {
            string path = DataFolder + "/" + fileName;
            var preset = AssetDatabase.LoadAssetAtPath<OceanQualityPreset>(path);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<OceanQualityPreset>();
                AssetDatabase.CreateAsset(preset, path);
            }

            preset.nearResolution = nearRes;
            preset.midResolution = midRes;
            preset.farResolution = farRes;
            preset.horizontalDisplacementIterations = iterations;
            preset.maxCpuLargeWaves = largeWaves;
            preset.maxCpuMediumWaves = mediumWaves;

            EditorUtility.SetDirty(preset);
            return preset;
        }

        private static OceanSeaStateProfile CreateSeaStateCalm(string fileName)
        {
            var profile = GetOrCreateProfile(fileName);
            profile.globalCurrentDirection = new Vector2(1f, 0.1f);
            profile.globalCurrentSpeed = 0.15f;
            profile.directionJitterDegrees = 1f;
            profile.foamIntensity = 0.15f;
            profile.crestFoamThreshold = 0.78f;
            profile.buoyancyMultiplier = 0.9f;
            profile.dragMultiplier = 0.85f;
            profile.waveImpactMultiplier = 0.75f;
            profile.splashFrequencyScale = 0.5f;
            profile.waves = new[]
            {
                Wave(new Vector2(1f, 0f), 0.35f, 26f, 2.5f, 1f, 0.35f, 0f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(0.3f, 1f), 0.18f, 15f, 2.0f, 1f, 0.25f, 1.8f, 1f, 1f, OceanWaveBand.Medium),
                Wave(new Vector2(1f, 0.6f), 0.06f, 6f, 1.6f, 1f, 0.2f, 0.3f, 0f, 1f, OceanWaveBand.Ripple),
            };
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static OceanSeaStateProfile CreateSeaStateModerate(string fileName)
        {
            var profile = GetOrCreateProfile(fileName);
            profile.globalCurrentDirection = new Vector2(1f, 0.35f);
            profile.globalCurrentSpeed = 0.35f;
            profile.directionJitterDegrees = 4f;
            profile.foamIntensity = 0.4f;
            profile.crestFoamThreshold = 0.66f;
            profile.buoyancyMultiplier = 1f;
            profile.dragMultiplier = 1f;
            profile.waveImpactMultiplier = 1f;
            profile.splashFrequencyScale = 1f;
            profile.waves = new[]
            {
                Wave(new Vector2(1f, 0.1f), 0.75f, 34f, 3.5f, 1f, 0.48f, 0f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(0.3f, 1f), 0.5f, 22f, 3.0f, 1f, 0.36f, 1.1f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(1f, 0.55f), 0.24f, 11f, 2.3f, 1f, 0.3f, 0.2f, 1f, 1f, OceanWaveBand.Medium),
                Wave(new Vector2(-0.6f, 1f), 0.14f, 7.5f, 2.6f, 1f, 0.26f, 2.2f, 1f, 1f, OceanWaveBand.Medium),
                Wave(new Vector2(0.8f, 0.4f), 0.08f, 5.5f, 1.9f, 1f, 0.2f, 0.8f, 0f, 1f, OceanWaveBand.Ripple),
            };
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static OceanSeaStateProfile CreateSeaStateStorm(string fileName)
        {
            var profile = GetOrCreateProfile(fileName);
            profile.globalCurrentDirection = new Vector2(0.7f, 0.7f);
            profile.globalCurrentSpeed = 0.7f;
            profile.directionJitterDegrees = 10f;
            profile.foamIntensity = 0.85f;
            profile.crestFoamThreshold = 0.52f;
            profile.buoyancyMultiplier = 1.35f;
            profile.dragMultiplier = 1.45f;
            profile.waveImpactMultiplier = 1.55f;
            profile.splashFrequencyScale = 1.75f;
            profile.waves = new[]
            {
                Wave(new Vector2(1f, 0.3f), 1.35f, 44f, 5.2f, 1f, 0.62f, 0f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(-0.4f, 1f), 0.95f, 28f, 4.5f, 1f, 0.5f, 0.8f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(0.2f, 1f), 0.68f, 20f, 4.0f, 1f, 0.43f, 1.9f, 1f, 1f, OceanWaveBand.Large),
                Wave(new Vector2(1f, 0.5f), 0.38f, 11f, 3.2f, 1f, 0.36f, 2.5f, 1f, 1f, OceanWaveBand.Medium),
                Wave(new Vector2(-1f, 0.1f), 0.32f, 8.5f, 3.4f, 1f, 0.33f, 3.1f, 1f, 1f, OceanWaveBand.Medium),
                Wave(new Vector2(0.8f, -0.2f), 0.12f, 5f, 2.5f, 1f, 0.24f, 1.2f, 0f, 1f, OceanWaveBand.Ripple),
            };
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static OceanSeaStateProfile GetOrCreateProfile(string fileName)
        {
            string path = DataFolder + "/" + fileName;
            var profile = AssetDatabase.LoadAssetAtPath<OceanSeaStateProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<OceanSeaStateProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            return profile;
        }

        private static OceanWaveDefinition Wave(
            Vector2 dir,
            float amplitude,
            float wavelength,
            float speed,
            float frequency,
            float steepness,
            float phase,
            float physicsWeight,
            float visualWeight,
            OceanWaveBand band)
        {
            return new OceanWaveDefinition
            {
                direction = dir,
                amplitude = amplitude,
                wavelength = wavelength,
                speed = speed,
                frequency = frequency,
                steepness = steepness,
                phase = phase,
                physicsWeight = physicsWeight,
                visualWeight = visualWeight,
                band = band,
            };
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

        private static Material CreateOrUpdateOceanMaterialAsset()
        {
            EnsureFolder("Assets/Source/Environment");
            EnsureFolder("Assets/Source/Environment/Materials");
            EnsureFolder(OceanMaterialsFolder);

            Shader shader = Shader.Find(OceanShaderPath);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, OceanMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            ApplyOceanMaterialDefaults(material, shader.name == OceanShaderPath);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyOceanMaterialDefaults(Material material, bool stylizedShader)
        {
            if (material == null)
                return;

            if (stylizedShader)
            {
                SetIfExists(material, "_BaseColorDeep", new Color(0.05f, 0.32f, 0.55f, 1f));
                SetIfExists(material, "_BaseColorShallow", new Color(0.13f, 0.62f, 0.68f, 1f));
                SetIfExists(material, "_FoamColor", new Color(0.88f, 0.95f, 1f, 1f));
                SetIfExists(material, "_FoamIntensity", 0.4f);
                SetIfExists(material, "_CrestFoamThreshold", 0.62f);
                SetIfExists(material, "_ShallowDepthMax", 5.0f);
                SetIfExists(material, "_DepthFadeDistance", 8f);
                SetIfExists(material, "_TransparencyMin", 0.30f);
                SetIfExists(material, "_TransparencyMax", 0.84f);
                SetIfExists(material, "_Gloss", 0.55f);
                SetIfExists(material, "_SpecularPower", 48f);
                SetIfExists(material, "_RippleScale", 1.2f);
                SetIfExists(material, "_RippleSpeed", 0.65f);
            }
            else
            {
                material.color = new Color(0.12f, 0.43f, 0.61f, 0.9f);
            }
        }

        private static void SetIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void SetIfExists(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void WireShipScriptReferences(
            GameObject ship,
            ShipBuoyancyController buoyancy,
            ShipDeckMotionProvider motion,
            ShipWaterFxController fx,
            Rigidbody rb)
        {
            var waveField = Object.FindFirstObjectByType<OceanWaveField>();
            if (waveField == null)
                waveField = Object.FindAnyObjectByType<OceanWaveField>(FindObjectsInactive.Include);

            buoyancy.ConfigureReferences(waveField, rb);
            EditorUtility.SetDirty(buoyancy);

            if (motion != null)
            {
                motion.ConfigureReferences(buoyancy, rb);
                EditorUtility.SetDirty(motion);
            }

            if (fx != null)
            {
                fx.ConfigureReferences(buoyancy, FindFallbackSplashPrefab());
                EditorUtility.SetDirty(fx);
            }

            EditorUtility.SetDirty(ship);
        }

        private static ParticleSystem FindFallbackSplashPrefab()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab Splash", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                var ps = prefab.GetComponentInChildren<ParticleSystem>(true);
                if (ps != null)
                    return ps;
            }

            return null;
        }
    }
}

