namespace Multipeglin.Events.Network.Map;

/// <summary>
/// Sent by the host when a map node is activated (clicked).
/// Includes the battle asset name so the client can load the exact same encounter
/// regardless of its own map generation state.
/// </summary>
public class NodeActivatedEvent
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    /// <summary>MapDataBattle.name from the host (e.g. "SlimeEncounter2")</summary>
    public string BattleName { get; set; }

    /// <summary>Serialized UnityEngine.Random.state at the moment of node activation,
    /// before the battle scene loads. Client restores this so RandomPegField
    /// generates identical positions.</summary>
    public string RngState { get; set; }

    /// <summary>MapData asset name for non-battle nodes (e.g. MapDataPegMinigame).
    /// Used by the client to look up the correct ScriptableObject for scene init.</summary>
    public string MapDataName { get; set; }
}
