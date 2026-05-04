using Multipeglin.Events.Network;
using Multipeglin.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Fires on the host when a client's HostPongEvent arrives. Per the registry
/// convention, the IClientHandler runs on the receiving side regardless of
/// hosting role (see ClassSelectClientHandler for the same pattern). Feeds
/// the pong to HostRttTracker so it can match the token against the
/// per-peer outstanding probe.
/// </summary>
public sealed class HostPongClientHandler : IClientHandler<HostPongEvent>
{
    public void Handle(HostPongEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<HostRttTracker>(out var tracker)
            || !services.TryResolve<GameEventRegistry>(out var ger))
        {
            return;
        }

        tracker.OnPongReceived(ger.CurrentSenderPeerId, networkEvent.Token);
    }
}
