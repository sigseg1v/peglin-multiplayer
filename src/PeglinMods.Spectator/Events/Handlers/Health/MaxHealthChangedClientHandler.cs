namespace PeglinMods.Spectator.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class MaxHealthChangedClientHandler : IClientHandler<MaxHealthChangedEvent>
{
    public void Handle(MaxHealthChangedEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnPlayerMaxHealthChanged?.Invoke(networkEvent.NewMaxHealth);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"MaxHealthChanged handler failed: {e.Message}");
        }
    }
}
