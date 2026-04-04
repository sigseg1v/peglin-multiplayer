using System;
using System.Collections.Generic;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Handlers.Lobby;
using PeglinMods.Multiplayer.Events.Network.Lobby;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Network;
using IMessageSender = PeglinMods.Multiplayer.Network.IMessageSender;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Multiplayer lobby UI: class selection, ready status, and start button.
/// Rendered as a panel inside the MultiplayerUI overlay.
/// </summary>
public static class LobbyUI
{
    private static readonly string[] ClassNames = { "Peglin", "Balladin", "Roundrel", "Spinventor" };

    // Current lobby state (updated by LobbyStateClientHandler or locally on host)
    private static LobbyStateEvent _latestLobbyState;
    private static int _localChosenClass;
    private static bool _localIsReady;
    private static bool _gameStartReceived;
    private static GameStartEvent _gameStartEvent;

    // UI references (set by MultiplayerUI when creating the lobby panel)
    private static GameObject _lobbyRoot;
    private static readonly List<PlayerRow> _playerRows = new List<PlayerRow>();
    private static Button _startButton;
    private static TextMeshProUGUI _startButtonText;
    private static Button _readyButton;
    private static TextMeshProUGUI _readyButtonText;
    private static bool _isHost;

    private class PlayerRow
    {
        public GameObject Root;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI ClassText;
        public Button LeftArrow;
        public Button RightArrow;
        public TextMeshProUGUI ReadyText;
        public int SlotIndex;
        public bool IsLocalPlayer;
    }

    public static bool GameStartReceived => _gameStartReceived;
    public static GameStartEvent LatestGameStartEvent => _gameStartEvent;

    public static void Reset()
    {
        _latestLobbyState = null;
        _localChosenClass = 0;
        _localIsReady = false;
        _gameStartReceived = false;
        _gameStartEvent = null;

        // Destroy dynamically created GameObjects before clearing references
        foreach (var row in _playerRows)
        {
            if (row.Root != null)
                UnityEngine.Object.Destroy(row.Root);
        }
        _playerRows.Clear();

        if (_startButton != null)
        {
            UnityEngine.Object.Destroy(_startButton.gameObject);
            _startButton = null;
            _startButtonText = null;
        }

        if (_readyButton != null)
        {
            UnityEngine.Object.Destroy(_readyButton.gameObject);
            _readyButton = null;
            _readyButtonText = null;
        }

        if (_lobbyRoot != null)
        {
            UnityEngine.Object.Destroy(_lobbyRoot);
            _lobbyRoot = null;
        }
    }

    /// <summary>Called by LobbyStateClientHandler when receiving lobby state from host.</summary>
    public static void ApplyLobbyState(LobbyStateEvent state)
    {
        _latestLobbyState = state;
    }

    /// <summary>Called by GameStartClientHandler when host starts the game.</summary>
    public static void OnGameStartReceived(GameStartEvent evt)
    {
        _gameStartReceived = true;
        _gameStartEvent = evt;
    }

    /// <summary>
    /// Build the lobby panel inside the given parent transform.
    /// Called by MultiplayerUI when transitioning to lobby state.
    /// </summary>
    public static GameObject CreateLobbyPanel(Transform parent, bool isHost, Action<string> createText, Func<Transform, string, string, Color, Vector2, Vector2, Button> createButton)
    {
        _isHost = isHost;
        _gameStartReceived = false;

        _lobbyRoot = new GameObject("CoopLobbyPanel");
        _lobbyRoot.transform.SetParent(parent, false);
        var rect = _lobbyRoot.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Player rows will be created dynamically in UpdateLobbyUI

        return _lobbyRoot;
    }

    /// <summary>
    /// Called every frame by MultiplayerUI when lobby panel is active.
    /// Rebuilds the lobby display from the latest state.
    /// </summary>
    public static void UpdateLobbyUI(
        Transform lobbyParent,
        bool isHost,
        Func<Transform, string, string, int, TextMeshProUGUI> createText,
        Func<Transform, string, string, Color, Vector2, Vector2, Button> createButton)
    {
        _isHost = isHost;

        List<LobbyPlayerEntry> players;
        if (isHost)
        {
            // Host builds lobby state from PlayerRegistry
            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<PlayerRegistry>(out var registry)) return;

            players = new List<LobbyPlayerEntry>();
            foreach (var slot in registry.GetAllSlots())
            {
                players.Add(new LobbyPlayerEntry
                {
                    SlotIndex = slot.SlotIndex,
                    PlayerName = slot.PlayerName,
                    ChosenClass = slot.ChosenClass,
                    ChosenClassName = LobbyHelper.GetClassName(slot.ChosenClass),
                    IsReady = slot.IsReady,
                    IsHost = slot.IsHost,
                });
            }
        }
        else if (_latestLobbyState?.Players != null)
        {
            players = _latestLobbyState.Players;
        }
        else
        {
            return; // No state yet
        }

