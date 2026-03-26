namespace PeglinMods.Spectator.Events.Handlers.Ball;

using System;
using PeglinMods.Spectator.Events.Network.Ball;
using UnityEngine;

public sealed class ShotFiredClientHandler : IClientHandler<ShotFiredEvent>
{
    public void Handle(ShotFiredEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Shot fired at ({networkEvent.AimX:F2}, {networkEvent.AimY:F2})");
            // PachinkoBall.OnShotFired is a public static PachinkoBallFired(Vector2) delegate
            PachinkoBall.OnShotFired?.Invoke(new Vector2(networkEvent.AimX, networkEvent.AimY));
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"ShotFired handler failed: {e.Message}");
        }
    }
}
