using Multipeglin.Events.Network.Deck;

namespace Multipeglin.Events.Handlers.Deck;

public sealed class BallUsedServerHandler : IServerHandler<BallUsedEvent>
{
    public BallUsedEvent Handle(BallUsedEvent networkEvent) => networkEvent;
}
