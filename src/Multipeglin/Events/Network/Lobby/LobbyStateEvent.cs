using System.Collections.Generic;

namespace Multipeglin.Events.Network.Lobby;

public class LobbyStateEvent
{
    public List<LobbyPlayerEntry> Players { get; set; } = new List<LobbyPlayerEntry>();
    public int CruciballLevel { get; set; }
}
