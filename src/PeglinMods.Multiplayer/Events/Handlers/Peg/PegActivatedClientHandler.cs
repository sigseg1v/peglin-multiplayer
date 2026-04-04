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

            global::Peg peg = null;
            if (!string.IsNullOrEmpty(networkEvent.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(networkEvent.PegGuid);
            }

            if (peg == null || !peg.gameObject.activeSelf) return;

            // Skip bombs entirely — heartbeat handles bomb state (HitCount, detonation).
            // Calling PegActivated on bombs runs full detonation logic.
            if (peg is Bomb) return;

            // Call PegActivated for the visual pop (destruction animation, audio, sprite change)
            // but suppress game logic delegates to prevent attack resolution, relic effects,
            // and status effect application from running on the client.
            var savedOnPegActivated = global::Peg.OnPegActivated;
            var savedOnPegHit = global::Peg.OnPegHit;
            global::Peg.OnPegActivated = null;
            global::Peg.OnPegHit = null;
            try
            {
                peg.PegActivated(playAudio: true, forcePop: false);
            }
            catch { }
            finally
            {
                global::Peg.OnPegActivated = savedOnPegActivated;
                global::Peg.OnPegHit = savedOnPegHit;
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger?.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
