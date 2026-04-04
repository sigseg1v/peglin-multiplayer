using System;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;

namespace PeglinMods.Multiplayer.Network;

public class LiteNetTransport : INetworkTransport, INetEventListener
{
    private NetManager _netManager;
    private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();

    public bool IsHost { get; private set; }
    public bool IsConnected => _peers.Count > 0;
    public IReadOnlyList<int> ConnectedPeerIds => _peers.Keys.ToList();

    public event Action<int, byte[]> OnDataReceived;
    public event Action<int> OnClientConnected;
    public event Action<int> OnDisconnected;

    public void StartHost(int port)
    {
        IsHost = true;
        _netManager = new NetManager(this) { AutoRecycle = true };
        _netManager.Start(port);
    }

    public void Connect(string address, int port)
    {
        IsHost = false;
        _netManager = new NetManager(this) { AutoRecycle = true };
        _netManager.Start();
        _netManager.Connect(address, port, NetworkConfig.ConnectionKey);
    }

    public void Send(byte[] data) => Broadcast(data);

    public void SendTo(int peerId, byte[] data)
    {
        if (_peers.TryGetValue(peerId, out var peer))
            peer.Send(data, DeliveryMethod.ReliableOrdered);
    }

    public void Broadcast(byte[] data)
    {
        foreach (var peer in _peers.Values)
            peer.Send(data, DeliveryMethod.ReliableOrdered);
    }

    public void PollEvents() => _netManager?.PollEvents();

    public void Stop()
    {
        _netManager?.Stop();
        _peers.Clear();
    }

    public void OnPeerConnected(NetPeer peer)
    {
        _peers[peer.Id] = peer;
        OnClientConnected?.Invoke(peer.Id);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _peers.Remove(peer.Id);
        OnDisconnected?.Invoke(peer.Id);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var data = new byte[reader.AvailableBytes];
        reader.GetBytes(data, data.Length);
        OnDataReceived?.Invoke(peer.Id, data);
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(NetworkConfig.ConnectionKey);
    }

    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
