using Multipeglin.Network.Protocol;

namespace Multipeglin.Network;

public class NetworkHost : IMessageSender
{
    private readonly INetworkTransport _transport;
    private readonly INetworkSerializer _serializer;

    public NetworkHost(INetworkTransport transport, INetworkSerializer serializer)
    {
        _transport = transport;
        _serializer = serializer;
    }

    public void Send<TNetworkEvent>(TNetworkEvent networkEvent) where TNetworkEvent : class
    {
        var data = _serializer.Serialize(networkEvent);
        _transport.Broadcast(data);
    }

    public void SendTo<TNetworkEvent>(int peerId, TNetworkEvent networkEvent) where TNetworkEvent : class
    {
        var data = _serializer.Serialize(networkEvent);
        _transport.SendTo(peerId, data);
    }
}
