namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyKilledServerHandler : IServerHandler<EnemyKilledEvent>
{
    public EnemyKilledEvent Handle(EnemyKilledEvent networkEvent) => networkEvent;
}
