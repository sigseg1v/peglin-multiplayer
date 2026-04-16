namespace Multipeglin.Events.Handlers.Ball;

using System;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;

public sealed class MultiballSpawnedClientHandler : IClientHandler<MultiballSpawnedEvent>
{
    public void Handle(MultiballSpawnedEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger?.LogInfo($"[MultiballSpawned] pos=({e.PosX:F1},{e.PosY:F1}), vel=({e.VelX:F1},{e.VelY:F1}), orb={e.OrbName}");

            ClientBallRenderer.Instance?.OnMultiballSpawned(e.PosX, e.PosY, e.VelX, e.VelY, e.OrbName);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"MultiballSpawned handler failed: {ex.Message}");
        }
    }
}
