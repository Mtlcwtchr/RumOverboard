using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// High-level sea state sequencing with smooth profile blending.
    /// </summary>
    public class OceanSeaStateController : MonoBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanSeaStateProfile[] progression = new OceanSeaStateProfile[0];
        [SerializeField] private float transitionSeconds = 10f;

        [Header("Auto progression")]
        [SerializeField] private bool autoProgression;
        [SerializeField] private float autoStepSeconds = 120f;

        private int _currentIndex;
        private float _nextAutoTime;

        private void Awake()
        {
            if (waveField == null)
                waveField = GetComponentInChildren<OceanWaveField>();

            if (progression != null && progression.Length > 0)
            {
                _currentIndex = 0;
                waveField.SetSeaState(progression[0], 0.01f);
            }

            _nextAutoTime = Time.time + autoStepSeconds;
        }

        private void Update()
        {
            if (!autoProgression || progression == null || progression.Length <= 1)
                return;

            if (Time.time < _nextAutoTime)
                return;

            _nextAutoTime = Time.time + autoStepSeconds;
            AdvanceToNextState();
        }

        public void AdvanceToNextState()
        {
            if (progression == null || progression.Length == 0 || waveField == null)
                return;

            _currentIndex = Mathf.Clamp(_currentIndex + 1, 0, progression.Length - 1);
            waveField.SetSeaState(progression[_currentIndex], transitionSeconds);
        }

        public void SetStateByIndex(int index)
        {
            if (progression == null || progression.Length == 0 || waveField == null)
                return;

            _currentIndex = Mathf.Clamp(index, 0, progression.Length - 1);
            waveField.SetSeaState(progression[_currentIndex], transitionSeconds);
        }
    }
}

