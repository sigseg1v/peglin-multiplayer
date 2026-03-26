namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyMovedClientHandler : IClientHandler<EnemyMovedEvent>
{
    public void Handle(EnemyMovedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} moved from slot {networkEvent.FromSlot} to {networkEvent.ToSlot}");
            // Enemy slot movement requires complex scene manipulation - log only for now
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemyMoved handler failed: {e.Message}");
        }
    }
}
