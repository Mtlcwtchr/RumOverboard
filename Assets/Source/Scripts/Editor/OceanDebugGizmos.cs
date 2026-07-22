using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    /// <summary>
    /// Reusable IMGUI drawing for the ocean/wind/sail debug windows: a top-down "mini-map" with
    /// direction arrows (ship heading, wind, current, dominant wave, sail thrust). Draws with
    /// <see cref="Handles"/> in GUI space (valid during OnGUI Repaint in an EditorWindow), plus
    /// GUI labels — no SceneView camera needed.
    ///
    /// World → map convention: top-down, +Z is up (screen −y), +X is right (screen +x).
    /// </summary>
    public static class OceanDebugGizmos
    {
        public struct MapVector
        {
            public Vector2 worldDir;  // XZ world direction (need not be normalized)
            public float weight01;    // 0..1 length relative to the map radius
            public Color color;
            public string label;
        }

        public sealed class MiniMap
        {
            public bool hasShip;
            public float shipHeadingDeg;                       // ship yaw (transform.eulerAngles.y)
            public readonly List<MapVector> vectors = new List<MapVector>();

            public void Clear()
            {
                hasShip = false;
                vectors.Clear();
            }

            public void Add(string label, Vector2 worldDir, float weight01, Color color)
            {
                vectors.Add(new MapVector
                {
                    worldDir = worldDir,
                    weight01 = Mathf.Clamp01(weight01),
                    color = color,
                    label = label,
                });
            }
        }

        // Shared palette (matches the scene gizmo conventions).
        public static readonly Color ShipColor = new Color(1f, 0.95f, 0.55f, 1f);
        public static readonly Color WindColor = new Color(1f, 0.85f, 0.25f, 1f);
        public static readonly Color CurrentColor = new Color(0.25f, 0.9f, 1f, 1f);
        public static readonly Color WaveColor = new Color(0.35f, 0.6f, 1f, 1f);
        public static readonly Color ThrustColor = new Color(0.4f, 1f, 0.45f, 1f);

        private static Vector2 WorldToMap(Vector2 worldDir) => new Vector2(worldDir.x, -worldDir.y);

        /// <summary>Reserves a square-ish layout rect and draws the mini-map into it.</summary>
        public static void DrawMiniMapLayout(MiniMap map, float height = 190f)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            rect = new RectExtend(rect).Padding(6f);
            DrawMiniMap(rect, map);
        }

        public static void DrawMiniMap(Rect rect, MiniMap map)
        {
            DrawPanelBackground(rect);

            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 14f;
            if (radius < 8f)
                return;

            if (Event.current.type == EventType.Repaint)
            {
                DrawCompassRing(center, radius);

                if (map != null)
                {
                    // Ship: filled triangle pointing along its heading.
                    if (map.hasShip)
                    {
                        Vector2 fwd = HeadingToWorld(map.shipHeadingDeg);
                        DrawShipIcon(center, WorldToMap(fwd), 13f);
                    }

                    for (int i = 0; i < map.vectors.Count; i++)
                    {
                        MapVector v = map.vectors[i];
                        if (v.worldDir.sqrMagnitude < 1e-6f)
                            continue;

                        Vector2 dir = WorldToMap(v.worldDir.normalized);
                        float len = Mathf.Lerp(radius * 0.28f, radius, v.weight01);
                        DrawArrow(center, dir, len, v.color, 2.5f);
                    }
                }
            }

            // Labels (drawn outside the Repaint guard so they layout correctly).
            DrawCompassLabels(center, radius);
            if (map != null)
            {
                for (int i = 0; i < map.vectors.Count; i++)
                {
                    MapVector v = map.vectors[i];
                    if (v.worldDir.sqrMagnitude < 1e-6f)
                        continue;
                    Vector2 dir = WorldToMap(v.worldDir.normalized);
                    float len = Mathf.Lerp(radius * 0.28f, radius, v.weight01);
                    DrawTipLabel(center + dir * (len + 2f), v.label, v.color);
                }
            }
        }

        /// <summary>A dedicated wind compass: wind arrow + optional ship heading + apparent-wind angle.</summary>
        public static void DrawWindCompassLayout(float windDeg, float windStrength01, bool hasShip, float shipHeadingDeg, float height = 190f)
        {
            var map = new MiniMap { hasShip = hasShip, shipHeadingDeg = shipHeadingDeg };
            map.Add($"Wind {(int)windStrength01Pretty(windStrength01)}", HeadingToWorld(windDeg), Mathf.Clamp01(windStrength01), WindColor);
            DrawMiniMapLayout(map, height);
        }

        private static float windStrength01Pretty(float s) => Mathf.Round(Mathf.Clamp01(s) * 100f);

        // --- primitives --------------------------------------------------------

        private static void DrawPanelBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.11f, 0.13f, 0.16f, 1f));
            // 1px border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.5f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.5f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.5f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.5f));
        }

        private static void DrawCompassRing(Vector2 center, float radius)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.14f);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius * 0.5f);

            // Cross hairs.
            Handles.color = new Color(1f, 1f, 1f, 0.09f);
            Handles.DrawLine(new Vector3(center.x - radius, center.y), new Vector3(center.x + radius, center.y));
            Handles.DrawLine(new Vector3(center.x, center.y - radius), new Vector3(center.x, center.y + radius));
        }

        private static void DrawCompassLabels(Vector2 center, float radius)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } };
            GUI.Label(new Rect(center.x - 6f, center.y - radius - 14f, 20f, 14f), "N", style);
            GUI.Label(new Rect(center.x - 6f, center.y + radius, 20f, 14f), "S", style);
            GUI.Label(new Rect(center.x + radius + 2f, center.y - 7f, 20f, 14f), "E", style);
            GUI.Label(new Rect(center.x - radius - 12f, center.y - 7f, 20f, 14f), "W", style);
        }

        private static void DrawArrow(Vector2 origin, Vector2 dir, float length, Color color, float width)
        {
            Handles.color = color;
            Vector2 tip = origin + dir * length;
            Handles.DrawAAPolyLine(width, new Vector3(origin.x, origin.y), new Vector3(tip.x, tip.y));

            Vector2 back = -dir;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float head = Mathf.Clamp(length * 0.22f, 6f, 14f);
            Vector2 h1 = tip + (back + perp * 0.55f) * head;
            Vector2 h2 = tip + (back - perp * 0.55f) * head;
            Handles.DrawAAPolyLine(width, new Vector3(h1.x, h1.y), new Vector3(tip.x, tip.y), new Vector3(h2.x, h2.y));
        }

        private static void DrawShipIcon(Vector2 center, Vector2 fwd, float size)
        {
            Vector2 perp = new Vector2(-fwd.y, fwd.x);
            Vector3 nose = new Vector3(center.x + fwd.x * size, center.y + fwd.y * size);
            Vector3 tailL = new Vector3(center.x - fwd.x * size * 0.7f + perp.x * size * 0.55f, center.y - fwd.y * size * 0.7f + perp.y * size * 0.55f);
            Vector3 tailR = new Vector3(center.x - fwd.x * size * 0.7f - perp.x * size * 0.55f, center.y - fwd.y * size * 0.7f - perp.y * size * 0.55f);

            Handles.color = ShipColor;
            Handles.DrawAAConvexPolygon(nose, tailL, tailR);
            Handles.color = new Color(0f, 0f, 0f, 0.6f);
            Handles.DrawAAPolyLine(1.5f, nose, tailL, tailR, nose);
        }

        private static void DrawTipLabel(Vector2 pos, string text, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            GUI.Label(new Rect(pos.x - 2f, pos.y - 7f, 120f, 14f), text, style);
        }

        // Unity yaw (deg) → world XZ forward. 0° = +Z, 90° = +X (matches OceanWindSystem/profile swell).
        public static Vector2 HeadingToWorld(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        // Small helper to inset a rect.
        private readonly struct RectExtend
        {
            private readonly Rect _r;
            public RectExtend(Rect r) { _r = r; }
            public Rect Padding(float p) => new Rect(_r.x + p, _r.y + p, _r.width - p * 2f, _r.height - p * 2f);
            public static implicit operator Rect(RectExtend e) => e._r;
        }
    }
}
