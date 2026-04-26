using System;
using System.Collections.Generic;
using Multipeglin.Events;
using Multipeglin.Events.Handlers.Lobby;
using Multipeglin.Events.Network.Lobby;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using IMessageSender = Multipeglin.Network.IMessageSender;

namespace Multipeglin.UI;

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
    private static int _hostCruciballLevel;

    // UI references (set by MultiplayerUI when creating the lobby panel)
    private static GameObject _lobbyRoot;
    private static readonly List<PlayerRow> _playerRows = new List<PlayerRow>();
    private static Button _startButton;
    private static TextMeshProUGUI _startButtonText;
    private static Button _readyButton;
    private static TextMeshProUGUI _readyButtonText;
    private static GameObject _cruciballRow;
    private static TextMeshProUGUI _cruciballValueText;
    private static Button _cruciballLeftBtn;
    private static Button _cruciballRightBtn;
    private static bool _isHost;

    /// <summary>Host-authoritative cruciball level (0–20). Read by LobbyHelper.</summary>
    public static int HostCruciballLevel => _hostCruciballLevel;

    private class PlayerRow
    {
        public GameObject Root;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI ClassText;
        public Button LeftArrow;
        public Button RightArrow;
        public TextMeshProUGUI ReadyText;
        public TextMeshProUGUI VersionText;
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
        _hostCruciballLevel = 0;

        // Destroy dynamically created GameObjects before clearing references
        foreach (var row in _playerRows)
        {
            if (row.Root != null)
            {
                UnityEngine.Object.Destroy(row.Root);
            }
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

        if (_cruciballRow != null)
        {
            UnityEngine.Object.Destroy(_cruciballRow);
            _cruciballRow = null;
            _cruciballValueText = null;
            _cruciballLeftBtn = null;
            _cruciballRightBtn = null;
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
        if (state != null)
        {
            _hostCruciballLevel = state.CruciballLevel;
        }
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
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<PlayerRegistry>(out var registry))
            {
                return;
            }

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
                    GameVersion = slot.GameVersion,
                    ModVersion = slot.ModVersion,
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
        {
            AddPlayerRow(lobbyParent, _playerRows.Count, createText, createButton);
        }

        // Hide extra rows
        for (var i = players.Count; i < _playerRows.Count; i++)
        {
            _playerRows[i].Root.SetActive(false);
        }

        // Update each row
        var localGameVer = UnityEngine.Application.version ?? "unknown";
        var localModVer = MultiplayerPluginInfo.VERSION;
        var localVersionTag = $"Peglin {localGameVer} (mod {localModVer})";
        var hasVersionMismatch = false;

        var localName = MultiplayerUI.LocalPlayerName;
        for (var i = 0; i < players.Count; i++)
        {
            var entry = players[i];
            var row = _playerRows[i];
            row.Root.SetActive(true);
            row.SlotIndex = entry.SlotIndex;

            // Determine if this row is the local player. We match by name so it works with
            // 3+ players (every non-host name is unique). Host always identifies its own row
            // via IsHost regardless of name. As a fallback for the host, if there is no
            // matching name, we still pick IsHost so the host UI works even if the local
            // name is empty for some reason.
            if (isHost)
            {
                row.IsLocalPlayer = entry.IsHost;
            }
            else if (!string.IsNullOrEmpty(localName))
            {
                row.IsLocalPlayer = !entry.IsHost && entry.PlayerName == localName;
            }
            else
            {
                row.IsLocalPlayer = !entry.IsHost; // legacy 2-player fallback
            }

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

            // Version text in column 4
            var gameVer = row.IsLocalPlayer ? localGameVer : (entry.GameVersion ?? "?");
            var modVer = row.IsLocalPlayer ? localModVer : (entry.ModVersion ?? "?");
            var versionTag = $"Peglin {gameVer} (mod {modVer})";
            var versionMatch = versionTag == localVersionTag;
            if (!versionMatch)
            {
                hasVersionMismatch = true;
            }

            row.VersionText.text = versionTag;
            row.VersionText.color = versionMatch
                ? new Color(0.53f, 1f, 0.53f)
                : new Color(1f, 0.4f, 0.4f);
        }

        // Cruciball selector row — host can change, client is read-only
        if (_cruciballRow == null)
        {
            CreateCruciballRow(lobbyParent, createText, createButton);
        }

        UpdateCruciballRow(isHost);

        // Start button (host only) — gap below the cruciball row
        if (_startButton == null && isHost)
        {
            _startButton = createButton(
                lobbyParent,
                "StartGameBtn",
                "Start Game",
                new Color(0.2f, 0.55f, 0.25f, 1f),
                new Vector2(0, -280),
                new Vector2(400, 72));
            _startButton.onClick.AddListener(OnStartClicked);
            _startButtonText = _startButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (_startButton != null)
        {
            _startButton.gameObject.SetActive(isHost);
            if (isHost)
            {
                if (hasVersionMismatch)
                {
                    _startButton.interactable = false;
                    _startButtonText.text = "Unable to start, version mismatch";
                }
                else
                {
                    var allReady = true;
                    foreach (var p in players)
                    {
                        if (!p.IsHost && !p.IsReady)
                        {
                            allReady = false;
                            break;
                        }
                    }

                    var hasClients = players.Count > 1;
                    _startButton.interactable = allReady && hasClients;
                    _startButtonText.text = "Start Game";
                }
            }
        }

        // Ready button (client only — same position as Start Game)
        if (_readyButton == null && !isHost)
        {
            _readyButton = createButton(
                lobbyParent,
                "ReadyBtn",
                "Ready",
                new Color(0.5f, 0.3f, 0.2f, 1f),
                new Vector2(0, -280),
                new Vector2(400, 72));
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
        // Rows positioned from top of lobby area: first row at y=170, each row 67px apart
        float yBase = 170 - rowIndex * 67;

        var rowObj = new GameObject($"PlayerRow_{rowIndex}");
        rowObj.transform.SetParent(parent, false);
        var rowRect = rowObj.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0, yBase);
        rowRect.sizeDelta = new Vector2(920, 56);

        var row = new PlayerRow
        {
            Root = rowObj,         // Column 1: Player name (left side)
            NameText = createText(rowObj.transform, $"Name_{rowIndex}", string.Empty, 30)
        };
        var nameRect = row.NameText.rectTransform;
        nameRect.anchorMin = new Vector2(0.5f, 0.5f);
        nameRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameRect.pivot = new Vector2(0.5f, 0.5f);
        nameRect.anchoredPosition = new Vector2(-300, 0);
        nameRect.sizeDelta = new Vector2(220, 44);
        row.NameText.alignment = TextAlignmentOptions.Left;

        // Column 2: Class selection (center) — [<] ClassName [>]
        var ri = rowIndex;

        row.LeftArrow = createButton(
            rowObj.transform,
            $"Left_{rowIndex}",
            "<",
            new Color(0.3f, 0.3f, 0.4f, 1f),
            new Vector2(-100, 0),
            new Vector2(44, 44));
        row.LeftArrow.onClick.AddListener(() => OnClassArrow(ri, -1));
        OffsetArrowLabel(row.LeftArrow);

        row.ClassText = createText(rowObj.transform, $"Class_{rowIndex}", "Peglin", 30);
        var classRect = row.ClassText.rectTransform;
        classRect.anchorMin = new Vector2(0.5f, 0.5f);
        classRect.anchorMax = new Vector2(0.5f, 0.5f);
        classRect.pivot = new Vector2(0.5f, 0.5f);
        classRect.anchoredPosition = new Vector2(-10, 0);
        classRect.sizeDelta = new Vector2(140, 44);
        row.ClassText.alignment = TextAlignmentOptions.Center;

        row.RightArrow = createButton(
            rowObj.transform,
            $"Right_{rowIndex}",
            ">",
            new Color(0.3f, 0.3f, 0.4f, 1f),
            new Vector2(80, 0),
            new Vector2(44, 44));
        row.RightArrow.onClick.AddListener(() => OnClassArrow(ri, 1));
        OffsetArrowLabel(row.RightArrow);

        // Column 3: Ready status text (shifted left to make room for version)
        row.ReadyText = createText(rowObj.transform, $"Ready_{rowIndex}", string.Empty, 28);
        var readyRect = row.ReadyText.rectTransform;
        readyRect.anchorMin = new Vector2(0.5f, 0.5f);
        readyRect.anchorMax = new Vector2(0.5f, 0.5f);
        readyRect.pivot = new Vector2(0.5f, 0.5f);
        readyRect.anchoredPosition = new Vector2(180, 0);
        readyRect.sizeDelta = new Vector2(160, 44);
        row.ReadyText.alignment = TextAlignmentOptions.Center;

        // Column 4: Version info (right side)
        row.VersionText = createText(rowObj.transform, $"Version_{rowIndex}", string.Empty, 18);
        var verRect = row.VersionText.rectTransform;
        verRect.anchorMin = new Vector2(0.5f, 0.5f);
        verRect.anchorMax = new Vector2(0.5f, 0.5f);
        verRect.pivot = new Vector2(0.5f, 0.5f);
        verRect.anchoredPosition = new Vector2(380, 0);
        verRect.sizeDelta = new Vector2(260, 44);
        row.VersionText.alignment = TextAlignmentOptions.Left;

        _playerRows.Add(row);
    }

    private static void CreateCruciballRow(
        Transform parent,
        Func<Transform, string, string, int, TextMeshProUGUI> createText,
        Func<Transform, string, string, Color, Vector2, Vector2, Button> createButton)
    {
        // Positioned below the (up to 4) player rows, with breathing room above the
        // Start/Ready button at y=-280. Player rows now end at y ≈ -141 for 4 players.
        _cruciballRow = new GameObject("CruciballRow");
        _cruciballRow.transform.SetParent(parent, false);
        var rowRect = _cruciballRow.AddComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0, -200);
        rowRect.sizeDelta = new Vector2(560, 48);

        var label = createText(_cruciballRow.transform, "CruciballLabel", "Cruciball Level:", 28);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(-110, 0);
        labelRect.sizeDelta = new Vector2(260, 44);
        label.alignment = TextAlignmentOptions.Right;

        _cruciballLeftBtn = createButton(
            _cruciballRow.transform,
            "CruciballLeft",
            "<",
            new Color(0.3f, 0.3f, 0.4f, 1f),
            new Vector2(50, 0),
            new Vector2(40, 40));
        _cruciballLeftBtn.onClick.AddListener(() => OnCruciballArrow(-1));
        OffsetArrowLabel(_cruciballLeftBtn);

        _cruciballValueText = createText(_cruciballRow.transform, "CruciballValue", "0", 30);
        var valRect = _cruciballValueText.rectTransform;
        valRect.anchorMin = new Vector2(0.5f, 0.5f);
        valRect.anchorMax = new Vector2(0.5f, 0.5f);
        valRect.pivot = new Vector2(0.5f, 0.5f);
        valRect.anchoredPosition = new Vector2(120, 0);
        valRect.sizeDelta = new Vector2(80, 44);
        _cruciballValueText.alignment = TextAlignmentOptions.Center;

        _cruciballRightBtn = createButton(
            _cruciballRow.transform,
            "CruciballRight",
            ">",
            new Color(0.3f, 0.3f, 0.4f, 1f),
            new Vector2(190, 0),
            new Vector2(40, 40));
        _cruciballRightBtn.onClick.AddListener(() => OnCruciballArrow(1));
        OffsetArrowLabel(_cruciballRightBtn);
    }

    /// <summary>
    /// The "&lt;" / "&gt;" glyphs in this UI font sit visually high inside the button.
    /// Shift the label rect down a few pixels so the arrow appears vertically
    /// centered. CreateButton stretches the label to fill the button, so adjusting
    /// offsetMin/offsetMax shifts the entire text rect.
    /// </summary>
    private static void OffsetArrowLabel(Button btn)
    {
        if (btn == null)
        {
            return;
        }

        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null)
        {
            return;
        }

        var r = label.rectTransform;
        r.offsetMin = new Vector2(r.offsetMin.x, r.offsetMin.y - 3f);
        r.offsetMax = new Vector2(r.offsetMax.x, r.offsetMax.y - 3f);
    }

    private static void UpdateCruciballRow(bool isHost)
    {
        if (_cruciballValueText == null)
        {
            return;
        }

        _cruciballValueText.text = _hostCruciballLevel.ToString();
        // Client never sees the arrows — display is read-only.
        _cruciballLeftBtn?.gameObject.SetActive(isHost);

        _cruciballRightBtn?.gameObject.SetActive(isHost);
    }

    private static void OnCruciballArrow(int direction)
    {
        if (!_isHost)
        {
            return;
        }

        var next = _hostCruciballLevel + direction;
        if (next < 0)
        {
            next = 20;
        }

        if (next > 20)
        {
            next = 0;
        }

        _hostCruciballLevel = next;

        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        if (services.TryResolve<PlayerRegistry>(out var registry) && services.TryResolve<IGameEventRegistry>(out var er))
        {
            LobbyHelper.BroadcastLobbyState(registry, er);
        }
    }

    private static void OnClassArrow(int rowIndex, int direction)
    {
        _localChosenClass = (_localChosenClass + direction + ClassNames.Length) % ClassNames.Length;

        var services = MultiplayerPlugin.Services;
        if (services == null)
        {
            return;
        }

        if (_isHost)
        {
            // Host updates its own slot and broadcasts lobby state
            if (services.TryResolve<PlayerRegistry>(out var registry))
            {
                var hostSlot = registry.GetHostSlot();
                hostSlot?.ChosenClass = _localChosenClass;
            }

            if (services.TryResolve<PlayerRegistry>(out var reg2) && services.TryResolve<IGameEventRegistry>(out var er))
            {
                LobbyHelper.BroadcastLobbyState(reg2, er);
            }
        }
        else
        {
            // Client sends ClassSelectEvent to host over the network
            if (services.TryResolve<IMessageSender>(out var sender))
            {
                sender.Send(new ClassSelectEvent { ChosenClass = _localChosenClass });
            }
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
        if (services == null)
        {
            return;
        }

        if (!services.TryResolve<PlayerRegistry>(out var registry))
        {
            return;
        }

        if (!services.TryResolve<IGameEventRegistry>(out var eventRegistry))
        {
            return;
        }

        if (!registry.AllClientsReady)
        {
            return;
        }

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

        // Close the Steam lobby (if any) so late joiners can't enter mid-run.
        if (services.TryResolve<ISteamTransport>(out var steam))
        {
            try
            {
                steam.CloseLobbyOnStart();
            }
            catch (Exception ex) { MultiplayerPlugin.Logger?.LogWarning($"[Lobby] CloseLobbyOnStart failed: {ex.Message}"); }
        }

        // Broadcast game start (include the lobby-selected cruciball level)
        eventRegistry.Dispatch(new GameStartEvent { FinalPlayers = finalPlayers, CruciballLevel = _hostCruciballLevel });

        // Host sets its own class and starts the game
        var hostSlot = registry.GetHostSlot();
        if (hostSlot != null)
        {
            var hostClass = (Peglin.ClassSystem.Class)hostSlot.ChosenClass;
            StaticGameData.chosenClass = hostClass;
            Patches.MultiplayerClientPatches.SetCruciballManagerClass(hostClass);
            Patches.MultiplayerClientPatches.SetCruciballManagerLevel(_hostCruciballLevel);
            MultiplayerPlugin.Logger?.LogInfo($"[Lobby] Starting game: host class={hostSlot.ChosenClass}, cruciball={_hostCruciballLevel}, {finalPlayers.Count} players");
        }

        // Start the game by calling PlayButton.MovetoCharacterSelect()
        // The class select screen will be skipped by a patch since we already chose
        _gameStartReceived = true;
        _gameStartEvent = new GameStartEvent { FinalPlayers = finalPlayers, CruciballLevel = _hostCruciballLevel };

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
