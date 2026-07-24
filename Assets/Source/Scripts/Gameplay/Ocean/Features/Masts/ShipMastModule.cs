using System;
using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean.Features.Masts
{
    /// <summary>
    /// Reusable mast module prefab metadata (visual mast, gameplay colliders, sail anchors).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipMastModule : MonoBehaviour
    {
        [SerializeField] private string moduleId;
        [SerializeField] private Transform mastVisual;
        [SerializeField] private Transform[] ropeVisuals;
        [SerializeField] private Transform sailAnchorRoot;
        [SerializeField] private Transform climbZonesRoot;
        [SerializeField] private Transform interactionZonesRoot;

        public string ModuleId => moduleId;
        public Transform MastVisual => mastVisual;
        public Transform[] RopeVisuals => ropeVisuals;
        public Transform SailAnchorRoot => sailAnchorRoot;
        public Transform ClimbZonesRoot => climbZonesRoot;
        public Transform InteractionZonesRoot => interactionZonesRoot;

        public void Configure(
            string id,
            Transform mast,
            Transform[] ropes,
            Transform anchors,
            Transform climbZones,
            Transform interactionZones)
        {
            moduleId = id;
            mastVisual = mast;
            ropeVisuals = ropes;
            sailAnchorRoot = anchors;
            climbZonesRoot = climbZones;
            interactionZonesRoot = interactionZones;
        }

        public Transform FindAnchor(string anchorName)
        {
            if (sailAnchorRoot == null || string.IsNullOrWhiteSpace(anchorName))
                return null;

            return sailAnchorRoot.Find(anchorName);
        }

        private void OnDrawGizmosSelected()
        {
            if (sailAnchorRoot == null)
                return;

            DrawMastAxisGizmo();

            Dictionary<string, SailQuadAnchors> sailQuads = CollectSailQuadAnchors();
            if (sailQuads.Count == 0)
                return;

            Gizmos.color = new Color(0.15f, 0.75f, 1f, 1f);
            foreach (var pair in sailQuads)
            {
                SailQuadAnchors quad = pair.Value;
                if (!quad.IsComplete)
                    continue;

                Gizmos.DrawLine(quad.TopLeft.position, quad.TopRight.position);
                Gizmos.DrawLine(quad.TopRight.position, quad.BottomRight.position);
                Gizmos.DrawLine(quad.BottomRight.position, quad.BottomLeft.position);
                Gizmos.DrawLine(quad.BottomLeft.position, quad.TopLeft.position);

                // Diagonal cross makes the generated sail panel orientation obvious in Scene view.
                Gizmos.DrawLine(quad.TopLeft.position, quad.BottomRight.position);
                Gizmos.DrawLine(quad.TopRight.position, quad.BottomLeft.position);
                Gizmos.DrawSphere(quad.Center, 0.04f);
            }
        }

        private void DrawMastAxisGizmo()
        {
            Transform mastBase = FindAnchor("MastBase");
            Transform mastTop = FindAnchor("MastTop");
            if (mastBase == null || mastTop == null)
                return;

            Gizmos.color = new Color(1f, 0.65f, 0.1f, 1f);
            Gizmos.DrawLine(mastBase.position, mastTop.position);
            Gizmos.DrawSphere(mastBase.position, 0.045f);
            Gizmos.DrawSphere(mastTop.position, 0.045f);
        }

        private Dictionary<string, SailQuadAnchors> CollectSailQuadAnchors()
        {
            var result = new Dictionary<string, SailQuadAnchors>(StringComparer.Ordinal);
            if (sailAnchorRoot == null)
                return result;

            foreach (Transform anchor in sailAnchorRoot)
            {
                if (anchor == null)
                    continue;

                string anchorName = anchor.name;
                int separator = anchorName.LastIndexOf('_');
                if (separator <= 0 || separator >= anchorName.Length - 1)
                    continue;

                string sailName = anchorName.Substring(0, separator);
                string suffix = anchorName.Substring(separator + 1);
                if (!IsSailCornerSuffix(suffix))
                    continue;

                if (!result.TryGetValue(sailName, out SailQuadAnchors quad))
                    quad = default;

                if (suffix.Equals("TopLeft", StringComparison.Ordinal))
                    quad.TopLeft = anchor;
                else if (suffix.Equals("TopRight", StringComparison.Ordinal))
                    quad.TopRight = anchor;
                else if (suffix.Equals("BottomLeft", StringComparison.Ordinal))
                    quad.BottomLeft = anchor;
                else if (suffix.Equals("BottomRight", StringComparison.Ordinal))
                    quad.BottomRight = anchor;

                result[sailName] = quad;
            }

            return result;
        }

        private static bool IsSailCornerSuffix(string suffix)
        {
            return suffix.Equals("TopLeft", StringComparison.Ordinal) ||
                   suffix.Equals("TopRight", StringComparison.Ordinal) ||
                   suffix.Equals("BottomLeft", StringComparison.Ordinal) ||
                   suffix.Equals("BottomRight", StringComparison.Ordinal);
        }

        private struct SailQuadAnchors
        {
            public Transform TopLeft;
            public Transform TopRight;
            public Transform BottomLeft;
            public Transform BottomRight;

            public bool IsComplete => TopLeft != null && TopRight != null && BottomLeft != null && BottomRight != null;

            public Vector3 Center =>
                (TopLeft.position + TopRight.position + BottomLeft.position + BottomRight.position) * 0.25f;
        }
    }
}

