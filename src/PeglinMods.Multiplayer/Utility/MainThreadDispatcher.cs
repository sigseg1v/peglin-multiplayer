using System;
using System.Collections.Concurrent;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.Multiplayer;
using UnityEngine;

namespace PeglinMods.Multiplayer.Utility;

public class MainThreadDispatcher : MonoBehaviour
{
    public static MainThreadDispatcher Instance { get; private set; }

    private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

    // --- Heartbeat: self-contained timer that resolves services each tick ---
    private float _heartbeatTimer;
    private int _heartbeatCount;

    private void Awake()
    {
        // Singleton guard: destroy duplicate instances from previous sessions
        // (DontDestroyOnLoad + HideAndDontSave can leave old instances alive)
        if (Instance != null && Instance != this)
        {
            MultiplayerPlugin.Logger?.LogWarning("[MainThreadDispatcher] Duplicate detected — destroying old instance");
            Destroy(Instance.gameObject);
        }
        Instance = this;
        _heartbeatCount = 0;
        MultiplayerPlugin.Logger?.LogInfo("[MainThreadDispatcher] Awake — Instance set, heartbeat will auto-start");
    }

    public void Enqueue(Action action)
    {
        _queue.Enqueue(action);
    }

    private void Update()
    {
        while (_queue.TryDequeue(out var action))
        {
            action.Invoke();
        }

        // Self-contained heartbeat — no external setup needed
        RunHeartbeat();
    }

    private void RunHeartbeat()
    {
        // Check battle state directly — delegate subscriptions get lost when the
        // game reassigns its static delegate fields (they're not C# events).
        var battleState = Battle.BattleController.CurrentBattleState;
        bool shotActive = battleState == Battle.BattleController.BattleState.AWAITING_SHOT_COMPLETION
                       || battleState == Battle.BattleController.BattleState.NAVIGATION;

        float interval = shotActive ? 1f : 2f;
        _heartbeatTimer += Time.unscaledDeltaTime;
        if (_heartbeatTimer < interval) return;
        _heartbeatTimer = 0f;

        // Resolve hosting status each tick — no stale references
        try
        {
            var services = MultiplayerPlugin.Services;
            if (services == null) return;

            if (!services.TryResolve<IMultiplayerMode>(out var mode) || !mode.IsHosting) return;
            if (!services.TryResolve<IGameStateSyncService>(out var sync)) return;

            _heartbeatCount++;
            var tag = shotActive ? $"HEARTBEAT#{_heartbeatCount}(shot)" : $"HEARTBEAT#{_heartbeatCount}";
            sync.SyncAll(tag);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[HEARTBEAT#{_heartbeatCount}] Exception: {ex.Message}");
        }
    }
}
