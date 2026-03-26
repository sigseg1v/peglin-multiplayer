namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class HealthDepletedClientHandler : IClientHandler<HealthDepletedEvent>
{
    public void Handle(HealthDepletedEvent networkEvent)
    {
        PlayerHealthController.OnHealthDepleted?.Invoke();
    }
}
