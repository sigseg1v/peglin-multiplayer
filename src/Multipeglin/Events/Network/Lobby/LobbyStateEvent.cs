using System.Collections.Generic;

namespace Multipeglin.Events.Network.Lobby;

public class LobbyStateEvent
{
    public List<LobbyPlayerEntry> Players { get; set; } = new List<LobbyPlayerEntry>();

    public int CruciballLevel { get; set; }

    /// <summary>True when the host's lobby is in "Continue" mode (resuming a saved
    /// run). Clients use this to lock the class selector — saved players must
    /// keep their original class.</summary>
    public bool IsContinue { get; set; }
}
