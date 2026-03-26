namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicAddedClientHandler : IClientHandler<RelicAddedEvent>
{
    public void Handle(RelicAddedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Relic added - {networkEvent.RelicName} (effect {networkEvent.RelicEffect})");
    }
}
