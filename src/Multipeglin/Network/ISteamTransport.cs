using System;
using Steamworks;

namespace Multipeglin.Network;

public interface ISteamTransport : INetworkTransport
{
    CSteamID HostedLobbyId { get; }

    void JoinSteamLobby(CSteamID lobbyId);

    void CloseLobbyOnStart();

    event Action<CSteamID> OnIncomingInvite;
}
