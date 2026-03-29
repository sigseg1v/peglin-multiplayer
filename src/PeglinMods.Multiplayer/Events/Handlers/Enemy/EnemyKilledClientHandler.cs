namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyKilledClientHandler : IClientHandler<EnemyKilledEvent>
{
    public void Handle(EnemyKilledEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"[EnemyKilled] guid={networkEvent.EnemyId} loc={networkEvent.LocKey}");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger.LogInfo($"[EnemyKilled] Found enemy '{enemy.locKey}' by GUID, setting hp=0");
                enemy.CurrentHealth = 0;
                enemyIdentifier.Unregister(networkEvent.EnemyId);
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyKilled] Could not find enemy guid={networkEvent.EnemyId}");
            }

            global::Battle.Enemies.Enemy.OnEnemyKilled?.Invoke(networkEvent.LocKey);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyKilled handler failed: {e.Message}");
        }
    }
}
