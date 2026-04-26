using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyKilledServerHandler : IServerHandler<EnemyKilledEvent>
{
    public EnemyKilledEvent Handle(EnemyKilledEvent networkEvent) => networkEvent;
}
