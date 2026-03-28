namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemySpawnedServerHandler : IServerHandler<EnemySpawnedEvent>
{
    public EnemySpawnedEvent Handle(EnemySpawnedEvent networkEvent) => networkEvent;
}
