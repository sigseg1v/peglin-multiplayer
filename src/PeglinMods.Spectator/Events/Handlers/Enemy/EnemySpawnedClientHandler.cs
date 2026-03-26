namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemySpawnedClientHandler : IClientHandler<EnemySpawnedEvent>
{
    public void Handle(EnemySpawnedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy spawned {networkEvent.LocKey} at slot {networkEvent.SlotIndex}");
    }
}
