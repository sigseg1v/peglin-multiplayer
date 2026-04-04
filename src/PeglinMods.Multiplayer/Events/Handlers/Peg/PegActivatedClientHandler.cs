namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Find the actual peg on the client by GUID and trigger visual activation
            global::Peg peg = null;
            if (!string.IsNullOrEmpty(networkEvent.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(networkEvent.PegGuid);
            }

            if (peg != null && peg.gameObject.activeSelf)
            {
                // Skip PegActivated for bombs — it runs full game logic (increments
                // HitCount, triggers detonation, chain-explodes nearby pegs).
                // Bomb state is synced authoritatively via the heartbeat snapshot.
                if (peg is Bomb)
                {
                    MultiplayerPlugin.Logger?.LogInfo($"[PegActivated] Skipping PegActivated for bomb {networkEvent.PegGuid} — heartbeat handles bomb state");
                }
                else
                {
                    try { peg.PegActivated(playAudio: true, forcePop: false); }
                    catch { }
                }
            }

            // Fire the global delegate for UI/sound subscribers (skip for bombs
            // to prevent relic triggers and battle controller side effects)
            if (!(peg is Bomb))
                global::Peg.OnPegActivated?.Invoke((global::Peg.PegType)networkEvent.PegType, peg);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
