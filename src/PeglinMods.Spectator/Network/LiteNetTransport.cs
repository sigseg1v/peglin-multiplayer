using System;
using System.Collections.Generic;
using LiteNetLib;

namespace PeglinMods.Spectator.Network;

public class LiteNetTransport : INetworkTransport, INetEventListener
{
    private NetManager _netManager;
    private readonly List<NetPeer> _peers = new List<NetPeer>();

    public bool IsHost { get; private set; }
    public bool IsConnected => _peers.Count > 0;

    public event Action<byte[]> OnDataReceived;
    public event Action OnClientConnected;
    public event Action OnDisconnected;

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

    public void Broadcast(byte[] data)
    {
        foreach (var peer in _peers)
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
        _peers.Add(peer);
        OnClientConnected?.Invoke();
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _peers.Remove(peer);
        OnDisconnected?.Invoke();
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var data = new byte[reader.AvailableBytes];
        reader.GetBytes(data, data.Length);
        OnDataReceived?.Invoke(data);
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(NetworkConfig.ConnectionKey);
    }

    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
