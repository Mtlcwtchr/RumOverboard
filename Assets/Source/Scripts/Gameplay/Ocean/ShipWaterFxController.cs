using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public class ShipWaterFxController : MonoBehaviour
    {
        [SerializeField] private ShipBuoyancyController buoyancy;
        [SerializeField] private ParticleSystem splashPrefab;
        [SerializeField] private int poolSize = 12;

        [Header("Thresholds")]
        [SerializeField] private float impactThreshold = 3f;
        [SerializeField] private float minInterval = 0.08f;

        private readonly Queue<ParticleSystem> _pool = new Queue<ParticleSystem>(16);
        private float _nextEmitTime;
        private ParticleSystem _procedural; // asset-free splash used when no splashPrefab is assigned

        public void ConfigureReferences(ShipBuoyancyController buoyancyController, ParticleSystem splashTemplate)
        {
            buoyancy = buoyancyController;
            if (splashTemplate != null)
                splashPrefab = splashTemplate;
        }

        private void Awake()
        {
            if (buoyancy == null)
                buoyancy = GetComponentInParent<ShipBuoyancyController>();

            if (splashPrefab != null)
                BuildPool();
            else
                _procedural = FoamParticleFactory.CreateFoamSystem(transform, "ImpactSplashFoam",
                    new Color(0.96f, 0.98f, 1f, 0.95f), 0.9f, 1.1f, gravity: 1f);
        }

        private void OnEnable()
        {
            if (buoyancy != null)
                buoyancy.WaterContact += OnWaterContact;
        }

        private void OnDisable()
        {
            if (buoyancy != null)
                buoyancy.WaterContact -= OnWaterContact;
        }

        private void BuildPool()
        {
            if (splashPrefab == null)
                return;

            while (_pool.Count < poolSize)
            {
                var ps = Instantiate(splashPrefab, transform);
                ps.gameObject.SetActive(false);
                _pool.Enqueue(ps);
            }
        }

        private void OnWaterContact(ShipWaterContactEvent ev)
        {
            if (ev.contactType != ShipWaterContactType.Impact)
                return;
            if (ev.impactStrength < impactThreshold)
                return;
            if (Time.time < _nextEmitTime)
                return;

            _nextEmitTime = Time.time + minInterval;

            if (splashPrefab == null)
            {
                EmitProcedural(ev);
                return;
            }

            var ps = Acquire();
            if (ps == null)
                return;

            Transform t = ps.transform;
            t.position = ev.worldPoint;
            t.rotation = Quaternion.LookRotation(ev.surfaceNormal.sqrMagnitude > 0.001f ? ev.surfaceNormal : Vector3.up);
            ps.gameObject.SetActive(true);

            var main = ps.main;
            main.startSpeedMultiplier = Mathf.Lerp(1f, 6f, Mathf.Clamp01(ev.impactStrength / 12f));
            ps.Play(true);

            StartCoroutine(ReturnAfter(ps, main.duration + main.startLifetime.constantMax + 0.05f));
        }

        // Asset-free splash burst: throw foam dots up along the wave normal, scaled by impact.
        private void EmitProcedural(ShipWaterContactEvent ev)
        {
            if (_procedural == null)
                return;

            int count = Mathf.Clamp(Mathf.RoundToInt(ev.impactStrength * 1.5f), 4, 40);
            float burst = Mathf.Lerp(1.5f, 5f, Mathf.Clamp01(ev.impactStrength / 12f));
            Vector3 up = ev.surfaceNormal.sqrMagnitude > 0.001f ? ev.surfaceNormal.normalized : Vector3.up;

            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                emit.position = ev.worldPoint + Random.insideUnitSphere * 0.3f;
                emit.velocity = up * burst + Random.insideUnitSphere * (burst * 0.4f);
                _procedural.Emit(emit, 1);
            }
        }

        private System.Collections.IEnumerator ReturnAfter(ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ps == null)
                yield break;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            _pool.Enqueue(ps);
        }

        private ParticleSystem Acquire()
        {
            if (_pool.Count > 0)
                return _pool.Dequeue();

            if (splashPrefab == null)
                return null;

            var ps = Instantiate(splashPrefab, transform);
            ps.gameObject.SetActive(false);
            return ps;
        }
    }
}

