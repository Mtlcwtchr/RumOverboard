using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Simulation
{
    public readonly struct ShipHelmDynamicsInput
    {
        public readonly float DeltaTime;
        public readonly float WheelAngle;
        public readonly float RudderAngle;
        public readonly float PendingSteer;
        public readonly bool Manned;
        public readonly float ForwardFlow;
        public readonly float LateralFlow;

        public ShipHelmDynamicsInput(
            float deltaTime,
            float wheelAngle,
            float rudderAngle,
            float pendingSteer,
            bool manned,
            float forwardFlow,
            float lateralFlow)
        {
            DeltaTime = deltaTime;
            WheelAngle = wheelAngle;
            RudderAngle = rudderAngle;
            PendingSteer = pendingSteer;
            Manned = manned;
            ForwardFlow = forwardFlow;
            LateralFlow = lateralFlow;
        }
    }

    public readonly struct ShipHelmDynamicsSettings
    {
        public readonly float MaxWheelDegrees;
        public readonly float WheelTurnRate;
        public readonly float MaxRudderAngle;
        public readonly float RudderResponse;
        public readonly float RudderYawCoefficient;
        public readonly float WaveWheelDisturbance;
        public readonly float WaveRudderBuffet;
        public readonly float UnmannedCentering;
        public readonly bool InvertSteering;

        public ShipHelmDynamicsSettings(
            float maxWheelDegrees,
            float wheelTurnRate,
            float maxRudderAngle,
            float rudderResponse,
            float rudderYawCoefficient,
            float waveWheelDisturbance,
            float waveRudderBuffet,
            float unmannedCentering,
            bool invertSteering)
        {
            MaxWheelDegrees = maxWheelDegrees;
            WheelTurnRate = wheelTurnRate;
            MaxRudderAngle = maxRudderAngle;
            RudderResponse = rudderResponse;
            RudderYawCoefficient = rudderYawCoefficient;
            WaveWheelDisturbance = waveWheelDisturbance;
            WaveRudderBuffet = waveRudderBuffet;
            UnmannedCentering = unmannedCentering;
            InvertSteering = invertSteering;
        }
    }

    public readonly struct ShipHelmDynamicsResult
    {
        public readonly float WheelAngle;
        public readonly float RudderAngle;
        public readonly float YawTorque;

        public ShipHelmDynamicsResult(float wheelAngle, float rudderAngle, float yawTorque)
        {
            WheelAngle = wheelAngle;
            RudderAngle = rudderAngle;
            YawTorque = yawTorque;
        }
    }

    public static class ShipHelmDynamicsSolver
    {
        public static ShipHelmDynamicsResult Step(in ShipHelmDynamicsInput input, in ShipHelmDynamicsSettings settings)
        {
            float dt = Mathf.Max(0.0001f, input.DeltaTime);
            float wheel = input.WheelAngle + Mathf.Clamp(input.PendingSteer, -1f, 1f) * settings.WheelTurnRate * dt;

            if (!input.Manned)
            {
                wheel += input.LateralFlow * settings.WaveWheelDisturbance * dt;
                wheel -= Mathf.Sign(wheel) * Mathf.Abs(input.ForwardFlow) * settings.UnmannedCentering * dt;
            }

            wheel = Mathf.Clamp(wheel, -settings.MaxWheelDegrees, settings.MaxWheelDegrees);
            float wheelNormalized = settings.MaxWheelDegrees > 0.01f
                ? Mathf.Clamp(wheel / settings.MaxWheelDegrees, -1f, 1f)
                : 0f;

            float targetRudder = wheelNormalized * settings.MaxRudderAngle;
            if (input.Manned)
                targetRudder += input.LateralFlow * settings.WaveRudderBuffet;

            targetRudder = Mathf.Clamp(targetRudder, -settings.MaxRudderAngle * 1.5f, settings.MaxRudderAngle * 1.5f);
            float rudder = Mathf.MoveTowards(
                input.RudderAngle,
                targetRudder,
                settings.RudderResponse * settings.MaxRudderAngle * dt);

            float yawTorque = settings.RudderYawCoefficient * input.ForwardFlow * Mathf.Sin(rudder * Mathf.Deg2Rad);
            if (settings.InvertSteering)
                yawTorque = -yawTorque;

            return new ShipHelmDynamicsResult(wheel, rudder, yawTorque);
        }
    }
}

