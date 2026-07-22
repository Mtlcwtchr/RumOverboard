using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public abstract class OceanCurrentZoneBase : MonoBehaviour, IOceanCurrentZone
    {
        [SerializeField] protected float strength = 0.4f;
        [SerializeField] protected AnimationCurve falloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        public abstract Vector3 EvaluateCurrent(Vector3 worldPos, float time);

        protected float EvaluateFalloff01(float normalizedDistance)
        {
            return Mathf.Max(0f, falloff.Evaluate(Mathf.Clamp01(normalizedDistance)));
        }
    }

    public sealed class DirectionalCurrentZone : OceanCurrentZoneBase
    {
        [SerializeField] private Vector3 localDirection = Vector3.forward;
        [SerializeField] private Vector3 localExtents = new Vector3(12f, 5f, 26f);

        public override Vector3 EvaluateCurrent(Vector3 worldPos, float time)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            Vector3 ext = localExtents;
            if (Mathf.Abs(local.x) > ext.x || Mathf.Abs(local.y) > ext.y || Mathf.Abs(local.z) > ext.z)
                return Vector3.zero;

            float nx = Mathf.Abs(local.x) / Mathf.Max(0.001f, ext.x);
            float ny = Mathf.Abs(local.y) / Mathf.Max(0.001f, ext.y);
            float nz = Mathf.Abs(local.z) / Mathf.Max(0.001f, ext.z);
            float edge = Mathf.Max(nx, Mathf.Max(ny, nz));

            float w = EvaluateFalloff01(edge);
            Vector3 dir = transform.TransformDirection(localDirection.normalized);
            return dir * (strength * w);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.7f, 1f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, localExtents * 2f);
            Gizmos.DrawLine(Vector3.zero, localDirection.normalized * 2f);
        }
#endif
    }

    public sealed class VortexCurrentZone : OceanCurrentZoneBase
    {
        [SerializeField] private float radius = 16f;
        [SerializeField] private float height = 8f;
        [SerializeField] private bool clockwise = true;

        public override Vector3 EvaluateCurrent(Vector3 worldPos, float time)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            if (Mathf.Abs(local.y) > height * 0.5f)
                return Vector3.zero;

            Vector2 p = new Vector2(local.x, local.z);
            float dist = p.magnitude;
            if (dist > radius || dist < 0.01f)
                return Vector3.zero;

            Vector2 tangent = clockwise
                ? new Vector2(p.y, -p.x)
                : new Vector2(-p.y, p.x);
            tangent.Normalize();

            float w = EvaluateFalloff01(dist / radius);
            Vector3 worldDir = transform.TransformDirection(new Vector3(tangent.x, 0f, tangent.y));
            return worldDir * (strength * w);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }

    public sealed class SplineCurrentZone : OceanCurrentZoneBase
    {
        [SerializeField] private Transform[] points = new Transform[0];
        [SerializeField] private float width = 8f;

        public override Vector3 EvaluateCurrent(Vector3 worldPos, float time)
        {
            if (points == null || points.Length < 2)
                return Vector3.zero;

            float bestSqr = float.MaxValue;
            Vector3 bestDir = Vector3.zero;

            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                if (a == null || b == null)
                    continue;

                Vector3 ab = b.position - a.position;
                float lenSqr = ab.sqrMagnitude;
                if (lenSqr < 0.0001f)
                    continue;

                float t = Mathf.Clamp01(Vector3.Dot(worldPos - a.position, ab) / lenSqr);
                Vector3 closest = a.position + ab * t;
                float sqr = (worldPos - closest).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestDir = ab.normalized;
                }
            }

            if (bestSqr == float.MaxValue)
                return Vector3.zero;

            float dist = Mathf.Sqrt(bestSqr);
            if (dist > width)
                return Vector3.zero;

            float w = EvaluateFalloff01(dist / Mathf.Max(0.001f, width));
            return bestDir * (strength * w);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (points == null || points.Length < 2)
                return;

            Gizmos.color = new Color(0f, 0.6f, 1f, 0.8f);
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null)
                    continue;

                Gizmos.DrawLine(points[i].position, points[i + 1].position);
                Gizmos.DrawWireSphere(points[i].position, width * 0.2f);
            }
        }
#endif
    }
}

