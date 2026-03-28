namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;

public sealed class PegDestroyedClientHandler : IClientHandler<PegDestroyedEvent>
{
    public void Handle(PegDestroyedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Peg destroyed (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // Peg.OnPegDestroyed is a public static PegDestroyed(PegType, Peg) delegate
            global::Peg.OnPegDestroyed?.Invoke((global::Peg.PegType)networkEvent.PegType, null);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegDestroyed handler failed: {e.Message}");
        }
    }
}
