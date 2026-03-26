using System;

namespace PeglinMods.Spectator.Network;

public interface INetworkTransport
{
    bool IsHost { get; }
    bool IsConnected { get; }
    void StartHost(int port);
    void Connect(string address, int port);
    void Send(byte[] data);
    void Broadcast(byte[] data);
    void PollEvents();
    void Stop();
    event Action<byte[]> OnDataReceived;
    event Action OnClientConnected;
    event Action OnDisconnected;
}
