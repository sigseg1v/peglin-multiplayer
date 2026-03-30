using Battle;
using HarmonyLib;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Network.Ball;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Network;
using UnityEngine;

namespace PeglinMods.Multiplayer.GameState;

public class BallPositionSync : MonoBehaviour
{
    private IGameEventRegistry _registry;
    private IMultiplayerMode _mode;
    private INetworkTransport _transport;
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
    }

    private void Update()
    {
        if (_registry == null || _mode == null || _transport == null) return;
        if (!_mode.IsHosting || !_transport.IsConnected) return;

        var state = BattleController.CurrentBattleState;

        // Stream ball position during active ball physics (20 Hz)
        if (state == BattleController.BattleState.AWAITING_SHOT_COMPLETION)
        {
            if (Time.time - _lastSendTime >= _sendInterval)
            {
                _lastSendTime = Time.time;
                SendBallPosition();
            }
        }
        // Stream aim direction while player is aiming (10 Hz)
        else if (state == BattleController.BattleState.AWAITING_SHOT)
        {
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
