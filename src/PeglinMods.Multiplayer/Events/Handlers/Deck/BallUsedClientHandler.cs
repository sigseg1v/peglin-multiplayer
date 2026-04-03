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

            // Call DrawBall directly — this creates the PachinkoBall (needed for
            // orb display rotation/position), pops from shuffledDeck, fires onBallUsed,
            // and triggers DeckInfoManager's orb draw animation.
            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm != null && dm.shuffledDeck != null && dm.shuffledDeck.Count > 0)
            {
                var orbName = dm.shuffledDeck.Peek()?.name;
                dm.DrawBall(null);
                MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Called DrawBall for '{orbName}' ({dm.shuffledDeck.Count} remaining)");
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] shuffledDeck empty, cannot draw for '{e.OrbName}'");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }
}
