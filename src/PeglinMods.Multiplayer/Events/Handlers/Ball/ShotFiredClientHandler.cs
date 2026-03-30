namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

public sealed class ShotFiredClientHandler : IClientHandler<ShotFiredEvent>
{
    public void Handle(ShotFiredEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger?.LogInfo($"[ShotFired] orb={e.OrbName}, aim=({e.AimX:F2},{e.AimY:F2}), spawn=({e.SpawnX:F1},{e.SpawnY:F1})");

            // Spawn visual ball on client with correct orb sprite
            ClientBallRenderer.Instance?.OnShotFired(e.AimX, e.AimY);

            // Hide aim line
            ClientAimRenderer.Instance?.HideAim();
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"ShotFired handler failed: {ex.Message}");
        }
    }
}
