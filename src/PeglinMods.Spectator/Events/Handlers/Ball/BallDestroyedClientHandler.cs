namespace PeglinMods.Spectator.Events.Handlers.Ball;

using System;
using PeglinMods.Spectator.Events.Network.Ball;

public sealed class BallDestroyedClientHandler : IClientHandler<BallDestroyedEvent>
{
    public void Handle(BallDestroyedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo("Spectator: Ball destroyed");
            // PachinkoBall.OnPachinkoBallDestroyed is a public static PachinkoBallDestroyed(PachinkoBall) delegate
            PachinkoBall.OnPachinkoBallDestroyed?.Invoke(null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BallDestroyed handler failed: {e.Message}");
        }
    }
}
