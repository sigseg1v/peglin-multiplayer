using System;

using Battle;
using BepInEx.Logging;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Utility;
using Tutorial;
using UnityEngine;
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
        // FULL SYNC on battle start — enemies spawned, pegs placed, deck ready
        BattleController.OnBattleStarted += () => SafeSync("BattleStarted", () => _sync.SyncAll("BattleStarted"));

        // FULL SYNC after each round — ensures client stays in sync even if individual events missed
        BattleController.OnRoundCountIncremented += (_) => SafeSync("RoundIncremented", () => _sync.SyncAll("RoundIncremented"));

        // Sync enemies and player after turn complete (enemies may have moved/attacked)
        BattleController.OnTurnComplete += () => SafeSync("TurnComplete", () =>
        {
            _sync.SyncEnemies();
            _sync.SyncPlayer();
        });

        // Sync pegboard after reload (pegs refresh) and enemies/player
        BattleController.OnReloadStarted += () => SafeSync("ReloadStarted", () =>
        {
            _sync.SyncPegboard();
            _sync.SyncEnemies();
            _sync.SyncPlayer();
        });

        // Sync pegboard immediately when refresh pegs activate (mid-turn board restore)
        BattleController.OnPreRefreshActivated += () => SafeSync("PreRefresh", () =>
        {
            _sync.SyncPegboard();
        });

        // Sync after attack resolves (health changed, enemies may have died)
        BattleController.OnAttackStarted += () => SafeSync("AttackStarted", () =>
        {
            _sync.SyncEnemies();
            _sync.SyncPlayer();
        });

        // Sync after shot completes (peg states changed from hits)
        BattleController.OnShotComplete += () => SafeSync("ShotComplete", () =>
        {
            _sync.SyncPegboard();
            _sync.SyncEnemies();
            _sync.SyncDeck();
        });

        // Sync pegs when they're destroyed (real-time during ball physics)
        Peg.OnPegDestroyed += (_, _) => SafeSync("PegDestroyed", () => _sync.SyncPegboard());

        // Sync pegboard when bomb detonates (destroys nearby pegs)
        BattleController.OnBombDetonated += () => SafeSync("BombDetonated", () => _sync.SyncPegboard());

        // Sync pegboard when bomb is thrown (bomb state changes)
        BattleController.OnBombThrown += () => SafeSync("BombThrown", () => _sync.SyncPegboard());

        // Sync pegboard on crit activation (crit pegs change visual state)
        BattleController.onCriticalHitActivated += () => SafeSync("CritActivated", () => _sync.SyncPegboard());

        // Sync pegboard on crit deactivation
        BattleController.onCriticalHitDeactivated += () => SafeSync("CritDeactivated", () => _sync.SyncPegboard());

        // Sync pegboard when refresh potion activates (refreshes cleared pegs)
        BattleController.onRefreshPotionActivated += () => SafeSync("RefreshPotion", () => _sync.SyncPegboard());

        // Sync deck when an orb is used (so client sees current/upcoming orb updates)
        DeckManager.onBallUsed += (_) => SafeSync("BallUsed", () => _sync.SyncDeck());

        // FULL SYNC on victory (final state)
        BattleController.OnVictory += () => SafeSync("Victory", () => _sync.SyncAll("Victory"));

        // Sync map state when a node is selected
        Map.MapController.OnNodeSelectionEvent += (name, floor, cb) =>
            SafeSync("NodeSelected", () => _sync.SyncMap());

        // Sync deck when orbs change
        DeckManager.onDeckShuffled += (_) => SafeSync("DeckShuffled", () => _sync.SyncDeck());
        DeckManager.onDeckSizeChanged += () => SafeSync("DeckSizeChanged", () => _sync.SyncDeck());

        // Sync relics when they change
        Relics.RelicManager.OnRelicAdded += (_) => SafeSync("RelicAdded", () => _sync.SyncRelics());
        Relics.RelicManager.OnRelicRemoved += (_) => SafeSync("RelicRemoved", () => _sync.SyncRelics());

        // FULL SYNC on any scene change — ensures client has complete state on new scenes
        SceneManager.sceneLoaded += (scene, loadMode) =>
        {
            if (loadMode == LoadSceneMode.Single)
                SafeSync("SceneLoaded:" + scene.name, () => _sync.SyncAll("SceneLoaded:" + scene.name));
        };

        // Heartbeat is now self-contained in MainThreadDispatcher.RunHeartbeat()
        // It resolves IMultiplayerMode and IGameStateSyncService each tick,
        // so it works regardless of initialization order.

        _log.LogInfo("StateSyncSubscriptions registered (event-driven sync + self-contained heartbeat in MainThreadDispatcher)");
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
