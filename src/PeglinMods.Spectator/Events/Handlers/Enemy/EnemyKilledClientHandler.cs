namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyKilledClientHandler : IClientHandler<EnemyKilledEvent>
{
    public void Handle(EnemyKilledEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} ({networkEvent.LocKey}) killed");
    }
}
