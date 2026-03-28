using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace PeglinMods.Spectator.Network;

/// <summary>
/// Simple TCP transport. No external dependencies.
/// </summary>
public class LiteNetTransport : INetworkTransport
{
    private TcpListener _listener;
    private TcpClient _client;
    private readonly List<TcpClient> _peers = new List<TcpClient>();

    public bool IsHost { get; private set; }
    public bool IsConnected => _peers.Count > 0 || (_client?.Connected ?? false);

    public event Action<byte[]> OnDataReceived;
    public event Action OnClientConnected;
    public event Action OnDisconnected;

    public void StartHost(int port)
    {
        IsHost = true;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _listener.BeginAcceptTcpClient(OnAccept, null);
    }

    public void Connect(string address, int port)
    {
        IsHost = false;
        _client = new TcpClient();
        _client.BeginConnect(address, port, OnConnectedCallback, null);
    }

    public void Send(byte[] data) => Broadcast(data);

    public void Broadcast(byte[] data)
    {
        var frame = new byte[4 + data.Length];
        BitConverter.GetBytes(data.Length).CopyTo(frame, 0);
        data.CopyTo(frame, 4);

        foreach (var peer in _peers)
            try { peer.GetStream().Write(frame, 0, frame.Length); } catch { }

        if (_client?.Connected == true)
            try { _client.GetStream().Write(frame, 0, frame.Length); } catch { }
    }

    public void PollEvents()
    {
        if (_listener != null && _listener.Pending())
            _listener.BeginAcceptTcpClient(OnAccept, null);

        foreach (var peer in _peers.ToArray())
            ReadFrom(peer);

        if (_client?.Connected == true)
            ReadFrom(_client);
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { }
        try { _client?.Close(); } catch { }
        foreach (var p in _peers) try { p.Close(); } catch { }
        _peers.Clear();
    }

    private void ReadFrom(TcpClient tcp)
    {
        try
        {
            var stream = tcp.GetStream();
            while (stream.DataAvailable)
            {
                var lenBuf = new byte[4];
                stream.Read(lenBuf, 0, 4);
                var len = BitConverter.ToInt32(lenBuf, 0);
                var data = new byte[len];
                int read = 0;
                while (read < len)
                    read += stream.Read(data, read, len - read);
                OnDataReceived?.Invoke(data);
            }
        }
        catch { }
    }

    private void OnAccept(IAsyncResult ar)
    {
        try
        {
            var peer = _listener.EndAcceptTcpClient(ar);
            _peers.Add(peer);
            OnClientConnected?.Invoke();
        }
        catch { }
    }

    private void OnConnectedCallback(IAsyncResult ar)
    {
        try
        {
            _client.EndConnect(ar);
            OnClientConnected?.Invoke();
        }
        catch { OnDisconnected?.Invoke(); }
    }
}
