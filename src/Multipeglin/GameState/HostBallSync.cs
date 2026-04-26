using Multipeglin.Events;
using Multipeglin.GameState.Providers;
using Multipeglin.GameState.Snapshots;
using Multipeglin.Multiplayer;
using Multipeglin.Utility;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Host-side 20 Hz ball streamer. Every tick, captures every active non-dummy
/// PachinkoBall in the scene and dispatches a single <see cref="BallStateSnapshot"/>
/// with every in-flight ball. The client reconciles by GUID: spawn missing
/// visuals, update existing ones, destroy any it has that aren't in the snapshot.
///
/// This replaces per-ball streamers and per-path Harmony patches (Circcae,
/// bramball vines, squirrel, convert-to-gold, etc.) — any ball that lands in
/// FindObjectsOfType and is in FIRING state is included.
/// </summary>
public class HostBallSync : MonoBehaviour
{
    private const float SendInterval = 0.05f; // 20 Hz

    private float _lastSendTime;
    private bool _hadBallsLastTick;
    private IGameEventRegistry _registry;
    private IMultiplayerMode _mode;
    private BallIdentifier _ballId;
    private BallStateProvider _provider;

    private void Start()
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        services.TryResolve(out _registry);
        services.TryResolve(out _mode);
        services.TryResolve(out _ballId);

        if (_ballId != null)
        {
            _provider = new BallStateProvider(MultiplayerPlugin.Logger, _ballId);
        }
    }

    private void Update()
    {
        if (_registry == null || _mode == null || _provider == null)
        {
            Start();
            if (_registry == null || _mode == null || _provider == null)
            {
                return;
            }
        }

        if (!_mode.IsHosting)
        {
            return;
        }

        if (Time.time - _lastSendTime < SendInterval)
        {
            return;
        }

        _lastSendTime = Time.time;

        var snap = _provider.Capture();
        var hasBalls = snap.Balls.Count > 0;

        // Dispatch whenever balls are present OR on the first tick after they
        // disappear — the empty snapshot tells the client to drop visuals that
        // were never paired with an explicit "destroyed" event.
        if (hasBalls || _hadBallsLastTick)
        {
            _registry.Dispatch(snap);
            _hadBallsLastTick = hasBalls;
        }

        // Periodically prune the GUID registry of destroyed balls.
        if (!hasBalls)
        {
            _ballId.PruneDestroyed();
        }
    }
}
