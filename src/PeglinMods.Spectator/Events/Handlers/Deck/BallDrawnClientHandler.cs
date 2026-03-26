namespace PeglinMods.Spectator.Events.Handlers.Deck;

using System;
using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallDrawnClientHandler : IClientHandler<BallDrawnEvent>
{
    public void Handle(BallDrawnEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Drew orb {networkEvent.OrbName} (level {networkEvent.Level})");
            // DeckManager.onBallDrawn requires a GameObject reference we don't have on the client - log only
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"BallDrawn handler failed: {e.Message}");
        }
    }
}
