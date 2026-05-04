using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Multipeglin.Network;

public class LiteNetTransport : INetworkTransport, INetEventListener
{
    private NetManager _netManager;
    private readonly ConcurrentDictionary<int, NetPeer> _peers = new ConcurrentDictionary<int, NetPeer>();

    public bool IsHost { get; private set; }

    public bool IsConnected => _peers.Count > 0;

    public IReadOnlyList<int> ConnectedPeerIds => _peers.Keys.ToList();

    public event Action<int, byte[]> OnDataReceived;

    public event Action<int> OnClientConnected;

    public event Action<int> OnDisconnected;

    public event Action<string> OnConnectionRejected;

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
        {
            peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }

    public void Broadcast(byte[] data)
    {
        foreach (var peer in _peers.Values)
        {
            peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
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
        _peers.TryRemove(peer.Id, out _);
        if (!IsHost && disconnectInfo.Reason == DisconnectReason.ConnectionRejected)
        {
            var reason = "version";
            try
            {
                if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.AvailableBytes > 0)
                {
                    reason = disconnectInfo.AdditionalData.GetString();
                }
            }
            catch
            {
            }

            OnConnectionRejected?.Invoke(reason);
        }
        else
        {
            OnDisconnected?.Invoke(peer.Id);
        }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var data = new byte[reader.AvailableBytes];
        reader.GetBytes(data, data.Length);
        OnDataReceived?.Invoke(peer.Id, data);
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Check connection key (version match)
        string key;
        try
        {
            key = request.Data.GetString();
        }
        catch
        {
            request.Reject();
            return;
        }

        if (key != NetworkConfig.ConnectionKey)
        {
            request.Reject();
            return;
        }

        // Check player capacity
        if (_peers.Count >= NetworkConfig.MaxClients)
        {
            var writer = new NetDataWriter();
            writer.Put("full");
            request.Reject(writer);
            return;
        }

        request.Accept();
    }

    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }

    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
