using UnityEngine;

namespace Multipeglin.Network;

/// <summary>
/// Drives <see cref="HostRttTracker.Tick"/> from a Unity Update. Cheap —
/// the tracker gates internally on probe/log cadences.
/// </summary>
public class HostRttTickerBehaviour : MonoBehaviour
{
    private HostRttTracker _tracker;

    private void Update()
    {
        if (_tracker == null)
        {
            var services = MultiplayerPlugin.Services;
            if (services == null
                || !services.TryResolve<HostRttTracker>(out _tracker))
            {
                return;
            }
        }

        _tracker.Tick();
    }
}
