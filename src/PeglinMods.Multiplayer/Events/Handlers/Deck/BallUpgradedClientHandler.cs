namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallUpgradedClientHandler : IClientHandler<BallUpgradedEvent>
{
    public void Handle(BallUpgradedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Orb upgraded {networkEvent.PreviousOrbName} -> {networkEvent.NewOrbName} (level {networkEvent.NewLevel})");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallUpgraded handler failed: {e.Message}");
        }
    }
}
