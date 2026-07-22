using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    internal static class OceanWaveMath
    {
        public struct Accumulator
        {
            public Vector3 displacement;
            public float dHdX;
            public float dHdZ;
            public float verticalVelocity;
            public Vector3 horizontalVelocity;

            public void Add(in Accumulator rhs)
            {
                displacement += rhs.displacement;
                dHdX += rhs.dHdX;
                dHdZ += rhs.dHdZ;
                verticalVelocity += rhs.verticalVelocity;
                horizontalVelocity += rhs.horizontalVelocity;
            }
        }

        public static Accumulator EvaluateWave(
            in OceanWaveDefinition wave,
            Vector2 positionXZ,
            float time,
            float directionJitterDegrees,
            uint seed,
            int waveIndex,
            bool physicsPass)
        {
            var accum = new Accumulator();
            if (!wave.IsValid)
                return accum;

            float weight = physicsPass ? wave.physicsWeight : wave.visualWeight;
            if (weight <= 0.0001f)
                return accum;

            Vector2 dir = ApplyDirectionalJitter(wave.DirectionNormalized, directionJitterDegrees, seed, waveIndex);
            float k = (2f * Mathf.PI / Mathf.Max(0.01f, wave.wavelength)) * Mathf.Max(0.001f, wave.frequency);
            float c = wave.speed;
            float phase = k * Vector2.Dot(dir, positionXZ) - k * c * time + wave.phase;

            float sin = Mathf.Sin(phase);
            float cos = Mathf.Cos(phase);

            float amp = wave.amplitude * weight;
            float steep = Mathf.Clamp01(wave.steepness);
            float qa = steep * amp;

            accum.displacement.x = qa * dir.x * cos;
            accum.displacement.y = amp * sin;
            accum.displacement.z = qa * dir.y * cos;

            float slope = amp * k * cos;
            accum.dHdX = slope * dir.x;
            accum.dHdZ = slope * dir.y;

            accum.verticalVelocity = -amp * k * c * cos;
            float horizSpeed = qa * k * c * sin;
            accum.horizontalVelocity = new Vector3(horizSpeed * dir.x, 0f, horizSpeed * dir.y);

            return accum;
        }

        public static Vector3 BuildNormal(float dHdX, float dHdZ)
        {
            return new Vector3(-dHdX, 1f, -dHdZ).normalized;
        }

        private static Vector2 ApplyDirectionalJitter(Vector2 dir, float jitterDeg, uint seed, int index)
        {
            if (jitterDeg <= 0.001f)
                return dir;

            float rnd = Hash01(seed, (uint)index);
            float jitter = Mathf.Lerp(-jitterDeg, jitterDeg, rnd) * Mathf.Deg2Rad;
            float s = Mathf.Sin(jitter);
            float c = Mathf.Cos(jitter);
            return new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c).normalized;
        }

        private static float Hash01(uint seed, uint index)
        {
            uint x = seed ^ (index * 0x9E3779B9u + 0x7F4A7C15u);
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }
    }
}

