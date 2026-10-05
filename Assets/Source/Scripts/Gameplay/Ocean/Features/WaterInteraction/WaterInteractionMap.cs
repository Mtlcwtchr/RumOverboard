using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.WaterInteraction
{
    /// <summary>
    /// Owns the GPU interaction map the ocean shader reads (foam + hull-driven displacement waves).
    /// Purely cosmetic and LOCAL: every peer runs its own map from the ships it renders, so nothing
    /// here is networked. The map is a square of <see cref="WaterInteractionConfig.MapSize"/> metres
    /// centred on the camera (snapped to whole texels so re-centring never blurs it).
    ///
    /// Writers call <see cref="Stamp"/> during the frame (any number of ships); the map advances
    /// once per frame in LateUpdate and publishes globals:
    ///   _WaterInteractionTex   (R foam, G height, B vertical velocity)
    ///   _WaterInteractionRect  (minX, minZ, 1/size, enabled)
    ///   _WaterInteractionParams(displacement scale, normal strength, texel size m, 0)
    /// </summary>
    [DefaultExecutionOrder(900)] // after ships/players moved, before the camera renders
    public sealed class WaterInteractionMap : MonoBehaviour
    {
        private const int MaxStamps = 64;

        private static readonly int PropTex = Shader.PropertyToID("_WaterInteractionTex");
        private static readonly int PropRect = Shader.PropertyToID("_WaterInteractionRect");
        private static readonly int PropParams = Shader.PropertyToID("_WaterInteractionParams");
        private static readonly int PropShift = Shader.PropertyToID("_Shift");
        private static readonly int PropSimRect = Shader.PropertyToID("_Rect");
        private static readonly int PropDt = Shader.PropertyToID("_Dt");
        private static readonly int PropFoamDecay = Shader.PropertyToID("_FoamDecay");
        private static readonly int PropFoamSpread = Shader.PropertyToID("_FoamSpread");
        private static readonly int PropWaveC2 = Shader.PropertyToID("_WaveC2");
        private static readonly int PropWaveDamping = Shader.PropertyToID("_WaveDamping");
        private static readonly int PropHeightRestore = Shader.PropertyToID("_HeightRestore");
        private static readonly int PropStampPush = Shader.PropertyToID("_StampPush");
        private static readonly int PropStampCount = Shader.PropertyToID("_StampCount");
        private static readonly int PropStampA = Shader.PropertyToID("_StampA");
        private static readonly int PropStampB = Shader.PropertyToID("_StampB");

        private static WaterInteractionMap _instance;

        [SerializeField] private WaterInteractionConfig config;
        [Tooltip("What the map follows. Falls back to the main camera.")]
        [SerializeField] private Transform follow;

        private RenderTexture _a, _b;
        private Material _sim;
        private Vector2 _min;
        private bool _hasMin;
        private int _resolution;
        private float _size;
        private int _stampCount;
        private readonly Vector4[] _stampA = new Vector4[MaxStamps];
        private readonly Vector4[] _stampB = new Vector4[MaxStamps];
        private readonly List<Vector4> _pendingA = new(MaxStamps * 2);
        private readonly List<Vector4> _pendingB = new(MaxStamps * 2);

        public static WaterInteractionMap Instance => _instance;
        public RenderTexture Texture => _a;
        public int StampsLastFrame { get; private set; }
        public WaterInteractionConfig Config => config != null ? config : WaterInteractionConfig.Active;

        /// <summary>Find or create the scene's map (first writer creates it).</summary>
        public static WaterInteractionMap Ensure(WaterInteractionConfig cfg = null)
        {
            if (_instance != null)
            {
                if (cfg != null && _instance.config == null) _instance.config = cfg;
                return _instance;
            }
            _instance = FindAnyObjectByType<WaterInteractionMap>();
            if (_instance == null)
            {
                var go = new GameObject("WaterInteractionMap");
                _instance = go.AddComponent<WaterInteractionMap>();
            }
            if (cfg != null && _instance.config == null) _instance.config = cfg;
            return _instance;
        }

        /// <summary>
        /// Write into the map this frame: an ellipse at <paramref name="world"/> (XZ) with
        /// <paramref name="radius"/> across and radius×<paramref name="stretch"/> along
        /// <paramref name="dirXZ"/>. Foam is max-blended; height pulls the surface toward
        /// <paramref name="height"/> (m, relative to the waves).
        /// </summary>
        public static void Stamp(Vector3 world, Vector2 dirXZ, float radius, float stretch, float foam, float height)
        {
            if (_instance == null || radius <= 0.01f)
                return;
            if (dirXZ.sqrMagnitude < 1e-6f) dirXZ = Vector2.up;
            else dirXZ.Normalize();
            _instance.Queue(new Vector4(world.x, world.z, radius, Mathf.Clamp01(foam)),
                new Vector4(dirXZ.x, dirXZ.y, Mathf.Max(0.2f, stretch), height));
        }

        private void Queue(Vector4 a, Vector4 b)
        {
            _pendingA.Add(a);
            _pendingB.Add(b);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Release();
            Shader.SetGlobalVector(PropRect, Vector4.zero);
        }

        private void OnDisable() => Shader.SetGlobalVector(PropRect, Vector4.zero);

        /// <summary>Clear foam + waves (debug reset).</summary>
        public void Clear()
        {
            if (_a != null) Graphics.Blit(Texture2D.blackTexture, _a);
            if (_b != null) Graphics.Blit(Texture2D.blackTexture, _b);
        }

        private void Release()
        {
            if (_a != null) { _a.Release(); Destroy(_a); _a = null; }
            if (_b != null) { _b.Release(); Destroy(_b); _b = null; }
            if (_sim != null) { Destroy(_sim); _sim = null; }
        }

        private bool EnsureResources(WaterInteractionConfig cfg)
        {
            int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(64, cfg.Resolution)), 64, 2048);
            if (_a != null && _resolution == res && _sim != null)
                return true;

            Release();
            Shader shader = cfg.SimShader != null ? cfg.SimShader : Shader.Find("Hidden/RumOverboard/WaterInteractionSim");
            if (shader == null || !shader.isSupported)
                return false;
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                return false;

            _sim = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _a = Create(res, "WaterInteraction_A");
            _b = Create(res, "WaterInteraction_B");
            _resolution = res;
            _hasMin = false;
            Clear();
            return true;
        }

        private static RenderTexture Create(int res, string name)
        {
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            rt.Create();
            return rt;
        }

        private void LateUpdate()
        {
            WaterInteractionConfig cfg = Config;
            if (!EnsureResources(cfg))
            {
                Shader.SetGlobalVector(PropRect, Vector4.zero);
                _pendingA.Clear();
                _pendingB.Clear();
                return;
            }

            Transform target = follow != null ? follow : (Camera.main != null ? Camera.main.transform : null);
            Vector3 centre = target != null ? target.position : Vector3.zero;

            _size = Mathf.Max(16f, cfg.MapSize);
            float texel = _size / _resolution;
            Vector2 min = new Vector2(
                Mathf.Floor((centre.x - _size * 0.5f) / texel) * texel,
                Mathf.Floor((centre.z - _size * 0.5f) / texel) * texel);
            Vector2 shift = _hasMin ? (min - _min) / _size : Vector2.zero;
            if (!_hasMin || Mathf.Abs(shift.x) >= 1f || Mathf.Abs(shift.y) >= 1f)
            {
                Clear();
                shift = Vector2.zero;
            }
            _min = min;
            _hasMin = true;

            // This frame's stamps (keep the strongest if over budget).
            _stampCount = Mathf.Min(_pendingA.Count, MaxStamps);
            if (_pendingA.Count > MaxStamps)
            {
                int stride = Mathf.CeilToInt(_pendingA.Count / (float)MaxStamps);
                _stampCount = 0;
                for (int i = 0; i < _pendingA.Count && _stampCount < MaxStamps; i += stride, _stampCount++)
                {
                    _stampA[_stampCount] = _pendingA[i];
                    _stampB[_stampCount] = _pendingB[i];
                }
            }
            else
            {
                for (int i = 0; i < _stampCount; i++)
                {
                    _stampA[i] = _pendingA[i];
                    _stampB[i] = _pendingB[i];
                }
            }
            for (int i = _stampCount; i < MaxStamps; i++)
            {
                _stampA[i] = Vector4.zero;
                _stampB[i] = Vector4.zero;
            }
            StampsLastFrame = _stampCount;
            _pendingA.Clear();
            _pendingB.Clear();

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 1f / 30f);
            float c = Mathf.Min(cfg.WaveSpeed, 0.65f * texel / Mathf.Max(1e-4f, dt)); // stay inside the CFL limit
            _sim.SetVector(PropShift, shift);
            _sim.SetVector(PropSimRect, new Vector4(_min.x, _min.y, _size, 1f / _size));
            _sim.SetFloat(PropDt, dt);
            _sim.SetFloat(PropFoamDecay, cfg.FoamDecay);
            _sim.SetFloat(PropFoamSpread, cfg.FoamSpread);
            _sim.SetFloat(PropWaveC2, (c / texel) * (c / texel));
            _sim.SetFloat(PropWaveDamping, cfg.WaveDamping);
            _sim.SetFloat(PropHeightRestore, cfg.HeightRestore);
            _sim.SetFloat(PropStampPush, cfg.StampPush);
            _sim.SetInt(PropStampCount, _stampCount);
            _sim.SetVectorArray(PropStampA, _stampA);
            _sim.SetVectorArray(PropStampB, _stampB);

            Graphics.Blit(_a, _b, _sim, 0);
            (_a, _b) = (_b, _a);

            Shader.SetGlobalTexture(PropTex, _a);
            Shader.SetGlobalVector(PropRect, new Vector4(_min.x, _min.y, 1f / _size, 1f));
            Shader.SetGlobalVector(PropParams, new Vector4(cfg.DisplacementScale, cfg.NormalStrength, texel, 0f));
        }
    }
}

