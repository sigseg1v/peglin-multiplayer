namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyMovedServerHandler : IServerHandler<EnemyMovedEvent>
{
    public EnemyMovedEvent Handle(EnemyMovedEvent networkEvent) => networkEvent;
}
