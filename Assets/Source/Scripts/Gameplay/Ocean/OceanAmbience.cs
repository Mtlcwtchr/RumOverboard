#if FUSION2
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Procedural sea sound (no audio assets in the project): low-passed pink noise whose level
    /// breathes with a slow swell rhythm, rises with the ship's speed through the water (hull rush)
    /// and with wind, plus short splash bursts on wave impacts (NetworkShip.LastImpactStrength,
    /// replicated, so every peer hears the same slams). Local-only cosmetics.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class OceanAmbience : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 0.45f;
        [SerializeField] private float swellPeriod = 7f;
        [SerializeField] private float speedForFullRush = 7f;

        private System.Random _rng;
        private int _sampleRate;
        // Pink-noise state (Paul Kellet) + two one-pole low-passes.
        private float _b0, _b1, _b2, _b3, _b4, _b5, _b6, _lpA, _lpB;
        private double _phase;

        // Written on the main thread, read on the audio thread.
        private volatile float _rush;
        private volatile float _splash;
        private float _lastImpact;
        private Vector3 _lastShipPos;
        private bool _hasShip;

        private void Awake()
        {
            _rng = new System.Random(1234);
            _sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            if (!src.isPlaying) src.Play();
        }

        private void Update()
        {
            NetworkShip ship = NetworkShip.All.Count > 0 ? NetworkShip.All[0] : null;
            float speed = 0f;
            if (ship != null)
            {
                Vector3 p = ship.transform.position;
                if (_hasShip && Time.deltaTime > 0f)
                    speed = Vector3.Distance(p, _lastShipPos) / Time.deltaTime;
                _lastShipPos = p;
                _hasShip = true;

                float impact = ship.NetworkedImpactStrength;
                if (impact > _lastImpact + 0.5f)
                    _splash = Mathf.Clamp01(_splash + impact * 0.12f);
                _lastImpact = Mathf.Lerp(_lastImpact, impact, 0.2f);
            }
            _rush = Mathf.Lerp(_rush, Mathf.Clamp01(speed / Mathf.Max(0.1f, speedForFullRush)), 1f - Mathf.Exp(-2f * Time.deltaTime));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_rng == null) return;
            double dt = 1.0 / Mathf.Max(8000, _sampleRate);
            float rush = _rush;
            float splash = _splash;
            float splashDecay = Mathf.Exp(-(float)dt * 3.5f);

            for (int i = 0; i < data.Length; i += channels)
            {
                float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
                _b0 = 0.99886f * _b0 + white * 0.0555179f;
                _b1 = 0.99332f * _b1 + white * 0.0750759f;
                _b2 = 0.96900f * _b2 + white * 0.1538520f;
                _b3 = 0.86650f * _b3 + white * 0.3104856f;
                _b4 = 0.55000f * _b4 + white * 0.5329522f;
                _b5 = -0.7616f * _b5 - white * 0.0168980f;
                float pink = (_b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362f) * 0.11f;
                _b6 = white * 0.115926f;

                // Deep surf: heavily low-passed, breathing with the swell.
                _phase += dt / Mathf.Max(1f, swellPeriod);
                float swell = 0.55f + 0.45f * Mathf.Sin((float)(_phase * 2.0 * System.Math.PI));
                _lpA += (pink - _lpA) * 0.035f;
                // Hull rush / spray: brighter band, scales with speed and splashes.
                _lpB += (pink - _lpB) * 0.25f;

                float sample = _lpA * (0.9f + 0.6f * swell) * 2.2f
                             + _lpB * (rush * 0.55f + splash * 1.4f);
                splash *= splashDecay;

                sample *= volume;
                for (int c = 0; c < channels; c++)
                    data[i + c] += sample;
            }
            _splash = splash;
        }
    }
}
#endif
