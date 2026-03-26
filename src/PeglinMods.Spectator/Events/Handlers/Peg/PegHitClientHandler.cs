namespace PeglinMods.Spectator.Events.Handlers.Peg;

using System;
using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Peg hit (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // Peg.OnPegHit is a public static PegHitEvent(PegType, Peg) delegate
            global::Peg.OnPegHit?.Invoke((global::Peg.PegType)networkEvent.PegType, null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"PegHit handler failed: {e.Message}");
        }
    }
}
