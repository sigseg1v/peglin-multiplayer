namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemyDestroyedServerHandler : IServerHandler<EnemyDestroyedEvent>
{
    public EnemyDestroyedEvent Handle(EnemyDestroyedEvent networkEvent) => networkEvent;
}
