using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Steamworks;

namespace Multipeglin.Network;

public class SteamTransport : ISteamTransport
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    private const string KEY_MOD_VERSION = "mod_version";
    private const string KEY_GAME_VERSION = "game_version";
    private const string KEY_HOST_NAME = "host_name";
    private const string KEY_STATE = "state";
    private const byte HELLO_BYTE = 0xAA;
    private const int HOST_PEER_ID = -1;

    private Callback<LobbyCreated_t> _cbLobbyCreated;
    private Callback<LobbyEnter_t> _cbLobbyEnter;
    private Callback<LobbyChatUpdate_t> _cbLobbyChat;
    private readonly Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
    private Callback<P2PSessionRequest_t> _cbSessionRequest;
    private Callback<P2PSessionConnectFail_t> _cbSessionFail;

    private CSteamID _mySteamId;
    private CSteamID _lobbyId;
    private CSteamID _hostSteamId;
    private bool _isHost;
    private bool _started;

    private readonly Dictionary<CSteamID, int> _peerIdByCSteamId = new Dictionary<CSteamID, int>();
    private readonly Dictionary<int, CSteamID> _cSteamIdByPeerId = new Dictionary<int, CSteamID>();
    private int _nextPeerId = 1;

    private byte[] _recvBuffer = new byte[1200];

    public bool IsHost => _isHost;

    public bool IsConnected => _peerIdByCSteamId.Count > 0;

    public IReadOnlyList<int> ConnectedPeerIds => _peerIdByCSteamId.Values.ToList();

    public event Action<int, byte[]> OnDataReceived;

    public event Action<int> OnClientConnected;

    public event Action<int> OnDisconnected;

    public event Action<string> OnConnectionRejected;

    public CSteamID HostedLobbyId => _lobbyId;

    // Fires when Steam delivers an incoming lobby join request (friend invite
    // or "Join Game" overlay click). Arg = the lobby CSteamID to join.
    // The transport does NOT auto-join — the UI layer decides whether to
    // prompt the user and call JoinSteamLobby() on accept.
    public event Action<CSteamID> OnIncomingInvite;

    public SteamTransport()
    {
        if (!SteamManager.Initialized)
        {
            return;
        }

        try
        {
            _mySteamId = SteamUser.GetSteamID();
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            Log?.LogInfo($"[Steam] Transport constructed. SteamID={_mySteamId.m_SteamID}");
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Steam] Transport ctor failed: {ex}");
        }
    }

    public void StartHost(int port)
    {
        _isHost = true;
        _started = true;
        EnsureSessionCallbacks();
        try
        {
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, NetworkConfig.MaxClients + 1);
            Log?.LogInfo($"[Steam] CreateLobby requested (FriendsOnly, capacity {NetworkConfig.MaxClients + 1})");
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Steam] CreateLobby failed: {ex}");
            OnConnectionRejected?.Invoke("steam-error");
        }
    }

    public void Connect(string address, int port)
    {
        throw new NotSupportedException("SteamTransport.Connect is not supported; use JoinSteamLobby(CSteamID).");
    }

    public void JoinSteamLobby(CSteamID lobbyId)
    {
        _isHost = false;
        _started = true;
        EnsureSessionCallbacks();
        try
        {
            SteamMatchmaking.JoinLobby(lobbyId);
            Log?.LogInfo($"[Steam] JoinLobby requested for {lobbyId.m_SteamID}");
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Steam] JoinLobby failed: {ex}");
            OnConnectionRejected?.Invoke("steam-error");
        }
    }

    public void Send(byte[] data)
    {
        if (_isHost)
        {
            Broadcast(data);
        }
        else if (_cSteamIdByPeerId.TryGetValue(HOST_PEER_ID, out var host))
        {
            SendP2P(host, data);
        }
    }

    public void SendTo(int peerId, byte[] data)
    {
        if (_cSteamIdByPeerId.TryGetValue(peerId, out var sid))
        {
            SendP2P(sid, data);
        }
    }

    public void Broadcast(byte[] data)
    {
        foreach (var sid in _cSteamIdByPeerId.Values)
        {
            SendP2P(sid, data);
        }
    }

    public void PollEvents()
    {
        if (!_started)
        {
            return;
        }

        while (SteamNetworking.IsP2PPacketAvailable(out var size, 0))
        {
            if (size > _recvBuffer.Length)
            {
                _recvBuffer = new byte[size];
            }

            if (!SteamNetworking.ReadP2PPacket(_recvBuffer, size, out var read, out CSteamID sender, 0))
            {
                break;
            }

            if (read == 1 && _recvBuffer[0] == HELLO_BYTE)
            {
                GetOrAssignPeerId(sender);
                continue;
            }

            var peerId = GetOrAssignPeerId(sender);
            var copy = new byte[read];
            Buffer.BlockCopy(_recvBuffer, 0, copy, 0, (int)read);
            try
            { OnDataReceived?.Invoke(peerId, copy); }
            catch (Exception ex) { Log?.LogError($"[Steam] OnDataReceived handler threw: {ex}"); }
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        try
        {
            foreach (var sid in _cSteamIdByPeerId.Values)
            {
                try
                { SteamNetworking.CloseP2PSessionWithUser(sid); }
                catch { }
            }

            _peerIdByCSteamId.Clear();
            _cSteamIdByPeerId.Clear();
            _nextPeerId = 1;

            if (_lobbyId.IsValid())
            {
                try
                { SteamMatchmaking.LeaveLobby(_lobbyId); }
                catch { }
            }

            _lobbyId = CSteamID.Nil;
            _hostSteamId = CSteamID.Nil;

            try
            { SteamFriends.SetRichPresence("connect", string.Empty); }
            catch { }

            DisposeSessionCallbacks();
            Log?.LogInfo("[Steam] Transport stopped");
        }
        catch (Exception ex)
        {
            Log?.LogError($"[Steam] Stop error: {ex}");
        }
    }

    public void CloseLobbyOnStart()
    {
        if (!_isHost || !_lobbyId.IsValid())
        {
            return;
        }

        try
        {
            SteamMatchmaking.SetLobbyData(_lobbyId, KEY_STATE, "closed");
            SteamMatchmaking.SetLobbyType(_lobbyId, ELobbyType.k_ELobbyTypePrivate);
            Log?.LogInfo("[Steam] Lobby closed (state=closed, type=private)");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Steam] CloseLobbyOnStart failed: {ex.Message}");
        }
    }

    private void OnLobbyCreated(LobbyCreated_t evt)
    {
        if (evt.m_eResult != EResult.k_EResultOK)
        {
            Log?.LogError($"[Steam] LobbyCreated failed: {evt.m_eResult}");
            OnConnectionRejected?.Invoke($"steam-lobby-{evt.m_eResult}");
            return;
        }

        _lobbyId = new CSteamID(evt.m_ulSteamIDLobby);
        _hostSteamId = _mySteamId;

        try
        {
            SteamMatchmaking.SetLobbyData(_lobbyId, KEY_MOD_VERSION, MultiplayerPluginInfo.VERSION);
            SteamMatchmaking.SetLobbyData(_lobbyId, KEY_GAME_VERSION, UnityEngine.Application.version ?? "unknown");
            SteamMatchmaking.SetLobbyData(_lobbyId, KEY_HOST_NAME, SteamFriends.GetPersonaName());
            SteamMatchmaking.SetLobbyData(_lobbyId, KEY_STATE, "open");
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {_lobbyId.m_SteamID}");
            Log?.LogInfo($"[Steam] Lobby {_lobbyId.m_SteamID} created and advertised");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[Steam] SetLobbyData failed: {ex.Message}");
        }
    }

    private void OnLobbyEnter(LobbyEnter_t evt)
    {
        var lobby = new CSteamID(evt.m_ulSteamIDLobby);
        if (evt.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Log?.LogWarning($"[Steam] LobbyEnter failed: response={evt.m_EChatRoomEnterResponse}");
            OnConnectionRejected?.Invoke($"steam-enter-{evt.m_EChatRoomEnterResponse}");
            return;
        }

        if (_isHost)
        {
            return;
        }

        _lobbyId = lobby;

        var remoteModVersion = SteamMatchmaking.GetLobbyData(_lobbyId, KEY_MOD_VERSION);
        var lobbyState = SteamMatchmaking.GetLobbyData(_lobbyId, KEY_STATE);

        if (string.IsNullOrEmpty(remoteModVersion))
        {
            Log?.LogWarning("[Steam] Lobby has no mod_version metadata");
            SteamMatchmaking.LeaveLobby(_lobbyId);
            _lobbyId = CSteamID.Nil;
            OnConnectionRejected?.Invoke("Lobby is not hosted by Multipeglin.");
            return;
        }

        if (remoteModVersion != MultiplayerPluginInfo.VERSION)
        {
            Log?.LogWarning($"[Steam] Version mismatch: host={remoteModVersion} local={MultiplayerPluginInfo.VERSION}");
            SteamMatchmaking.LeaveLobby(_lobbyId);
            _lobbyId = CSteamID.Nil;
            OnConnectionRejected?.Invoke($"Version mismatch: host v{remoteModVersion}, you v{MultiplayerPluginInfo.VERSION}");
            return;
        }

        if (lobbyState == "closed")
        {
            Log?.LogWarning("[Steam] Lobby already closed");
            SteamMatchmaking.LeaveLobby(_lobbyId);
            _lobbyId = CSteamID.Nil;
            OnConnectionRejected?.Invoke("Game already in progress");
            return;
        }

        _hostSteamId = SteamMatchmaking.GetLobbyOwner(_lobbyId);
        if (!_hostSteamId.IsValid() || _hostSteamId == _mySteamId)
        {
            Log?.LogWarning($"[Steam] Invalid lobby owner: {_hostSteamId.m_SteamID}");
            SteamMatchmaking.LeaveLobby(_lobbyId);
            _lobbyId = CSteamID.Nil;
            OnConnectionRejected?.Invoke("Could not resolve host.");
            return;
        }

        _peerIdByCSteamId[_hostSteamId] = HOST_PEER_ID;
        _cSteamIdByPeerId[HOST_PEER_ID] = _hostSteamId;

        SendP2P(_hostSteamId, new[] { HELLO_BYTE });

        Log?.LogInfo($"[Steam] Lobby entered, host={_hostSteamId.m_SteamID}");
        try
        { OnClientConnected?.Invoke(HOST_PEER_ID); }
        catch (Exception ex) { Log?.LogError($"[Steam] OnClientConnected handler threw: {ex}"); }
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t evt)
    {
        if (!_lobbyId.IsValid() || evt.m_ulSteamIDLobby != _lobbyId.m_SteamID)
        {
            return;
        }

        var member = new CSteamID(evt.m_ulSteamIDUserChanged);
        var change = (EChatMemberStateChange)evt.m_rgfChatMemberStateChange;

        var left = (change & (EChatMemberStateChange.k_EChatMemberStateChangeLeft
                             | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                             | EChatMemberStateChange.k_EChatMemberStateChangeKicked
                             | EChatMemberStateChange.k_EChatMemberStateChangeBanned)) != 0;
        if (left)
        {
            HandlePeerLeft(member);
            return;
        }

        if (_isHost && (change & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
        {
            if (member == _mySteamId)
            {
                return;
            }

            var current = _peerIdByCSteamId.Count;
            if (current >= NetworkConfig.MaxClients)
            {
                Log?.LogWarning($"[Steam] Lobby full — refusing {member.m_SteamID} ({current}/{NetworkConfig.MaxClients})");
                try
                { SteamNetworking.CloseP2PSessionWithUser(member); }
                catch { }
            }
            else
            {
                Log?.LogInfo($"[Steam] Lobby member entered: {member.m_SteamID} ({current + 1}/{NetworkConfig.MaxClients})");
            }
        }
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t evt)
    {
        if (!evt.m_steamIDLobby.IsValid())
        {
            Log?.LogWarning("[Steam] GameLobbyJoinRequested with invalid lobby id");
            return;
        }
        // If we are already hosting a lobby of our own, ignore stale incoming
        // invites rather than hijacking the host into client mode.
        if (_isHost)
        {
            Log?.LogInfo($"[Steam] Ignoring incoming invite to {evt.m_steamIDLobby.m_SteamID} — already hosting");
            return;
        }

        Log?.LogInfo($"[Steam] Incoming invite to lobby {evt.m_steamIDLobby.m_SteamID} — awaiting user confirmation");
        try
        { OnIncomingInvite?.Invoke(evt.m_steamIDLobby); }
        catch (Exception ex) { Log?.LogError($"[Steam] OnIncomingInvite handler threw: {ex}"); }
    }

    private void OnP2PSessionRequest(P2PSessionRequest_t evt)
    {
        var sender = evt.m_steamIDRemote;
        if (!IsLobbyMember(sender))
        {
            Log?.LogWarning($"[Steam] Refusing P2P from non-member {sender.m_SteamID}");
            try
            { SteamNetworking.CloseP2PSessionWithUser(sender); }
            catch { }

            return;
        }

        if (_isHost && !_peerIdByCSteamId.ContainsKey(sender) && _peerIdByCSteamId.Count >= NetworkConfig.MaxClients)
        {
            Log?.LogWarning($"[Steam] Refusing P2P from {sender.m_SteamID}: lobby full");
            try
            { SteamNetworking.CloseP2PSessionWithUser(sender); }
            catch { }

            return;
        }

        if (!SteamNetworking.AcceptP2PSessionWithUser(sender))
        {
            Log?.LogWarning($"[Steam] AcceptP2PSessionWithUser({sender.m_SteamID}) returned false");
            return;
        }

        if (_isHost && !_peerIdByCSteamId.ContainsKey(sender))
        {
            var peerId = GetOrAssignPeerId(sender);
            Log?.LogInfo($"[Steam] Accepted P2P session: {sender.m_SteamID} -> peerId {peerId}");
            try
            { OnClientConnected?.Invoke(peerId); }
            catch (Exception ex) { Log?.LogError($"[Steam] OnClientConnected handler threw: {ex}"); }
        }
    }

    private void OnP2PSessionConnectFail(P2PSessionConnectFail_t evt)
    {
        var sid = evt.m_steamIDRemote;
        var err = (EP2PSessionError)evt.m_eP2PSessionError;
        Log?.LogWarning($"[Steam] P2P session failed: {sid.m_SteamID} error={err}");
        HandlePeerLeft(sid);
    }

    private void HandlePeerLeft(CSteamID sid)
    {
        if (!_peerIdByCSteamId.TryGetValue(sid, out var peerId))
        {
            return;
        }

        try
        { SteamNetworking.CloseP2PSessionWithUser(sid); }
        catch { }

        _peerIdByCSteamId.Remove(sid);
        _cSteamIdByPeerId.Remove(peerId);
        Log?.LogInfo($"[Steam] Peer left: {sid.m_SteamID} (peerId {peerId})");
        try
        { OnDisconnected?.Invoke(peerId); }
        catch (Exception ex) { Log?.LogError($"[Steam] OnDisconnected handler threw: {ex}"); }
    }

    private int GetOrAssignPeerId(CSteamID sid)
    {
        if (_peerIdByCSteamId.TryGetValue(sid, out var id))
        {
            return id;
        }

        id = _nextPeerId++;
        _peerIdByCSteamId[sid] = id;
        _cSteamIdByPeerId[id] = sid;
        return id;
    }

    private bool IsLobbyMember(CSteamID sid)
    {
        if (!_lobbyId.IsValid())
        {
            return false;
        }

        var count = SteamMatchmaking.GetNumLobbyMembers(_lobbyId);
        for (var i = 0; i < count; i++)
        {
            if (SteamMatchmaking.GetLobbyMemberByIndex(_lobbyId, i) == sid)
            {
                return true;
            }
        }

        return false;
    }

    private void SendP2P(CSteamID sid, byte[] data)
    {
        if (data.Length > 1100)
        {
            Log?.LogWarning($"[Steam] SendP2P {data.Length}B exceeds 1100B MTU hint");
        }

        if (!SteamNetworking.SendP2PPacket(sid, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, 0))
        {
            Log?.LogWarning($"[Steam] SendP2PPacket to {sid.m_SteamID} returned false");
        }
    }

    private void EnsureSessionCallbacks()
    {
        if (_cbLobbyCreated == null)
        {
            _cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        }

        if (_cbLobbyEnter == null)
        {
            _cbLobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        }

        if (_cbLobbyChat == null)
        {
            _cbLobbyChat = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        }

        if (_cbSessionRequest == null)
        {
            _cbSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        }

        if (_cbSessionFail == null)
        {
            _cbSessionFail = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
        }
    }

    private void DisposeSessionCallbacks()
    {
        try
        { _cbLobbyCreated?.Dispose(); }
        catch { }

        try
        { _cbLobbyEnter?.Dispose(); }
        catch { }

        try
        { _cbLobbyChat?.Dispose(); }
        catch { }

        try
        { _cbSessionRequest?.Dispose(); }
        catch { }

        try
        { _cbSessionFail?.Dispose(); }
        catch { }

        _cbLobbyCreated = null;
        _cbLobbyEnter = null;
        _cbLobbyChat = null;
        _cbSessionRequest = null;
        _cbSessionFail = null;
    }
}
