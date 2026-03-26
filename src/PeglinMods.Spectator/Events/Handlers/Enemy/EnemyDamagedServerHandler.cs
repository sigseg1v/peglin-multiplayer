namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyDamagedServerHandler : IServerHandler<EnemyDamagedEvent>
{
    public EnemyDamagedEvent Handle(EnemyDamagedEvent networkEvent) => networkEvent;
}
