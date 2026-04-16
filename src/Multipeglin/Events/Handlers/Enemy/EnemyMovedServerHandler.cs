namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemyMovedServerHandler : IServerHandler<EnemyMovedEvent>
{
    public EnemyMovedEvent Handle(EnemyMovedEvent networkEvent) => networkEvent;
}
