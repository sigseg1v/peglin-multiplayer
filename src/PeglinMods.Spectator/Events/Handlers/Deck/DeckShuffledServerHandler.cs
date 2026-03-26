namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class DeckShuffledServerHandler : IServerHandler<DeckShuffledEvent>
{
    public DeckShuffledEvent Handle(DeckShuffledEvent networkEvent) => networkEvent;
}
