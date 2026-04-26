
using Multipeglin.Events.Network.Deck;

namespace Multipeglin.Events.Handlers.Deck;
public sealed class BallDrawnServerHandler : IServerHandler<BallDrawnEvent>
{
    public BallDrawnEvent Handle(BallDrawnEvent networkEvent) => networkEvent;
}
