namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class BallPositionClientHandler : IClientHandler<BallPositionEvent>
{
    public void Handle(BallPositionEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo(
                $"BallPosition: pos=({networkEvent.PosX:F2}, {networkEvent.PosY:F2}) vel=({networkEvent.VelX:F2}, {networkEvent.VelY:F2})");
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallPosition handler failed: {e.Message}");
        }
    }
}
