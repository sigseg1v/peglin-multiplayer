namespace PeglinMods.Spectator.Events.Handlers.Peg;

using System;
using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegDestroyedClientHandler : IClientHandler<PegDestroyedEvent>
{
    public void Handle(PegDestroyedEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Peg destroyed (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // Peg.OnPegDestroyed is a public static PegDestroyed(PegType, Peg) delegate
            global::Peg.OnPegDestroyed?.Invoke((global::Peg.PegType)networkEvent.PegType, null);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"PegDestroyed handler failed: {e.Message}");
        }
    }
}
