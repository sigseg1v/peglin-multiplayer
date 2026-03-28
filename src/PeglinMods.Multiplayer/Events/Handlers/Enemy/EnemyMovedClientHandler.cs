namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyMovedClientHandler : IClientHandler<EnemyMovedEvent>
{
    public void Handle(EnemyMovedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy {networkEvent.EnemyId} moved from slot {networkEvent.FromSlot} to {networkEvent.ToSlot}");
            // Enemy slot movement requires complex scene manipulation - log only for now
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyMoved handler failed: {e.Message}");
        }
    }
}
