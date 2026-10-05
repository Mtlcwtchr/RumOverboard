using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Builds stylised foam particle systems at runtime with NO project assets — a soft radial
    /// dot texture and an unlit alpha-blended material are generated in code, so bow spray, wake
    /// and impact splashes work on any ship without authoring prefabs/textures.
    /// Shared texture/material are cached statically.
    /// </summary>
    public static class FoamParticleFactory
    {
        private static Texture2D _softDot;
        private static Material _foamMaterial;

        public static Texture2D SoftDot => _softDot != null ? _softDot : (_softDot = CreateSoftDotTexture(64));
        public static Material FoamMaterial => _foamMaterial != null ? _foamMaterial : (_foamMaterial = CreateFoamMaterial());

        public static Texture2D CreateSoftDotTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FoamSoftDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float r = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // smoothstep — soft round edge
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static Material CreateFoamMaterial()
        {
            // Sprites/Default is always present, unlit, alpha-blended, and multiplies by particle color.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            var mat = new Material(shader) { name = "FoamRuntimeMaterial" };
            if (mat.HasProperty("_MainTex"))
                mat.mainTexture = SoftDot;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", SoftDot);
            return mat;
        }

        /// <summary>
        /// Creates a world-space, manually-emitted foam system (call ParticleSystem.Emit yourself):
        /// zero gravity/speed, soft alpha fade in/out, gentle growth over life.
        /// </summary>
        public static ParticleSystem CreateFoamSystem(Transform parent, string systemName, Color color, float startSize, float lifetime, int maxParticles = 2000, float gravity = 0f)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // wake stays put as the ship moves on
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = startSize;
            main.startColor = color;
            main.gravityModifier = gravity; // splashes fall back into the sea; surface foam floats (0)
            main.maxParticles = maxParticles;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // we drive it with Emit()

            var shape = ps.shape;
            shape.enabled = false;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = gradient;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.4f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = FoamMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortingFudge = -5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }
    }
}
