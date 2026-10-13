#if FUSION2
using System.Collections.Generic;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Rigging
{
    /// <summary>
    /// Client-derived rope visuals (GDD tier C): replicated line state in, curves out.
    ///   aloft (sail corner, swung with the braced yard) → lead block on the mast → free end
    ///   (in hands / on a pin / loose).
    ///
    /// TENSION LOOK: every line gets a tension 0..1 from its replicated load
    /// (<see cref="Tension"/>): a loaded line is drawn straight, an unloaded one hangs in a belly and
    /// sways; an overloaded one shivers. A brace holding the yard at its stop reads taut even in a
    /// calm, the other brace of that yard hangs by the rope the swing has given it. Rope in hand or
    /// loose adds its real excess (rope out minus the straight distance) to the sag; on a pin the
    /// excess goes into the coil. Nothing here is networked or physical.
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
            public float Tension = -1f; // smoothed, -1 = not initialised
        }

        private static readonly Dictionary<RigLine, Visual> Visuals = new();
        private static readonly Vector3[] Points = new Vector3[Segments * 2 + 2];
        private static readonly Dictionary<int, Vector3[]> Exact = new();
        private static Material _fallback;

        public static void Render(NetworkShip ship)
        {
            RiggingConfig cfg = RiggingConfig.Active;
            float time = Time.time;
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

                // Tension, smoothed so a line doesn't snap between straight and slack.
                float target = Tension(ship, i, out float yardSlack);
                v.Tension = v.Tension < 0f
                    ? target
                    : Mathf.Lerp(v.Tension, target, 1f - Mathf.Exp(-Mathf.Max(0.1f, cfg.RopeTensionResponse) * Time.deltaTime));
                float slackLook = 1f - v.Tension;

                float load = ship.GetLineLoad(i);
                float hum = cfg.RopeHumAmplitude * Mathf.Clamp01((load - cfg.RopeHumLoad) / Mathf.Max(0.01f, cfg.RopeHumLoad));
                float humPhase = time * cfg.RopeHumFrequency * Mathf.PI * 2f + i * 1.3f;
                float sway = cfg.RopeSwayAmplitude * slackLook *
                             Mathf.Sin(time * cfg.RopeSwayFrequency * Mathf.PI * 2f + i * 1.7f);

                float rope = ship.GetLineOut(i);
                float span = Vector3.Distance(block, end);
                float excess = Mathf.Max(0f, rope - span);
                // On a pin the spare rope is coiled; in hand / loose it hangs in the span.
                float physical = mode == RigLineMode.Tied ? 0f : excess;
                float deckSlack = cfg.RopeSlackSag * span * slackLook + physical + yardSlack * 0.5f;

                int count = 0;
                if (line.Aloft != null)
                {
                    Vector3 aloft = AloftPoint(ship, line);
                    float aloftSpan = Vector3.Distance(aloft, block);
                    float aloftSlack = 0.01f + cfg.RopeSlackSag * aloftSpan * slackLook + yardSlack;
                    count = WriteSag(aloft, block, aloftSlack, 0, sway * 0.7f, hum, humPhase);
                }
                count = WriteSag(block, end, deckSlack, Mathf.Max(0, count - 1), sway, hum, humPhase + 0.9f);

                v.Line.positionCount = count;
                if (!Exact.TryGetValue(count, out Vector3[] exact))
                    Exact[count] = exact = new Vector3[count];
                System.Array.Copy(Points, exact, count);
                v.Line.SetPositions(exact);

                v.Coil.gameObject.SetActive(mode == RigLineMode.Tied);
                if (mode == RigLineMode.Tied)
                {
                    v.Coil.position = end + Vector3.down * 0.18f;
                    v.Coil.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.45f, Mathf.Clamp01(excess / 4f));
                }
                v.Knot.gameObject.SetActive(mode != RigLineMode.Tied);
                v.Knot.position = end;

                if (!ship.HasStateAuthority)
                    RopeSystem.SyncEndHandle(ship, line, i); // clients aim at where the end is
            }
        }

        /// <summary>
        /// How taut line <paramref name="i"/> is right now, 0 (slack) .. 1 (taut), from its replicated
        /// load (any peer). <paramref name="yardSlack"/>: rope (m) a brace has been given by the yard
        /// swinging away from it (0 for halyards and for the brace holding the yard).
        /// </summary>
        public static float Tension(NetworkShip ship, int i, out float yardSlack)
        {
            yardSlack = 0f;
            RigLine line = ship != null ? ship.Line(i) : null;
            if (line == null)
                return 0f;
            RiggingConfig cfg = RiggingConfig.Active;
            float t = Mathf.Clamp01(ship.GetLineLoad(i) / Mathf.Max(0.001f, cfg.RopeTautLoad));
            if (!line.IsBrace || ship.GetLineMode(i) == RigLineMode.Loose)
                return t;

            int partner = ship.BracePartner(i);
            int port = line.Kind == RigLineKind.BracePort ? i : partner;
            int star = line.Kind == RigLineKind.BraceStarboard ? i : partner;
            YardSystem.Limits(ship, port, star, line.BraceMaxAngle, out float lo, out float hi);
            float angle = ship.GetYardAngle(line.SailIndex);
            // The port brace stops the yard at hi, the starboard one at lo.
            float gap = line.Kind == RigLineKind.BracePort ? hi - angle : angle - lo;
            gap = Mathf.Max(0f, gap);
            if (gap < 0.75f)
                return Mathf.Max(t, cfg.BraceContactTension);
            yardSlack = YardArm(ship, line) * gap * Mathf.Deg2Rad;
            return t * Mathf.Clamp01(1f - gap / 10f);
        }

        public static float Tension(NetworkShip ship, int i) => Tension(ship, i, out _);

        // Where the line goes aloft: its authored point, swung with the sail's yard around the same
        // pivot as the cloth (top-edge middle) so braces / halyards follow the braced yard.
        private static Vector3 AloftPoint(NetworkShip ship, RigLine line)
        {
            Vector3 world = line.Aloft.position;
            if (ship.Sails == null || !ship.Sails.TryGetFlatSail(line.SailIndex, out var mast, out int s))
                return world;
            float angle = mast.GetSheetAngle(s);
            if (Mathf.Abs(angle) < 0.01f)
                return world;
            var sail = mast.Sails[s];
            Vector3 pivot = (sail.topLeftLocal + sail.topRightLocal) * 0.5f;
            Vector3 local = ship.transform.InverseTransformPoint(world);
            local = pivot + Quaternion.AngleAxis(angle, Vector3.up) * (local - pivot);
            return ship.transform.TransformPoint(local);
        }

        private static float YardArm(NetworkShip ship, RigLine line)
        {
            if (line.Aloft == null || ship.Sails == null || !ship.Sails.TryGetFlatSail(line.SailIndex, out var mast, out int s))
                return 3f;
            var sail = mast.Sails[s];
            Vector3 pivot = (sail.topLeftLocal + sail.topRightLocal) * 0.5f;
            Vector3 d = ship.transform.InverseTransformPoint(line.Aloft.position) - pivot;
            d.y = 0f;
            return Mathf.Max(0.5f, d.magnitude);
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
        // sway: sideways swing (m) at mid-span; hum: fast shiver (m) of a strained rope.
        private static int WriteSag(Vector3 from, Vector3 to, float slack, int start, float sway = 0f, float hum = 0f, float phase = 0f)
        {
            float len = Vector3.Distance(from, to);
            float drop = Mathf.Min(Mathf.Sqrt(Mathf.Max(0f, slack) * Mathf.Max(0.01f, len)) * 0.6f, 4f);
            Vector3 along = len > 1e-4f ? (to - from) / len : Vector3.up;
            Vector3 side = Vector3.Cross(along, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            // Gravity hangs the belly straight down; for a near-vertical span (block → aloft) the
            // belly bows sideways instead.
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, along);
            down = down.sqrMagnitude > 0.04f ? down.normalized : side;
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments;
                float belly = 4f * t * (1f - t);
                float shiver = hum * Mathf.Sin(phase + t * 9.4f); // standing wave along the rope
                Vector3 p = Vector3.Lerp(from, to, t) + down * (drop * belly) + side * ((sway + shiver) * belly);
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
