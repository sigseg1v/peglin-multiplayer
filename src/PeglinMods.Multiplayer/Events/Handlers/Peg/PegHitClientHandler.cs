namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;
using PeglinMods.Multiplayer.Multiplayer;

public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent networkEvent)
    {
        try
        {
            var mode = MultiplayerPlugin.Services?.TryResolve<IMultiplayerMode>(out var m) == true ? m : null;
            if (mode == null || !mode.IsSpectating) return;

            // Don't invoke OnPegHit with null peg — subscribers dereference it and NRE.
            // The visual peg effects are handled by PegActivatedClientHandler instead.
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegHit handler failed: {e.Message}");
        }
    }
}
