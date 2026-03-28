namespace PeglinMods.Multiplayer.Events.Handlers.Ball;

using PeglinMods.Multiplayer.Events.Network.Ball;

public sealed class BallDestroyedServerHandler : IServerHandler<BallDestroyedEvent>
{
    public BallDestroyedEvent Handle(BallDestroyedEvent networkEvent) => networkEvent;
}
