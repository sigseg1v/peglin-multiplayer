using System;
using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine.SceneManagement;

namespace PeglinMods.Multiplayer.Events.Subscriptions;

/// <summary>
/// Subscribes to key game events and triggers full or partial state sync.
/// State sync captures the entire board/enemy/player state at critical moments
/// rather than relying on delta updates which can desync.
/// </summary>
public sealed class StateSyncSubscriptions
{
    private readonly IGameStateSyncService _sync;
    private readonly IMultiplayerMode _mode;
    private readonly ManualLogSource _log;

    public StateSyncSubscriptions(IGameStateSyncService sync, IMultiplayerMode mode, ManualLogSource log)
    {
        _sync = sync;
        _mode = mode;
        _log = log;
    }

    public void Subscribe()
    {
        // Full sync on battle start (enemies spawned, pegs placed, deck ready)
        BattleController.OnBattleStarted += () => SafeSync("BattleStarted", () => _sync.SyncAll());

        // Sync enemies after they move/spawn
        BattleController.OnTurnComplete += () => SafeSync("TurnComplete", () =>
        {
            _sync.SyncEnemies();
            _sync.SyncPlayer();
        });

        // Sync pegboard after reload (pegs refresh)
        BattleController.OnReloadStarted += () => SafeSync("ReloadStarted", () =>
        {
            _sync.SyncPegboard();
            _sync.SyncPlayer();
        });

        // Sync after attack resolves (health changed, enemies may have died)
        BattleController.OnAttackStarted += () => SafeSync("AttackStarted", () =>
        {
            _sync.SyncEnemies();
            _sync.SyncPlayer();
        });

        // Full sync on victory (final state)
        BattleController.OnVictory += () => SafeSync("Victory", () => _sync.SyncAll());

        // Sync map state on scene changes (via SceneWatcher calling this)
        // Map sync happens via MapController.OnNodeSelectionEvent subscription
        Map.MapController.OnNodeSelectionEvent += (name, floor, cb) =>
            SafeSync("NodeSelected", () => _sync.SyncMap());

        // Sync deck when orbs change
        DeckManager.onDeckShuffled += (_) => SafeSync("DeckShuffled", () => _sync.SyncDeck());
        DeckManager.onDeckSizeChanged += () => SafeSync("DeckSizeChanged", () => _sync.SyncDeck());

        // Sync relics when they change
        Relics.RelicManager.OnRelicAdded += (_) => SafeSync("RelicAdded", () => _sync.SyncRelics());
        Relics.RelicManager.OnRelicRemoved += (_) => SafeSync("RelicRemoved", () => _sync.SyncRelics());

        // Sync map state on ANY scene change so the client follows the host
        // through PostMainMenu → ForestMap → Battle → etc.
        SceneManager.sceneLoaded += (scene, loadMode) =>
        {
            if (loadMode == LoadSceneMode.Single)
                SafeSync("SceneLoaded:" + scene.name, () => _sync.SyncMap());
        };

        _log.LogInfo("StateSyncSubscriptions registered");
    }

    private void SafeSync(string trigger, Action action)
    {
        if (!_mode.IsHosting) return;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.LogWarning($"StateSync[{trigger}] failed: {ex.Message}");
        }
    }
}
