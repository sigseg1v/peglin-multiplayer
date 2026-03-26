namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicUsedClientHandler : IClientHandler<RelicUsedEvent>
{
    public void Handle(RelicUsedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Relic used (effect {networkEvent.RelicEffect})");
    }
}
