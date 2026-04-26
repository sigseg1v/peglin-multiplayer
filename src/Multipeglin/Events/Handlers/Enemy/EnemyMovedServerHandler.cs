using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyMovedServerHandler : IServerHandler<EnemyMovedEvent>
{
    public EnemyMovedEvent Handle(EnemyMovedEvent networkEvent) => networkEvent;
}
