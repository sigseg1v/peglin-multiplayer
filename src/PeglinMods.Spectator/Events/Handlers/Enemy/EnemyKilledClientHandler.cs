namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyKilledClientHandler : IClientHandler<EnemyKilledEvent>
{
    public void Handle(EnemyKilledEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} ({networkEvent.LocKey}) killed");
            global::Battle.Enemies.Enemy.OnEnemyKilled?.Invoke(networkEvent.LocKey);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemyKilled handler failed: {e.Message}");
        }
    }
}