        // Ensure we have the right number of rows
        while (_playerRows.Count < players.Count)
            AddPlayerRow(lobbyParent, _playerRows.Count, createText, createButton);

        // Hide extra rows
        for (int i = players.Count; i < _playerRows.Count; i++)
            _playerRows[i].Root.SetActive(false);

        // Update each row
        for (int i = 0; i < players.Count; i++)
        {
            var entry = players[i];
            var row = _playerRows[i];
            row.Root.SetActive(true);
            row.SlotIndex = entry.SlotIndex;

            // Determine if this row is the local player
            row.IsLocalPlayer = isHost ? entry.IsHost : !entry.IsHost;

            row.NameText.text = entry.PlayerName ?? "???";
            row.NameText.color = row.IsLocalPlayer ? new Color(0.53f, 1f, 0.53f) : new Color(0.53f, 0.67f, 1f);

            // For local player, show _localChosenClass directly (no round-trip delay)
            row.ClassText.text = row.IsLocalPlayer
                ? LobbyHelper.GetClassName(_localChosenClass)
                : (entry.ChosenClassName ?? LobbyHelper.GetClassName(entry.ChosenClass));

            // Show arrows only for local player
            row.LeftArrow.gameObject.SetActive(row.IsLocalPlayer);
            row.RightArrow.gameObject.SetActive(row.IsLocalPlayer);

            // Ready status text in column 3
            if (entry.IsHost)
            {
                row.ReadyText.text = "<color=#88FF88>HOST</color>";
            }
            else if (row.IsLocalPlayer)
            {
                row.ReadyText.text = _localIsReady
                    ? "<color=#88FF88>READY</color>"
                    : "<color=#FF8888>NOT READY</color>";
            }
            else
            {
                row.ReadyText.text = entry.IsReady
                    ? "<color=#88FF88>READY</color>"
                    : "<color=#FF8888>NOT READY</color>";
            }
        }

        // Start button (host only)
        if (_startButton == null && isHost)
        {
            _startButton = createButton(lobbyParent, "StartGameBtn", "Start Game",
                new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, -160), new Vector2(400, 72));
            _startButton.onClick.AddListener(OnStartClicked);
            _startButtonText = _startButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (_startButton != null)
        {
            _startButton.gameObject.SetActive(isHost);
            if (isHost)
            {
                var allReady = true;
                foreach (var p in players)
                    if (!p.IsHost && !p.IsReady) { allReady = false; break; }
                var hasClients = players.Count > 1;
                _startButton.interactable = allReady && hasClients;
            }
        }

        // Ready button (client only — same position as Start Game)
        if (_readyButton == null && !isHost)
        {
            _readyButton = createButton(lobbyParent, "ReadyBtn", "Ready",
                new Color(0.5f, 0.3f, 0.2f, 1f), new Vector2(0, -160), new Vector2(400, 72));
            _readyButton.onClick.AddListener(OnReadyToggle);
            _readyButtonText = _readyButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (_readyButton != null)
        {
            _readyButton.gameObject.SetActive(!isHost);
            _readyButtonText.text = _localIsReady ? "Not Ready" : "Ready";
            var colors = _readyButton.colors;
            colors.normalColor = _localIsReady ? new Color(0.5f, 0.3f, 0.2f) : new Color(0.2f, 0.55f, 0.25f);
            _readyButton.colors = colors;
        }
    }

