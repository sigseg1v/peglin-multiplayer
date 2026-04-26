
using System;
using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;
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
