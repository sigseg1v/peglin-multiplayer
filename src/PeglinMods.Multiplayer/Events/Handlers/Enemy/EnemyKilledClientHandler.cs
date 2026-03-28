namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyKilledClientHandler : IClientHandler<EnemyKilledEvent>
{
    public void Handle(EnemyKilledEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy {networkEvent.EnemyId} ({networkEvent.LocKey}) killed");
            global::Battle.Enemies.Enemy.OnEnemyKilled?.Invoke(networkEvent.LocKey);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyKilled handler failed: {e.Message}");
        }
    }
}
