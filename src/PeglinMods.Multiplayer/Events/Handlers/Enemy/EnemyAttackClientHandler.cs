namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyAttackClientHandler : IClientHandler<EnemyAttackEvent>
{
    public void Handle(EnemyAttackEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"[EnemyAttack] guid={networkEvent.EnemyId} dmg={networkEvent.Damage} melee={networkEvent.IsMelee}");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger.LogInfo($"[EnemyAttack] '{enemy.locKey}' attacking for {networkEvent.Damage}");
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyAttack] Could not find enemy guid={networkEvent.EnemyId}");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyAttack handler failed: {e.Message}");
        }
    }
}
