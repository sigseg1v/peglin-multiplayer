using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Appliers;
using PeglinMods.Multiplayer.GameState.Snapshots;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Receives state snapshots from the network and routes them to the correct applier.
/// Inverse of GameStateSyncService.
/// </summary>
public class GameStateApplyService
{
    private readonly ManualLogSource _log;
    private readonly MapStateApplier _mapApplier;
    private readonly PlayerStateApplier _playerApplier;
    private readonly EnemyStateApplier _enemyApplier;
    private readonly PegboardStateApplier _pegboardApplier;
    private readonly DeckStateApplier _deckApplier;
    private readonly RelicStateApplier _relicApplier;

    public GameStateApplyService(ManualLogSource log)
    {
        _log = log;
        _mapApplier = new MapStateApplier(log);
        _playerApplier = new PlayerStateApplier(log);
        _enemyApplier = new EnemyStateApplier(log);
        _pegboardApplier = new PegboardStateApplier(log);
        _deckApplier = new DeckStateApplier(log);
        _relicApplier = new RelicStateApplier(log);
    }

    public void ApplyAll(FullGameStateSnapshot snapshot)
    {
        _log.LogInfo("[ApplyService] Applying full game state...");
        try
        {
            if (snapshot.Map != null) _mapApplier.Apply(snapshot.Map);
            if (snapshot.Player != null) _playerApplier.Apply(snapshot.Player);
            if (snapshot.Enemies != null) _enemyApplier.Apply(snapshot.Enemies);
            if (snapshot.Pegboard != null) _pegboardApplier.Apply(snapshot.Pegboard);
            if (snapshot.Deck != null) _deckApplier.Apply(snapshot.Deck);
            if (snapshot.Relics != null) _relicApplier.Apply(snapshot.Relics);
            _log.LogInfo("[ApplyService] Full game state applied successfully.");
        }
        catch (Exception ex)
        {
            _log.LogError($"[ApplyService] ApplyAll failed: {ex.Message}");
        }
    }

    public void ApplyMapState(MapStateSnapshot snapshot)
    {
        try { _mapApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyMapState failed: {ex.Message}"); }
    }

    public void ApplyPlayerState(PlayerStateSnapshot snapshot)
    {
        try { _playerApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyPlayerState failed: {ex.Message}"); }
    }

    public void ApplyEnemyState(EnemyStateSnapshot snapshot)
    {
        try { _enemyApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyEnemyState failed: {ex.Message}"); }
    }

    public void ApplyPegboardState(PegboardStateSnapshot snapshot)
    {
        try { _pegboardApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyPegboardState failed: {ex.Message}"); }
    }

    public void ApplyDeckState(DeckStateSnapshot snapshot)
    {
        try { _deckApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyDeckState failed: {ex.Message}"); }
    }

    public void ApplyRelicState(RelicStateSnapshot snapshot)
    {
        try { _relicApplier.Apply(snapshot); }
        catch (Exception ex) { _log.LogError($"[ApplyService] ApplyRelicState failed: {ex.Message}"); }
    }
}
