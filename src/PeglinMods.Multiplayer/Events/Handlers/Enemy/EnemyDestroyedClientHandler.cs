namespace PeglinMods.Multiplayer.Events.Handlers.Enemy;

using System;
using PeglinMods.Multiplayer.Events.Network.Enemy;

public sealed class EnemyDestroyedClientHandler : IClientHandler<EnemyDestroyedEvent>
{
    public void Handle(EnemyDestroyedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy {networkEvent.EnemyId} destroyed");
            global::Battle.Enemies.Enemy.OnEnemyDestroyed?.Invoke(null);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemyDestroyed handler failed: {e.Message}");
        }
    }
}
