#if FUSION2
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using RumOverboard.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.Networking
{
    /// <summary>
    /// Owns the NetworkRunner and the session lifecycle. Session model matches PEAK:
    /// one player is the host (= authoritative server) and the rest are pure clients.
    ///
    ///   • StartHost()  — become the server + play.
    ///   • StartClient()— join an existing host by session name.
    ///   • StartAuto()  — first peer into the session becomes host, others clients
    ///                    (drop-in "just press play, friend joins" flow).
    ///   • StartShared()— P2P-ish Shared mode: no single server, each client owns
    ///                    its own objects. Cheating is "a friends problem", per design.
    ///
    /// This is the single INetworkRunnerCallbacks implementer; it also drives spawning
    /// (host-authoritative) and feeds per-tick input via InputReader.
    /// </summary>
    public class ConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private GameConfig config;

        [Tooltip("Player prefab: NetworkObject + NetworkPlayer + Rigidbody + a sync component " +
                 "(NetworkTransform on Fusion core, or NetworkRigidbody3D with the Physics addon).")]
        [SerializeField] private NetworkPrefabRef playerPrefab;

        [Tooltip("Optional spawn points. Falls back to a small ring around the origin.")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("Network Ship")]
        [Tooltip("Optional networked ship prefab (NetworkObject + NetworkShip + Rigidbody + sync component). " +
                 "If assigned, host spawns exactly one authoritative ship at session start.")]
        [SerializeField] private NetworkPrefabRef shipPrefab;

        [Tooltip("Optional explicit spawn transform for the network ship. If empty, the manager " +
                 "uses Scene Ship Placeholder Name, then origin.")]
        [SerializeField] private Transform shipSpawnPoint;

        [Tooltip("Name of the scene placeholder ship used as a spawn pose fallback.")]
        [SerializeField] private string sceneShipPlaceholderName = "MooredShipPlaceholder";

        [Tooltip("When true, disables the non-networked placeholder ship after the network ship spawns.")]
        [SerializeField] private bool disableScenePlaceholderAfterShipSpawn = true;

        [Tooltip("If false, ship spawning is skipped even if Ship Prefab is assigned.")]
        [SerializeField] private bool spawnShipOnSessionStart = true;

        [Tooltip("Auto-start on Play for quick iteration (creates its own runner, no menu). " +
                 "Leave OFF when the scene is launched from the Fusion Menu — the manager then " +
                 "attaches to the runner the menu already created for the session.")]
        [SerializeField] private bool autoStartOnPlay = false;
        [SerializeField] private GameMode autoStartMode = GameMode.AutoHostOrClient;

        [Tooltip("Seconds to wait for the menu-created runner to appear before giving up " +
                 "(only used when Auto Start On Play is off).")]
        [SerializeField] private float attachTimeout = 10f;

        private NetworkRunner _runner;
        private readonly Dictionary<PlayerRef, NetworkObject> _players = new();
        private readonly HashSet<PlayerRef> _pendingSpawns = new();
        private readonly InputReader _input = new();
        private int _spawnCounter;
        private bool _attached; // true when we borrowed a runner we didn't create

        private NetworkObject _ship;
        private bool _shipSpawnPending;
        private bool _warnedMissingShipPrefab;
        private GameObject _disabledPlaceholderShip;
        private bool _disabledPlaceholderWasActive;

        public NetworkRunner Runner => _runner;
        public bool IsRunning => _runner != null && _runner.IsRunning;

        private async void Start()
        {
            if (autoStartOnPlay)
                await StartGame(autoStartMode, null);
            else
                await AttachToActiveRunner();
        }

        // ---- Menu-driven attach --------------------------------------------------
        /// <summary>
        /// When this scene is loaded by the Fusion Menu, the menu already owns a running
        /// NetworkRunner. We don't create our own — we find it, register our callbacks so
        /// we drive spawning + input, and spawn anyone who joined before we existed
        /// (the host's own player, whose OnPlayerJoined fired during scene load).
        /// </summary>
        private async UniTask AttachToActiveRunner()
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, attachTimeout);
            NetworkRunner runner = FindRunningRunner();
            while (runner == null && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.Delay(50);
                runner = FindRunningRunner();
            }

            if (runner == null)
            {
                Debug.LogWarning("[ConnectionManager] No active runner found to attach to. " +
                                 "Launch this scene from the menu, or enable Auto Start On Play.");
                return;
            }

            _runner = runner;
            _attached = true;
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);
            Debug.Log($"[ConnectionManager] Attached to menu runner. Mode={_runner.GameMode}, " +
                      $"IsServer={_runner.IsServer}, LocalPlayer={_runner.LocalPlayer}.");

            SpawnMissingPlayers();
            EnsureShipSpawned();
        }

        private static NetworkRunner FindRunningRunner()
        {
            foreach (var r in NetworkRunner.Instances)
                if (r != null && r.IsRunning)
                    return r;
            return null;
        }

        /// <summary>
        /// Adds the Physics-addon simulator to the runner GameObject (once) so PhysX is stepped
        /// inside Fusion's tick and predicted forward on clients. SimulateForward = clients run
        /// Physics.Simulate on new (forward) ticks — the cheap client-side prediction mode.
        /// </summary>
        private static void EnsurePhysicsSimulator(GameObject runnerGo)
        {
            if (runnerGo.GetComponent<Fusion.Addons.Physics.RunnerSimulatePhysics3D>() != null)
                return;

            var sim = runnerGo.AddComponent<Fusion.Addons.Physics.RunnerSimulatePhysics3D>();
            sim.ClientPhysicsSimulation = Fusion.Addons.Physics.ClientPhysicsSimulation.SimulateForward;
        }

        /// <summary>Spawn any player already in the session that we haven't spawned yet.</summary>
        private void SpawnMissingPlayers()
        {
            if (_runner.GameMode == GameMode.Shared)
            {
                PlayerRef local = _runner.LocalPlayer;
                if (local.IsRealPlayer && !_players.ContainsKey(local))
                    SpawnFor(_runner, local);
                return;
            }

            if (!_runner.IsServer) return;
            foreach (PlayerRef player in _runner.ActivePlayers)
                if (!_players.ContainsKey(player))
                    SpawnFor(_runner, player);
        }

        // ---- Public entry points -------------------------------------------------
        public UniTask<bool> StartHost(string session = null)   => StartGame(GameMode.Host, session);
        public UniTask<bool> StartClient(string session = null) => StartGame(GameMode.Client, session);
        public UniTask<bool> StartAuto(string session = null)   => StartGame(GameMode.AutoHostOrClient, session);
        public UniTask<bool> StartShared(string session = null) => StartGame(GameMode.Shared, session);

        public async UniTask Disconnect()
        {
            if (_runner != null)
                await _runner.Shutdown();
        }

        // ---- Core start ----------------------------------------------------------
        private async UniTask<bool> StartGame(GameMode mode, string session)
        {
            if (_runner != null)
            {
                Debug.LogWarning("[ConnectionManager] Runner already exists.");
                return false;
            }

            ApplyAppId();

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            // Fusion Physics addon: steps + predicts PhysX inside the sim loop. Must exist on the
            // runner GameObject before StartGame so Fusion registers it. (Menu-created runners get
            // this from FusionMenuConnectionBehaviourSdk.CreateRunner instead.)
            EnsurePhysicsSimulator(gameObject);

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            // Sync the scene that's already open (single-scene bootstrap). NetworkSceneInfo
            // is what StartGameArgs.Scene expects; the default scene manager reconciles a
            // scene that's already loaded, so Multiplayer Play Mode won't double-load it.
            var sceneInfo = new NetworkSceneInfo();
            SceneRef sceneRef = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
            if (sceneRef.IsValid)
                sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Additive);

            var args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = string.IsNullOrEmpty(session)
                    ? (config != null ? config.DefaultSessionName : "rum-overboard")
                    : session,
                PlayerCount = config != null ? config.MaxCrew : 4,
                SceneManager = sceneManager,
                Scene = sceneInfo,
            };

            StartGameResult result = await _runner.StartGame(args);
            if (!result.Ok)
            {
                Debug.LogError($"[ConnectionManager] StartGame failed: {result.ShutdownReason}");
                return false;
            }

            Debug.Log($"[ConnectionManager] Running as {mode}. IsServer={_runner.IsServer}, " +
                      $"LocalPlayer={_runner.LocalPlayer}, Session='{_runner.SessionInfo.Name}'.");

            EnsureShipSpawned();
            return true;
        }

        /// <summary>
        /// Pushes our App Id into Fusion's global settings so the dashboard step is optional.
        /// (PhotonAppSettings.Global exists once the Fusion SDK is imported.)
        /// </summary>
        private void ApplyAppId()
        {
            string appId = config != null && !string.IsNullOrEmpty(config.PhotonAppId)
                ? config.PhotonAppId
                : GameConfig.DefaultAppId;

            var global = PhotonAppSettings.Global;
            if (global == null || string.IsNullOrEmpty(appId)) return;

            global.AppSettings.AppIdFusion = appId;
            string region = config != null ? config.FixedRegion : string.Empty;
            if (!string.IsNullOrEmpty(region))
                global.AppSettings.FixedRegion = region;
        }

        // ---- Spawning ------------------------------------------------------------
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Host mode: only the server spawns everyone. Shared mode: each client spawns itself.
            bool shouldSpawn = runner.GameMode == GameMode.Shared
                ? player == runner.LocalPlayer
                : runner.IsServer;
            if (!shouldSpawn || _players.ContainsKey(player)) return;

            SpawnFor(runner, player);

            // In host mode this no-ops after the first successful ship spawn.
            EnsureShipSpawned();
        }

        // Strictly one spawn per player. `_pendingSpawns` is the in-flight lock: whoever adds
        // the player first owns the spawn, so OnPlayerJoined and the attach-time sweep can both
        // call this without ever producing a duplicate (which is what corrupted authority and
        // left NetworkRigidbody3D stuck kinematic).
        private void SpawnFor(NetworkRunner runner, PlayerRef player)
        {
            if (_players.ContainsKey(player) || !_pendingSpawns.Add(player))
                return;

            SpawnRoutine(runner, player).Forget();
        }

        private async UniTaskVoid SpawnRoutine(NetworkRunner runner, PlayerRef player)
        {
            float deadline = Time.realtimeSinceStartup + 8f;
            try
            {
                while (runner != null && runner.IsRunning && Time.realtimeSinceStartup < deadline)
                {
                    if (_players.ContainsKey(player))
                        return;

                    try
                    {
                        GetSpawnPose(out Vector3 pos, out Quaternion rot);
                        NetworkObject obj = runner.Spawn(playerPrefab, pos, rot, player);
                        if (obj != null)
                        {
                            _players[player] = obj;
                            Debug.Log($"[ConnectionManager] Spawned player {player} (id={obj.Id}).");
                            return;
                        }
                    }
                    catch (NetworkObjectSpawnException ex)
                    {
                        // Prefab table still loading — poll briefly and retry.
                        Debug.LogWarning($"[ConnectionManager] Spawn deferred for {player}: {ex.Message}");
                    }

                    await UniTask.Delay(120);
                }

                if (!_players.ContainsKey(player) && runner != null && runner.IsRunning)
                    Debug.LogError($"[ConnectionManager] Could not spawn player {player} after retries. " +
                                   $"Check the Fusion prefab table and Player Prefab assignment.");
            }
            finally
            {
                _pendingSpawns.Remove(player);
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            _pendingSpawns.Remove(player);
            if (!_players.TryGetValue(player, out NetworkObject obj)) return;
            if (obj != null && runner.IsServer)
                runner.Despawn(obj);
            _players.Remove(player);
        }

        private void GetSpawnPose(out Vector3 pos, out Quaternion rot)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform p = spawnPoints[_spawnCounter % spawnPoints.Length];
                pos = p.position;
                rot = p.rotation;
            }
            else
            {
                float angle = _spawnCounter * 90f * Mathf.Deg2Rad;
                pos = new Vector3(Mathf.Cos(angle), 1f, Mathf.Sin(angle)) * 1.5f;
                rot = Quaternion.identity;
            }
            _spawnCounter++;
        }

        private void EnsureShipSpawned()
        {
            if (!CanSpawnShip(_runner) || _shipSpawnPending || _ship != null)
                return;

            _shipSpawnPending = true;
            SpawnShipRoutine(_runner).Forget();
        }

        private bool CanSpawnShip(NetworkRunner runner)
        {
            if (!spawnShipOnSessionStart || runner == null || !runner.IsRunning)
                return false;

            if (!shipPrefab.IsValid)
            {
                if (!_warnedMissingShipPrefab)
                {
                    _warnedMissingShipPrefab = true;
                    Debug.Log("[ConnectionManager] Ship Prefab is not assigned. Skipping network ship spawn.");
                }
                return false;
            }

            // This implementation is host-authoritative. Shared-mode ship ownership is a separate task.
            if (runner.GameMode == GameMode.Shared)
                return false;

            return runner.IsServer;
        }

        private async UniTaskVoid SpawnShipRoutine(NetworkRunner runner)
        {
            float deadline = Time.realtimeSinceStartup + 8f;

            try
            {
                while (runner != null && runner.IsRunning && Time.realtimeSinceStartup < deadline)
                {
                    if (_ship != null)
                        return;

                    GetShipSpawnPose(out Vector3 pos, out Quaternion rot, out GameObject placeholder);

                    try
                    {
                        NetworkObject shipObj = runner.Spawn(shipPrefab, pos, rot);
                        if (shipObj != null)
                        {
                            _ship = shipObj;
                            DisablePlaceholderShip(placeholder);
                            Debug.Log($"[ConnectionManager] Spawned network ship '{shipObj.name}' (id={shipObj.Id}).");
                            return;
                        }
                    }
                    catch (NetworkObjectSpawnException ex)
                    {
                        // Prefab table may still be warming up or Ship Prefab can be unassigned.
                        Debug.LogWarning($"[ConnectionManager] Ship spawn deferred: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ConnectionManager] Ship spawn failed: {ex.Message}");
                        return;
                    }

                    await UniTask.Delay(120);
                }

                if (_ship == null && runner != null && runner.IsRunning)
                    Debug.LogWarning("[ConnectionManager] Ship was not spawned. Assign 'Ship Prefab' on GameManager if a networked ship is required.");
            }
            finally
            {
                _shipSpawnPending = false;
            }
        }

        private void GetShipSpawnPose(out Vector3 pos, out Quaternion rot, out GameObject placeholder)
        {
            placeholder = null;

            if (shipSpawnPoint != null)
            {
                pos = shipSpawnPoint.position;
                rot = shipSpawnPoint.rotation;
                return;
            }

            if (!string.IsNullOrWhiteSpace(sceneShipPlaceholderName))
            {
                placeholder = GameObject.Find(sceneShipPlaceholderName);
                if (placeholder != null)
                {
                    pos = placeholder.transform.position;
                    rot = placeholder.transform.rotation;
                    return;
                }
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
        }

        private void DisablePlaceholderShip(GameObject placeholder)
        {
            if (!disableScenePlaceholderAfterShipSpawn || placeholder == null)
                return;

            _disabledPlaceholderShip = placeholder;
            _disabledPlaceholderWasActive = placeholder.activeSelf;
            placeholder.SetActive(false);
        }

        private void RestorePlaceholderShip()
        {
            if (_disabledPlaceholderShip == null)
                return;

            _disabledPlaceholderShip.SetActive(_disabledPlaceholderWasActive);
            _disabledPlaceholderShip = null;
            _disabledPlaceholderWasActive = false;
        }

        // ---- Input ---------------------------------------------------------------
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (Camera.main != null)
                _input.CameraYaw = Camera.main.transform.eulerAngles.y;
            input.Set(_input.Read());
        }

        // ---- Remaining INetworkRunnerCallbacks (unused, required by interface) ----
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) =>
            Debug.LogWarning($"[ConnectionManager] Disconnected: {reason}");
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            Debug.LogError($"[ConnectionManager] Connect failed: {reason}");
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnSceneLoadDone(NetworkRunner runner)
        {
            EnsureShipSpawned();
        }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[ConnectionManager] Shutdown: {shutdownReason}");
            _players.Clear();
            _pendingSpawns.Clear();
            _spawnCounter = 0;
            _ship = null;
            _shipSpawnPending = false;
            _warnedMissingShipPrefab = false;
            RestorePlaceholderShip();
            _runner = null;
            _attached = false;
        }

        private void OnDestroy()
        {
            // We only own runners we created; a borrowed (menu) runner is left alone.
            if (_runner != null && _attached)
                _runner.RemoveCallbacks(this);
        }
    }
}
#endif
