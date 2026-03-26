namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyMovedServerHandler : IServerHandler<EnemyMovedEvent>
{
    public EnemyMovedEvent Handle(EnemyMovedEvent networkEvent) => networkEvent;
}
