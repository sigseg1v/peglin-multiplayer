namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyAttackClientHandler : IClientHandler<EnemyAttackEvent>
{
    public void Handle(EnemyAttackEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} attacked for {networkEvent.Damage} damage (melee: {networkEvent.IsMelee})");
    }
}
