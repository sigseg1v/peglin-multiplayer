namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallDrawnClientHandler : IClientHandler<BallDrawnEvent>
{
    public void Handle(BallDrawnEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Drew orb {networkEvent.OrbName} (level {networkEvent.Level})");
            // DeckManager.onBallDrawn requires a GameObject reference we don't have on the client - log only
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallDrawn handler failed: {e.Message}");
        }
    }
}
