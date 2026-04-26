using System;
using Multipeglin.Events.Network.Ball;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Ball;

public sealed class BallWallBounceClientHandler : IClientHandler<BallWallBounceEvent>
{
    public void Handle(BallWallBounceEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Ball bounced off wall at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // PachinkoBall.OnPachinkoBallWallBounce is a public static PachinkoBallWallBounce(Vector3) delegate
            PachinkoBall.OnPachinkoBallWallBounce?.Invoke(new Vector3(networkEvent.PosX, networkEvent.PosY, networkEvent.PosZ));
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallWallBounce handler failed: {e.Message}");
        }
    }
}
