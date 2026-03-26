using PeglinMods.Spectator.Events;
using PeglinMods.Spectator.Network.Protocol;

namespace PeglinMods.Spectator.Network;

public class NetworkClient : IMessageReceiver
{
    private readonly INetworkTransport _transport;
    private readonly IGameEventRegistry _eventRegistry;
    private readonly INetworkSerializer _serializer;

    public NetworkClient(INetworkTransport transport, IGameEventRegistry eventRegistry, INetworkSerializer serializer)
    {
        _transport = transport;
        _eventRegistry = eventRegistry;
        _serializer = serializer;

        _transport.OnDataReceived += ProcessIncoming;
    }

    public void ProcessIncoming(byte[] data)
    {
        var (typeId, jsonPayload) = _serializer.Deserialize(data);
        _eventRegistry.HandleIncoming(typeId, jsonPayload);
    }
}
