using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BepInEx.Logging;
using PeglinMods.Spectator.DI;
using PeglinMods.Spectator.Events.Handlers;
using PeglinMods.Spectator.Network;
using PeglinMods.Spectator.Spectator;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinMods.Spectator.UI;

public class MultiplayerUI : MonoBehaviour
{
    private static ManualLogSource Log => SpectatorPlugin.Logger;

    // Services
    private INetworkTransport _transport;
    private ISpectatorMode _spectatorMode;

    // Root canvas
    private GameObject _canvasObj;
    private Canvas _canvas;

    // Overlay panels
    private GameObject _overlayPanel;
    private GameObject _mainPanel;
    private GameObject _hostPanel;
    private GameObject _joinPanel;
    private GameObject _lobbyPanel;
    private GameObject _spectatorPanel;

    // Main panel elements
    private TMP_InputField _nameInput;
    private Button _hostButton;
    private Button _joinButton;

    // Join panel elements
    private TMP_InputField _codeInput;
    private TextMeshProUGUI _statusText;

    // Host panel elements
    private TextMeshProUGUI _hostInfoText;

    // Lobby panel elements
    private TextMeshProUGUI _lobbyListText;
    private TextMeshProUGUI _lobbyStatusText;
    private Button _playButton;

    // Spectator panel elements (client fullscreen event feed)
    private TextMeshProUGUI _spectatorFeedText;
    private int _lastFeedVersion;

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
            _transport = SpectatorPlugin.Services.Resolve<INetworkTransport>();
            _spectatorMode = SpectatorPlugin.Services.Resolve<ISpectatorMode>();

            _transport.OnClientConnected += OnConnected;
            _transport.OnDisconnected += OnDisconnected;

            CreateCanvas();
            CreateOverlay();
            HideOverlay();

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

