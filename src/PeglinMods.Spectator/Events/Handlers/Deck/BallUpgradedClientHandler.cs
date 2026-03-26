namespace PeglinMods.Spectator.Events.Handlers.Deck;

using System;
using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUpgradedClientHandler : IClientHandler<BallUpgradedEvent>
{
    public void Handle(BallUpgradedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Orb upgraded {networkEvent.PreviousOrbName} -> {networkEvent.NewOrbName} (level {networkEvent.NewLevel})");
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BallUpgraded handler failed: {e.Message}");
        }
    }
}
