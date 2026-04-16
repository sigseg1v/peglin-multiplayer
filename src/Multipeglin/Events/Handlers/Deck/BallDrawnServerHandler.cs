namespace Multipeglin.Events.Handlers.Deck;

using Multipeglin.Events.Network.Deck;

public sealed class BallDrawnServerHandler : IServerHandler<BallDrawnEvent>
{
    public BallDrawnEvent Handle(BallDrawnEvent networkEvent) => networkEvent;
}
