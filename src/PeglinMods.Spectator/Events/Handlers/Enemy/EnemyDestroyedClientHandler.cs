namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyDestroyedClientHandler : IClientHandler<EnemyDestroyedEvent>
{
    public void Handle(EnemyDestroyedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} destroyed");
            global::Battle.Enemies.Enemy.OnEnemyDestroyed?.Invoke(null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemyDestroyed handler failed: {e.Message}");
        }
    }
}
