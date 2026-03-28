namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyAttackClientHandler : IClientHandler<EnemyAttackEvent>
{
    public void Handle(EnemyAttackEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy {networkEvent.EnemyId} attacked for {networkEvent.Damage} damage (melee: {networkEvent.IsMelee})");
            // Finding the exact enemy and invoking its attack animation is complex - log only for now
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyAttack handler failed: {e.Message}");
        }
    }
}
