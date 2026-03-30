namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.GameState;

public sealed class BallDestroyedClientHandler : IClientHandler<BallDestroyedEvent>
{
    public void Handle(BallDestroyedEvent e)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo("Multiplayer: Ball destroyed");
            ClientBallRenderer.Instance?.OnBallDestroyed();
            PachinkoBall.OnPachinkoBallDestroyed?.Invoke(null);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallDestroyed handler failed: {ex.Message}");
        }
    }
}
