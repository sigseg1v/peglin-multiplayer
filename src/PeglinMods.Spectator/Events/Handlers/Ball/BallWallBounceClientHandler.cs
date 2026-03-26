namespace PeglinMods.Spectator.Events.Handlers.Ball;

using System;
using PeglinMods.Spectator.Events.Network.Ball;
using UnityEngine;

public sealed class BallWallBounceClientHandler : IClientHandler<BallWallBounceEvent>
{
    public void Handle(BallWallBounceEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Ball bounced off wall at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // PachinkoBall.OnPachinkoBallWallBounce is a public static PachinkoBallWallBounce(Vector3) delegate
            PachinkoBall.OnPachinkoBallWallBounce?.Invoke(new Vector3(networkEvent.PosX, networkEvent.PosY, networkEvent.PosZ));
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BallWallBounce handler failed: {e.Message}");
        }
    }
}
