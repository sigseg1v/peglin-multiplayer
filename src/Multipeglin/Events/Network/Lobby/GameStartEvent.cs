using System.Collections.Generic;

namespace Multipeglin.Events.Network.Lobby;

public class GameStartEvent
{
    public List<LobbyPlayerEntry> FinalPlayers { get; set; } = new List<LobbyPlayerEntry>();

    public int CruciballLevel { get; set; }

    /// <summary>True when the host clicked "Continue" with a restored save.</summary>
    public bool IsContinue { get; set; }
}
