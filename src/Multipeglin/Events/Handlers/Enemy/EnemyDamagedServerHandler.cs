
using Multipeglin.Events.Network.Enemy;

namespace Multipeglin.Events.Handlers.Enemy;
public sealed class EnemyDamagedServerHandler : IServerHandler<EnemyDamagedEvent>
{
    public EnemyDamagedEvent Handle(EnemyDamagedEvent networkEvent) => networkEvent;
}
