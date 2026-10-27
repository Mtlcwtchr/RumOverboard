using UnityEngine;

namespace RumOverboard.Gameplay.World.Mooring
{
    /// <summary>
    /// Visual only: the two mooring lines (bow + stern) between ship cleats and pier bollards,
    /// drawn as sagging catenary-ish curves. Slack lines sag, taut lines straighten.
    /// </summary>
    public sealed class MooringLineView : MonoBehaviour
    {
        private const int Segments = 14;
        private LineRenderer[] _lines;
        private readonly Vector3[] _points = new Vector3[Segments + 1];

        public static MooringLineView Create(Transform parent, Material material)
        {
            var go = new GameObject("MooringLines");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MooringLineView>();
            view._lines = new LineRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var lineGo = new GameObject(i == 0 ? "BowLine" : "SternLine");
                lineGo.transform.SetParent(go.transform, false);
                var lr = lineGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = Segments + 1;
                lr.widthMultiplier = 0.07f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.sharedMaterial = material;
                lr.startColor = lr.endColor = new Color(0.55f, 0.45f, 0.3f);
                lr.enabled = false;
                view._lines[i] = lr;
            }
            return view;
        }

        public void Hide()
        {
            if (_lines == null)
                return;
            foreach (LineRenderer lr in _lines)
                if (lr != null)
                    lr.enabled = false;
        }

        /// <summary>Draws line <paramref name="index"/> from <paramref name="a"/> to <paramref name="b"/> with rest length <paramref name="rest"/>.</summary>
        public void Draw(int index, Vector3 a, Vector3 b, float rest)
        {
            if (_lines == null || index < 0 || index >= _lines.Length || _lines[index] == null)
                return;
            float span = Vector3.Distance(a, b);
            float slack = Mathf.Max(0f, rest - span);
            // Parabolic sag for a given excess length: s ≈ sqrt(3/8 · span · slack).
            float sag = Mathf.Min(Mathf.Sqrt(0.375f * Mathf.Max(span, 0.1f) * slack), 4f) + 0.05f;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                _points[i] = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }
            LineRenderer lr = _lines[index];
            lr.SetPositions(_points);
            lr.enabled = true;
        }
    }
}
