namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyDamagedServerHandler : IServerHandler<EnemyDamagedEvent>
{
    public EnemyDamagedEvent Handle(EnemyDamagedEvent networkEvent) => networkEvent;
}
