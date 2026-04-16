namespace Multipeglin.Events.Handlers.Ball;

using System;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;

public sealed class BallPositionClientHandler : IClientHandler<BallPositionEvent>
{
    public void Handle(BallPositionEvent e)
    {
        try
        {
            ClientBallRenderer.Instance?.UpdateBallPosition(e.PosX, e.PosY, e.VelX, e.VelY, e.Timestamp);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallPosition handler failed: {ex.Message}");
        }
    }
}
