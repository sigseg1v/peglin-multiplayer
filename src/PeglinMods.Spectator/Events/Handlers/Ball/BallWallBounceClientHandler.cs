namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class BallWallBounceClientHandler : IClientHandler<BallWallBounceEvent>
{
    public void Handle(BallWallBounceEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Ball bounced off wall at ({networkEvent.PosX:F2}, {networkEvent.PosY:F2})");
    }
}
