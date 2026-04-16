namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemyKilledServerHandler : IServerHandler<EnemyKilledEvent>
{
    public EnemyKilledEvent Handle(EnemyKilledEvent networkEvent) => networkEvent;
}
