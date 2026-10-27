#if FUSION2
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace RumOverboard.Frontend
{
    /// <summary>
    /// Lobby room state (scene NetworkObject in Lobby.unity): the crew roster with ready flags.
    /// Host-authoritative: the host fills names from each player's connection token; clients
    /// toggle their ready flag via RPC. The captain (host) starts the voyage through GameSession.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkLobby : NetworkBehaviour, IPlayerJoined, IPlayerLeft
    {
        public struct Member : INetworkStruct
        {
            public NetworkString<_32> Name;
            public NetworkBool Ready;
            public int Order;
        }

        public readonly struct MemberView
        {
            public readonly PlayerRef Player;
            public readonly string Name;
            public readonly bool Ready;
            public readonly bool IsCaptain;
            public readonly bool IsLocal;

            public MemberView(PlayerRef player, string name, bool ready, bool captain, bool local)
            {
                Player = player;
                Name = name;
                Ready = ready;
                IsCaptain = captain;
                IsLocal = local;
            }
        }

        [Networked, Capacity(8)] private NetworkDictionary<PlayerRef, Member> Members => default;
        [Networked] private PlayerRef Captain { get; set; }
        [Networked] private int NextOrder { get; set; }

        public static NetworkLobby Instance { get; private set; }

        public bool IsReadyForUse => Object != null && Object.IsValid;
        public int Count => IsReadyForUse ? Members.Count : 0;

        public override void Spawned()
        {
            Instance = this;
            if (!HasStateAuthority)
                return;
            Captain = Runner.LocalPlayer;
            foreach (PlayerRef player in Runner.ActivePlayers)
                Register(player);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (HasStateAuthority)
                Register(player);
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (HasStateAuthority)
                Members.Remove(player);
        }

        private void Register(PlayerRef player)
        {
            if (!player.IsRealPlayer || Members.ContainsKey(player))
                return;
            string name = player == Runner.LocalPlayer
                ? PlayerProfile.Name
                : PlayerProfile.FromToken(Runner.GetPlayerConnectionToken(player));
            if (string.IsNullOrEmpty(name))
                name = $"Матрос {player.PlayerId}";
            Members.Set(player, new Member
            {
                Name = name,
                Ready = player == Runner.LocalPlayer,
                Order = NextOrder++,
            });
        }

        /// <summary>Local player's ready flag.</summary>
        public bool LocalReady => IsReadyForUse && Members.TryGet(Runner.LocalPlayer, out Member m) && m.Ready;

        public void SetLocalReady(bool ready)
        {
            if (IsReadyForUse)
                RPC_SetReady(ready);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SetReady(NetworkBool ready, RpcInfo info = default)
        {
            PlayerRef player = info.Source.IsRealPlayer ? info.Source : Runner.LocalPlayer;
            if (!Members.TryGet(player, out Member m))
                return;
            m.Ready = ready;
            Members.Set(player, m);
        }

        /// <summary>Everyone except the captain has pressed "ready".</summary>
        public bool AllReady
        {
            get
            {
                if (!IsReadyForUse)
                    return false;
                foreach (KeyValuePair<PlayerRef, Member> kv in Members)
                    if (kv.Key != Captain && !kv.Value.Ready)
                        return false;
                return true;
            }
        }

        public List<MemberView> Snapshot()
        {
            var list = new List<MemberView>();
            if (!IsReadyForUse)
                return list;
            var order = new List<(int order, MemberView view)>();
            foreach (KeyValuePair<PlayerRef, Member> kv in Members)
                order.Add((kv.Value.Order, new MemberView(kv.Key, kv.Value.Name.ToString(), kv.Value.Ready,
                    kv.Key == Captain, kv.Key == Runner.LocalPlayer)));
            order.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var o in order)
                list.Add(o.view);
            return list;
        }

        /// <summary>Cheap change detector for the UI.</summary>
        public int StateHash()
        {
            if (!IsReadyForUse)
                return 0;
            unchecked
            {
                int h = 17;
                foreach (KeyValuePair<PlayerRef, Member> kv in Members)
                    h += (kv.Key.PlayerId * 397) ^ kv.Value.Name.GetHashCode() ^ (kv.Value.Ready ? 7919 : 0) ^ kv.Value.Order;
                return h;
            }
        }
    }
}
#endif