    private static void AddPlayerRow(
        Transform parent,
        int rowIndex,
        Func<Transform, string, string, int, TextMeshProUGUI> createText,
        Func<Transform, string, string, Color, Vector2, Vector2, Button> createButton)
    {
        // Rows positioned from top of lobby area: first row at y=60, each row 72px apart
        float yBase = 60 - rowIndex * 72;

        var rowObj = new GameObject($"PlayerRow_{rowIndex}");
        rowObj.transform.SetParent(parent, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0, yBase);
        rowRect.sizeDelta = new Vector2(840, 56);

        var row = new PlayerRow { Root = rowObj };

        // Column 1: Player name (left side)
        row.NameText = createText(rowObj.transform, $"Name_{rowIndex}", "", 30);
        var nameRect = row.NameText.rectTransform;
        nameRect.anchorMin = new Vector2(0.5f, 0.5f);
        nameRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameRect.pivot = new Vector2(0.5f, 0.5f);
        nameRect.anchoredPosition = new Vector2(-300, 0);
        nameRect.sizeDelta = new Vector2(220, 44);
        row.NameText.alignment = TextAlignmentOptions.Left;

        // Column 2: Class selection (center) — [<] ClassName [>]
        int ri = rowIndex;

        row.LeftArrow = createButton(rowObj.transform, $"Left_{rowIndex}", "<",
            new Color(0.3f, 0.3f, 0.4f, 1f), new Vector2(-100, 0), new Vector2(44, 44));
        row.LeftArrow.onClick.AddListener(() => OnClassArrow(ri, -1));

        row.ClassText = createText(rowObj.transform, $"Class_{rowIndex}", "Peglin", 30);
        var classRect = row.ClassText.rectTransform;
        classRect.anchorMin = new Vector2(0.5f, 0.5f);
        classRect.anchorMax = new Vector2(0.5f, 0.5f);
        classRect.pivot = new Vector2(0.5f, 0.5f);
        classRect.anchoredPosition = new Vector2(-10, 0);
        classRect.sizeDelta = new Vector2(140, 44);
        row.ClassText.alignment = TextAlignmentOptions.Center;

        row.RightArrow = createButton(rowObj.transform, $"Right_{rowIndex}", ">",
            new Color(0.3f, 0.3f, 0.4f, 1f), new Vector2(80, 0), new Vector2(44, 44));
        row.RightArrow.onClick.AddListener(() => OnClassArrow(ri, 1));

        // Column 3: Ready status text (right side)
        row.ReadyText = createText(rowObj.transform, $"Ready_{rowIndex}", "", 28);
        var readyRect = row.ReadyText.rectTransform;
        readyRect.anchorMin = new Vector2(0.5f, 0.5f);
        readyRect.anchorMax = new Vector2(0.5f, 0.5f);
        readyRect.pivot = new Vector2(0.5f, 0.5f);
        readyRect.anchoredPosition = new Vector2(280, 0);
        readyRect.sizeDelta = new Vector2(200, 44);
        row.ReadyText.alignment = TextAlignmentOptions.Center;

        _playerRows.Add(row);
    }

    private static void OnClassArrow(int rowIndex, int direction)
    {
        _localChosenClass = (_localChosenClass + direction + ClassNames.Length) % ClassNames.Length;

        var services = MultiplayerPlugin.Services;
        if (services == null) return;

        if (_isHost)
        {
            // Host updates its own slot and broadcasts lobby state
            if (services.TryResolve<PlayerRegistry>(out var registry))
            {
                var hostSlot = registry.GetHostSlot();
                if (hostSlot != null) hostSlot.ChosenClass = _localChosenClass;
            }
            if (services.TryResolve<PlayerRegistry>(out var reg2) && services.TryResolve<IGameEventRegistry>(out var er))
                LobbyHelper.BroadcastLobbyState(reg2, er);
        }
        else
        {
            // Client sends ClassSelectEvent to host over the network
            if (services.TryResolve<IMessageSender>(out var sender))
                sender.Send(new ClassSelectEvent { ChosenClass = _localChosenClass });
        }
    }

    private static void OnReadyToggle()
    {
        _localIsReady = !_localIsReady;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IMessageSender>(out var sender) == true)
        {
            sender.Send(new ReadyEvent { IsReady = _localIsReady });
        }
    }

    private static void OnStartClicked()
    {
        var services = MultiplayerPlugin.Services;
        if (services == null) return;
        if (!services.TryResolve<PlayerRegistry>(out var registry)) return;
        if (!services.TryResolve<IGameEventRegistry>(out var eventRegistry)) return;

        if (!registry.AllClientsReady) return;

        // Build final player list
        var finalPlayers = new List<LobbyPlayerEntry>();
        foreach (var slot in registry.GetAllSlots())
        {
            finalPlayers.Add(new LobbyPlayerEntry
            {
                SlotIndex = slot.SlotIndex,
                PlayerName = slot.PlayerName,
                ChosenClass = slot.ChosenClass,
                ChosenClassName = LobbyHelper.GetClassName(slot.ChosenClass),
                IsReady = true,
                IsHost = slot.IsHost,
            });
        }

        // Broadcast game start
        eventRegistry.Dispatch(new GameStartEvent { FinalPlayers = finalPlayers });

        // Host sets its own class and starts the game
        var hostSlot = registry.GetHostSlot();
        if (hostSlot != null)
        {
            StaticGameData.chosenClass = (Peglin.ClassSystem.Class)hostSlot.ChosenClass;
            MultiplayerPlugin.Logger?.LogInfo($"[Lobby] Starting game: host class={hostSlot.ChosenClass}, {finalPlayers.Count} players");
        }

        // Start the game by calling PlayButton.MovetoCharacterSelect()
        // The class select screen will be skipped by a patch since we already chose
        _gameStartReceived = true;
        _gameStartEvent = new GameStartEvent { FinalPlayers = finalPlayers };

        var playButton = UnityEngine.Object.FindObjectOfType<PeglinUI.MainMenu.PlayButton>();
        if (playButton != null)
        {
            playButton.MovetoCharacterSelect();
        }
        else
        {
            MultiplayerPlugin.Logger?.LogWarning("[Lobby] PlayButton not found");
        }
    }
}
