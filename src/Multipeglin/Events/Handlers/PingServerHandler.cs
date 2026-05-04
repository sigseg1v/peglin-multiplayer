using Multipeglin.Events.Network;

namespace Multipeglin.Events.Handlers;

/// <summary>
/// Host-side: turn an incoming PingEvent around as a PongEvent addressed to
/// the same peer that sent it. We don't broadcast — only the asking client
/// needs the echo. Returning null suppresses the default broadcast path;
/// we send directly via the transport instead.
/// </summary>
public sealed class PingServerHandler : IServerHandler<PingEvent>
{
    public PingEvent Handle(PingEvent networkEvent)
    {
        var services = MultiplayerPlugin.Services;
        if (services == null
            || !services.TryResolve<GameEventRegistry>(out var ger)
            || !services.TryResolve<global::Multipeglin.Network.INetworkTransport>(out var transport)
            || !services.TryResolve<global::Multipeglin.Network.Protocol.INetworkSerializer>(out var serializer))
        {
            return null;
        }

        var senderPeerId = ger.CurrentSenderPeerId;
        var pong = new PongEvent { Token = networkEvent.Token };
        var bytes = serializer.Serialize(pong);
        transport.SendTo(senderPeerId, bytes);
        return null;
    }
}
