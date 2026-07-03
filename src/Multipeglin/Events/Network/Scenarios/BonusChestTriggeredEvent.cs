namespace Multipeglin.Events.Network.Scenarios;

/// <summary>
/// Bidirectional: a player detonated every bomb in a navigation pegboard,
/// triggering the secret bonus chest room.
///
/// Client -> host: "my local bomb counter hit zero". The host dedupes (first
/// trigger wins), applies the transition locally, and rebroadcasts.
/// Host -> clients: "everyone transition to the bonus TREASURE room now".
///
/// Source values:
///   "chest"        - ChestScenarioController treasure-room navigation bombs
///   "store_robbed" - ScenarioNavigationBonusChest pegboard bombs (sets StoreRobbed)
/// </summary>
public class BonusChestTriggeredEvent
{
    public string Source { get; set; }
}
