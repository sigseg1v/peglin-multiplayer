namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class BallUpgradedServerHandler : IServerHandler<BallUpgradedEvent>
{
    public BallUpgradedEvent Handle(BallUpgradedEvent networkEvent) => networkEvent;
}
