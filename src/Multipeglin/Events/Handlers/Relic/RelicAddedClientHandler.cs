using System;
using Multipeglin.Events.Network.Relic;

namespace Multipeglin.Events.Handlers.Relic;

public sealed class RelicAddedClientHandler : IClientHandler<RelicAddedEvent>
{
    public void Handle(RelicAddedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Relic added {networkEvent.RelicName} (effect {networkEvent.RelicEffect})");
            // RelicManager is a ScriptableObject wired via SerializeField, not findable via
            // FindObjectOfType. Cannot invoke OnRelicAdded without an instance reference - log only.
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"RelicAdded handler failed: {e.Message}");
        }
    }
}
