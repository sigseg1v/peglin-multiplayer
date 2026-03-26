namespace PeglinMods.Spectator.Events.Handlers.Enemy;

using PeglinMods.Spectator.Events.Network.Enemy;

public sealed class EnemyDestroyedClientHandler : IClientHandler<EnemyDestroyedEvent>
{
    public void Handle(EnemyDestroyedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Enemy {networkEvent.EnemyId} destroyed");
    }
}
