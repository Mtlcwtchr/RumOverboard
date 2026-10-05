using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// Diagnostics: instantiates the network ship prefab in a temp scene and logs ship-local
    /// geometry (mast anchors, helm, deck heights, platforms). Used to author climb surfaces /
    /// spawn points and to sanity-check the prefab from batch mode.
    /// </summary>
    public static class ShipGeometryProbe
    {
        private const string ShipPrefabPath = "Assets/Source/Prefabs/NetworkShip.prefab";

        [MenuItem("RumOverboard/Ship Sandbox/Diagnostics/Log Ship Geometry")]
        public static void LogShipGeometry()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ship.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();

            var sb = new StringBuilder("[ShipGeometryProbe]\n");
            Transform root = ship.transform;

            Bounds b = new Bounds(root.position, Vector3.zero);
            int active = 0;
            foreach (Collider c in ship.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                active++;
                b.Encapsulate(c.bounds);
            }
            sb.AppendLine($"active colliders={active} bounds center={b.center} size={b.size}");

            foreach (Rigidbody rb in ship.GetComponentsInChildren<Rigidbody>(true))
                sb.AppendLine($"rigidbody {Path(rb.transform, root)} mass={rb.mass} kin={rb.isKinematic} active={rb.gameObject.activeInHierarchy}");

            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == "MastBase" || n == "MastTop" || n.EndsWith("Module") || n.Contains("Wheel") ||
                    n.Contains("Helm") || n.Contains("Stand") || n == "Anchor" || n.Contains("Ladder") || n.Contains("Stair"))
                    sb.AppendLine($"node {Path(t, root)} local={root.InverseTransformPoint(t.position)} activeInHierarchy={t.gameObject.activeInHierarchy}");
            }

            // Deck height grid (ship local x/z), first upward-facing hit from above.
            sb.AppendLine("deck grid (x,z -> y, collider):");
            for (float z = Mathf.Floor(b.min.z); z <= b.max.z; z += 2f)
            {
                var line = new StringBuilder($"  z={z,6:F1}:");
                for (float x = -4f; x <= 4f; x += 2f)
                {
                    if (Physics.Raycast(new Vector3(x, b.max.y + 1f, z), Vector3.down, out RaycastHit hit, b.size.y + 5f) && hit.normal.y > 0.7f)
                        line.Append($" [{x:F0}:{hit.point.y:F2} {hit.collider.name}]");
                    else
                        line.Append($" [{x:F0}:-]");
                }
                sb.AppendLine(line.ToString());
            }

            // Platforms around each mast.
            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "MastBase") continue;
                Transform top = t.parent != null ? t.parent.Find("MastTop") : null;
                if (top == null) continue;
                sb.AppendLine($"mast {Path(t.parent, root)} base={t.position} top={top.position}");
                for (float y = t.position.y + 1f; y < top.position.y; y += 0.5f)
                {
                    var line = new StringBuilder();
                    foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        Vector3 o = new Vector3(t.position.x, y, t.position.z) + dir * 0.9f;
                        if (Physics.Raycast(o + Vector3.up * 0.45f, Vector3.down, out RaycastHit hit, 0.5f) && hit.normal.y > 0.8f)
                            line.Append($" {dir}:{hit.point.y:F2}({hit.collider.name})");
                    }
                    if (line.Length > 0)
                        sb.AppendLine($"  y={y:F1}{line}");
                }
                // Trunk thickness: ray inward from 2m out at mid height.
                Vector3 mid = Vector3.Lerp(t.position, top.position, 0.3f);
                if (Physics.Raycast(mid + Vector3.back * 2f, Vector3.forward, out RaycastHit th, 2f))
                    sb.AppendLine($"  trunk back surface at {th.distance:F2}m from 2m probe ({th.collider.name})");
            }

            Debug.Log(sb.ToString());
        }

        public static void LogQuarterdeckAccess()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath));
            ship.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();
            var sb = new StringBuilder("[QuarterdeckProbe] rows z, cols x=-6..8 step .5 (deck y, '#' = steep, '.' = none)\n");
            for (float z = -9f; z <= -2f; z += 0.25f)
            {
                sb.Append($"z={z,6:F2}:");
                for (float x = -6f; x <= 8f; x += 0.5f)
                {
                    if (Physics.Raycast(new Vector3(x, 4.5f, z), Vector3.down, out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Ignore))
                        sb.Append(h.normal.y > 0.7f ? $" {h.point.y,4:F1}" : "   #");
                    else sb.Append("    .");
                }
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
        }

        private static string Path(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null && t != root)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }
    }
}
