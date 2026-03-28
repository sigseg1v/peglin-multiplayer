namespace PeglinMods.Multiplayer.Events.Handlers.Health;

using System;
using global::Battle;
using PeglinMods.Multiplayer.Events.Network.Health;

public sealed class PlayerDamagedClientHandler : IClientHandler<PlayerDamagedEvent>
{
    public void Handle(PlayerDamagedEvent networkEvent)
    {
        try
        {
            PlayerHealthController.OnPlayerDamaged?.Invoke(networkEvent.Damage);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PlayerDamaged handler failed: {e.Message}");
        }
    }
}
