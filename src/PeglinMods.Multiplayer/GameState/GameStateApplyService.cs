using System;
using System.Collections;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState.Appliers;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.GameState;

/// <summary>
/// Receives state snapshots from the network and routes them to the correct applier.
/// Buffers snapshots and re-applies after scene loads (with delay for initialization).
/// This ensures state is applied even if it arrives before the target scene loads.
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

    // Buffered latest snapshots — re-applied on scene load
    private FullGameStateSnapshot _latestFull;
    private EnemyStateSnapshot _latestEnemies;
    private PegboardStateSnapshot _latestPegboard;
    private PlayerStateSnapshot _latestPlayer;

    public GameStateApplyService(ManualLogSource log)
    {
        _log = log;
        _mapApplier = new MapStateApplier(log);
        _playerApplier = new PlayerStateApplier(log);
        _enemyApplier = new EnemyStateApplier(log);
        _pegboardApplier = new PegboardStateApplier(log);
        _deckApplier = new DeckStateApplier(log);
        _relicApplier = new RelicStateApplier(log);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        // Re-apply buffered state after scene initialization completes.
        // Delay 3 frames so BattleController.Awake/Start and MapController finish first.
        var dispatcher = MultiplayerPlugin.Services?.TryResolve<MainThreadDispatcher>(out var d) == true ? d : null;
        if (dispatcher != null)
        {
            dispatcher.StartCoroutine(ApplyBufferedAfterDelay(scene.name));
        }
    }

    private IEnumerator ApplyBufferedAfterDelay(string sceneName)
    {
        // Wait for scene initialization (BattleController.Awake, EnemyManager.Initialize, etc.)
        yield return null;
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.5f);

        _log.LogInfo($"[ApplyService] Re-applying buffered state after scene '{sceneName}' loaded");

        if (_latestFull != null)
        {
            ApplyNonMapState(_latestFull);
        }
        else
        {
            // Apply individual buffered snapshots
            if (_latestPlayer != null) SafeApply("Player", () => _playerApplier.Apply(_latestPlayer));
            if (_latestEnemies != null) SafeApply("Enemies", () => _enemyApplier.Apply(_latestEnemies));
            if (_latestPegboard != null) SafeApply("Pegboard", () => _pegboardApplier.Apply(_latestPegboard));
        }
    }

    public void ApplyAll(FullGameStateSnapshot snapshot)
    {
        _log.LogInfo("[ApplyService] Applying full game state...");
        _latestFull = snapshot;

        try
        {
            // Map applier handles scene transitions — always apply immediately
            if (snapshot.Map != null)
            {
                _mapApplier.Apply(snapshot.Map);
            }

            // Apply non-map state only if we're on the right scene
            ApplyNonMapState(snapshot);

            _log.LogInfo("[ApplyService] Full game state applied.");
        }
        catch (Exception ex)
        {
            _log.LogError($"[ApplyService] ApplyAll failed: {ex.Message}");
        }
    }

    private void ApplyNonMapState(FullGameStateSnapshot snapshot)
    {
        var currentScene = SceneManager.GetActiveScene().name;

        // Player state works on any scene
        if (snapshot.Player != null)
            SafeApply("Player", () => _playerApplier.Apply(snapshot.Player));

        // Enemy/pegboard/deck/relic state only makes sense in Battle
        if (currentScene != "Battle")
        {
            _log.LogInfo($"[ApplyService] Not on Battle scene (on '{currentScene}'), enemy/peg state buffered for later.");
            return;
        }

        if (snapshot.Enemies != null) SafeApply("Enemies", () => _enemyApplier.Apply(snapshot.Enemies));
        if (snapshot.Pegboard != null) SafeApply("Pegboard", () => _pegboardApplier.Apply(snapshot.Pegboard));
        if (snapshot.Deck != null) SafeApply("Deck", () => _deckApplier.Apply(snapshot.Deck));
        if (snapshot.Relics != null) SafeApply("Relics", () => _relicApplier.Apply(snapshot.Relics));
    }

    // --- Individual snapshot appliers (called from per-type client handlers) ---

    public void ApplyMapState(MapStateSnapshot snapshot)
    {
        SafeApply("Map", () => _mapApplier.Apply(snapshot));
    }

    public void ApplyPlayerState(PlayerStateSnapshot snapshot)
    {
        _latestPlayer = snapshot;
        SafeApply("Player", () => _playerApplier.Apply(snapshot));
    }

    public void ApplyEnemyState(EnemyStateSnapshot snapshot)
    {
        _latestEnemies = snapshot;
        if (SceneManager.GetActiveScene().name != "Battle")
        {
            _log.LogInfo("[ApplyService] Buffered enemy state (not on Battle scene)");
            return;
        }
        SafeApply("Enemies", () => _enemyApplier.Apply(snapshot));
    }

    public void ApplyPegboardState(PegboardStateSnapshot snapshot)
    {
        _latestPegboard = snapshot;
        if (SceneManager.GetActiveScene().name != "Battle")
        {
            _log.LogInfo("[ApplyService] Buffered pegboard state (not on Battle scene)");
            return;
        }
        SafeApply("Pegboard", () => _pegboardApplier.Apply(snapshot));
    }

    public void ApplyDeckState(DeckStateSnapshot snapshot)
    {
        SafeApply("Deck", () => _deckApplier.Apply(snapshot));
    }

    public void ApplyRelicState(RelicStateSnapshot snapshot)
    {
        SafeApply("Relics", () => _relicApplier.Apply(snapshot));
    }

    private void SafeApply(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.LogError($"[ApplyService] {name} failed: {ex.Message}"); }
    }
}
