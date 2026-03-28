namespace PeglinMods.Multiplayer.Events.Handlers.Peg;

using System;
using PeglinMods.Multiplayer.Events.Network.Peg;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        try
        {
            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Peg activated (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
            // Peg.OnPegActivated is a public static PegHitEvent(PegType, Peg) delegate
            global::Peg.OnPegActivated?.Invoke((global::Peg.PegType)networkEvent.PegType, null);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"PegActivated handler failed: {e.Message}");
        }
    }
}
