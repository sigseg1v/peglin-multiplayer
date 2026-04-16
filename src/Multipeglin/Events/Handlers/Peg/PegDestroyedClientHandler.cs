namespace Multipeglin.Events.Handlers.Peg;

using System;
using Multipeglin.Events.Network.Peg;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;

public sealed class PegDestroyedClientHandler : IClientHandler<PegDestroyedEvent>
{
    public void Handle(PegDestroyedEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Find the actual peg on the client by GUID
            global::Peg peg = null;
            if (!string.IsNullOrEmpty(networkEvent.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(networkEvent.PegGuid);
            }

            // Destroy the peg visually
            if (peg != null && peg.gameObject.activeSelf && peg.pegType != global::Peg.PegType.DESTROYED)
            {
                try { peg.DestroyPeg(peg.pegType); }
                catch { peg.gameObject.SetActive(false); }
            }
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegDestroyed handler failed: {e.Message}");
        }
    }
}
