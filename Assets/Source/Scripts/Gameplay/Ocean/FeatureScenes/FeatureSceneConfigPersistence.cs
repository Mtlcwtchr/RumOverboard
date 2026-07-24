using UnityEngine;
using RumOverboard.Gameplay.Ocean.FeatureScenes.Sails;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    public static class FeatureSceneConfigPersistence
    {
        public const string HullConfigAssetPath = "Assets/Source/Configs/FeatureScenes/HullFeatureConfig.asset";
        public const string WheelConfigAssetPath = "Assets/Source/Configs/FeatureScenes/WheelFeatureConfig.asset";
        public const string SailConfigAssetPath = "Assets/Source/Configs/FeatureScenes/SailFeatureConfig.asset";

#if UNITY_EDITOR
        public static HullFeatureConfig LoadOrCreateHullConfig()
        {
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<HullFeatureConfig>(HullConfigAssetPath);
            if (cfg != null)
                return cfg;

            EnsureFolder("Assets/Source/Configs");
            EnsureFolder("Assets/Source/Configs/FeatureScenes");
            cfg = ScriptableObject.CreateInstance<HullFeatureConfig>();
            UnityEditor.AssetDatabase.CreateAsset(cfg, HullConfigAssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return cfg;
        }

        public static WheelFeatureConfig LoadOrCreateWheelConfig()
        {
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<WheelFeatureConfig>(WheelConfigAssetPath);
            if (cfg != null)
                return cfg;

            EnsureFolder("Assets/Source/Configs");
            EnsureFolder("Assets/Source/Configs/FeatureScenes");
            cfg = ScriptableObject.CreateInstance<WheelFeatureConfig>();
            UnityEditor.AssetDatabase.CreateAsset(cfg, WheelConfigAssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return cfg;
        }

        public static SailFeatureConfig LoadOrCreateSailConfig()
        {
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<SailFeatureConfig>(SailConfigAssetPath);
            if (cfg != null)
                return cfg;

            EnsureFolder("Assets/Source/Configs");
            EnsureFolder("Assets/Source/Configs/FeatureScenes");
            cfg = ScriptableObject.CreateInstance<SailFeatureConfig>();
            UnityEditor.AssetDatabase.CreateAsset(cfg, SailConfigAssetPath);
            UnityEditor.AssetDatabase.SaveAssets();
            return cfg;
        }

        public static void SaveAsset(ScriptableObject asset)
        {
            if (asset == null)
                return;

            UnityEditor.EditorUtility.SetDirty(asset);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(next))
                    UnityEditor.AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
#else
        public static HullFeatureConfig LoadOrCreateHullConfig() => null;
        public static WheelFeatureConfig LoadOrCreateWheelConfig() => null;
        public static SailFeatureConfig LoadOrCreateSailConfig() => null;
        public static void SaveAsset(ScriptableObject asset) { }
#endif
    }
}

