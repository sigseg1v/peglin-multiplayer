namespace Multipeglin.Events.Network.Scenarios;

/// <summary>
/// Client -> host: the client has finished treasure relic selection.
/// Host applies the relic to the client's CoopPlayerState.
/// </summary>
public class TreasureCompleteEvent
{
    /// <summary>RelicEffect enum value of the chosen relic, or -1 if skipped.</summary>
    public int ChosenRelicEffect { get; set; } = -1;

    /// <summary>Loc key of the chosen relic (for logging), or null if skipped.</summary>
    public string ChosenRelicName { get; set; }
}
