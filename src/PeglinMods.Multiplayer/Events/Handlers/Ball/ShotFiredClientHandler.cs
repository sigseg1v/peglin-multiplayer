namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.GameState;
using UnityEngine;

public sealed class ShotFiredClientHandler : IClientHandler<ShotFiredEvent>
{
    public void Handle(ShotFiredEvent e)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Shot fired at ({e.AimX:F2}, {e.AimY:F2})");

            // Spawn visual ball on client
            ClientBallRenderer.Instance?.OnShotFired(e.AimX, e.AimY);

            // Hide aim line
            ClientAimRenderer.Instance?.HideAim();

            PachinkoBall.OnShotFired?.Invoke(new Vector2(e.AimX, e.AimY));
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"ShotFired handler failed: {ex.Message}");
        }
    }
}
