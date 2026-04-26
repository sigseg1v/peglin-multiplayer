using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyDestroyedServerHandler : IServerHandler<EnemyDestroyedEvent>
{
    public EnemyDestroyedEvent Handle(EnemyDestroyedEvent networkEvent) => networkEvent;
}
