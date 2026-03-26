namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyAttackClientHandler : IClientHandler<EnemyAttackEvent>
{
    public void Handle(EnemyAttackEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} attacked for {networkEvent.Damage} damage (melee: {networkEvent.IsMelee})");
            // Finding the exact enemy and invoking its attack animation is complex - log only for now
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemyAttack handler failed: {e.Message}");
        }
    }
}