        // Refresh spectator feed when visible and new events arrived
        if (_spectatorPanel != null && _spectatorPanel.activeSelf && EventFeed.Version != _lastFeedVersion)
        {
            _lastFeedVersion = EventFeed.Version;
            _spectatorFeedText.text = EventFeed.GetText(40);
        }
    }

    private void OnDestroy()
    {
        if (_transport != null)
        {
            _transport.OnClientConnected -= OnConnected;
            _transport.OnDisconnected -= OnDisconnected;
        }
        if (_canvasObj != null) Destroy(_canvasObj);
    }

    // --- Canvas ---

    private void CreateCanvas()
    {
        // Canvas must be a root-level visible object - NOT parented to the
        // HideAndDontSave mod object, or Unity UI raycasting won't work.
        _canvasObj = new GameObject("SpectatorMultiplayerCanvas");
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
            new Color(0.12f, 0.12f, 0.12f, 1f), new Vector2(720, 608));

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

        // Name label
        var nameLabel = CreateText(_mainPanel.transform, "NameLabel", "Name:", 29);
        var nameLabelRect = nameLabel.rectTransform;
        nameLabelRect.anchorMin = new Vector2(0.5f, 0.5f);
        nameLabelRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameLabelRect.pivot = new Vector2(0.5f, 0.5f);
        nameLabelRect.anchoredPosition = new Vector2(0, 120);
        nameLabelRect.sizeDelta = new Vector2(480, 40);

        // Name input
        _nameInput = CreateInputField(_mainPanel.transform, "NameInput",
            "Enter your name...", new Vector2(0, 72), new Vector2(480, 64));
        _nameInput.characterLimit = 20;
        _nameInput.onValueChanged.AddListener(OnNameChanged);

        // Host button (disabled until name entered)
        _hostButton = CreateButton(_mainPanel.transform, "HostBtn", "Host Game",
            new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, -16), new Vector2(480, 88));
        _hostButton.onClick.AddListener(OnHostClicked);
        _hostButton.interactable = false;

        // Join button (disabled until name entered)
        _joinButton = CreateButton(_mainPanel.transform, "JoinBtn", "Join Game",
            new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -120), new Vector2(480, 88));
        _joinButton.onClick.AddListener(OnJoinClicked);
        _joinButton.interactable = false;

        // Back button
        var backBtn = CreateButton(_mainPanel.transform, "BackBtn", "Close",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -224), new Vector2(480, 88));
        backBtn.onClick.AddListener(HideOverlay);
    }

    private void OnNameChanged(string name)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        _hostButton.interactable = hasName;
        _joinButton.interactable = hasName;
        LocalPlayerName = name.Trim();
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
            new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -64), new Vector2(480, 88));
        connectBtn.onClick.AddListener(OnConnectClicked);

        // Status text
        _statusText = CreateText(_joinPanel.transform, "StatusText", "", 26);
        _statusText.color = new Color(0.8f, 0.8f, 0.3f, 1f);
        var statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, -144);
        statusRect.sizeDelta = new Vector2(640, 48);

        // Cancel button
        var cancelBtn = CreateButton(_joinPanel.transform, "CancelBtn", "Back",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -208), new Vector2(480, 88));
        cancelBtn.onClick.AddListener(OnCancelJoinClicked);

        _joinPanel.SetActive(false);
    }

    private void CreateLobbyPanel(Transform parent)
    {
        _lobbyPanel = new GameObject("LobbyPanel");
        _lobbyPanel.transform.SetParent(parent, false);
        var rect = _lobbyPanel.GetComponent<RectTransform>() ?? _lobbyPanel.AddComponent<RectTransform>();
        StretchFill(rect);

        // "Players:" label — positioned below the parent's "Multiplayer" title
        var playersLabel = CreateText(_lobbyPanel.transform, "PlayersLabel", "Players:", 28);
        playersLabel.alignment = TextAlignmentOptions.Left;
        var plRect = playersLabel.rectTransform;
        plRect.anchorMin = new Vector2(0.5f, 0.5f);
        plRect.anchorMax = new Vector2(0.5f, 0.5f);
        plRect.pivot = new Vector2(0.5f, 0.5f);
        plRect.anchoredPosition = new Vector2(0, 120);
        plRect.sizeDelta = new Vector2(580, 36);

        // Player list
        _lobbyListText = CreateText(_lobbyPanel.transform, "LobbyList", "", 26);
        _lobbyListText.alignment = TextAlignmentOptions.TopLeft;
        var listRect = _lobbyListText.rectTransform;
        listRect.anchorMin = new Vector2(0.5f, 0.5f);
        listRect.anchorMax = new Vector2(0.5f, 0.5f);
        listRect.pivot = new Vector2(0.5f, 0.5f);
        listRect.anchoredPosition = new Vector2(0, 50);
        listRect.sizeDelta = new Vector2(580, 100);

        // Status line
        _lobbyStatusText = CreateText(_lobbyPanel.transform, "LobbyStatus", "", 22);
        _lobbyStatusText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        var statusRect = _lobbyStatusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, -20);
        statusRect.sizeDelta = new Vector2(640, 36);

        // Play button (host only, hidden until client connects)
        _playButton = CreateButton(_lobbyPanel.transform, "PlayBtn", "Play",
            new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, -100), new Vector2(480, 88));
        _playButton.onClick.AddListener(OnPlayClicked);
        _playButton.gameObject.SetActive(false);

        // Disconnect button
        var disconnectBtn = CreateButton(_lobbyPanel.transform, "DisconnectBtn", "Disconnect",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(0, -200), new Vector2(480, 88));
        disconnectBtn.onClick.AddListener(OnDisconnectClicked);

        _lobbyPanel.SetActive(false);
    }

    private void UpdateLobbyPanel()
    {
        if (_lobbyListText == null) return;

        var lines = "";
        var myRole = _spectatorMode.IsHosting ? "Host" : "Client";
        lines += $"  <color=#88FF88>{LocalPlayerName}</color>  <color=#AAAAAA>({myRole} - You)</color>\n";

        bool remoteConnected = false;
        if (RemotePeerInfo.Received)
        {
            var remoteRole = RemotePeerInfo.IsHost ? "Host" : "Client";
            lines += $"  <color=#88AAFF>{RemotePeerInfo.PlayerName}</color>  <color=#AAAAAA>({remoteRole})</color>\n";
            remoteConnected = true;
        }
        else if (_transport.IsConnected)
        {
            lines += "  <color=#AAAA55>Connecting...</color>\n";
        }
        else if (_spectatorMode.IsHosting)
        {
            lines += "  <color=#AAAA55>Waiting for player...</color>\n";
        }

        _lobbyListText.text = lines;

        if (_spectatorMode.IsHosting)
            _lobbyStatusText.text = $"Hosting on port {NetworkConfig.DefaultPort}";
        else if (_transport.IsConnected)
            _lobbyStatusText.text = "Connected";
        else
            _lobbyStatusText.text = "Connecting...";

        // Show Play button for host when a client is connected
        if (_playButton != null)
            _playButton.gameObject.SetActive(_spectatorMode.IsHosting && remoteConnected);
    }

    private void OnDisconnectClicked()
    {
        _transport.Stop();
        _spectatorMode.Disable();
        RemotePeerInfo.Reset();
        if (_spectatorPanel != null)
            _spectatorPanel.SetActive(false);
        _overlayPanel.SetActive(true);
        Log?.LogInfo("Disconnected");
        ShowMainPanel();
    }

    private void OnPlayClicked()
    {
        // Host clicks Play → hide overlay and start a new game
        HideOverlay();
        var playButton = UnityEngine.Object.FindObjectOfType<PeglinUI.MainMenu.PlayButton>();
        if (playButton != null)
        {
            playButton.MovetoCharacterSelect();
            Log?.LogInfo("Host starting game via PlayButton.MovetoCharacterSelect()");
        }
        else
        {
            Log?.LogWarning("PlayButton not found — cannot start game");
        }
    }

    private void CreateSpectatorPanel()
    {
        // Fullscreen event feed for the client — created on the canvas directly, not inside centerPanel
        _spectatorPanel = new GameObject("SpectatorPanel");
        _spectatorPanel.transform.SetParent(_canvasObj.transform, false);
        var bg = _spectatorPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);
        StretchFill(_spectatorPanel.GetComponent<RectTransform>());

        // Title bar
        var title = CreateText(_spectatorPanel.transform, "SpectatorTitle", "Spectating — Event Feed", 32);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -10);
        titleRect.sizeDelta = new Vector2(0, 48);

        // Scrolling event text
        _spectatorFeedText = CreateText(_spectatorPanel.transform, "FeedText", "", 18);
        _spectatorFeedText.alignment = TextAlignmentOptions.TopLeft;
        _spectatorFeedText.enableWordWrapping = true;
        _spectatorFeedText.overflowMode = TextOverflowModes.Truncate;
        var feedRect = _spectatorFeedText.rectTransform;
        feedRect.anchorMin = new Vector2(0, 0);
        feedRect.anchorMax = new Vector2(1, 1);
        feedRect.offsetMin = new Vector2(20, 80);
        feedRect.offsetMax = new Vector2(-20, -64);

        // Disconnect button at bottom
        var disconnectBtn = CreateButton(_spectatorPanel.transform, "SpectatorDisconnect", "Disconnect",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(0, 0), new Vector2(300, 60));
        var dcRect = disconnectBtn.GetComponent<RectTransform>();
        dcRect.anchorMin = new Vector2(0.5f, 0);
        dcRect.anchorMax = new Vector2(0.5f, 0);
        dcRect.pivot = new Vector2(0.5f, 0);
        dcRect.anchoredPosition = new Vector2(0, 10);
        disconnectBtn.onClick.AddListener(OnDisconnectClicked);

        _spectatorPanel.SetActive(false);
    }

    private void ShowSpectatorView()
    {
        if (_spectatorPanel == null)
            CreateSpectatorPanel();

        _overlayPanel.SetActive(false);
        _spectatorPanel.SetActive(true);
        _overlayVisible = true;
        EventFeed.Clear();
        _lastFeedVersion = -1;
    }

    // --- Panel switching ---

    private void ShowMainPanel()
    {
        _mainPanel.SetActive(true);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _lobbyPanel.SetActive(false);
    }

    private void ShowLobby()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _lobbyPanel.SetActive(true);
        UpdateLobbyPanel();
    }

    private void ShowOverlay()
    {
        _overlayVisible = true;

        // Client spectating: show fullscreen event feed
        if (_spectatorMode.IsSpectating && _transport.IsConnected)
        {
            ShowSpectatorView();
            return;
        }

        _overlayPanel.SetActive(true);

        if (_spectatorMode.IsHosting)
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
                SpectatorPlugin.Logger?.LogWarning("MultiplayerUI: instance is null, cannot toggle overlay");
        }
        catch (Exception ex)
        {
            SpectatorPlugin.Logger?.LogError($"MultiplayerUI.ToggleOverlay crashed: {ex}");
        }
    }

    private void ShowJoinPanel()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(true);
        _lobbyPanel.SetActive(false);
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
            _spectatorMode.EnableHosting();
            _transport.StartHost(NetworkConfig.DefaultPort);
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
            _spectatorMode.EnableSpectating();
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

        if (_spectatorMode.IsSpectating)
        {
            // Client: switch to fullscreen spectator event feed
            ShowSpectatorView();
        }
        else
        {
            // Host: refresh lobby
            UpdateLobbyPanel();
        }

        Log.LogInfo("Connected to host");
    }

    private void OnDisconnected()
    {
        _lastConnectionStatus = "Disconnected";
        RemotePeerInfo.Reset();
        UpdateLobbyPanel();
        Log.LogInfo("Disconnected from host");
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

    private static void StretchFill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
