using System;
using global::Battle;
using Multipeglin.Events.Network.Health;

namespace Multipeglin.Events.Handlers.Health;

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
