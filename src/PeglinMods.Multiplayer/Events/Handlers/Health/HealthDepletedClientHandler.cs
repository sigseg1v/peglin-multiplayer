namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Health;

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
            MultiplayerPlugin.Logger.LogWarning($"HealthDepleted handler failed: {e.Message}");
        }
    }
}
