using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public class OceanWaveField : MonoBehaviour, IOceanSampler
    {
        [SerializeField] private OceanSimulationConfig config;
        [SerializeField] private OceanCurrentSystem currentSystem;
        [SerializeField] private OceanWindSystem windSystem;
        [SerializeField] private OceanDepthProvider depthProvider;
        [SerializeField] private OceanQualityLevel qualityLevel = OceanQualityLevel.Medium;

        [Header("Time")]
        [SerializeField] private bool useUnscaledTime;
        [SerializeField] private float oceanTimeScale = 1f;
        [SerializeField] private float oceanTimeOffset;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges;

        [Header("Runtime tuning")]
        [SerializeField] [Range(0f, 4f)] private float runtimeWaveAmplitudeMultiplier = 1f;
        [SerializeField] [Range(0.1f, 4f)] private float runtimeWaveSpeedMultiplier = 1f;
        [SerializeField] [Range(0f, 4f)] private float runtimeCurrentSpeedMultiplier = 1f;
        [SerializeField] [Range(-180f, 180f)] private float runtimeCurrentDirectionOffsetDegrees;

        [Header("Wind/current coupling")]
        [SerializeField] [Range(0f, 0.5f)] private float windToCurrentFactor = 0.06f;
        [SerializeField] [Min(0f)] private float maxWindDrivenCurrent = 2.5f;

        // Distinct jitter index for the synthesized swell wave (kept out of the authored-wave index range).
        // Public so the GPU path (OceanSurfaceRenderer) bakes the swell with the identical jitter.
        public const int SwellWaveIndex = 251;

        private OceanSeaStateProfile _activeProfile;
        private OceanSeaStateProfile _targetProfile;
        private float _transitionT = 1f;
        private float _transitionDuration = 8f;
        private float _transitionElapsed;

        private OceanQualityPreset _quality;

        public OceanSimulationConfig Config => config;
        public OceanSeaStateProfile ActiveProfile => _activeProfile;
        public OceanQualityPreset Quality => _quality;

        // Falls back to the config's default so editor/debug queries work before Awake runs.
        private OceanSeaStateProfile QueryProfile =>
            _activeProfile != null ? _activeProfile : (config != null ? config.defaultProfile : null);

        /// <summary>Name of the active sea state (for HUD/debug).</summary>
        public string ActiveProfileName => QueryProfile != null ? QueryProfile.name : "(none)";

        /// <summary>Heading of the strongest wave component (swell or biggest authored wave), XZ world vector.</summary>
        public Vector3 DominantWaveDirection
        {
            get
            {
                var p = QueryProfile;
                if (p == null) return Vector3.forward;
                Vector2 d = p.DominantWaveDirection2D;
                return new Vector3(d.x, 0f, d.y);
            }
        }

        /// <summary>Rough significant wave height (peak-to-trough-ish), scaled by runtime amplitude — for debug gauges.</summary>
        public float EstimatedWaveHeight =>
            QueryProfile != null ? QueryProfile.EstimatedWaveHeight() * runtimeWaveAmplitudeMultiplier : 0f;

        /// <summary>Convenience: surface height at a world position sampled at the current ocean time.</summary>
        public float SampleHeight(Vector3 worldPos) => Sample(worldPos, OceanTimeNow).surfaceHeight;

        public float RuntimeWaveAmplitudeMultiplier
        {
            get => runtimeWaveAmplitudeMultiplier;
            set => runtimeWaveAmplitudeMultiplier = Mathf.Clamp(value, 0f, 4f);
        }

        public float RuntimeWaveSpeedMultiplier
        {
            get => runtimeWaveSpeedMultiplier;
            set => runtimeWaveSpeedMultiplier = Mathf.Clamp(value, 0.1f, 4f);
        }

        public float RuntimeCurrentSpeedMultiplier
        {
            get => runtimeCurrentSpeedMultiplier;
            set => runtimeCurrentSpeedMultiplier = Mathf.Clamp(value, 0f, 4f);
        }

        public float RuntimeCurrentDirectionOffsetDegrees
        {
            get => runtimeCurrentDirectionOffsetDegrees;
            set => runtimeCurrentDirectionOffsetDegrees = Mathf.Clamp(value, -180f, 180f);
        }

        public float OceanTimeScale
        {
            get => oceanTimeScale;
            set => oceanTimeScale = Mathf.Max(0f, value);
        }

        public OceanWindSystem WindSystem
        {
            get => windSystem;
            set => windSystem = value;
        }

        public float WindToCurrentFactor
        {
            get => windToCurrentFactor;
            set => windToCurrentFactor = Mathf.Clamp(value, 0f, 0.5f);
        }

        public float MaxWindDrivenCurrent
        {
            get => maxWindDrivenCurrent;
            set => maxWindDrivenCurrent = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            ResolveReferences();
            RebuildQuality();
            InitializeProfile();
        }

        private void OnValidate()
        {
            if (oceanTimeScale < 0f)
                oceanTimeScale = 0f;
            runtimeWaveAmplitudeMultiplier = Mathf.Clamp(runtimeWaveAmplitudeMultiplier, 0f, 4f);
            runtimeWaveSpeedMultiplier = Mathf.Clamp(runtimeWaveSpeedMultiplier, 0.1f, 4f);
            runtimeCurrentSpeedMultiplier = Mathf.Clamp(runtimeCurrentSpeedMultiplier, 0f, 4f);
            runtimeCurrentDirectionOffsetDegrees = Mathf.Clamp(runtimeCurrentDirectionOffsetDegrees, -180f, 180f);
            windToCurrentFactor = Mathf.Clamp(windToCurrentFactor, 0f, 0.5f);
            maxWindDrivenCurrent = Mathf.Max(0f, maxWindDrivenCurrent);
            if (config != null)
                RebuildQuality();
        }

        public void ResetRuntimeTuning()
        {
            runtimeWaveAmplitudeMultiplier = 1f;
            runtimeWaveSpeedMultiplier = 1f;
            runtimeCurrentSpeedMultiplier = 1f;
            runtimeCurrentDirectionOffsetDegrees = 0f;
        }

        public float OceanTimeNow
        {
            get
            {
                float baseTime = useUnscaledTime ? Time.unscaledTime : Time.time;
                return baseTime * oceanTimeScale + oceanTimeOffset;
            }
        }

        public OceanSample Sample(Vector3 worldPos, float simulationTime)
        {
            EnsureReady();

            float seaLevel = config != null ? config.seaLevel : 0f;
            float depthDamping = 1f;
            if (depthProvider != null)
            {
                float estimatedDepth = depthProvider.EvaluateDepthMeters(worldPos, seaLevel);
                float shallowFactorSeed = depthProvider.EvaluateShallowFactor(estimatedDepth);
                depthDamping = depthProvider.EvaluateWaveDepthDamping(shallowFactorSeed);
            }

            Vector2 originalXZ = new Vector2(worldPos.x, worldPos.z);
            Vector2 guessedXZ = originalXZ;
            int iterations = _quality != null ? _quality.horizontalDisplacementIterations : 3;

            for (int i = 0; i < iterations; i++)
            {
                var accumIter = EvaluateBands(guessedXZ, simulationTime, includeRipple: false, physicsPass: true, depthDamping);
                guessedXZ = originalXZ - new Vector2(accumIter.displacement.x, accumIter.displacement.z);
            }

            var accum = EvaluateBands(guessedXZ, simulationTime, includeRipple: false, physicsPass: true, depthDamping);
            float surfaceHeight = seaLevel + accum.displacement.y;

            float depthMeters = depthProvider != null
                ? depthProvider.EvaluateDepthMeters(worldPos, surfaceHeight)
                : 40f;
            float shallowFactor = depthProvider != null
                ? depthProvider.EvaluateShallowFactor(depthMeters)
                : 0f;

            Vector3 currentVelocity = EvaluateCurrentVelocity(worldPos, simulationTime);

            OceanSample sample;
            sample.surfaceHeight = surfaceHeight;
            sample.surfaceNormal = OceanWaveMath.BuildNormal(accum.dHdX, accum.dHdZ);
            sample.displacement = accum.displacement;
            sample.verticalVelocity = accum.verticalVelocity;
            sample.horizontalVelocity = accum.horizontalVelocity;
            sample.currentVelocity = currentVelocity;
            sample.waterVelocity = currentVelocity + accum.horizontalVelocity + Vector3.up * accum.verticalVelocity;
            sample.depthMeters = depthMeters;
            sample.shallowFactor = shallowFactor;
            return sample;
        }

        public void SampleVisualSurface(Vector3 worldPos, float simulationTime, out Vector3 displacement, out Vector3 normal)
        {
            EnsureReady();

            float seaLevel = config != null ? config.seaLevel : 0f;
            float depthDamping = 1f;
            if (depthProvider != null)
            {
                float estimatedDepth = depthProvider.EvaluateDepthMeters(worldPos, seaLevel);
                float shallow = depthProvider.EvaluateShallowFactor(estimatedDepth);
                depthDamping = depthProvider.EvaluateWaveDepthDamping(shallow);
            }

            Vector2 originalXZ = new Vector2(worldPos.x, worldPos.z);
            Vector2 guessedXZ = originalXZ;
            int iterations = Mathf.Max(1, (_quality != null ? _quality.horizontalDisplacementIterations : 3) - 1);

            for (int i = 0; i < iterations; i++)
            {
                var accumIter = EvaluateBands(guessedXZ, simulationTime, includeRipple: false, physicsPass: false, depthDamping);
                guessedXZ = originalXZ - new Vector2(accumIter.displacement.x, accumIter.displacement.z);
            }

            var accum = EvaluateBands(guessedXZ, simulationTime, includeRipple: true, physicsPass: false, depthDamping);
            displacement = accum.displacement;
            displacement.y += seaLevel;
            normal = OceanWaveMath.BuildNormal(accum.dHdX, accum.dHdZ);
        }

        public void SetSeaState(OceanSeaStateProfile profile, float overrideTransitionSeconds = -1f)
        {
            if (profile == null || profile == _activeProfile)
                return;

            _targetProfile = profile;
            _transitionElapsed = 0f;
            _transitionDuration = overrideTransitionSeconds > 0f
                ? overrideTransitionSeconds
                : (config != null ? config.transitionSeconds : 8f);
            _transitionT = 0f;

            if (logStateChanges)
                Debug.Log($"[OceanWaveField] Transition to profile '{profile.name}' over {_transitionDuration:0.0}s");
        }

        public void SetQuality(OceanQualityLevel level)
        {
            qualityLevel = level;
            RebuildQuality();
        }

        public void ApplyTimeCorrection(float deltaSeconds)
        {
            oceanTimeOffset += deltaSeconds;
        }

        private void Update()
        {
            if (_targetProfile == null || _targetProfile == _activeProfile)
                return;

            _transitionElapsed += Time.deltaTime;
            _transitionT = Mathf.Clamp01(_transitionElapsed / Mathf.Max(0.01f, _transitionDuration));

            if (_transitionT >= 1f)
            {
                _activeProfile = _targetProfile;
                _targetProfile = null;
                _transitionT = 1f;
            }
        }

        private void ResolveReferences()
        {
            if (currentSystem == null)
                currentSystem = GetComponentInChildren<OceanCurrentSystem>();
            if (windSystem == null)
                windSystem = GetComponentInChildren<OceanWindSystem>();
            if (windSystem == null)
                windSystem = FindAnyObjectByType<OceanWindSystem>();
            if (depthProvider == null)
                depthProvider = GetComponentInChildren<OceanDepthProvider>();
        }

        private void EnsureReady()
        {
            if (_activeProfile == null)
                InitializeProfile();
            if (_quality == null)
                RebuildQuality();
        }

        private void InitializeProfile()
        {
            if (config != null)
            {
                _activeProfile = config.defaultProfile;
                _targetProfile = null;
                _transitionT = 1f;
                _transitionDuration = Mathf.Max(0.01f, config.transitionSeconds);
            }
        }

        private void RebuildQuality()
        {
            _quality = config != null ? config.ResolveQuality(qualityLevel) : null;
            if (_quality == null)
            {
                _quality = OceanQualityPreset.CreateRuntime("RuntimeFallback", 72, 48, 32);
                _quality.horizontalDisplacementIterations = 3;
                _quality.maxCpuLargeWaves = 6;
                _quality.maxCpuMediumWaves = 6;
            }
        }

        private Vector3 EvaluateCurrentVelocity(Vector3 worldPos, float time)
        {
            Vector3 global = Vector3.zero;
            if (_activeProfile != null)
                global += _activeProfile.GlobalCurrent;

            if (_targetProfile != null && _targetProfile != _activeProfile)
                global = Vector3.Lerp(global, _targetProfile.GlobalCurrent, _transitionT);

            if (currentSystem != null)
                global += currentSystem.EvaluateCurrent(worldPos, time);

            if (windSystem != null && windToCurrentFactor > 0f)
            {
                Vector3 windDriven = windSystem.EvaluateWind(worldPos, time);
                windDriven.y = 0f;
                windDriven *= windToCurrentFactor;

                if (maxWindDrivenCurrent > 0f)
                    windDriven = Vector3.ClampMagnitude(windDriven, maxWindDrivenCurrent);

                global += windDriven;
            }

            if (Mathf.Abs(runtimeCurrentDirectionOffsetDegrees) > 0.001f)
                global = Quaternion.Euler(0f, runtimeCurrentDirectionOffsetDegrees, 0f) * global;

            global *= runtimeCurrentSpeedMultiplier;

            return global;
        }

        private OceanWaveMath.Accumulator EvaluateBands(
            Vector2 posXZ,
            float time,
            bool includeRipple,
            bool physicsPass,
            float depthDamping)
        {
            var result = new OceanWaveMath.Accumulator();

            if (_activeProfile != null)
            {
                var a = EvaluateProfile(_activeProfile, posXZ, time, includeRipple, physicsPass, depthDamping, 0f);
                result.Add(a);
            }

            if (_targetProfile != null && _targetProfile != _activeProfile)
            {
                var b = EvaluateProfile(_targetProfile, posXZ, time, includeRipple, physicsPass, depthDamping, 0.5f);

                float t = _transitionT;
                result.displacement = Vector3.Lerp(result.displacement, b.displacement, t);
                result.dHdX = Mathf.Lerp(result.dHdX, b.dHdX, t);
                result.dHdZ = Mathf.Lerp(result.dHdZ, b.dHdZ, t);
                result.verticalVelocity = Mathf.Lerp(result.verticalVelocity, b.verticalVelocity, t);
                result.horizontalVelocity = Vector3.Lerp(result.horizontalVelocity, b.horizontalVelocity, t);
            }

            return result;
        }

        private OceanWaveMath.Accumulator EvaluateProfile(
            OceanSeaStateProfile profile,
            Vector2 posXZ,
            float time,
            bool includeRipple,
            bool physicsPass,
            float depthDamping,
            float seedOffset)
        {
            var result = new OceanWaveMath.Accumulator();
            if (profile == null || profile.waves == null)
                return result;

            float waveAmplitudeMul = runtimeWaveAmplitudeMultiplier;
            float waveTime = time * runtimeWaveSpeedMultiplier;
            float choppiness = Mathf.Clamp(profile.choppiness, 0f, 2f);

            int maxLarge = _quality != null ? _quality.maxCpuLargeWaves : 6;
            int maxMedium = _quality != null ? _quality.maxCpuMediumWaves : 6;
            int largeCount = 0;
            int mediumCount = 0;

            uint seed = config != null ? config.sharedSeed : 1337u;
            seed += (uint)(seedOffset * 997f);

            // Dominant directional swell (the "big rollers from a heading"). Added first and NOT
            // subject to the large-wave cap, since it's the defining wave of the sea state.
            if (profile.HasSwell)
            {
                var swell = profile.BuildSwellWave();
                swell.steepness = Mathf.Clamp01(swell.steepness * choppiness);
                AccumulateWave(ref result, swell, posXZ, waveTime, profile.directionJitterDegrees,
                    seed, SwellWaveIndex, physicsPass, depthDamping, waveAmplitudeMul);
            }

            for (int i = 0; i < profile.waves.Length; i++)
            {
                var wave = profile.waves[i];
                if (!wave.IsValid)
                    continue;

                if (!includeRipple && wave.band == OceanWaveBand.Ripple)
                    continue;

                if (wave.band == OceanWaveBand.Large)
                {
                    if (largeCount >= maxLarge)
                        continue;
                    largeCount++;
                }
                else if (wave.band == OceanWaveBand.Medium)
                {
                    if (mediumCount >= maxMedium)
                        continue;
                    mediumCount++;
                }

                // Global crest-sharpness control. Clamped per wave so Gerstner crests don't self-intersect.
                wave.steepness = Mathf.Clamp01(wave.steepness * choppiness);

                float bandDamping = wave.band == OceanWaveBand.Ripple ? 1f : depthDamping;
                AccumulateWave(ref result, wave, posXZ, waveTime, profile.directionJitterDegrees,
                    seed, i, physicsPass, bandDamping, waveAmplitudeMul);
            }

            return result;
        }

        // Evaluates one wave and folds it (with depth damping + amplitude multiplier) into the accumulator.
        private static void AccumulateWave(
            ref OceanWaveMath.Accumulator result,
            in OceanWaveDefinition wave,
            Vector2 posXZ,
            float waveTime,
            float jitterDegrees,
            uint seed,
            int index,
            bool physicsPass,
            float bandDamping,
            float amplitudeMul)
        {
            var accum = OceanWaveMath.EvaluateWave(wave, posXZ, waveTime, jitterDegrees, seed, index, physicsPass);

            float totalMul = bandDamping * amplitudeMul;
            accum.displacement *= totalMul;
            accum.dHdX *= totalMul;
            accum.dHdZ *= totalMul;
            accum.verticalVelocity *= totalMul;
            accum.horizontalVelocity *= totalMul;

            result.Add(accum);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_activeProfile == null || _activeProfile.waves == null)
                return;

            Vector3 origin = transform.position;
            for (int i = 0; i < _activeProfile.waves.Length; i++)
            {
                var wave = _activeProfile.waves[i];
                if (!wave.IsValid)
                    continue;

                Vector3 dir = new Vector3(wave.DirectionNormalized.x, 0f, wave.DirectionNormalized.y);
                Gizmos.color = wave.band == OceanWaveBand.Large
                    ? new Color(0.1f, 0.6f, 1f, 0.95f)
                    : (wave.band == OceanWaveBand.Medium
                        ? new Color(0.2f, 0.9f, 0.8f, 0.9f)
                        : new Color(0.7f, 0.9f, 1f, 0.8f));
                Gizmos.DrawLine(origin + Vector3.up * (0.3f + i * 0.12f), origin + Vector3.up * (0.3f + i * 0.12f) + dir * Mathf.Clamp(wave.amplitude * 8f, 1f, 6f));
            }
        }
#endif
    }
}

