#if FUSION2
using System;
using System.IO;
using Cysharp.Threading.Tasks;
using Fusion;
using RumOverboard.Core;
using RumOverboard.Gameplay;
using RumOverboard.Gameplay.World;
using RumOverboard.Gameplay.World.Saves;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.Frontend
{
    public enum SessionPhase
    {
        Menu,
        Connecting,
        Lobby,
        Sailing,
        Leaving,
    }

    /// <summary>What the player chose on the "new / load voyage" screen.</summary>
    public sealed class HostRequest
    {
        public string LobbyName;
        public bool Private;
        public int MaxCrew = 4;
        public VoyageSave Voyage;
        /// <summary>Offline single-player: no Photon, no lobby room, straight to sea.</summary>
        public bool Offline;
    }

    /// <summary>
    /// Persistent (DontDestroyOnLoad) owner of the multiplayer session for the real game flow:
    /// MainMenu → Lobby room → voyage (WorldMapScene) → back to MainMenu. Owns the NetworkRunner
    /// (the gameplay scene's ConnectionManager attaches to it), the lobby directory and the
    /// voyage hand-off to the world (<see cref="VoyageLaunch"/>).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameSession : MonoBehaviour
    {
        private const float LostRunnerGrace = 20f;

        [SerializeField] private GameConfig config;
        [SerializeField] private string menuScene = "MainMenu";
        [SerializeField] private string lobbyScene = "Lobby";
        [SerializeField] private string gameScene = "WorldMapScene";

        public static GameSession Instance { get; private set; }

        public GameConfig Config => config;
        public string MenuScene => menuScene;
        public SessionPhase Phase { get; private set; } = SessionPhase.Menu;
        public string Status { get; private set; } = string.Empty;
        /// <summary>Message to show on the main menu (disconnect reason, errors). Cleared by the UI.</summary>
        public string Notice { get; set; } = string.Empty;
        public string Code { get; private set; } = string.Empty;
        public LobbyAdvert Advert { get; private set; }
        public bool Offline { get; private set; }
        public ILobbyDirectory Directory { get; private set; }
        public NetworkRunner Runner => _runner != null && _runner.IsRunning ? _runner : FindRunningRunner();
        public bool IsHost => Runner != null && Runner.IsServer;
        public int MaxCrew => config != null ? config.MaxCrew : 4;

        /// <summary>Raised whenever phase / status / advert changes.</summary>
        public event Action Changed;

        private NetworkRunner _runner;
        private Callbacks _callbacks;
        private bool _leaving;
        private float _runnerLostAt = -1f;

        // =========================================================================================
        // Bootstrap
        // =========================================================================================
        public static GameSession Ensure(GameConfig cfg)
        {
            if (Instance != null)
                return Instance;
            var go = new GameObject("GameSession");
            go.SetActive(false);
            var session = go.AddComponent<GameSession>();
            session.config = cfg;
            go.SetActive(true);
            return session;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (config == null)
                config = ScriptableObject.CreateInstance<GameConfig>();
            _callbacks = new Callbacks(this);
            Directory = new PhotonLobbyDirectory(transform, config);
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (GetComponent<FrontendUI>() == null)
                gameObject.AddComponent<FrontendUI>();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance != this)
                return;
            Instance = null;
            ConnectionManager.ExternalSessionActive = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            CodeOverride = null;
            ConnectionManager.ExternalSessionActive = false;
        }

        /// <summary>Tests: host under this fixed lobby code instead of a random one.</summary>
        public static string CodeOverride { get; set; }

        // =========================================================================================
        // Host
        // =========================================================================================
        public async UniTask<bool> HostAsync(HostRequest request)
        {
            if (Phase != SessionPhase.Menu || request == null)
                return false;
            Offline = request.Offline;
            SetPhase(SessionPhase.Connecting, request.Offline ? "Поднимаем паруса…" : "Создаём лобби…");
            Notice = string.Empty;
            await Directory.StopBrowsingAsync();

            int lobbyIndex = BuildIndex(lobbyScene), gameIndex = BuildIndex(gameScene);
            if (gameIndex < 0 || (!request.Offline && lobbyIndex < 0))
                return Fail($"Сцены '{lobbyScene}' / '{gameScene}' нет в Build Settings.");

            VoyageLaunch.Active = request.Voyage ?? VoyageSave.CreateNew(request.LobbyName);
            Advert = new LobbyAdvert
            {
                Name = string.IsNullOrWhiteSpace(request.LobbyName) ? $"Команда {PlayerProfile.Name}" : request.LobbyName.Trim(),
                Host = PlayerProfile.Name,
                Voyage = VoyageLabel(VoyageLaunch.Active),
                Private = request.Private,
            };
            int crew = Mathf.Clamp(request.MaxCrew, 1, Mathf.Max(1, MaxCrew));

            const int attempts = 3;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                string code = request.Offline ? "OFFLINE" : CodeOverride ?? Directory.CreateCode();
                NetworkRunner runner = CreateRunner(request.Offline ? "Single" : "Host");
                var scene = new NetworkSceneInfo();
                scene.AddSceneRef(SceneRef.FromIndex(request.Offline ? gameIndex : lobbyIndex), LoadSceneMode.Single);
                var args = new StartGameArgs
                {
                    GameMode = request.Offline ? GameMode.Single : GameMode.Host,
                    SessionName = code,
                    PlayerCount = request.Offline ? 1 : crew,
                    SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                    Scene = scene,
                    IsVisible = !request.Private,
                    IsOpen = true,
                    SessionProperties = request.Offline ? null : Directory.SessionProperties(Advert),
                    ConnectionToken = PlayerProfile.Token(),
                };

                StartGameResult result = await runner.StartGame(args);
                if (result.Ok)
                {
                    Code = code;
                    Debug.Log($"[Session] Hosting '{Advert.Name}' code={code} mode={args.GameMode} crew={crew} " +
                              $"voyage='{VoyageLaunch.Active.Name}'.");
                    RefreshPhaseFromScene();
                    return true;
                }

                DestroyRunner(runner);
                Debug.LogWarning($"[Session] Host start failed ({result.ShutdownReason}), attempt {attempt + 1}.");
                if (result.ShutdownReason != ShutdownReason.GameIdAlreadyExists)
                {
                    VoyageLaunch.Active = null;
                    return Fail(ReasonText(result.ShutdownReason), offerOffline: !request.Offline);
                }
            }
            VoyageLaunch.Active = null;
            return Fail("Не удалось подобрать свободный код лобби.");
        }

        /// <summary>Host in the lobby room: everyone aboard, load the voyage for the whole crew.</summary>
        public bool StartVoyage()
        {
            NetworkRunner runner = Runner;
            if (Phase != SessionPhase.Lobby || runner == null || !runner.IsServer)
                return false;
            int gameIndex = BuildIndex(gameScene);
            if (gameIndex < 0)
                return false;
            LobbyAdvert advert = Advert;
            advert.Sailing = true;
            Advert = advert;
            Directory.Publish(runner, advert);
            SetPhase(SessionPhase.Connecting, "Отдаём швартовы…");
            runner.LoadScene(SceneRef.FromIndex(gameIndex), LoadSceneMode.Single);
            return true;
        }

        // =========================================================================================
        // Join
        // =========================================================================================
        public async UniTask<bool> JoinAsync(string codeInput)
        {
            if (Phase != SessionPhase.Menu)
                return false;
            string code = LobbyCode.Normalize(codeInput);
            if (code == null)
            {
                Notice = $"Код лобби — {LobbyCode.Length} символов (буквы и цифры 2–9).";
                Changed?.Invoke();
                return false;
            }
            Offline = false;
            SetPhase(SessionPhase.Connecting, $"Подключаемся к {code}…");
            Notice = string.Empty;
            await Directory.StopBrowsingAsync();

            string session = await Directory.ResolveAsync(code);
            if (string.IsNullOrEmpty(session))
                return Fail("Лобби с таким кодом не найдено.");

            NetworkRunner runner = CreateRunner("Client");
            var args = new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = session,
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                EnableClientSessionCreation = false,
                ConnectionToken = PlayerProfile.Token(),
            };
            StartGameResult result = await runner.StartGame(args);
            if (!result.Ok)
            {
                DestroyRunner(runner);
                return Fail(ReasonText(result.ShutdownReason));
            }

            if (!PhotonLobbyDirectory.VersionMatches(runner.SessionInfo))
            {
                _leaving = true;
                await runner.Shutdown();
                _leaving = false;
                DestroyRunner(runner);
                return Fail("У капитана другая версия игры.");
            }

            Code = code;
            Advert = Directory.Read(runner.SessionInfo);
            Debug.Log($"[Session] Joined '{Advert.Name}' code={code} (sailing={Advert.Sailing}).");
            RefreshPhaseFromScene();
            return true;
        }

        // =========================================================================================
        // Leave
        // =========================================================================================
        public void Leave(string notice = null) => LeaveAsync(notice).Forget();

        public async UniTask LeaveAsync(string notice = null)
        {
            if (Phase == SessionPhase.Menu || Phase == SessionPhase.Leaving)
                return;
            SetPhase(SessionPhase.Leaving, "Сходим на берег…");
            _leaving = true;
            try
            {
                NetworkWorld world = NetworkWorld.Instance;
                if (world != null && world.IsHost && world.HostSaveVoyage(force: true))
                    Debug.Log($"[Session] Voyage '{VoyageLaunch.Active?.Name}' saved on leave.");

                NetworkRunner runner = Runner ?? _runner;
                if (runner != null)
                {
                    try { await runner.Shutdown(); }
                    catch (Exception e) { Debug.LogWarning($"[Session] Shutdown: {e.Message}"); }
                }
                DestroyRunner(_runner);
            }
            finally
            {
                _leaving = false;
            }
            ReturnToMenu(notice);
        }

        private void ReturnToMenu(string notice)
        {
            _runner = null;
            _runnerLostAt = -1f;
            Code = string.Empty;
            Advert = default;
            Offline = false;
            VoyageLaunch.Active = null;
            ConnectionManager.ExternalSessionActive = false;
            UiInputCapture.Active = false;
            Notice = notice ?? string.Empty;
            SetPhase(SessionPhase.Menu, string.Empty);
            if (SceneManager.GetActiveScene().name != menuScene)
                SceneManager.LoadScene(menuScene, LoadSceneMode.Single);
        }

        // =========================================================================================
        // Runner management
        // =========================================================================================
        private NetworkRunner CreateRunner(string label)
        {
            ConnectionManager.ApplyAppId(config);
            ConnectionManager.ExternalSessionActive = true;
            var go = new GameObject($"NetworkRunner ({label})");
            go.transform.SetParent(transform, false);
            var runner = go.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            runner.AddCallbacks(_callbacks);
            ConnectionManager.EnsurePhysicsSimulator(go);
            go.AddComponent<NetworkSceneManagerDefault>();
            _runner = runner;
            return runner;
        }

        private void DestroyRunner(NetworkRunner runner)
        {
            if (runner == null)
                return;
            runner.RemoveCallbacks(_callbacks);
            if (runner.gameObject != null)
                Destroy(runner.gameObject);
            if (_runner == runner)
                _runner = null;
        }

        private static NetworkRunner FindRunningRunner()
        {
            foreach (NetworkRunner r in NetworkRunner.Instances)
                if (r != null && r.IsRunning)
                    return r;
            return null;
        }

        private void Update()
        {
            if (Phase != SessionPhase.Lobby && Phase != SessionPhase.Sailing)
                return;

            // Host migration hands the session to a new runner the ConnectionManager owns: follow it.
            if (_runner == null || !_runner.IsRunning)
            {
                NetworkRunner other = FindRunningRunner();
                if (other != null)
                {
                    if (_runner != null && _runner != other)
                        DestroyRunner(_runner);
                    _runner = other;
                    _runner.AddCallbacks(_callbacks);
                    _runnerLostAt = -1f;
                    Debug.Log("[Session] Following the migrated runner.");
                    return;
                }
                if (_runnerLostAt < 0f)
                    _runnerLostAt = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _runnerLostAt > LostRunnerGrace)
                    ReturnToMenu("Связь с сессией потеряна.");
            }
            else
            {
                _runnerLostAt = -1f;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Phase == SessionPhase.Connecting || Phase == SessionPhase.Lobby || Phase == SessionPhase.Sailing)
                RefreshPhaseFromScene();
        }

        private void RefreshPhaseFromScene()
        {
            string active = SceneManager.GetActiveScene().name;
            if (active == lobbyScene)
                SetPhase(SessionPhase.Lobby, string.Empty);
            else if (active == gameScene)
                SetPhase(SessionPhase.Sailing, string.Empty);
        }

        // =========================================================================================
        // Callbacks from the runner
        // =========================================================================================
        private void HandleShutdown(NetworkRunner runner, ShutdownReason reason)
        {
            if (_leaving || reason == ShutdownReason.HostMigration)
                return;
            if (Phase == SessionPhase.Menu || (Phase == SessionPhase.Connecting && string.IsNullOrEmpty(Code)))
                return; // a failed start: HostAsync / JoinAsync report it
            if (runner != _runner && _runner != null && _runner.IsRunning)
                return;
            Debug.Log($"[Session] Session ended: {reason}.");
            ReturnToMenu(reason == ShutdownReason.Ok ? "Сессия завершена." : ReasonText(reason));
        }

        private void HandleHostMigration(NetworkRunner runner)
        {
            if (Phase == SessionPhase.Sailing)
                return; // ConnectionManager resumes the voyage under the new host
            Debug.Log("[Session] Captain left the lobby.");
            _leaving = true;
            ShutdownQuietly(runner).Forget();
        }

        private async UniTaskVoid ShutdownQuietly(NetworkRunner runner)
        {
            try
            {
                if (runner != null)
                    await runner.Shutdown(shutdownReason: ShutdownReason.HostMigration);
            }
            finally
            {
                _leaving = false;
                DestroyRunner(runner);
                ReturnToMenu("Капитан распустил лобби.");
            }
        }

        // =========================================================================================
        // Helpers
        // =========================================================================================
        private bool Fail(string message, bool offerOffline = false)
        {
            ConnectionManager.ExternalSessionActive = false;
            Notice = message;
            OfferOffline = offerOffline;
            SetPhase(SessionPhase.Menu, string.Empty);
            return false;
        }

        /// <summary>The last host attempt failed for a network reason; the UI may offer an offline voyage.</summary>
        public bool OfferOffline { get; set; }

        private void SetPhase(SessionPhase phase, string status)
        {
            Phase = phase;
            Status = status ?? string.Empty;
            Changed?.Invoke();
        }

        public static string VoyageLabel(VoyageSave save)
        {
            if (save == null || !save.IsGenerated)
                return "Новый рейс";
            return $"{save.Name} · {save.ProgressText}";
        }

        public static string ReasonText(ShutdownReason reason) => reason switch
        {
            ShutdownReason.GameNotFound => "Лобби не найдено — проверьте код.",
            ShutdownReason.GameIsFull => "В лобби нет мест.",
            ShutdownReason.GameClosed => "Лобби закрыто.",
            ShutdownReason.PhotonCloudTimeout => "Нет связи с Photon Cloud.",
            ShutdownReason.ConnectionTimeout => "Хост не отвечает (таймаут).",
            ShutdownReason.ConnectionRefused => "Хост отклонил подключение.",
            ShutdownReason.DisconnectedByPluginLogic => "Отключено сервером.",
            ShutdownReason.InvalidAuthentication => "Ошибка авторизации Photon (проверьте App Id).",
            ShutdownReason.MaxCcuReached => "Превышен лимит игроков Photon (CCU).",
            ShutdownReason.InvalidRegion => "Неверный регион Photon.",
            _ => $"Соединение прервано ({reason}).",
        };

        private static int BuildIndex(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                if (Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)) == sceneName)
                    return i;
            return -1;
        }

        private sealed class Callbacks : NetworkCallbacksBase
        {
            private readonly GameSession _owner;
            public Callbacks(GameSession owner) => _owner = owner;

            public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
            {
                if (_owner != null)
                    _owner.HandleShutdown(runner, shutdownReason);
            }

            public override void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
            {
                if (_owner != null)
                    _owner.HandleHostMigration(runner);
            }
        }
    }
}
#endif
