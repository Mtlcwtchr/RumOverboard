using System.Runtime.CompilerServices;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Generation
{
    /// <summary>
    /// Deterministic, allocation-free RNG (xorshift64*, seeded through splitmix64). The world must
    /// come out bit-identical on every peer from the replicated seed, so nothing here touches
    /// UnityEngine.Random (global state) or Mathf.PerlinNoise (not guaranteed across platforms).
    /// </summary>
    public struct WorldRandom
    {
        private ulong _state;

        public WorldRandom(int seed, int stream = 0)
        {
            ulong s = (ulong)(uint)seed | ((ulong)(uint)stream << 32);
            _state = SplitMix(ref s);
            if (_state == 0) _state = 0x9E3779B97F4A7C15UL;
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>[0, 1)</summary>
        public float Value() => (NextULong() >> 40) * (1f / (1 << 24));

        public float Range(float min, float max) => min + (max - min) * Value();

        /// <summary>[min, maxExclusive)</summary>
        public int Range(int min, int maxExclusive) =>
            maxExclusive <= min ? min : min + (int)(NextULong() % (ulong)(maxExclusive - min));

        public float Signed() => Value() * 2f - 1f;

        public Vector2 OnUnitCircle()
        {
            float a = Value() * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        public int NextSeed() => (int)(NextULong() >> 33);
    }

    /// <summary>Hash-based value noise / fBm — deterministic and thread-safe.</summary>
    public static class WorldNoise
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x27D4EB2Du ^ (uint)x * 0x165667B1u ^ (uint)y * 0x9E3779B1u;
                h ^= h >> 15;
                h *= 0x85EBCA77u;
                h ^= h >> 13;
                h *= 0xC2B2AE3Du;
                h ^= h >> 16;
                return (h & 0xFFFFFF) * (1f / 0xFFFFFF);
            }
        }

        /// <summary>Smooth value noise in [0, 1].</summary>
        public static float Value(float x, float y, int seed)
        {
            float fx = Mathf.Floor(x), fy = Mathf.Floor(y);
            int ix = (int)fx, iy = (int)fy;
            float tx = x - fx, ty = y - fy;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>Fractal value noise in roughly [-1, 1].</summary>
        public static float Fbm(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Value(x, y, seed + i * 1013) * 2f - 1f) * amp;
                norm += amp;
                amp *= 0.5f;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f - 9.7f;
            }
            return sum / norm;
        }

        /// <summary>Cheap per-pixel hash in [0, 1] (stipple, paper grain).</summary>
        public static float White(int x, int y, int seed) => Hash(x, y, seed);
    }
}
