using System;
using Multipeglin.Events.Network.Enemy;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Enemy;

public sealed class EnemyMovedClientHandler : IClientHandler<EnemyMovedEvent>
{
    public void Handle(EnemyMovedEvent networkEvent)
    {
        try
        {
            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger.LogInfo($"[EnemyMoved] '{enemy.locKey}' (guid={networkEvent.EnemyId}) slot {networkEvent.FromSlot} → {networkEvent.ToSlot}");
                // TODO: actually move the enemy to the new slot position
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyMoved] Could not find enemy guid={networkEvent.EnemyId} (slot {networkEvent.FromSlot} → {networkEvent.ToSlot})");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyMoved handler failed: {e.Message}");
        }
    }
}
