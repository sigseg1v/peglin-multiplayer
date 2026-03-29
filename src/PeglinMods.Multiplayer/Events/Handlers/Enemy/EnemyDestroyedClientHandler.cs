namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyDestroyedClientHandler : IClientHandler<EnemyDestroyedEvent>
{
    public void Handle(EnemyDestroyedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"[EnemyDestroyed] guid={networkEvent.EnemyId}");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger.LogInfo($"[EnemyDestroyed] Found enemy '{enemy.locKey}' by GUID, destroying");
                enemyIdentifier.Unregister(networkEvent.EnemyId);

                var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
                em?.RemoveEnemy(enemy);
                UnityEngine.Object.Destroy(enemy.gameObject);
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyDestroyed] Could not find enemy guid={networkEvent.EnemyId}");
            }

            global::Battle.Enemies.Enemy.OnEnemyDestroyed?.Invoke(null);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDestroyed handler failed: {e.Message}");
        }
    }
}
