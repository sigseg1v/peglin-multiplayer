using UnityEngine;

namespace Multipeglin.Network;

/// <summary>
/// Drives <see cref="AppLevelRttProvider.Tick"/> from a Unity Update so we
/// don't need a separate timer/coroutine. The provider gates internally on
/// PingIntervalSeconds, so a per-frame tick is cheap.
/// </summary>
public class RttPingTicker : MonoBehaviour
{
    private AppLevelRttProvider _provider;

    private void Update()
    {
        if (_provider == null)
        {
            var services = MultiplayerPlugin.Services;
            if (services == null
                || !services.TryResolve<AppLevelRttProvider>(out _provider))
            {
                return;
            }
        }

        _provider.Tick();
    }
}
