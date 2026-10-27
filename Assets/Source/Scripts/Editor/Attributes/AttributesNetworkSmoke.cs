using System;
using System.IO;
using System.Reflection;
using RumOverboard.Gameplay.Attributes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RumOverboard.EditorTools.Attributes
{
    /// <summary>
    /// Host + client replication test of AttributesScene through Multiplayer Play Mode:
    ///   1. writes the run request (temp folder) read by <see cref="AttributesNetSmoke"/> on both peers,
    ///   2. (re)launches MPPM virtual player "Player 2" (a clone editor) and waits until it launched —
    ///      always fresh: a clone that reloaded scripts can fail to load Fusion's config,
    ///   3. enters Play — the main editor hosts, the clone joins as client (AutoHostOrClient, Photon cloud),
    ///   4. waits for the client report, prints both reports as [AttributesNetSmoke] PASS/FAIL, exits Play.
    /// Menu: RumOverboard ▸ Attributes ▸ Run Network Smoke (MPPM host + client).
    /// MPPM activation goes through internal editor API (reflection); if it fails, activate Player 2 in
    /// Window ▸ Multiplayer ▸ Multiplayer Play Mode and run again. Player 2 stays active afterwards.
    /// </summary>
    public static class AttributesNetworkSmoke
    {
        private const string StageKey = "RumOverboard.AttributesNetSmoke.Stage"; // 0 idle, 1 launching clone, 2 playing
        private const string RunKey = "RumOverboard.AttributesNetSmoke.Run";
        private const float LaunchTimeout = 600f;  // first clone launch imports its Library
        private const float PlayTimeout = 300f;
        private const float ReportGrace = 40f;

        private static double _stageStart = -1;
        private static double _firstReportAt = -1;

        [MenuItem("RumOverboard/Attributes/Run Network Smoke (MPPM host + client)", priority = 22)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(AttributesSandboxSetup.ScenePath);

            string runId = DateTime.Now.ToString("HHmmss");
            Directory.CreateDirectory(AttributesNetSmoke.Folder);
            File.WriteAllText(AttributesNetSmoke.RequestPath, runId);
            foreach (string peer in new[] { "main", "clone" })
                File.Delete(AttributesNetSmoke.ReportPath(peer));

            SessionState.SetString(RunKey, runId);
            _stageStart = EditorApplication.timeSinceStartup;

            // A clone that picked up script changes runs on a read-only AssetDatabase and can fail to
            // load Fusion's NetworkProjectConfig (ScriptedImporter asset gone "out of date") — so the
            // smoke always starts from a freshly launched clone.
            string state = PlayerTwoState();
            if (state == "Launched" || state == "Launching")
            {
                if (!TryInvokePlayerTwo("Deactivate", out string deactivateError))
                {
                    Debug.LogError($"[AttributesNetSmoke] Could not restart MPPM Player 2: {deactivateError}.");
                    return;
                }
                SessionState.SetInt(StageKey, 3);
                Debug.Log($"[AttributesNetSmoke] run {runId}: restarting MPPM Player 2…");
                return;
            }

            StartClone(runId);
        }

        private static void StartClone(string runId)
        {
            if (!TryInvokePlayerTwo("Activate", out string error))
            {
                SessionState.SetInt(StageKey, 0);
                Debug.LogError($"[AttributesNetSmoke] Could not activate MPPM Player 2: {error}. " +
                               "Activate it in Window ▸ Multiplayer ▸ Multiplayer Play Mode and run again.");
                return;
            }
            SessionState.SetInt(StageKey, 1);
            _stageStart = EditorApplication.timeSinceStartup;
            Debug.Log($"[AttributesNetSmoke] run {runId}: waiting for MPPM Player 2 to launch…");
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0) return;
            if (_stageStart < 0) _stageStart = EditorApplication.timeSinceStartup; // after a domain reload
            double elapsed = EditorApplication.timeSinceStartup - _stageStart;

            if (stage == 3) // waiting for the old clone to stop
            {
                string state = PlayerTwoState();
                if (state == "NotLaunched" || state == "UnexpectedlyStopped")
                    StartClone(SessionState.GetString(RunKey, ""));
                else if (elapsed > 60.0)
                    Abort($"Player 2 did not stop (state {state})");
                return;
            }

            if (stage == 1)
            {
                string state = PlayerTwoState();
                if (state == "Launched" && elapsed > 3.0)
                {
                    SessionState.SetInt(StageKey, 2);
                    _stageStart = EditorApplication.timeSinceStartup;
                    _firstReportAt = -1;
                    EditorApplication.isPlaying = true;
                }
                else if (state == "UnexpectedlyStopped" || elapsed > LaunchTimeout)
                {
                    Abort($"Player 2 did not launch (state {state}, {elapsed:0}s)");
                }
                return;
            }

            // stage 2: playing
            if (!EditorApplication.isPlaying)
            {
                if (elapsed > 30.0) Abort("Play mode ended before the client reported");
                return;
            }

            string runId = SessionState.GetString(RunKey, "");
            string main = ReadReport("main", runId);
            string clone = ReadReport("clone", runId);

            if (main != null && clone != null)
            {
                Finish(runId, main, clone);
                return;
            }

            // One peer reported; give the other a grace period (e.g. host still waiting for a client that failed).
            if ((main != null || clone != null) && _firstReportAt < 0)
                _firstReportAt = EditorApplication.timeSinceStartup;
            bool graceOver = _firstReportAt > 0 && EditorApplication.timeSinceStartup - _firstReportAt > ReportGrace;

            if (graceOver || elapsed > PlayTimeout)
                Finish(runId, main, clone);
        }

        private static void Finish(string runId, string main, string clone)
        {
            SessionState.SetInt(StageKey, 0);
            DeleteRequest();
            _stageStart = -1;
            _firstReportAt = -1;
            EditorApplication.isPlaying = false;

            bool bothPass = main != null && clone != null && !main.Contains("FAILED") && !clone.Contains("FAILED");
            bool rolesOk = main != null && clone != null &&
                           ((main.Contains(":host@") && clone.Contains(":client@")) ||
                            (main.Contains(":client@") && clone.Contains(":host@")));
            bool pass = bothPass && rolesOk;
            string text = $"[AttributesNetSmoke] run {runId}: {(pass ? "PASS" : "FAIL")}" +
                          (bothPass && !rolesOk ? " (peers did not end up as one host + one client)" : "") + "\n" +
                          $"--- main editor ---\n{main ?? "(no report)"}\n--- MPPM clone ---\n{clone ?? "(no report)"}";
            if (pass) Debug.Log(text);
            else Debug.LogError(text + "\nA clone that just reloaded scripts sometimes fails to load Fusion's config — rerun once before digging.");
        }

        private static void Abort(string reason)
        {
            SessionState.SetInt(StageKey, 0);
            DeleteRequest();
            _stageStart = -1;
            Debug.LogError($"[AttributesNetSmoke] FAIL: {reason}");
        }

        /// <summary>So a normal Play right after the test doesn't run the scenario again.</summary>
        private static void DeleteRequest()
        {
            try { File.Delete(AttributesNetSmoke.RequestPath); }
            catch (IOException) { }
        }

        private static string ReadReport(string role, string runId)
        {
            string path = AttributesNetSmoke.ReportPath(role);
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            return text.Contains($"run {runId}:") ? text : null;
        }

        // ───────────────────────── MPPM (internal API via reflection) ─────────────────────────

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static object PlayerTwo()
        {
            // Any public type of UnityEditor.MultiplayerModule gives us the assembly holding MPPM internals.
            Assembly asm = typeof(Unity.Multiplayer.Editor.EditorMultiplayerRolesManager).Assembly;
            Type mppm = asm?.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode");
            return mppm?.GetProperty("PlayerTwo", Any)?.GetValue(null);
        }

        private static string PlayerTwoState()
        {
            object player = PlayerTwo();
            return player?.GetType().GetProperty("PlayerState", Any)?.GetValue(player)?.ToString() ?? "Unavailable";
        }

        /// <summary>Calls UnityPlayer.Activate(out ActivationError, List&lt;string&gt;) or Deactivate(out ActivationError).</summary>
        private static bool TryInvokePlayerTwo(string methodName, out string error)
        {
            error = null;
            try
            {
                object player = PlayerTwo();
                if (player == null)
                {
                    error = "MPPM API not found (editor version changed?)";
                    return false;
                }

                MethodInfo method = player.GetType().GetMethod(methodName, Any);
                if (method == null)
                {
                    error = $"UnityPlayer.{methodName} not found";
                    return false;
                }
                var args = new object[method.GetParameters().Length];
                bool ok = (bool)method.Invoke(player, args);
                if (!ok) error = $"{methodName} returned {args[0]}";
                return ok;
            }
            catch (Exception e)
            {
                error = e.GetBaseException().Message;
                return false;
            }
        }
    }
}
