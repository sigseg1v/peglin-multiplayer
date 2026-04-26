using Multipeglin.Events.Network.Battle;

namespace Multipeglin.Events.Handlers.Battle;

public sealed class AttackStartedServerHandler : IServerHandler<AttackStartedEvent>
{
    public AttackStartedEvent Handle(AttackStartedEvent networkEvent) => networkEvent;
}
