namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyMovedClientHandler : IClientHandler<EnemyMovedEvent>
{
    public void Handle(EnemyMovedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} moved from slot {networkEvent.FromSlot} to {networkEvent.ToSlot}");
    }
}
