namespace PeglinMods.Spectator.Events.Handlers.Battle;

using PeglinMods.Spectator.Events.Network.Battle;

public sealed class AttackStartedServerHandler : IServerHandler<AttackStartedEvent>
{
    public AttackStartedEvent Handle(AttackStartedEvent networkEvent) => networkEvent;
}
