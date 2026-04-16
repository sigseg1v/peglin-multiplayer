namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemyAttackServerHandler : IServerHandler<EnemyAttackEvent>
{
    public EnemyAttackEvent Handle(EnemyAttackEvent networkEvent) => networkEvent;
}
