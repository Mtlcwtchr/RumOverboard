#if UNITY_EDITOR
using RumOverboard.Gameplay.Ocean;
using UnityEditor;
using UnityEngine;

namespace Source.Scripts.Editor
{
    [CustomEditor(typeof(ShipBuoyancyController))]
    public class ShipBuoyancyControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty _points;
        private SerializedProperty _debugDrawForces;

        private void OnEnable()
        {
            _points = serializedObject.FindProperty("points");
            _debugDrawForces = serializedObject.FindProperty("debugDrawForces");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Buoyancy points can be moved directly in Scene view. Enable force debug to inspect per-point forces.", MessageType.Info);
            if (_debugDrawForces != null)
                EditorGUILayout.PropertyField(_debugDrawForces);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (_points == null)
                return;

            var buoyancy = (ShipBuoyancyController)target;
            Transform tr = buoyancy.transform;

            serializedObject.Update();
            for (int i = 0; i < _points.arraySize; i++)
            {
                SerializedProperty point = _points.GetArrayElementAtIndex(i);
                SerializedProperty localPos = point.FindPropertyRelative("localPosition");
                SerializedProperty radius = point.FindPropertyRelative("radius");
                SerializedProperty nameProp = point.FindPropertyRelative("name");

                Vector3 world = tr.TransformPoint(localPos.vector3Value);
                float handleSize = HandleUtility.GetHandleSize(world) * 0.08f;

                Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
                var fmh_53_63_639202599389656560 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(world, handleSize, Vector3.zero, Handles.SphereHandleCap);
                Handles.DrawWireDisc(world, Vector3.up, Mathf.Max(0.02f, radius.floatValue));

                string label = string.IsNullOrEmpty(nameProp.stringValue) ? $"P{i}" : nameProp.stringValue;
                Handles.Label(world + Vector3.up * 0.25f, label);

                if (moved != world)
                {
                    Undo.RecordObject(buoyancy, "Move Buoyancy Point");
                    localPos.vector3Value = tr.InverseTransformPoint(moved);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif

