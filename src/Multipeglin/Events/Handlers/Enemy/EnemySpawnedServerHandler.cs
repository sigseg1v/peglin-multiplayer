using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemySpawnedServerHandler : IServerHandler<EnemySpawnedEvent>
{
    public EnemySpawnedEvent Handle(EnemySpawnedEvent networkEvent) => networkEvent;
}
