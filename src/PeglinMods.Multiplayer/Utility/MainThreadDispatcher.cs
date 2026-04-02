using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PeglinMods.Multiplayer.Utility;

public class MainThreadDispatcher : MonoBehaviour
{
    private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

    // --- Heartbeat timer (more robust than coroutine — survives scene loads) ---
    private Action _heartbeatAction;
    private Func<float> _heartbeatIntervalFunc;
    private float _heartbeatTimer;

    /// <summary>
    /// Register a heartbeat callback that fires at the interval returned by intervalFunc.
    /// Runs in Update, never dies from scene loads or exceptions.
    /// </summary>
    public void SetHeartbeat(Action action, Func<float> intervalFunc)
    {
        _heartbeatAction = action;
        _heartbeatIntervalFunc = intervalFunc;
        _heartbeatTimer = 0f;
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

        // Heartbeat timer
        if (_heartbeatAction != null && _heartbeatIntervalFunc != null)
        {
            _heartbeatTimer += Time.unscaledDeltaTime;
            float interval = _heartbeatIntervalFunc();
            if (_heartbeatTimer >= interval)
            {
                _heartbeatTimer = 0f;
                try { _heartbeatAction(); }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Logger?.LogWarning($"[Heartbeat] Exception: {ex.Message}");
                }
            }
        }
    }
}
