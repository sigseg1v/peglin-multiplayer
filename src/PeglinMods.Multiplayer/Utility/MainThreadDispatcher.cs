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
    private bool _shotActive;

    private void Awake()
    {
        Instance = this;
        MultiplayerPlugin.Logger?.LogInfo("[MainThreadDispatcher] Awake — Instance set, heartbeat will auto-start");

        // Subscribe to shot events for adaptive interval
        try
        {
            PachinkoBall.OnShotFired += (_) => _shotActive = true;
            Battle.BattleController.OnShotComplete += () => _shotActive = false;
            Battle.BattleController.OnBattleEnded += () => _shotActive = false;
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[MainThreadDispatcher] Shot event subscription failed: {ex.Message}");
        }
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
        float interval = _shotActive ? 1f : 2f;
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
            var tag = _shotActive ? $"HEARTBEAT#{_heartbeatCount}(shot)" : $"HEARTBEAT#{_heartbeatCount}";
            sync.SyncAll(tag);
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[HEARTBEAT#{_heartbeatCount}] Exception: {ex.Message}");
        }
    }
}
