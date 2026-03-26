namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class BallUsedServerHandler : IServerHandler<BallUsedEvent>
{
    public BallUsedEvent Handle(BallUsedEvent networkEvent) => networkEvent;
}
