namespace PeglinMods.Multiplayer.Events.Network.Deck;

public class BallUpgradedEvent
{
    public string PreviousOrbName { get; set; }
    public string NewOrbName { get; set; }
    public int NewLevel { get; set; }
}
