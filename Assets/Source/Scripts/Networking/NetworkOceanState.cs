#if FUSION2
using Fusion;
using RumOverboard.Gameplay.Ocean;
using UnityEngine;

namespace RumOverboard.Networking
{
    public struct OceanNetState : INetworkStruct
    {
        public int SeaStateIndex;
        public uint SharedSeed;
        public float OceanTime;
    }

    /// <summary>
    /// Replicates compact ocean state for deterministic client-side sampling: the shared
    /// RNG seed and active sea-state index (which no other networked object carries), plus
    /// the authoritative ocean clock.
    ///
    /// OCEAN-CLOCK OWNERSHIP: the ocean time is a single-authority value. When a
    /// <see cref="NetworkShip"/> exists it already owns and corrects that clock, so this
    /// component DEFERS — it stops writing/correcting OceanTime to avoid two systems fighting
    /// over <c>waveField.ApplyTimeCorrection</c>. Use NetworkOceanState as the ocean-clock
    /// authority only in ship-less scenes; the seed/sea-state half always replicates.
    /// </summary>
    public class NetworkOceanState : NetworkBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanSimulationConfig config;

        [Tooltip("Threshold/gain for nudging local ocean time toward the authority's " +
                 "(only used when this component owns the clock, i.e. no NetworkShip present).")]
        [SerializeField] private float oceanTimeCorrectionThreshold = 0.1f;
        [SerializeField] private float oceanTimeCorrectionGain = 0.1f;

        [Networked] public OceanNetState State { get; set; }

        // False when a NetworkShip is present — the ship owns the ocean clock, we don't touch it.
        private bool _ownsOceanTime;

        public override void Spawned()
        {
            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (config == null && waveField != null)
                config = waveField.Config;

            _ownsOceanTime = FindAnyObjectByType<NetworkShip>() == null;
            if (!_ownsOceanTime)
                Debug.Log("[NetworkOceanState] A NetworkShip owns the ocean clock; " +
                          "deferring OceanTime replication/correction to it.");

            if (HasStateAuthority)
            {
                var s = State;
                s.SeaStateIndex = 0;
                s.SharedSeed = config != null ? config.sharedSeed : 1337u;
                if (_ownsOceanTime)
                    s.OceanTime = waveField != null ? waveField.OceanTimeNow : 0f;
                State = s;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (waveField == null || !_ownsOceanTime)
                return;

            if (HasStateAuthority)
            {
                var s = State;
                s.OceanTime = waveField.OceanTimeNow;
                State = s;
            }
            else
            {
                float error = State.OceanTime - waveField.OceanTimeNow;
                if (Mathf.Abs(error) > Mathf.Max(0.01f, oceanTimeCorrectionThreshold))
                    waveField.ApplyTimeCorrection(error * Mathf.Clamp01(oceanTimeCorrectionGain));
            }
        }
    }
}
#endif
