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
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy {networkEvent.EnemyId} took {networkEvent.Damage} damage (remaining: {networkEvent.RemainingHealth})");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
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
                MultiplayerPlugin.Logger.LogWarning($"Could not find enemy {networkEvent.EnemyId} for damage update");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDamaged handler failed: {e.Message}");
        }
    }
}
