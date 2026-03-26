namespace PeglinMods.Spectator.Events.Handlers.Currency;

using PeglinMods.Spectator.Events.Network.Currency;

public sealed class GoldChangedClientHandler : IClientHandler<GoldChangedEvent>
{
    public void Handle(GoldChangedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Gold changed {networkEvent.PreviousAmount} -> {networkEvent.NewAmount} ({(networkEvent.IsGain ? "+" : "")}{networkEvent.Delta})");
    }
}
