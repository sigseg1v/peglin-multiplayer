namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;
using BattleCtrl = global::Battle.BattleController;

/// <summary>
/// On client: store the latest turn state so UI can display whose turn it is.
/// </summary>
public sealed class TurnChangeClientHandler : IClientHandler<TurnChangeEvent>
{
    /// <summary>
    /// The most recent turn change event received from the host.
    /// UI code can read this to show turn indicators.
    /// </summary>
    public static TurnChangeEvent LatestTurnState { get; internal set; }

    /// <summary>
    /// Whether it is the local (client) player's turn to aim.
    /// Set by comparing ActiveSlotIndex against the local player's slot.
    /// </summary>
    public static bool IsMyTurn { get; internal set; }

    /// <summary>
    /// Human-readable message for UI overlay (e.g. "Waiting for Player2...").
    /// </summary>
    public static string TurnMessage { get; internal set; } = "";

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
            else if (registry.LocalSlot != null)
            {
                // Client: use the LocalSlot set during GameStartClientHandler
                mySlot = registry.LocalSlot.SlotIndex;
            }
        }

        IsMyTurn = mySlot >= 0 &&
                   networkEvent.ActiveSlotIndex == mySlot &&
                   networkEvent.TurnPhase == nameof(GameState.TurnPhase.PLAYER_AIMING);

        // Reset the client shot flag on any turn change so the client can shoot
        // again when their next turn comes around.
        Patches.MultiplayerClientPatches.ClientShotSentThisTurn = false;

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
        else if (networkEvent.TurnPhase == nameof(GameState.TurnPhase.ALL_DONE)
              || networkEvent.TurnPhase == nameof(GameState.TurnPhase.DAMAGE_PHASE))
        {
            TurnMessage = "";
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
