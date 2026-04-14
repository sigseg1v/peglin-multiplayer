using System.Collections.Generic;
using PeglinMods.Multiplayer.GameState;

namespace PeglinMods.Multiplayer.Events.Network.Scenarios;

/// <summary>
/// Client → host: the client has finished a TextScenario dialogue (mirror, altar, etc.).
/// Contains the client's full post-dialogue state so the host can update CoopPlayerState.
/// </summary>
public class TextScenarioCompleteEvent
{
    /// <summary>Complete deck after dialogue modifications (add/remove/upgrade).</summary>
    public List<SerializedOrb> CompleteDeck { get; set; } = new List<SerializedOrb>();

    /// <summary>Current health after any dialogue-inflicted damage/healing.</summary>
    public float CurrentHealth { get; set; }

    /// <summary>Max health (may change from relic effects).</summary>
    public float MaxHealth { get; set; }

    /// <summary>Gold after any dialogue costs/rewards.</summary>
    public int Gold { get; set; }

    /// <summary>Full relic list after any relics gained during dialogue.</summary>
    public List<SerializedRelic> Relics { get; set; } = new List<SerializedRelic>();
}
