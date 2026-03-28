namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyDestroyedServerHandler : IServerHandler<EnemyDestroyedEvent>
{
    public EnemyDestroyedEvent Handle(EnemyDestroyedEvent networkEvent) => networkEvent;
}
