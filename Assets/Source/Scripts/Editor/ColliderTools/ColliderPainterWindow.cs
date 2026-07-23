using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.ColliderTools
{
    /// <summary>
    /// Manual box-collider painter. You place points on the ship surface (clicks raycast the
    /// ship's own colliders), connect them, then "bind" a set of points into a cross-section
    /// FACE — the collider is built symmetrically around that face along its normal (2 points =
    /// a beam of the brush width). Supports a Draw brush and a Select brush (with drag-select),
    /// Duplicate (edges become quad faces), a move handle, copy/paste and mirror.
    /// </summary>
    public class ColliderPainterWindow : EditorWindow
    {
        private const string GeneratedRootName = "SketchColliders";

        private enum Brush { Draw, Select }

        [System.Serializable]
        private class SNode
        {
            public Vector3 Local;
            public Vector3 LocalNormal = Vector3.up;
            public bool Bound;
        }

        [System.Serializable]
        private class SEdge
        {
            public int A;
            public int B;
        }

        [System.Serializable]
        private class SFace
        {
            public List<int> Nodes = new List<int>();
        }

        private class ClipData
        {
            public List<Vector3> Pos = new List<Vector3>();
            public List<Vector3> Nrm = new List<Vector3>();
            public List<Vector2Int> Edges = new List<Vector2Int>();
            public List<int[]> Faces = new List<int[]>();
        }

        [SerializeField] private GameObject _target;
        [SerializeField] private List<SNode> _nodes = new List<SNode>();
        [SerializeField] private List<SEdge> _edges = new List<SEdge>();
        [SerializeField] private List<SFace> _faces = new List<SFace>();
        [SerializeField] private List<int> _selection = new List<int>();

        [SerializeField] private float _width = 0.5f;
        [SerializeField] private float _thickness = 0.2f;
        [SerializeField] private bool _active;          // tool active
        [SerializeField] private Brush _brush = Brush.Draw;
        [SerializeField] private bool _moveTool = true;
        [SerializeField] private bool _showGrid = true;
        [SerializeField] private bool _previewColliders = true;

        [SerializeField] private int _step = -1;        // node the running step continues from (-1 = detached)
        [SerializeField] private int _lastNode = -1;

        [SerializeField] private Vector2 _pointsScroll;
        [SerializeField] private bool _showPoints = true;
        [SerializeField] private bool _showSelection = true;

        // Transient.
        private bool _marquee;
        private bool _marqueeAdditive;
        private Vector2 _marqueeStart;
        private Vector2 _marqueeEnd;
        private ClipData _clip;

        private const float PickPixelRadius = 12f;

        [MenuItem("RumOverboard/Colliders/Collider Painter", false, 20)]
        public static void Open()
        {
            var w = GetWindow<ColliderPainterWindow>(false, "Collider Painter", true);
            w.minSize = new Vector2(320f, 460f);
            if (w._target == null && Selection.activeGameObject != null)
                w._target = Selection.activeGameObject;
            w.Show();
        }

        private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
        private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        // ------------------------------------------------------------------
        // Window UI
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _target = (GameObject)EditorGUILayout.ObjectField("Ship", _target, typeof(GameObject), true);
                if (GUILayout.Button("Use Selection", GUILayout.Width(110f)))
                    _target = Selection.activeGameObject;
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_target == null))
            {
                GUI.backgroundColor = _active ? Color.green : Color.white;
                if (GUILayout.Button(_active ? "Editing… (click to exit)" : "Enter Edit Mode", GUILayout.Height(28f)))
                {
                    _active = !_active;
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }

            using (new EditorGUI.DisabledScope(!_active))
            {
                _brush = (Brush)GUILayout.Toolbar((int)_brush, new[] { "Draw", "Select" });
                if (_brush == Brush.Select)
                    _moveTool = EditorGUILayout.Toggle("Move Handle", _moveTool);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            _width = Mathf.Max(0.02f, EditorGUILayout.FloatField("Width", _width));
            _thickness = Mathf.Max(0.02f, EditorGUILayout.FloatField("Thickness", _thickness));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gizmos", EditorStyles.boldLabel);
            _showGrid = EditorGUILayout.Toggle("Show Grid", _showGrid);
            _previewColliders = EditorGUILayout.Toggle("Preview Colliders", _previewColliders);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Binding — Faces: {_faces.Count}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bind Selected"))
                    BindSelected();
                if (GUILayout.Button("Unbind Selected"))
                    UnbindSelected();
            }
            using (new EditorGUI.DisabledScope(_selection.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Duplicate"))
                    Duplicate();
                if (GUILayout.Button("Copy"))
                    Copy();
                if (GUILayout.Button("Paste"))
                    Paste();
            }
            using (new EditorGUI.DisabledScope(_selection.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Mirror Horizontally"))
                    Mirror(Vector3.right);
                if (GUILayout.Button("Mirror Vertically"))
                    Mirror(Vector3.up);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Colliders"))
                    BuildColliders();
                if (GUILayout.Button("Clear Selection"))
                    _selection.Clear();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Delete Selected"))
                    DeleteSelected();
                if (GUILayout.Button("Clear Sketch"))
                    ClearSketch();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_target == null))
                if (GUILayout.Button("Add Mesh Colliders To Ship"))
                    AddMeshColliders(_target);
            if (GUILayout.Button("Create Painting Scene (from StylShip prefab)"))
                CreatePaintingScene();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Native Colliders", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("Disable ALL Native Colliders", GUILayout.Height(24f)))
                    SetNativeColliders(false);
                if (GUILayout.Button("Enable ALL Native Colliders"))
                    SetNativeColliders(true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Draw brush: click ship to place; next click connects. Click an existing point to close a loop.\n" +
                "Space: detach/attach the running step.\n" +
                "Select brush: drag = marquee, Shift = add. Move Handle drags selected points on axes.\n" +
                "Bind Selected: turn selected points into a cross-section face (2 = beam). Duplicate: edges → quad faces.\n" +
                "Ctrl+C / Ctrl+V copy/paste, Ctrl+D duplicate, Delete removes.",
                MessageType.Info);

            DrawSelectionList();
            DrawPointsList();
        }

        // ------------------------------------------------------------------
        // Scene interaction
        // ------------------------------------------------------------------

        private void OnSceneGUI(SceneView sv)
        {
            if (!_active || _target == null)
                return;

            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = TryPickSurface(ray, out Vector3 hitPoint, out Vector3 hitNormal);

            DrawSketch();
            DrawOverlay(hasHit);
            if (hasHit)
                DrawCursor(hitPoint, hitNormal);
            if (_brush == Brush.Draw && _step >= 0 && hasHit)
                Handles.DrawLine(NodeWorld(_step), hitPoint);
            if (_marquee)
                DrawMarquee();

            if (_brush == Brush.Select && _moveTool && _selection.Count > 0)
                DoMoveHandle();

            bool ours = HandleUtility.nearestControl == id;

            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && ours)
                    {
                        if (_brush == Brush.Draw)
                        {
                            if (e.shift) BeginMarquee(e, true);
                            else if (hasHit) PlaceOrConnect(hitPoint, hitNormal);
                        }
                        else
                        {
                            BeginMarquee(e, e.shift);
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_marquee) { _marqueeEnd = e.mousePosition; e.Use(); }
                    break;

                case EventType.MouseUp:
                    if (_marquee) { ApplyMarquee(); _marquee = false; e.Use(); }
                    break;

                case EventType.KeyDown:
                    HandleKey(e);
                    break;
            }

            sv.Repaint();
            Repaint();
        }

        private void HandleKey(Event e)
        {
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.C) { Copy(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.V) { Paste(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.D) { Duplicate(); e.Use(); }
            else if (e.keyCode == KeyCode.Space) { ToggleAttach(); e.Use(); }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { DeleteSelected(); e.Use(); }
        }

        private void BeginMarquee(Event e, bool additive)
        {
            _marquee = true;
            _marqueeAdditive = additive;
            _marqueeStart = _marqueeEnd = e.mousePosition;
        }

        private void DoMoveHandle()
        {
            Vector3 c = SelectionCentroidWorld();
            EditorGUI.BeginChangeCheck();
            Vector3 np = Handles.PositionHandle(c, _target.transform.rotation);
            if (EditorGUI.EndChangeCheck())
                MoveSelected(np - c);
        }

        private void PlaceOrConnect(Vector3 worldPoint, Vector3 worldNormal)
        {
            RecordUndo("Place Point");
            int existing = PickNode(Event.current.mousePosition);
            if (existing >= 0)
            {
                if (_step >= 0 && _step != existing)
                    AddEdge(_step, existing);
                _step = existing;
                _lastNode = existing;
                return;
            }

            _nodes.Add(new SNode
            {
                Local = _target.transform.InverseTransformPoint(worldPoint),
                LocalNormal = _target.transform.InverseTransformDirection(worldNormal),
            });
            int idx = _nodes.Count - 1;
            if (_step >= 0)
                AddEdge(_step, idx);
            _step = idx;
            _lastNode = idx;
        }

        private void ToggleAttach()
        {
            _step = _step >= 0 ? -1 : _lastNode;
        }

        private void AddEdge(int a, int b)
        {
            if (a == b || a < 0 || b < 0)
                return;
            foreach (SEdge x in _edges)
                if ((x.A == a && x.B == b) || (x.A == b && x.B == a))
                    return;
            _edges.Add(new SEdge { A = a, B = b });
        }

        // ------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------

        private int PickNode(Vector2 guiPoint)
        {
            int best = -1;
            float bestDist = PickPixelRadius;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(NodeWorld(i)), guiPoint);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private void ApplyMarquee()
        {
            RecordUndo("Select");
            Rect rect = MarqueeRect();
            if (rect.width < 3f && rect.height < 3f)
            {
                int n = PickNode(_marqueeEnd);
                if (!_marqueeAdditive)
                    _selection.Clear();
                if (n >= 0)
                    ToggleSelect(n);
                return;
            }

            if (!_marqueeAdditive)
                _selection.Clear();
            for (int i = 0; i < _nodes.Count; i++)
                if (rect.Contains(HandleUtility.WorldToGUIPoint(NodeWorld(i))) && !_selection.Contains(i))
                    _selection.Add(i);
        }

        private void ToggleSelect(int i)
        {
            if (!_selection.Remove(i))
                _selection.Add(i);
        }

        private Rect MarqueeRect()
        {
            float x = Mathf.Min(_marqueeStart.x, _marqueeEnd.x);
            float y = Mathf.Min(_marqueeStart.y, _marqueeEnd.y);
            return new Rect(x, y, Mathf.Abs(_marqueeEnd.x - _marqueeStart.x), Mathf.Abs(_marqueeEnd.y - _marqueeStart.y));
        }

        private List<int> Sel() => _selection.Where(InRange).Distinct().ToList();

        private Vector3 SelectionCentroidWorld()
        {
            Vector3 sum = Vector3.zero;
            int c = 0;
            foreach (int i in Sel()) { sum += NodeWorld(i); c++; }
            return c > 0 ? sum / c : _target.transform.position;
        }

        // ------------------------------------------------------------------
        // Binding / faces / building
        // ------------------------------------------------------------------

        private void BindSelected()
        {
            var sel = Sel();
            if (sel.Count < 2)
                return;
            RecordUndo("Bind");
            _faces.Add(new SFace { Nodes = sel });
            foreach (int i in sel)
                _nodes[i].Bound = true;
            BuildColliders();
        }

        private void UnbindSelected()
        {
            var set = new HashSet<int>(Sel());
            if (set.Count == 0)
                return;
            RecordUndo("Unbind");
            _faces.RemoveAll(f => f.Nodes.All(set.Contains));
            foreach (int i in set)
                if (InRange(i)) _nodes[i].Bound = false;
            BuildColliders();
        }

        private void BuildColliders()
        {
            if (_target == null)
                return;

            Transform existing = _target.transform.Find(GeneratedRootName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var root = new GameObject(GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Sketch Colliders");
            root.transform.SetParent(_target.transform, false);

            int built = 0;
            foreach (SFace f in _faces)
            {
                if (TryMakeFaceBox(f.Nodes, out Vector3 pos, out Quaternion rot, out Vector3 size))
                {
                    var go = new GameObject($"SketchBox_{built}");
                    go.transform.SetParent(root.transform, true);
                    go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one;
                    go.AddComponent<BoxCollider>().size = size;
                    built++;
                }
            }

            Debug.Log($"[ColliderPainter] Built {built} box colliders from {_faces.Count} faces.");
        }

        // A face -> a box built symmetrically around the cross-section along its normal.
        // 2 points: a beam of the brush width. 3+ points: the in-plane bounds extruded by thickness.
        private bool TryMakeFaceBox(List<int> face, out Vector3 pos, out Quaternion rot, out Vector3 size)
        {
            pos = default; rot = Quaternion.identity; size = Vector3.one;
            var idx = face.Where(InRange).Distinct().ToList();
            if (idx.Count < 2)
                return false;

            if (idx.Count == 2)
            {
                Vector3 a = NodeWorld(idx[0]);
                Vector3 b = NodeWorld(idx[1]);
                Vector3 dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-4f) return false;
                dir /= len;
                Vector3 nrm = (NodeWorldNormal(idx[0]) + NodeWorldNormal(idx[1])).normalized;
                if (nrm.sqrMagnitude < 1e-4f) nrm = Vector3.up;
                Vector3 wdir = Vector3.Cross(nrm, dir);
                if (wdir.sqrMagnitude < 1e-4f) wdir = Vector3.Cross(dir, Vector3.up);
                wdir.Normalize();
                Vector3 up = Vector3.Cross(dir, wdir).normalized;
                pos = (a + b) * 0.5f;
                rot = Quaternion.LookRotation(dir, up);
                size = new Vector3(_width, _thickness, len);
                return true;
            }

            var pts = idx.Select(NodeWorld).ToList();
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 p in pts) centroid += p;
            centroid /= pts.Count;

            Vector3 normal = NewellNormal(pts);
            if (normal.sqrMagnitude < 1e-6f)
            {
                normal = Vector3.zero;
                foreach (int i in idx) normal += NodeWorldNormal(i);
                normal = normal.sqrMagnitude < 1e-6f ? Vector3.up : normal.normalized;
            }

            Vector3 u = pts[1] - pts[0];
            u -= normal * Vector3.Dot(u, normal);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(normal, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(normal, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(normal, u).normalized;

            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (Vector3 p in pts)
            {
                float du = Vector3.Dot(p - centroid, u);
                float dv = Vector3.Dot(p - centroid, v);
                if (du < uMin) uMin = du; if (du > uMax) uMax = du;
                if (dv < vMin) vMin = dv; if (dv > vMax) vMax = dv;
            }

            Vector3 center = centroid + u * ((uMin + uMax) * 0.5f) + v * ((vMin + vMax) * 0.5f);
            var m = new Matrix4x4();
            m.SetColumn(0, u); m.SetColumn(1, v); m.SetColumn(2, normal); m.SetColumn(3, new Vector4(0, 0, 0, 1));
            pos = center;
            rot = m.rotation;
            size = new Vector3(Mathf.Max(uMax - uMin, 0.02f), Mathf.Max(vMax - vMin, 0.02f), _thickness);
            return true;
        }

        private static Vector3 NewellNormal(List<Vector3> pts)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 c = pts[i];
                Vector3 d = pts[(i + 1) % pts.Count];
                n.x += (c.y - d.y) * (c.z + d.z);
                n.y += (c.z - d.z) * (c.x + d.x);
                n.z += (c.x - d.x) * (c.y + d.y);
            }
            return n.normalized;
        }

        // ------------------------------------------------------------------
        // Duplicate / clone / mirror / clipboard
        // ------------------------------------------------------------------

        private void Duplicate()
        {
            var sel = Sel();
            if (sel.Count == 0)
                return;
            RecordUndo("Duplicate");

            Vector3 offset = _target.transform.InverseTransformDirection(Vector3.up) * _width;
            var map = new Dictionary<int, int>();
            foreach (int i in sel)
            {
                _nodes.Add(new SNode { Local = _nodes[i].Local + offset, LocalNormal = _nodes[i].LocalNormal, Bound = true });
                _nodes[i].Bound = true;
                map[i] = _nodes.Count - 1;
            }

            var selSet = new HashSet<int>(sel);
            var used = new HashSet<int>();
            foreach (SEdge e in _edges.ToArray())
            {
                if (!selSet.Contains(e.A) || !selSet.Contains(e.B))
                    continue;
                AddEdge(e.A, map[e.A]);
                AddEdge(e.B, map[e.B]);
                AddEdge(map[e.A], map[e.B]);
                _faces.Add(new SFace { Nodes = new List<int> { e.A, map[e.A], map[e.B], e.B } });
                used.Add(e.A); used.Add(e.B);
            }

            foreach (int i in sel)
            {
                if (used.Contains(i))
                    continue;
                AddEdge(i, map[i]);
                _faces.Add(new SFace { Nodes = new List<int> { i, map[i] } });
            }

            _selection = map.Values.ToList();
            BuildColliders();
        }

        private List<int> CloneSelected(System.Func<Vector3, Vector3> posMap, System.Func<Vector3, Vector3> nrmMap)
        {
            var sel = Sel();
            var map = new Dictionary<int, int>();
            foreach (int i in sel)
            {
                _nodes.Add(new SNode { Local = posMap(_nodes[i].Local), LocalNormal = nrmMap(_nodes[i].LocalNormal), Bound = _nodes[i].Bound });
                map[i] = _nodes.Count - 1;
            }
            var selSet = new HashSet<int>(sel);
            foreach (SEdge e in _edges.ToArray())
                if (selSet.Contains(e.A) && selSet.Contains(e.B))
                    AddEdge(map[e.A], map[e.B]);
            foreach (SFace f in _faces.ToArray())
                if (f.Nodes.All(selSet.Contains))
                    _faces.Add(new SFace { Nodes = f.Nodes.Select(x => map[x]).ToList() });
            return map.Values.ToList();
        }

        private void Mirror(Vector3 localAxis)
        {
            var sel = Sel();
            if (sel.Count == 0)
                return;
            RecordUndo("Mirror");

            Vector3 c = Vector3.zero;
            foreach (int i in sel) c += _nodes[i].Local;
            c /= sel.Count;
            Vector3 axis = localAxis.normalized;

            Vector3 PosMap(Vector3 p) => p - 2f * Vector3.Dot(p - c, axis) * axis;
            Vector3 NrmMap(Vector3 n) => n - 2f * Vector3.Dot(n, axis) * axis;

            _selection = CloneSelected(PosMap, NrmMap);
            BuildColliders();
        }

        private void Copy()
        {
            var sel = Sel();
            if (sel.Count == 0)
                return;
            var local = new Dictionary<int, int>();
            _clip = new ClipData();
            foreach (int i in sel)
            {
                local[i] = _clip.Pos.Count;
                _clip.Pos.Add(_nodes[i].Local);
                _clip.Nrm.Add(_nodes[i].LocalNormal);
            }
            foreach (SEdge e in _edges)
                if (local.ContainsKey(e.A) && local.ContainsKey(e.B))
                    _clip.Edges.Add(new Vector2Int(local[e.A], local[e.B]));
            foreach (SFace f in _faces)
                if (f.Nodes.All(local.ContainsKey))
                    _clip.Faces.Add(f.Nodes.Select(x => local[x]).ToArray());
        }

        private void Paste()
        {
            if (_clip == null || _clip.Pos.Count == 0)
                return;
            RecordUndo("Paste");
            Vector3 offset = _target.transform.InverseTransformDirection(Vector3.up) * _width;
            int baseIdx = _nodes.Count;
            for (int i = 0; i < _clip.Pos.Count; i++)
                _nodes.Add(new SNode { Local = _clip.Pos[i] + offset, LocalNormal = _clip.Nrm[i] });
            foreach (Vector2Int e in _clip.Edges)
                AddEdge(baseIdx + e.x, baseIdx + e.y);
            foreach (int[] f in _clip.Faces)
                _faces.Add(new SFace { Nodes = f.Select(x => baseIdx + x).ToList() });
            _selection = Enumerable.Range(baseIdx, _clip.Pos.Count).ToList();
        }

        private void MoveSelected(Vector3 worldDelta)
        {
            RecordUndo("Move Points");
            foreach (int i in Sel())
            {
                Vector3 w = NodeWorld(i) + worldDelta;
                _nodes[i].Local = _target.transform.InverseTransformPoint(w);
            }
            BuildColliders();
        }

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        private void DeleteSelected()
        {
            var remove = new HashSet<int>(Sel());
            if (remove.Count == 0)
                return;
            RecordUndo("Delete Points");

            _edges.RemoveAll(e => remove.Contains(e.A) || remove.Contains(e.B));
            _faces.RemoveAll(f => f.Nodes.Any(remove.Contains));

            var remap = new int[_nodes.Count];
            var kept = new List<SNode>();
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (remove.Contains(i)) { remap[i] = -1; continue; }
                remap[i] = kept.Count;
                kept.Add(_nodes[i]);
            }
            _nodes = kept;
            foreach (SEdge e in _edges) { e.A = remap[e.A]; e.B = remap[e.B]; }
            foreach (SFace f in _faces)
                for (int k = 0; k < f.Nodes.Count; k++) f.Nodes[k] = remap[f.Nodes[k]];

            _selection.Clear();
            _step = -1;
            _lastNode = _nodes.Count - 1;
            BuildColliders();
        }

        private void ClearSketch()
        {
            RecordUndo("Clear Sketch");
            _nodes.Clear(); _edges.Clear(); _faces.Clear(); _selection.Clear();
            _step = -1; _lastNode = -1;
        }

        private void RecordUndo(string label) => Undo.RegisterCompleteObjectUndo(this, label);

        // ------------------------------------------------------------------
        // Scene / setup helpers
        // ------------------------------------------------------------------

        private static int AddMeshColliders(GameObject root)
        {
            if (root == null)
                return 0;
            int added = 0;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null)
                    continue;
                Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh = mf.sharedMesh;
                added++;
            }
            Debug.Log($"[ColliderPainter] Added {added} MeshColliders for painting.");
            return added;
        }

        private void CreatePaintingScene()
        {
            const string prefabPath = "Assets/Stylized_Pirate_Ship/StylShip_Unity.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Collider Painter",
                    $"Ship prefab not found at '{prefabPath}'. Assign a ship and use 'Add Mesh Colliders To Ship' instead.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            ship.transform.position = Vector3.zero;
            AddMeshColliders(ship);

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ColliderPainting.unity");

            _target = ship;
            _active = true;
            Selection.activeGameObject = ship;
            SceneView.RepaintAll();
        }

        // Raycast directly against the ship's own colliders (reliable in edit mode).
        private bool TryPickSurface(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = Vector3.up;
            if (_target == null)
                return false;

            Transform generated = _target.transform.Find(GeneratedRootName);
            float best = float.MaxValue;
            bool found = false;
            foreach (Collider c in _target.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                    continue;
                if (generated != null && c.transform.IsChildOf(generated))
                    continue;
                if (c.Raycast(ray, out RaycastHit h, 100000f) && h.distance < best)
                {
                    best = h.distance;
                    point = h.point;
                    normal = h.normal;
                    found = true;
                }
            }
            return found;
        }

        private void SetNativeColliders(bool enabled)
        {
            if (_target == null)
                return;
            Transform generated = _target.transform.Find(GeneratedRootName);
            int changed = 0;
            foreach (Collider c in _target.GetComponentsInChildren<Collider>(true))
            {
                if (c == null)
                    continue;
                // Keep our own sketch colliders active.
                if (generated != null && c.transform.IsChildOf(generated))
                    continue;
                Undo.RecordObject(c, "Toggle Native Colliders");
                c.enabled = enabled;
                changed++;
            }
            Debug.Log($"[ColliderPainter] {(enabled ? "Enabled" : "Disabled")} {changed} native colliders (sketch kept).");
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void DrawSketch()
        {
            Handles.color = Color.cyan;
            foreach (SEdge e in _edges)
                if (InRange(e.A) && InRange(e.B))
                    Handles.DrawLine(NodeWorld(e.A), NodeWorld(e.B), 2f);

            // Faces + preview boxes.
            foreach (SFace f in _faces)
            {
                var idx = f.Nodes.Where(InRange).ToList();
                Handles.color = new Color(1f, 0.4f, 0.9f, 0.9f);
                for (int i = 0; i < idx.Count; i++)
                    Handles.DrawLine(NodeWorld(idx[i]), NodeWorld(idx[(i + 1) % idx.Count]));

                if (_previewColliders && TryMakeFaceBox(f.Nodes, out Vector3 pos, out Quaternion rot, out Vector3 size))
                {
                    Handles.color = new Color(0.2f, 1f, 0.4f, 0.9f);
                    using (new Handles.DrawingScope(Matrix4x4.TRS(pos, rot, Vector3.one)))
                        Handles.DrawWireCube(Vector3.zero, size);
                }
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                Vector3 p = NodeWorld(i);
                float s = HandleUtility.GetHandleSize(p) * 0.06f;
                bool sel = _selection.Contains(i);
                Handles.color = i == _step ? Color.yellow : sel ? Color.red : _nodes[i].Bound ? Color.green : Color.white;
                Handles.SphereHandleCap(0, p, Quaternion.identity, s, EventType.Repaint);
            }
        }

        private void DrawOverlay(bool hasHit)
        {
            Handles.BeginGUI();
            var rect = new Rect(8f, 8f, 340f, 62f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            var st = new GUIStyle(EditorStyles.whiteLabel) { fontSize = 11 };
            GUI.Label(new Rect(14f, 12f, 340f, 18f), $"Collider Painter — {_brush}  (pts: {_nodes.Count}, faces: {_faces.Count}, sel: {_selection.Count})", st);
            GUI.Label(new Rect(14f, 30f, 340f, 18f), hasHit ? "Cursor over ship" : "Cursor NOT over a collider", st);
            GUI.Label(new Rect(14f, 46f, 340f, 18f), _brush == Brush.Draw ? (_step >= 0 ? "Step attached (Space to detach)" : "Step detached (Space to attach)") : "Select: drag=marquee, Shift=add", st);
            Handles.EndGUI();
        }

        private void DrawCursor(Vector3 point, Vector3 normal)
        {
            Handles.color = Color.yellow;
            float s = HandleUtility.GetHandleSize(point);
            Handles.DrawWireDisc(point, normal, s * 0.08f);
            if (!_showGrid)
                return;

            Handles.color = new Color(1f, 1f, 1f, 0.25f);
            Vector3 t = Vector3.Cross(normal, Vector3.up);
            if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(normal, Vector3.right);
            t.Normalize();
            Vector3 b = Vector3.Cross(normal, t);
            float step = Mathf.Max(_width, 0.25f);
            for (int i = -3; i <= 3; i++)
            {
                Handles.DrawLine(point + t * (i * step) - b * (3 * step), point + t * (i * step) + b * (3 * step));
                Handles.DrawLine(point + b * (i * step) - t * (3 * step), point + b * (i * step) + t * (3 * step));
            }
        }

        private void DrawMarquee()
        {
            Handles.BeginGUI();
            EditorGUI.DrawRect(MarqueeRect(), new Color(0.3f, 0.6f, 1f, 0.15f));
            Handles.EndGUI();
        }

        // ------------------------------------------------------------------
        // Lists
        // ------------------------------------------------------------------

        private void DrawSelectionList()
        {
            EditorGUILayout.Space();
            _showSelection = EditorGUILayout.Foldout(_showSelection, $"Selection ({_selection.Count})", true);
            if (!_showSelection)
                return;
            if (_selection.Count == 0)
            {
                EditorGUILayout.LabelField("  (none)", EditorStyles.miniLabel);
                return;
            }
            for (int s = 0; s < _selection.Count; s++)
            {
                int i = _selection[s];
                if (!InRange(i)) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"#{i}", GUILayout.Width(40f));
                    EditorGUILayout.LabelField(_nodes[i].Bound ? "bound" : "-", GUILayout.Width(48f));
                    EditorGUILayout.LabelField(FormatVec(_nodes[i].Local), EditorStyles.miniLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(44f))) FrameNode(i);
                    if (GUILayout.Button("x", GUILayout.Width(22f))) { _selection.RemoveAt(s); s--; }
                }
            }
        }

        private void DrawPointsList()
        {
            EditorGUILayout.Space();
            _showPoints = EditorGUILayout.Foldout(_showPoints, $"All Points ({_nodes.Count})", true);
            if (!_showPoints)
                return;
            _pointsScroll = EditorGUILayout.BeginScrollView(_pointsScroll, GUILayout.MaxHeight(200f));
            for (int i = 0; i < _nodes.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool sel = _selection.Contains(i);
                    bool newSel = GUILayout.Toggle(sel, GUIContent.none, GUILayout.Width(18f));
                    if (newSel != sel) ToggleSelect(i);
                    EditorGUILayout.LabelField($"#{i}", i == _step ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(40f));
                    EditorGUILayout.LabelField(FormatVec(_nodes[i].Local), EditorStyles.miniLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(44f))) FrameNode(i);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static string FormatVec(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        private void FrameNode(int i)
        {
            if (InRange(i) && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(NodeWorld(i), SceneView.lastActiveSceneView.rotation, 3f);
        }

        private bool InRange(int i) => i >= 0 && i < _nodes.Count;
        private Vector3 NodeWorld(int i) => _target.transform.TransformPoint(_nodes[i].Local);
        private Vector3 NodeWorldNormal(int i) => _target.transform.TransformDirection(_nodes[i].LocalNormal);
    }
}
