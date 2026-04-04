using System;
using BepInEx.Logging;
using PeglinMods.Multiplayer.Events.Handlers.Coop;
using PeglinMods.Multiplayer.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Shows turn information as a banner at the top of the screen.
/// Displays "YOUR TURN" (green) when it's this player's turn,
/// or "{PlayerName}'s turn" (yellow) when spectating.
/// Auto-fades after 3 seconds but re-appears on turn change.
/// </summary>
public class TurnIndicatorUI : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    private GameObject _canvasObj;
    private GameObject _bannerObj;
    private TextMeshProUGUI _bannerText;
    private Image _bannerBg;
    private CanvasGroup _canvasGroup;

    // Fade state
    private float _showTimer;
    private const float ShowDuration = 3f;
    private const float FadeSpeed = 2f;

    // Track turn changes
    private string _lastTurnMessage = "";
    private int _lastRound = -1;
    private int _lastActiveSlot = -1;

    private void Start()
    {
        try
        {
            CreateUI();
        }
        catch (Exception ex)
        {
            Log?.LogError($"[TurnIndicatorUI] Start failed: {ex}");
        }
    }

    private void CreateUI()
    {
        // Screen-space overlay canvas
        _canvasObj = new GameObject("TurnIndicatorCanvas");
        DontDestroyOnLoad(_canvasObj);

        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // Above game UI, below multiplayer overlay

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = _canvasObj.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;

        // Banner background at top of screen
        _bannerObj = new GameObject("TurnBanner");
        _bannerObj.transform.SetParent(_canvasObj.transform, false);
        _bannerBg = _bannerObj.AddComponent<Image>();
        _bannerBg.color = new Color(0, 0, 0, 0.7f);

        var bannerRect = _bannerObj.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.25f, 1f);
        bannerRect.anchorMax = new Vector2(0.75f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0, 0);
        bannerRect.sizeDelta = new Vector2(0, 64);

        // Banner text
        var textObj = new GameObject("TurnText");
        textObj.transform.SetParent(_bannerObj.transform, false);
        _bannerText = textObj.AddComponent<TextMeshProUGUI>();
        _bannerText.text = "";
        _bannerText.fontSize = 36;
        _bannerText.alignment = TextAlignmentOptions.Center;
        _bannerText.color = Color.white;

        var textRect = _bannerText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    private void Update()
    {
        try
        {
            if (_canvasObj == null || _canvasGroup == null) return;

            // Only show in multiplayer
            var services = MultiplayerPlugin.Services;
            if (services == null) { Hide(); return; }
            if (!services.TryResolve<IMultiplayerMode>(out var mode)) { Hide(); return; }
            if (!mode.IsHosting && !mode.IsSpectating) { Hide(); return; }

            var turnState = TurnChangeClientHandler.LatestTurnState;
            if (turnState == null) { Hide(); return; }

            // Detect turn change
            string currentMessage = TurnChangeClientHandler.TurnMessage;
            bool turnChanged = (turnState.ActiveSlotIndex != _lastActiveSlot) ||
                               (turnState.RoundNumber != _lastRound) ||
                               (currentMessage != _lastTurnMessage);

            if (turnChanged)
            {
                _lastActiveSlot = turnState.ActiveSlotIndex;
                _lastRound = turnState.RoundNumber;
                _lastTurnMessage = currentMessage;
                Show(currentMessage, TurnChangeClientHandler.IsMyTurn);
            }

            // Fade logic
            if (_showTimer > 0)
            {
                _showTimer -= Time.unscaledDeltaTime;
                float targetAlpha = (_showTimer > 0) ? 1f : 0f;
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * FadeSpeed);
            }
            else if (_canvasGroup.alpha > 0)
            {
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 0f, Time.unscaledDeltaTime * FadeSpeed);
            }
        }
        catch (Exception ex)
        {
            Log?.LogError($"[TurnIndicatorUI] Update error: {ex.Message}");
        }
    }

    private void Show(string message, bool isMyTurn)
    {
        if (string.IsNullOrEmpty(message))
        {
            _showTimer = 0;
            return;
        }

        if (_bannerText != null)
        {
            _bannerText.text = message;
            _bannerText.color = isMyTurn
                ? new Color(0.3f, 1f, 0.3f) // Green for your turn
                : new Color(1f, 0.9f, 0.3f); // Yellow for others
        }

        if (_bannerBg != null)
        {
            _bannerBg.color = isMyTurn
                ? new Color(0.1f, 0.3f, 0.1f, 0.8f)
                : new Color(0.3f, 0.25f, 0.05f, 0.7f);
        }

        _showTimer = ShowDuration;
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;
    }

    private void Hide()
    {
        if (_canvasGroup != null && _canvasGroup.alpha > 0)
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 0f, Time.unscaledDeltaTime * FadeSpeed);
    }

    private void OnDestroy()
    {
        if (_canvasObj != null)
            Destroy(_canvasObj);
    }
}
