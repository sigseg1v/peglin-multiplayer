namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using System;
using global::Battle.Enemies;
using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemySpawnedClientHandler : IClientHandler<EnemySpawnedEvent>
{
    public void Handle(EnemySpawnedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy spawned {networkEvent.LocKey} at slot {networkEvent.SlotIndex} (HP: {networkEvent.CurrentHealth}/{networkEvent.MaxHealth})");

            // Enemy spawning on client is complex - the host's scene transition should trigger
            // local spawning. Just invoke the delegate with null for logging/UI purposes.
            global::Battle.Enemies.Enemy.OnEnemySpawned?.Invoke(null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"EnemySpawned handler failed: {e.Message}");
        }
    }
}
