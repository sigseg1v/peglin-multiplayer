namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemySpawnedServerHandler : IServerHandler<EnemySpawnedEvent>
{
    public EnemySpawnedEvent Handle(EnemySpawnedEvent networkEvent) => networkEvent;
}
