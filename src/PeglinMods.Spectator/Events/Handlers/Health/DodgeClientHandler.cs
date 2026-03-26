namespace PeglinMods.Spectator.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Spectator.Events.Network.Health;

public sealed class DodgeClientHandler : IClientHandler<DodgeEvent>
{
    public void Handle(DodgeEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnDodge?.Invoke(networkEvent.DodgeInfo);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"Dodge handler failed: {e.Message}");
        }
    }
}
