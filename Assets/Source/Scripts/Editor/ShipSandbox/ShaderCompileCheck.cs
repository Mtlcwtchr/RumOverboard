using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// Compiles the ocean + water-interaction shaders for the current platform and logs any errors.
    /// Batch: -executeMethod RumOverboard.EditorTools.ShipSandbox.ShaderCompileCheck.RunBatch
    /// </summary>
    public static class ShaderCompileCheck
    {
        private static readonly string[] Shaders =
        {
            "Source/Gameplay/Ocean/StylizedURP",
            "Hidden/RumOverboard/WaterInteractionSim",
        };

        [MenuItem("RumOverboard/Ship Sandbox/Diagnostics/Compile Ocean Shaders")]
        public static bool Run()
        {
            bool ok = true;
            ShaderCompilerPlatform platform = Application.platform == RuntimePlatform.OSXEditor
                ? ShaderCompilerPlatform.Metal
                : ShaderCompilerPlatform.D3D;
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

            foreach (string name in Shaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[ShaderCheck] '{name}' not found");
                    ok = false;
                    continue;
                }

                foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                {
                    Debug.Log($"[ShaderCheck] {name}: {m.severity} {m.message} ({m.file}:{m.line})");
                    if (m.severity == ShaderCompilerMessageSeverity.Error) ok = false;
                }

                ShaderData data = ShaderUtil.GetShaderData(shader);
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    ShaderData.Subshader sub = data.GetSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        ShaderData.Pass pass = sub.GetPass(p);
                        foreach (ShaderType type in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo info = pass.CompileVariant(type, Array.Empty<string>(), platform, target);
                            foreach (ShaderMessage m in info.Messages)
                                Debug.Log($"[ShaderCheck] {name} pass {p} {type}: {m.severity} {m.message} (line {m.line})");
                            if (!info.Success) ok = false;
                            Debug.Log($"[ShaderCheck] {name} pass {p} {type}: {(info.Success ? "OK" : "FAILED")}");
                        }
                    }
                }
            }
            Debug.Log($"[ShaderCheck] RESULT {(ok ? "OK" : "ERRORS")}");
            return ok;
        }

        public static void RunBatch()
        {
            bool ok;
            try { ok = Run(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}

