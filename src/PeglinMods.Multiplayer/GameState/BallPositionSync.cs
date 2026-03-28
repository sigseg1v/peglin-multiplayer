using Battle;
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

        // Only sync during active ball physics
        if (BattleController.CurrentBattleState != BattleController.BattleState.AWAITING_SHOT_COMPLETION)
            return;

        if (Time.time - _lastSendTime < _sendInterval) return;
        _lastSendTime = Time.time;

        var ball = FindObjectOfType<PachinkoBall>();
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
        });
    }
}
