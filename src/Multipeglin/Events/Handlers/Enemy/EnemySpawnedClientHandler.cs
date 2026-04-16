namespace Multipeglin.Events.Handlers.Enemy;

using System;
using global::Battle.Enemies;
using Multipeglin.Events.Network.Enemy;

public sealed class EnemySpawnedClientHandler : IClientHandler<EnemySpawnedEvent>
{
    public void Handle(EnemySpawnedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Enemy spawned {networkEvent.LocKey} at slot {networkEvent.SlotIndex} (HP: {networkEvent.CurrentHealth}/{networkEvent.MaxHealth})");

            // Enemy spawning is handled by EnemyStateApplier during periodic sync.
            // Don't invoke OnEnemySpawned with null — subscribers dereference it.
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"EnemySpawned handler failed: {e.Message}");
        }
    }
}
