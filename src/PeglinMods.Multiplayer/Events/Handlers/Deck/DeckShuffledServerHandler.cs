namespace PeglinMods.Multiplayer.Events.Handlers.Deck;

using PeglinMods.Multiplayer.Events.Network.Deck;

public sealed class DeckShuffledServerHandler : IServerHandler<DeckShuffledEvent>
{
    public DeckShuffledEvent Handle(DeckShuffledEvent networkEvent) => networkEvent;
}
