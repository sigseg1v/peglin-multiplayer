using System;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyDestroyedClientHandler : IClientHandler<EnemyDestroyedEvent>
{
    public void Handle(EnemyDestroyedEvent networkEvent)
    {
        try
        {
            // EnemyKilled already unregisters the GUID and sets hp=0.
            // EnemyDestroyed fires after the death animation. If the enemy
            // is still around (not yet cleaned up by state sync), destroy it now.
            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                enemyIdentifier.Unregister(networkEvent.EnemyId);
                var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
                em?.RemoveEnemy(enemy);
                UnityEngine.Object.Destroy(enemy.gameObject);
            }
            // Don't invoke OnEnemyDestroyed with null — subscribers dereference it
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDestroyed handler failed: {e.Message}");
        }
    }
}
