namespace Multipeglin.Events.Network.Scenarios;

/// <summary>
/// Client -> host: the client has finished the PegMinigame orb/relic prize.
/// Host applies the chosen reward to the client's CoopPlayerState.
/// </summary>
public class PegMinigameCompleteEvent
{
    /// <summary>Prefab name of the chosen orb, or null if skipped or relic.</summary>
    public string ChosenOrbPrefabName { get; set; }

    /// <summary>Level of the chosen orb (0 if skipped or relic).</summary>
    public int OrbLevel { get; set; }

    /// <summary>RelicEffect enum value of the chosen relic, or -1 if skipped or orb.</summary>
    public int ChosenRelicEffect { get; set; } = -1;

    /// <summary>True if the player skipped the reward.</summary>
    public bool Skipped { get; set; }
}
