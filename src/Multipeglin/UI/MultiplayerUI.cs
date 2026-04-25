using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BepInEx.Logging;
using Multipeglin.DI;
using Multipeglin.Events.Handlers;
using Multipeglin.Network;
using Multipeglin.Multiplayer;
using Multipeglin.GameState.Appliers;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Multipeglin.UI;

public class MultiplayerUI : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    // Services
    private INetworkTransport _transport;
    private ISteamTransport _steamTransport; // null when Steam unavailable
    private TransportRouter _router;
    private IMultiplayerMode _multiplayerMode;

    // Current process AppID — normally Peglin's (1296610), but Spacewar (480) under
    // dev-network-player. Read at UI init from the initialized Steamworks lib so the
    // friend filter matches whichever AppID both machines are running.
    private AppId_t _currentAppId = new AppId_t(1296610);

    // Root canvas
    private GameObject _canvasObj;
    private Canvas _canvas;

    // Overlay panels
    private GameObject _overlayPanel;
    private GameObject _mainPanel;
    private GameObject _hostPanel;
    private GameObject _joinPanel;
    private GameObject _lobbyPanel;
    private GameObject _multiplayerPanel;

    // Main panel elements
    private Button _hostButton;
    private Button _joinButton;
    private Button _hostSteamButton;
    private Button _joinFriendButton;
    private GameObject _advancedIpContainer; // legacy Host/Join IP buttons; hidden until toggled
    private Button _advancedToggleButton;
    private bool _advancedIpVisible;

    // Friend-list panel (Steam join flow)
    private GameObject _friendListPanel;
    private GameObject _friendListContent;
    private TextMeshProUGUI _friendListStatusText;

    // Invite Friend button (created on lobby panel when hosting via Steam)
    private Button _inviteFriendButton;

    // Join panel elements
    private TMP_InputField _codeInput;
    private TextMeshProUGUI _statusText;

    // Host panel elements
    private TextMeshProUGUI _hostInfoText;

    // Lobby panel elements
    private TextMeshProUGUI _lobbyStatusText;
    private TextMeshProUGUI _lobbyJoinLinkText;
    private float _inviteCopiedFlashUntil;

    // Multiplayer panel elements (client fullscreen event feed)
    private TextMeshProUGUI _multiplayerFeedText;
    private int _lastFeedVersion;

    // Mirror mode waiting overlay
    private GameObject _waitingPanel;
    private TextMeshProUGUI _waitingText;

    // Transparent spectator banner (for PegMinigame etc — shows text without blocking the view)
    private GameObject _spectatorBanner;
    private TextMeshProUGUI _spectatorBannerText;

    // Coop turn indicator (small banner at top of screen)
    private GameObject _turnIndicatorPanel;
    private TextMeshProUGUI _turnIndicatorText;

    // Connection error dialog
    private GameObject _errorPanel;

    // Static access for menu button and player name
    private static MultiplayerUI _instance;
    public static string LocalPlayerName { get; private set; } = "";

    // State
    private bool _overlayVisible;
    private string _lastConnectionStatus = "";

    private void Start()
    {
        try
        {
            _instance = this;
            _transport = MultiplayerPlugin.Services.Resolve<INetworkTransport>();
            _multiplayerMode = MultiplayerPlugin.Services.Resolve<IMultiplayerMode>();
            MultiplayerPlugin.Services.TryResolve<TransportRouter>(out _router);

            // Late-attach SteamTransport now that Peglin's own SteamManager has initialized.
            // Doing this in DI (plugin Awake) touches SteamManager.Initialized before any
            // scene loads, which force-instantiates a SteamManager that doesn't survive.
            if (_router != null && !_router.HasSteam
                && Environment.GetEnvironmentVariable("SKIP_STEAM_INIT") != "1")
            {
                try
                {
                    if (SteamManager.Initialized)
                    {
                        _router.AttachSteam(new SteamTransport());
                    }
                    else
                    {
                        Log?.LogInfo("[Steam] SteamManager not initialized — Steam UI disabled");
                    }
                }
                catch (Exception ex)
                {
                    Log?.LogWarning($"[Steam] Attach failed: {ex.Message}");
                }
            }
            _steamTransport = (_router != null && _router.HasSteam) ? _router : null;
            if (_steamTransport != null)
            {
                try { _currentAppId = SteamUtils.GetAppID(); } catch { }
                Log?.LogInfo($"[Steam] Friend filter using appId={_currentAppId.m_AppId}");
            }

            _transport.OnClientConnected += peerId => OnConnected();
            _transport.OnDisconnected += peerId => OnDisconnected(peerId);
            _transport.OnConnectionRejected += reason => OnConnectionRejected(reason);

            if (_steamTransport != null)
                _steamTransport.OnIncomingInvite += OnSteamInviteReceived;

            // Auto-detect player name: env var > Steam display name > fallback
            var envName = Environment.GetEnvironmentVariable("MULTIPEGLIN_PLAYER_NAME");
            if (!string.IsNullOrEmpty(envName))
            {
                LocalPlayerName = envName;
            }
            else
            {
                try
                {
                    if (SteamManager.Initialized)
                        LocalPlayerName = SteamFriends.GetPersonaName();
                }
                catch { }
            }
            if (string.IsNullOrEmpty(LocalPlayerName))
                LocalPlayerName = "Player";
            Log?.LogInfo($"Player name: {LocalPlayerName}");

            CreateCanvas();
            CreateOverlay();
            HideOverlay();

            // Show warning if patch targets are missing (game was probably updated)
            var missing = MultiplayerPlugin.MissingPatches;
            if (missing != null && missing.Count > 0)
            {
                ShowErrorDialog(
                    $"Multipeglin v{MultiplayerPluginInfo.VERSION} was built for " +
                    $"Peglin v{MultiplayerPluginInfo.COMPILED_GAME_VERSION}.\n\n" +
                    $"{missing.Count} game method(s) could not be found.\n" +
                    "A game update may have affected the mod.\n" +
                    "Check for a mod update.");
            }

            Log?.LogInfo("MultiplayerUI initialized");
        }
        catch (Exception ex)
        {
            Log?.LogError($"MultiplayerUI.Start() failed: {ex}");
        }
    }

    private void Update()
    {
        // Refresh lobby display when visible (handshake arrives asynchronously)
        if (_lobbyPanel != null && _lobbyPanel.activeSelf)
            UpdateLobbyPanel();

        // Refresh multiplayer feed when visible in diagnostics mode and new events arrived
        if (_multiplayerPanel != null && _multiplayerPanel.activeSelf
            && _multiplayerMode.ClientMode == ClientMode.Diagnostics
            && EventFeed.Version != _lastFeedVersion)
        {
            _lastFeedVersion = EventFeed.Version;
            _multiplayerFeedText.text = EventFeed.GetText(40);
        }

        // Mirror mode waiting overlay — shows when host is on a non-followable scene
        // BUT NOT while the lobby is active (before game starts)
        // AND NOT during Battle scene when the turn indicator is active (avoids duplicate text)
        if (_multiplayerMode.IsSpectating && _multiplayerMode.ClientMode == ClientMode.Mirror
            && LobbyUI.GameStartReceived)
        {
            var currentScene = SceneManager.GetActiveScene().name;
            var waitMsg = MapStateApplier.ClientWaitingMessage;
            bool suppressForBattle = currentScene == "Battle"
                && _turnIndicatorPanel != null && _turnIndicatorPanel.activeSelf;

            bool isSpectatingScene = currentScene == "PegMinigame" || currentScene == "TextScenario";

            if (!string.IsNullOrEmpty(waitMsg) && !suppressForBattle)
            {
                if (isSpectatingScene)
                {
                    // Transparent top banner — game board visible behind it
                    if (_spectatorBanner == null) CreateSpectatorBanner();
                    _spectatorBanner.SetActive(true);
                    _spectatorBannerText.text = waitMsg;
                    if (_waitingPanel != null) _waitingPanel.SetActive(false);
                }
                else
                {
                    // Dark fullscreen overlay for non-spectatable scenes
                    if (_waitingPanel == null) CreateWaitingPanel();
                    _waitingPanel.SetActive(true);
                    _waitingText.text = waitMsg;
                    if (_spectatorBanner != null) _spectatorBanner.SetActive(false);
                }
            }
            else
            {
                if (_waitingPanel != null) _waitingPanel.SetActive(false);
                if (_spectatorBanner != null) _spectatorBanner.SetActive(false);
            }
        }
        else
        {
            if (_waitingPanel != null) _waitingPanel.SetActive(false);
            if (_spectatorBanner != null) _spectatorBanner.SetActive(false);
        }

        // Coop turn indicator — shown during Battle for both host and client
        UpdateTurnIndicator();
    }

    private void OnDestroy()
    {
        // Transport event cleanup happens via transport.Stop() on disconnect.
        // Lambda subscriptions can't be directly unsubscribed, but the transport
        // is shared and lives for the plugin lifetime alongside this UI.
        if (_canvasObj != null) Destroy(_canvasObj);
    }

    // --- Canvas ---

    private void CreateCanvas()
    {
        // Canvas must be a root-level visible object - NOT parented to the
        // HideAndDontSave mod object, or Unity UI raycasting won't work.
        _canvasObj = new GameObject("MultiplayerMultiplayerCanvas");
        DontDestroyOnLoad(_canvasObj);

        _canvas = _canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasObj.AddComponent<GraphicRaycaster>();
    }

    // --- Overlay ---

    private void CreateOverlay()
    {
        // Full-screen semi-transparent background
        _overlayPanel = new GameObject("OverlayPanel");
        _overlayPanel.transform.SetParent(_canvasObj.transform, false);
        var overlayImg = _overlayPanel.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.85f);
        StretchFill(_overlayPanel.GetComponent<RectTransform>());

        // Centered content panel
        var centerPanel = CreatePanel(_overlayPanel.transform, "CenterPanel",
            new Color(0.12f, 0.12f, 0.12f, 1f), new Vector2(960, 880));

        // Title
        var title = CreateText(centerPanel.transform, "Title", "Multiplayer", 48);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1);
        titleRect.anchorMax = new Vector2(0.5f, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -32);
        titleRect.sizeDelta = new Vector2(640, 64);

        CreateMainPanel(centerPanel.transform);
        CreateHostPanel(centerPanel.transform);
        CreateJoinPanel(centerPanel.transform);
        CreateLobbyPanel(centerPanel.transform);

        ShowMainPanel();
    }

    private void CreateMainPanel(Transform parent)
    {
        _mainPanel = new GameObject("MainPanel");
        _mainPanel.transform.SetParent(parent, false);
        var mainRect = _mainPanel.GetComponent<RectTransform>() ?? _mainPanel.AddComponent<RectTransform>();
        StretchFill(mainRect);

        bool steamAvailable = _steamTransport != null;

        if (steamAvailable)
        {
            _hostSteamButton = CreateButton(_mainPanel.transform, "HostSteamBtn", "Host (Steam)",
                new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, 120), new Vector2(480, 88));
            _hostSteamButton.onClick.AddListener(OnHostSteamClicked);

            _joinFriendButton = CreateButton(_mainPanel.transform, "JoinFriendBtn", "Join Friend",
                new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, 16), new Vector2(480, 88));
            _joinFriendButton.onClick.AddListener(OnJoinFriendClicked);

            var steamStatus = CreateText(_mainPanel.transform, "SteamStatus",
                $"Steam: {LocalPlayerName}", 22);
            steamStatus.color = new Color(0.7f, 0.85f, 0.7f, 1f);
            steamStatus.raycastTarget = false;
            var ssRect = steamStatus.rectTransform;
            ssRect.anchorMin = new Vector2(0.5f, 0.5f);
            ssRect.anchorMax = new Vector2(0.5f, 0.5f);
            ssRect.pivot = new Vector2(0.5f, 0.5f);
            ssRect.anchoredPosition = new Vector2(0, 196);
            ssRect.sizeDelta = new Vector2(480, 30);

            _advancedToggleButton = CreateButton(_mainPanel.transform, "AdvancedToggle",
                "Advanced: Direct IP",
                new Color(0.25f, 0.25f, 0.3f, 1f), new Vector2(0, -64), new Vector2(360, 48));
            _advancedToggleButton.onClick.AddListener(OnAdvancedToggleClicked);

            _advancedIpContainer = new GameObject("AdvancedIpContainer");
            _advancedIpContainer.transform.SetParent(_mainPanel.transform, false);
            var advRect = _advancedIpContainer.AddComponent<RectTransform>();
            advRect.anchorMin = new Vector2(0.5f, 0.5f);
            advRect.anchorMax = new Vector2(0.5f, 0.5f);
            advRect.pivot = new Vector2(0.5f, 0.5f);
            advRect.anchoredPosition = Vector2.zero;
            advRect.sizeDelta = new Vector2(480, 160);

            _hostButton = CreateButton(_advancedIpContainer.transform, "HostBtn", "Host (Direct IP)",
                new Color(0.3f, 0.45f, 0.3f, 1f), new Vector2(0, -116), new Vector2(420, 64));
            _hostButton.onClick.AddListener(OnHostClicked);

            _joinButton = CreateButton(_advancedIpContainer.transform, "JoinBtn", "Join (Direct IP)",
                new Color(0.3f, 0.35f, 0.5f, 1f), new Vector2(0, -184), new Vector2(420, 64));
            _joinButton.onClick.AddListener(OnJoinClicked);

            _advancedIpContainer.SetActive(false);

            var backBtn = CreateButton(_mainPanel.transform, "BackBtn", "Close",
                new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -260), new Vector2(360, 56));
            backBtn.onClick.AddListener(HideOverlay);
        }
        else
        {
            _hostButton = CreateButton(_mainPanel.transform, "HostBtn", "Host Game",
                new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, 40), new Vector2(480, 88));
            _hostButton.onClick.AddListener(OnHostClicked);

            _joinButton = CreateButton(_mainPanel.transform, "JoinBtn", "Join Game",
                new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -64), new Vector2(480, 88));
            _joinButton.onClick.AddListener(OnJoinClicked);

            var backBtn = CreateButton(_mainPanel.transform, "BackBtn", "Close",
                new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -168), new Vector2(480, 88));
            backBtn.onClick.AddListener(HideOverlay);
        }

        // Version text
        var ver = CreateText(_mainPanel.transform, "VersionText",
            $"Multipeglin v{MultiplayerPluginInfo.VERSION}", 40);
        ver.color = Color.white;
        ver.raycastTarget = false;
        var verRect = ver.rectTransform;
        verRect.anchorMin = new Vector2(0.5f, 0.5f);
        verRect.anchorMax = new Vector2(0.5f, 0.5f);
        verRect.pivot = new Vector2(0.5f, 0.5f);
        verRect.anchoredPosition = new Vector2(0, -310);
        verRect.sizeDelta = new Vector2(480, 30);
    }

    private void OnAdvancedToggleClicked()
    {
        _advancedIpVisible = !_advancedIpVisible;
        if (_advancedIpContainer != null)
            _advancedIpContainer.SetActive(_advancedIpVisible);
        if (_advancedToggleButton != null)
        {
            var label = _advancedToggleButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.text = "Advanced: Direct IP";
        }
    }

    private void CreateHostPanel(Transform parent)
    {
        _hostPanel = new GameObject("HostPanel");
        _hostPanel.transform.SetParent(parent, false);
        var hostRect = _hostPanel.GetComponent<RectTransform>() ?? _hostPanel.AddComponent<RectTransform>();
        StretchFill(hostRect);

        _hostInfoText = CreateText(_hostPanel.transform, "HostInfo", "", 29);
        var infoRect = _hostInfoText.rectTransform;
        infoRect.anchorMin = new Vector2(0.5f, 0.5f);
        infoRect.anchorMax = new Vector2(0.5f, 0.5f);
        infoRect.pivot = new Vector2(0.5f, 0.5f);
        infoRect.anchoredPosition = new Vector2(0, 80);
        infoRect.sizeDelta = new Vector2(640, 48);

        _hostPanel.SetActive(false);
    }

    private void CreateJoinPanel(Transform parent)
    {
        _joinPanel = new GameObject("JoinPanel");
        _joinPanel.transform.SetParent(parent, false);
        var joinRect = _joinPanel.GetComponent<RectTransform>() ?? _joinPanel.AddComponent<RectTransform>();
        StretchFill(joinRect);

        // Label
        var label = CreateText(_joinPanel.transform, "Label", "Enter host address (IP:PORT):", 29);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0, 96);
        labelRect.sizeDelta = new Vector2(640, 48);

        // Input field with default value
        _codeInput = CreateInputField(_joinPanel.transform, "CodeInput",
            "IP:PORT", new Vector2(0, 32), new Vector2(560, 72));
        _codeInput.text = $"127.0.0.1:{NetworkConfig.DefaultPort}";

        // Connect button
        var connectBtn = CreateButton(_joinPanel.transform, "ConnectBtn", "Connect",
            new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -104), new Vector2(480, 88));
        connectBtn.onClick.AddListener(OnConnectClicked);

        // Status text
        _statusText = CreateText(_joinPanel.transform, "StatusText", "", 26);
        _statusText.color = new Color(0.8f, 0.8f, 0.3f, 1f);
        var statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, -184);
        statusRect.sizeDelta = new Vector2(640, 48);

        // Cancel button
        var cancelBtn = CreateButton(_joinPanel.transform, "CancelBtn", "Back",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -248), new Vector2(480, 88));
        cancelBtn.onClick.AddListener(OnCancelJoinClicked);

        // Version text
        var ver = CreateText(_joinPanel.transform, "VersionText",
            $"Multipeglin v{MultiplayerPluginInfo.VERSION}", 40);
        ver.color = Color.white;
        ver.raycastTarget = false;
        var verRect = ver.rectTransform;
        verRect.anchorMin = new Vector2(0.5f, 0.5f);
        verRect.anchorMax = new Vector2(0.5f, 0.5f);
        verRect.pivot = new Vector2(0.5f, 0.5f);
        verRect.anchoredPosition = new Vector2(0, -310);
        verRect.sizeDelta = new Vector2(480, 30);

        _joinPanel.SetActive(false);
    }

    private void CreateLobbyPanel(Transform parent)
    {
        _lobbyPanel = new GameObject("LobbyPanel");
        _lobbyPanel.transform.SetParent(parent, false);
        var rect = _lobbyPanel.GetComponent<RectTransform>() ?? _lobbyPanel.AddComponent<RectTransform>();
        StretchFill(rect);

        // Status line
        _lobbyStatusText = CreateText(_lobbyPanel.transform, "LobbyStatus", "", 22);
        _lobbyStatusText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        var statusRect = _lobbyStatusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, 160);
        statusRect.sizeDelta = new Vector2(640, 36);

        // Persistent join-link line (steam://joinlobby/... when hosting via Steam)
        _lobbyJoinLinkText = CreateText(_lobbyPanel.transform, "LobbyJoinLink", "", 16);
        _lobbyJoinLinkText.color = new Color(0.55f, 0.75f, 0.95f, 1f);
        _lobbyJoinLinkText.enableWordWrapping = true;
        var linkRect = _lobbyJoinLinkText.rectTransform;
        linkRect.anchorMin = new Vector2(0.5f, 0.5f);
        linkRect.anchorMax = new Vector2(0.5f, 0.5f);
        linkRect.pivot = new Vector2(0.5f, 0.5f);
        linkRect.anchoredPosition = new Vector2(0, 120);
        linkRect.sizeDelta = new Vector2(780, 44);

        // Invite Friend button (only meaningful when hosting via Steam; toggled in UpdateLobbyPanel)
        _inviteFriendButton = CreateButton(_lobbyPanel.transform, "InviteFriendBtn", "Invite Friend",
            new Color(0.2f, 0.45f, 0.55f, 1f), new Vector2(0, -345), new Vector2(400, 56));
        _inviteFriendButton.onClick.AddListener(OnInviteFriendClicked);
        _inviteFriendButton.gameObject.SetActive(false);

        // Disconnect button at the bottom
        var disconnectBtn = CreateButton(_lobbyPanel.transform, "DisconnectBtn", "Disconnect",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(0, -405), new Vector2(400, 56));
        disconnectBtn.onClick.AddListener(OnDisconnectClicked);

        _lobbyPanel.SetActive(false);
    }

    private void OnInviteFriendClicked()
    {
        Log?.LogInfo("[Invite] Clicked");
        if (_steamTransport == null)
        {
            Log?.LogWarning("[Invite] No Steam transport");
            return;
        }
        var lobbyId = _steamTransport.HostedLobbyId;
        if (!lobbyId.IsValid())
        {
            Log?.LogWarning("[Invite] No hosted lobby id");
            return;
        }

        uint appId = 0;
        try { appId = SteamUtils.GetAppID().m_AppId; } catch { }
        string joinUrl = $"steam://joinlobby/{appId}/{lobbyId.m_SteamID}/{SteamUser.GetSteamID().m_SteamID}";

        bool overlayEnabled = false;
        try { overlayEnabled = SteamUtils.IsOverlayEnabled(); } catch (Exception ex) { Log?.LogWarning($"[Invite] IsOverlayEnabled threw: {ex.Message}"); }
        Log?.LogInfo($"[Invite] appId={appId} lobby={lobbyId.m_SteamID} overlayEnabled={overlayEnabled} joinUrl={joinUrl}");

        if (overlayEnabled)
        {
            try
            {
                SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
                Log?.LogInfo("[Invite] ActivateGameOverlayInviteDialog called");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Invite] ActivateGameOverlayInviteDialog failed: {ex}");
            }
            return;
        }

        // Overlay unavailable (common under Proton when game wasn't launched via Steam):
        // copy the join URL to clipboard and flash confirmation so the host can paste it
        // to a friend manually via Steam chat.
        try { GUIUtility.systemCopyBuffer = joinUrl; } catch (Exception ex) { Log?.LogWarning($"[Invite] Clipboard copy failed: {ex.Message}"); }
        _inviteCopiedFlashUntil = Time.unscaledTime + 3f;
    }

    private void CreateFriendListPanel(Transform parent)
    {
        _friendListPanel = new GameObject("FriendListPanel");
        _friendListPanel.transform.SetParent(parent, false);
        var rect = _friendListPanel.GetComponent<RectTransform>() ?? _friendListPanel.AddComponent<RectTransform>();
        StretchFill(rect);

        var title = CreateText(_friendListPanel.transform, "FriendsTitle", "Friends in Peglin", 30);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0, 210);
        titleRect.sizeDelta = new Vector2(640, 40);

        // Scroll viewport
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(_friendListPanel.transform, false);
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(0.08f, 0.08f, 0.1f, 1f);
        viewport.AddComponent<RectMask2D>();
        var vpRect = viewport.GetComponent<RectTransform>();
        vpRect.anchorMin = new Vector2(0.5f, 0.5f);
        vpRect.anchorMax = new Vector2(0.5f, 0.5f);
        vpRect.pivot = new Vector2(0.5f, 0.5f);
        vpRect.anchoredPosition = new Vector2(0, 0);
        vpRect.sizeDelta = new Vector2(760, 340);

        _friendListContent = new GameObject("Content");
        _friendListContent.transform.SetParent(viewport.transform, false);
        var contentRect = _friendListContent.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);

        _friendListStatusText = CreateText(_friendListPanel.transform, "FriendsStatus", "", 22);
        _friendListStatusText.color = new Color(0.8f, 0.8f, 0.3f, 1f);
        var stRect = _friendListStatusText.rectTransform;
        stRect.anchorMin = new Vector2(0.5f, 0.5f);
        stRect.anchorMax = new Vector2(0.5f, 0.5f);
        stRect.pivot = new Vector2(0.5f, 0.5f);
        stRect.anchoredPosition = new Vector2(0, -195);
        stRect.sizeDelta = new Vector2(640, 36);

        var refreshBtn = CreateButton(_friendListPanel.transform, "RefreshBtn", "Refresh",
            new Color(0.25f, 0.4f, 0.55f, 1f), new Vector2(-160, -250), new Vector2(260, 56));
        refreshBtn.onClick.AddListener(PopulateFriendList);

        var backBtn = CreateButton(_friendListPanel.transform, "FriendsBackBtn", "Back",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(160, -250), new Vector2(260, 56));
        backBtn.onClick.AddListener(() =>
        {
            _friendListPanel.SetActive(false);
            ShowMainPanel();
        });

        _friendListPanel.SetActive(false);
    }

    private void ShowFriendListPanel()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _lobbyPanel.SetActive(false);

        if (_friendListPanel == null)
        {
            // Use the same parent as the other panels
            var parent = _mainPanel.transform.parent;
            CreateFriendListPanel(parent);
        }
        _friendListPanel.SetActive(true);
        PopulateFriendList();
    }

    private void PopulateFriendList()
    {
        if (_friendListContent == null) return;

        // Clear existing rows
        for (int i = _friendListContent.transform.childCount - 1; i >= 0; i--)
            Destroy(_friendListContent.transform.GetChild(i).gameObject);

        int found = 0;
        try
        {
            int total = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < total; i++)
            {
                var fid = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(fid, out FriendGameInfo_t info)) continue;
                if (info.m_gameID.AppID() != _currentAppId) continue;
                if (!info.m_steamIDLobby.IsValid()) continue;

                var name = SteamFriends.GetFriendPersonaName(fid);
                AddFriendRow(found, name, info.m_steamIDLobby);
                found++;
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"PopulateFriendList failed: {ex}");
            _friendListStatusText.text = "Failed to read Steam friends list.";
            return;
        }

        _friendListStatusText.text = found == 0
            ? "No friends are hosting a Multipeglin lobby right now."
            : $"{found} friend{(found == 1 ? "" : "s")} hosting.";
    }

    private void AddFriendRow(int index, string name, CSteamID lobbyId)
    {
        var row = new GameObject($"Friend_{index}");
        row.transform.SetParent(_friendListContent.transform, false);
        var rRect = row.AddComponent<RectTransform>();
        rRect.anchorMin = new Vector2(0, 1);
        rRect.anchorMax = new Vector2(1, 1);
        rRect.pivot = new Vector2(0.5f, 1);
        rRect.anchoredPosition = new Vector2(0, -index * 64 - 8);
        rRect.sizeDelta = new Vector2(-20, 56);

        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        var nameText = CreateText(row.transform, "Name", name, 26);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        var nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = new Vector2(20, 0);
        nameRect.offsetMax = new Vector2(-180, 0);

        var joinBtn = CreateButton(row.transform, "JoinBtn", "Join",
            new Color(0.2f, 0.45f, 0.25f, 1f), Vector2.zero, new Vector2(140, 44));
        var jbRect = joinBtn.GetComponent<RectTransform>();
        jbRect.anchorMin = new Vector2(1, 0.5f);
        jbRect.anchorMax = new Vector2(1, 0.5f);
        jbRect.pivot = new Vector2(1, 0.5f);
        jbRect.anchoredPosition = new Vector2(-16, 0);
        var lobbyCapture = lobbyId;
        joinBtn.onClick.AddListener(() => OnJoinLobbyClicked(lobbyCapture));
    }

    private void OnJoinLobbyClicked(CSteamID lobbyId)
    {
        if (_steamTransport == null) return;
        try
        {
            _router?.UseSteam();
            _multiplayerMode.EnableSpectating();
            Utility.FileLogger.RoleTag = "CLIENT";
            if (_multiplayerMode is MultiplayerMode mode)
                mode.ClientMode = ClientMode.Mirror;
            _steamTransport.JoinSteamLobby(lobbyId);
            _friendListStatusText.text = "Joining...";
            Log.LogInfo($"Joining Steam lobby {lobbyId.m_SteamID}");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to join Steam lobby: {ex}");
            _friendListStatusText.text = $"Error: {ex.Message}";
        }
    }

    private void UpdateLobbyPanel()
    {
        if (_lobbyStatusText == null) return;

        bool steamActive = _router != null && _router.ActiveIsSteam;
        bool flashActive = Time.unscaledTime < _inviteCopiedFlashUntil;
        if (flashActive)
        {
            _lobbyStatusText.text = "Join link copied to clipboard — paste to a friend in Steam chat";
        }
        else if (_multiplayerMode.IsHosting)
        {
            _lobbyStatusText.text = steamActive
                ? "Hosting via Steam lobby"
                : $"Hosting on port {NetworkConfig.DefaultPort}";
        }
        else if (_transport.IsConnected)
            _lobbyStatusText.text = "Connected";
        else
            _lobbyStatusText.text = "Connecting...";

        // Invite Friend button: only while hosting via Steam
        if (_inviteFriendButton != null)
            _inviteFriendButton.gameObject.SetActive(_multiplayerMode.IsHosting && steamActive);

        // Join-link line: shown only as a fallback when the Steam overlay is unavailable
        // (e.g. running under Proton or launched outside Steam). When the overlay works,
        // the Invite Friend button opens the normal Steam invite dialog and no URL is shown.
        if (_lobbyJoinLinkText != null)
        {
            bool show = false;
            if (_multiplayerMode.IsHosting && steamActive && _steamTransport != null)
            {
                var lobbyId = _steamTransport.HostedLobbyId;
                bool overlayEnabled = false;
                try { overlayEnabled = SteamUtils.IsOverlayEnabled(); } catch { }
                if (lobbyId.IsValid() && !overlayEnabled)
                {
                    uint appId = 0;
                    try { appId = SteamUtils.GetAppID().m_AppId; } catch { }
                    _lobbyJoinLinkText.text =
                        $"steam://joinlobby/{appId}/{lobbyId.m_SteamID}/{SteamUser.GetSteamID().m_SteamID}";
                    show = true;
                }
            }
            _lobbyJoinLinkText.gameObject.SetActive(show);
        }

        // Delegate to LobbyUI for the class select / ready system
        LobbyUI.UpdateLobbyUI(
            _lobbyPanel.transform,
            _multiplayerMode.IsHosting,
            (parent, name, text, size) => CreateText(parent, name, text, size),
            (parent, name, text, color, pos, sz) => CreateButton(parent, name, text, color, pos, sz));

        // Check if game start was triggered (host or client)
        if (LobbyUI.GameStartReceived)
        {
            HideOverlay();
        }
    }

    private void OnDisconnectClicked()
    {
        Multiplayer.MultiplayerSession.DisconnectAndReset("User clicked disconnect");
        if (_multiplayerPanel != null)
            _multiplayerPanel.SetActive(false);
        if (_waitingPanel != null)
            _waitingPanel.SetActive(false);
        if (_turnIndicatorPanel != null)
            _turnIndicatorPanel.SetActive(false);
        _overlayPanel.SetActive(true);
        ShowMainPanel();
    }

    private void CreateMultiplayerPanel()
    {
        // Fullscreen event feed for the client — created on the canvas directly, not inside centerPanel
        _multiplayerPanel = new GameObject("MultiplayerPanel");
        _multiplayerPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _multiplayerPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);
        StretchFill(_multiplayerPanel.GetComponent<RectTransform>());

        // Title bar
        var title = CreateText(_multiplayerPanel.transform, "MultiplayerTitle", "Spectating — Event Feed", 42);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -10);
        titleRect.sizeDelta = new Vector2(0, 48);

        // Scrolling event text
        _multiplayerFeedText = CreateText(_multiplayerPanel.transform, "FeedText", "", 36);
        _multiplayerFeedText.alignment = TextAlignmentOptions.TopLeft;
        _multiplayerFeedText.enableWordWrapping = true;
        _multiplayerFeedText.overflowMode = TextOverflowModes.Truncate;
        var feedRect = _multiplayerFeedText.rectTransform;
        feedRect.anchorMin = new Vector2(0, 0);
        feedRect.anchorMax = new Vector2(1, 1);
        feedRect.offsetMin = new Vector2(20, 80);
        feedRect.offsetMax = new Vector2(-20, -64);

        // Disconnect button at bottom
        var disconnectBtn = CreateButton(_multiplayerPanel.transform, "MultiplayerDisconnect", "Disconnect",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(0, 0), new Vector2(300, 60));
        var dcRect = disconnectBtn.GetComponent<RectTransform>();
        dcRect.anchorMin = new Vector2(0.5f, 0);
        dcRect.anchorMax = new Vector2(0.5f, 0);
        dcRect.pivot = new Vector2(0.5f, 0);
        dcRect.anchoredPosition = new Vector2(0, 10);
        disconnectBtn.onClick.AddListener(OnDisconnectClicked);

        _multiplayerPanel.SetActive(false);
    }

    private void CreateWaitingPanel()
    {
        _waitingPanel = new GameObject("WaitingPanel");
        _waitingPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _waitingPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.1f, 0.92f);
        StretchFill(_waitingPanel.GetComponent<RectTransform>());

        _waitingText = CreateText(_waitingPanel.transform, "WaitingText", "", 42);
        _waitingText.alignment = TextAlignmentOptions.Center;
        var rect = _waitingText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(800, 200);

        _waitingPanel.SetActive(false);
    }

    private void CreateSpectatorBanner()
    {
        _spectatorBanner = new GameObject("SpectatorBanner");
        _spectatorBanner.transform.SetParent(_canvasObj.transform, false);
        var bg = _spectatorBanner.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.15f, 0.7f);

        var bannerRect = _spectatorBanner.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0, 1);
        bannerRect.anchorMax = new Vector2(1, 1);
        bannerRect.pivot = new Vector2(0.5f, 1);
        bannerRect.anchoredPosition = Vector2.zero;
        bannerRect.sizeDelta = new Vector2(0, 60);

        _spectatorBannerText = CreateText(_spectatorBanner.transform, "SpectatorText", "", 32);
        _spectatorBannerText.alignment = TextAlignmentOptions.Center;
        var textRect = _spectatorBannerText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);

        _spectatorBanner.SetActive(false);
    }

    private void UpdateTurnIndicator()
    {
        var scene = SceneManager.GetActiveScene().name;
        var turnMsg = Events.Handlers.Coop.TurnChangeClientHandler.TurnMessage;

        bool shouldShow = scene == "Battle"
            && _transport != null && _transport.IsConnected
            && LobbyUI.GameStartReceived
            && !string.IsNullOrEmpty(turnMsg);

        if (shouldShow)
        {
            if (_turnIndicatorPanel == null)
                CreateTurnIndicator();
            _turnIndicatorPanel.SetActive(true);
            _turnIndicatorText.text = turnMsg;
        }
        else if (_turnIndicatorPanel != null)
        {
            _turnIndicatorPanel.SetActive(false);
        }
    }

    private void CreateTurnIndicator()
    {
        _turnIndicatorPanel = new GameObject("TurnIndicatorPanel");
        _turnIndicatorPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _turnIndicatorPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.15f, 0.75f);
        bg.raycastTarget = false;
        var panelRect = _turnIndicatorPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.25f, 0.92f);
        panelRect.anchorMax = new Vector2(0.75f, 1f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        _turnIndicatorText = CreateText(_turnIndicatorPanel.transform, "TurnText", "", 28);
        _turnIndicatorText.alignment = TextAlignmentOptions.Center;
        _turnIndicatorText.raycastTarget = false;
        var textRect = _turnIndicatorText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 2);
        textRect.offsetMax = new Vector2(-10, -2);

        _turnIndicatorPanel.SetActive(false);
    }


    private void ShowMultiplayerView()
    {
        _overlayPanel.SetActive(false);

        if (_multiplayerMode.ClientMode == ClientMode.Diagnostics)
        {
            // Diagnostics mode: show fullscreen event feed
            if (_multiplayerPanel == null)
                CreateMultiplayerPanel();
            _multiplayerPanel.SetActive(true);
            _overlayVisible = true;
            EventFeed.Clear();
            _lastFeedVersion = -1;
        }
        else
        {
            // Mirror mode: hide everything so the game renders normally
            if (_multiplayerPanel != null)
                _multiplayerPanel.SetActive(false);
            _overlayVisible = false;
        }
    }

    // --- Panel switching ---

    private void ShowMainPanel()
    {
        _mainPanel.SetActive(true);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _lobbyPanel.SetActive(false);
        if (_friendListPanel != null) _friendListPanel.SetActive(false);
    }

    private void ShowLobby()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _lobbyPanel.SetActive(true);
        if (_friendListPanel != null) _friendListPanel.SetActive(false);
        // Hide the waiting panel (MapStateApplier shows it for MainMenu)
        if (_waitingPanel != null)
            _waitingPanel.SetActive(false);
        UpdateLobbyPanel();
    }

    private void ShowOverlay()
    {
        _overlayVisible = true;

        // If in a multiplayer session (hosting or spectating) and game has started,
        // show the spectator/diagnostics view
        if (_multiplayerMode.IsSpectating && _transport.IsConnected && LobbyUI.GameStartReceived)
        {
            ShowMultiplayerView();
            return;
        }

        _overlayPanel.SetActive(true);

        // If connected (either hosting or spectating), show lobby
        if ((_multiplayerMode.IsHosting || _multiplayerMode.IsSpectating) && _transport.IsConnected)
            ShowLobby();
        else if (_multiplayerMode.IsHosting)
            ShowLobby();
        else
            ShowMainPanel();
    }

    private void HideOverlay()
    {
        _overlayVisible = false;
        _overlayPanel.SetActive(false);
    }

    private void ToggleOverlay()
    {
        if (_overlayVisible) HideOverlay();
        else ShowOverlay();
    }

    public static void ToggleOverlayStatic()
    {
        try
        {
            if (_instance != null)
                _instance.ToggleOverlay();
            else
                MultiplayerPlugin.Logger?.LogWarning("MultiplayerUI: instance is null, cannot toggle overlay");
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"MultiplayerUI.ToggleOverlay crashed: {ex}");
        }
    }

    private void ShowJoinPanel()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(true);
        _lobbyPanel.SetActive(false);
        if (_friendListPanel != null) _friendListPanel.SetActive(false);
        _statusText.text = _lastConnectionStatus;
    }

    private void ShowHostingState()
    {
        ShowLobby();
    }

    // --- Button handlers ---

    private void OnHostClicked()
    {
        try
        {
            _router?.UseLite();
            _multiplayerMode.EnableHosting();
            _transport.StartHost(NetworkConfig.DefaultPort);
            Utility.FileLogger.RoleTag = "HOST";

            // Register host in PlayerRegistry
            if (MultiplayerPlugin.Services?.TryResolve<Multiplayer.PlayerRegistry>(out var registry) == true)
                registry.RegisterHost(LocalPlayerName, Application.version ?? "unknown", MultiplayerPluginInfo.VERSION);

            Log.LogInfo($"Started hosting on port {NetworkConfig.DefaultPort}");
            ShowLobby();
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start hosting: {ex}");
            _lastConnectionStatus = $"Error: {ex.Message}";
            ShowMainPanel();
        }
    }

    private void OnHostSteamClicked()
    {
        if (_steamTransport == null || _router == null)
        {
            Log?.LogWarning("Host (Steam) clicked but Steam transport unavailable");
            return;
        }
        try
        {
            _router.UseSteam();
            _multiplayerMode.EnableHosting();
            _steamTransport.StartHost(0);
            Utility.FileLogger.RoleTag = "HOST";

            if (MultiplayerPlugin.Services?.TryResolve<Multiplayer.PlayerRegistry>(out var registry) == true)
                registry.RegisterHost(LocalPlayerName, Application.version ?? "unknown", MultiplayerPluginInfo.VERSION);

            Log.LogInfo("Started Steam lobby hosting");
            ShowLobby();
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start Steam hosting: {ex}");
            _lastConnectionStatus = $"Error: {ex.Message}";
            ShowMainPanel();
        }
    }

    private void OnJoinFriendClicked()
    {
        if (_steamTransport == null)
        {
            Log?.LogWarning("Join Friend clicked but Steam transport unavailable");
            return;
        }
        ShowFriendListPanel();
    }

    private void OnSteamInviteReceived(CSteamID lobbyId)
    {
        // Steam delivers lobby invites via callback on an arbitrary thread —
        // marshal to the main thread before touching Unity objects.
        var dispatcher = Utility.MainThreadDispatcher.Instance;
        if (dispatcher != null)
            dispatcher.Enqueue(() => PromptAcceptInvite(lobbyId));
        else
            PromptAcceptInvite(lobbyId);
    }

    private void PromptAcceptInvite(CSteamID lobbyId)
    {
        // Skip the dialog if the user is already connected/hosting — a stale
        // invite arriving mid-session shouldn't interrupt the game.
        if (_multiplayerMode.IsHosting || _multiplayerMode.IsSpectating || _transport.IsConnected)
        {
            Log?.LogInfo($"[Steam] Ignoring incoming invite {lobbyId.m_SteamID}: already in session");
            return;
        }

        string inviterName = "A friend";
        try
        {
            var owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            if (owner.IsValid())
            {
                var name = SteamFriends.GetFriendPersonaName(owner);
                if (!string.IsNullOrEmpty(name)) inviterName = name;
            }
        }
        catch { }

        ShowConfirmDialog(
            $"{inviterName} invited you to a Multipeglin lobby.\nJoin them?",
            onAccept: () => AcceptSteamInvite(lobbyId),
            onDecline: () => Log?.LogInfo($"[Steam] Declined invite to {lobbyId.m_SteamID}"));
    }

    private void AcceptSteamInvite(CSteamID lobbyId)
    {
        try
        {
            _router?.UseSteam();
            _multiplayerMode.EnableSpectating();
            Utility.FileLogger.RoleTag = "CLIENT";
            if (_multiplayerMode is MultiplayerMode mode)
                mode.ClientMode = ClientMode.Mirror;

            _steamTransport?.JoinSteamLobby(lobbyId);

            _overlayPanel.SetActive(true);
            ShowLobby();
            Log.LogInfo($"[Steam] Accepted invite, joining lobby {lobbyId.m_SteamID}");
        }
        catch (Exception ex)
        {
            Log.LogError($"AcceptSteamInvite failed: {ex}");
        }
    }

    private void OnJoinClicked()
    {
        _lastConnectionStatus = "";
        ShowJoinPanel();
    }

    private void OnConnectClicked()
    {
        var code = _codeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            _statusText.text = "Please enter a host code.";
            return;
        }

        if (!TryParseHostCode(code, out var ip, out var port))
        {
            _statusText.text = "Invalid format. Use IP:PORT";
            return;
        }

        try
        {
            _statusText.text = $"Connecting to {ip}:{port}...";
            _router?.UseLite();
            _multiplayerMode.EnableSpectating();
            Utility.FileLogger.RoleTag = "CLIENT";
            if (_multiplayerMode is MultiplayerMode mode)
                mode.ClientMode = ClientMode.Mirror;
            _transport.Connect(ip, port);
            Log.LogInfo($"Connecting to {ip}:{port}");
            // OnConnected callback will switch to lobby panel
        }
        catch (Exception ex)
        {
            _statusText.text = $"Error: {ex.Message}";
            _lastConnectionStatus = _statusText.text;
            Log.LogError($"Failed to connect: {ex}");
        }
    }

    private void OnCancelJoinClicked()
    {
        ShowMainPanel();
    }

    // --- Network callbacks ---

    private void OnConnected()
    {
        _lastConnectionStatus = "Connected!";

        // Both host and client: show the lobby for class selection and ready-up
        _overlayPanel.SetActive(true);
        ShowLobby();

        Log.LogInfo("Connected — showing lobby");
    }

    private void OnDisconnected(int peerId)
    {
        // Host in lobby (game not yet started): a single client dropping should
        // not tear down the lobby. ServiceRegistration's host-side OnDisconnected
        // already removes the slot from PlayerRegistry; we just need to rebroadcast
        // lobby state so the host UI stops showing the dropped client.
        if (_multiplayerMode != null && _multiplayerMode.IsHosting && !LobbyUI.GameStartReceived)
        {
            Log?.LogInfo($"[Lobby] Client peer {peerId} disconnected during lobby — keeping lobby open");
            var dispatcher = Utility.MainThreadDispatcher.Instance;
            Action rebroadcast = () =>
            {
                try
                {
                    var services = MultiplayerPlugin.Services;
                    if (services != null
                        && services.TryResolve<Multiplayer.PlayerRegistry>(out var reg)
                        && services.TryResolve<Multipeglin.Events.IGameEventRegistry>(out var er))
                    {
                        Multipeglin.Events.Handlers.Lobby.LobbyHelper.BroadcastLobbyState(reg, er);
                    }
                }
                catch (Exception ex) { Log?.LogWarning($"[Lobby] Rebroadcast after disconnect failed: {ex.Message}"); }
            };
            if (dispatcher != null) dispatcher.Enqueue(rebroadcast);
            else rebroadcast();
            return;
        }

        _lastConnectionStatus = "Disconnected";
        Log?.LogInfo("Transport disconnected — scheduling full reset");

        // Queue the reset to next frame to avoid calling transport.Stop() from
        // within the PollEvents callback that fired this event.
        var disp = Utility.MainThreadDispatcher.Instance;
        if (disp != null)
        {
            disp.Enqueue(HandleDisconnectReset);
        }
        else
        {
            HandleDisconnectReset();
        }
    }

    private void HandleDisconnectReset()
    {
        Multiplayer.MultiplayerSession.DisconnectAndReset("Transport disconnected");

        if (_multiplayerPanel != null)
            _multiplayerPanel.SetActive(false);
        if (_waitingPanel != null)
            _waitingPanel.SetActive(false);
        if (_turnIndicatorPanel != null)
            _turnIndicatorPanel.SetActive(false);
        _overlayPanel?.SetActive(true);
        ShowMainPanel();
    }

    private void OnConnectionRejected(string reason)
    {
        Log?.LogWarning($"Connection rejected (reason={reason}). Local version: {MultiplayerPluginInfo.VERSION}");

        var dispatcher = Utility.MainThreadDispatcher.Instance;
        if (dispatcher != null)
            dispatcher.Enqueue(() => HandleConnectionRejected(reason));
        else
            HandleConnectionRejected(reason);
    }

    private void HandleConnectionRejected(string reason)
    {
        // Clean up transport and mode without full DisconnectAndReset (we never left main menu)
        _transport.Stop();
        _multiplayerMode.Disable();
        Utility.FileLogger.RoleTag = null;

        if (reason == "full")
        {
            ShowErrorDialog(
                "Game is at the maximum number of players,\nunable to join.");
        }
        else
        {
            ShowErrorDialog(
                "Failed to connect to host.\n" +
                $"Your version: {MultiplayerPluginInfo.VERSION}\n\n" +
                "Make sure you and the host have the\nsame version of Multipeglin installed.");
        }
    }

    private void ShowErrorDialog(string message)
    {
        if (_errorPanel != null)
            Destroy(_errorPanel);

        _errorPanel = new GameObject("ErrorPanel");
        _errorPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _errorPanel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.9f);
        StretchFill(_errorPanel.GetComponent<RectTransform>());

        var box = CreatePanel(_errorPanel.transform, "ErrorBox",
            new Color(0.15f, 0.1f, 0.1f, 1f), new Vector2(700, 280));

        var msgText = CreateText(box.transform, "ErrorMessage", message, 26);
        msgText.enableWordWrapping = true;
        var msgRect = msgText.rectTransform;
        msgRect.anchorMin = new Vector2(0.5f, 0.5f);
        msgRect.anchorMax = new Vector2(0.5f, 0.5f);
        msgRect.pivot = new Vector2(0.5f, 0.5f);
        msgRect.anchoredPosition = new Vector2(0, 30);
        msgRect.sizeDelta = new Vector2(620, 160);

        var okBtn = CreateButton(box.transform, "OkBtn", "Ok",
            new Color(0.3f, 0.3f, 0.5f, 1f), new Vector2(0, -100), new Vector2(200, 56));
        okBtn.onClick.AddListener(() =>
        {
            Destroy(_errorPanel);
            _errorPanel = null;
        });
    }

    private void ShowConfirmDialog(string message, Action onAccept, Action onDecline)
    {
        if (_errorPanel != null)
            Destroy(_errorPanel);

        _errorPanel = new GameObject("ConfirmPanel");
        _errorPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _errorPanel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.9f);
        StretchFill(_errorPanel.GetComponent<RectTransform>());

        var box = CreatePanel(_errorPanel.transform, "ConfirmBox",
            new Color(0.1f, 0.15f, 0.2f, 1f), new Vector2(700, 280));

        var msgText = CreateText(box.transform, "ConfirmMessage", message, 26);
        msgText.enableWordWrapping = true;
        var msgRect = msgText.rectTransform;
        msgRect.anchorMin = new Vector2(0.5f, 0.5f);
        msgRect.anchorMax = new Vector2(0.5f, 0.5f);
        msgRect.pivot = new Vector2(0.5f, 0.5f);
        msgRect.anchoredPosition = new Vector2(0, 30);
        msgRect.sizeDelta = new Vector2(620, 160);

        var acceptBtn = CreateButton(box.transform, "AcceptBtn", "Join",
            new Color(0.2f, 0.5f, 0.3f, 1f), new Vector2(-110, -100), new Vector2(200, 56));
        acceptBtn.onClick.AddListener(() =>
        {
            Destroy(_errorPanel);
            _errorPanel = null;
            try { onAccept?.Invoke(); } catch (Exception ex) { Log?.LogError($"ConfirmDialog onAccept threw: {ex}"); }
        });

        var declineBtn = CreateButton(box.transform, "DeclineBtn", "Decline",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(110, -100), new Vector2(200, 56));
        declineBtn.onClick.AddListener(() =>
        {
            Destroy(_errorPanel);
            _errorPanel = null;
            try { onDecline?.Invoke(); } catch (Exception ex) { Log?.LogError($"ConfirmDialog onDecline threw: {ex}"); }
        });
    }

    // --- Helpers ---

    private static string GetLocalIP()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static bool TryParseHostCode(string code, out string ip, out int port)
    {
        ip = null;
        port = 0;

        // Support plain IP (use default port) or IP:PORT
        var colonIdx = code.LastIndexOf(':');
        if (colonIdx < 0)
        {
            ip = code;
            port = NetworkConfig.DefaultPort;
            return !string.IsNullOrWhiteSpace(ip);
        }

        ip = code.Substring(0, colonIdx);
        var portStr = code.Substring(colonIdx + 1);
        return !string.IsNullOrWhiteSpace(ip) && int.TryParse(portStr, out port) && port > 0 && port <= 65535;
    }

    // --- UI Factory Methods ---

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.richText = true;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label,
        Color bgColor, Vector2 position, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var img = obj.AddComponent<Image>();
        img.color = bgColor;

        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(
            Mathf.Min(bgColor.r + 0.15f, 1f),
            Mathf.Min(bgColor.g + 0.15f, 1f),
            Mathf.Min(bgColor.b + 0.15f, 1f), 1f);
        colors.pressedColor = new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f, 1f);
        btn.colors = colors;

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var text = CreateText(obj.transform, "Label", label, 35);
        StretchFill(text.rectTransform);

        return btn;
    }

    private static TMP_InputField CreateInputField(Transform parent, string name,
        string placeholder, Vector2 position, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var bg = obj.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // Text area (where typed text appears)
        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(obj.transform, false);
        var textAreaRect = textArea.AddComponent<RectTransform>();
        StretchFill(textAreaRect);
        textAreaRect.offsetMin = new Vector2(10, 5);
        textAreaRect.offsetMax = new Vector2(-10, -5);
        textArea.AddComponent<RectMask2D>();

        // Input text
        var inputTextObj = new GameObject("Text");
        inputTextObj.transform.SetParent(textArea.transform, false);
        var inputText = inputTextObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 32;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFill(inputText.rectTransform);

        // Placeholder text
        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        var placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 32;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFill(placeholderText.rectTransform);

        // Wire up the TMP_InputField
        var inputField = obj.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputText;
        inputField.placeholder = placeholderText;
        inputField.fontAsset = inputText.font;
        inputField.pointSize = 32;
        inputField.characterLimit = 50;

        return inputField;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        var img = obj.AddComponent<Image>();
        img.color = color;

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        return obj;
    }

    private static Toggle CreateToggle(Transform parent, string name, string label,
        Vector2 position, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // Background box
        var bgObj = new GameObject("Background");
        bgObj.transform.SetParent(obj.transform, false);
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.5f);
        bgRect.anchorMax = new Vector2(0, 0.5f);
        bgRect.pivot = new Vector2(0, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 0);
        bgRect.sizeDelta = new Vector2(32, 32);

        // Checkmark
        var checkObj = new GameObject("Checkmark");
        checkObj.transform.SetParent(bgObj.transform, false);
        var checkImg = checkObj.AddComponent<Image>();
        checkImg.color = new Color(0.3f, 0.8f, 0.3f, 1f);
        var checkRect = checkObj.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.15f, 0.15f);
        checkRect.anchorMax = new Vector2(0.85f, 0.85f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;

        var toggle = obj.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.isOn = false;

        // Label text to the right of the checkbox
        var labelText = CreateText(obj.transform, "Label", label, 22);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        var labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(42, 0);
        labelRect.offsetMax = Vector2.zero;

        return toggle;
    }

    private static void StretchFill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
