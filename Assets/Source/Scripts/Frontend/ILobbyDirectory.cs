#if FUSION2
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;

namespace RumOverboard.Frontend
{
    /// <summary>What the host advertises about its lobby.</summary>
    public struct LobbyAdvert
    {
        public string Name;
        public string Host;
        public string Voyage;
        public bool Sailing;
        public bool Private;
    }

    /// <summary>One joinable lobby as shown in the browser.</summary>
    public sealed class LobbyListing
    {
        public string Code;
        public string Name;
        public string Host;
        public string Voyage;
        public bool Sailing;
        public int Players;
        public int MaxPlayers;
        public bool Open;
        public bool VersionMatches;

        public bool Joinable => Open && VersionMatches && Players < MaxPlayers;
    }

    /// <summary>
    /// Lobby discovery / advertising, independent of the game transport. The Photon Cloud
    /// implementation lists Fusion sessions directly; a Steam implementation would publish a
    /// Steam lobby carrying the Fusion session name and resolve codes / invites through it.
    /// </summary>
    public interface ILobbyDirectory
    {
        /// <summary>Backend name for the UI ("Photon Cloud", "Steam").</summary>
        string DisplayName { get; }
        bool IsBrowsing { get; }
        /// <summary>Human-readable browse status (searching, error, …).</summary>
        string Status { get; }
        IReadOnlyList<LobbyListing> Listings { get; }
        event Action Changed;

        UniTask StartBrowsingAsync();
        UniTask StopBrowsingAsync();

        /// <summary>New shareable code for a lobby this peer is about to host.</summary>
        string CreateCode();

        /// <summary>Fusion session name for a lobby code (null = not found).</summary>
        UniTask<string> ResolveAsync(string code);

        /// <summary>Session properties to start the host session with.</summary>
        Dictionary<string, SessionProperty> SessionProperties(LobbyAdvert advert);

        /// <summary>Re-publishes the advert of a running host session (state change etc.).</summary>
        void Publish(NetworkRunner hostRunner, LobbyAdvert advert);

        /// <summary>Reads the advert back from a joined session (clients).</summary>
        LobbyAdvert Read(SessionInfo session);
    }
}
#endif
