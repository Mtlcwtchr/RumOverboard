#if FUSION2
using System.Collections.Generic;
using RumOverboard.Gameplay.Attributes;
using RumOverboard.Gameplay.Items;
using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>
    /// Fire presentation on every peer, driven only by replicated state (no RPCs):
    ///   Burning flag → flames + smoke + a flickering light on the item (FlammableData.FireVfx replaces the
    ///   procedural flames when set); Burning → off while the item is intact → a puff of steam;
    ///   Broken + Exploded → explosion (fireball, smoke, flash); bucket Water going down → splash.
    /// </summary>
    [DefaultExecutionOrder(1200)]
    public sealed class FireVisuals : MonoBehaviour
    {
        private const int MaxLights = 8;

        [SerializeField] private FireConfig config;
        [Tooltip("Additive particle material (URP Particles/Unlit). Created at runtime if empty.")]
        [SerializeField] private Material flameMaterial;
        [Tooltip("Alpha-blended particle material for smoke / steam / water. Created at runtime if empty.")]
        [SerializeField] private Material smokeMaterial;

        private sealed class Flame
        {
            public GameObject Root;
            public Light Light;
            public float Seed;
        }

        private readonly Dictionary<WorldItem, Flame> _flames = new();
        private readonly Dictionary<WorldItem, float> _water = new();
        private readonly HashSet<WorldItem> _exploded = new();
        private readonly List<WorldItem> _gone = new();
        private Texture2D _soft;

        /// <summary>Counters for tests / the debug window (this peer).</summary>
        public int FlamesShown => _flames.Count;
        public int ExplosionsShown { get; private set; }
        public int SplashesShown { get; private set; }
        public int SteamShown { get; private set; }

        public static FireVisuals Instance { get; private set; }

        private FireConfig Config => FireDirector.Instance != null ? FireDirector.Instance.Config
                                   : config != null ? config : config = FireConfig.CreateDefault();

        private void Awake()
        {
            Instance = this;
            EnsureMaterials();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            int lights = 0;
            foreach (WorldItem item in WorldItem.All)
            {
                if (item == null || item.Object == null || !item.Object.IsValid || item.Attributes.Set == null) continue;
                AttributesComponent a = item.Attributes;

                if (item.Broken)
                {
                    if (a.HasFlag(AttrFlag.Exploded) && _exploded.Add(item))
                        Explosion(item.WorldBounds);
                    RemoveFlame(item, steam: false);
                    continue;
                }

                bool burning = a.HasFlag(AttrFlag.Burning) && !item.IsStored;
                if (burning)
                {
                    Flame f = _flames.TryGetValue(item, out Flame existing) ? existing : _flames[item] = CreateFlame(item);
                    Animate(f, item, lights++ < MaxLights);
                }
                else RemoveFlame(item, steam: !item.IsStored);

                if (a.GetData<BucketData>() is { } bucket && a.Has(Attr.Water))
                {
                    float w = a.GetCurrent(Attr.Water);
                    if (_water.TryGetValue(item, out float before) && w < before - 0.01f)
                        Splash(item, bucket);
                    _water[item] = w;
                }
            }

            _gone.Clear();
            foreach (WorldItem w in _flames.Keys) if (w == null || w.Object == null || !w.Object.IsValid) _gone.Add(w);
            foreach (WorldItem w in _water.Keys) if (w == null || w.Object == null || !w.Object.IsValid) _gone.Add(w);
            foreach (WorldItem w in _exploded) if (w == null || w.Object == null || !w.Object.IsValid) _gone.Add(w);
            foreach (WorldItem w in _gone)
            {
                if (_flames.TryGetValue(w, out Flame f) && f.Root != null) Destroy(f.Root);
                _flames.Remove(w);
                _water.Remove(w);
                _exploded.Remove(w);
            }
        }

        // ───────────────────────── flames ─────────────────────────

        private Flame CreateFlame(WorldItem item)
        {
            Bounds b = item.WorldBounds;
            float size = Mathf.Clamp(b.extents.magnitude, 0.15f, 1.5f) * Config.FlameSize;
            var root = new GameObject("Fire");
            root.transform.SetParent(item.transform, false);
            root.transform.position = b.center;

            FlammableData data = item.Attributes.GetData<FlammableData>();
            if (data != null && data.FireVfx != null)
                Instantiate(data.FireVfx, root.transform, false);
            else
            {
                Particles(root.transform, "Flames", flameMaterial, loop: true, rate: 28f * size + 8f, life: (0.35f, 0.7f),
                          speed: (0.6f, 1.6f), size: (0.35f * size, 0.8f * size), shape: Vector3.Scale(b.extents, new Vector3(1.2f, 0.6f, 1.2f)),
                          start: new Color(1f, 0.75f, 0.25f, 0.9f), end: new Color(0.9f, 0.18f, 0.05f, 0f), gravity: -0.4f);
                Particles(root.transform, "Smoke", smokeMaterial, loop: true, rate: 7f * size + 3f, life: (1.6f, 2.8f),
                          speed: (0.8f, 1.5f), size: (0.5f * size, 1.4f * size), shape: b.extents * 0.6f,
                          start: new Color(0.15f, 0.13f, 0.12f, 0.55f), end: new Color(0.3f, 0.3f, 0.3f, 0f), gravity: -0.15f,
                          offset: Vector3.up * (b.extents.y + 0.3f));
            }

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * (b.extents.y + 0.2f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.shadows = LightShadows.None;
            return new Flame { Root = root, Light = light, Seed = Random.value * 100f };
        }

        private void Animate(Flame f, WorldItem item, bool lit)
        {
            if (f.Root == null) return;
            f.Root.transform.position = item.WorldBounds.center;
            f.Root.transform.rotation = Quaternion.identity;
            FireConfig cfg = Config;
            f.Light.enabled = lit;
            float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(f.Seed, Time.time * 7f);
            f.Light.intensity = cfg.FlameLightIntensity * flicker;
            f.Light.range = cfg.FlameLightRange;
        }

        private void RemoveFlame(WorldItem item, bool steam)
        {
            if (!_flames.TryGetValue(item, out Flame f)) return;
            _flames.Remove(item);
            if (f.Root == null) return;
            Bounds b = item.WorldBounds;
            Destroy(f.Root);
            if (steam)
            {
                SteamShown++;
                Burst(b.center + Vector3.up * b.extents.y, "Steam", smokeMaterial, 30, (1f, 1.8f), (1f, 2.5f), (0.4f, 1.1f),
                      b.extents, new Color(0.9f, 0.9f, 0.92f, 0.6f), new Color(1f, 1f, 1f, 0f), -0.3f, 3f);
            }
        }

        // ───────────────────────── one-shots ─────────────────────────

        private void Explosion(Bounds b)
        {
            ExplosionsShown++;
            Vector3 c = b.center;
            Burst(c, "Fireball", flameMaterial, 60, (0.35f, 0.8f), (3f, 9f), (0.6f, 1.6f), b.extents,
                  new Color(1f, 0.85f, 0.4f, 1f), new Color(0.8f, 0.15f, 0.02f, 0f), 0.2f, 2f);
            Burst(c, "BlastSmoke", smokeMaterial, 40, (2f, 4f), (1.5f, 4.5f), (1.2f, 2.8f), b.extents,
                  new Color(0.12f, 0.11f, 0.1f, 0.8f), new Color(0.35f, 0.35f, 0.35f, 0f), -0.1f, 5f);

            var lightGo = new GameObject("BlastLight");
            lightGo.transform.position = c;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.65f, 0.3f);
            light.range = 18f;
            light.intensity = Config.ExplosionLightIntensity;
            lightGo.AddComponent<FadeLight>().Duration = 0.6f;
        }

        private void Splash(WorldItem bucket, BucketData data)
        {
            SplashesShown++;
            Vector3 origin = bucket.transform.position;
            Vector3 centre = FireRules.SplashCentre(origin, bucket.transform.forward, data.SplashDistance * 0.6f);
            Burst(centre, "Splash", smokeMaterial, 70, (0.5f, 1f), (2f, 5f), (0.08f, 0.2f),
                  new Vector3(data.SplashRadius * 0.5f, 0.1f, data.SplashRadius * 0.5f),
                  new Color(0.75f, 0.88f, 1f, 0.9f), new Color(0.75f, 0.88f, 1f, 0f), 1.2f, 2f);
        }

        // ───────────────────────── particle helpers ─────────────────────────

        private void Burst(Vector3 at, string label, Material mat, int count, (float, float) life, (float, float) speed,
                           (float, float) size, Vector3 shape, Color start, Color end, float gravity, float destroyAfter)
        {
            var go = new GameObject(label);
            go.transform.position = at;
            ParticleSystem ps = Particles(go.transform, label, mat, loop: false, rate: 0f, life, speed, size, shape, start, end, gravity);
            ps.Emit(count);
            Destroy(go, destroyAfter + life.Item2);
        }

        private ParticleSystem Particles(Transform parent, string label, Material mat, bool loop, float rate,
                                         (float min, float max) life, (float min, float max) speed, (float min, float max) size,
                                         Vector3 shape, Color start, Color end, float gravity, Vector3 offset = default)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up); // emit upwards (cone axis = +Z)
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = loop;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.min, life.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = start;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.playOnAwake = loop;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = loop ? 12f : 70f;
            sh.radius = Mathf.Max(0.05f, Mathf.Max(shape.x, shape.z));
            sh.radiusThickness = 1f;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(start.a, 0.12f), new GradientAlphaKey(end.a, 1f) });
            col.color = g;

            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, loop ? 0.3f : 1.6f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        private void EnsureMaterials()
        {
            if (flameMaterial != null && smokeMaterial != null) return;
            _soft = SoftTexture();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (flameMaterial == null) flameMaterial = MakeMaterial(shader, additive: true);
            if (smokeMaterial == null) smokeMaterial = MakeMaterial(shader, additive: false);
        }

        private Material MakeMaterial(Shader shader, bool additive)
        {
            var m = new Material(shader) { name = additive ? "Fire (runtime)" : "Smoke (runtime)" };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", _soft);
            m.mainTexture = _soft;
            if (m.HasProperty("_Surface")) // URP particles: transparent, additive / alpha blend
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (additive) m.EnableKeyword("_BLENDMODE_ADD");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }

        /// <summary>Radial soft dot (particles need some texture; keeps the feature free of art assets).</summary>
        public static Texture2D SoftTexture(int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
#endif
