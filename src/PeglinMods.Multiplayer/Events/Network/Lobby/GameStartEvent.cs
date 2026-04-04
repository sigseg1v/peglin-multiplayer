using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Events.Network.Lobby;

public class GameStartEvent
{
    public List<LobbyPlayerEntry> FinalPlayers { get; set; } = new List<LobbyPlayerEntry>();
}
