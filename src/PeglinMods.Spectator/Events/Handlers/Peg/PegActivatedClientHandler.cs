namespace PeglinMods.Spectator.Events.Handlers.Peg;

using System;
using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Peg activated (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // Peg.OnPegActivated is a public static PegHitEvent(PegType, Peg) delegate
            global::Peg.OnPegActivated?.Invoke((global::Peg.PegType)networkEvent.PegType, null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
