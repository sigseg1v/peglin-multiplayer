using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;

namespace PeglinMods.Multiplayer.GameState;

public enum TurnPhase
{
    WAITING_FOR_PLAYERS,
    PLAYER_AIMING,
    SHOT_IN_FLIGHT,
    ALL_DONE,
    DAMAGE_PHASE,
}

/// <summary>
/// Host-side manager that tracks turn order during co-op battles.
/// Each round, every player gets one shot in order. Once all have shot,
/// the round ends and the damage phase begins.
/// </summary>
public class TurnManager
{
    private readonly ManualLogSource _log;
    private readonly CoopStateManager _coopState;

    /// <summary>Ordered list of slot indices for the current round.</summary>
    public List<int> TurnOrder { get; private set; } = new List<int>();

    /// <summary>Index into TurnOrder for the current turn.</summary>
    public int CurrentTurnIndex { get; private set; }

    /// <summary>Current phase of the turn system.</summary>
    public TurnPhase Phase { get; private set; } = TurnPhase.WAITING_FOR_PLAYERS;

    /// <summary>How many full rounds have been played this battle.</summary>
    public int RoundNumber { get; private set; }

    /// <summary>The slot index of the player whose turn it currently is, or -1 if none.</summary>
    public int CurrentPlayerSlot =>
        TurnOrder.Count > 0 && CurrentTurnIndex >= 0 && CurrentTurnIndex < TurnOrder.Count
            ? TurnOrder[CurrentTurnIndex]
            : -1;

    /// <summary>Whether all players have shot this round.</summary>
    public bool AllPlayersHaveShot =>
        TurnOrder.Count > 0 &&
        TurnOrder.All(slot =>
        {
            var state = _coopState.GetPlayerState(slot);
            return state != null && state.HasShotThisRound;
        });

    /// <summary>Static snapshot of the latest turn state, readable by UI on any thread.</summary>
    public static TurnSnapshot LatestSnapshot { get; private set; }

    public TurnManager(ManualLogSource log, CoopStateManager coopState)
    {
        _log = log;
        _coopState = coopState;
    }

    /// <summary>
    /// Build the turn order from the current player states.
    /// Called at battle start or when players change.
    /// </summary>
    public void BuildTurnOrder()
    {
        TurnOrder = _coopState.PlayerStates.Keys.OrderBy(k => k).ToList();
        _log.LogInfo($"[TurnManager] Built turn order: [{string.Join(", ", TurnOrder)}]");
    }

    /// <summary>
    /// Begin a new round: reset HasShotThisRound for all players,
    /// set CurrentTurnIndex to 0, and enter PLAYER_AIMING phase.
    /// </summary>
    public void StartNewRound()
    {
        if (TurnOrder.Count == 0)
            BuildTurnOrder();

        RoundNumber++;
        CurrentTurnIndex = 0;

        foreach (var slot in TurnOrder)
        {
            var state = _coopState.GetPlayerState(slot);
            if (state != null)
                state.HasShotThisRound = false;
        }

        Phase = TurnPhase.PLAYER_AIMING;
        UpdateSnapshot();

        _log.LogInfo($"[TurnManager] Round {RoundNumber} started. First turn: slot {CurrentPlayerSlot}");
    }

    /// <summary>
    /// Mark the current player's shot as started (ball in flight).
    /// </summary>
    public void MarkShotFired()
    {
        var slot = CurrentPlayerSlot;
        if (slot < 0) return;

        var state = _coopState.GetPlayerState(slot);
        if (state != null)
            state.HasShotThisRound = true;

        Phase = TurnPhase.SHOT_IN_FLIGHT;
        UpdateSnapshot();

        _log.LogInfo($"[TurnManager] Slot {slot} shot fired, phase -> SHOT_IN_FLIGHT");
    }

    /// <summary>
    /// Current shot has resolved. Advance to the next player or ALL_DONE.
    /// </summary>
    public void AdvanceTurn()
    {
        CurrentTurnIndex++;

        if (CurrentTurnIndex >= TurnOrder.Count)
        {
            Phase = TurnPhase.ALL_DONE;
            UpdateSnapshot();
            _log.LogInfo($"[TurnManager] All players have shot. Phase -> ALL_DONE");
            return;
        }

        Phase = TurnPhase.PLAYER_AIMING;
        UpdateSnapshot();

        _log.LogInfo($"[TurnManager] Advanced to slot {CurrentPlayerSlot} (index {CurrentTurnIndex}/{TurnOrder.Count})");
    }

    /// <summary>
    /// Enter the damage phase (enemies attack all players).
    /// </summary>
    public void EnterDamagePhase()
    {
        Phase = TurnPhase.DAMAGE_PHASE;
        UpdateSnapshot();
        _log.LogInfo("[TurnManager] Phase -> DAMAGE_PHASE");
    }

