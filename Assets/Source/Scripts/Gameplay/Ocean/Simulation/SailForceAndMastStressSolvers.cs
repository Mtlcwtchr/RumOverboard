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

    /// <summary>
    /// Lift / drag polar of a cloth square sail vs angle of attack (deg, between the apparent wind
    /// and the sail plane): lift peaks at <see cref="StallAngle"/> and fades to 0 when the sail is
    /// broadside (pure drag); drag = profile + pressure (∝ sin²) + induced (∝ Cl², low aspect ratio).
    /// Best L/D ≈ 3 at ~10°, so a ship braced sharp drives from ~35–40° apparent wind.
    /// </summary>
    public static class SailPolar
    {
        public const float MaxLift = 1f;
        public const float StallAngle = 25f;
        public const float ProfileDrag = 0.1f;
        public const float BroadsideDrag = 1.2f;
        public const float InducedDrag = 0.2f;

        public static float Lift(float aoaDeg)
        {
            float a = Mathf.Clamp(aoaDeg, 0f, 90f);
            return a <= StallAngle
                ? MaxLift * Mathf.Sin(a / StallAngle * 90f * Mathf.Deg2Rad)
                : MaxLift * Mathf.Cos((a - StallAngle) / (90f - StallAngle) * 90f * Mathf.Deg2Rad);
        }

        public static float Drag(float aoaDeg, float liftCoeff)
        {
            float s = Mathf.Sin(Mathf.Clamp(aoaDeg, 0f, 90f) * Mathf.Deg2Rad);
            return ProfileDrag + BroadsideDrag * s * s + InducedDrag * liftCoeff * liftCoeff;
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
            float dynamicPressure = 0.5f * Mathf.Max(0f, input.AirDensity) * speed * speed;

            // The cloth normal's sign is just mesh winding; the wind always fills the windward face,
            // so orient it to leeward — otherwise lift points to windward/aft and nothing drives upwind.
            Vector3 leeNormal = Vector3.Dot(appDir, input.SailNormal) >= 0f ? input.SailNormal : -input.SailNormal;
            float sinAoA = Mathf.Clamp01(Vector3.Dot(appDir, leeNormal));
            float aoa = Mathf.Asin(sinAoA) * Mathf.Rad2Deg;

            float liftCoeff = SailPolar.Lift(aoa) * Mathf.Clamp(input.LiftFactor, 0f, 2f);
            float dragCoeff = SailPolar.Drag(aoa, liftCoeff) * Mathf.Clamp(input.DragFactor, 0f, 2f);

            Vector3 liftDir = leeNormal - appDir * sinAoA; // ⟂ wind, toward leeward
            liftDir = liftDir.sqrMagnitude > 1e-6f ? liftDir.normalized : Vector3.zero;

            Vector3 rigForce = (appDir * dragCoeff + liftDir * liftCoeff)
                               * (dynamicPressure * input.EffectiveArea * Mathf.Max(0f, input.SailForceScale));

            Vector3 horizontal = Vector3.ProjectOnPlane(rigForce, Vector3.up);
            rigForce = horizontal + Vector3.up * (rigForce.y * Mathf.Clamp01(input.VerticalForceFactor));

            // Only part of the athwartships push reaches the hull (arcade keel: less leeway and heel).
            Vector3 driveForce = rigForce;
            Vector3 shipRight = Vector3.Cross(Vector3.up, input.ShipForward);
            if (shipRight.sqrMagnitude > 1e-6f)
            {
                shipRight.Normalize();
                driveForce -= shipRight * (Vector3.Dot(driveForce, shipRight) * (1f - Mathf.Clamp01(input.SideForceFactor)));
            }

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

