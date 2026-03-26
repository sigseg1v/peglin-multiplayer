namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class BallDestroyedClientHandler : IClientHandler<BallDestroyedEvent>
{
    public void Handle(BallDestroyedEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo("Spectator: Ball destroyed");
    }
}
