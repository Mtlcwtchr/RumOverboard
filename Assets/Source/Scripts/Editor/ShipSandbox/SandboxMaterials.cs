using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>Shared URP Lit material assets for generated sandbox props (stairs, pin rails, blocks).</summary>
    public static class SandboxMaterials
    {
        private const string Folder = "Assets/Source/Environment/Materials/Sandbox";

        public static Material Wood() => Get("SB_Wood", new Color(0.45f, 0.30f, 0.17f), 0.25f, 0f);
        public static Material DarkWood() => Get("SB_DarkWood", new Color(0.26f, 0.16f, 0.09f), 0.3f, 0f);
        public static Material HullPaint() => Get("SB_HullTar", new Color(0.11f, 0.075f, 0.055f), 0.35f, 0f);
        public static Material Brass() => Get("SB_Brass", new Color(0.78f, 0.6f, 0.25f), 0.65f, 0.9f);
        public static Material Rope() => Get("SB_Rope", new Color(0.62f, 0.5f, 0.32f), 0.1f, 0f);
        public static Material Highlight() => Get("SB_StationMarker", new Color(1f, 0.82f, 0.3f), 0.4f, 0f, emission: true);

        /// <summary>3D-text material that respects depth (GUI/Text Shader defaults to "always on top").</summary>
        public static Material LabelText(Font font)
        {
            string path = $"{Folder}/SB_LabelText.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (font == null || font.material == null) return null;
                mat = new Material(font.material) { name = "SB_LabelText" };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (font != null) mat.mainTexture = font.material.mainTexture;
            mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material Get(string name, Color color, float smoothness, float metallic, bool emission = false)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Source/Environment/Materials"))
                    AssetDatabase.CreateFolder("Assets/Source/Environment", "Materials");
                AssetDatabase.CreateFolder("Assets/Source/Environment/Materials", "Sandbox");
            }

            string path = $"{Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (emission && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.6f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
