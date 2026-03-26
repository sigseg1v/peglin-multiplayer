namespace PeglinMods.Spectator.Events.Handlers.Relic;

using System;
using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicRemovedClientHandler : IClientHandler<RelicRemovedEvent>
{
    public void Handle(RelicRemovedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Relic removed (effect {networkEvent.RelicEffect})");
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"RelicRemoved handler failed: {e.Message}");
        }
    }
}
