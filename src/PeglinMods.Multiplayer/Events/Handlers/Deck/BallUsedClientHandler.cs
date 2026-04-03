namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using HarmonyLib;
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

            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm?.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                var popped = dm.shuffledDeck.Pop();
                MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Popped '{popped?.name}' from shuffledDeck ({dm.shuffledDeck.Count} remaining), firing onBallUsed");
                DeckManager.onBallUsed?.Invoke(popped);

                // Show the orb at the aimer position during aiming phase
                GameState.ClientBallRenderer.Instance?.OnOrbDrawn(popped?.name);
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] shuffledDeck empty, cannot pop for '{e.OrbName}'");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }
}