    /// <summary>
    /// Check if the given slot is the one currently taking a turn.
    /// </summary>
    public bool IsSlotsTurn(int slotIndex) =>
        Phase == TurnPhase.PLAYER_AIMING && CurrentPlayerSlot == slotIndex;

    /// <summary>
    /// Check if the local player's turn is active.
    /// Host is always slot 0; clients are never the host.
    /// </summary>
    public bool IsLocalPlayerTurn(bool isHost)
    {
        if (Phase != TurnPhase.PLAYER_AIMING) return false;
        return isHost && CurrentPlayerSlot == 0;
    }


    /// <summary>
    /// Remove a player from the turn order (e.g., on disconnect).
    /// If the removed player was the current turn player, advances to the next
    /// player or marks ALL_DONE if no players remain.
    /// Returns true if the removed player was the active turn player.
    /// </summary>
    public bool RemovePlayer(int slotIndex)
    {
        var beforeIndex = CurrentTurnIndex;
        var beforePhase = Phase;
        var beforeCount = TurnOrder.Count;
        var beforeSlot = CurrentPlayerSlot;

        int removeIdx = TurnOrder.IndexOf(slotIndex);
        if (removeIdx < 0)
        {
            _log.LogInfo($"[TurnManager] RemovePlayer: slot {slotIndex} not in TurnOrder [{string.Join(", ", TurnOrder)}], no-op");
            return false;
        }

        bool wasCurrentTurn = (removeIdx == CurrentTurnIndex) &&
            (Phase == TurnPhase.PLAYER_AIMING || Phase == TurnPhase.SHOT_IN_FLIGHT);
        TurnOrder.RemoveAt(removeIdx);

        _log.LogInfo($"[TurnManager] RemovePlayer: removed slot {slotIndex} (was at index {removeIdx}). " +
            $"Before: turnIdx={beforeIndex}, phase={beforePhase}, count={beforeCount}, activeSlot={beforeSlot}");

        if (TurnOrder.Count == 0)
        {
            Phase = TurnPhase.ALL_DONE;
            CurrentTurnIndex = 0;
            UpdateSnapshot();
            _log.LogInfo("[TurnManager] RemovePlayer: no players left, phase -> ALL_DONE");
            return wasCurrentTurn;
        }

        if (wasCurrentTurn)
        {
            if (CurrentTurnIndex >= TurnOrder.Count)
            {
                Phase = TurnPhase.ALL_DONE;
                UpdateSnapshot();
                _log.LogInfo("[TurnManager] RemovePlayer: removed last-in-order player, phase -> ALL_DONE");
            }
            else
            {
                Phase = TurnPhase.PLAYER_AIMING;
                UpdateSnapshot();
                _log.LogInfo($"[TurnManager] RemovePlayer: advancing to slot {CurrentPlayerSlot} (index {CurrentTurnIndex}/{TurnOrder.Count})");
            }
        }
        else if (removeIdx < CurrentTurnIndex)
        {
            CurrentTurnIndex--;
            UpdateSnapshot();
            _log.LogInfo($"[TurnManager] RemovePlayer: adjusted CurrentTurnIndex {beforeIndex} -> {CurrentTurnIndex}");
        }
        else
        {
            UpdateSnapshot();
            _log.LogInfo("[TurnManager] RemovePlayer: removed future-turn player, no index change");
        }

        _log.LogInfo($"[TurnManager] RemovePlayer result: turnIdx={CurrentTurnIndex}, phase={Phase}, " +
            $"count={TurnOrder.Count}, activeSlot={CurrentPlayerSlot}, order=[{string.Join(", ", TurnOrder)}]");

        return wasCurrentTurn;
    }
    /// <summary>
    /// Reset the turn manager for a new battle.
    /// </summary>
    public void Reset()
    {
        TurnOrder.Clear();
        CurrentTurnIndex = 0;
        RoundNumber = 0;
        Phase = TurnPhase.WAITING_FOR_PLAYERS;
        UpdateSnapshot();
        _log.LogInfo("[TurnManager] Reset");
    }

    /// <summary>
    /// Get the player name for the currently active slot.
    /// </summary>
    public string GetCurrentPlayerName()
    {
        var slot = CurrentPlayerSlot;
        if (slot < 0) return "";
        var state = _coopState.GetPlayerState(slot);
        return state?.PlayerName ?? $"Player {slot}";
    }

    private void UpdateSnapshot()
    {
        LatestSnapshot = new TurnSnapshot
        {
            ActiveSlotIndex = CurrentPlayerSlot,
            ActivePlayerName = GetCurrentPlayerName(),
            Phase = Phase,
            RoundNumber = RoundNumber,
        };
    }
}

/// <summary>
/// Immutable snapshot of turn state, safe to read from UI threads.
/// </summary>
public class TurnSnapshot
{
    public int ActiveSlotIndex { get; set; } = -1;
    public string ActivePlayerName { get; set; } = "";
    public TurnPhase Phase { get; set; } = TurnPhase.WAITING_FOR_PLAYERS;
    public int RoundNumber { get; set; }
}
