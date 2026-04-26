using Multipeglin.Events.Network.Deck;

namespace Multipeglin.Events.Handlers.Deck;

public sealed class DeckShuffledServerHandler : IServerHandler<DeckShuffledEvent>
{
    public DeckShuffledEvent Handle(DeckShuffledEvent networkEvent) => networkEvent;
}
