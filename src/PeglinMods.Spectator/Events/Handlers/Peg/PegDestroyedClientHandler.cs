namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegDestroyedClientHandler : IClientHandler<PegDestroyedEvent>
{
    public void Handle(PegDestroyedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Peg destroyed (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
    }
}
