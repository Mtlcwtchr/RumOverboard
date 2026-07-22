#if UNITY_EDITOR
using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace Source.Scripts.Editor
{
    [CustomEditor(typeof(ShipSailSystem))]
    public class ShipSailSystemEditor : UnityEditor.Editor
    {
        private static readonly string[] CornerPropNames =
        {
            "topLeftLocal",
            "topRightLocal",
            "bottomLeftLocal",
            "bottomRightLocal",
        };

        private static readonly string[] CornerLabels = { "TL", "TR", "BL", "BR" };

        private static readonly Color[] CornerColors =
        {
            new Color(0.2f, 0.95f, 1f, 0.95f),
            new Color(0.2f, 0.95f, 1f, 0.95f),
            new Color(1f, 0.82f, 0.28f, 0.95f),
            new Color(1f, 0.82f, 0.28f, 0.95f),
        };

        private SerializedProperty _sails;
        private SerializedProperty _editorShowAttachmentHandles;
        private SerializedProperty _editorShowPointLabels;
        private SerializedProperty _editorPointHandleSize;
        private SerializedProperty _editorDrawMeshPreviewGizmo;

        private void OnEnable()
        {
            _sails = serializedObject.FindProperty("sails");
            _editorShowAttachmentHandles = serializedObject.FindProperty("editorShowAttachmentHandles");
            _editorShowPointLabels = serializedObject.FindProperty("editorShowPointLabels");
            _editorPointHandleSize = serializedObject.FindProperty("editorPointHandleSize");
            _editorDrawMeshPreviewGizmo = serializedObject.FindProperty("editorDrawMeshPreviewGizmo");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Точки TL/TR/BL/BR можно тягать прямо в Scene View. Это точки жесткой привязки канатов (граница паруса).",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sync точки из текущего NetworkShip Visual", GUILayout.Height(24f)))
                {
                    var sailSystem = (ShipSailSystem)target;
                    Undo.RecordObject(sailSystem, "Sync Sail Points From Current Ship Visual");
                    sailSystem.SyncAnchorPointsFromCurrentShipVisual();
                    EditorUtility.SetDirty(sailSystem);
                    SceneView.RepaintAll();
                    serializedObject.Update();
                }

                if (GUILayout.Button("Пересчитать после правок", GUILayout.Height(24f)))
                {
                    var sailSystem = (ShipSailSystem)target;
                    Undo.RecordObject(sailSystem, "Recompute Sail Rig");
                    sailSystem.NotifyRigPointsEditedInEditor();
                    EditorUtility.SetDirty(sailSystem);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (_sails == null || _editorShowAttachmentHandles == null || !_editorShowAttachmentHandles.boolValue)
                return;

            var sailSystem = (ShipSailSystem)target;
            Transform tr = sailSystem.transform;

            serializedObject.Update();
            bool changed = false;

            for (int i = 0; i < _sails.arraySize; i++)
            {
                SerializedProperty sail = _sails.GetArrayElementAtIndex(i);
                if (sail == null)
                    continue;

                SerializedProperty nameProp = sail.FindPropertyRelative("name");
                SerializedProperty enabledProp = sail.FindPropertyRelative("enabled");
                SerializedProperty hoistProp = sail.FindPropertyRelative("hoist01");
                SerializedProperty extensionProp = sail.FindPropertyRelative("extension01");
                SerializedProperty sheetProp = sail.FindPropertyRelative("sheetAngleDeg");
                SerializedProperty gridWProp = sail.FindPropertyRelative("gridWidth");
                SerializedProperty gridHProp = sail.FindPropertyRelative("gridHeight");

                Vector3[] cornersLocal = new Vector3[4];
                SerializedProperty[] cornerProps = new SerializedProperty[4];
                for (int c = 0; c < 4; c++)
                {
                    cornerProps[c] = sail.FindPropertyRelative(CornerPropNames[c]);
                    cornersLocal[c] = cornerProps[c].vector3Value;
                }

                Color frameColor = enabledProp != null && enabledProp.boolValue
                    ? new Color(0.25f, 0.6f, 1f, 0.75f)
                    : new Color(0.3f, 0.3f, 0.3f, 0.45f);

                Vector3 tl = tr.TransformPoint(cornersLocal[0]);
                Vector3 trw = tr.TransformPoint(cornersLocal[1]);
                Vector3 bl = tr.TransformPoint(cornersLocal[2]);
                Vector3 br = tr.TransformPoint(cornersLocal[3]);

                Handles.color = frameColor;
                Handles.DrawLine(tl, trw);
                Handles.DrawLine(trw, br);
                Handles.DrawLine(br, bl);
                Handles.DrawLine(bl, tl);

                for (int c = 0; c < 4; c++)
                {
                    Vector3 world = tr.TransformPoint(cornerProps[c].vector3Value);
                    float baseSize = _editorPointHandleSize != null ? _editorPointHandleSize.floatValue : 0.08f;
                    float handleSize = HandleUtility.GetHandleSize(world) * Mathf.Max(0.01f, baseSize);

                    Handles.color = CornerColors[c];
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(world, handleSize, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(sailSystem, "Move Sail Anchor Point");
                        cornerProps[c].vector3Value = tr.InverseTransformPoint(moved);
                        cornersLocal[c] = cornerProps[c].vector3Value;
                        changed = true;
                    }

                    if (_editorShowPointLabels != null && _editorShowPointLabels.boolValue)
                    {
                        string sailName = string.IsNullOrWhiteSpace(nameProp.stringValue) ? $"Sail {i + 1}" : nameProp.stringValue;
                        Handles.Label(world + Vector3.up * (handleSize * 1.4f), $"{sailName} {CornerLabels[c]}");
                    }
                }

                if (_editorDrawMeshPreviewGizmo != null && _editorDrawMeshPreviewGizmo.boolValue)
                {
                    int w = Mathf.Max(2, gridWProp != null ? gridWProp.intValue : 2);
                    int h = Mathf.Max(2, gridHProp != null ? gridHProp.intValue : 2);
                    float hoist = hoistProp != null ? hoistProp.floatValue : 1f;
                    float extension = extensionProp != null ? extensionProp.floatValue : 1f;
                    float sheetDeg = sheetProp != null ? sheetProp.floatValue : 0f;
                    float deployment = hoist * Mathf.Lerp(0.15f, 1f, extension);

                    Handles.color = new Color(0.92f, 0.96f, 0.34f, 0.45f);
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            Vector3 p = ComputePreviewWorldPoint(tr, cornersLocal, x, y, w, h, deployment, extension, sheetDeg);
                            if (x < w - 1)
                            {
                                Vector3 right = ComputePreviewWorldPoint(tr, cornersLocal, x + 1, y, w, h, deployment, extension, sheetDeg);
                                Handles.DrawLine(p, right);
                            }

                            if (y < h - 1)
                            {
                                Vector3 down = ComputePreviewWorldPoint(tr, cornersLocal, x, y + 1, w, h, deployment, extension, sheetDeg);
                                Handles.DrawLine(p, down);
                            }
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (changed)
            {
                sailSystem.NotifyRigPointsEditedInEditor();
                EditorUtility.SetDirty(sailSystem);
                SceneView.RepaintAll();
            }
        }

        private static Vector3 ComputePreviewWorldPoint(
            Transform shipTransform,
            Vector3[] cornersLocal,
            int x,
            int y,
            int gridWidth,
            int gridHeight,
            float deployment,
            float extension,
            float sheetAngleDeg)
        {
            float u = gridWidth > 1 ? x / (float)(gridWidth - 1) : 0f;
            float v = gridHeight > 1 ? y / (float)(gridHeight - 1) : 0f;

            Vector3 topLeft = cornersLocal[0];
            Vector3 topRight = cornersLocal[1];
            Vector3 bottomLeft = cornersLocal[2];
            Vector3 bottomRight = cornersLocal[3];

            Vector3 bl = Vector3.Lerp(topLeft, bottomLeft, deployment);
            Vector3 br = Vector3.Lerp(topRight, bottomRight, deployment);

            Vector3 bottomMid = (bl + br) * 0.5f;
            float spread = Mathf.Lerp(0.3f, 1f, extension);
            bl = bottomMid + (bl - bottomMid) * spread;
            br = bottomMid + (br - bottomMid) * spread;

            Vector3 top = Vector3.Lerp(topLeft, topRight, u);
            Vector3 bottom = Vector3.Lerp(bl, br, u);
            Vector3 localPos = Vector3.Lerp(top, bottom, v);

            if (Mathf.Abs(sheetAngleDeg) > 0.01f)
            {
                Vector3 pivot = (topLeft + topRight) * 0.5f;
                Quaternion rot = Quaternion.AngleAxis(sheetAngleDeg, Vector3.up);
                localPos = pivot + rot * (localPos - pivot);
            }

            return shipTransform.TransformPoint(localPos);
        }

    }
}
#endif

