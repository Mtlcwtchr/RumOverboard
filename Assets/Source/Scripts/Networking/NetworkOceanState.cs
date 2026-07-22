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
    /// Replicates compact ocean state for deterministic client-side sampling.
    /// </summary>
    public class NetworkOceanState : NetworkBehaviour
    {
        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private OceanSimulationConfig config;

        [Networked] public OceanNetState State { get; set; }

        public override void Spawned()
        {
            if (waveField == null)
                waveField = FindFirstObjectByType<OceanWaveField>();
            if (config == null && waveField != null)
                config = waveField.Config;

            if (HasStateAuthority)
            {
                var s = State;
                s.SeaStateIndex = 0;
                s.SharedSeed = config != null ? config.sharedSeed : 1337u;
                s.OceanTime = waveField != null ? waveField.OceanTimeNow : 0f;
                State = s;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (waveField == null)
                return;

            if (HasStateAuthority)
            {
                var s = State;
                s.OceanTime = waveField.OceanTimeNow;
                State = s;
            }
            else
            {
                // Small correction hook: clients can compare local time to authoritative OceanTime.
                float error = State.OceanTime - waveField.OceanTimeNow;
                if (Mathf.Abs(error) > 0.1f)
                    waveField.ApplyTimeCorrection(error * 0.1f);
            }
        }
    }
}
#endif

