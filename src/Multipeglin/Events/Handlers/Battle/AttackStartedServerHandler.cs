namespace Multipeglin.Events.Handlers.Battle;

using Multipeglin.Events.Network.Battle;

public sealed class AttackStartedServerHandler : IServerHandler<AttackStartedEvent>
{
    public AttackStartedEvent Handle(AttackStartedEvent networkEvent) => networkEvent;
}
