#if FUSION2
using System.Collections.Generic;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Client-derived rope visuals (GDD tier C): replicated line state in, curves out.
    ///   aloft (sail corner) → lead block on the mast → free end (in hands / on a pin / loose).
    /// The deck-side span sags by its slack (rope length minus straight distance) so a taut line
    /// reads taut and a slack one hangs. A tied line shows a coil on its pin; a loose end a knot.
    /// Nothing here is networked or physical.
    /// </summary>
    public static class RopeRenderSystem
    {
        private const int Segments = 16;

        private sealed class Visual
        {
            public LineRenderer Line;
            public Transform Coil;
            public Transform Knot;
            public Vector3 SmoothedEnd;
            public bool HasEnd;
        }

        private static readonly Dictionary<RigLine, Visual> Visuals = new();
        private static readonly Vector3[] Points = new Vector3[Segments * 2 + 2];
        private static readonly Dictionary<int, Vector3[]> Exact = new();
        private static Material _fallback;

        public static void Render(NetworkShip ship)
        {
            for (int i = 0; i < NetworkShip.MaxLines; i++)
            {
                RigLine line = ship.Line(i);
                if (line == null)
                    continue;

                Visual v = Get(line);
                RigLineMode mode = ship.GetLineMode(i);
                Vector3 block = line.Block.position;

                Vector3 end;
                if (mode == RigLineMode.Held && RopeSystem.FindPlayer(ship.GetLineHolder(i)) is { } holder)
                    end = RopeSystem.HandPosition(holder.RenderPosition, holder.ViewYaw);
                else if (mode == RigLineMode.Tied && ship.GetLinePin(i) is { } pin)
                    end = pin.TiePoint;
                else
                    end = ship.transform.TransformPoint(ship.GetLineEndLocal(i));

                // Loose ends update at tick rate: smooth them in ship space for display.
                if (mode == RigLineMode.Loose && v.HasEnd)
                {
                    Vector3 prev = ship.transform.TransformPoint(v.SmoothedEnd);
                    end = Vector3.Lerp(prev, end, 1f - Mathf.Exp(-25f * Time.deltaTime));
                }
                v.SmoothedEnd = ship.transform.InverseTransformPoint(end);
                v.HasEnd = true;

                float rope = ship.GetLineOut(i);
                int count = 0;
                if (line.Aloft != null)
                    count = WriteSag(line.Aloft.position, block, 0.02f, 0);
                count = WriteSag(block, end, Mathf.Max(0f, rope - Vector3.Distance(block, end)), Mathf.Max(0, count - 1));

                v.Line.positionCount = count;
                if (!Exact.TryGetValue(count, out Vector3[] exact))
                    Exact[count] = exact = new Vector3[count];
                System.Array.Copy(Points, exact, count);
                v.Line.SetPositions(exact);

                v.Coil.gameObject.SetActive(mode == RigLineMode.Tied);
                if (mode == RigLineMode.Tied)
                    v.Coil.position = end + Vector3.down * 0.18f;
                v.Knot.gameObject.SetActive(mode != RigLineMode.Tied);
                v.Knot.position = end;

                if (!ship.HasStateAuthority)
                    RopeSystem.SyncEndHandle(ship, line, i); // clients aim at where the end is
            }
        }

        public static void Forget(NetworkShip ship)
        {
            var dead = new List<RigLine>();
            foreach (var kv in Visuals)
                if (kv.Key == null || kv.Key.Ship == ship)
                    dead.Add(kv.Key);
            foreach (RigLine l in dead)
            {
                if (Visuals.TryGetValue(l, out Visual v) && v.Line != null)
                    Object.Destroy(v.Line.gameObject);
                Visuals.Remove(l);
            }
        }

        // Rope between two points with a catenary-ish sag from its slack (m); returns next index.
        private static int WriteSag(Vector3 from, Vector3 to, float slack, int start, float hum = 0f, float phase = 0f)
        {
            float len = Vector3.Distance(from, to);
            float drop = Mathf.Min(Mathf.Sqrt(Mathf.Max(0f, slack) * Mathf.Max(0.01f, len)) * 0.6f, 4f);
            Vector3 along = len > 1e-4f ? (to - from) / len : Vector3.up;
            Vector3 side = Vector3.Cross(along, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            float shiver = hum * Mathf.Sin(phase);
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments;
                float belly = 4f * t * (1f - t);
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.down * (drop * belly) + side * (shiver * belly);
                Points[start + s] = p;
            }
            return start + Segments + 1;
        }

        private static Visual Get(RigLine line)
        {
            if (Visuals.TryGetValue(line, out Visual v) && v.Line != null)
                return v;

            Material mat = line.RopeMaterial != null ? line.RopeMaterial : Fallback();
            var root = new GameObject($"RopeVisual_{line.DisplayName}");
            root.layer = 2; // Ignore Raycast: visuals never block the view ray
            var lr = root.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.04f;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Tile;
            lr.sharedMaterial = mat;

            v = new Visual
            {
                Line = lr,
                Coil = MakeCoil(root.transform, mat),
                Knot = MakePrimitive(PrimitiveType.Sphere, root.transform, mat, Vector3.one * 0.12f),
            };
            Visuals[line] = v;
            return v;
        }

        // A hanging coil of rope (stacked flattened rings).
        private static Transform MakeCoil(Transform parent, Material mat)
        {
            var coil = new GameObject("Coil").transform;
            coil.SetParent(parent, false);
            for (int k = 0; k < 3; k++)
            {
                Transform ring = MakePrimitive(PrimitiveType.Cylinder, coil, mat, new Vector3(0.22f - k * 0.03f, 0.02f, 0.22f - k * 0.03f));
                ring.localPosition = new Vector3(0f, -k * 0.05f, 0f);
                ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            return coil;
        }

        private static Transform MakePrimitive(PrimitiveType type, Transform parent, Material mat, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = 2;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Material Fallback()
        {
            if (_fallback != null)
                return _fallback;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _fallback = new Material(shader) { name = "RopeRuntime" };
            Color rope = new Color(0.62f, 0.5f, 0.32f);
            if (_fallback.HasProperty("_BaseColor")) _fallback.SetColor("_BaseColor", rope);
            _fallback.color = rope;
            return _fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Visuals.Clear();
            _fallback = null;
        }
    }
}
#endif
