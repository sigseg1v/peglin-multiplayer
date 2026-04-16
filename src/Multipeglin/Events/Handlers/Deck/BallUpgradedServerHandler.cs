namespace Multipeglin.Events.Handlers.Deck;

using Multipeglin.Events.Network.Deck;

public sealed class BallUpgradedServerHandler : IServerHandler<BallUpgradedEvent>
{
    public BallUpgradedEvent Handle(BallUpgradedEvent networkEvent) => networkEvent;
}
