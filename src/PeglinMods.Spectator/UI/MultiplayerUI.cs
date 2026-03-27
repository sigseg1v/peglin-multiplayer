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

    // Corner indicator (always visible)
    private GameObject _cornerIndicator;
    private TextMeshProUGUI _cornerText;

    // Overlay panels
    private GameObject _overlayPanel;
    private GameObject _mainPanel;
    private GameObject _hostPanel;
    private GameObject _joinPanel;

    // Join panel elements
    private TMP_InputField _codeInput;
    private TextMeshProUGUI _statusText;

    // Host panel elements
    private TextMeshProUGUI _hostInfoText;
    private TextMeshProUGUI _hostVersionText;
    private TextMeshProUGUI _lobbyText;

    // Static access for menu button
    private static MultiplayerUI _instance;

    // State
    private bool _overlayVisible;
    private string _lastConnectionStatus = "";

    private void Start()
    {
        _instance = this;
        _transport = SpectatorPlugin.Services.Resolve<INetworkTransport>();
        _spectatorMode = SpectatorPlugin.Services.Resolve<ISpectatorMode>();

        _transport.OnClientConnected += OnConnected;
        _transport.OnDisconnected += OnDisconnected;

        CreateCanvas();
        CreateCornerIndicator();
        CreateOverlay();

        HideOverlay();
        UpdateCornerIndicator();
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

    private void Update()
    {
        if (UnityEngine.Input.GetKeyDown(KeyCode.F7))
        {
            if (_overlayVisible) HideOverlay();
            else ShowOverlay();
        }

        UpdateCornerIndicator();
    }

    // --- Canvas ---

    private void CreateCanvas()
    {
        _canvasObj = new GameObject("SpectatorMultiplayerCanvas");
        _canvasObj.transform.SetParent(transform, false);
        _canvas = _canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasObj.AddComponent<GraphicRaycaster>();
    }

    // --- Corner Indicator ---

    private void CreateCornerIndicator()
    {
        _cornerIndicator = new GameObject("CornerIndicator");
        _cornerIndicator.transform.SetParent(_canvasObj.transform, false);

        var bg = _cornerIndicator.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);

        var rect = _cornerIndicator.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-10, 10);
        rect.sizeDelta = new Vector2(200, 40);

        var btn = _cornerIndicator.AddComponent<Button>();
        btn.onClick.AddListener(ToggleOverlay);

        _cornerText = CreateText(_cornerIndicator.transform, "CornerText", "Multiplayer [F7]", 16);
        StretchFill(_cornerText.rectTransform);
    }

    private void UpdateCornerIndicator()
    {
        if (_cornerText == null) return;

        if (_spectatorMode.IsHosting)
        {
            _cornerText.text = _transport.IsConnected ? "Hosting (1)" : "Hosting";
            _cornerIndicator.GetComponent<Image>().color = new Color(0.1f, 0.4f, 0.15f, 0.8f);
            UpdateLobbyText();
        }
        else if (_spectatorMode.IsSpectating && _transport.IsConnected)
        {
            _cornerText.text = "Spectating";
            _cornerIndicator.GetComponent<Image>().color = new Color(0.15f, 0.2f, 0.5f, 0.8f);

            // Update join panel with host version after handshake
            if (RemotePeerInfo.Received && _statusText != null && !_statusText.text.Contains("Host Version"))
            {
                var mismatch = RemotePeerInfo.GameVersion != (Application.version ?? "")
                    ? " <color=#FF6666>(MISMATCH!)</color>" : "";
                _statusText.text = $"Connected!\nClient Version: mod={SpectatorPluginInfo.VERSION} game={Application.version}\n" +
                    $"Host Version: mod={RemotePeerInfo.ModVersion} game={RemotePeerInfo.GameVersion}{mismatch}";
            }
        }
        else if (_spectatorMode.IsSpectating && !_transport.IsConnected)
        {
            _cornerText.text = "Connecting...";
            _cornerIndicator.GetComponent<Image>().color = new Color(0.5f, 0.4f, 0.1f, 0.8f);
        }
        else
        {
            _cornerText.text = "Multiplayer [F7]";
            _cornerIndicator.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
        }
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
            new Color(0.12f, 0.12f, 0.12f, 1f), new Vector2(450, 380));

        // Title
        var title = CreateText(centerPanel.transform, "Title", "Multiplayer", 30);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1);
        titleRect.anchorMax = new Vector2(0.5f, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -20);
        titleRect.sizeDelta = new Vector2(400, 40);

        CreateMainPanel(centerPanel.transform);
        CreateHostPanel(centerPanel.transform);
        CreateJoinPanel(centerPanel.transform);

        ShowMainPanel();
    }

    private void CreateMainPanel(Transform parent)
    {
        _mainPanel = new GameObject("MainPanel");
        _mainPanel.transform.SetParent(parent, false);
        var mainRect = _mainPanel.GetComponent<RectTransform>() ?? _mainPanel.AddComponent<RectTransform>();
        StretchFill(mainRect);

        // Host button
        var hostBtn = CreateButton(_mainPanel.transform, "HostBtn", "Host Game",
            new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, 20), new Vector2(300, 55));
        hostBtn.onClick.AddListener(OnHostClicked);

        // Join button
        var joinBtn = CreateButton(_mainPanel.transform, "JoinBtn", "Join Game",
            new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -50), new Vector2(300, 55));
        joinBtn.onClick.AddListener(OnJoinClicked);

        // Back button
        var backBtn = CreateButton(_mainPanel.transform, "BackBtn", "Close",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -120), new Vector2(300, 55));
        backBtn.onClick.AddListener(HideOverlay);
    }

    private void CreateHostPanel(Transform parent)
    {
        _hostPanel = new GameObject("HostPanel");
        _hostPanel.transform.SetParent(parent, false);
        var hostRect = _hostPanel.GetComponent<RectTransform>() ?? _hostPanel.AddComponent<RectTransform>();
        StretchFill(hostRect);

        _hostInfoText = CreateText(_hostPanel.transform, "HostInfo", "", 20);
        var infoRect = _hostInfoText.rectTransform;
        infoRect.anchorMin = new Vector2(0.5f, 0.5f);
        infoRect.anchorMax = new Vector2(0.5f, 0.5f);
        infoRect.pivot = new Vector2(0.5f, 0.5f);
        infoRect.anchoredPosition = new Vector2(0, 50);
        infoRect.sizeDelta = new Vector2(400, 40);

        _hostVersionText = CreateText(_hostPanel.transform, "HostVersion", "", 14);
        _hostVersionText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        var verRect = _hostVersionText.rectTransform;
        verRect.anchorMin = new Vector2(0.5f, 0.5f);
        verRect.anchorMax = new Vector2(0.5f, 0.5f);
        verRect.pivot = new Vector2(0.5f, 0.5f);
        verRect.anchoredPosition = new Vector2(0, 15);
        verRect.sizeDelta = new Vector2(400, 25);

        _lobbyText = CreateText(_hostPanel.transform, "LobbyInfo", "", 15);
        _lobbyText.alignment = TextAlignmentOptions.Center;
        var lobbyRect = _lobbyText.rectTransform;
        lobbyRect.anchorMin = new Vector2(0.5f, 0.5f);
        lobbyRect.anchorMax = new Vector2(0.5f, 0.5f);
        lobbyRect.pivot = new Vector2(0.5f, 0.5f);
        lobbyRect.anchoredPosition = new Vector2(0, -20);
        lobbyRect.sizeDelta = new Vector2(400, 50);

        var stopBtn = CreateButton(_hostPanel.transform, "StopHostBtn", "Stop Hosting",
            new Color(0.5f, 0.2f, 0.2f, 1f), new Vector2(0, -80), new Vector2(300, 55));
        stopBtn.onClick.AddListener(OnStopHostClicked);

        _hostPanel.SetActive(false);
    }

    private void CreateJoinPanel(Transform parent)
    {
        _joinPanel = new GameObject("JoinPanel");
        _joinPanel.transform.SetParent(parent, false);
        var joinRect = _joinPanel.GetComponent<RectTransform>() ?? _joinPanel.AddComponent<RectTransform>();
        StretchFill(joinRect);

        // Label
        var label = CreateText(_joinPanel.transform, "Label", "Enter host code (IP:PORT):", 18);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0, 60);
        labelRect.sizeDelta = new Vector2(400, 30);

        // Input field
        _codeInput = CreateInputField(_joinPanel.transform, "CodeInput",
            $"127.0.0.1:{NetworkConfig.DefaultPort}", new Vector2(0, 20), new Vector2(350, 45));

        // Connect button
        var connectBtn = CreateButton(_joinPanel.transform, "ConnectBtn", "Connect",
            new Color(0.2f, 0.35f, 0.6f, 1f), new Vector2(0, -40), new Vector2(300, 55));
        connectBtn.onClick.AddListener(OnConnectClicked);

        // Status text
        _statusText = CreateText(_joinPanel.transform, "StatusText", "", 16);
        _statusText.color = new Color(0.8f, 0.8f, 0.3f, 1f);
        var statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0.5f);
        statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0, -90);
        statusRect.sizeDelta = new Vector2(400, 30);

        // Cancel button
        var cancelBtn = CreateButton(_joinPanel.transform, "CancelBtn", "Back",
            new Color(0.35f, 0.2f, 0.2f, 1f), new Vector2(0, -130), new Vector2(300, 55));
        cancelBtn.onClick.AddListener(OnCancelJoinClicked);

        _joinPanel.SetActive(false);
    }

    // --- Panel switching ---

    private void ShowMainPanel()
    {
        _mainPanel.SetActive(true);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(false);
    }

    private void ShowOverlay()
    {
        _overlayVisible = true;
        _overlayPanel.SetActive(true);

        // If already hosting or spectating, show the relevant panel
        if (_spectatorMode.IsHosting)
            ShowHostingState();
        else if (_spectatorMode.IsSpectating)
            ShowJoinPanel();
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
        if (_instance != null)
            _instance.ToggleOverlay();
    }

    private void ShowJoinPanel()
    {
        _mainPanel.SetActive(false);
        _hostPanel.SetActive(false);
        _joinPanel.SetActive(true);
        _statusText.text = _lastConnectionStatus;
    }

    private void ShowHostingState()
    {
        var ip = GetLocalIP();
        _hostInfoText.text = $"Hosting on: <b>{ip}:{NetworkConfig.DefaultPort}</b>";
        _hostVersionText.text = $"Host Version: mod={SpectatorPluginInfo.VERSION} game={Application.version}";
        UpdateLobbyText();
        _mainPanel.SetActive(false);
        _joinPanel.SetActive(false);
        _hostPanel.SetActive(true);
    }

    private void UpdateLobbyText()
    {
        if (_lobbyText == null) return;

        if (RemotePeerInfo.Received)
        {
            var mismatch = RemotePeerInfo.GameVersion != (Application.version ?? "unknown")
                ? " <color=#FF6666>(MISMATCH!)</color>" : "";
            _lobbyText.text = $"Client connected\n" +
                $"Client Version: mod={RemotePeerInfo.ModVersion} game={RemotePeerInfo.GameVersion}{mismatch}";
        }
        else if (_transport.IsConnected)
        {
            _lobbyText.text = "Client connected (awaiting handshake...)";
        }
        else
        {
            _lobbyText.text = "Waiting for client to connect...";
        }
    }

    // --- Button handlers ---

    private void OnHostClicked()
    {
        try
        {
            _spectatorMode.EnableHosting();
            _transport.StartHost(NetworkConfig.DefaultPort);
            Log.LogInfo($"Started hosting on port {NetworkConfig.DefaultPort}");
            ShowHostingState();
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
            _lastConnectionStatus = _statusText.text;
            _transport.Connect(ip, port);
            _spectatorMode.EnableSpectating();
            Log.LogInfo($"Connecting to {ip}:{port}");
        }
        catch (Exception ex)
        {
            _statusText.text = $"Error: {ex.Message}";
            _lastConnectionStatus = _statusText.text;
            Log.LogError($"Failed to connect: {ex}");
        }
    }

    private void OnStopHostClicked()
    {
        _transport.Stop();
        _spectatorMode.Disable();
        Log.LogInfo("Stopped hosting");
        ShowMainPanel();
    }

    private void OnCancelJoinClicked()
    {
        if (_spectatorMode.IsSpectating)
        {
            _transport.Stop();
            _spectatorMode.Disable();
            _lastConnectionStatus = "";
            Log.LogInfo("Disconnected");
        }
        ShowMainPanel();
    }

    // --- Network callbacks ---

    private void OnConnected()
    {
        _lastConnectionStatus = "Connected!";
        if (_statusText != null)
        {
            _statusText.text = $"Connected!\nClient Version: mod={SpectatorPluginInfo.VERSION} game={Application.version}";
            _statusText.color = new Color(0.3f, 0.8f, 0.3f, 1f);
        }
        UpdateLobbyText();
        Log.LogInfo("Connected to host");
    }

    private void OnDisconnected()
    {
        _lastConnectionStatus = "Disconnected";
        RemotePeerInfo.Reset();
        if (_statusText != null)
        {
            _statusText.text = "Disconnected";
            _statusText.color = new Color(0.8f, 0.3f, 0.3f, 1f);
        }
        UpdateLobbyText();
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

        var text = CreateText(obj.transform, "Label", label, 22);
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
        inputText.fontSize = 20;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        StretchFill(inputText.rectTransform);

        // Placeholder text
        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        var placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 20;
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
        inputField.pointSize = 20;
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
