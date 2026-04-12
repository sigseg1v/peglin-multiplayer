using System;
using System.Collections.Generic;
using System.Linq;
using Battle.Attacks;
using PeglinMods.Multiplayer.Events.Network.Scenarios;
using PeglinMods.Multiplayer.Multiplayer;
using PeglinMods.Multiplayer.Network;
using PeglinMods.Multiplayer.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Interactive mirror event UI shown on the client.
/// Allows the client to choose "Remove an Orb" or "Remove All Orbs".
/// For "Remove an Orb", shows a grid of the client's orbs to pick from.
/// </summary>
public static class MirrorEventUI
{
    private static GameObject _canvasObj;
    private static GameObject _choicePanel;
    private static GameObject _orbGridPanel;
    private static bool _isActive;

    public static bool IsActive => _isActive;

    public static void Show()
    {
        if (_isActive) return;
        _isActive = true;
        CreateChoiceUI();
    }

    public static void Hide()
    {
        _isActive = false;
        if (_canvasObj != null)
        {
            UnityEngine.Object.Destroy(_canvasObj);
            _canvasObj = null;
        }
        _choicePanel = null;
        _orbGridPanel = null;
    }

    private static void CreateChoiceUI()
    {
        if (_canvasObj != null) UnityEngine.Object.Destroy(_canvasObj);

        _canvasObj = new GameObject("MirrorEventCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_canvasObj);

        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasObj.AddComponent<GraphicRaycaster>();

        // Semi-transparent background
        var bgObj = new GameObject("Background");
        bgObj.transform.SetParent(_canvasObj.transform, false);
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.7f);
        var bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Choice panel
        _choicePanel = new GameObject("ChoicePanel");
        _choicePanel.transform.SetParent(_canvasObj.transform, false);
        var panelImg = _choicePanel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        var panelRect = _choicePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.25f, 0.3f);
        panelRect.anchorMax = new Vector2(0.75f, 0.7f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Title
        CreateText(_choicePanel.transform, "Title", "Mirror Event", 36,
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(0, -15), new Vector2(400, 50));

        // Subtitle
        CreateText(_choicePanel.transform, "Subtitle", "Choose your fate:", 24,
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(0, -60), new Vector2(400, 36));

        // Remove One button
        CreateButton(_choicePanel.transform, "RemoveOneBtn", "Remove an Orb",
            new Color(0.3f, 0.5f, 0.7f, 1f),
            new Vector2(0, -30), new Vector2(360, 70),
            OnRemoveOneClicked);

        // Remove All button
        CreateButton(_choicePanel.transform, "RemoveAllBtn", "Remove All Orbs",
            new Color(0.7f, 0.3f, 0.3f, 1f),
            new Vector2(0, -115), new Vector2(360, 70),
            OnRemoveAllClicked);
    }

    private static void OnRemoveOneClicked()
    {
        MultiplayerPlugin.Logger?.LogInfo("[MirrorEventUI] Remove One clicked — showing orb grid");
        if (_choicePanel != null) _choicePanel.SetActive(false);
        ShowOrbGrid();
    }

    private static void OnRemoveAllClicked()
    {
        MultiplayerPlugin.Logger?.LogInfo("[MirrorEventUI] Remove All clicked — sending to host");

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IMessageSender>(out var sender) == true)
            {
                sender.Send(new MirrorEventCompleteEvent
                {
                    Action = "remove_all",
                });
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[MirrorEventUI] Failed to send remove_all: {ex.Message}");
        }

        Hide();
    }

    private static void ShowOrbGrid()
    {
        if (_orbGridPanel != null) UnityEngine.Object.Destroy(_orbGridPanel);

        _orbGridPanel = new GameObject("OrbGridPanel");
        _orbGridPanel.transform.SetParent(_canvasObj.transform, false);
        var panelImg = _orbGridPanel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        var panelRect = _orbGridPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.1f, 0.1f);
        panelRect.anchorMax = new Vector2(0.9f, 0.9f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Title
        CreateText(_orbGridPanel.transform, "Title", "Choose an orb to remove:", 30,
            new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(0, -15), new Vector2(500, 44));

        // Scroll view with grid of orbs
        var scrollObj = new GameObject("ScrollView");
        scrollObj.transform.SetParent(_orbGridPanel.transform, false);
        var scrollRect = scrollObj.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0.05f, 0.05f);
        scrollRect.anchorMax = new Vector2(0.95f, 0.9f);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;

        var contentObj = new GameObject("Content");
        contentObj.transform.SetParent(scrollObj.transform, false);
        var contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;

        var grid = contentObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(180, 50);
        grid.spacing = new Vector2(10, 8);
        grid.padding = new RectOffset(10, 10, 10, 10);
        grid.childAlignment = TextAnchor.UpperLeft;

        var csf = contentObj.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sv = scrollObj.AddComponent<ScrollRect>();
        sv.content = contentRect;
        sv.horizontal = false;
        sv.vertical = true;

        // Populate with client's orbs from DeckManager.completeDeck
        var deck = DeckManager.completeDeck;
        if (deck != null)
        {
            // Get orb identifiers for GUID matching
            OrbIdentifier orbId = null;
            MultiplayerPlugin.Services?.TryResolve(out orbId);

            for (int i = 0; i < deck.Count; i++)
            {
                var orbGo = deck[i];
                if (orbGo == null) continue;

                // Skip CannotBeRemoved orbs
                if (orbGo.GetComponent<CannotBeRemoved>() != null) continue;

                var attack = orbGo.GetComponent<Attack>();
                var orbName = attack != null ? attack.GetNameWithLevel() : orbGo.name;
                var prefabName = orbGo.name.Replace("(Clone)", "").Trim();
                var guid = orbId?.GetGuid(orbGo) ?? "";

                CreateOrbButton(contentObj.transform, i, orbName, prefabName, guid);
            }
        }
    }

    private static void CreateOrbButton(Transform parent, int index, string displayName, string prefabName, string guid)
    {
        var btnObj = new GameObject($"Orb_{index}");
        btnObj.transform.SetParent(parent, false);

        var btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.2f, 0.25f, 0.35f, 1f);

        var btn = btnObj.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.55f, 1f);
        colors.pressedColor = new Color(0.15f, 0.2f, 0.3f, 1f);
        btn.colors = colors;

        // Capture values for lambda
        var capturedPrefab = prefabName;
        var capturedGuid = guid;
        btn.onClick.AddListener(() => OnOrbSelected(capturedPrefab, capturedGuid));

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = displayName;
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var textRect = tmp.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(5, 2);
        textRect.offsetMax = new Vector2(-5, -2);
    }

    private static void OnOrbSelected(string prefabName, string guid)
    {
        MultiplayerPlugin.Logger?.LogInfo($"[MirrorEventUI] Orb selected for removal: {prefabName} (guid={guid})");

        try
        {
            var services = MultiplayerPlugin.Services;
            if (services?.TryResolve<IMessageSender>(out var sender) == true)
            {
                sender.Send(new MirrorEventCompleteEvent
                {
                    Action = "remove_one",
                    RemovedOrbName = prefabName,
                    RemovedOrbGuid = guid,
                });
            }
        }
        catch (Exception ex)
        {
            MultiplayerPlugin.Logger?.LogError($"[MirrorEventUI] Failed to send remove_one: {ex.Message}");
        }

        Hide();
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var rect = tmp.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label,
        Color bgColor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = bgColor;
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        var btn = obj.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        var textRect = tmp.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);

        return btn;
    }
}
