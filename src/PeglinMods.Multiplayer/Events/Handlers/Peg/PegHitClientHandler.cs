namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent e)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Find the actual peg by GUID and invoke OnPegHit with the real peg reference.
            // This is important for bombs (Bomb.PegActivated uses hit count from OnPegHit),
            // gold coins, and other per-peg effects.
            global::Peg peg = null;
            if (!string.IsNullOrEmpty(e.PegGuid))
            {
                var pegId = MultiplayerPlugin.Services?.TryResolve<PegIdentifier>(out var p) == true ? p : null;
                peg = pegId?.Find(e.PegGuid);
            }

            // Invoke with actual peg reference (null-safe — some subscribers handle null).
            // Skip for bombs — OnPegHit subscribers run game logic (relic effects,
            // battle controller state) that the dumb-canvas client should not execute.
            if (peg != null && !(peg is Bomb))
            {
                global::Peg.OnPegHit?.Invoke((global::Peg.PegType)e.PegType, peg);
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegHit handler failed: {ex.Message}");
        }
    }
}
