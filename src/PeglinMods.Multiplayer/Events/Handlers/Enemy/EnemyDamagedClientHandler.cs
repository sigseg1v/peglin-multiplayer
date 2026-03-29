namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using global::Battle.Enemies;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyDamagedClientHandler : IClientHandler<EnemyDamagedEvent>
{
    public void Handle(EnemyDamagedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"[EnemyDamaged] guid={networkEvent.EnemyId} dmg={networkEvent.Damage} remaining={networkEvent.RemainingHealth}");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                var oldHp = enemy.CurrentHealth;
                enemy.CurrentHealth = networkEvent.RemainingHealth;
                MultiplayerPlugin.Logger.LogInfo($"[EnemyDamaged] '{enemy.locKey}' hp: {oldHp} → {networkEvent.RemainingHealth}");

                global::Battle.Enemies.Enemy.OnEnemyDamaged?.Invoke(
                    enemy,
                    networkEvent.Damage,
                    (global::Battle.Enemies.Enemy.EnemyDamageSource)networkEvent.DamageSource);
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyDamaged] Could not find enemy guid={networkEvent.EnemyId}");
                enemyIdentifier.DumpState("EnemyDamaged_Miss");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDamaged handler failed: {e.Message}");
        }
    }
}
