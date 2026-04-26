using System;
using HarmonyLib;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

namespace Multipeglin.Events.Handlers.Peg;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating)
            {
                return;
            }

            global::Peg peg = null;
            if (!string.IsNullOrEmpty(networkEvent.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(networkEvent.PegGuid);
            }

            if (peg == null || !peg.gameObject.activeSelf)
            {
                return;
            }

            // Skip bombs — heartbeat handles bomb state
            if (peg is Bomb)
            {
                return;
            }

            // BouncerPeg never pops — it bounces and accumulates damage over N hits.
            // Previously we SetActive(false)'d it, which hid it after the first hit
            // and prevented the user from perceiving subsequent hits. Heartbeat keeps
            // the bouncer's visual state in sync; nothing to do here.
            if (peg is global::Battle.BouncerPeg)
            {
                return;
            }

            // LongPeg has a two-phase host lifecycle:
            //   (a) PegActivated → _hit=true, _cleared=true, gray "Hit" color, but
            //       collider STAYS enabled. Visually the peg is still there, just gray.
            //   (b) Later (5+ bounces or 0.5s _beingHit timer or end-of-shot hook):
            //       SetActiveStatus(false) → collider off, fade via RemoveIfCleared.
            // The PegActivatedEvent represents phase (a), so the client must mirror
            // the gray visual without disabling the collider or popping the peg.
            // Phase (b) is detected via heartbeat (provider reports IsCleared once
            // the host actually disables the collider) and handled by the applier.
            if (peg is LongPeg longPeg)
            {
                LongPegVisualHelper.ApplyHitVisual(longPeg);
                return;
            }

            // Do the visual pop directly — don't call PegActivated() because
            // it accesses relicManager (null on client pegs → NRE) and runs
            // game logic (relic effects, status effects, attack resolution).
            try
            {
                // Collect coins visually before the peg is popped
                try
                {
                    var overlayField = AccessTools.Field(typeof(global::Peg), "PegCoinOverlayInstance");
                    var overlay = overlayField?.GetValue(peg) as global::Battle.PegBehaviour.PegCoinOverlay;
                    if (overlay != null && overlay.NumCoins > 0)
                    {
                        overlay.CollectCoins();
                    }
                }
                catch { }

                // Mark as cleared
                var clearedField = AccessTools.Field(typeof(global::Peg), "_cleared");
                clearedField?.SetValue(peg, true);

                // Disable colliders so ball passes through
                if (peg is RegularPeg)
                {
                    var disableMethod = AccessTools.Method(typeof(RegularPeg), "DisableRegularColliders");
                    disableMethod?.Invoke(peg, null);
                }

                // Play the destruction animation (scale tween → shrink to nothing)
                if (peg is RegularPeg)
                {
                    var playMethod = AccessTools.Method(typeof(RegularPeg), "PlayDestructionAnimation");
                    playMethod?.Invoke(peg, null);
                }
                else
                {
                    // Fallback for other non-Regular/Bouncer/Long peg types: just deactivate
                    peg.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Logger?.LogWarning($"[PegActivated] Visual pop failed: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
