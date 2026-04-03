namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using System;
using DG.Tweening;
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

            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();

            // If the shuffle animation is still running, force-complete it so DrawNextOrb
            // doesn't fight the plunger animation for the same transform.
            if (dim != null && DeckInfoManager.animating)
                ForceCompleteShuffleAnimation(dim);

            int displayCountBefore = dim?.displayOrbs?.Count ?? -1;

            // Pop from shuffledDeck (data)
            var popped = dm.shuffledDeck.Pop();
            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Popped '{popped?.name}' ({dm.shuffledDeck.Count} remaining)");

            // Fire onBallUsed — DeckInfoManager subscribes to this in OnEnable and calls
            // DrawNextOrb, which pops from _displayOrbs and starts the plunger/scale animation.
            DeckManager.onBallUsed?.Invoke(popped);

            int displayCountAfter = dim?.displayOrbs?.Count ?? -1;
            bool delegateFired = displayCountAfter < displayCountBefore;
            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] displayOrbs: {displayCountBefore} -> {displayCountAfter} (delegate {(delegateFired ? "fired" : "did NOT fire")})");

            // Only call DrawNextOrb manually if the delegate did NOT handle it.
            // Previously we ALWAYS called it, causing a double-pop that broke the animation chain.
            if (!delegateFired && dim != null && displayCountAfter > 0)
            {
                MultiplayerPlugin.Logger?.LogWarning("[BallUsed] Delegate did not fire — calling DrawNextOrb manually as fallback");
                var drawMethod = AccessTools.Method(typeof(DeckInfoManager), "DrawNextOrb");
                drawMethod?.Invoke(dim, new object[] { popped });
            }

            // Show orb at aimer via ClientBallRenderer
            GameState.ClientBallRenderer.Instance?.OnOrbDrawn(popped?.name);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"BallUsed handler failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Force-complete the shuffle animation (plunger drop → create sprites → plunger rise).
    /// Without this, DrawNextOrb's plunger animation fights with the shuffle's plunger animation.
    /// </summary>
    private static void ForceCompleteShuffleAnimation(DeckInfoManager dim)
    {
        try
        {
            var plungerParentField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
            var plungerParent = plungerParentField?.GetValue(dim) as Transform;
            if (plungerParent != null)
            {
                // Complete with callbacks so PlungerPlungeComplete → ShuffleAnimComplete chain fires
                int safety = 0;
                while (DeckInfoManager.animating && safety++ < 5)
                {
                    DOTween.Complete(plungerParent, true);
                }
            }
            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Force-completed shuffle animation (still animating: {DeckInfoManager.animating})");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] Failed to complete shuffle anim: {ex.Message}");
        }
    }
}
