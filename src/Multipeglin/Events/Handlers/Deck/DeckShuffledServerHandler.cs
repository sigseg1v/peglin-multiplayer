namespace Multipeglin.Events.Handlers.Deck;

using Multipeglin.Events.Network.Deck;

public sealed class DeckShuffledServerHandler : IServerHandler<DeckShuffledEvent>
{
    public DeckShuffledEvent Handle(DeckShuffledEvent networkEvent) => networkEvent;
}
