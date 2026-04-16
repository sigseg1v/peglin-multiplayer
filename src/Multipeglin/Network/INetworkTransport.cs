using System;
using System.Collections.Generic;

namespace Multipeglin.Network;

public interface INetworkTransport
{
    bool IsHost { get; }
    bool IsConnected { get; }
    IReadOnlyList<int> ConnectedPeerIds { get; }
    void StartHost(int port);
    void Connect(string address, int port);
    void Send(byte[] data);
    void SendTo(int peerId, byte[] data);
    void Broadcast(byte[] data);
    void PollEvents();
    void Stop();
    event Action<int, byte[]> OnDataReceived;
    event Action<int> OnClientConnected;
    event Action<int> OnDisconnected;
}
