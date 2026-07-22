using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    /// <summary>
    /// Ensures the player Animator controller has the parameters NetworkPlayer drives. Currently
    /// adds the <c>Steering</c> bool (set while at the helm) if it's missing — so the "hands on the
    /// wheel" pose can be wired without a manual param add. Runs once automatically + on demand.
    /// </summary>
    public static class PlayerAnimatorSetup
    {
        private const string ControllerPath = "Assets/Source/Animation/NetworkPlayer.controller";
        private const string SteeringParam = "Steering";
        private const string Menu = "RumOverboard/Setup/Add Steering Animator Param";

        [InitializeOnLoadMethod]
        private static void AutoRunOnce()
        {
            string key = "RumOverboard.PlayerAnimatorSetup.Steering.v1:" + Application.dataPath;
            if (EditorPrefs.GetBool(key, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(key, false))
                    return;
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null)
                    return; // not imported yet — retry next reload
                if (EnsureSteeringParam(out string summary))
                {
                    EditorPrefs.SetBool(key, true);
                    Debug.Log($"[PlayerAnimatorSetup] {summary}");
                }
            };
        }

        [MenuItem(Menu, true)]
        private static bool Validate() => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null;

        [MenuItem(Menu)]
        private static void Run()
        {
            EnsureSteeringParam(out string summary);
            EditorUtility.DisplayDialog("Player Animator", summary, "OK");
        }

        public static bool EnsureSteeringParam(out string summary)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                summary = $"Animator controller not found at '{ControllerPath}'.";
                return false;
            }

            foreach (var p in controller.parameters)
            {
                if (p.name == SteeringParam)
                {
                    summary = $"'{SteeringParam}' parameter already present — nothing to do.";
                    return true;
                }
            }

            controller.AddParameter(SteeringParam, AnimatorControllerParameterType.Bool);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            summary = $"Added '{SteeringParam}' (Bool) to the player Animator controller. " +
                      "Wire a 'hands on the wheel' pose/state gated on it when ready.";
            return true;
        }
    }
}
