namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyAttackServerHandler : IServerHandler<EnemyAttackEvent>
{
    public EnemyAttackEvent Handle(EnemyAttackEvent networkEvent) => networkEvent;
}
