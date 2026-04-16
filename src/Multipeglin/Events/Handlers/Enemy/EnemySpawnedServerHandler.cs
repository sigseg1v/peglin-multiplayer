namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemySpawnedServerHandler : IServerHandler<EnemySpawnedEvent>
{
    public EnemySpawnedEvent Handle(EnemySpawnedEvent networkEvent) => networkEvent;
}
