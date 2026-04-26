using System;
using Multipeglin.Events.Network.Deck;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Deck;

public sealed class BallDrawnClientHandler : IClientHandler<BallDrawnEvent>
{
    public void Handle(BallDrawnEvent e)
    {
        try
        {
            // In coop mode, deck draw events reflect the HOST's active player.
            // Don't modify the client's own deck — heartbeat sync handles it.
            if (UI.LobbyUI.GameStartReceived)
            {
                return;
            }

            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
            {
                return;
            }

            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Drew orb {e.OrbName} (level {e.Level})");

            // Fire onBallDrawn with the next orb so DeckInfoManager can update
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                var nextOrb = dm.shuffledDeck.Peek();
                DeckManager.onBallDrawn?.Invoke(nextOrb);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallDrawn handler failed: {ex.Message}");
        }
    }
}
