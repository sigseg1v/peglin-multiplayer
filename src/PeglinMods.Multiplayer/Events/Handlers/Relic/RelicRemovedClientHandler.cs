namespace PeglinMods.Multiplayer.Events.Handlers.Relic;

using System;
using PeglinMods.Multiplayer.Events.Network.Relic;

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
