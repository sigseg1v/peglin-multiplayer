namespace PeglinMods.Spectator.Events.Handlers.Relic;

using System;
using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicUsedClientHandler : IClientHandler<RelicUsedEvent>
{
    public void Handle(RelicUsedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Relic used (effect {networkEvent.RelicEffect})");
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"RelicUsed handler failed: {e.Message}");
        }
    }
}
