using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.GameState.Providers;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;

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

    public GameStateSyncService(
        ManualLogSource log,
        IGameEventRegistry registry,
        IMultiplayerMode mode)
    {
        _log = log;
        _registry = registry;
        _mode = mode;
        _mapProvider = new MapStateProvider(log);
        _playerProvider = new PlayerStateProvider(log);
        _enemyProvider = new EnemyStateProvider(log);
        _pegboardProvider = new PegboardStateProvider(log);
        _deckProvider = new DeckStateProvider(log);
        _relicProvider = new RelicStateProvider(log);
    }

    public void SyncAll()
    {
        if (!_mode.IsHosting) return;

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

            _registry.Dispatch(snapshot);
            _log.LogInfo($"SyncAll: sent full state (map={snapshot.Map?.ActiveScene}, enemies={snapshot.Enemies?.Enemies?.Count ?? 0}, pegs={snapshot.Pegboard?.TotalPegCount ?? 0})");
        }
        catch (Exception ex)
        {
            _log.LogError($"SyncAll failed: {ex.Message}");
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
