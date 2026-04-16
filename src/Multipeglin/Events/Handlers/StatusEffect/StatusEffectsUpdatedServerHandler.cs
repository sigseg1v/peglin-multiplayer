namespace Multipeglin.Events.Handlers.StatusEffect;

using Multipeglin.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedServerHandler : IServerHandler<StatusEffectsUpdatedEvent>
{
    public StatusEffectsUpdatedEvent Handle(StatusEffectsUpdatedEvent networkEvent) => networkEvent;
}
