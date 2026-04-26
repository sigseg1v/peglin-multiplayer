namespace Multipeglin.Events.Network.Lobby;

public class LobbyPlayerEntry
{
    public int SlotIndex { get; set; }

    public string PlayerName { get; set; }

    public int ChosenClass { get; set; }

    public string ChosenClassName { get; set; }

    public bool IsReady { get; set; }

    public bool IsHost { get; set; }

    public string GameVersion { get; set; }

    public string ModVersion { get; set; }
}
