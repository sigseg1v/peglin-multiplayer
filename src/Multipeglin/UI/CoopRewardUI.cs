using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Multipeglin.Events;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.GameState;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Multipeglin.UI;

/// <summary>
/// Displays reward/relic selection overlays for co-op multiplayer.
/// When the host sends relic or reward choices, this UI shows buttons
/// for the client to pick from. After choosing, shows a waiting message
/// until all players have decided.
/// </summary>
public class CoopRewardUI : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    private GameObject _canvasObj;
    private GameObject _overlayPanel;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _statusText;
    private GameObject _buttonContainer;
    private readonly List<GameObject> _buttons = new List<GameObject>();

    // Track what we're currently displaying
    private enum DisplayState
    {
        Hidden,
        RelicChoices,
        RewardChoices,
        Waiting
    }

    private DisplayState _currentState = DisplayState.Hidden;
    private int _displayedRelicCount;
    private int _displayedRewardCount;

    // Track scene changes to auto-hide the overlay when leaving a battle
    private string _lastSceneName;

    // Host-side force-continue: after 60s of waiting on the same phase, show a
    // "Force Continue" button so the host can break out of any client-side softlock.
    private const float ForceContinueDelaySeconds = 60f;
    private float _hostWaitingStartTime = -1f;
    private string _hostWaitingPhaseKey;
    private GameObject _forceContinueButton;

    // Navigate-phase standalone force button: shown independent of the dimmed
    // overlay so the host can keep aiming while the timer runs out, but force-
    // resolve once 60s elapse without all players shooting.
    private GameObject _navForceButton;
    private float _navPhaseStartedAt = -1f;

    private void Start()
    {
        try
        {
            CreateUI();
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopRewardUI] Start failed: {ex}");
        }
    }

    private void CreateUI()
    {
        _canvasObj = new GameObject("CoopRewardCanvas");
        DontDestroyOnLoad(_canvasObj);

        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // Above everything

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasObj.AddComponent<GraphicRaycaster>();

        // Full-screen overlay panel (hidden by default)
        _overlayPanel = new GameObject("RewardOverlay");
        _overlayPanel.transform.SetParent(_canvasObj.transform, false);
        var overlayImg = _overlayPanel.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.88f);
        StretchFill(_overlayPanel.GetComponent<RectTransform>());

        // Title text
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(_overlayPanel.transform, false);
        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Choose a Reward";
        _titleText.fontSize = 48;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = Color.white;
        var titleRect = _titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1);
        titleRect.anchorMax = new Vector2(0.5f, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -40);
        titleRect.sizeDelta = new Vector2(800, 64);

        // Button container (centered area for choice buttons)
        _buttonContainer = new GameObject("ButtonContainer");
        _buttonContainer.transform.SetParent(_overlayPanel.transform, false);
        var containerRect = _buttonContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = new Vector2(0, 0);
        containerRect.sizeDelta = new Vector2(900, 400);

        // Status text (below buttons, for "Waiting for others...")
        var statusObj = new GameObject("StatusText");
        statusObj.transform.SetParent(_overlayPanel.transform, false);
        _statusText = statusObj.AddComponent<TextMeshProUGUI>();
        _statusText.text = string.Empty;
        _statusText.fontSize = 32;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = new Color(0.8f, 0.8f, 0.3f);
        var statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 0);
        statusRect.anchorMax = new Vector2(0.5f, 0);
        statusRect.pivot = new Vector2(0.5f, 0);
        statusRect.anchoredPosition = new Vector2(0, 80);
        statusRect.sizeDelta = new Vector2(800, 48);

        _overlayPanel.SetActive(false);
    }

    private void Update()
    {
        try
        {
            if (_overlayPanel == null)
            {
                return;
            }

            // Hide overlay on scene changes:
            // 1. When entering Battle (previous reward phase is over)
            // 2. When leaving Battle to any other scene (post-battle rewards are done)
            // 3. When entering a map scene (ensures stale overlays don't block input)
            var currentScene = SceneManager.GetActiveScene().name;
            if (_lastSceneName != null && _lastSceneName != currentScene)
            {
                var shouldHide = currentScene == "Battle"
                    || _lastSceneName == "Battle"
                    || currentScene == "ForestMap" || currentScene == "CastleMap"
                    || currentScene == "MinesMap" || currentScene == "CoreMap";
                if (shouldHide && _currentState != DisplayState.Hidden)
                {
                    Log?.LogInfo($"[CoopRewardUI] Scene change {_lastSceneName} -> {currentScene}, hiding overlay");
                    HideOverlay();
                    CoopRewardState.Reset();
                    CoopNavigateState.Reset();
                    DestroyNavForceButton();
                    _navPhaseStartedAt = -1f;
                    Patches.MultiplayerClientPatches.AllowNavigateLogic = false;
                }
            }

            _lastSceneName = currentScene;

            // Only active in multiplayer
            var services = MultiplayerPlugin.Services;
            if (services == null)
            {
                return;
            }

            if (!services.TryResolve<IMultiplayerMode>(out var mode))
            {
                return;
            }

            if (!mode.IsHosting && !mode.IsSpectating)
            {
                return;
            }

            // Standalone navigate-phase force button (host only; independent of overlay).
            TickNavigateStandaloneForce(mode);

            // Repaint slot tally colors (everyone, every frame the tally changes).
            CoopNavigateSlotPainter.Tick();

            // Check if all choices are complete -- hide overlay
            if (CoopRewardState.AllChoicesComplete)
            {
                if (_currentState != DisplayState.Hidden)
                {
                    HideOverlay();
                    CoopRewardState.Reset();
                }

                return;
            }

            // Check if we're waiting for other players (reward/relic phases) OR
            // we're in the parallel-shoot navigate phase having already voted.
            var inNavigateWait = CoopNavigateState.PhaseActive
                && CoopNavigateState.LocalVoteCast
                && !CoopNavigateState.Resolved;
            if (CoopRewardState.WaitingForOtherPlayers || inNavigateWait)
            {
                if (_currentState != DisplayState.Waiting)
                {
                    ShowWaiting();
                }

                TickHostForceContinue();
                return;
            }

            // Check for pending relic choices
            var relicChoices = CoopRewardState.PendingRelicChoices;
            if (relicChoices?.Choices != null && relicChoices.Choices.Count > 0)
            {
                if (_currentState != DisplayState.RelicChoices || _displayedRelicCount != relicChoices.Choices.Count)
                {
                    ShowRelicChoices(relicChoices);
                }

                return;
            }

            // Post-battle rewards now use the native BattleUpgradeCanvas.
            // Skip the custom reward display — it's replaced by PostBattleStartEvent flow.
            // (RewardChoicesEvent is no longer sent for post-battle rewards.)

            // Nothing to show
            if (_currentState != DisplayState.Hidden)
            {
                HideOverlay();
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopRewardUI] Update error: {ex.Message}");
        }
    }

    private void ShowRelicChoices(RelicChoicesEvent choices)
    {
        ClearButtons();

        _titleText.text = "Choose a Relic";
        _statusText.text = string.Empty;
        _overlayPanel.SetActive(true);
        _currentState = DisplayState.RelicChoices;
        _displayedRelicCount = choices.Choices.Count;

        float buttonWidth = 340;
        float spacing = 20;
        var totalWidth = choices.Choices.Count * buttonWidth + (choices.Choices.Count - 1) * spacing;
        var startX = -totalWidth / 2f + buttonWidth / 2f;

        for (var i = 0; i < choices.Choices.Count; i++)
        {
            var relic = choices.Choices[i];
            var xPos = startX + i * (buttonWidth + spacing);

            // Try to resolve relic description via localization as a fallback
            var desc = relic.LocKey ?? string.Empty;
            try
            {
                // If LocKey looks like a raw loc key (no spaces, starts with lowercase),
                // try to resolve it via the localization system
                if (!string.IsNullOrEmpty(desc) && !desc.Contains(" "))
                {
                    var resolved = I2.Loc.LocalizationManager.GetTranslation("Relics/" + desc + "_desc");
                    if (string.IsNullOrEmpty(resolved))
                    {
                        resolved = I2.Loc.LocalizationManager.GetTranslation(desc);
                    }

                    if (!string.IsNullOrEmpty(resolved))
                    {
                        desc = resolved;
                    }
                }
            }
            catch
            {
                /* localization not available, use as-is */
            }

            var btn = CreateChoiceButton(
                _buttonContainer.transform,
                $"RelicBtn_{i}",
                relic.EffectName ?? $"Relic {relic.Effect}",
                desc,
                new Color(0.25f, 0.2f, 0.45f),
                new Vector2(xPos, 0),
                new Vector2(buttonWidth, 260));

            var capturedEffect = relic.Effect;
            btn.onClick.AddListener(() => OnRelicChosen(capturedEffect));
        }

        Log?.LogInfo($"[CoopRewardUI] Showing {choices.Choices.Count} relic choices");
    }

    private void ShowRewardChoices(RewardChoicesEvent choices)
    {
        ClearButtons();

        _titleText.text = "Choose a Reward";
        _statusText.text = string.Empty;
        _overlayPanel.SetActive(true);
        _currentState = DisplayState.RewardChoices;
        _displayedRewardCount = choices.Options.Count;

        float buttonWidth = 240;
        float spacing = 16;
        var totalWidth = choices.Options.Count * buttonWidth + (choices.Options.Count - 1) * spacing;
        var startX = -totalWidth / 2f + buttonWidth / 2f;

        for (var i = 0; i < choices.Options.Count; i++)
        {
            var option = choices.Options[i];
            var xPos = startX + i * (buttonWidth + spacing);

            var bgColor = GetRewardColor(option.Type);

            var btn = CreateChoiceButton(
                _buttonContainer.transform,
                $"RewardBtn_{i}",
                option.DisplayName ?? option.Type,
                option.Description ?? string.Empty,
                bgColor,
                new Vector2(xPos, 0),
                new Vector2(buttonWidth, 200));

            var capturedIndex = option.OptionIndex;
            btn.onClick.AddListener(() => OnRewardChosen(capturedIndex));
        }

        Log?.LogInfo($"[CoopRewardUI] Showing {choices.Options.Count} reward choices");
    }

    private void ShowWaiting()
    {
        ClearButtons();

        var services = MultiplayerPlugin.Services;
        var isHost = services?.TryResolve<IMultiplayerMode>(out var m) == true && m.IsHosting;

        // Use context-specific messages
        if (CoopRewardState.ShopAwaitingHostNavigation
            || CoopRewardState.TextScenarioAwaitingHostNavigation
            || CoopRewardState.PegMinigameAwaitingHostNavigation)
        {
            // Client-side post-shop/event/peg-minigame: choice is done, host is picking next stage.
            _titleText.text = "Waiting for other players to select the next stage...";
        }
        else if (CoopRewardState.TreasureAwaitingHostNavigation)
        {
            // Client-side post-treasure: relic chosen, host is shooting the chest.
            _titleText.text = "Waiting for other players...";
        }
        else if (CoopRewardState.TextScenarioPhaseActive)
        {
            _titleText.text = "Waiting for other players to finish the event...";
        }
        else if (CoopRewardState.ShopPhaseActive)
        {
            _titleText.text = "Waiting for other players to finish shopping...";
        }
        else if (CoopRewardState.TreasurePhaseActive)
        {
            _titleText.text = "Waiting for other players to choose a relic...";
        }
        else if (CoopRewardState.PegMinigamePhaseActive)
        {
            _titleText.text = "Waiting for other players to finish...";
        }
        else if (CoopNavigateState.PhaseActive && CoopNavigateState.LocalVoteCast && !CoopNavigateState.Resolved)
        {
            _titleText.text = "Waiting for other players to navigate...";
        }
        else if (CoopRewardState.HostRelicSelectionActive)
        {
            _titleText.text = isHost
                ? "Waiting for all players to choose their initial relic..."
                : "Other players are choosing their initial relics...";
        }
        else if (CoopRewardState.ClientInNativeRewardPhase || CoopRewardState.HostRewardPhaseActive)
        {
            // Post-battle reward — client picked from native BattleUpgradeCanvas
            // and is now waiting for host to finish its own rewards and navigation.
            _titleText.text = "Waiting for other players...";
        }
        else
        {
            _titleText.text = "Waiting...";
        }

        _statusText.text = string.Empty;
        _overlayPanel.SetActive(true);
        _currentState = DisplayState.Waiting;

        // Track host-side waiting phase for the 60s force-continue button.
        // Reset the timer whenever the host transitions between distinct phases.
        if (isHost)
        {
            var phaseKey = ResolveHostWaitingPhase();
            if (phaseKey != null)
            {
                if (_hostWaitingPhaseKey != phaseKey)
                {
                    _hostWaitingPhaseKey = phaseKey;
                    _hostWaitingStartTime = Time.unscaledTime;
                    DestroyForceContinueButton();
                }
            }
            else
            {
                ClearHostWaitingTimer();
            }
        }
        else
        {
            ClearHostWaitingTimer();
        }
    }

    private void HideOverlay()
    {
        ClearButtons();
        DestroyForceContinueButton();
        ClearHostWaitingTimer();
        _overlayPanel.SetActive(false);
        _currentState = DisplayState.Hidden;
        _displayedRelicCount = 0;
        _displayedRewardCount = 0;
    }

    /// <summary>
    /// Returns a phase key the host is currently waiting on (and has finished its
    /// own part of), or null if the host isn't waiting on a recoverable phase.
    /// </summary>
    private static string ResolveHostWaitingPhase()
    {
        if (CoopRewardState.ShopPhaseActive && CoopRewardState.HostShopDone)
        {
            return "shop";
        }

        if (CoopRewardState.TreasurePhaseActive && CoopRewardState.HostTreasureDone)
        {
            return "treasure";
        }

        if (CoopRewardState.PegMinigamePhaseActive && CoopRewardState.HostPegMinigameDone)
        {
            return "peg_minigame";
        }

        if (CoopRewardState.TextScenarioPhaseActive && CoopRewardState.HostTextScenarioDone)
        {
            return "text_scenario";
        }

        if (CoopRewardState.HostRewardPhaseActive && CoopRewardState.HostRewardsDone)
        {
            return "post_battle";
        }

        if (CoopRewardState.HostRelicSelectionActive && CoopRewardState.HostHasChosenRelic)
        {
            return "starting_relic";
        }

        if (CoopNavigateState.PhaseActive && !CoopNavigateState.Resolved)
        {
            // Host can force-resolve the navigate phase regardless of whether
            // it has voted itself — covers the case where the host is stuck
            // (e.g., its own nav ball is jammed and clients are waiting).
            return "navigate";
        }

        return null;
    }

    private void ClearHostWaitingTimer()
    {
        _hostWaitingStartTime = -1f;
        _hostWaitingPhaseKey = null;
    }

    private void DestroyForceContinueButton()
    {
        if (_forceContinueButton != null)
        {
            Destroy(_forceContinueButton);
            _forceContinueButton = null;
        }
    }

    /// <summary>
    /// Once the host has been waiting on the same phase for 60s, spawn a
    /// "Force Continue" button so the host can resume past a stuck client.
    /// </summary>
    private void TickHostForceContinue()
    {
        if (_currentState != DisplayState.Waiting)
        {
            return;
        }

        if (_hostWaitingStartTime < 0f || _hostWaitingPhaseKey == null)
        {
            return;
        }

        // Re-validate the phase: clients may have completed naturally meanwhile
        // (in which case AllChoicesComplete will hide the overlay on the next tick).
        if (ResolveHostWaitingPhase() != _hostWaitingPhaseKey)
        {
            ClearHostWaitingTimer();
            DestroyForceContinueButton();
            return;
        }

        if (_forceContinueButton != null)
        {
            return;
        }

        if (Time.unscaledTime - _hostWaitingStartTime < ForceContinueDelaySeconds)
        {
            return;
        }

        _forceContinueButton = CreateForceContinueButton();
    }

    private GameObject CreateForceContinueButton()
    {
        var obj = new GameObject("ForceContinueButton");
        obj.transform.SetParent(_overlayPanel.transform, false);

        var img = obj.AddComponent<Image>();
        img.color = new Color(0.55f, 0.18f, 0.18f);

        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.7f, 0.25f, 0.25f);
        colors.pressedColor = new Color(0.4f, 0.12f, 0.12f);
        btn.colors = colors;

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0);
        rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 160);
        rect.sizeDelta = new Vector2(360, 70);

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(obj.transform, false);
        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = "Force Continue";
        label.fontSize = 32;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        btn.onClick.AddListener(OnForceContinueClicked);
        return obj;
    }

    private void OnForceContinueClicked()
    {
        var phase = _hostWaitingPhaseKey;
        Log?.LogWarning($"[CoopRewardUI] Force Continue clicked — phase='{phase}'");

        try
        {
            switch (phase)
            {
                case "shop":
                    ForceContinueShop();
                    break;
                case "treasure":
                    ForceContinueTreasure();
                    break;
                case "peg_minigame":
                    ForceContinuePegMinigame();
                    break;
                case "text_scenario":
                    ForceContinueTextScenario();
                    break;
                case "post_battle":
                    ForceContinuePostBattle();
                    break;
                case "starting_relic":
                    ForceContinueStartingRelic();
                    break;
                case "navigate":
                    ForceContinueNavigate();
                    break;
                default:
                    Log?.LogWarning($"[CoopRewardUI] Force Continue: unknown phase '{phase}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopRewardUI] Force Continue failed: {ex}");
        }

        DestroyForceContinueButton();
        ClearHostWaitingTimer();
    }

    private static void DispatchAllChoicesComplete(string phase)
    {
        var services = MultiplayerPlugin.Services;
        if (services?.TryResolve<IGameEventRegistry>(out var reg) == true)
        {
            reg.Dispatch(new AllChoicesCompleteEvent { Phase = phase });
        }
    }

    private void ForceContinueShop()
    {
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.ShopPhaseActive = false;
        CoopRewardState.ShopCompletionProceeded = true;
        DispatchAllChoicesComplete("shop");

        var pending = CoopRewardState.PendingShopManager;
        CoopRewardState.PendingShopManager = null;
        if (pending is global::Scenarios.Shop.ShopManager shopMgr)
        {
            Log?.LogWarning("[CoopRewardUI] Force-resuming host CloseStore");
            shopMgr.CloseStore();
        }
    }

    private void ForceContinueTreasure()
    {
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.TreasurePhaseActive = false;
        DispatchAllChoicesComplete("treasure");

        var pending = CoopRewardState.PendingChestController;
        CoopRewardState.PendingChestController = null;
        if (pending != null)
        {
            Log?.LogWarning("[CoopRewardUI] Force-resuming host ChestScenarioController.Skip");
            pending.Skip();
        }
    }

    private void ForceContinuePegMinigame()
    {
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.PegMinigamePhaseActive = false;
        DispatchAllChoicesComplete("peg_minigame");

        var pending = CoopRewardState.PendingPegMinigameManager;
        CoopRewardState.PendingPegMinigameManager = null;
        if (pending != null)
        {
            Log?.LogWarning("[CoopRewardUI] Force-resuming host PegMinigameManager.FadeAndLoad");
            pending.FadeAndLoad();
        }
    }

    private void ForceContinueTextScenario()
    {
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.TextScenarioPhaseActive = false;
        DispatchAllChoicesComplete("text_scenario");

        var pending = CoopRewardState.PendingDialogueSystemScenario;
        CoopRewardState.PendingDialogueSystemScenario = null;
        if (pending is RNG.Scenarios.DialogueSystemScenario scenario)
        {
            Log?.LogWarning("[CoopRewardUI] Force-resuming host DialogueSystemScenario.ConversationEnded");
            scenario.ConversationEnded();
        }
    }

    private void ForceContinuePostBattle()
    {
        CoopRewardState.WaitingForOtherPlayers = false;
        CoopRewardState.HostRewardPhaseActive = false;
        DispatchAllChoicesComplete("post_battle");

        var pbc = CoopRewardState.PendingPostBattleController;
        CoopRewardState.PendingPostBattleController = null;
        if (pbc != null)
        {
            Log?.LogWarning("[CoopRewardUI] Force-resuming host PostBattleController.StartNavigation");
            var navMethod = HarmonyLib.AccessTools.Method(typeof(global::Battle.PostBattleController), "StartNavigation");
            navMethod?.Invoke(pbc, new object[] { true });
        }
    }

    private void ForceContinueNavigate()
    {
        Log?.LogWarning("[CoopRewardUI] Force-resolving navigate phase");
        CoopNavigateResolver.ForceResolve();
        DestroyNavForceButton();
    }

    /// <summary>
    /// Standalone navigate-phase force-skip button. Appears in the bottom-right
    /// of the screen on the host after 60s in-phase, regardless of whether the
    /// host has voted. Independent of the dimmed waiting overlay so the host
    /// can keep aiming while the timer runs out.
    /// </summary>
    private void TickNavigateStandaloneForce(IMultiplayerMode mode)
    {
        if (mode == null || !mode.IsHosting)
        {
            DestroyNavForceButton();
            _navPhaseStartedAt = -1f;
            return;
        }

        if (!CoopNavigateState.PhaseActive || CoopNavigateState.Resolved)
        {
            DestroyNavForceButton();
            _navPhaseStartedAt = -1f;
            return;
        }

        // Track when we first observed the active phase.
        if (_navPhaseStartedAt < 0f)
        {
            _navPhaseStartedAt = Time.unscaledTime;
        }

        // If the dimmed-overlay flow is already showing the force-continue
        // button (host has voted), skip the standalone variant to avoid two
        // overlapping buttons.
        if (_currentState == DisplayState.Waiting && _hostWaitingPhaseKey == "navigate")
        {
            DestroyNavForceButton();
            return;
        }

        if (Time.unscaledTime - _navPhaseStartedAt < ForceContinueDelaySeconds)
        {
            return;
        }

        if (_navForceButton != null)
        {
            return;
        }

        _navForceButton = CreateNavForceButton();
    }

    private GameObject CreateNavForceButton()
    {
        var obj = new GameObject("NavForceContinueButton");
        obj.transform.SetParent(_canvasObj.transform, false);

        var img = obj.AddComponent<Image>();
        img.color = new Color(0.55f, 0.18f, 0.18f, 0.9f);

        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.7f, 0.25f, 0.25f);
        colors.pressedColor = new Color(0.4f, 0.12f, 0.12f);
        btn.colors = colors;

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-30, 30);
        rect.sizeDelta = new Vector2(280, 60);

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(obj.transform, false);
        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = "Force Continue";
        label.fontSize = 26;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        btn.onClick.AddListener(ForceContinueNavigate);
        return obj;
    }

    private void DestroyNavForceButton()
    {
        if (_navForceButton != null)
        {
            Destroy(_navForceButton);
            _navForceButton = null;
        }
    }

    private void ForceContinueStartingRelic()
    {
        CoopRewardState.HostRelicSelectionActive = false;
        CoopRewardState.AllChoicesComplete = true;
        CoopRewardState.WaitingForOtherPlayers = false;
        DispatchAllChoicesComplete("starting_relic");

        if (CoopRewardState.PendingGameInitInstance is GameInit gameInit)
        {
            CoopRewardState.PendingGameInitInstance = null;
            Log?.LogWarning("[CoopRewardUI] Force-resuming host GameInit.LoadMapScene");
            var loadMapMethod = typeof(GameInit).GetMethod("LoadMapScene",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loadMapMethod?.Invoke(gameInit, null);
        }
    }

    private void OnRelicChosen(int relicEffect)
    {
        Log?.LogInfo($"[CoopRewardUI] Relic chosen: effect={relicEffect}");

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IMultiplayerMode>(out var mode) == true && mode.IsHosting)
            {
                // Host: apply the relic directly via RelicManager, then mark as chosen
                var relicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                // Find the Relic asset by effect — can't use CommonRelicPool because
                // GetMultipleRelicsOffOfQueue already dequeued the relics from the pool.
                var allRelics = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
                foreach (var relic in allRelics)
                {
                    if ((int)relic.effect == relicEffect)
                    {
                        if (relicMgrs != null && relicMgrs.Length > 0)
                        {
                            Patches.MultiplayerClientPatches.AllowRelicSync = true;
                            try
                            {
                                relicMgrs[0].AddRelic(relic);
                            }
                            finally { Patches.MultiplayerClientPatches.AllowRelicSync = false; }
                        }

                        Log?.LogInfo($"[CoopRewardUI] Host added relic: {relic.effect} ({relic.locKey})");
                        break;
                    }
                }
                // Mark host as chosen - same logic as GameInit_LoadMapScene_Prefix
                CoopRewardState.HostHasChosenRelic = true;

                // Save host state with the new relic
                if (services.TryResolve<CoopStateManager>(out var coopState))
                {
                    coopState.SaveActivePlayerState();
                }

                // Check if all clients have also chosen
                if (CoopRewardState.AllClientRelicChoicesReceived)
                {
                    Log?.LogInfo("[CoopRewardUI] All relic choices received — proceeding to map");
                    CoopRewardState.HostRelicSelectionActive = false;
                    CoopRewardState.AllChoicesComplete = true;
                    CoopRewardState.WaitingForOtherPlayers = false;
                    if (services.TryResolve<IGameEventRegistry>(out var reg))
                    {
                        reg.Dispatch(new AllChoicesCompleteEvent { Phase = "starting_relic" });
                    }

                    var gameInit = CoopRewardState.PendingGameInitInstance as GameInit;
                    if (gameInit != null)
                    {
                        var loadMapMethod = typeof(GameInit).GetMethod("LoadMapScene",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        loadMapMethod?.Invoke(gameInit, null);
                    }
                }
            }
            else
            {
                // Client: send to host via network
                if (services?.TryResolve<IMessageSender>(out var sender) == true)
                {
                    sender.Send(new RelicChoiceEvent { ChosenRelicEffect = relicEffect });
                }

                // Also add the relic to the CLIENT's local RelicManager so it shows in the UI.
                // The host tracks it in CoopPlayerState; the client needs it in RelicManager for display.
                var clientRelicMgrs = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.RelicManager>();
                if (clientRelicMgrs != null && clientRelicMgrs.Length > 0)
                {
                    var allRelics = UnityEngine.Resources.FindObjectsOfTypeAll<Relics.Relic>();
                    foreach (var relic in allRelics)
                    {
                        if ((int)relic.effect == relicEffect)
                        {
                            Patches.MultiplayerClientPatches.AllowRelicSync = true;
                            try
                            {
                                clientRelicMgrs[0].AddRelic(relic);
                            }
                            finally { Patches.MultiplayerClientPatches.AllowRelicSync = false; }

                            Log?.LogInfo($"[CoopRewardUI] Client added relic locally: {relic.effect}");
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopRewardUI] Failed to process relic choice: {ex.Message}");
        }

        CoopRewardState.PendingRelicChoices = null;
        if (!CoopRewardState.AllChoicesComplete)
        {
            CoopRewardState.WaitingForOtherPlayers = true;
            ShowWaiting();
        }
    }

    private void OnRewardChosen(int optionIndex)
    {
        Log?.LogInfo($"[CoopRewardUI] Reward chosen: index={optionIndex}");

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IMessageSender>(out var sender) == true)
            {
                sender.Send(new RewardChoiceEvent { ChosenOptionIndex = optionIndex });
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopRewardUI] Failed to send reward choice: {ex.Message}");
        }

        // Post-battle reward selection is per-player — no need to wait for others.
        // Hide the overlay immediately after the choice is sent to the host.
        CoopRewardState.PendingRewardChoices = null;
        CoopRewardState.AllChoicesComplete = true;
    }

    // --- UI Factory Helpers ---

    private Button CreateChoiceButton(
        Transform parent,
        string name,
        string title,
        string description,
        Color bgColor,
        Vector2 position,
        Vector2 size)
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
            Mathf.Min(bgColor.b + 0.15f, 1f),
            1f);
        colors.pressedColor = new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f, 1f);
        btn.colors = colors;

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // Title text (top portion of button)
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(obj.transform, false);
        var titleTmp = titleObj.AddComponent<TextMeshProUGUI>();
        titleTmp.text = title;
        titleTmp.fontSize = 42;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;
        titleTmp.enableWordWrapping = true;
        var titleRect = titleTmp.rectTransform;
        titleRect.anchorMin = new Vector2(0, 0.55f);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = new Vector2(8, 0);
        titleRect.offsetMax = new Vector2(-8, -8);

        // Description text (bottom portion of button)
        if (!string.IsNullOrEmpty(description))
        {
            var descObj = new GameObject("Description");
            descObj.transform.SetParent(obj.transform, false);
            var descTmp = descObj.AddComponent<TextMeshProUGUI>();
            descTmp.text = description;
            descTmp.fontSize = 30;
            descTmp.alignment = TextAlignmentOptions.Center;
            descTmp.color = new Color(0.8f, 0.8f, 0.8f);
            descTmp.enableWordWrapping = true;
            descTmp.overflowMode = TextOverflowModes.Ellipsis;
            var descRect = descTmp.rectTransform;
            descRect.anchorMin = new Vector2(0, 0);
            descRect.anchorMax = new Vector2(1, 0.55f);
            descRect.offsetMin = new Vector2(8, 8);
            descRect.offsetMax = new Vector2(-8, 0);
        }

        _buttons.Add(obj);
        return btn;
    }

    private void ClearButtons()
    {
        foreach (var btn in _buttons)
        {
            if (btn != null)
            {
                Destroy(btn);
            }
        }

        _buttons.Clear();
    }

    private static Color GetRewardColor(string type)
    {
        switch (type)
        {
            case "relic":
                return new Color(0.25f, 0.2f, 0.45f);
            case "orb_upgrade":
                return new Color(0.2f, 0.35f, 0.5f);
            case "orb_add":
                return new Color(0.2f, 0.4f, 0.3f);
            case "heal":
                return new Color(0.4f, 0.25f, 0.25f);
            case "max_hp":
                return new Color(0.45f, 0.2f, 0.3f);
            case "skip":
                return new Color(0.3f, 0.3f, 0.2f);
            default:
                return new Color(0.25f, 0.25f, 0.25f);
        }
    }

    private static void StretchFill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        ClearButtons();
        DestroyNavForceButton();
        if (_canvasObj != null)
        {
            Destroy(_canvasObj);
        }
    }
}
