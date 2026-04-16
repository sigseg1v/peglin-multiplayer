namespace Multipeglin.Events.Handlers.Ball;

using System;
using Multipeglin.Events.Network.Ball;
using Multipeglin.GameState;

public sealed class BallDestroyedClientHandler : IClientHandler<BallDestroyedEvent>
{
    public void Handle(BallDestroyedEvent e)
    {
        try
        {
            ClientBallRenderer.Instance?.OnBallDestroyed();
            // Don't invoke OnPachinkoBallDestroyed with null — subscribers dereference it
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallDestroyed handler failed: {ex.Message}");
        }
    }
}
