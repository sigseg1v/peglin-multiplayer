using System;
using System.Collections;
using BepInEx.Logging;
using Loading;
using PeglinMods.Multiplayer.GameState.Appliers;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
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
    private readonly EnemyIdentifier _enemyId;
    private readonly PegIdentifier _pegId;

    // Buffered latest snapshots — re-applied on scene load
    private FullGameStateSnapshot _latestFull;
    private EnemyStateSnapshot _latestEnemies;
    private PegboardStateSnapshot _latestPegboard;
    private PlayerStateSnapshot _latestPlayer;

    public GameStateApplyService(ManualLogSource log, EnemyIdentifier enemyId, PegIdentifier pegId)
    {
        _log = log;
        _mapApplier = new MapStateApplier(log);
        _playerApplier = new PlayerStateApplier(log);
        _enemyApplier = new EnemyStateApplier(log, enemyId);
        _pegboardApplier = new PegboardStateApplier(log, pegId);
        _deckApplier = new DeckStateApplier(log);
        _relicApplier = new RelicStateApplier(log);
        _enemyId = enemyId;
        _pegId = pegId;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        // Clear GUID registries on scene transition so stale refs don't linger
        _log.LogInfo($"[ApplyService] Scene loaded: '{scene.name}' — clearing GUID registries");
        _enemyId.Clear();
        _pegId.Clear();

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

        // Verify we're still on the same scene (another load might have started)
        var currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != sceneName)
        {
            _log.LogWarning($"[ApplyService] Scene changed during delay: expected '{sceneName}', now on '{currentScene}' — skipping re-apply");
            yield break;
        }

        // If on Battle scene, wait for enemy prefab cache to be populated by BattleController.Awake → LoadEnemyAssets
        if (currentScene == "Battle")
        {
            var cache = AssetLoading.Instance?.EnemyPrefabs;
            int waitFrames = 0;
            while ((cache == null || cache.Count == 0) && waitFrames < 30)
            {
                yield return null;
                waitFrames++;
                cache = AssetLoading.Instance?.EnemyPrefabs;
            }
            if (cache != null && cache.Count > 0)
                _log.LogInfo($"[ApplyService] Enemy prefab cache ready: {cache.Count} entries (waited {waitFrames} frames)");
            else
                _log.LogWarning($"[ApplyService] Enemy prefab cache still empty after {waitFrames} frames — enemies may fail to spawn");
        }

        _log.LogInfo($"[ApplyService] Re-applying buffered state after scene '{sceneName}' loaded");
        DiagnosticLogger.DumpBattleState($"CLIENT_BeforeReapply_{sceneName}");

        if (_latestFull != null)
        {
            // Check that the buffered snapshot's scene matches where we are now.
            // Don't apply a MainMenu snapshot to Battle or vice versa.
            var snapshotScene = _latestFull.Map?.ActiveScene;
            if (!string.IsNullOrEmpty(snapshotScene) &&
                !string.Equals(snapshotScene, currentScene, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning($"[ApplyService] Stale snapshot: snapshot scene='{snapshotScene}', current='{currentScene}' — skipping non-map state");
                // Still apply player state (works on any scene)
                if (_latestFull.Player != null)
                    SafeApply("Player", () => _playerApplier.Apply(_latestFull.Player));
            }
            else
            {
                ApplyNonMapState(_latestFull);
            }
        }
        else
        {
            // Apply individual buffered snapshots
            if (_latestPlayer != null) SafeApply("Player", () => _playerApplier.Apply(_latestPlayer));
            if (_latestEnemies != null) SafeApply("Enemies", () => _enemyApplier.Apply(_latestEnemies));
            if (_latestPegboard != null) SafeApply("Pegboard", () => _pegboardApplier.Apply(_latestPegboard));
        }

        DiagnosticLogger.DumpBattleState($"CLIENT_AfterReapply_{sceneName}");
    }

    public void ApplyAll(FullGameStateSnapshot snapshot)
    {
        var scene = SceneManager.GetActiveScene().name;
        _log.LogInfo($"[ApplyService] Applying full game state... (current scene={scene}, " +
            $"host scene={snapshot.Map?.ActiveScene}, enemies={snapshot.Enemies?.Enemies?.Count ?? 0}, pegs={snapshot.Pegboard?.TotalPegCount ?? 0})");
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

        // Post-apply consistency check
        VerifyConsistency(snapshot);
    }

    private void VerifyConsistency(FullGameStateSnapshot snapshot)
    {
        try
        {
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != "Battle") return;

            // Check enemy count
            if (snapshot.Enemies?.Enemies != null)
            {
                var em = UnityEngine.Object.FindObjectOfType<EnemyManager>();
                int clientEnemies = em?.Enemies?.Count ?? 0;
                int hostEnemies = snapshot.Enemies.Enemies.Count;
                if (clientEnemies != hostEnemies)
                {
                    _log.LogWarning($"[Consistency] ENEMY COUNT MISMATCH: host={hostEnemies}, client={clientEnemies}");
                    _enemyId.DumpState("ConsistencyCheck");
                }
                else
                {
                    _log.LogInfo($"[Consistency] Enemies OK: {clientEnemies} match");
                }
            }

            // Check peg count (active pegs)
            if (snapshot.Pegboard?.Pegs != null)
            {
                var livePegs = UnityEngine.Object.FindObjectsOfType<Peg>(false); // active only
                int clientPegs = livePegs?.Length ?? 0;
                int hostActivePegs = 0;
                foreach (var p in snapshot.Pegboard.Pegs)
                    if (!p.IsDestroyed) hostActivePegs++;

                if (System.Math.Abs(clientPegs - hostActivePegs) > 5) // allow small variance
                {
                    _log.LogWarning($"[Consistency] PEG COUNT MISMATCH: host_active={hostActivePegs}, client_active={clientPegs}");
                }
                else
                {
                    _log.LogInfo($"[Consistency] Pegs OK: host={hostActivePegs}, client={clientPegs}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning($"[Consistency] Check failed: {ex.Message}");
        }
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
