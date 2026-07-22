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

            BuildPool();
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

