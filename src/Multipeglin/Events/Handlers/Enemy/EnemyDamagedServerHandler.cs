namespace Multipeglin.Events.Handlers.Enemy;

using Multipeglin.Events.Network.Enemy;

public sealed class EnemyDamagedServerHandler : IServerHandler<EnemyDamagedEvent>
{
    public EnemyDamagedEvent Handle(EnemyDamagedEvent networkEvent) => networkEvent;
}
