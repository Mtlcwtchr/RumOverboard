#if FUSION2
using UnityEngine;

namespace RumOverboard.Gameplay.Fire
{
    /// <summary>Fades a one-shot light out and removes it.</summary>
    public sealed class FadeLight : MonoBehaviour
    {
        public float Duration = 0.5f;
        private Light _light;
        private float _start, _t;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _start = _light != null ? _light.intensity : 0f;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_light != null) _light.intensity = _start * Mathf.Clamp01(1f - _t / Duration);
            if (_t >= Duration) Destroy(gameObject);
        }
    }
}
#endif
