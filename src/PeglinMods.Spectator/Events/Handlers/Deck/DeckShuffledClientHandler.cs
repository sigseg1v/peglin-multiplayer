namespace PeglinMods.Spectator.Events.Handlers.Deck;

using System;
using PeglinMods.Spectator.Events.Network.Deck;

public sealed class DeckShuffledClientHandler : IClientHandler<DeckShuffledEvent>
{
    public void Handle(DeckShuffledEvent networkEvent)
    {
        try
        {
            SpectatorPlugin.Logger.LogInfo($"Spectator: Deck shuffled ({networkEvent.DeckSize} cards)");
            // DeckManager.onDeckShuffled is a public static Shuffled delegate
            DeckManager.onDeckShuffled?.Invoke(networkEvent.DeckSize);
        }
        catch (Exception e)
        {
            SpectatorPlugin.Logger.LogWarning($"DeckShuffled handler failed: {e.Message}");
        }
    }
}
