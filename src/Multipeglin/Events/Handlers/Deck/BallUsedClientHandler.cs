using System;
using DG.Tweening;
using HarmonyLib;
using Multipeglin.Events.Network.Deck;
using Multipeglin.Multiplayer;
using UnityEngine;

namespace Multipeglin.Events.Handlers.Deck;

public sealed class BallUsedClientHandler : IClientHandler<BallUsedEvent>
{
    public void Handle(BallUsedEvent e)
    {
        try
        {
            // In coop mode, BallUsed events reflect the HOST's active player's deck actions.
            // Don't pop from the client's own deck — the heartbeat sync handles deck state.
            if (UI.LobbyUI.GameStartReceived)
            {
                return;
            }

            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
            {
                return;
            }

            var dms = Resources.FindObjectsOfTypeAll<DeckManager>();
            var dm = dms.Length > 0 ? dms[0] : null;
            if (dm == null || dm.shuffledDeck == null || dm.shuffledDeck.Count == 0)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] shuffledDeck empty, cannot draw for '{e.OrbName}'");
                return;
            }

            var dim = UnityEngine.Object.FindObjectOfType<DeckInfoManager>();

            // Force-complete shuffle animation if still running
            if (dim != null && DeckInfoManager.animating)
            {
                ForceCompleteShuffleAnimation(dim);
            }

            // Pop from shuffledDeck (data)
            var popped = dm.shuffledDeck.Pop();
            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Popped '{popped?.name}' ({dm.shuffledDeck.Count} remaining)");

            // === DECK UI: directly set the active orb WITHOUT animation ===
            // On the client, activePachinkoBall doesn't exist (DrawBall is blocked).
            // DeckInfoManager.DrawNextOrb's animation chain crashes at PreSetActive/SetActive
            // because they call UpdateAimTrackingTransform which accesses activePachinkoBall.
            // We bypass the entire chain and directly set the current orb.
            //
            // Do NOT fire DeckManager.onBallUsed — DeckInfoManager subscribes to it and
            // would trigger DrawNextOrb, which crashes for the same reason.
            if (dim != null && dim.displayOrbs != null && dim.displayOrbs.Count > 0)
            {
                SetActiveOrbDirectly(dim);
            }
            else
            {
                MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] displayOrbs empty ({dim?.displayOrbs?.Count ?? -1}) — cannot set active orb");
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
    /// Directly pop the next display orb and place it at the active orb position.
    /// Bypasses DrawNextOrb → BallDrawFinished → EndCurrentOrbScale chain entirely,
    /// because that chain depends on activePachinkoBall which doesn't exist on the client.
    /// </summary>
    private static void SetActiveOrbDirectly(DeckInfoManager dim)
    {
        try
        {
            var nextOrb = dim.displayOrbs.Pop();

            // Kill any ongoing tweens on the plunger (from shuffle or previous draw)
            var plungerField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
            var plunger = plungerField?.GetValue(dim) as Transform;
            if (plunger != null)
            {
                DOTween.Kill(plunger);
            }

            // Destroy the current active orb if one exists
            var currentOrbField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrb");
            var oldOrb = currentOrbField?.GetValue(dim) as GameObject;
            if (oldOrb != null)
            {
                DOTween.Kill(oldOrb.transform);
                UnityEngine.Object.Destroy(oldOrb);
            }

            // Clear _nextOrb to prevent stale animation callbacks from using it
            var nextOrbField = AccessTools.Field(typeof(DeckInfoManager), "_nextOrb");
            nextOrbField?.SetValue(dim, null);

            // Kill any tweens on the new orb
            DOTween.Kill(nextOrb.transform);

            // Get the orb height BEFORE unparenting (need it for plunger shift)
            var spriteRenderer = nextOrb.GetComponent<SpriteRenderer>();
            var orbHeight = spriteRenderer != null ? spriteRenderer.bounds.size.y : 0f;

            // Unparent from plunger so world position is independent
            nextOrb.transform.SetParent(null);

            // Move to the active orb display position (instant, no animation)
            var displayPosField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrbDisplayPos");
            var displayPos = displayPosField?.GetValue(dim) as Transform;
            if (displayPos != null)
            {
                nextOrb.transform.position = displayPos.position;
            }

            nextOrb.transform.localScale = Vector3.one * 0.85f; // ACTIVE_ORB_DISPLAY_HEIGHT

            // Move the plunger up by the orb height so remaining orbs shift up to fill the gap
            // (same as what DrawNextOrb does on the host)
            if (plunger != null && orbHeight > 0f)
            {
                plunger.position += Vector3.up * orbHeight;
            }

            // Set level ring sprite
            var uod = nextOrb.GetComponentInChildren<PeglinUI.OrbDisplay.UpcomingOrbDisplay>();
            var levelIdx = 0;
            if (uod?.attack != null)
            {
                levelIdx = Mathf.Clamp(uod.attack.Level - 1, 0, 2);
            }

            var levelRingField = AccessTools.Field(typeof(DeckInfoManager), "_currentOrbLevelRingRenderer");
            var levelSpritesField = AccessTools.Field(typeof(DeckInfoManager), "_orbLevelDisplaySprites");
            var levelRing = levelRingField?.GetValue(dim) as SpriteRenderer;
            var levelSprites = levelSpritesField?.GetValue(dim) as Sprite[];
            if (levelRing != null && levelSprites != null && levelIdx < levelSprites.Length)
            {
                levelRing.sprite = levelSprites[levelIdx];
            }

            // Activate the level frame mask
            if (uod?.mainOrbLevelFrameMask != null)
            {
                uod.mainOrbLevelFrameMask.SetActive(true);
            }

            // Set as current orb
            currentOrbField?.SetValue(dim, nextOrb);

            // Enable the animator
            var animator = nextOrb.GetComponentInChildren<Animator>();
            animator?.speed = 1f;

            // Fire events for any listeners
            DeckInfoManager.onActiveOrbScaleStarted?.Invoke(nextOrb);
            DeckInfoManager.onActiveOrbScaleCompleted?.Invoke();
            DeckInfoManager.populatingDisplayOrb = false;
            DeckInfoManager.animating = false;

            MultiplayerPlugin.Logger?.LogInfo($"[BallUsed] Set active orb directly at ({displayPos?.position.x:F1},{displayPos?.position.y:F1}), displayOrbs remaining: {dim.displayOrbs.Count}");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[BallUsed] SetActiveOrbDirectly failed: {ex.Message}");
            DeckInfoManager.populatingDisplayOrb = false;
        }
    }

    /// <summary>
    /// Force-complete the shuffle animation so it doesn't fight with our direct orb placement.
    /// </summary>
    private static void ForceCompleteShuffleAnimation(DeckInfoManager dim)
    {
        try
        {
            var plungerParentField = AccessTools.Field(typeof(DeckInfoManager), "_plungerParent");
            var plungerParent = plungerParentField?.GetValue(dim) as Transform;
            if (plungerParent != null)
            {
                var safety = 0;
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
