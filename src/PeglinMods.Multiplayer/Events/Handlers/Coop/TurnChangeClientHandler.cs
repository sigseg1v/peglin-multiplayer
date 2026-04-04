namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;

/// <summary>
/// On client: store the latest turn state so UI can display whose turn it is.
/// </summary>
public sealed class TurnChangeClientHandler : IClientHandler<TurnChangeEvent>
{
    /// <summary>
    /// The most recent turn change event received from the host.
    /// UI code can read this to show turn indicators.
    /// </summary>
    public static TurnChangeEvent LatestTurnState { get; private set; }

    /// <summary>
    /// Whether it is the local (client) player's turn to aim.
    /// Set by comparing ActiveSlotIndex against the local player's slot.
    /// </summary>
    public static bool IsMyTurn { get; private set; }

    /// <summary>
    /// Human-readable message for UI overlay (e.g. "Waiting for Player2...").
    /// </summary>
    public static string TurnMessage { get; private set; } = "";

    public void Handle(TurnChangeEvent networkEvent)
    {
        LatestTurnState = networkEvent;

        // Determine if it's this client's turn
        var services = MultiplayerPlugin.Services;
        int mySlot = -1;

        if (services != null &&
            services.TryResolve<Multiplayer.PlayerRegistry>(out var registry) &&
            services.TryResolve<PeglinMods.Multiplayer.Network.INetworkTransport>(out var transport))
        {
            if (transport.IsHost)
            {
                // Host processes turns locally via TurnManager, but still updates static state
                mySlot = 0;
            }
            else
            {
                // Client: find our slot from the registry using our local peer info
                // On the client side, peerId -1 represents us (we are the local peer)
                var hostSlot = registry.GetHostSlot();
                // Clients are not the host, so check all non-host slots
                foreach (var slot in registry.GetAllSlots())
                {
                    if (!slot.IsHost)
                    {
                        // On the client, there's only one non-host slot that is "us"
                        // The client only knows about itself and the host
                        mySlot = slot.SlotIndex;
                        break;
                    }
                }
            }
        }

        IsMyTurn = mySlot >= 0 &&
                   networkEvent.ActiveSlotIndex == mySlot &&
                   networkEvent.TurnPhase == nameof(GameState.TurnPhase.PLAYER_AIMING);

        if (IsMyTurn)
        {
            TurnMessage = "Your turn! Aim and shoot.";
        }
        else if (networkEvent.TurnPhase == nameof(GameState.TurnPhase.PLAYER_AIMING))
        {
            TurnMessage = $"Waiting for {networkEvent.ActivePlayerName}...";
        }
        else if (networkEvent.TurnPhase == nameof(GameState.TurnPhase.SHOT_IN_FLIGHT))
        {
            TurnMessage = $"{networkEvent.ActivePlayerName}'s shot in flight...";
        }
        else if (networkEvent.TurnPhase == nameof(GameState.TurnPhase.ALL_DONE))
        {
            TurnMessage = "All players have shot.";
        }
        else if (networkEvent.TurnPhase == nameof(GameState.TurnPhase.DAMAGE_PHASE))
        {
            TurnMessage = "Enemies attacking...";
        }
        else
        {
            TurnMessage = "";
        }

        MultiplayerPlugin.Logger?.LogInfo(
            $"[TurnChange] Slot {networkEvent.ActiveSlotIndex} ({networkEvent.ActivePlayerName}), " +
            $"phase={networkEvent.TurnPhase}, round={networkEvent.RoundNumber}, isMyTurn={IsMyTurn}");
    }
}
