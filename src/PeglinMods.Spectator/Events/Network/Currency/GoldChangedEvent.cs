namespace PeglinMods.Spectator.Events.Network.Currency;

public class GoldChangedEvent
{
    public int PreviousAmount { get; set; }
    public int NewAmount { get; set; }
    public int Delta { get; set; }
    public bool IsGain { get; set; }
}
