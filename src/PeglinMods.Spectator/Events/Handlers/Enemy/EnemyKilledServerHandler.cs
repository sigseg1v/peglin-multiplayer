namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyKilledServerHandler : IServerHandler<EnemyKilledEvent>
{
    public EnemyKilledEvent Handle(EnemyKilledEvent networkEvent) => networkEvent;
}
