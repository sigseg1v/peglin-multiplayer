namespace Multipeglin.Events.Handlers.Coop;

using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using UnityEngine;
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
            services.TryResolve<Multipeglin.Network.INetworkTransport>(out var transport))
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

        // Hide the host aim line on turn change — the host only sends aim updates
        // during its own turn, so clear any stale line from the previous turn.
        GameState.ClientAimRenderer.Instance?.HideAim();

        // Enable/disable targeting on the client based on whose turn it is
        EnableClientTargeting(IsMyTurn);

        // Clear client target indicator on the host when turns change
        TargetSelectClientHandler.ClearClientTarget();

        if (IsMyTurn)
        {
            TurnMessage = "Your turn! Aim and shoot.";

            // Reset the discard counter so the client UI shows "0/N" at the start
            // of each turn. The game normally resets NumShotsDiscarded in DrawBall,
            // but the client's ball is created locally (not via the game's DrawBall).
            try
            {
                var bc = Object.FindObjectOfType<BattleCtrl>();
                if (bc != null) bc.NumShotsDiscarded = 0;
            }
            catch { }
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

    /// <summary>
    /// Enable or disable enemy targeting on the client so the player can select
    /// which enemy to attack during their aiming phase.
    /// </summary>
    private static void EnableClientTargeting(bool enable)
    {
        try
        {
            var tm = Object.FindObjectOfType<global::Battle.TargetingManager>();
            if (tm == null) return;

            if (enable)
            {
                // Use SPELL targeting type to allow selecting any enemy (including flying)
                tm.SetTargetingStatus(canTarget: true, global::Battle.TargetingType.SPELL);
                tm.AutoSelect();
            }
            else
            {
                tm.SetTargetingStatus(canTarget: false, global::Battle.TargetingType.SINGLE);
            }
        }
        catch (System.Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[TurnChange] EnableClientTargeting failed: {ex.Message}");
        }
    }
}
