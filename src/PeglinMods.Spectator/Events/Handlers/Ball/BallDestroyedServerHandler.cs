namespace PeglinMods.Spectator.Events.Handlers.Ball;

using PeglinMods.Spectator.Events.Network.Ball;

public sealed class BallDestroyedServerHandler : IServerHandler<BallDestroyedEvent>
{
    public BallDestroyedEvent Handle(BallDestroyedEvent networkEvent) => networkEvent;
}
