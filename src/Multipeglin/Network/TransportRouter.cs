using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Steamworks;

namespace Multipeglin.Network;

/// <summary>
/// Forwards INetworkTransport + ISteamTransport calls to whichever underlying
/// transport is currently active. Lets the UI flip between Steam lobbies and
/// direct IP while keeping NetworkHost/NetworkClient/Phase6_Handshake bound to
/// a single stable INetworkTransport reference.
/// </summary>
public class TransportRouter : INetworkTransport, ISteamTransport
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    private readonly LiteNetTransport _lite;
    private SteamTransport _steam; // null until AttachSteam is called
    private INetworkTransport _active;

    public bool HasSteam => _steam != null;

    public SteamTransport InnerSteam => _steam;

    public LiteNetTransport InnerLite => _lite;

    public bool ActiveIsSteam => _active == _steam;

    public TransportRouter(LiteNetTransport lite, SteamTransport steam)
    {
        _lite = lite;
        _active = lite;

        _lite.OnDataReceived += (p, d) => OnDataReceived?.Invoke(p, d);
        _lite.OnClientConnected += p => OnClientConnected?.Invoke(p);
        _lite.OnDisconnected += p => OnDisconnected?.Invoke(p);
        _lite.OnConnectionRejected += r => OnConnectionRejected?.Invoke(r);

        if (steam != null)
        {
            AttachSteam(steam);
        }
    }

    /// <summary>
    /// Late-bind a SteamTransport once Peglin's own SteamManager has initialized.
    /// Called from MultiplayerUI.Start (PreMainMenu scene) — not from DI, which
    /// runs in plugin Awake before any scene loads.
    /// </summary>
    public void AttachSteam(SteamTransport steam)
    {
        if (steam == null || _steam != null)
        {
            return;
        }

        _steam = steam;
        _steam.OnDataReceived += (p, d) => OnDataReceived?.Invoke(p, d);
        _steam.OnClientConnected += p => OnClientConnected?.Invoke(p);
        _steam.OnDisconnected += p => OnDisconnected?.Invoke(p);
        _steam.OnConnectionRejected += r => OnConnectionRejected?.Invoke(r);
        Log?.LogInfo("[Router] Steam transport attached");
    }

    public void UseLite()
    {
        if (_active == _lite)
        {
            return;
        }

        try
        { _active?.Stop(); }
        catch (Exception ex) { Log?.LogWarning($"[Router] Prev transport stop failed: {ex.Message}"); }

        _active = _lite;
        Log?.LogInfo("[Router] Active transport = LiteNet");
    }

    public void UseSteam()
    {
        if (_steam == null || _active == _steam)
        {
            return;
        }

        try
        { _active?.Stop(); }
        catch (Exception ex) { Log?.LogWarning($"[Router] Prev transport stop failed: {ex.Message}"); }

        _active = _steam;
        Log?.LogInfo("[Router] Active transport = Steam");
    }

    // --- INetworkTransport ---

    public bool IsHost => _active.IsHost;

    public bool IsConnected => _active.IsConnected;

    public IReadOnlyList<int> ConnectedPeerIds => _active.ConnectedPeerIds;

    public void StartHost(int port) => _active.StartHost(port);

    public void Connect(string address, int port) => _active.Connect(address, port);

    public void Send(byte[] data) => _active.Send(data);

    public void SendTo(int peerId, byte[] data) => _active.SendTo(peerId, data);

    public void Broadcast(byte[] data) => _active.Broadcast(data);

    public void Stop() => _active.Stop();

    public void PollEvents()
    {
        _lite.PollEvents();
        _steam?.PollEvents();
    }

    public event Action<int, byte[]> OnDataReceived;

    public event Action<int> OnClientConnected;

    public event Action<int> OnDisconnected;

    public event Action<string> OnConnectionRejected;

    // --- ISteamTransport ---

    public CSteamID HostedLobbyId => _steam?.HostedLobbyId ?? CSteamID.Nil;

    public void JoinSteamLobby(CSteamID lobbyId)
    {
        if (_steam == null)
        {
            OnConnectionRejected?.Invoke("Steam not available");
            return;
        }

        UseSteam();
        _steam.JoinSteamLobby(lobbyId);
    }

    public void CloseLobbyOnStart() => _steam?.CloseLobbyOnStart();

    public event Action<CSteamID> OnIncomingInvite
    {
        add { if (_steam != null)
            {
                _steam.OnIncomingInvite += value;
            }
        }

        remove { if (_steam != null)
            {
                _steam.OnIncomingInvite -= value;
            }
        }
    }
}
