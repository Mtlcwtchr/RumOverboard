#if FUSION2
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using RumOverboard.Core;
using RumOverboard.Networking;
using UnityEngine;

namespace RumOverboard.Frontend
{
    /// <summary>
    /// Photon Cloud lobby directory: the lobby code IS the Fusion session name, and the lobby
    /// list comes from a separate browser runner sitting in the ClientServer session lobby.
    /// Private lobbies are invisible in the list but joinable by code.
    /// </summary>
    public sealed class PhotonLobbyDirectory : NetworkCallbacksBase, ILobbyDirectory
    {
        private const string KeyName = "n";
        private const string KeyHost = "h";
        private const string KeyVoyage = "d";
        private const string KeyState = "s";
        private const string KeyVersion = "v";

        private readonly Transform _parent;
        private readonly GameConfig _config;
        private readonly List<LobbyListing> _listings = new();
        private NetworkRunner _browser;
        private bool _starting;

        public PhotonLobbyDirectory(Transform parent, GameConfig config)
        {
            _parent = parent;
            _config = config;
        }

        public string DisplayName => "Photon Cloud";
        public bool IsBrowsing => _browser != null;
        public string Status { get; private set; } = string.Empty;
        public IReadOnlyList<LobbyListing> Listings => _listings;
        public event Action Changed;

        public async UniTask StartBrowsingAsync()
        {
            if (_browser != null || _starting)
                return;
            _starting = true;
            SetStatus("Поиск лобби…");
            try
            {
                ConnectionManager.ApplyAppId(_config);
                var go = new GameObject("NetworkRunner (Lobby Browser)");
                go.transform.SetParent(_parent, false);
                _browser = go.AddComponent<NetworkRunner>();
                _browser.AddCallbacks(this);
                StartGameResult result = await _browser.JoinSessionLobby(SessionLobby.ClientServer);
                if (!result.Ok)
                {
                    Debug.LogWarning($"[Lobby] Browser could not join the session lobby: {result.ShutdownReason}");
                    await DisposeBrowser();
                    SetStatus("Нет связи с Photon Cloud — список недоступен, но можно войти по коду.");
                    return;
                }
                SetStatus(_listings.Count == 0 ? "Открытых лобби пока нет." : string.Empty);
            }
            finally
            {
                _starting = false;
            }
        }

        public async UniTask StopBrowsingAsync()
        {
            while (_starting)
                await UniTask.Yield();
            await DisposeBrowser();
            _listings.Clear();
            Status = string.Empty;
        }

        private async UniTask DisposeBrowser()
        {
            NetworkRunner runner = _browser;
            _browser = null;
            if (runner == null)
                return;
            runner.RemoveCallbacks(this);
            try { await runner.Shutdown(); }
            catch (Exception e) { Debug.LogWarning($"[Lobby] Browser shutdown: {e.Message}"); }
            if (runner != null && runner.gameObject != null)
                UnityEngine.Object.Destroy(runner.gameObject);
        }

        public override void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            if (runner != _browser)
                return;
            _listings.Clear();
            foreach (SessionInfo info in sessionList)
            {
                if (info == null || !info.IsValid || !info.IsVisible)
                    continue;
                LobbyAdvert advert = Read(info);
                _listings.Add(new LobbyListing
                {
                    Code = info.Name,
                    Name = string.IsNullOrEmpty(advert.Name) ? info.Name : advert.Name,
                    Host = advert.Host,
                    Voyage = advert.Voyage,
                    Sailing = advert.Sailing,
                    Players = info.PlayerCount,
                    MaxPlayers = info.MaxPlayers,
                    Open = info.IsOpen,
                    VersionMatches = GetString(info, KeyVersion) == Application.version,
                });
            }
            _listings.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            SetStatus(_listings.Count == 0 ? "Открытых лобби пока нет." : string.Empty);
        }

        public override void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (runner != _browser)
                return;
            _browser = null;
            _listings.Clear();
            SetStatus($"Поиск остановлен ({shutdownReason}).");
        }

        public string CreateCode() => LobbyCode.Create();

        public UniTask<string> ResolveAsync(string code) => UniTask.FromResult(LobbyCode.Normalize(code));

        public Dictionary<string, SessionProperty> SessionProperties(LobbyAdvert advert) => new()
        {
            [KeyName] = Clip(advert.Name, 32),
            [KeyHost] = Clip(advert.Host, 24),
            [KeyVoyage] = Clip(advert.Voyage, 48),
            [KeyState] = advert.Sailing ? 1 : 0,
            [KeyVersion] = Application.version,
        };

        public void Publish(NetworkRunner hostRunner, LobbyAdvert advert)
        {
            if (hostRunner == null || !hostRunner.IsRunning || !hostRunner.IsServer || hostRunner.SessionInfo == null)
                return;
            hostRunner.SessionInfo.UpdateCustomProperties(SessionProperties(advert));
        }

        public LobbyAdvert Read(SessionInfo session) => new()
        {
            Name = GetString(session, KeyName),
            Host = GetString(session, KeyHost),
            Voyage = GetString(session, KeyVoyage),
            Sailing = GetInt(session, KeyState) == 1,
            Private = session != null && !session.IsVisible,
        };

        /// <summary>True when the joined session was hosted by the same game build.</summary>
        public static bool VersionMatches(SessionInfo session) => GetString(session, KeyVersion) == Application.version;

        private static string GetString(SessionInfo session, string key)
        {
            if (session?.Properties == null || !session.Properties.TryGetValue(key, out SessionProperty p) || p == null)
                return string.Empty;
            return p.IsString ? (string)p : p.PropertyValue?.ToString() ?? string.Empty;
        }

        private static int GetInt(SessionInfo session, string key)
        {
            if (session?.Properties == null || !session.Properties.TryGetValue(key, out SessionProperty p) || p == null)
                return 0;
            return p.IsInt ? (int)p : 0;
        }

        private static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length > max ? s.Substring(0, max) : s;

        private void SetStatus(string status)
        {
            Status = status;
            Changed?.Invoke();
        }
    }
}
#endif
