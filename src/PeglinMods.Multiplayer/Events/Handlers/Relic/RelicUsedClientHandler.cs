namespace PeglinMods.Multiplayer.Events.Handlers.Relic;

using System;
using PeglinMods.Multiplayer.Events.Network.Relic;

public sealed class RelicUsedClientHandler : IClientHandler<RelicUsedEvent>
{
    public void Handle(RelicUsedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Relic used (effect {networkEvent.RelicEffect})");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"RelicUsed handler failed: {e.Message}");
        }
    }
}
