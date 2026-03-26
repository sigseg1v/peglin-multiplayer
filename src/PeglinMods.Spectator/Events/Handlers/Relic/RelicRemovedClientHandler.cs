namespace PeglinMods.Spectator.Events.Handlers.Relic;

using PeglinMods.Spectator.Events.Network.Relic;

public sealed class RelicRemovedClientHandler : IClientHandler<RelicRemovedEvent>
{
    public void Handle(RelicRemovedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Relic removed (effect {networkEvent.RelicEffect})");
    }
}
