namespace PeglinMods.Multiplayer.Events.Handlers.Coop;

using PeglinMods.Multiplayer.Events.Network.Coop;
using PeglinMods.Multiplayer.GameState;

/// <summary>
/// Host broadcasts turn change events to all clients.
/// Also updates local turn UI state so the host sees "Waiting for X..." during other players' turns.
/// </summary>
public sealed class TurnChangeServerHandler : IServerHandler<TurnChangeEvent>
{
    public TurnChangeEvent Handle(TurnChangeEvent networkEvent)
    {
        // Update the same statics that TurnChangeClientHandler uses,
        // so MultiplayerUI can show turn messages on the host too.
        TurnChangeClientHandler.LatestTurnState = networkEvent;

        bool isHostTurn = networkEvent.ActiveSlotIndex == 0
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
