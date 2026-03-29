namespace PeglinMods.Multiplayer.Events.Network.Map;

/// <summary>
/// Sent by the host when a map node is activated (clicked).
/// The client finds the matching node by position and activates it,
/// which sets up StaticGameData.dataToLoad for the correct battle.
/// </summary>
public class NodeActivatedEvent
{
    public float PosX { get; set; }
    public float PosY { get; set; }
}
