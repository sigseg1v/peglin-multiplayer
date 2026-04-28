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

    /// <summary>True for placeholder rows in a continue lobby where the saved
    /// player hasn't (re)joined yet. The host reorders by saved SlotIndex and
    /// inserts MISSING entries for absent slots so every client renders the
    /// roster in the same order the host sees.</summary>
    public bool IsMissing { get; set; }
}
