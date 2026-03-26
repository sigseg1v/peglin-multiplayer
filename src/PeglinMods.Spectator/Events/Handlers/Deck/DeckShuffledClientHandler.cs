namespace PeglinMods.Spectator.Events.Handlers.Deck;

using PeglinMods.Spectator.Events.Network.Deck;

public sealed class DeckShuffledClientHandler : IClientHandler<DeckShuffledEvent>
{
    public void Handle(DeckShuffledEvent networkEvent)
    {
        SpectatorPlugin.Logger.LogInfo($"Spectator: Deck shuffled ({networkEvent.DeckSize} cards)");
    }
}
