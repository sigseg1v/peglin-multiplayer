namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class ShotFiredClientHandler : IClientHandler<ShotFiredEvent>
{
    public void Handle(ShotFiredEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Shot fired at ({networkEvent.AimX:F2}, {networkEvent.AimY:F2})");
    }
}
