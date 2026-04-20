namespace Multipeglin.UI;

using System;
using Multipeglin.Events.Handlers.Coop;
using Multipeglin.Events.Network.Coop;
using Multipeglin.Events.Subscriptions;
using Multipeglin.Multiplayer;
using Multipeglin.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattleCtrl = global::Battle.BattleController;

/// <summary>
/// Screen-space Skip Turn button shown during battle when it's the local
/// player's turn. Host click runs CoopSubscriptions.SkipCurrentTurn directly;
/// client click sends SkipTurnRequestEvent to the host.
/// </summary>
public sealed class SkipTurnButton : MonoBehaviour
{
    private GameObject _canvasObj;
    private Canvas _canvas;
    private GameObject _buttonObj;
    private Button _button;
    private TMP_FontAsset _font;

    private void Start()
    {
        TryEnsureFont();
    }

    private void OnDestroy()
    {
        if (_canvasObj != null) Destroy(_canvasObj);
    }

    private void Update()
    {
        if (MultiplayerPlugin.Services == null) { SetVisible(false); return; }
        if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode))
        {
            SetVisible(false);
            return;
        }
        if (!mode.IsHosting && !mode.IsSpectating) { SetVisible(false); return; }

        bool myTurn;
        if (mode.IsHosting)
        {
            // Host: gate on local BattleController + TurnManager (both local authority).
            bool inBattle = BattleCtrl.CurrentBattleState == BattleCtrl.BattleState.AWAITING_SHOT;
            if (!inBattle) { SetVisible(false); return; }
            if (!MultiplayerPlugin.Services.TryResolve<GameState.TurnManager>(out var tm))
            { SetVisible(false); return; }
            myTurn = tm.Phase == GameState.TurnPhase.PLAYER_AIMING && tm.CurrentPlayerSlot == 0;
        }
        else
        {
            // Client: trust host-authoritative "is my turn" flag alone. Don't gate on local
            // BattleController state, which may lag behind the turn-change event and would
            // otherwise cause the button to flicker or not appear at all.
            myTurn = TurnChangeClientHandler.IsMyTurn;
        }

        SetVisible(myTurn);
    }

    private void SetVisible(bool visible)
    {
        if (!visible)
        {
            if (_canvasObj != null) _canvasObj.SetActive(false);
            return;
        }
        if (_canvasObj == null) Build();
        _canvasObj.SetActive(true);
    }

    private void TryEnsureFont()
    {
        if (_font != null) return;
        try
        {
            foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>())
                if (tmp.font != null) { _font = tmp.font; break; }
        }
        catch { }
    }

    private void Build()
    {
        _canvasObj = new GameObject("SkipTurnCanvas");
        DontDestroyOnLoad(_canvasObj);
        _canvas = _canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 8500;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasObj.AddComponent<GraphicRaycaster>();

        _buttonObj = new GameObject("SkipTurnButton");
        _buttonObj.transform.SetParent(_canvasObj.transform, false);

        var img = _buttonObj.AddComponent<Image>();
        // Peglin-style gray button: soft neutral gray with subtle alpha
        var baseColor = new Color(0.35f, 0.35f, 0.38f, 0.95f);
        img.color = baseColor;

        _button = _buttonObj.AddComponent<Button>();
        var colors = _button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f); // slight brighten
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        _button.colors = colors;
        _button.onClick.AddListener(OnClick);

        // Anchor middle-left: above the discard buttons, below the coop peglin sprites.
        var rect = _buttonObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(213f, 105f);
        rect.sizeDelta = new Vector2(260f, 70f);

        // Dark border frame behind the fill
        var borderObj = new GameObject("Border");
        borderObj.transform.SetParent(_buttonObj.transform, false);
        borderObj.transform.SetAsFirstSibling();
        var borderImg = borderObj.AddComponent<Image>();
        borderImg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        var borderRect = borderObj.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-4f, -4f);
        borderRect.offsetMax = new Vector2(4f, 4f);

        // Label
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(_buttonObj.transform, false);
        var tmp = labelObj.AddComponent<TextMeshProUGUI>();
        TryEnsureFont();
        if (_font != null) tmp.font = _font;
        tmp.text = "SKIP TURN";
        tmp.fontSize = 32;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        var labelRect = tmp.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private void OnClick()
    {
        try
        {
            if (MultiplayerPlugin.Services == null) return;
            if (!MultiplayerPlugin.Services.TryResolve<IMultiplayerMode>(out var mode)) return;

            if (mode.IsHosting)
            {
                var subs = CoopSubscriptions.Instance;
                if (subs == null)
                {
                    MultiplayerPlugin.Logger?.LogWarning("[SkipTurnButton] Host click but CoopSubscriptions.Instance null");
                    return;
                }
                subs.SkipCurrentTurn(0, "host UI");
            }
            else if (mode.IsSpectating)
            {
                if (!MultiplayerPlugin.Services.TryResolve<IMessageSender>(out var sender))
                {
                    MultiplayerPlugin.Logger?.LogWarning("[SkipTurnButton] Client click but IMessageSender not resolvable");
                    return;
                }
                sender.Send(new SkipTurnRequestEvent());
                MultiplayerPlugin.Logger?.LogInfo("[SkipTurnButton] Client sent SkipTurnRequestEvent");
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogWarning($"[SkipTurnButton] Click failed: {ex.Message}");
        }
    }
}
