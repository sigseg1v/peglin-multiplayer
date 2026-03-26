using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace PeglinMods.Spectator.Network;

public class LiteNetTransport : INetworkTransport, INetEventListener
{
    private NetManager _netManager;
    private NetPeer _serverPeer;

    public bool IsHost { get; private set; }
    public bool IsConnected { get; private set; }

    public event Action<byte[]> OnDataReceived;
    public event Action OnClientConnected;
    public event Action OnDisconnected;

    public void StartHost(int port)
    {
        _netManager = new NetManager(this);
        _netManager.Start(port);
        IsHost = true;
        IsConnected = true;
    }

    public void Connect(string address, int port)
    {
        _netManager = new NetManager(this);
        _netManager.Start();
        _netManager.Connect(address, port, NetworkConfig.ConnectionKey);
    }

    public void Send(byte[] data)
    {
        _serverPeer?.Send(data, DeliveryMethod.ReliableOrdered);
    }

    public void Broadcast(byte[] data)
    {
        if (_netManager == null) return;
        foreach (var peer in _netManager.ConnectedPeerList)
        {
            peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }

    public void PollEvents()
    {
        _netManager?.PollEvents();
    }

    public void Stop()
    {
        _netManager?.Stop();
        _netManager = null;
        _serverPeer = null;
        IsHost = false;
        IsConnected = false;
    }

    // INetEventListener

    public void OnPeerConnected(NetPeer peer)
    {
        if (!IsHost)
        {
            _serverPeer = peer;
            IsConnected = true;
        }
        OnClientConnected?.Invoke();
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (!IsHost && peer == _serverPeer)
        {
            _serverPeer = null;
            IsConnected = false;
        }
        OnDisconnected?.Invoke();
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var bytes = new byte[reader.AvailableBytes];
        reader.GetBytes(bytes, bytes.Length);
        reader.Recycle();
        OnDataReceived?.Invoke(bytes);
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(NetworkConfig.ConnectionKey);
    }
}
