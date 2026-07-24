using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.FeatureScenes
{
    [CreateAssetMenu(menuName = "RumOverboard/FeatureScenes/Wheel Feature Config", fileName = "WheelFeatureConfig")]
    public class WheelFeatureConfig : ScriptableObject
    {
        [Header("Wheel")]
        [Range(30f, 1440f)] public float maxWheelDegrees = 900f;
        [Min(1f)] public float wheelTurnRate = 320f;

        [Header("Rudder")]
        [Range(1f, 85f)] public float maxRudderAngle = 35f;
        [Min(0.01f)] public float rudderResponse = 2.5f;
        [Min(0f)] public float rudderYawCoefficient = 9000f;
        public bool invertSteering;

        [Header("Water disturbance")]
        [Min(0f)] public float waveWheelDisturbance = 40f;
        [Min(0f)] public float waveRudderBuffet = 1.5f;
        [Min(0f)] public float unmannedCentering = 12f;
        public bool simulateWater;

        [Header("Visual")]
        public bool showWaterSurface = true;

        public void CaptureFrom(WheelFeatureSceneController scene)
        {
            if (scene == null || scene.WheelSystem == null)
                return;

            WheelFeatureStandaloneSystem wheel = scene.WheelSystem;
            maxWheelDegrees = wheel.MaxWheelDegrees;
            wheelTurnRate = wheel.WheelTurnRate;
            maxRudderAngle = wheel.MaxRudderAngle;
            rudderResponse = wheel.RudderResponse;
            rudderYawCoefficient = wheel.RudderYawCoefficient;
            invertSteering = wheel.InvertSteering;
            waveWheelDisturbance = wheel.WaveWheelDisturbance;
            waveRudderBuffet = wheel.WaveRudderBuffet;
            unmannedCentering = wheel.UnmannedCentering;
            simulateWater = wheel.SimulateWater;
            showWaterSurface = scene.SurfaceRenderer != null && scene.SurfaceRenderer.enabled;
        }

        public void ApplyTo(WheelFeatureSceneController scene)
        {
            if (scene == null || scene.WheelSystem == null)
                return;

            WheelFeatureStandaloneSystem wheel = scene.WheelSystem;
            wheel.MaxWheelDegrees = maxWheelDegrees;
            wheel.WheelTurnRate = wheelTurnRate;
            wheel.MaxRudderAngle = maxRudderAngle;
            wheel.RudderResponse = rudderResponse;
            wheel.RudderYawCoefficient = rudderYawCoefficient;
            wheel.InvertSteering = invertSteering;
            wheel.WaveWheelDisturbance = waveWheelDisturbance;
            wheel.WaveRudderBuffet = waveRudderBuffet;
            wheel.UnmannedCentering = unmannedCentering;
            wheel.SimulateWater = simulateWater;

            if (scene.SurfaceRenderer != null)
                scene.SurfaceRenderer.enabled = showWaterSurface;
        }
    }
}

