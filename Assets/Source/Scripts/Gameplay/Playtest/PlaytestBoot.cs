#if FUSION2
using System;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using RumOverboard.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RumOverboard.Gameplay.Playtest
{
    /// <summary>
    /// Playtest scene bootstrap + F1 overlay (help, network status, reconnect).
    ///
    /// Before the ConnectionManager starts (Awake runs before every Start) it picks how to connect:
    ///   command line  -host | -client | -auto   (default: auto = first instance hosts, the next ones join)
    ///                 -session &lt;name&gt;          (default: <see cref="DefaultSession"/>)
    ///                 -region &lt;code&gt;           (builds default to <see cref="DefaultRegion"/>, so every instance
    ///                                          meets in the same Photon region; "best" = Photon picks)
    /// "Reconnect as …" in the overlay shuts the session down and reloads the scene in the chosen mode.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class PlaytestBoot : MonoBehaviour
    {
        public const string DefaultSession = "rum-playtest";
        public const string DefaultRegion = "eu";

        [SerializeField] private bool helpVisible;
        [SerializeField] private Rect windowRect = new Rect(0f, 0f, 560f, 620f);

        private const int WindowId = 0x0F1A1E57;
        private static GameMode? _pendingMode; // survives the scene reload of "Reconnect as …"
        private static string _pendingSession;

        private ConnectionManager _cm;
        private GameMode _mode = GameMode.AutoHostOrClient;
        private string _session = DefaultSession;
        private string _region = "";
        private float _bannerUntil;
        private Vector2 _scroll;
        private bool _reconnecting;

        public static bool ArgPresent(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }

        private void Awake()
        {
            _cm = FindAnyObjectByType<ConnectionManager>();
            if (ArgPresent("-host")) _mode = GameMode.Host;
            else if (ArgPresent("-client")) _mode = GameMode.Client;
            _session = ArgValue("-session") ?? DefaultSession;
            if (_pendingMode.HasValue) { _mode = _pendingMode.Value; _pendingMode = null; }
            if (_pendingSession != null) { _session = _pendingSession; _pendingSession = null; }

            // A fixed region in builds: two instances picking different "best" regions never meet.
            // Not in the editor — it would dirty the PhotonAppSettings asset.
            _region = ArgValue("-region") ?? (Application.isEditor ? "" : DefaultRegion);
            if (_region == "best") _region = "";
            if (!Application.isEditor && PhotonAppSettings.Global != null)
                PhotonAppSettings.Global.AppSettings.FixedRegion = _region;

            Application.runInBackground = true; // a second window on the same PC keeps simulating
            if (_cm != null) _cm.OverrideAutoStart(_mode, _session);
            _bannerUntil = Time.realtimeSinceStartup + 20f;
        }

        private float _nextClientRetry;

        private void Update()
        {
            // "-client" doesn't fall back to offline: keep trying until the host's session is up.
            if (_mode == GameMode.Client && !_reconnecting && _cm != null && _cm.Runner == null &&
                Time.realtimeSinceStartup > _nextClientRetry && Time.realtimeSinceStartup > 3f)
            {
                _nextClientRetry = Time.realtimeSinceStartup + 4f;
                _cm.StartClient(_session).Forget();
            }

            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                helpVisible = !helpVisible;
                Cursor.lockState = helpVisible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = helpVisible;
            }
        }

        private void OnGUI()
        {
            if (!helpVisible)
            {
                // Below the HUD's session / crew lines (top-left), centred, wide enough not to wrap.
                GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.UpperCenter };
                bool banner = Time.realtimeSinceStartup < _bannerUntil;
                float w = Mathf.Min(Screen.width - 20f, 760f);
                GUI.Label(new Rect(Screen.width * 0.5f - w * 0.5f, 48f, w, 44f),
                          (banner ? "<b>PLAYTEST</b> — <b>F1</b>: управление, механики, сеть\n" : "F1 — справка / сеть   ") + NetLine(), style);
                return;
            }
            windowRect.x = Screen.width * 0.5f - windowRect.width * 0.5f;
            windowRect.y = Mathf.Max(10f, Screen.height * 0.5f - windowRect.height * 0.5f);
            GUI.Window(WindowId, windowRect, Draw, "Playtest — F1");
        }

        private string NetLine()
        {
            NetworkRunner r = _cm != null ? _cm.Runner : null;
            if (r == null || !r.IsRunning) return _reconnecting ? "сеть: переподключение…" : $"сеть: подключение ({_mode}, '{_session}')…";
            string role = r.GameMode == GameMode.Single ? "<color=#ff7755>ОФЛАЙН (Photon недоступен)</color>"
                        : r.IsServer ? "ХОСТ" : "КЛИЕНТ";
            int crew = 0;
            foreach (PlayerRef _ in r.ActivePlayers) crew++;
            string region = r.SessionInfo != null && r.SessionInfo.Region != null ? r.SessionInfo.Region : (_region == "" ? "best" : _region);
            return $"сеть: {role} · сессия '{r.SessionInfo?.Name}' · регион {region} · игроков {crew}";
        }

        private void Draw(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("<b>Сеть</b>  " + NetLine());
            GUILayout.Label("Первый запущенный экземпляр — хост, следующие подключаются к нему (та же сессия и регион). " +
                            "Запускать второй, когда первый уже в игре.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Переподключиться: авто")) Reconnect(GameMode.AutoHostOrClient);
            if (GUILayout.Button("…как хост")) Reconnect(GameMode.Host);
            if (GUILayout.Button("…как клиент")) Reconnect(GameMode.Client);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("<b>Управление</b>");
            GUILayout.Label(
                "WASD — ходьба · Shift — бег (стамина) · Space — прыжок · мышь — взгляд\n" +
                "E — взаимодействие: ванты/мачта (лазать), штурвал, канаты, нагели, предметы, сундук, бочка с водой\n" +
                "ЛКМ — выбирать канат / бросить предмет · ПКМ — травить канат · G — положить предмет / отпустить канат\n" +
                "Q — использовать предмет: бутылка — глоток рома, подзорная труба — смотреть, ведро — выплеснуть воду\n" +
                "R — ragdoll (упасть)");

            GUILayout.Space(6f);
            GUILayout.Label("<b>Что проверить</b>");
            GUILayout.Label(
                "• Корабль: лазание по вантам, штурвал (руль), канаты/реи, брызги и пена у корпуса\n" +
                "• Стамина: бег, прыжки, лазание, плавание, тяга каната — полоска внизу, истощение\n" +
                "• Ром: бутылки на палубе — Q пьёт, раскачка походки и камеры, ragdoll\n" +
                "• Предметы: взять/нести/бросить, тяжёлое замедляет, сундук на корме (E — положить/достать),\n" +
                "  удар ломает, предметы в воде плавают\n" +
                "• Огонь: F9 → Ignite на куче у носа; огонь переходит на соседей; ведро (Q) тушит и мочит;\n" +
                "  E на бочку с водой — набрать; ведро в море набирается само; пороховая бочка взрывается;\n" +
                "  разбитый фонарь поджигает всё рядом\n" +
                "• Мультиплеер: всё выше видно на другом экземпляре; решает хост, клиенты только показывают");

            GUILayout.Space(6f);
            GUILayout.Label("<b>Окна отладки</b> (клавиша — открыть/закрыть, освобождает курсор)");
            GUILayout.Label(
                "F2 такелаж · F3 вода/волны · F4 штурвал · F5 ром и ragdoll · F6 атрибуты сущностей\n" +
                "F7 стамина · F8 предметы (спавн, выдать в руки, сломать) · F9 огонь (поджечь, потушить, взрыв)\n" +
                "Настройки в окнах меняет только хост (на клиенте они только для чтения).");

            GUILayout.Space(6f);
            GUILayout.Label("<b>Командная строка</b>: -host | -client | -auto, -session имя, -region eu|ru|us|asia|best,\n" +
                            "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 (оконный режим)");
            GUILayout.EndScrollView();
        }

        private async void Reconnect(GameMode mode)
        {
            if (_reconnecting) return;
            _reconnecting = true;
            _pendingMode = mode;
            _pendingSession = _session;
            if (_cm != null) await _cm.Disconnect();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
        }
    }
}
#endif
