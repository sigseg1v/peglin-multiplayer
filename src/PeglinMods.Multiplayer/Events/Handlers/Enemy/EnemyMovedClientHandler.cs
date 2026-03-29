namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;
using PeglinMods.Multiplayer.Utility;

public sealed class EnemyMovedClientHandler : IClientHandler<EnemyMovedEvent>
{
    public void Handle(EnemyMovedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"[EnemyMoved] guid={networkEvent.EnemyId} slot {networkEvent.FromSlot} → {networkEvent.ToSlot}");

            var enemyIdentifier = MultiplayerPlugin.Services.Resolve<EnemyIdentifier>();
            var enemy = enemyIdentifier.Find(networkEvent.EnemyId);
            if (enemy != null)
            {
                MultiplayerPlugin.Logger.LogInfo($"[EnemyMoved] Found '{enemy.locKey}' by GUID");
                // TODO: actually move the enemy to the new slot position
            }
            else
            {
                MultiplayerPlugin.Logger.LogWarning($"[EnemyMoved] Could not find enemy guid={networkEvent.EnemyId}");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyMoved handler failed: {e.Message}");
        }
    }
}
