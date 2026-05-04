using Multipeglin.Events.Network;
using Multipeglin.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Host-side: feed the pong to HostRttTracker so it can match the token to
/// the per-peer outstanding probe and update the RTT estimate.
/// </summary>
public sealed class HostPongServerHandler : IServerHandler<HostPongEvent>
{
    public HostPongEvent Handle(HostPongEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<HostRttTracker>(out var tracker)
            || !services.TryResolve<GameEventRegistry>(out var ger))
        {
            return null;
        }

        tracker.OnPongReceived(ger.CurrentSenderPeerId, networkEvent.Token);
        return null;
    }
}
