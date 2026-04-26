
using BepInEx.Logging;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;

namespace Multipeglin.Events.Handlers.Coop;
/// <summary>
/// Host broadcasts turn change events to all clients.
/// Also updates local turn UI state so the host sees "Waiting for X..." during other players' turns.
/// </summary>
public sealed class TurnChangeServerHandler : IServerHandler<TurnChangeEvent>
{
    private static readonly ManualLogSource _log = Logger.CreateLogSource("TurnChangeServer");

    public TurnChangeEvent Handle(TurnChangeEvent networkEvent)
    {
        _log.LogInfo($"[TurnChangeServer] Broadcasting turn change: slot={networkEvent.ActiveSlotIndex}, player={networkEvent.ActivePlayerName}, phase={networkEvent.TurnPhase}, round={networkEvent.RoundNumber}");

        // When it's a client's turn, hide the host's native prediction trajectory.
        // PredictionManager lives on BattleController (not the ball), so deactivating
        // the ball doesn't hide its line renderer and bounce indicators.
        if (networkEvent.ActiveSlotIndex > 0
            && networkEvent.TurnPhase == nameof(TurnPhase.PLAYER_AIMING))
        {
            try
            {
                var bc = UnityEngine.Object.FindObjectOfType<global::Battle.BattleController>();
                bc?.PredictionManager?.PlayerFired();
            }
            catch (System.Exception ex)
            {
                _log.LogWarning($"[TurnChangeServer] Failed to hide prediction: {ex.Message}");
            }
        }

        // Hide the remote aim line on any turn change — the new active player
        // will send fresh aim updates once they start aiming.
        ClientAimRenderer.Instance?.HideAim();

        // Update the same statics that TurnChangeClientHandler uses,
        // so MultiplayerUI can show turn messages on the host too.
        TurnChangeClientHandler.LatestTurnState = networkEvent;

        var isHostTurn = networkEvent.ActiveSlotIndex == 0
            && networkEvent.TurnPhase == nameof(TurnPhase.PLAYER_AIMING);
        TurnChangeClientHandler.IsMyTurn = isHostTurn;

        if (isHostTurn)
        {
            TurnChangeClientHandler.TurnMessage = "Your turn! Aim and shoot.";
        }
        else if (networkEvent.TurnPhase == nameof(TurnPhase.PLAYER_AIMING))
        {
            TurnChangeClientHandler.TurnMessage = $"Waiting for {networkEvent.ActivePlayerName}...";
        }
        else if (networkEvent.TurnPhase == nameof(TurnPhase.SHOT_IN_FLIGHT))
        {
            TurnChangeClientHandler.TurnMessage = $"{networkEvent.ActivePlayerName}'s shot in flight...";
        }
        else if (networkEvent.TurnPhase == nameof(TurnPhase.ALL_DONE))
        {
            TurnChangeClientHandler.TurnMessage = "All players have shot.";
        }
        else if (networkEvent.TurnPhase == nameof(TurnPhase.DAMAGE_PHASE))
        {
            TurnChangeClientHandler.TurnMessage = "Enemies attacking...";
        }
        else
        {
            TurnChangeClientHandler.TurnMessage = "";
        }

        return networkEvent;
    }
}
