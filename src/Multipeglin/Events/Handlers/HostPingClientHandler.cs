using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Client-side: the host pinged us — echo a HostPongEvent back with the same
/// token so the host can compute round-trip to this peer.
/// </summary>
public sealed class HostPingClientHandler : IClientHandler<HostPingEvent>
{
    public void Handle(HostPingEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<global::Multipeglin.Network.IMessageSender>(out var sender))
        {
            return;
        }

        sender.Send(new HostPongEvent { Token = networkEvent.Token });
    }
}
