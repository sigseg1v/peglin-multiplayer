namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUpgradedClientHandler : IClientHandler<BallUpgradedEvent>
{
    public void Handle(BallUpgradedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Upgraded {networkEvent.PreviousOrbName} -> {networkEvent.NewOrbName} (level {networkEvent.NewLevel})");
    }
}
