using System;
using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes.Sails
{
    [Serializable]
    public sealed class SailMastFeatureEntry
    {
        public string mastName = "Mast";
        public SailMastRigType rigType = SailMastRigType.DoubleSail;
        public Vector3 localPosition;
        [Range(0f, 360f)] public float yawDegrees;
        [Min(3f)] public float mastHeight = 9f;
        [Min(0.1f)] public float mastRadius = 0.2f;
        [Min(0.2f)] public float areaScale = 1f;
        [Min(1000f)] public float yieldStrength = 40_000_000f;
    }

    [CreateAssetMenu(menuName = "RumOverboard/FeatureScenes/Sail Feature Config", fileName = "SailFeatureConfig")]
    public sealed class SailFeatureConfig : ScriptableObject
    {
        [Header("Ocean")]
        [Range(0f, 4f)] public float waveStrength = 1f;
        [Range(0.1f, 4f)] public float waveSpeed = 1f;
        [Range(0f, 4f)] public float currentSpeed = 1f;

        [Header("Wind")]
        public bool windEnabled = true;
        [Range(0f, 360f)] public float windDirection = 35f;
        [Min(0f)] public float windBaseStrength = 12f;
        [Min(0f)] public float windGustStrength = 4f;
        [Min(0.01f)] public float windGustFrequency = 0.12f;
        [Min(0f)] public float windTurbulenceStrength = 1.5f;
        [Min(0.001f)] public float windTurbulenceScale = 0.05f;

        [Header("Sail force")]
        [Min(0f)] public float airDensity = 1.225f;
        [Min(0f)] public float sailForceScale = 1f;
        [Min(0f)] public float maxForcePerSail = 6000f;
        [Range(0f, 2f)] public float liftFactor = 1.1f;
        [Range(0f, 2f)] public float dragFactor = 1f;
        [Range(0f, 1f)] public float sideForceFactor = 0.6f;
        [Range(0f, 1f)] public float reverseDriveFactor = 0.12f;
        [Range(0f, 1f)] public float verticalForceFactor = 0.08f;
        public bool blockReverseDriveFromHeadwind = true;

        [Header("View")]
        public bool showWaterSurface = true;

        [Header("Masts")]
        public List<SailMastFeatureEntry> masts = new List<SailMastFeatureEntry>
        {
            new SailMastFeatureEntry { mastName = "Fore", rigType = SailMastRigType.DoubleSail, localPosition = new Vector3(0f, 0f, 3.6f), mastHeight = 9.5f },
            new SailMastFeatureEntry { mastName = "Main", rigType = SailMastRigType.DoubleSail, localPosition = new Vector3(0f, 0f, -0.6f), mastHeight = 10.8f },
            new SailMastFeatureEntry { mastName = "Mizzen", rigType = SailMastRigType.Triangular, localPosition = new Vector3(0f, 0f, -4.2f), mastHeight = 8.2f },
        };

        public void CaptureFrom(SailFeatureSceneController scene)
        {
            if (scene == null)
                return;

            if (scene.WaveField != null)
            {
                waveStrength = scene.WaveField.RuntimeWaveAmplitudeMultiplier;
                waveSpeed = scene.WaveField.RuntimeWaveSpeedMultiplier;
                currentSpeed = scene.WaveField.RuntimeCurrentSpeedMultiplier;
            }

            if (scene.WindSystem != null)
            {
                windEnabled = scene.WindSystem.WindEnabled;
                windDirection = scene.WindSystem.DirectionDegrees;
                windBaseStrength = scene.WindSystem.BaseStrength;
                windGustStrength = scene.WindSystem.GustStrength;
                windGustFrequency = scene.WindSystem.GustFrequency;
                windTurbulenceStrength = scene.WindSystem.TurbulenceStrength;
                windTurbulenceScale = scene.WindSystem.TurbulenceScale;
            }

            SailFeatureStandaloneSystem system = scene.SailSystem;
            if (system != null)
            {
                airDensity = system.AirDensity;
                sailForceScale = system.SailForceScale;
                maxForcePerSail = system.MaxForcePerSail;
                liftFactor = system.LiftFactor;
                dragFactor = system.DragFactor;
                sideForceFactor = system.SideForceFactor;
                reverseDriveFactor = system.ReverseDriveFactor;
                verticalForceFactor = system.VerticalForceFactor;
                blockReverseDriveFromHeadwind = system.BlockReverseDriveFromHeadwind;
                masts = system.ExportMastEntries();
            }

            showWaterSurface = scene.SurfaceRenderer != null && scene.SurfaceRenderer.enabled;
        }

        public void ApplyTo(SailFeatureSceneController scene)
        {
            if (scene == null)
                return;

            if (scene.WaveField != null)
            {
                scene.WaveField.RuntimeWaveAmplitudeMultiplier = waveStrength;
                scene.WaveField.RuntimeWaveSpeedMultiplier = waveSpeed;
                scene.WaveField.RuntimeCurrentSpeedMultiplier = currentSpeed;
            }

            if (scene.WindSystem != null)
            {
                scene.WindSystem.WindEnabled = windEnabled;
                scene.WindSystem.DirectionDegrees = windDirection;
                scene.WindSystem.BaseStrength = windBaseStrength;
                scene.WindSystem.GustStrength = windGustStrength;
                scene.WindSystem.GustFrequency = windGustFrequency;
                scene.WindSystem.TurbulenceStrength = windTurbulenceStrength;
                scene.WindSystem.TurbulenceScale = windTurbulenceScale;
            }

            SailFeatureStandaloneSystem system = scene.SailSystem;
            if (system != null)
            {
                system.AirDensity = airDensity;
                system.SailForceScale = sailForceScale;
                system.MaxForcePerSail = maxForcePerSail;
                system.LiftFactor = liftFactor;
                system.DragFactor = dragFactor;
                system.SideForceFactor = sideForceFactor;
                system.ReverseDriveFactor = reverseDriveFactor;
                system.VerticalForceFactor = verticalForceFactor;
                system.BlockReverseDriveFromHeadwind = blockReverseDriveFromHeadwind;
                system.ImportMastEntries(masts);
            }

            if (scene.SurfaceRenderer != null)
                scene.SurfaceRenderer.enabled = showWaterSurface;
        }
    }
}

