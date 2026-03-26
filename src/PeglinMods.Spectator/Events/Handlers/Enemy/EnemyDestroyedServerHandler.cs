namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyDestroyedServerHandler : IServerHandler<EnemyDestroyedEvent>
{
    public EnemyDestroyedEvent Handle(EnemyDestroyedEvent networkEvent) => networkEvent;
}
