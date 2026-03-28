using PeglinMods.Multiplayer.Network.Protocol;

namespace PeglinMods.Multiplayer.Network;

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
}
