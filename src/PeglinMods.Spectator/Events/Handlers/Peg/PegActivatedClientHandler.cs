namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegActivatedClientHandler : IClientHandler<PegActivatedEvent>
{
    public void Handle(PegActivatedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Peg activated (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
    }
}
