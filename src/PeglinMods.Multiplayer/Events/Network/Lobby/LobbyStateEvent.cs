using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Events.Network.Lobby;

public class LobbyStateEvent
{
    public List<LobbyPlayerEntry> Players { get; set; } = new List<LobbyPlayerEntry>();
}
