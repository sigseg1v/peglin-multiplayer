using System.Collections.Generic;

namespace PeglinMods.Multiplayer.Events.Network.Coop;

/// <summary>
/// Client → host: signals that the client has finished the post-battle
/// reward screen and sends the client's updated state for the host to
/// store in CoopPlayerState.
/// </summary>
public class PostBattleCompleteEvent
{
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public int Gold { get; set; }
    public List<PostBattleOrbEntry> CompleteDeck { get; set; } = new List<PostBattleOrbEntry>();
}

public class PostBattleOrbEntry
{
    public string PrefabName { get; set; }
    public int Level { get; set; }
}
