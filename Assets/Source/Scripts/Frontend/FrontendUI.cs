#if FUSION2
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using RumOverboard.Gameplay;
using RumOverboard.Gameplay.World.Saves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RumOverboard.Frontend
{
    /// <summary>
    /// Runtime-built frontend UI living next to <see cref="GameSession"/> (DontDestroyOnLoad):
    /// main menu, new / load voyage, lobby browser + join by code, lobby room, connecting overlay
    /// and the in-voyage pause menu (Esc).
    /// </summary>
    [RequireComponent(typeof(GameSession))]
    public sealed class FrontendUI : MonoBehaviour
    {
        public enum MenuScreen
        {
            Main,
            Host,
            Load,
            Join,
        }

        private GameSession _session;
        private Canvas _canvas;
        private Image _backdrop;
        private EventSystem _eventSystem;
        private RectTransform _main, _host, _load, _join, _lobby, _busy, _pause;
        private MenuScreen _screen = MenuScreen.Main;
        private SessionPhase _lastPhase = SessionPhase.Menu;
        private bool _pauseOpen;

        // Main
        private Text _notice;
        private Button _offlineButton;
        private InputField _nameField;
        private Text _footer;

        // Host setup
        private Text _hostTitle, _hostVoyage, _crewLabel;
        private GameObject _voyageNameRow, _seedRow;
        private InputField _voyageName, _seedField, _lobbyName;
        private Button _visibilityButton;
        private int _crew;
        private bool _private;
        private VoyageSave _loadSave;
        private HostRequest _lastRequest;

        // Load
        private RectTransform _saveList;
        private string _confirmDeleteId;

        // Join
        private InputField _codeField;
        private Text _joinStatus;
        private RectTransform _lobbyList;

        // Lobby room
        private Text _roomTitle, _roomCode, _roomVoyage, _roomCrew, _roomHint;
        private RectTransform _memberList;
        private Button _readyButton, _sailButton, _copyButton;
        private int _memberHash = int.MinValue;

        // Busy / pause
        private Text _busyText;
        private Text _pauseCode, _pauseInfo;
        private Button _pauseCopy, _pauseLeave;

        public MenuScreen CurrentScreen => _screen;
        public bool PauseOpen => _pauseOpen;

        // =========================================================================================
        // Lifecycle
        // =========================================================================================
        private void Awake()
        {
            _session = GetComponent<GameSession>();
            Build();
        }

        private void OnEnable()
        {
            if (_session == null)
                _session = GetComponent<GameSession>();
            _session.Changed += Refresh;
            _session.Directory.Changed += OnDirectoryChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureEventSystem();
            Refresh();
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.Changed -= Refresh;
                if (_session.Directory != null)
                    _session.Directory.Changed -= OnDirectoryChanged;
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureEventSystem();
            Refresh();
        }

        private void EnsureEventSystem()
        {
            if (_eventSystem == null)
            {
                var go = new GameObject("Frontend EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                go.transform.SetParent(transform, false);
                _eventSystem = go.GetComponent<EventSystem>();
            }
            bool other = false;
            foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude))
                if (es != _eventSystem)
                    other = true;
            _eventSystem.gameObject.SetActive(!other);
        }

        private void Update()
        {
            SessionPhase phase = _session.Phase;
            Keyboard kb = Keyboard.current;
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;

            if (phase == SessionPhase.Sailing)
            {
                if (esc)
                {
                    if (_pauseOpen)
                        SetPause(false);
                    else if (!UiInputCapture.BusyThisFrame)
                        SetPause(true);
                }
                if (_pauseOpen)
                    UpdatePause();
                return;
            }

            if (phase == SessionPhase.Menu && esc && _screen != MenuScreen.Main && !UiKit.IsTyping())
                ShowMenu(MenuScreen.Main);
            if (phase == SessionPhase.Lobby)
                UpdateLobbyRoom();
            if (phase == SessionPhase.Connecting || phase == SessionPhase.Leaving)
                _busyText.text = _session.Status + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // =========================================================================================
        // Navigation
        // =========================================================================================
        public void ShowMenu(MenuScreen screen)
        {
            if (_screen == MenuScreen.Join && screen != MenuScreen.Join && _session.Phase == SessionPhase.Menu)
                _session.Directory.StopBrowsingAsync().Forget();
            _screen = screen;
            if (screen == MenuScreen.Load)
                RebuildSaves();
            if (screen == MenuScreen.Join)
            {
                RebuildLobbyList();
                _session.Directory.StartBrowsingAsync().Forget();
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_main == null)
                return;
            SessionPhase phase = _session.Phase;
            if (phase == SessionPhase.Menu && _lastPhase != SessionPhase.Menu)
                _screen = MenuScreen.Main; // back from a session (or a failed start): show the notice
            if (phase != SessionPhase.Lobby)
                _memberHash = int.MinValue;
            _lastPhase = phase;

            bool menu = phase == SessionPhase.Menu;
            _main.gameObject.SetActive(menu && _screen == MenuScreen.Main);
            _host.gameObject.SetActive(menu && _screen == MenuScreen.Host);
            _load.gameObject.SetActive(menu && _screen == MenuScreen.Load);
            _join.gameObject.SetActive(menu && _screen == MenuScreen.Join);
            _lobby.gameObject.SetActive(phase == SessionPhase.Lobby);
            _busy.gameObject.SetActive(phase == SessionPhase.Connecting || phase == SessionPhase.Leaving);

            if (phase != SessionPhase.Sailing && _pauseOpen)
                SetPause(false);
            _pause.gameObject.SetActive(phase == SessionPhase.Sailing && _pauseOpen);
            _backdrop.gameObject.SetActive(phase != SessionPhase.Sailing);

            if (menu)
            {
                bool hasNotice = !string.IsNullOrEmpty(_session.Notice);
                _notice.gameObject.SetActive(hasNotice);
                _notice.text = _session.Notice;
                _offlineButton.gameObject.SetActive(hasNotice && _session.OfferOffline && _lastRequest != null);
                _nameField.SetTextWithoutNotify(PlayerProfile.Name);
            }
            if (phase == SessionPhase.Lobby)
                UpdateLobbyRoom(force: true);
        }

        private void OnDirectoryChanged()
        {
            if (_session.Phase == SessionPhase.Menu && _screen == MenuScreen.Join)
                RebuildLobbyList();
        }

        // =========================================================================================
        // Build
        // =========================================================================================
        private void Build()
        {
            _canvas = UiKit.CreateCanvas(transform, "Frontend Canvas", 500);
            _backdrop = UiKit.Panel(_canvas.transform, UiKit.Backdrop, "Backdrop");
            UiKit.Stretch(_backdrop.rectTransform);

            _main = MakeScreen("Main");
            BuildMain(UiKit.CardPanel(_main, 760f));
            _host = MakeScreen("Host");
            BuildHost(UiKit.CardPanel(_host, 860f));
            _load = MakeScreen("Load");
            BuildLoad(UiKit.CardPanel(_load, 980f));
            _join = MakeScreen("Join");
            BuildJoin(UiKit.CardPanel(_join, 1040f));
            _lobby = MakeScreen("Lobby");
            BuildLobbyRoom(UiKit.CardPanel(_lobby, 900f));
            _busy = MakeScreen("Busy");
            BuildBusy(UiKit.CardPanel(_busy, 640f));
            _pause = MakeScreen("Pause");
            Image dim = UiKit.Panel(_pause, UiKit.Dim, "Dim");
            UiKit.Stretch(dim.rectTransform);
            BuildPause(UiKit.CardPanel(_pause, 640f));
        }

        private RectTransform MakeScreen(string name)
        {
            RectTransform rt = UiKit.Stretch(UiKit.Rect(name, _canvas.transform));
            rt.gameObject.SetActive(false);
            return rt;
        }

        private static void Title(Transform card, string text, string subtitle = null)
        {
            Text t = UiKit.Label(card, text, 44, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(t, -1f, 60f);
            if (!string.IsNullOrEmpty(subtitle))
            {
                Text s = UiKit.Label(card, subtitle, 22, UiKit.Muted, TextAnchor.MiddleCenter);
                UiKit.Size(s, -1f, 30f);
            }
            UiKit.Spacer(card, 6f);
        }

        private void BuildMain(RectTransform card)
        {
            Text title = UiKit.Label(card, "RUM OVERBOARD", 68, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(title, -1f, 92f);
            Text sub = UiKit.Label(card, "кооперативный пиратский рейс", 24, UiKit.Muted, TextAnchor.MiddleCenter);
            UiKit.Size(sub, -1f, 32f);
            UiKit.Spacer(card, 10f);

            _notice = UiKit.Label(card, string.Empty, 24, UiKit.Danger, TextAnchor.MiddleCenter);
            _offlineButton = UiKit.Button(card, "Играть оффлайн (одиночный рейс)", () =>
            {
                if (_lastRequest == null)
                    return;
                _lastRequest.Offline = true;
                Host(_lastRequest);
            }, UiKit.ButtonColor, 50f);

            UiKit.Button(card, "Новый рейс", () => OpenHostNew(), UiKit.PrimaryColor, 66f, -1f, 30);
            UiKit.Button(card, "Загрузить рейс", () => ShowMenu(MenuScreen.Load), null, 62f);
            UiKit.Button(card, "Присоединиться", () => ShowMenu(MenuScreen.Join), null, 62f);

            UiKit.Spacer(card, 8f);
            RectTransform nameRow = UiKit.HStack(card, 12f, 54f);
            Text nameLabel = UiKit.Label(nameRow, "Ваше имя:", 24, UiKit.Muted);
            UiKit.Size(nameLabel, 150f);
            _nameField = UiKit.Input(nameRow, "Имя в команде", PlayerProfile.Name, PlayerProfile.MaxNameLength);
            _nameField.onEndEdit.AddListener(v =>
            {
                PlayerProfile.Name = v;
                _nameField.SetTextWithoutNotify(PlayerProfile.Name);
            });

            UiKit.Spacer(card, 8f);
            UiKit.Button(card, "Выход", Quit, UiKit.DangerButton, 52f);
            _footer = UiKit.Label(card, $"v{Application.version} · сеть: {_session.Directory.DisplayName}", 18,
                UiKit.Muted, TextAnchor.MiddleCenter);
            UiKit.Size(_footer, -1f, 26f);
        }

        private void BuildHost(RectTransform card)
        {
            _hostTitle = UiKit.Label(card, "Новый рейс", 44, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(_hostTitle, -1f, 60f);
            _hostVoyage = UiKit.Label(card, string.Empty, 24, UiKit.TextColor, TextAnchor.MiddleCenter);

            _voyageNameRow = FieldRow(card, "Название рейса", out _voyageName, "Рейс", 32);
            _seedRow = FieldRow(card, "Сид мира", out _seedField, "пусто — случайный", 10);
            _seedField.contentType = InputField.ContentType.IntegerNumber;
            FieldRow(card, "Название лобби", out _lobbyName, "Команда …", 32);

            RectTransform crewRow = UiKit.HStack(card, 12f, 54f);
            Text crewTitle = UiKit.Label(crewRow, "Экипаж (макс.)", 24, UiKit.Muted);
            UiKit.Size(crewTitle, 240f);
            UiKit.Button(crewRow, "–", () => SetCrew(_crew - 1), null, 54f, 64f, 32);
            _crewLabel = UiKit.Label(crewRow, "4", 30, UiKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(_crewLabel, 80f);
            UiKit.Button(crewRow, "+", () => SetCrew(_crew + 1), null, 54f, 64f, 32);

            RectTransform visRow = UiKit.HStack(card, 12f, 54f);
            Text visTitle = UiKit.Label(visRow, "Видимость", 24, UiKit.Muted);
            UiKit.Size(visTitle, 240f);
            _visibilityButton = UiKit.Button(visRow, "Открытое лобби", () => SetPrivate(!_private), null, 54f);

            UiKit.Spacer(card, 8f);
            UiKit.Button(card, "Создать лобби", () => Host(BuildRequest(offline: false)), UiKit.PrimaryColor, 64f, -1f, 30);
            UiKit.Button(card, "Одиночный рейс (оффлайн)", () => Host(BuildRequest(offline: true)), null, 54f);
            UiKit.Button(card, "Назад", () => ShowMenu(MenuScreen.Main), null, 50f);
        }

        private static GameObject FieldRow(Transform card, string label, out InputField field, string placeholder, int max)
        {
            RectTransform row = UiKit.HStack(card, 12f, 54f);
            Text t = UiKit.Label(row, label, 24, UiKit.Muted);
            UiKit.Size(t, 240f);
            field = UiKit.Input(row, placeholder, string.Empty, max);
            return row.gameObject;
        }

        private void BuildLoad(RectTransform card)
        {
            Title(card, "Сохранённые рейсы", "Капитан хранит рейс у себя: продолжить его можно только с этого компьютера.");
            UiKit.ScrollList(card, 560f, out _saveList);
            UiKit.Button(card, "Назад", () => ShowMenu(MenuScreen.Main), null, 52f);
        }

        private void BuildJoin(RectTransform card)
        {
            Title(card, "Присоединиться", "Войдите по коду от капитана или выберите открытое лобби.");
            RectTransform codeRow = UiKit.HStack(card, 12f, 62f);
            _codeField = UiKit.Input(codeRow, "КОД ЛОББИ", string.Empty, 12, 62f);
            _codeField.characterValidation = InputField.CharacterValidation.Alphanumeric;
            _codeField.onEndEdit.AddListener(v =>
            {
                if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                    Join(v);
            });
            UiKit.Button(codeRow, "Войти по коду", () => Join(_codeField.text), UiKit.PrimaryColor, 62f, 260f);

            _joinStatus = UiKit.Label(card, string.Empty, 22, UiKit.Muted, TextAnchor.MiddleCenter);
            UiKit.Size(_joinStatus, -1f, 30f);
            UiKit.ScrollList(card, 460f, out _lobbyList);

            RectTransform buttons = UiKit.HStack(card, 12f, 52f);
            UiKit.Button(buttons, "Обновить", () => RestartBrowsing().Forget(), null, 52f);
            UiKit.Button(buttons, "Назад", () => ShowMenu(MenuScreen.Main), null, 52f);
        }

        private void BuildLobbyRoom(RectTransform card)
        {
            _roomTitle = UiKit.Label(card, "Лобби", 44, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(_roomTitle, -1f, 60f);

            RectTransform codeRow = UiKit.HStack(card, 16f, 60f);
            codeRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _roomCode = UiKit.Label(codeRow, "Код: ——", 40, UiKit.TextColor, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Size(_roomCode, -1f, -1f, 1f);
            _copyButton = UiKit.Button(codeRow, "Копировать", () => CopyCode(_copyButton), null, 54f, 220f, 22);

            _roomVoyage = UiKit.Label(card, string.Empty, 24, UiKit.Muted, TextAnchor.MiddleCenter);
            _roomCrew = UiKit.Label(card, string.Empty, 24, UiKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.ScrollList(card, 300f, out _memberList);
            _roomHint = UiKit.Label(card, string.Empty, 20, UiKit.Muted, TextAnchor.MiddleCenter);

            _readyButton = UiKit.Button(card, "Готов!", ToggleReady, UiKit.PrimaryColor, 62f, -1f, 30);
            _sailButton = UiKit.Button(card, "Отплыть", () => _session.StartVoyage(), UiKit.PrimaryColor, 62f, -1f, 30);
            UiKit.Button(card, "Покинуть лобби", () => _session.Leave(), UiKit.DangerButton, 52f);
        }

        private void BuildBusy(RectTransform card)
        {
            _busyText = UiKit.Label(card, "Подключение…", 32, UiKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(_busyText, -1f, 120f);
        }

        private void BuildPause(RectTransform card)
        {
            Title(card, "Пауза", "Игра не останавливается — команда в море.");
            RectTransform codeRow = UiKit.HStack(card, 12f, 54f);
            _pauseCode = UiKit.Label(codeRow, string.Empty, 28, UiKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Size(_pauseCode, -1f, -1f, 1f);
            _pauseCopy = UiKit.Button(codeRow, "Копировать", () => CopyCode(_pauseCopy), null, 50f, 200f, 22);
            _pauseInfo = UiKit.Label(card, string.Empty, 22, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Spacer(card, 6f);
            UiKit.Button(card, "Продолжить", () => SetPause(false), UiKit.PrimaryColor, 62f, -1f, 30);
            _pauseLeave = UiKit.Button(card, "Покинуть рейс", () => _session.Leave(), UiKit.DangerButton, 56f);
            UiKit.Button(card, "Выйти из игры", Quit, null, 50f);
        }

        // =========================================================================================
        // Host setup
        // =========================================================================================
        private void OpenHostNew()
        {
            _session.Notice = string.Empty;
            _loadSave = null;
            _hostTitle.text = "Новый рейс";
            _hostVoyage.gameObject.SetActive(false);
            _voyageNameRow.SetActive(true);
            _seedRow.SetActive(true);
            _voyageName.text = $"Рейс {DateTime.Now:dd.MM HH:mm}";
            _seedField.text = string.Empty;
            PrepareLobbyFields();
            ShowMenu(MenuScreen.Host);
        }

        private void OpenHostLoad(VoyageSave save)
        {
            _session.Notice = string.Empty;
            _loadSave = save;
            _hostTitle.text = "Продолжить рейс";
            _hostVoyage.gameObject.SetActive(true);
            _hostVoyage.text = $"{save.Name} — {save.ProgressText}";
            _voyageNameRow.SetActive(false);
            _seedRow.SetActive(false);
            PrepareLobbyFields();
            ShowMenu(MenuScreen.Host);
        }

        private void PrepareLobbyFields()
        {
            _lobbyName.text = $"Команда {PlayerProfile.Name}";
            SetCrew(_session.MaxCrew);
            SetPrivate(false);
        }

        private void SetCrew(int crew)
        {
            _crew = Mathf.Clamp(crew, 1, Mathf.Max(1, _session.MaxCrew));
            _crewLabel.text = _crew.ToString();
        }

        private void SetPrivate(bool value)
        {
            _private = value;
            UiKit.SetLabel(_visibilityButton, value ? "Только по коду (скрыто)" : "Открытое лобби (в списке)");
        }

        private HostRequest BuildRequest(bool offline)
        {
            VoyageSave voyage = _loadSave;
            if (voyage == null)
            {
                int seed = 0;
                if (!string.IsNullOrWhiteSpace(_seedField.text) && int.TryParse(_seedField.text.Trim(), out int parsed))
                    seed = Mathf.Abs(parsed);
                if (seed == 0)
                    seed = UnityEngine.Random.Range(1, int.MaxValue);
                voyage = VoyageSave.CreateNew(_voyageName.text, seed);
            }
            return new HostRequest
            {
                LobbyName = _lobbyName.text,
                Private = _private,
                MaxCrew = _crew,
                Voyage = voyage,
                Offline = offline,
            };
        }

        private void Host(HostRequest request)
        {
            _lastRequest = request;
            _session.OfferOffline = false;
            _session.HostAsync(request).Forget();
        }

        // =========================================================================================
        // Load
        // =========================================================================================
        private void RebuildSaves()
        {
            UiKit.Clear(_saveList);
            List<VoyageSave> saves = VoyageSaveStore.List();
            if (saves.Count == 0)
            {
                Text empty = UiKit.Label(_saveList, "Сохранённых рейсов пока нет — начните новый.", 24, UiKit.Muted, TextAnchor.MiddleCenter);
                UiKit.Size(empty, -1f, 80f);
                return;
            }
            foreach (VoyageSave save in saves)
            {
                VoyageSave s = save;
                RectTransform row = UiKit.HStack(_saveList, 12f, 76f, "Save " + s.Id);
                row.gameObject.AddComponent<Image>().color = UiKit.Row;
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 10, 8, 8);
                Text info = UiKit.Label(row,
                    $"<b>{Escape(s.Name)}</b>\n<color=#9AA6B5><size=20>{s.ProgressText} · {s.Updated:dd.MM.yyyy HH:mm} · сид {s.Seed}</size></color>", 26);
                UiKit.Size(info, -1f, -1f, 1f);
                UiKit.Button(row, "Продолжить", () => OpenHostLoad(s), UiKit.PrimaryColor, 56f, 200f, 22);
                UiKit.Button(row, _confirmDeleteId == s.Id ? "Точно?" : "Удалить", () =>
                {
                    if (_confirmDeleteId == s.Id)
                    {
                        VoyageSaveStore.Delete(s.Id);
                        _confirmDeleteId = null;
                    }
                    else
                    {
                        _confirmDeleteId = s.Id;
                    }
                    RebuildSaves();
                }, UiKit.DangerButton, 56f, 140f, 22);
            }
        }

        // =========================================================================================
        // Join
        // =========================================================================================
        private void Join(string code)
        {
            _session.Notice = string.Empty;
            if (LobbyCode.Normalize(code) == null)
            {
                _joinStatus.text = $"<color=#F27366>Код — {LobbyCode.Length} символов (A–Z без I/O, цифры 2–9).</color>";
                return;
            }
            _session.JoinAsync(code).Forget();
        }

        private async UniTaskVoid RestartBrowsing()
        {
            await _session.Directory.StopBrowsingAsync();
            RebuildLobbyList();
            if (_session.Phase == SessionPhase.Menu && _screen == MenuScreen.Join)
                await _session.Directory.StartBrowsingAsync();
        }

        private void RebuildLobbyList()
        {
            ILobbyDirectory dir = _session.Directory;
            _joinStatus.text = dir.Status;
            UiKit.Clear(_lobbyList);
            foreach (LobbyListing listing in dir.Listings)
            {
                LobbyListing l = listing;
                RectTransform row = UiKit.HStack(_lobbyList, 12f, 76f, "Lobby " + l.Code);
                row.gameObject.AddComponent<Image>().color = UiKit.Row;
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 10, 8, 8);
                string state = l.Sailing ? "в плавании" : "собирает команду";
                Text info = UiKit.Label(row,
                    $"<b>{Escape(l.Name)}</b>\n<color=#9AA6B5><size=20>Капитан {Escape(l.Host)} · {Escape(l.Voyage)} · {state}</size></color>", 26);
                UiKit.Size(info, -1f, -1f, 1f);
                Text count = UiKit.Label(row, $"{l.Players}/{l.MaxPlayers}", 26, UiKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Size(count, 90f);
                string label = !l.VersionMatches ? "Версия" : l.Players >= l.MaxPlayers ? "Полно" : !l.Open ? "Закрыто" : "Войти";
                Button join = UiKit.Button(row, label, () => _session.JoinAsync(l.Code).Forget(), UiKit.PrimaryColor, 56f, 160f, 22);
                join.interactable = l.Joinable;
            }
        }

        // =========================================================================================
        // Lobby room
        // =========================================================================================
        private void UpdateLobbyRoom(bool force = false)
        {
            NetworkLobby lobby = NetworkLobby.Instance;
            NetworkRunner runner = _session.Runner;
            bool host = runner != null && runner.IsServer;
            LobbyAdvert advert = _session.Advert;

            int hash = lobby != null ? lobby.StateHash() : 0;
            if (!force && hash == _memberHash)
                return;
            _memberHash = hash;

            _roomTitle.text = string.IsNullOrEmpty(advert.Name) ? "Лобби" : advert.Name;
            _roomCode.text = $"Код: <color=#EEB84D>{_session.Code}</color>";
            _roomVoyage.text = $"{advert.Voyage}{(advert.Private ? " · только по коду" : string.Empty)}";

            int max = runner != null && runner.SessionInfo != null ? runner.SessionInfo.MaxPlayers : _session.MaxCrew;
            _roomCrew.text = $"Экипаж {(lobby != null ? lobby.Count : 0)}/{max}";

            UiKit.Clear(_memberList);
            if (lobby != null)
            {
                foreach (NetworkLobby.MemberView m in lobby.Snapshot())
                {
                    RectTransform row = UiKit.HStack(_memberList, 12f, 56f, "Member");
                    row.gameObject.AddComponent<Image>().color = UiKit.Row;
                    row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(16, 16, 4, 4);
                    string tags = (m.IsCaptain ? " <color=#EEB84D>(капитан)</color>" : string.Empty) +
                                  (m.IsLocal ? " <color=#9AA6B5>(вы)</color>" : string.Empty);
                    Text name = UiKit.Label(row, Escape(m.Name) + tags, 26);
                    UiKit.Size(name, -1f, -1f, 1f);
                    string status = m.IsCaptain ? "на мостике" : m.Ready ? "готов" : "не готов";
                    Text st = UiKit.Label(row, status, 24, m.IsCaptain || m.Ready ? UiKit.Good : UiKit.Muted, TextAnchor.MiddleRight);
                    UiKit.Size(st, 200f);
                }
            }
            else
            {
                Text wait = UiKit.Label(_memberList, "Загрузка лобби…", 24, UiKit.Muted, TextAnchor.MiddleCenter);
                UiKit.Size(wait, -1f, 60f);
            }

            bool allReady = lobby != null && lobby.AllReady;
            _readyButton.gameObject.SetActive(!host);
            _sailButton.gameObject.SetActive(host);
            _sailButton.interactable = allReady;
            UiKit.SetLabel(_readyButton, lobby != null && lobby.LocalReady ? "Не готов" : "Готов!");
            _roomHint.text = host
                ? allReady ? "Вся команда готова. Друзья смогут подключиться по коду и после отплытия."
                           : "Ждём готовности команды…"
                : "Капитан отплывёт, когда все будут готовы.";
        }

        private void ToggleReady()
        {
            NetworkLobby lobby = NetworkLobby.Instance;
            if (lobby != null)
                lobby.SetLocalReady(!lobby.LocalReady);
        }

        private void CopyCode(Button source)
        {
            if (string.IsNullOrEmpty(_session.Code))
                return;
            GUIUtility.systemCopyBuffer = _session.Code;
            UiKit.SetLabel(source, "Скопировано");
            ResetLabelLater(source).Forget();
        }

        private static async UniTaskVoid ResetLabelLater(Button button)
        {
            await UniTask.Delay(1500, ignoreTimeScale: true);
            if (button != null)
                UiKit.SetLabel(button, "Копировать");
        }

        // =========================================================================================
        // Pause
        // =========================================================================================
        public void SetPause(bool open)
        {
            if (open == _pauseOpen)
                return;
            _pauseOpen = open;
            UiInputCapture.Active = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open)
                UpdatePause();
            if (_pause != null)
                _pause.gameObject.SetActive(open && _session.Phase == SessionPhase.Sailing);
        }

        private void UpdatePause()
        {
            NetworkRunner runner = _session.Runner;
            bool online = !_session.Offline && !string.IsNullOrEmpty(_session.Code);
            _pauseCode.text = online ? $"Код лобби: <color=#EEB84D>{_session.Code}</color>" : "Одиночный рейс (оффлайн)";
            _pauseCopy.gameObject.SetActive(online);
            string crew = runner != null && runner.SessionInfo != null && runner.IsRunning
                ? $"Экипаж {runner.SessionInfo.PlayerCount}/{runner.SessionInfo.MaxPlayers}"
                : string.Empty;
            VoyageSave voyage = VoyageLaunch.Active;
            bool host = runner != null && runner.IsServer;
            string save = host && voyage != null ? $" · автосохранение «{voyage.Name}» ({voyage.ProgressText})" : string.Empty;
            _pauseInfo.text = crew + save;
            UiKit.SetLabel(_pauseLeave, host && voyage != null ? "Сохранить и покинуть рейс" : "Покинуть рейс");
        }

        // =========================================================================================
        // Misc
        // =========================================================================================
        private static string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("<", "‹").Replace(">", "›");

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
#endif
