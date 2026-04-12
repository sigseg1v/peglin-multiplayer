namespace PeglinMods.Multiplayer.Events.Network.Scenarios;

/// <summary>
/// Host → clients: signals that a Mirror event has started and the client
/// should show their own interactive UI for orb removal choices.
/// </summary>
public class MirrorEventStartEvent
{
    public string ScenarioName { get; set; }
}
