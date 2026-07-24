using RumOverboard.Gameplay.Ocean.Simulation;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public partial class ShipSailSystem
    {
        private void ApplySailForcesCore(SailPanel sail, Vector3 trueWind, bool applyForces)
        {
            Vector3 apparentWind = trueWind - shipBody.linearVelocity;
            if (apparentWind.sqrMagnitude < 0.01f)
            {
                sail.lastForce = Vector3.zero;
                return;
            }

            float deployment = sail.hoist01 * Mathf.Lerp(0.15f, 1f, sail.extension01);
            float effectiveArea = sail.baseArea * deployment * (1f - sail.damage01);
            if (effectiveArea <= 0.001f)
            {
                sail.lastForce = Vector3.zero;
                return;
            }

            Vector3 sailNormal = ComputeAggregateNormalCore(sail);
            SailAerodynamicsResult aero = SailAerodynamicsSolver.Solve(new SailAerodynamicsInput(
                apparentWind,
                sailNormal,
                transform.forward,
                effectiveArea,
                airDensity,
                sailForceScale,
                maxForcePerSail,
                liftFactor,
                dragFactor,
                sideForceFactor,
                reverseDriveFactor,
                verticalForceFactor,
                blockReverseDriveFromHeadwind));

            Vector3 rigForce = aero.RigForce;
            Vector3 driveForce = aero.DriveForce;
            Vector3 forcePoint = ComputeSailCenterCore(sail);

            sail.lastForce = driveForce;
            sail.lastForcePoint = forcePoint;

            if (applyForces && driveForce.sqrMagnitude > 0.0001f)
                shipBody.AddForceAtPosition(driveForce, forcePoint, ForceMode.Force);

            if (applyForces && rigForce.sqrMagnitude > 0.0001f && sail.mastIndex >= 0 && sail.mastIndex < masts.Length)
            {
                masts[sail.mastIndex].totalForceOnMast += rigForce;
                sail.lastMastMoment = ComputeMastMomentContributionCore(sail, rigForce);
                masts[sail.mastIndex].currentBendingMoment += sail.lastMastMoment;
            }
        }

        private Vector3 ComputeAggregateNormalCore(SailPanel sail)
        {
            Vector3 normal = Vector3.zero;
            int w = sail.gridWidth;
            int h = sail.gridHeight;

            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    Vector3 p00 = sail.positions[sail.GetIndex(x, y)];
                    Vector3 p10 = sail.positions[sail.GetIndex(x + 1, y)];
                    Vector3 p01 = sail.positions[sail.GetIndex(x, y + 1)];
                    normal += Vector3.Cross(p10 - p00, p01 - p00);
                }
            }

            if (normal.sqrMagnitude < 0.0001f)
                return transform.forward;
            return normal.normalized;
        }

        private Vector3 ComputeSailCenterCore(SailPanel sail)
        {
            Vector3 center = Vector3.zero;
            int count = sail.ParticleCount;
            for (int i = 0; i < count; i++)
                center += sail.positions[i];
            return center / Mathf.Max(1, count);
        }

        private void ComputeMastStrengthsCore()
        {
            if (masts == null)
                return;

            for (int i = 0; i < masts.Length; i++)
            {
                if (masts[i] == null)
                    continue;

                var mast = masts[i];
                mast.maxBendingMoment = MastStressSolver.ComputeMaxBendingMoment(mast.radius, mast.yieldStrength);
            }
        }

        private void ResetMastForcesCore()
        {
            if (masts == null)
                return;

            for (int i = 0; i < masts.Length; i++)
            {
                if (masts[i] == null)
                    continue;

                masts[i].totalForceOnMast = Vector3.zero;
                masts[i].currentBendingMoment = 0f;
            }
        }

        private float ComputeMastMomentContributionCore(SailPanel sail, Vector3 force)
        {
            if (sail.mastIndex < 0 || sail.mastIndex >= masts.Length)
                return 0f;

            var mast = masts[sail.mastIndex];
            Vector3 mastBase = transform.TransformPoint(mast.baseLocal);
            Vector3 mastTop = transform.TransformPoint(mast.topLocal);
            return MastStressSolver.ComputeMomentContribution(mastBase, mastTop, sail.mastAttachHeight, force);
        }

        private void ComputeMastStressCore()
        {
            if (masts == null)
                return;

            for (int i = 0; i < masts.Length; i++)
            {
                if (masts[i] == null)
                    continue;

                var mast = masts[i];
                mast.stressRatio = MastStressSolver.ComputeStressRatio(mast.currentBendingMoment, mast.maxBendingMoment);
            }
        }
    }
}

