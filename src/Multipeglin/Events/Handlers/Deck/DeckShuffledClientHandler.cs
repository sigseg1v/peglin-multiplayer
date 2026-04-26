using System;
using Multipeglin.Events.Network.Deck;

namespace Multipeglin.Events.Handlers.Deck;

public sealed class DeckShuffledClientHandler : IClientHandler<DeckShuffledEvent>
{
    public void Handle(DeckShuffledEvent networkEvent)
    {
        try
        {
            // In coop, deck shuffles are host-side only. Don't shuffle client's deck.
            if (UI.LobbyUI.GameStartReceived)
            {
                return;
            }

            MultiplayerPlugin.Logger.LogInfo($"Multiplayer: Deck shuffled ({networkEvent.DeckSize} cards)");
            // DeckManager.onDeckShuffled is a public static Shuffled delegate
            DeckManager.onDeckShuffled?.Invoke(networkEvent.DeckSize);
        }
        catch (Exception e)
        {
            MultiplayerPlugin.Logger.LogWarning($"DeckShuffled handler failed: {e.Message}");
        }
    }
}
