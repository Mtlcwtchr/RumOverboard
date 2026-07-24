using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Simulation
{
    public readonly struct ShipBuoyancyPointForceInput
    {
        public readonly Vector3 Up;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;
        public readonly Vector3 SurfaceNormal;
        public readonly Vector3 RelativeVelocity;
        public readonly float Submergence;
        public readonly float EffectiveBuoyancy;
        public readonly float PointBuoyancy;
        public readonly float BuoyancyMultiplier;
        public readonly float PointDamping;
        public readonly float VerticalDamping;
        public readonly float LateralDrag;
        public readonly float LongitudinalDrag;
        public readonly float CurrentRelativeDrag;
        public readonly float DragMultiplier;
        public readonly float MassScale;
        public readonly float MaxForcePerPoint;

        public ShipBuoyancyPointForceInput(
            Vector3 up,
            Vector3 forward,
            Vector3 right,
            Vector3 surfaceNormal,
            Vector3 relativeVelocity,
            float submergence,
            float effectiveBuoyancy,
            float pointBuoyancy,
            float buoyancyMultiplier,
            float pointDamping,
            float verticalDamping,
            float lateralDrag,
            float longitudinalDrag,
            float currentRelativeDrag,
            float dragMultiplier,
            float massScale,
            float maxForcePerPoint)
        {
            Up = up;
            Forward = forward;
            Right = right;
            SurfaceNormal = surfaceNormal;
            RelativeVelocity = relativeVelocity;
            Submergence = submergence;
            EffectiveBuoyancy = effectiveBuoyancy;
            PointBuoyancy = pointBuoyancy;
            BuoyancyMultiplier = buoyancyMultiplier;
            PointDamping = pointDamping;
            VerticalDamping = verticalDamping;
            LateralDrag = lateralDrag;
            LongitudinalDrag = longitudinalDrag;
            CurrentRelativeDrag = currentRelativeDrag;
            DragMultiplier = dragMultiplier;
            MassScale = massScale;
            MaxForcePerPoint = maxForcePerPoint;
        }
    }

    public readonly struct ShipBuoyancyPointForceResult
    {
        public readonly Vector3 TotalForce;
        public readonly Vector3 BuoyancyForce;
        public readonly Vector3 DampingForce;

        public ShipBuoyancyPointForceResult(Vector3 totalForce, Vector3 buoyancyForce, Vector3 dampingForce)
        {
            TotalForce = totalForce;
            BuoyancyForce = buoyancyForce;
            DampingForce = dampingForce;
        }
    }

    public static class ShipBuoyancyPointForceSolver
    {
        public static ShipBuoyancyPointForceResult Solve(in ShipBuoyancyPointForceInput input)
        {
            Vector3 buoyDir = Vector3.Slerp(input.Up, input.SurfaceNormal, 0.45f).normalized;
            float buoyancyMagnitude = input.EffectiveBuoyancy * input.PointBuoyancy * input.BuoyancyMultiplier * input.Submergence;
            Vector3 buoyancy = buoyDir * buoyancyMagnitude;

            Vector3 vertical = Vector3.Project(input.RelativeVelocity, input.Up);
            Vector3 lateral = Vector3.Project(input.RelativeVelocity, input.Right);
            Vector3 longitudinal = Vector3.Project(input.RelativeVelocity, input.Forward);

            Vector3 damping = Vector3.zero;
            damping += -vertical * (input.VerticalDamping * input.PointDamping * input.DragMultiplier * input.Submergence);
            damping += -lateral * (input.LateralDrag * input.DragMultiplier * input.Submergence);
            damping += -longitudinal * (input.LongitudinalDrag * input.DragMultiplier * input.Submergence);
            damping += -input.RelativeVelocity * (input.CurrentRelativeDrag * input.DragMultiplier * input.Submergence * 0.25f);
            damping *= input.MassScale;

            Vector3 total = buoyancy + damping;
            float maxForce = Mathf.Max(0.001f, input.MaxForcePerPoint);
            if (total.magnitude > maxForce)
            {
                float scale = maxForce / total.magnitude;
                buoyancy *= scale;
                damping *= scale;
                total *= scale;
            }

            return new ShipBuoyancyPointForceResult(total, buoyancy, damping);
        }
    }

    public readonly struct ShipStabilityTorqueInput
    {
        public readonly Quaternion LocalToWorld;
        public readonly Vector3 TransformUp;
        public readonly Vector3 TransformRight;
        public readonly Vector3 AngularVelocity;
        public readonly Vector3 AverageSurfaceNormal;
        public readonly float SurfaceNormalInfluence;
        public readonly float PitchStability;
        public readonly float RollStability;
        public readonly float PitchDamping;
        public readonly float RollDamping;
        public readonly float InversionRecoveryTorque;
        public readonly float MaxStabilityTorque;
        public readonly float DragMultiplier;

        public ShipStabilityTorqueInput(
            Quaternion localToWorld,
            Vector3 transformUp,
            Vector3 transformRight,
            Vector3 angularVelocity,
            Vector3 averageSurfaceNormal,
            float surfaceNormalInfluence,
            float pitchStability,
            float rollStability,
            float pitchDamping,
            float rollDamping,
            float inversionRecoveryTorque,
            float maxStabilityTorque,
            float dragMultiplier)
        {
            LocalToWorld = localToWorld;
            TransformUp = transformUp;
            TransformRight = transformRight;
            AngularVelocity = angularVelocity;
            AverageSurfaceNormal = averageSurfaceNormal;
            SurfaceNormalInfluence = surfaceNormalInfluence;
            PitchStability = pitchStability;
            RollStability = rollStability;
            PitchDamping = pitchDamping;
            RollDamping = rollDamping;
            InversionRecoveryTorque = inversionRecoveryTorque;
            MaxStabilityTorque = maxStabilityTorque;
            DragMultiplier = dragMultiplier;
        }
    }

    public static class ShipStabilityTorqueSolver
    {
        public static Vector3 Solve(in ShipStabilityTorqueInput input)
        {
            Vector3 normalizedSurfaceNormal = input.AverageSurfaceNormal.sqrMagnitude > 0.0001f
                ? input.AverageSurfaceNormal.normalized
                : Vector3.up;

            Vector3 desiredUp = Vector3.Slerp(
                Vector3.up,
                normalizedSurfaceNormal,
                Mathf.Clamp01(input.SurfaceNormalInfluence));

            Quaternion worldToLocal = Quaternion.Inverse(input.LocalToWorld);
            Vector3 correctionAxisWorld = Vector3.Cross(input.TransformUp, desiredUp);
            Vector3 correctionAxisLocal = worldToLocal * correctionAxisWorld;
            Vector3 localAngularVelocity = worldToLocal * input.AngularVelocity;

            Vector3 localTorque = Vector3.zero;
            localTorque.x = correctionAxisLocal.x * input.PitchStability - localAngularVelocity.x * input.PitchDamping;
            localTorque.z = correctionAxisLocal.z * input.RollStability - localAngularVelocity.z * input.RollDamping;

            Vector3 worldTorque = input.LocalToWorld * localTorque;

            float upsideDown = Mathf.Clamp01(-Vector3.Dot(input.TransformUp, desiredUp));
            if (upsideDown > 0.0001f)
            {
                Vector3 recoveryAxis = correctionAxisWorld.sqrMagnitude > 0.0001f
                    ? correctionAxisWorld.normalized
                    : input.TransformRight;
                worldTorque += recoveryAxis * (upsideDown * input.InversionRecoveryTorque);
            }

            float maxTorque = Mathf.Max(0.01f, input.MaxStabilityTorque) * Mathf.Max(0.25f, input.DragMultiplier);
            return Vector3.ClampMagnitude(worldTorque, maxTorque);
        }
    }

    public readonly struct ShipHullWindForceInput
    {
        public readonly Vector3 WindVelocity;
        public readonly Vector3 ShipVelocity;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;
        public readonly float WindLongitudinalFactor;
        public readonly float WindLateralFactor;
        public readonly float HullForceCoefficient;
        public readonly float MaxWindForce;

        public ShipHullWindForceInput(
            Vector3 windVelocity,
            Vector3 shipVelocity,
            Vector3 forward,
            Vector3 right,
            float windLongitudinalFactor,
            float windLateralFactor,
            float hullForceCoefficient,
            float maxWindForce)
        {
            WindVelocity = windVelocity;
            ShipVelocity = shipVelocity;
            Forward = forward;
            Right = right;
            WindLongitudinalFactor = windLongitudinalFactor;
            WindLateralFactor = windLateralFactor;
            HullForceCoefficient = hullForceCoefficient;
            MaxWindForce = maxWindForce;
        }
    }

    public static class ShipHullWindForceSolver
    {
        public static Vector3 Solve(in ShipHullWindForceInput input)
        {
            Vector3 relativeWind = input.WindVelocity - input.ShipVelocity;
            Vector3 longitudinal = Vector3.Project(relativeWind, input.Forward) * Mathf.Max(0f, input.WindLongitudinalFactor);
            Vector3 lateral = Vector3.Project(relativeWind, input.Right) * Mathf.Max(0f, input.WindLateralFactor);
            Vector3 windForce = (longitudinal + lateral) * Mathf.Max(0f, input.HullForceCoefficient);

            if (input.MaxWindForce > 0f)
                windForce = Vector3.ClampMagnitude(windForce, input.MaxWindForce);

            return windForce;
        }
    }
}

