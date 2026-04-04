namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using HarmonyLib;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;
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

            // Do the visual pop directly — don't call PegActivated() because
            // it accesses relicManager (null on client pegs → NRE) and runs
            // game logic (relic effects, status effects, attack resolution).
            try
            {
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
                    // Fallback for non-RegularPeg types: just deactivate
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
