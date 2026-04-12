using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Client-side read-only overlay that shows the host's TextScenario dialogue state.
/// Displays the NPC text, speaker name, and response options with a highlight on the
/// host's currently hovered response.
/// </summary>
public class TextScenarioSpectatorUI : MonoBehaviour
{
    private static TextScenarioSpectatorUI _instance;
    public static TextScenarioSpectatorUI Instance => _instance;

    private GameObject _canvasObj;
    private GameObject _dialoguePanel;
    private TextMeshProUGUI _speakerText;
    private TextMeshProUGUI _subtitleText;
    private GameObject _responsesContainer;
    private readonly List<GameObject> _responseItems = new List<GameObject>();
    private int _lastHighlightIndex = -1;

    private static readonly Color HighlightColor = new Color(1f, 0.85f, 0.3f, 1f);
    private static readonly Color NormalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color ArrowColor = new Color(1f, 0.85f, 0.3f, 1f);

    private void Awake()
    {
        _instance = this;
        CreateUI();
        Hide();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_canvasObj != null) Destroy(_canvasObj);
    }

    private void CreateUI()
    {
        // Canvas
        _canvasObj = new GameObject("TextScenarioSpectatorCanvas");
        _canvasObj.transform.SetParent(transform, false);
        DontDestroyOnLoad(_canvasObj);

        var canvas = _canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // Below MultiplayerUI (100)

        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Main dialogue panel — bottom-center, semi-transparent dark background
        _dialoguePanel = new GameObject("DialoguePanel");
        _dialoguePanel.transform.SetParent(_canvasObj.transform, false);
        var panelImg = _dialoguePanel.AddComponent<Image>();
        panelImg.color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

        var panelRect = _dialoguePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.1f, 0f);
        panelRect.anchorMax = new Vector2(0.9f, 0.4f);
        panelRect.offsetMin = new Vector2(0, 20);
        panelRect.offsetMax = new Vector2(0, 0);

        // Speaker name — top of panel
        var speakerObj = new GameObject("SpeakerName");
        speakerObj.transform.SetParent(_dialoguePanel.transform, false);
        _speakerText = speakerObj.AddComponent<TextMeshProUGUI>();
        _speakerText.fontSize = 28;
        _speakerText.fontStyle = FontStyles.Bold;
        _speakerText.color = new Color(0.9f, 0.75f, 0.4f, 1f);
        _speakerText.alignment = TextAlignmentOptions.TopLeft;

        var speakerRect = _speakerText.rectTransform;
        speakerRect.anchorMin = new Vector2(0, 1);
        speakerRect.anchorMax = new Vector2(1, 1);
        speakerRect.pivot = new Vector2(0, 1);
        speakerRect.anchoredPosition = new Vector2(20, -10);
        speakerRect.sizeDelta = new Vector2(-40, 36);

        // Subtitle text — main dialogue area
        var subtitleObj = new GameObject("SubtitleText");
        subtitleObj.transform.SetParent(_dialoguePanel.transform, false);
        _subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
        _subtitleText.fontSize = 24;
        _subtitleText.color = Color.white;
        _subtitleText.alignment = TextAlignmentOptions.TopLeft;
        _subtitleText.enableWordWrapping = true;

        var subtitleRect = _subtitleText.rectTransform;
        subtitleRect.anchorMin = new Vector2(0, 0.4f);
        subtitleRect.anchorMax = new Vector2(1, 1);
        subtitleRect.offsetMin = new Vector2(20, 0);
        subtitleRect.offsetMax = new Vector2(-20, -50);

        // Responses container — bottom portion of panel
        _responsesContainer = new GameObject("ResponsesContainer");
        _responsesContainer.transform.SetParent(_dialoguePanel.transform, false);

        var respRect = _responsesContainer.AddComponent<RectTransform>();
        respRect.anchorMin = new Vector2(0, 0);
        respRect.anchorMax = new Vector2(1, 0.4f);
        respRect.offsetMin = new Vector2(20, 10);
        respRect.offsetMax = new Vector2(-20, 0);

        // Vertical layout for response items
        var vlg = _responsesContainer.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 4;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
    }

    public void Show()
    {
        if (_canvasObj != null) _canvasObj.SetActive(true);
    }

    public void Hide()
    {
        if (_canvasObj != null) _canvasObj.SetActive(false);
    }

    public void UpdateDialogue(string speakerName, string subtitleText, List<string> responses, int highlightedIndex)
    {
        if (_canvasObj == null) return;

        _speakerText.text = speakerName ?? "";
        _subtitleText.text = subtitleText ?? "";

        UpdateResponses(responses, highlightedIndex);
        Show();
    }

    private void UpdateResponses(List<string> responses, int highlightedIndex)
    {
        if (responses == null) responses = new List<string>();

        // Rebuild response items if count changed
        while (_responseItems.Count < responses.Count)
        {
            var item = CreateResponseItem(_responsesContainer.transform, _responseItems.Count);
            _responseItems.Add(item);
        }
        while (_responseItems.Count > responses.Count)
        {
            var last = _responseItems[_responseItems.Count - 1];
            _responseItems.RemoveAt(_responseItems.Count - 1);
            Destroy(last);
        }

        // Update text and highlight
        for (int i = 0; i < responses.Count; i++)
        {
            var item = _responseItems[i];
            item.SetActive(true);

            var arrowText = item.transform.Find("Arrow")?.GetComponent<TextMeshProUGUI>();
            var responseText = item.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();

            if (responseText != null)
                responseText.text = responses[i];

            bool isHighlighted = i == highlightedIndex;
            if (arrowText != null)
            {
                arrowText.text = isHighlighted ? ">" : " ";
                arrowText.color = ArrowColor;
            }
            if (responseText != null)
                responseText.color = isHighlighted ? HighlightColor : NormalColor;
        }

        _lastHighlightIndex = highlightedIndex;
    }

    private GameObject CreateResponseItem(Transform parent, int index)
    {
        var item = new GameObject($"Response_{index}");
        item.transform.SetParent(parent, false);

        var le = item.AddComponent<LayoutElement>();
        le.preferredHeight = 32;

        var hlg = item.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = 8;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Arrow indicator
        var arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(item.transform, false);
        var arrowTmp = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowTmp.fontSize = 24;
        arrowTmp.fontStyle = FontStyles.Bold;
        arrowTmp.color = ArrowColor;
        arrowTmp.alignment = TextAlignmentOptions.MidlineLeft;
        arrowTmp.text = " ";
        var arrowLe = arrowObj.AddComponent<LayoutElement>();
        arrowLe.preferredWidth = 24;

        // Response text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(item.transform, false);
        var textTmp = textObj.AddComponent<TextMeshProUGUI>();
        textTmp.fontSize = 22;
        textTmp.color = NormalColor;
        textTmp.alignment = TextAlignmentOptions.MidlineLeft;
        textTmp.enableWordWrapping = true;
        var textLe = textObj.AddComponent<LayoutElement>();
        textLe.flexibleWidth = 1;

        return item;
    }
}
