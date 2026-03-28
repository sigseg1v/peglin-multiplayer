namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallUsedClientHandler : IClientHandler<BallUsedEvent>
{
    public void Handle(BallUsedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Used orb {networkEvent.OrbName}");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallUsed handler failed: {e.Message}");
        }
    }
}
