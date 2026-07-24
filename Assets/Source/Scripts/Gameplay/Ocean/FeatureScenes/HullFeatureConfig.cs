using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    [CreateAssetMenu(menuName = "RumOverboard/FeatureScenes/Hull Feature Config", fileName = "HullFeatureConfig")]
    public class HullFeatureConfig : ScriptableObject
    {
        [Header("Ocean")]
        [Range(0f, 4f)] public float waveStrength = 1f;
        [Range(0.1f, 4f)] public float waveSpeed = 1f;
        [Range(0f, 4f)] public float currentSpeed = 1f;
        [Range(-180f, 180f)] public float currentDirectionOffset;

        [Header("Wind")]
        public bool windEnabled = true;
        [Range(0f, 360f)] public float windDirection = 35f;
        [Min(0f)] public float windBaseStrength = 12f;
        [Min(0f)] public float windGustStrength = 4f;
        [Min(0.01f)] public float windGustFrequency = 0.12f;
        [Min(0f)] public float windTurbulenceStrength = 1.5f;
        [Min(0.001f)] public float windTurbulenceScale = 0.05f;

        [Header("Hull")]
        [Min(0f)] public float buoyancyForce = 30f;
        [Min(0f)] public float verticalDamping = 10f;
        [Min(0f)] public float longitudinalDrag = 2f;
        [Min(0f)] public float lateralDrag = 12f;
        [Min(0f)] public float angularDrag = 2.2f;
        [Min(0f)] public float currentRelativeDrag = 2.5f;
        [Min(0f)] public float rollStability = 28f;
        [Min(0f)] public float pitchStability = 22f;

        [Header("Visual")]
        public bool showSurface;
        public bool drawHullForces = true;

        public void CaptureFrom(HullFeatureSceneController scene)
        {
            if (scene == null)
                return;

            OceanWaveField wave = scene.WaveField;
            if (wave != null)
            {
                waveStrength = wave.RuntimeWaveAmplitudeMultiplier;
                waveSpeed = wave.RuntimeWaveSpeedMultiplier;
                currentSpeed = wave.RuntimeCurrentSpeedMultiplier;
                currentDirectionOffset = wave.RuntimeCurrentDirectionOffsetDegrees;
            }

            OceanWindSystem wind = scene.WindSystem;
            if (wind != null)
            {
                windEnabled = wind.WindEnabled;
                windDirection = wind.DirectionDegrees;
                windBaseStrength = wind.BaseStrength;
                windGustStrength = wind.GustStrength;
                windGustFrequency = wind.GustFrequency;
                windTurbulenceStrength = wind.TurbulenceStrength;
                windTurbulenceScale = wind.TurbulenceScale;
            }

            ShipBuoyancyController hull = scene.HullBuoyancy;
            if (hull != null)
            {
                buoyancyForce = hull.BuoyancyForce;
                verticalDamping = hull.VerticalDamping;
                longitudinalDrag = hull.LongitudinalDrag;
                lateralDrag = hull.LateralDrag;
                angularDrag = hull.AngularDragCoefficient;
                currentRelativeDrag = hull.CurrentRelativeDrag;
                rollStability = hull.RollStability;
                pitchStability = hull.PitchStability;
                drawHullForces = hull.DebugDrawForces;
            }

            showSurface = scene.SurfaceRenderer != null && scene.SurfaceRenderer.enabled;
        }

        public void ApplyTo(HullFeatureSceneController scene)
        {
            if (scene == null)
                return;

            OceanWaveField wave = scene.WaveField;
            if (wave != null)
            {
                wave.RuntimeWaveAmplitudeMultiplier = waveStrength;
                wave.RuntimeWaveSpeedMultiplier = waveSpeed;
                wave.RuntimeCurrentSpeedMultiplier = currentSpeed;
                wave.RuntimeCurrentDirectionOffsetDegrees = currentDirectionOffset;
            }

            OceanWindSystem wind = scene.WindSystem;
            if (wind != null)
            {
                wind.WindEnabled = windEnabled;
                wind.DirectionDegrees = windDirection;
                wind.BaseStrength = windBaseStrength;
                wind.GustStrength = windGustStrength;
                wind.GustFrequency = windGustFrequency;
                wind.TurbulenceStrength = windTurbulenceStrength;
                wind.TurbulenceScale = windTurbulenceScale;
            }

            ShipBuoyancyController hull = scene.HullBuoyancy;
            if (hull != null)
            {
                hull.BuoyancyForce = buoyancyForce;
                hull.VerticalDamping = verticalDamping;
                hull.LongitudinalDrag = longitudinalDrag;
                hull.LateralDrag = lateralDrag;
                hull.AngularDragCoefficient = angularDrag;
                hull.CurrentRelativeDrag = currentRelativeDrag;
                hull.RollStability = rollStability;
                hull.PitchStability = pitchStability;
                hull.DebugDrawAlways = true;
                hull.DebugDrawForces = drawHullForces;
                hull.DebugDrawResultants = true;
                hull.DebugDrawNormals = true;
            }

            if (scene.SurfaceRenderer != null)
                scene.SurfaceRenderer.enabled = showSurface;
        }
    }
}

