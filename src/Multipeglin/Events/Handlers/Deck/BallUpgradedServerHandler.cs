
using Multipeglin.Events.Network.Deck;

namespace Multipeglin.Events.Handlers.Deck;
public sealed class BallUpgradedServerHandler : IServerHandler<BallUpgradedEvent>
{
    public BallUpgradedEvent Handle(BallUpgradedEvent networkEvent) => networkEvent;
}
