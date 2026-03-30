namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.GameState;

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
