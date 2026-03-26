namespace PeglinMods.Spectator.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class HealthDepletedClientHandler : IClientHandler<HealthDepletedEvent>
{
    public void Handle(HealthDepletedEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnHealthDepleted?.Invoke();
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"HealthDepleted handler failed: {e.Message}");
        }
    }
}
