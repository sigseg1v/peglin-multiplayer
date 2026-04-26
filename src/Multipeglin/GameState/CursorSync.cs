using Multipeglin.Events.Network.Cursor;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Samples the local cursor's world-space position every frame and broadcasts
/// it to peers when it actually moves. Runs on both host and client. The
/// receiver side smooths between updates in <see cref="RemoteCursorRenderer"/>,
/// so we keep wire traffic low (only on movement, rate-limited).
/// </summary>
public class CursorSync : MonoBehaviour
{
    // Send at most 30x/sec and only when the world position moved by at least
    // this much. 0.03 is ~3 pixels at typical zoom — too small to notice, big
    // enough to suppress sub-pixel jitter.
    private const float SendInterval = 1f / 30f;
    private const float MinMoveDelta = 0.03f;

    private IMessageSender _sender;
    private IMultiplayerMode _mode;
    private PlayerRegistry _registry;
    private INetworkTransport _transport;

    private float _lastSendTime;
    private Vector2 _lastSentWorldPos;
    private bool _hasSent;

    private void Start()
    {
        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        services.TryResolve(out _sender);
        services.TryResolve(out _mode);
        services.TryResolve(out _registry);
        services.TryResolve(out _transport);
    }

    private void Update()
    {
        if (_sender == null || _mode == null || _transport == null)
        {
            return;
        }

        if (!_transport.IsConnected)
        {
            return;
        }

        if (!_mode.IsHosting && !_mode.IsSpectating)
        {
            return;
        }

        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        if (Time.unscaledTime - _lastSendTime < SendInterval)
        {
            return;
        }

        var screenPos = Input.mousePosition;
        // Guard against mouse outside the window — Input.mousePosition clamps
        // but can report negative values on some platforms.
        if (screenPos.x < 0 || screenPos.y < 0
            || screenPos.x > Screen.width || screenPos.y > Screen.height)
        {
            return;
        }

        var worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
        var flat = new Vector2(worldPos.x, worldPos.y);

        if (_hasSent && (flat - _lastSentWorldPos).sqrMagnitude < MinMoveDelta * MinMoveDelta)
        {
            return;
        }

        var slot = LocalSlotIndex();
        if (slot < 0)
        {
            return;
        }

        _sender.Send(new CursorPositionEvent
        {
            FromSlot = slot,
            WorldX = flat.x,
            WorldY = flat.y,
        });

        _lastSentWorldPos = flat;
        _lastSendTime = Time.unscaledTime;
        _hasSent = true;
    }

    private int LocalSlotIndex()
    {
        if (_mode.IsHosting)
        {
            return 0;
        }

        return _registry?.LocalSlot?.SlotIndex ?? -1;
    }
}
