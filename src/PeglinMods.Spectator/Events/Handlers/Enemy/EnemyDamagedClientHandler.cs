namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using global::Battle.Enemies;
using PeglinMods.Spectator.Events.Network.Enemy;
using PeglinMods.Spectator.Utility;

public sealed class EnemyDamagedClientHandler : IClientHandler<EnemyDamagedEvent>
{
    public void Handle(EnemyDamagedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} took {networkEvent.Damage} damage (remaining: {networkEvent.RemainingHealth})");

            var enemyIdentifier = SpectatorPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                enemy.CurrentHealth = networkEvent.RemainingHealth;
                global::Battle.Enemies.Enemy.OnEnemyDamaged?.Invoke(
                    enemy,
                    networkEvent.Damage,
                    (global::Battle.Enemies.Enemy.EnemyDamageSource)networkEvent.DamageSource);
            }
            else
            {
                SpectatorPlugin.Logger.LogWarning($"Could not find enemy {networkEvent.EnemyId} for damage update");
            }
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemyDamaged handler failed: {e.Message}");
        }
    }
}
