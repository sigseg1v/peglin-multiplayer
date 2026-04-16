namespace Multipeglin.Events.Handlers.Deck;

using Multipeglin.Events.Network.Deck;

public sealed class BallUsedServerHandler : IServerHandler<BallUsedEvent>
{
    public BallUsedEvent Handle(BallUsedEvent networkEvent) => networkEvent;
}
