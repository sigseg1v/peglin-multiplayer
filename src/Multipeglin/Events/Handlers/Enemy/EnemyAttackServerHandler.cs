using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyAttackServerHandler : IServerHandler<EnemyAttackEvent>
{
    public EnemyAttackEvent Handle(EnemyAttackEvent networkEvent) => networkEvent;
}
