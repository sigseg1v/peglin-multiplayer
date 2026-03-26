namespace PeglinMods.Spectator.Events.Handlers.Relic;

using System;
using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicAddedClientHandler : IClientHandler<RelicAddedEvent>
{
    public void Handle(RelicAddedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Relic added {networkEvent.RelicName} (effect {networkEvent.RelicEffect})");
            // RelicManager is a ScriptableObject wired via SerializeField, not findable via
            // FindObjectOfType. Cannot invoke OnRelicAdded without an instance reference - log only.
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"RelicAdded handler failed: {e.Message}");
        }
    }
}
