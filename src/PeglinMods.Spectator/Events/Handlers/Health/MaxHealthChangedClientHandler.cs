namespace PeglinMods.Spectator.Events.Handlers.Health;

using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class MaxHealthChangedClientHandler : IClientHandler<MaxHealthChangedEvent>
{
    public void Handle(MaxHealthChangedEvent networkEvent)
    {
        PlayerHealthController.OnPlayerMaxHealthChanged?.Invoke(networkEvent.NewMaxHealth);
    }
}
