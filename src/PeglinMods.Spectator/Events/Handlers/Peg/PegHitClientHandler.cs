namespace PeglinMods.Spectator.Events.Handlers.Peg;

using PeglinMods.Spectator.Events.Network.Peg;

public sealed class PegHitClientHandler : IClientHandler<PegHitEvent>
{
    public void Handle(PegHitEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Peg hit (type {networkEvent.PegType}) at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
    }
}
