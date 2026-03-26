namespace PeglinMods.Spectator.Events.Handlers.StatusEffect;

using PeglinMods.Spectator.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedServerHandler : IServerHandler<StatusEffectsUpdatedEvent>
{
    public StatusEffectsUpdatedEvent Handle(StatusEffectsUpdatedEvent networkEvent) => networkEvent;
}
