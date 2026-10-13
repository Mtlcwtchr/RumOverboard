#if FUSION2
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Fusion;
using RumOverboard.Gameplay;
using RumOverboard.Gameplay.World;
using RumOverboard.Gameplay.World.Generation;
using RumOverboard.Gameplay.World.Saves;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RumOverboard.Frontend
{
    /// <summary>
    /// Play-mode scenarios for the boot menu (runner FrontendSetup.RunMenuSmoke, starts in Boot):
    ///   <c>-menuSmoke</c>                    solo: host a Photon lobby → sail → autosave → leave →
    ///                                         load the save offline → resumed at the saved checkpoint.
    ///   <c>-menuSmokeHost CODE CLIENTS</c>   host lobby CODE, wait for CLIENTS players (lobby + late joiners).
    ///   <c>-menuSmokeClient CODE</c>         join CODE (lobby or voyage in progress), get ready, sail, leave.
    /// Optional <c>-profileName NAME</c>. Exits the editor/player with 0 (all passed) or 1.
    /// </summary>
    public sealed class MenuSmokeTest : MonoBehaviour
    {
        private const string SoloFlag = "-menuSmoke";
        private const string HostFlag = "-menuSmokeHost";
        private const string ClientFlag = "-menuSmokeClient";

        private readonly StringBuilder _report = new();
        private readonly List<string> _failures = new();
        private GameSession _session;
        private string _role;
        private string _code;
        private int _expectedClients;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string role = args.Contains(HostFlag) ? HostFlag : args.Contains(ClientFlag) ? ClientFlag : args.Contains(SoloFlag) ? SoloFlag : null;
            if (role == null || FindAnyObjectByType<MenuSmokeTest>() != null)
                return;
            var go = new GameObject("MenuSmokeTest");
            DontDestroyOnLoad(go);
            var test = go.AddComponent<MenuSmokeTest>();
            test._role = role;
            int at = Array.IndexOf(args, role);
            test._code = at + 1 < args.Length ? args[at + 1] : null;
            if (role == HostFlag && at + 2 < args.Length)
                int.TryParse(args[at + 2], out test._expectedClients);
            int name = Array.IndexOf(args, "-profileName");
            if (name >= 0 && name + 1 < args.Length)
                PlayerProfile.SetSessionName(args[name + 1]);
        }

        private IEnumerator Start()
        {
            Log($"== Menu smoke test ({_role}) as '{PlayerProfile.Name}' ==");
            VoyageSaveStore.DirectoryOverride = Path.Combine(Application.temporaryCachePath, "MenuSmokeSaves_" + _role.Trim('-'));
            if (Directory.Exists(VoyageSaveStore.DirectoryOverride))
                Directory.Delete(VoyageSaveStore.DirectoryOverride, true);

            yield return Run(Boots());
            if (_failures.Count == 0)
            {
                if (_role == SoloFlag)
                {
                    yield return Run(Navigation());
                    yield return Run(SoloOnline());
                    yield return Run(LoadOffline());
                }
                else if (_role == HostFlag)
                    yield return Run(MultiHost());
                else
                    yield return Run(MultiClient());
            }
            Finish();
        }

        // =========================================================================================
        private IEnumerator Boots()
        {
            yield return Until(() => GameSession.Instance != null && SceneManager.GetActiveScene().name == GameSession.Instance.MenuScene, 30f);
            _session = GameSession.Instance;
            Check(_session != null, "boot: Boot → MainMenu, GameSession created");
            if (_session == null)
                yield break;
            FrontendUI ui = _session.GetComponent<FrontendUI>();
            Check(ui != null && ui.CurrentScreen == FrontendUI.MenuScreen.Main, "boot: main menu screen shown");
            Check(_session.Phase == SessionPhase.Menu, "boot: session idle in menu");
            Check(UnityEngine.EventSystems.EventSystem.current != null, "boot: an EventSystem is active");
        }

        private IEnumerator Navigation()
        {
            FrontendUI ui = _session.GetComponent<FrontendUI>();
            ui.ShowMenu(FrontendUI.MenuScreen.Load);
            Check(ui.CurrentScreen == FrontendUI.MenuScreen.Load, "ui: load screen opens");
            Check(VoyageSaveStore.List().Count == 0, "ui: no saves in a clean save folder");
            ui.ShowMenu(FrontendUI.MenuScreen.Join);
            yield return Until(() => _session.Directory.IsBrowsing || _session.Directory.Status.Contains("Нет связи"), 25f);
            Log($"browse: '{_session.Directory.Status}', {_session.Directory.Listings.Count} public lobbies");
            Check(_session.Directory.IsBrowsing, "ui: lobby browser connected to Photon");
            ui.ShowMenu(FrontendUI.MenuScreen.Main);
            yield return Until(() => !_session.Directory.IsBrowsing, 10f);
            Check(!_session.Directory.IsBrowsing, "ui: browser stops when leaving the join screen");

            Check(LobbyCode.Normalize(" ab-c 234 ") == "ABC234", "code: normalizes case and separators");
            Check(LobbyCode.Normalize("ABCDE0") == null && LobbyCode.Normalize("ABC") == null, "code: rejects ambiguous / short codes");
            Check(LobbyCode.Normalize(LobbyCode.Create()) != null, "code: generated codes are valid");
        }

        private string _saveId;

        private IEnumerator SoloOnline()
        {
            var save = VoyageSave.CreateNew("Smoke voyage", 424242);
            _saveId = save.Id;
            yield return Host(new HostRequest { LobbyName = "Smoke lobby", Private = true, MaxCrew = 4, Voyage = save });
            if (!Check(_session.Phase == SessionPhase.Lobby, $"host: lobby created (notice '{_session.Notice}')"))
                yield break;
            Check(LobbyCode.Normalize(_session.Code) == _session.Code, $"host: lobby code '{_session.Code}' is valid");
            Check(_session.Runner != null && _session.Runner.IsServer && _session.Runner.SessionInfo.Name == _session.Code,
                "host: Fusion session named by the code, running as host");
            Check(_session.Runner != null && !_session.Runner.SessionInfo.IsVisible, "host: private lobby is hidden from the list");

            yield return Until(() => NetworkLobby.Instance != null && NetworkLobby.Instance.Count == 1, 10f);
            NetworkLobby lobby = NetworkLobby.Instance;
            if (!Check(lobby != null && lobby.Count == 1, "lobby: room spawned with the captain"))
                yield break;
            NetworkLobby.MemberView me = lobby.Snapshot()[0];
            Check(me.IsCaptain && me.IsLocal && me.Name == PlayerProfile.Name, $"lobby: captain listed as '{me.Name}'");
            Check(lobby.AllReady, "lobby: solo captain may sail");
            Check(VoyageSaveStore.Load(_saveId) == null, "save: nothing written before sailing");

            Check(_session.StartVoyage(), "sail: voyage started");
            NetworkWorld world = null;
            yield return Until(() =>
            {
                world = NetworkWorld.Instance;
                return _session.Phase == SessionPhase.Sailing && world != null && world.IsReady &&
                       world.FindShip() != null && LocalPlayer() != null && world.MooredAt >= 0;
            }, 90f);
            if (!Check(world != null && world.IsReady && LocalPlayer() != null, "sail: world generated, crew aboard"))
                yield break;
            Check(SceneManager.GetActiveScene().name == "WorldMapScene", "sail: world scene loaded");
            Check(world.Seed == 424242, $"sail: world uses the voyage seed ({world.Seed})");
            Check(world.MooredAt == 0, "sail: ship moored at the start harbor");
            Check(NetworkLobby.Instance == null, "sail: lobby room unloaded");

            VoyageSave written = VoyageSaveStore.Load(_saveId);
            Check(written != null && written.IsGenerated && written.Seed == 424242 && written.Checkpoints == world.Layout.Checkpoints.Count,
                "save: voyage written on first generation");

            FrontendUI ui = _session.GetComponent<FrontendUI>();
            ui.SetPause(true);
            Check(ui.PauseOpen && UiInputCapture.Active && Cursor.lockState == CursorLockMode.None, "pause: opens, captures input, frees cursor");
            ui.SetPause(false);
            Check(!ui.PauseOpen && !UiInputCapture.Active, "pause: closes and releases input");

            world.HostSetReached(2);
            yield return Until(() => VoyageSaveStore.Load(_saveId)?.Reached == 2, 5f);
            Check(VoyageSaveStore.Load(_saveId)?.Reached == 2, "save: autosave on checkpoint progress");
            _savedWind = world.WindDegrees;

            yield return Leave();
            Check(_session.Phase == SessionPhase.Menu && SceneManager.GetActiveScene().name == _session.MenuScene, "leave: back in the main menu");
            Check(!NetworkRunner.Instances.Any(r => r != null && r.IsRunning), "leave: no runner left running");
            Check(VoyageLaunch.Active == null, "leave: voyage hand-off cleared");
        }

        private float _savedWind;

        private IEnumerator LoadOffline()
        {
            VoyageSave save = VoyageSaveStore.List().FirstOrDefault(s => s.Id == _saveId);
            if (!Check(save != null && save.Reached == 2, "load: saved voyage listed"))
                yield break;
            yield return Host(new HostRequest { Voyage = save, Offline = true });
            NetworkWorld world = null;
            yield return Until(() =>
            {
                world = NetworkWorld.Instance;
                return _session.Phase == SessionPhase.Sailing && world != null && world.IsReady &&
                       world.FindShip() != null && LocalPlayer() != null && world.MooredAt >= 0;
            }, 90f);
            if (!Check(world != null && world.IsReady, $"load: offline voyage started (notice '{_session.Notice}')"))
                yield break;
            Check(_session.Runner != null && _session.Runner.GameMode == GameMode.Single, "load: offline = Single mode, no lobby");
            Check(world.Seed == 424242 && Mathf.Abs(Mathf.DeltaAngle(world.WindDegrees, _savedWind)) < 0.01f,
                "load: same seed and wind → same world");
            Check(world.Reached == 2, "load: progress restored");
            WorldCheckpoint cp = world.Layout.Checkpoints[2];
            Vector3 ship = world.FindShip().transform.position;
            float d = Vector2.Distance(new Vector2(ship.x, ship.z), cp.Berth);
            Check(d < 5f, $"load: ship starts at the reached checkpoint's berth ({d:F1} m)");
            Check(world.MooredAt == 2, "load: moored at the reached checkpoint");
            yield return Leave();
            Check(_session.Phase == SessionPhase.Menu, "load: left back to menu");
        }

        // =========================================================================================
        private IEnumerator MultiHost()
        {
            GameSession.CodeOverride = LobbyCode.Normalize(_code);
            yield return Host(new HostRequest { LobbyName = "Smoke multi", Private = true, MaxCrew = 4, Voyage = VoyageSave.CreateNew("Multi", 777) });
            if (!Check(_session.Phase == SessionPhase.Lobby && _session.Code == GameSession.CodeOverride, $"host: lobby {_session.Code} up"))
                yield break;
            Log("host: waiting for a client in the lobby…");
            yield return Until(() => NetworkLobby.Instance != null && NetworkLobby.Instance.Count >= 2 && NetworkLobby.Instance.AllReady, 180f);
            NetworkLobby lobby = NetworkLobby.Instance;
            if (!Check(lobby != null && lobby.Count >= 2, "host: client joined the lobby room"))
                yield break;
            string names = string.Join(", ", lobby.Snapshot().Select(m => $"{m.Name}{(m.Ready ? "✓" : "")}"));
            Log($"host: crew {names}");
            Check(lobby.Snapshot().Any(m => !m.IsLocal && !string.IsNullOrEmpty(m.Name) && !m.Name.StartsWith("Матрос ")),
                "host: client name arrived via connection token");
            Check(lobby.AllReady, "host: client pressed ready");
            Check(_session.StartVoyage(), "host: sail");
            yield return Until(() => NetworkWorld.Instance != null && NetworkWorld.Instance.IsReady && NetworkPlayer.All.Count(p => p != null) >= 2, 90f);
            Check(NetworkPlayer.All.Count(p => p != null) >= 2, "host: both crew spawned in the world");
            if (_expectedClients >= 2)
            {
                Log("host: waiting for a late joiner…");
                var atSail = new HashSet<PlayerRef>(_session.Runner.ActivePlayers);
                bool LateSpawned() => NetworkPlayer.All.Any(p => p != null && p.Object != null && p.Object.IsValid && !atSail.Contains(p.Object.InputAuthority));
                yield return Until(LateSpawned, 180f);
                Check(LateSpawned(), "host: late joiner spawned mid-voyage");
            }
            Log("host: waiting for clients to leave…");
            yield return Until(() => _session.Runner != null && _session.Runner.ActivePlayers.Count() == 1, 180f);
            Check(_session.Runner != null && _session.Runner.ActivePlayers.Count() == 1, "host: clients left cleanly");
            yield return Leave();
        }

        private IEnumerator MultiClient()
        {
            string code = LobbyCode.Normalize(_code);
            float deadline = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _session.JoinAsync(code).Forget();
                yield return Until(() => _session.Phase != SessionPhase.Connecting && _session.Phase != SessionPhase.Menu, 40f);
                if (_session.Phase == SessionPhase.Lobby || _session.Phase == SessionPhase.Sailing)
                    break;
                Log($"client: join failed ('{_session.Notice}'), retrying…");
                yield return Wait(3f);
            }
            if (!Check(_session.Phase == SessionPhase.Lobby || _session.Phase == SessionPhase.Sailing, $"client: joined {code}"))
                yield break;

            bool late = _session.Phase == SessionPhase.Sailing;
            if (!late)
            {
                yield return Until(() => NetworkLobby.Instance != null && NetworkLobby.Instance.Count >= 2, 15f);
                NetworkLobby lobby = NetworkLobby.Instance;
                Check(lobby != null && lobby.Snapshot().Any(m => m.IsCaptain && !m.IsLocal), "client: sees the captain in the room");
                Check(lobby != null && lobby.Snapshot().Any(m => m.IsLocal && m.Name == PlayerProfile.Name), "client: listed under its own name");
                lobby?.SetLocalReady(true);
                yield return Until(() => NetworkLobby.Instance != null && NetworkLobby.Instance.LocalReady, 10f);
                Check(NetworkLobby.Instance != null && NetworkLobby.Instance.LocalReady, "client: ready flag replicated");
            }

            NetworkWorld world = null;
            yield return Until(() =>
            {
                world = NetworkWorld.Instance;
                return _session.Phase == SessionPhase.Sailing && world != null && world.IsReady && LocalPlayer() != null;
            }, 180f);
            Check(world != null && world.IsReady && LocalPlayer() != null,
                late ? "client: late join straight into the voyage, own pirate spawned" : "client: followed the captain to sea");
            Check(world != null && !world.ChecksumMismatch, "client: identical world (checksum)");
            yield return Wait(8f);
            yield return Leave();
            Check(_session.Phase == SessionPhase.Menu, "client: left back to menu");
        }

        // =========================================================================================
        private IEnumerator Host(HostRequest request)
        {
            _session.HostAsync(request).Forget();
            yield return Until(() => _session.Phase != SessionPhase.Connecting && _session.Phase != SessionPhase.Menu, 60f);
            if (_session.Phase == SessionPhase.Menu)
                Log($"host failed: {_session.Notice}");
        }

        private IEnumerator Leave()
        {
            _session.Leave();
            yield return Until(() => _session.Phase == SessionPhase.Menu && SceneManager.GetActiveScene().name == _session.MenuScene, 30f);
            yield return Wait(0.5f);
        }

        private static NetworkPlayer LocalPlayer() =>
            NetworkPlayer.All.FirstOrDefault(p => p != null && p.Object != null && p.Object.IsValid && p.HasInputAuthority);

        private static IEnumerator Until(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool done;
                try { done = condition(); }
                catch (Exception) { done = false; }
                if (done)
                    yield break;
                yield return null;
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private IEnumerator Run(IEnumerator step)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(step);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                object current;
                try
                {
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = top.Current;
                }
                catch (Exception e)
                {
                    Fail($"exception: {e}");
                    yield break;
                }
                if (current is IEnumerator nested)
                    stack.Push(nested);
                else
                    yield return current;
            }
        }

        private bool Check(bool ok, string what)
        {
            Log((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures.Add(what);
            return ok;
        }

        private void Fail(string what) => Check(false, what);
        private void Log(string line)
        {
            _report.AppendLine(line);
            Debug.Log("[MenuSmoke] " + line);
        }

        private void Finish()
        {
            _report.AppendLine(_failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {_failures.Count} FAILED");
            Debug.Log("[MenuSmoke]\n" + _report);
            if (VoyageSaveStore.DirectoryOverride != null && Directory.Exists(VoyageSaveStore.DirectoryOverride))
                Directory.Delete(VoyageSaveStore.DirectoryOverride, true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_failures.Count == 0 ? 0 : 1);
#else
            Application.Quit(_failures.Count == 0 ? 0 : 1);
#endif
        }
    }
}
#endif
