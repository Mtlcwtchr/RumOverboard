using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Simulation
{
    public readonly struct SailAerodynamicsInput
    {
        public readonly Vector3 ApparentWind;
        public readonly Vector3 SailNormal;
        public readonly Vector3 ShipForward;
        public readonly float EffectiveArea;
        public readonly float AirDensity;
        public readonly float SailForceScale;
        public readonly float MaxForcePerSail;
        public readonly float LiftFactor;
        public readonly float DragFactor;
        public readonly float SideForceFactor;
        public readonly float ReverseDriveFactor;
        public readonly float VerticalForceFactor;
        public readonly bool BlockReverseDriveFromHeadwind;

        public SailAerodynamicsInput(
            Vector3 apparentWind,
            Vector3 sailNormal,
            Vector3 shipForward,
            float effectiveArea,
            float airDensity,
            float sailForceScale,
            float maxForcePerSail,
            float liftFactor,
            float dragFactor,
            float sideForceFactor,
            float reverseDriveFactor,
            float verticalForceFactor,
            bool blockReverseDriveFromHeadwind)
        {
            ApparentWind = apparentWind;
            SailNormal = sailNormal;
            ShipForward = shipForward;
            EffectiveArea = effectiveArea;
            AirDensity = airDensity;
            SailForceScale = sailForceScale;
            MaxForcePerSail = maxForcePerSail;
            LiftFactor = liftFactor;
            DragFactor = dragFactor;
            SideForceFactor = sideForceFactor;
            ReverseDriveFactor = reverseDriveFactor;
            VerticalForceFactor = verticalForceFactor;
            BlockReverseDriveFromHeadwind = blockReverseDriveFromHeadwind;
        }
    }

    public readonly struct SailAerodynamicsResult
    {
        public readonly Vector3 RigForce;
        public readonly Vector3 DriveForce;

        public SailAerodynamicsResult(Vector3 rigForce, Vector3 driveForce)
        {
            RigForce = rigForce;
            DriveForce = driveForce;
        }
    }

    public static class SailAerodynamicsSolver
    {
        public static SailAerodynamicsResult Solve(in SailAerodynamicsInput input)
        {
            if (input.ApparentWind.sqrMagnitude < 0.01f || input.EffectiveArea <= 0.001f)
                return new SailAerodynamicsResult(Vector3.zero, Vector3.zero);

            Vector3 appDir = input.ApparentWind.normalized;
            float speed = input.ApparentWind.magnitude;
            float attack = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(-appDir, input.SailNormal)));
            float dynamicPressure = 0.5f * Mathf.Max(0f, input.AirDensity) * speed * speed;

            float liftCoeff = (attack * (1f - attack)) * 2.4f * Mathf.Clamp(input.LiftFactor, 0f, 2f);
            float dragCoeff = (0.15f + attack * 1.2f) * Mathf.Clamp(input.DragFactor, 0f, 2f);

            Vector3 dragDir = appDir;
            Vector3 liftDir = Vector3.Cross(appDir, Vector3.Cross(input.SailNormal, appDir));
            if (liftDir.sqrMagnitude > 0.0001f)
                liftDir.Normalize();

            Vector3 rigForce = (dragDir * dragCoeff + liftDir * (liftCoeff * Mathf.Clamp01(input.SideForceFactor)))
                               * (dynamicPressure * input.EffectiveArea * Mathf.Max(0f, input.SailForceScale));

            Vector3 horizontal = Vector3.ProjectOnPlane(rigForce, Vector3.up);
            rigForce = horizontal + Vector3.up * (rigForce.y * Mathf.Clamp01(input.VerticalForceFactor));

            Vector3 driveForce = rigForce;
            float forwardComponent = Vector3.Dot(driveForce, input.ShipForward);
            if (forwardComponent < 0f)
            {
                Vector3 reverse = input.ShipForward * forwardComponent;
                if (input.BlockReverseDriveFromHeadwind)
                    driveForce -= reverse;
                else
                    driveForce -= reverse * (1f - Mathf.Clamp01(input.ReverseDriveFactor));
            }

            if (input.MaxForcePerSail > 0f)
            {
                rigForce = Vector3.ClampMagnitude(rigForce, input.MaxForcePerSail);
                driveForce = Vector3.ClampMagnitude(driveForce, input.MaxForcePerSail);
            }

            return new SailAerodynamicsResult(rigForce, driveForce);
        }
    }

    public static class MastStressSolver
    {
        public static float ComputeMaxBendingMoment(float radius, float yieldStrength)
        {
            float r = Mathf.Max(0.0001f, radius);
            float sectionModulus = Mathf.PI * r * r * r / 4f;
            return sectionModulus * Mathf.Max(0f, yieldStrength);
        }

        public static float ComputeMomentContribution(Vector3 mastBase, Vector3 mastTop, float attachHeight01, Vector3 force)
        {
            Vector3 mastVector = mastTop - mastBase;
            float mastLength = mastVector.magnitude;
            if (mastLength < 0.0001f)
                return 0f;

            Vector3 mastAxis = mastVector / mastLength;
            float leverArm = mastLength * Mathf.Clamp01(attachHeight01);
            Vector3 lateralForce = force - mastAxis * Vector3.Dot(force, mastAxis);
            return lateralForce.magnitude * leverArm;
        }

        public static float ComputeStressRatio(float currentMoment, float maxMoment)
        {
            if (maxMoment <= 0f)
                return 0f;
            return currentMoment / maxMoment;
        }
    }
}

