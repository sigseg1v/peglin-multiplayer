namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUpgradedServerHandler : IServerHandler<BallUpgradedEvent>
{
    public BallUpgradedEvent Handle(BallUpgradedEvent networkEvent) => networkEvent;
}
