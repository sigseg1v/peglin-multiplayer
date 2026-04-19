namespace Multipeglin.Events.Handlers.Peg;

using System;
using HarmonyLib;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            global::Peg peg = null;
            if (!string.IsNullOrEmpty(networkEvent.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(networkEvent.PegGuid);
            }

            if (peg == null || !peg.gameObject.activeSelf) return;

            // Skip bombs — heartbeat handles bomb state
            if (peg is Bomb) return;

            // BouncerPeg never pops — it bounces and accumulates damage over N hits.
            // Previously we SetActive(false)'d it, which hid it after the first hit
            // and prevented the user from perceiving subsequent hits. Heartbeat keeps
            // the bouncer's visual state in sync; nothing to do here.
            if (peg is global::Battle.BouncerPeg) return;

            // LongPeg pops once on host then lingers ~0.5s before SetActiveStatus(false).
            // Previously we SetActive(false)'d immediately, which made the heartbeat
            // racing against the 0.5s window resurrect the peg every sync — user saw
            // the peg "stuck" after a hit. Instead, mark _cleared=true and let the
            // heartbeat drive the fade (provider now reports IsCleared once host's
            // _hit flag is set, below, so the applier correctly force-pops it).
            if (peg is LongPeg)
            {
                try
                {
                    var clearedField = AccessTools.Field(typeof(global::Peg), "_cleared");
                    clearedField?.SetValue(peg, true);
                }
                catch { }
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
