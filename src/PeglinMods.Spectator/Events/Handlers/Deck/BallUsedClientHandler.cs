namespace PeglinMods.Spectator.Events.Handlers.Deck;

using System;
using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUsedClientHandler : IClientHandler<BallUsedEvent>
{
    public void Handle(BallUsedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Used orb {networkEvent.OrbName}");
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BallUsed handler failed: {e.Message}");
        }
    }
}
