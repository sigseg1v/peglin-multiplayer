namespace Multipeglin.Multiplayer;

public class PlayerSlot
{
    public int SlotIndex { get; set; }

    public int PeerId { get; set; }

    public string PlayerName { get; set; }

    public bool IsHost { get; set; }

    public int ChosenClass { get; set; }

    public bool IsReady { get; set; }

    public string GameVersion { get; set; }

    public string ModVersion { get; set; }
}
