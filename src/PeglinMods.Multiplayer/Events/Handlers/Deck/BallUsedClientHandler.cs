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

            var orbName = dm.shuffledDeck.Peek()?.name;

            // Try DrawBall — creates PachinkoBall needed for orb display.
            // May fail on subsequent draws because BattleController state machine
            // isn't in the right state (Update is blocked on client).
            try
            {
                dm.DrawBall(null);
                MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] DrawBall succeeded for '{orbName}' ({dm.shuffledDeck.Count} remaining)");
            }
            catch
            {
                // DrawBall failed — manually pop and fire events for deck UI
                var popped = dm.shuffledDeck.Pop();
                DeckManager.onBallUsed?.Invoke(popped);
                MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] DrawBall failed, manual pop for '{popped?.name}' ({dm.shuffledDeck.Count} remaining)");

                // Show the orb via ClientBallRenderer as fallback
                GameState.ClientBallRenderer.Instance?.OnOrbDrawn(popped?.name);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }
}
