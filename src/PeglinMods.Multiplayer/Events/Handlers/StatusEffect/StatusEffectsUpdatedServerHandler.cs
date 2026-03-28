namespace PeglinMods.Multiplayer.Events.Handlers.StatusEffect;

using PeglinMods.Multiplayer.Events.Network.StatusEffect;

public sealed class StatusEffectsUpdatedServerHandler : IServerHandler<StatusEffectsUpdatedEvent>
{
    public StatusEffectsUpdatedEvent Handle(StatusEffectsUpdatedEvent networkEvent) => networkEvent;
}
