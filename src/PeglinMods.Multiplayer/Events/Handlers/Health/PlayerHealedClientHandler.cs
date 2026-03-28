namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class PlayerHealedClientHandler : IClientHandler<PlayerHealedEvent>
{
    public void Handle(PlayerHealedEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnPlayerHealed?.Invoke(networkEvent.Amount);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerHealed handler failed: {e.Message}");
        }
    }
}
