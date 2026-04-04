using System;
using System.Collections.Generic;
using BepInEx.Logging;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.GameState.Providers;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;

namespace PeglinMods.Multiplayer.GameState;

public class GameStateSyncService : IGameStateSyncService
{
    private readonly ManualLogSource _log;
    private readonly IGameEventRegistry _registry;
    private readonly IMultiplayerMode _mode;
    private readonly MapStateProvider _mapProvider;
    private readonly PlayerStateProvider _playerProvider;
    private readonly EnemyStateProvider _enemyProvider;
    private readonly PegboardStateProvider _pegboardProvider;
    private readonly DeckStateProvider _deckProvider;
    private readonly RelicStateProvider _relicProvider;
    private readonly CoopStateManager _coopStateManager;

    public GameStateSyncService(
        ManualLogSource log,
        IGameEventRegistry registry,
        IMultiplayerMode mode,
        EnemyIdentifier enemyId,
        PegIdentifier pegId,
        OrbIdentifier orbId,
        CoopStateManager coopStateManager = null)
    {
        _log = log;
        _registry = registry;
        _mode = mode;
        _mapProvider = new MapStateProvider(log);
        _playerProvider = new PlayerStateProvider(log);
        _enemyProvider = new EnemyStateProvider(log, enemyId);
        _pegboardProvider = new PegboardStateProvider(log, pegId);
        _deckProvider = new DeckStateProvider(log, orbId);
        _relicProvider = new RelicStateProvider(log);
        _coopStateManager = coopStateManager;
    }

    public void SyncAll(string trigger = null)
    {
        if (!_mode.IsHosting) return;

        var tag = string.IsNullOrEmpty(trigger) ? "" : $"[{trigger}] ";

        try
        {
            var snapshot = new FullGameStateSnapshot
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Map = _mapProvider.Capture(),
                Player = _playerProvider.Capture(),
                Deck = _deckProvider.Capture(),
                Relics = _relicProvider.Capture(),
                Enemies = _enemyProvider.Capture(),
                Pegboard = _pegboardProvider.Capture(),
            };

            // Add co-op multi-player data if available
            if (_coopStateManager != null && _coopStateManager.TotalPlayerCount > 0)
            {
                snapshot.ActivePlayerSlot = _coopStateManager.ActivePlayerSlot;
                snapshot.TotalPlayerCount = _coopStateManager.TotalPlayerCount;
                snapshot.PlayerSummaries = new List<CoopPlayerSummary>();

                foreach (var kvp in _coopStateManager.PlayerStates)
                {
                    var ps = kvp.Value;
                    // For the active player, use live singleton data
                    var isActive = kvp.Key == _coopStateManager.ActivePlayerSlot;
                    snapshot.PlayerSummaries.Add(new CoopPlayerSummary
                    {
                        SlotIndex = ps.SlotIndex,
                        PlayerName = ps.PlayerName,
                        ChosenClass = ps.ChosenClass,
                        CurrentHealth = isActive ? (snapshot.Player?.CurrentHealth ?? ps.CurrentHealth) : ps.CurrentHealth,
                        MaxHealth = isActive ? (snapshot.Player?.MaxHealth ?? ps.MaxHealth) : ps.MaxHealth,
                        Gold = isActive ? (snapshot.Player?.Gold ?? ps.Gold) : ps.Gold,
                        HasShotThisRound = ps.HasShotThisRound,
                    });
                }
            }

            _registry.Dispatch(snapshot);
            _log.LogInfo($"{tag}SyncAll: sent full state (map={snapshot.Map?.ActiveScene}, enemies={snapshot.Enemies?.Enemies?.Count ?? 0}, pegs={snapshot.Pegboard?.TotalPegCount ?? 0}, players={snapshot.TotalPlayerCount})");

            // Only dump verbose diagnostics for non-heartbeat syncs to reduce log noise
            if (trigger == null || !trigger.StartsWith("HEARTBEAT"))
                DiagnosticLogger.DumpBattleState("HOST_SyncAll");
        }
        catch (Exception ex)
        {
            _log.LogError($"{tag}SyncAll failed: {ex.Message}");
        }
    }

    public void SyncMap()
    {
        if (!_mode.IsHosting) return;
        var state = _mapProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncMap: scene={state?.ActiveScene}, floor={state?.TotalFloorCount}");
    }

    public void SyncPegboard()
    {
        if (!_mode.IsHosting) return;
        var state = _pegboardProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncPegboard: {state?.TotalPegCount} pegs ({state?.CritPegCount} crit, {state?.BombPegCount} bomb, {state?.ResetPegCount} reset)");
    }

    public void SyncEnemies()
    {
        if (!_mode.IsHosting) return;
        var state = _enemyProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncEnemies: {state?.Enemies?.Count ?? 0} enemies, battleState={state?.BattleStateName}");
    }

    public void SyncPlayer()
    {
        if (!_mode.IsHosting) return;
        var state = _playerProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncPlayer: hp={state?.CurrentHealth}/{state?.MaxHealth}, gold={state?.Gold}, effects={state?.StatusEffects?.Count ?? 0}");
    }

    public void SyncDeck()
    {
        if (!_mode.IsHosting) return;
        var state = _deckProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncDeck: {state?.DeckSize} orbs in complete deck, {state?.BattleDeck?.Count ?? 0} in battle deck");
    }

    public void SyncRelics()
    {
        if (!_mode.IsHosting) return;
        var state = _relicProvider.Capture();
        if (state != null) _registry.Dispatch(state);
        _log.LogInfo($"SyncRelics: {state?.TotalRelicCount} relics");
    }
}
