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
                try { peg.PegActivated(playAudio: true, forcePop: false); }
                catch { }
            }

            // Also fire the global delegate for any UI/sound subscribers
            global::Peg.OnPegActivated?.Invoke((global::Peg.PegType)networkEvent.PegType, peg);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
