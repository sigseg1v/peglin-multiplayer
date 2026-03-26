namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyDamagedClientHandler : IClientHandler<EnemyDamagedEvent>
{
    public void Handle(EnemyDamagedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} took {networkEvent.Damage} damage");
    }
}
