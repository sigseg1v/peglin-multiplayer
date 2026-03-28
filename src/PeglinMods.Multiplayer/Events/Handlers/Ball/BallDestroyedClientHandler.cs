namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using System;
using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class BallDestroyedClientHandler : IClientHandler<BallDestroyedEvent>
{
    public void Handle(BallDestroyedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo("Multiplayer: Ball destroyed");
            // PachinkoBall.OnPachinkoBallDestroyed is a public static PachinkoBallDestroyed(PachinkoBall) delegate
            PachinkoBall.OnPachinkoBallDestroyed?.Invoke(null);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallDestroyed handler failed: {e.Message}");
        }
    }
}
