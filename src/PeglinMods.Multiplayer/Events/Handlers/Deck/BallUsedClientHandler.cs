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
            if (dm == null || dm.shuffledDeck == null || dm.shuffledDeck.Count == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] shuffledDeck empty, cannot draw for '{e.OrbName}'");
                return;
            }

            // Pop from shuffledDeck (data)
            var popped = dm.shuffledDeck.Pop();
            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Popped '{popped?.name}' ({dm.shuffledDeck.Count} remaining)");

            // Fire onBallUsed for game systems
            DeckManager.onBallUsed?.Invoke(popped);

            // Trigger DeckInfoManager's draw animation from _displayOrbs
            try
            {
                var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();
                if (dim?.displayOrbs != null && dim.displayOrbs.Count > 0)
                {
                    var drawMethod = AccessTools.Method(typeof(DeckInfoManager), "DrawNextOrb");
                    drawMethod?.Invoke(dim, new object[] { popped });
                }
            }
            catch (System.Exception ex2)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] DrawNextOrb failed: {ex2.Message}");
            }

            // Show orb at aimer via ClientBallRenderer
            GameState.ClientBallRenderer.Instance?.OnOrbDrawn(popped?.name);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }
}
