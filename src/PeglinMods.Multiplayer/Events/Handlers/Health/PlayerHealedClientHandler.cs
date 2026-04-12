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
            // During native post-battle rewards, the client's health is managed locally.
            if (Coop.CoopRewardState.ClientInNativeRewardPhase) return;

            PlayerHealthController.OnPlayerHealed?.Invoke(networkEvent.Amount);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerHealed handler failed: {e.Message}");
        }
    }
}
