using Multipeglin.Events;
using Multipeglin.Events.Network.Ball;
using UnityEngine;

namespace Multipeglin.GameState;

/// <summary>
/// Attached to each multiball GameObject on the host. Streams the multiball's
/// position/velocity to clients at 20 Hz and dispatches a destroy event when the
/// GameObject is destroyed, so client visuals follow the authoritative host
/// physics instead of running their own. One instance per multiball keyed by Guid.
/// </summary>
public class HostMultiballStreamer : MonoBehaviour
{
    public string Guid;

    private const float SendInterval = 0.05f; // 20 Hz
    private Rigidbody2D _rb;
    private float _lastSendTime;
    private bool _destroyDispatched;

    private void Start()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(Guid)) return;
        if (Time.time - _lastSendTime < SendInterval) return;
        _lastSendTime = Time.time;

        var registry = MultiplayerPlugin.Services?.Resolve<IGameEventRegistry>();
        if (registry == null) return;

        var pos = transform.position;
        var vel = _rb != null ? _rb.velocity : Vector2.zero;
        registry.Dispatch(new MultiballPositionEvent
        {
            Guid = Guid,
            PosX = pos.x,
            PosY = pos.y,
            VelX = vel.x,
            VelY = vel.y,
            Timestamp = Time.time,
        });
    }

    private void OnDestroy()
    {
        if (_destroyDispatched || string.IsNullOrEmpty(Guid)) return;
        _destroyDispatched = true;

        var registry = MultiplayerPlugin.Services?.Resolve<IGameEventRegistry>();
        registry?.Dispatch(new MultiballDestroyedEvent { Guid = Guid });
    }
}
