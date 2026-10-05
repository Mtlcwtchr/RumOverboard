using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.WaterInteraction
{
    /// <summary>
    /// Makes the sea react to this hull (cosmetic, every peer — derived from the rendered transform,
    /// so kinematic proxies work exactly like the host):
    ///
    ///   • The real waterline is probed once from the hull colliders (rays from outboard at the
    ///     waterline height), giving N points per side + bow / stern tips.
    ///   • Every frame each point writes into <see cref="WaterInteractionMap"/>:
    ///       way-foam ∝ speed through the water (strongest at the bow),
    ///       immersion foam when the hull slams down / a wave climbs its side,
    ///       bow wave (raises the surface) and quarter trough (lowers it) — the map's wave solver
    ///       turns these into ripples that run off the hull,
    ///     plus a turbulent wake behind the stern. Foam lingers and spreads → real wake trails.
    ///   • Hard slams at the bow / quarters throw a splash (pooled particle prefab), and at speed
    ///     the bow throws spray.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipWaterInteractor : MonoBehaviour
    {
        [SerializeField] private WaterInteractionConfig config;
        [SerializeField] private OceanWaveField waveField;
        [Tooltip("Ship-local height of the design waterline.")]
        [SerializeField] private float waterlineY;

        private struct Probe
        {
            public Vector3 Local;
            public Vector3 OutwardLocal;
            public float ZNorm;     // -1 stern … +1 bow
            public Vector3 PrevWorld;
            public float PrevSub;
            public bool Has;
        }

        private Probe[] _probes = System.Array.Empty<Probe>();
        private Vector3 _bowLocal;
        private Vector3 _sternLocal;
        private float _halfBeam = 2f;
        private Vector3 _prevPos;
        private bool _hasPrev;
        private float _nextSplash;
        private float _nextSpray;
        private bool _built;

        private readonly List<GameObject> _splashPool = new();
        private int _splashNext;
        private Transform _splashRoot;

        public WaterInteractionConfig Config => config != null ? config : WaterInteractionConfig.Active;
        public int ProbeCount => _probes.Length;

        public void Configure(WaterInteractionConfig cfg, OceanWaveField field)
        {
            if (cfg != null) config = cfg;
            if (field != null) waveField = field;
        }

        private void Start()
        {
            if (config != null)
                WaterInteractionConfig.Active = config;
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            WaterInteractionMap.Ensure(config);
            BuildProbes();
        }

        private void OnDestroy()
        {
            if (_splashRoot != null)
                Destroy(_splashRoot.gameObject);
        }

        /// <summary>Re-probe the waterline (after the hull changed).</summary>
        public void BuildProbes()
        {
            var colliders = new List<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger)
                    colliders.Add(c);
            _built = true;
            if (colliders.Count == 0)
                return;

            // Ship-local extents of the solid geometry around the waterline.
            Bounds b = default;
            bool any = false;
            foreach (Collider c in colliders)
            {
                Bounds w = c.bounds;
                Vector3 lmin = transform.InverseTransformPoint(w.min);
                Vector3 lmax = transform.InverseTransformPoint(w.max);
                var local = new Bounds((lmin + lmax) * 0.5f, Vector3.zero);
                local.Encapsulate(lmin);
                local.Encapsulate(lmax);
                if (local.min.y > waterlineY + 0.6f || local.max.y < waterlineY - 0.6f)
                    continue;
                if (!any) { b = local; any = true; }
                else b.Encapsulate(local);
            }
            if (!any)
                return;

            Physics.SyncTransforms();
            int n = Mathf.Max(3, Config.SamplesPerSide);
            float reach = b.extents.x + 4f;
            var list = new List<Probe>(n * 2);
            float maxHalf = 0.5f;
            for (int k = 0; k < n; k++)
            {
                float z = Mathf.Lerp(b.min.z + b.size.z * 0.04f, b.max.z - b.size.z * 0.02f, k / (float)(n - 1));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 origin = new Vector3(b.center.x + side * reach, waterlineY + 0.05f, z);
                    if (!CastLocal(colliders, origin, new Vector3(-side, 0f, 0f), reach * 2f, out Vector3 hit, out Vector3 normal))
                        continue;
                    Vector3 outward = new Vector3(normal.x, 0f, normal.z);
                    if (outward.sqrMagnitude < 0.01f || Mathf.Sign(outward.x) != side)
                        outward = new Vector3(side, 0f, 0f);
                    list.Add(new Probe
                    {
                        Local = hit,
                        OutwardLocal = outward.normalized,
                        ZNorm = Mathf.Clamp((z - b.center.z) / Mathf.Max(0.5f, b.extents.z), -1f, 1f),
                    });
                    maxHalf = Mathf.Max(maxHalf, Mathf.Abs(hit.x - b.center.x));
                }
            }

            _halfBeam = maxHalf;
            _bowLocal = CastLocal(colliders, new Vector3(b.center.x, waterlineY + 0.05f, b.max.z + 4f), Vector3.back, b.size.z + 8f, out Vector3 bow, out _)
                ? bow : new Vector3(b.center.x, waterlineY, b.max.z);
            _sternLocal = CastLocal(colliders, new Vector3(b.center.x, waterlineY + 0.05f, b.min.z - 4f), Vector3.forward, b.size.z + 8f, out Vector3 stern, out _)
                ? stern : new Vector3(b.center.x, waterlineY, b.min.z);
            list.Add(new Probe { Local = _bowLocal, OutwardLocal = Vector3.forward, ZNorm = 1f });
            _probes = list.ToArray();
        }

        private bool CastLocal(List<Collider> colliders, Vector3 originLocal, Vector3 dirLocal, float distance,
            out Vector3 hitLocal, out Vector3 normalLocal)
        {
            var ray = new Ray(transform.TransformPoint(originLocal), transform.TransformDirection(dirLocal));
            float best = float.MaxValue;
            hitLocal = normalLocal = Vector3.zero;
            foreach (Collider c in colliders)
            {
                if (c is MeshCollider { convex: false } mc && mc.sharedMesh == null)
                    continue;
                if (c.Raycast(ray, out RaycastHit hit, distance) && hit.distance < best)
                {
                    best = hit.distance;
                    hitLocal = transform.InverseTransformPoint(hit.point);
                    normalLocal = transform.InverseTransformDirection(hit.normal);
                }
            }
            return best < float.MaxValue;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 1e-5f)
                return;
            if (!_built)
                BuildProbes();
            if (WaterInteractionMap.Instance == null)
                WaterInteractionMap.Ensure(config);
            if (waveField == null)
            {
                waveField = FindAnyObjectByType<OceanWaveField>();
                if (waveField == null) return;
            }

            WaterInteractionConfig cfg = Config;
            Vector3 pos = transform.position;
            Vector3 vel = _hasPrev ? (pos - _prevPos) / dt : Vector3.zero;
            _prevPos = pos;
            _hasPrev = true;
            if (vel.sqrMagnitude > 900f) vel = Vector3.zero; // teleport / spawn

            Vector3 planar = new Vector3(vel.x, 0f, vel.z);
            float speed = planar.magnitude;
            float range = Mathf.Max(0.1f, cfg.FullSpeed - cfg.MinSpeed);
            float speed01 = Mathf.Clamp01((speed - cfg.MinSpeed) / range);
            Vector3 fwd = transform.forward;
            Vector2 moveDir = speed > 0.05f ? new Vector2(planar.x, planar.z) / speed : new Vector2(fwd.x, fwd.z);

            for (int i = 0; i < _probes.Length; i++)
            {
                ref Probe p = ref _probes[i];
                Vector3 world = transform.TransformPoint(p.Local);
                Vector3 pVel = p.Has ? (world - p.PrevWorld) / dt : vel;
                if (pVel.sqrMagnitude > 900f) pVel = vel;
                float waterY = waveField.SampleHeight(world);
                float sub = waterY - world.y; // + = waterline point under the surface
                float immersion = p.Has ? (sub - p.PrevSub) / dt : 0f;
                p.PrevWorld = world;
                p.PrevSub = sub;
                p.Has = true;

                float contact = Mathf.Clamp01((sub + 1.2f) / 0.6f);
                if (contact <= 0f)
                    continue; // hull lifted clear of the sea here

                Vector3 pPlanar = new Vector3(pVel.x, 0f, pVel.z);
                float way = Mathf.Clamp01((pPlanar.magnitude - cfg.MinSpeed) / range);
                float bow = Mathf.Clamp01((p.ZNorm - 0.25f) / 0.75f);
                float stern = Mathf.Clamp01((-p.ZNorm - 0.45f) / 0.55f);

                float foam = cfg.HullFoam * way * (0.3f + 0.7f * Mathf.Max(bow, stern * 0.8f))
                             + cfg.ImmersionFoam * Mathf.Max(0f, immersion);
                float height = cfg.BowWave * way * way * bow - cfg.SternTrough * way * (stern + 0.25f * (1f - bow));
                float radius = cfg.HullFoamRadius * (1f + way * 0.6f + bow * 0.5f);
                Vector3 outward = transform.TransformDirection(p.OutwardLocal);
                Vector3 at = world + outward * (radius * 0.35f);
                at.y = waterY;
                Vector2 dir = pPlanar.sqrMagnitude > 0.01f ? new Vector2(pPlanar.x, pPlanar.z) : moveDir;
                WaterInteractionMap.Stamp(at, dir, radius, 1f + way * 1.5f, foam * contact, height);

                if (immersion > cfg.SplashMinImmersion && Time.time >= _nextSplash && (bow > 0.2f || stern > 0.2f || way > 0.5f))
                {
                    _nextSplash = Time.time + cfg.SplashCooldown;
                    Splash(at, cfg.SplashScale * Mathf.Clamp(immersion / cfg.SplashMinImmersion, 1f, 2.5f));
                }
            }

            // Turbulent wake shed behind the stern.
            if (speed01 > 0.001f)
            {
                Vector3 stern = transform.TransformPoint(_sternLocal);
                stern.y = waveField.SampleHeight(stern);
                float wakeR = Mathf.Max(0.6f, _halfBeam * cfg.WakeWidth) * (0.8f + 0.6f * speed01);
                Vector3 back = -new Vector3(moveDir.x, 0f, moveDir.y);
                WaterInteractionMap.Stamp(stern + back * (wakeR * 0.5f), moveDir, wakeR, 1.6f, cfg.WakeFoam * speed01, -cfg.SternTrough * 0.5f * speed01);
                WaterInteractionMap.Stamp(stern + back * (wakeR * 1.6f), moveDir, wakeR * 0.8f, 1.3f, cfg.WakeFoam * 0.75f * speed01, 0f);

                // Spray off the stem at speed.
                float fwdSpeed = Vector3.Dot(vel, fwd);
                if (fwdSpeed > cfg.BowSpraySpeed && Time.time >= _nextSpray && cfg.SplashPrefab != null)
                {
                    _nextSpray = Time.time + Mathf.Lerp(1.4f, 0.45f, Mathf.Clamp01((fwdSpeed - cfg.BowSpraySpeed) / 4f));
                    Vector3 bowW = transform.TransformPoint(_bowLocal);
                    bowW.y = waveField.SampleHeight(bowW);
                    Splash(bowW, cfg.SplashScale * 0.6f);
                }
            }
        }

        private void Splash(Vector3 at, float scale)
        {
            GameObject prefab = Config.SplashPrefab;
            if (prefab == null)
                return;
            if (_splashRoot == null)
                _splashRoot = new GameObject($"{name}_Splashes").transform;

            GameObject fx;
            if (_splashPool.Count < 6)
            {
                fx = Instantiate(prefab, _splashRoot);
                _splashPool.Add(fx);
            }
            else
            {
                fx = _splashPool[_splashNext];
                _splashNext = (_splashNext + 1) % _splashPool.Count;
            }
            if (fx == null)
                return;

            fx.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            fx.transform.localScale = Vector3.one * scale;
            fx.SetActive(true);
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(false);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            foreach (Probe p in _probes)
            {
                Vector3 w = transform.TransformPoint(p.Local);
                Gizmos.DrawWireSphere(w, 0.15f);
                Gizmos.DrawLine(w, w + transform.TransformDirection(p.OutwardLocal) * 0.6f);
            }
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.TransformPoint(_bowLocal), 0.3f);
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.TransformPoint(_sternLocal), 0.3f);
        }
#endif
    }
}

