
using Multipeglin.Events.Network.StatusEffect;

namespace Multipeglin.Events.Handlers.StatusEffect;
public sealed class StatusEffectsUpdatedServerHandler : IServerHandler<StatusEffectsUpdatedEvent>
{
    public StatusEffectsUpdatedEvent Handle(StatusEffectsUpdatedEvent networkEvent) => networkEvent;
}
