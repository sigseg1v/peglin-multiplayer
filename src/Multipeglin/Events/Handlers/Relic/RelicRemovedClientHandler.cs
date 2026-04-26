using System;
using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;

public sealed class RelicRemovedClientHandler : IClientHandler<RelicRemovedEvent>
{
    public void Handle(RelicRemovedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Relic removed (effect {networkEvent.RelicEffect})");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"RelicRemoved handler failed: {e.Message}");
        }
    }
}
