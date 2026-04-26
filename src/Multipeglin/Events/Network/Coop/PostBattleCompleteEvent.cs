using System.Collections.Generic;

namespace Multipeglin.Events.Network.Coop;

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

    /// <summary>Relic effect chosen during boss/rare relic selection. -1 if skipped or none.</summary>
    public int ChosenRelicEffect { get; set; } = -1;

    /// <summary>LocKey of the chosen relic, or null if skipped.</summary>
    public string ChosenRelicName { get; set; }
}

public class PostBattleOrbEntry
{
    public string PrefabName { get; set; }

    public int Level { get; set; }
}
