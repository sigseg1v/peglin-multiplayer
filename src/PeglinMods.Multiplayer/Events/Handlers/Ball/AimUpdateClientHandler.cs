using System;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.GameState;

namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

public sealed class AimUpdateClientHandler : IClientHandler<AimUpdateEvent>
{
    public void Handle(AimUpdateEvent e)
    {
        try
        {
            ClientAimRenderer.Instance?.UpdateAim(e.AimX, e.AimY, e.SpawnX, e.SpawnY);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"AimUpdate handler failed: {ex.Message}");
        }
    }
}
