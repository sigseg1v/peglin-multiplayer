using Battle;
using Multipeglin.Events;
using Multipeglin.Events.Network.Ball;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Host-side aim streamer (10 Hz). Ball flight positions are now streamed via
/// <see cref="HostBallSync"/> (which sends a full BallStateSnapshot at 20 Hz),
/// so this component is only responsible for broadcasting the aim vector while
/// the host is in AWAITING_SHOT so the client's aimer tracks the host.
/// </summary>
public class BallPositionSync : MonoBehaviour
{
    private IGameEventRegistry _registry;
    private IMultiplayerMode _mode;
    private INetworkTransport _transport;
    private TurnManager _turnManager;
    private float _lastAimSendTime;
    private const float AimSendInterval = 0.1f; // 10 Hz

    private void Start()
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;
        services.TryResolve(out _registry);
        services.TryResolve(out _mode);
        services.TryResolve(out _transport);
        services.TryResolve(out _turnManager);
    }

    private void Update()
    {
        if (_registry == null || _mode == null || _transport == null) return;
        if (!_mode.IsHosting || !_transport.IsConnected) return;

        var state = BattleController.CurrentBattleState;

        bool isNav = state == BattleController.BattleState.NAVIGATION;
        var activeBall = isNav ? FindActiveBall() : null;
        bool navBallFired = activeBall != null && activeBall.IsFiring();

        // Only stream aim while the host is actively aiming their own shot —
        // client turns have no aimer on the host side.
        if (state != BattleController.BattleState.AWAITING_SHOT && !(isNav && !navBallFired))
            return;

        if (_turnManager != null && _turnManager.CurrentPlayerSlot > 0) return;
        if (Time.time - _lastAimSendTime < AimSendInterval) return;
        _lastAimSendTime = Time.time;

        var ball = FindActiveBall();
        if (ball == null) return;
        var aimVec = ball.aimVector;
        if (aimVec == Vector2.zero) return;

        var pos = ball.transform.position;
        _registry.Dispatch(new AimUpdateEvent
        {
            AimX = aimVec.x,
            AimY = aimVec.y,
            SpawnX = pos.x,
            SpawnY = pos.y,
        });
    }

    private static PachinkoBall FindActiveBall()
    {
        foreach (var ball in FindObjectsOfType<PachinkoBall>())
        {
            if (!ball.IsDummy) return ball;
        }
        return null;
    }
}
