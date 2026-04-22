using Multipeglin.Multiplayer;
using Multipeglin.Patches;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Host-side catch-all for pachinko-ball synchronization. Periodically enumerates
/// every active non-dummy PachinkoBall in the scene and ensures each has a
/// HostMultiballStreamer + dispatched MultiballSpawnedEvent, regardless of how it
/// was spawned (Circcae FireOrbs, Bramball vine, squirrel relic, convert-to-gold,
/// multiballLevel cascade, etc.).
///
/// The per-path Harmony patches in MultiplayerClientPatches still dispatch early
/// for low latency; this registry is the safety net that guarantees convergence
/// per the DUMB CANVAS rule — the heartbeat must be able to reconstruct state
/// from scratch even if individual events are missed.
/// </summary>
public class HostBallRegistry : MonoBehaviour
{
    private const float ScanInterval = 0.1f;
    private float _lastScanTime;
    private IMultiplayerMode _mode;

    private void Start()
    {
        MultiplayerPlugin.Services?.TryResolve(out _mode);
    }

    private void Update()
    {
        if (_mode == null)
        {
            MultiplayerPlugin.Services?.TryResolve(out _mode);
            if (_mode == null) return;
        }
        if (!_mode.IsHosting) return;
        if (Time.time - _lastScanTime < ScanInterval) return;
        _lastScanTime = Time.time;

        var balls = FindObjectsOfType<PachinkoBall>();
        if (balls == null || balls.Length == 0) return;

        foreach (var ball in balls)
        {
            if (ball == null || ball.IsDummy) continue;
            MultiplayerClientPatches.EnsureBallRegistered(ball.gameObject, "scan");
        }
    }
}
