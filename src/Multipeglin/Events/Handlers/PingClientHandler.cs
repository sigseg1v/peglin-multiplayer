using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Fires on the host when a client's PingEvent arrives. Per the registry
/// convention, the IClientHandler runs on the receiving side regardless of
/// hosting role. Echoes a PongEvent back to the same peer so the client
/// can compute round-trip.
/// </summary>
public sealed class PingClientHandler : IClientHandler<PingEvent>
{
    public void Handle(PingEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<GameEventRegistry>(out var ger)
            || !services.TryResolve<global::Multipeglin.Network.INetworkTransport>(out var transport)
            || !services.TryResolve<global::Multipeglin.Network.Protocol.INetworkSerializer>(out var serializer))
        {
            return;
        }

        var senderPeerId = ger.CurrentSenderPeerId;
        var pong = new PongEvent { Token = networkEvent.Token };
        var bytes = serializer.Serialize(pong);
        transport.SendTo(senderPeerId, bytes);
    }
}
