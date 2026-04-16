using Battle;
using HarmonyLib;
using Multipeglin.Events;
using Multipeglin.Events.Network.Ball;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using UnityEngine;

namespace Multipeglin.GameState;

public class BallPositionSync : MonoBehaviour
{
    private IGameEventRegistry _registry;
    private IMultiplayerMode _mode;
    private INetworkTransport _transport;
    private TurnManager _turnManager;
    private float _sendInterval = 0.05f; // 20 Hz
    private float _lastSendTime;
    private float _lastAimSendTime;
    private float _aimSendInterval = 0.1f; // 10 Hz

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

        // During NAVIGATION, the ball aims then fires within the same state.
        // Check IsFiring to decide whether to send position or aim.
        bool isNav = state == BattleController.BattleState.NAVIGATION;
        var activeBall = isNav ? FindActiveBall() : null;
        bool navBallFired = activeBall != null && activeBall.IsFiring();

        // Stream ball position during active ball physics (20 Hz)
        // This runs for ANY player's shot — the host always runs physics.
        if (state == BattleController.BattleState.AWAITING_SHOT_COMPLETION
            || (isNav && navBallFired))
        {
            if (Time.time - _lastSendTime >= _sendInterval)
            {
                _lastSendTime = Time.time;
                SendBallPosition();
            }
        }
        // Stream aim direction while player is aiming (10 Hz)
        // Only send when it's the host's turn — during client turns the host
        // has no aimer and shouldn't broadcast stale aim data.
        else if (state == BattleController.BattleState.AWAITING_SHOT
            || (isNav && !navBallFired))
        {
            if (_turnManager != null && _turnManager.CurrentPlayerSlot > 0)
                return; // client's turn — skip aim broadcast

            if (Time.time - _lastAimSendTime >= _aimSendInterval)
            {
                _lastAimSendTime = Time.time;
                SendAimUpdate();
            }
        }
    }

    private void SendBallPosition()
    {
        var ball = FindActiveBall();
        if (ball == null) return;

        var rb = ball.GetComponent<Rigidbody2D>();
        var pos = ball.transform.position;
        var vel = rb != null ? rb.velocity : Vector2.zero;

        _registry.Dispatch(new BallPositionEvent
        {
            PosX = pos.x,
            PosY = pos.y,
            VelX = vel.x,
            VelY = vel.y,
            Timestamp = Time.time,
        });
    }

    private void SendAimUpdate()
    {
        // Get the aim vector from the active PachinkoBall (the ball being aimed).
        // PachinkoBall.aimVector updates in real-time as the player moves the mouse.
        // BattleController._previousAimVector is only set AFTER the shot fires.
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
            if (!ball.IsDummy)
                return ball;
        }
        return null;
    }
}
