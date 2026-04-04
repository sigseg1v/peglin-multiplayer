using System;
using System.Collections.Generic;
using PeglinMods.Multiplayer.Events;
using PeglinMods.Multiplayer.Events.Handlers.Lobby;
using PeglinMods.Multiplayer.Events.Network.Lobby;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Network;
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
    private static TextMeshProUGUI _titleText;
    private static readonly List<PlayerRow> _playerRows = new List<PlayerRow>();
    private static Button _startButton;
    private static TextMeshProUGUI _startButtonText;
    private static bool _isHost;

    private class PlayerRow
    {
        public GameObject Root;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI ClassText;
        public Button LeftArrow;
        public Button RightArrow;
        public TextMeshProUGUI ReadyText;
        public Button ReadyButton;
        public TextMeshProUGUI ReadyButtonText;
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
        _lobbyRoot = null;
        _playerRows.Clear();
        _startButton = null;
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

            row.ClassText.text = entry.ChosenClassName ?? LobbyHelper.GetClassName(entry.ChosenClass);

            // Show arrows only for local player
            row.LeftArrow.gameObject.SetActive(row.IsLocalPlayer);
            row.RightArrow.gameObject.SetActive(row.IsLocalPlayer);

            // Ready status
            if (entry.IsHost)
            {
                row.ReadyText.text = "<color=#88FF88>HOST</color>";
                row.ReadyButton.gameObject.SetActive(false);
            }
            else if (row.IsLocalPlayer)
            {
                row.ReadyText.text = "";
                row.ReadyButton.gameObject.SetActive(true);
                row.ReadyButtonText.text = _localIsReady ? "READY" : "NOT READY";
                var colors = row.ReadyButton.colors;
                colors.normalColor = _localIsReady ? new Color(0.2f, 0.55f, 0.25f) : new Color(0.5f, 0.3f, 0.2f);
                row.ReadyButton.colors = colors;
            }
            else
            {
                row.ReadyButton.gameObject.SetActive(false);
                row.ReadyText.text = entry.IsReady
                    ? "<color=#88FF88>READY</color>"
                    : "<color=#FF8888>NOT READY</color>";
            }
        }

        // Start button (host only)
        if (_startButton == null && isHost)
        {
            _startButton = createButton(lobbyParent, "StartGameBtn", "Start Game",
                new Color(0.2f, 0.55f, 0.25f, 1f), new Vector2(0, -200), new Vector2(480, 88));
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
    }

    private static void AddPlayerRow(
        Transform parent,
        int rowIndex,
        Func<Transform, string, string, int, TextMeshProUGUI> createText,
        Func<Transform, string, string, Color, Vector2, Vector2, Button> createButton)
    {
        float yBase = 80 - rowIndex * 80;

        var rowObj = new GameObject($"PlayerRow_{rowIndex}");
        rowObj.transform.SetParent(parent, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0, yBase);
        rowRect.sizeDelta = new Vector2(640, 64);

        var row = new PlayerRow { Root = rowObj };

        // Player name (left)
        row.NameText = createText(rowObj.transform, $"Name_{rowIndex}", "", 26);
        var nameRect = row.NameText.rectTransform;
        nameRect.anchorMin = new Vector2(0, 0.5f);
        nameRect.anchorMax = new Vector2(0, 0.5f);
        nameRect.pivot = new Vector2(0, 0.5f);
        nameRect.anchoredPosition = new Vector2(0, 0);
        nameRect.sizeDelta = new Vector2(160, 40);
        row.NameText.alignment = TextAlignmentOptions.Left;

        // Left arrow
        row.LeftArrow = createButton(rowObj.transform, $"Left_{rowIndex}", "<",
            new Color(0.3f, 0.3f, 0.4f, 1f), new Vector2(180, 0), new Vector2(40, 40));
        int ri = rowIndex;
        row.LeftArrow.onClick.AddListener(() => OnClassArrow(ri, -1));

        // Class name (center)
        row.ClassText = createText(rowObj.transform, $"Class_{rowIndex}", "Peglin", 26);
        var classRect = row.ClassText.rectTransform;
        classRect.anchorMin = new Vector2(0, 0.5f);
        classRect.anchorMax = new Vector2(0, 0.5f);
        classRect.pivot = new Vector2(0, 0.5f);
        classRect.anchoredPosition = new Vector2(230, 0);
        classRect.sizeDelta = new Vector2(140, 40);
        row.ClassText.alignment = TextAlignmentOptions.Center;

        // Right arrow
        row.RightArrow = createButton(rowObj.transform, $"Right_{rowIndex}", ">",
            new Color(0.3f, 0.3f, 0.4f, 1f), new Vector2(380, 0), new Vector2(40, 40));
        row.RightArrow.onClick.AddListener(() => OnClassArrow(ri, 1));

        // Ready text (for non-local players)
        row.ReadyText = createText(rowObj.transform, $"Ready_{rowIndex}", "", 24);
        var readyRect = row.ReadyText.rectTransform;
        readyRect.anchorMin = new Vector2(0, 0.5f);
        readyRect.anchorMax = new Vector2(0, 0.5f);
        readyRect.pivot = new Vector2(0, 0.5f);
        readyRect.anchoredPosition = new Vector2(440, 0);
        readyRect.sizeDelta = new Vector2(200, 40);
        row.ReadyText.alignment = TextAlignmentOptions.Center;

        // Ready button (for local client only)
        row.ReadyButton = createButton(rowObj.transform, $"ReadyBtn_{rowIndex}", "NOT READY",
            new Color(0.5f, 0.3f, 0.2f, 1f), new Vector2(500, 0), new Vector2(140, 40));
        row.ReadyButton.onClick.AddListener(OnReadyToggle);
        row.ReadyButtonText = row.ReadyButton.GetComponentInChildren<TextMeshProUGUI>();
        row.ReadyButton.gameObject.SetActive(false);

        _playerRows.Add(row);
    }

    private static void OnClassArrow(int rowIndex, int direction)
    {
        _localChosenClass = (_localChosenClass + direction + ClassNames.Length) % ClassNames.Length;

        if (_isHost)
        {
            // Host updates its own slot directly
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<PlayerRegistry>(out var registry) == true)
            {
                var hostSlot = registry.GetHostSlot();
                if (hostSlot != null) hostSlot.ChosenClass = _localChosenClass;
            }
        }
        else
        {
            // Client sends ClassSelectEvent to host
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IGameEventRegistry>(out var eventRegistry) == true)
            {
                eventRegistry.Dispatch(new ClassSelectEvent { ChosenClass = _localChosenClass });
            }
        }
    }

    private static void OnReadyToggle()
    {
        _localIsReady = !_localIsReady;

        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IGameEventRegistry>(out var eventRegistry) == true)
        {
            eventRegistry.Dispatch(new ReadyEvent { IsReady = _localIsReady });
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
