namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using PeglinMods.Multiplayer.Events.Network.Deck;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

public sealed class BallUsedClientHandler : IClientHandler<BallUsedEvent>
{
    public void Handle(BallUsedEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Used orb {e.OrbName}");

            // Pop from shuffledDeck and fire onBallUsed so DeckInfoManager updates the visual display
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                var popped = dm.shuffledDeck.Pop();
                DeckManager.onBallUsed?.Invoke(popped);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }
}
