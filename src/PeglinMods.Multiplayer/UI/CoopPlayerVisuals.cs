using System;
using System.Collections.Generic;
using BepInEx.Logging;
using PeglinMods.Multiplayer.DI;
using PeglinMods.Multiplayer.Events.Handlers.Lobby;
using PeglinMods.Multiplayer.GameState;
using PeglinMods.Multiplayer.GameState.Snapshots;
using PeglinMods.Multiplayer.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Manages player sprites and HUD in battle scenes for co-op multiplayer.
/// For each co-op player, clones the player sprite with an offset and shows
/// separate name, HP, and turn-arrow labels as screen-space overlays.
/// </summary>
public class CoopPlayerVisuals : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>Latest player summaries from the host heartbeat.</summary>
    public static List<CoopPlayerSummary> LatestPlayerSummaries { get; set; }

    /// <summary>Active player slot index from the host heartbeat.</summary>
    public static int LatestActiveSlot { get; set; } = -1;

    // Per-player visual data — name, HP, arrow, and status icons are separate panels
    private class PlayerVisual
    {
        public int SlotIndex;
        public GameObject SpriteClone; // null for slot 0 (host uses real player)
        public GameObject NamePanel;
        public TextMeshProUGUI NameText;
        public GameObject HpPanel;
        public TextMeshProUGUI HpText;
        public GameObject ArrowPanel;
        public TextMeshProUGUI ArrowText;
        public GameObject StatusIconContainer; // horizontal row of native-style icons
        public Dictionary<int, StatusIconEntry> StatusIcons = new Dictionary<int, StatusIconEntry>();
    }

    private class StatusIconEntry
    {
        public GameObject Root;
        public Image IconImage;
        public TextMeshProUGUI IntensityText;
    }

    // Cache the game's StatusEffectData ScriptableObject for icon sprites
    private static Battle.StatusEffects.StatusEffectData _statusEffectData;
    private static bool _statusEffectDataSearched;

    private readonly List<PlayerVisual> _visuals = new List<PlayerVisual>();
    private bool _inBattle;
    private string _lastScene = "";
    private GameObject _playerRef;
    private PlayerVisual _hostLabel;

    // Shared screen-space overlay canvas
    private static GameObject _overlayCanvasObj;
    private static Canvas _overlayCanvas;
    private static RectTransform _overlayCanvasRect;

    // Cache the game's TMP font once
    private static TMP_FontAsset _gameFont;
    private static bool _gameFontSearched;

    private void Update()
    {
        try
        {
            var scene = SceneManager.GetActiveScene().name;

            if (scene != _lastScene)
            {
                _lastScene = scene;
                CleanupVisuals();
                _playerRef = null;
                _inBattle = false;
            }

            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode)) return;
            if (!mode.IsHosting && !mode.IsSpectating) return;

            if (scene != "Battle")
            {
                if (_inBattle) { CleanupVisuals(); _inBattle = false; }
                return;
            }

            _inBattle = true;

            if (mode.IsHosting)
                BuildHostSummaries(services);

            var summaries = LatestPlayerSummaries;
            if (summaries == null || summaries.Count <= 1) return;

            if (_playerRef == null)
            {
                _playerRef = GameObject.FindGameObjectWithTag("Player");
                if (_playerRef == null) return;
            }

            EnsureVisuals(summaries);
            UpdateVisuals(summaries);
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] Update error: {ex.Message}");
        }
    }

    private void BuildHostSummaries(IServiceContainer services)
    {
        try
        {
            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            if (coopState.TotalPlayerCount < 2) return;

            // Read live status effects from the singleton for the active player
            List<StatusEffectEntry> liveEffects = null;
            try
            {
                var statusCtrl = FindObjectOfType<Battle.StatusEffects.PlayerStatusEffectController>();
                if (statusCtrl != null)
                {
                    var effectsList = HarmonyLib.AccessTools.Field(
                        typeof(Battle.StatusEffects.PlayerStatusEffectController), "_statusEffects")
                        ?.GetValue(statusCtrl) as System.Collections.IList;
                    if (effectsList != null && effectsList.Count > 0)
                    {
                        liveEffects = new List<StatusEffectEntry>();
                        foreach (var effect in effectsList)
                        {
                            var typeField = HarmonyLib.AccessTools.Field(effect.GetType(), "EffectType");
                            var intensityField = HarmonyLib.AccessTools.Field(effect.GetType(), "Intensity");
                            if (typeField == null) continue;
                            var effectType = typeField.GetValue(effect);
                            var intensity = (int)(intensityField?.GetValue(effect) ?? 0);
                            if (intensity <= 0) continue;
                            liveEffects.Add(new StatusEffectEntry
                            {
                                EffectType = (int)effectType,
                                EffectName = effectType.ToString(),
                                Intensity = intensity,
                            });
                        }
                    }
                }
            }
            catch { }

            var summaries = new List<CoopPlayerSummary>();
            foreach (var kvp in coopState.PlayerStates)
            {
                var ps = kvp.Value;
                var summary = new CoopPlayerSummary
                {
                    SlotIndex = ps.SlotIndex,
                    PlayerName = ps.PlayerName,
                    ChosenClass = ps.ChosenClass,
                    CurrentHealth = ps.CurrentHealth,
                    MaxHealth = ps.MaxHealth,
                    Gold = ps.Gold,
                    HasShotThisRound = ps.HasShotThisRound,
                };

                // Active player: read live from singleton (includes buffs gained this turn)
                // Inactive players: read from saved CoopPlayerState
                bool isActive = ps.SlotIndex == coopState.ActivePlayerSlot;
                if (isActive && liveEffects != null)
                {
                    summary.StatusEffects = liveEffects;
                }
                else if (ps.StatusEffects != null)
                {
                    foreach (var e in ps.StatusEffects)
                    {
                        summary.StatusEffects.Add(new StatusEffectEntry
                        {
                            EffectType = e.EffectType,
                            EffectName = ((Battle.StatusEffects.StatusEffectType)e.EffectType).ToString(),
                            Intensity = e.Intensity,
                        });
                    }
                }

                summaries.Add(summary);
            }

            LatestPlayerSummaries = summaries;
            LatestActiveSlot = coopState.ActivePlayerSlot;
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] BuildHostSummaries failed: {ex.Message}");
        }
    }

    private void OnDestroy() => CleanupVisuals();

    private void EnsureVisuals(List<CoopPlayerSummary> summaries)
    {
        // Remove stale visuals
        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            var v = _visuals[i];
            bool found = false;
            foreach (var s in summaries)
                if (s.SlotIndex == v.SlotIndex) { found = true; break; }
            if (!found || v.SpriteClone == null)
            {
                DestroyVisual(v);
                _visuals.RemoveAt(i);
            }
        }

        // Host label (slot 0)
        if (_hostLabel == null && _playerRef != null)
        {
            CoopPlayerSummary hostSummary = null;
            foreach (var s in summaries)
                if (s.SlotIndex == 0) { hostSummary = s; break; }
            if (hostSummary != null)
                _hostLabel = CreatePlayerLabels(hostSummary, spriteClone: null);
        }

        // Non-host clones + labels
        foreach (var summary in summaries)
        {
            if (summary.SlotIndex == 0) continue;
            bool exists = false;
            foreach (var v in _visuals)
                if (v.SlotIndex == summary.SlotIndex) { exists = true; break; }
            if (exists) continue;

            var visual = CreatePlayerVisual(summary);
            if (visual != null) _visuals.Add(visual);
        }
    }

    private void EnsureOverlayCanvas()
    {
        if (_overlayCanvasObj != null) return;
        _overlayCanvasObj = new GameObject("CoopPlayerLabelsOverlay");
        DontDestroyOnLoad(_overlayCanvasObj);
        _overlayCanvas = _overlayCanvasObj.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 9000;
        var scaler = _overlayCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _overlayCanvasRect = _overlayCanvasObj.GetComponent<RectTransform>();
    }

    private static TMP_FontAsset GetGameFont()
    {
        if (_gameFontSearched) return _gameFont;
        _gameFontSearched = true;
        try
        {
            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
                if (tmp.font != null) { _gameFont = tmp.font; break; }
        }
        catch { }
        return _gameFont;
    }

    /// <summary>Format a name: max 14 chars, 7 per line, max 2 lines.</summary>
    private static string FormatName(string name, int slot)
    {
        if (string.IsNullOrEmpty(name)) name = $"P{slot}";
        if (name.Length > 14) name = name.Substring(0, 14);
        if (name.Length > 7)
            name = name.Substring(0, 7) + "\n" + name.Substring(7);
        return name;
    }

    /// <summary>Create a single text panel (background + text).</summary>
    private GameObject CreateTextPanel(string goName, float width, float height,
        string text, float fontSize, Color textColor, Color bgColor,
        out TextMeshProUGUI tmpText)
    {
        EnsureOverlayCanvas();

        var panel = new GameObject(goName);
        panel.transform.SetParent(_overlayCanvasObj.transform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(width, height);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        // Background
        var bgImg = panel.AddComponent<Image>();
        bgImg.color = bgColor;

        // Text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(panel.transform, false);
        tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.fontStyle = FontStyles.Bold;
        var font = GetGameFont();
        if (font != null) tmpText.font = font;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = textColor;
        tmpText.outlineWidth = 0.3f;
        tmpText.outlineColor = Color.black;
        tmpText.enableWordWrapping = false;
        tmpText.overflowMode = TextOverflowModes.Overflow;
        tmpText.lineSpacing = -25f; // tighter line spacing for 2-line names

        var textRect = tmpText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return panel;
    }

    /// <summary>Create name, HP, and arrow panels for a player.</summary>
    private PlayerVisual CreatePlayerLabels(CoopPlayerSummary summary, GameObject spriteClone)
    {
        try
        {
            var nameColor = new Color(0.9f, 0.5f, 0.1f); // dark orange
            var hpColor = new Color(0.6f, 1f, 0.6f);      // green
            var arrowColor = new Color(1f, 0.9f, 0.3f);    // yellow
            var bgColor = new Color(0, 0, 0, 0.3f);

            string formattedName = FormatName(summary.PlayerName, summary.SlotIndex);
            bool twoLines = formattedName.Contains("\n");

            var namePanel = CreateTextPanel(
                $"CoopName_Slot{summary.SlotIndex}",
                187, twoLines ? 80 : 50,
                formattedName, 45, nameColor, bgColor,
                out var nameText);

            var hpPanel = CreateTextPanel(
                $"CoopHP_Slot{summary.SlotIndex}",
                160, 45,
                $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}",
                38, hpColor, bgColor,
                out var hpText);

            var arrowPanel = CreateTextPanel(
                $"CoopArrow_Slot{summary.SlotIndex}",
                80, 100,
                "\u25C0", 64, arrowColor, new Color(0, 0, 0, 0),
                out var arrowText);
            arrowText.outlineWidth = 0.4f;
            arrowPanel.SetActive(false); // only visible for active player

            // Status icon container — horizontal row positioned above the name
            EnsureOverlayCanvas();
            var iconContainer = new GameObject($"CoopStatusIcons_Slot{summary.SlotIndex}");
            iconContainer.transform.SetParent(_overlayCanvasObj.transform, false);
            var containerRect = iconContainer.AddComponent<RectTransform>();
            containerRect.sizeDelta = new Vector2(300, 42);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            var layout = iconContainer.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 2;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            iconContainer.SetActive(false);

            return new PlayerVisual
            {
                SlotIndex = summary.SlotIndex,
                SpriteClone = spriteClone,
                NamePanel = namePanel,
                NameText = nameText,
                HpPanel = hpPanel,
                HpText = hpText,
                ArrowPanel = arrowPanel,
                ArrowText = arrowText,
                StatusIconContainer = iconContainer,
            };
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] CreatePlayerLabels failed for slot {summary.SlotIndex}: {ex.Message}");
            return null;
        }
    }

    private PlayerVisual CreatePlayerVisual(CoopPlayerSummary summary)
    {
        if (_playerRef == null) return null;

        try
        {
            var clone = new GameObject($"CoopPlayer_Slot{summary.SlotIndex}");
            clone.transform.position = _playerRef.transform.position;

            var originalRenderer = _playerRef.GetComponentInChildren<SpriteRenderer>();
            if (originalRenderer != null)
            {
                var sr = clone.AddComponent<SpriteRenderer>();
                sr.sprite = originalRenderer.sprite;
                sr.material = originalRenderer.material;
                sr.sortingLayerID = originalRenderer.sortingLayerID;
                sr.sortingOrder = originalRenderer.sortingOrder - 1;
                sr.color = GetSlotColor(summary.SlotIndex);
            }

            return CreatePlayerLabels(summary, clone);
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] Failed to create visual for slot {summary.SlotIndex}: {ex.Message}");
            return null;
        }
    }

    private void UpdateVisuals(List<CoopPlayerSummary> summaries)
    {
        if (_playerRef == null) return;

        var basePos = _playerRef.transform.position;
        int activeSlot = LatestActiveSlot;
        var cam = Camera.main;

        // Host scale highlight
        try
        {
            float hostScale = (activeSlot == 0) ? 1.15f : 1f;
            _playerRef.transform.localScale = Vector3.Lerp(
                _playerRef.transform.localScale, Vector3.one * hostScale, Time.deltaTime * 5f);
        }
        catch { }

        // Update host label (slot 0)
        if (_hostLabel != null)
        {
            CoopPlayerSummary hostSummary = null;
            foreach (var s in summaries)
                if (s.SlotIndex == 0) { hostSummary = s; break; }

            if (hostSummary != null)
                UpdatePlayerLabel(_hostLabel, hostSummary, basePos, activeSlot, cam);
        }

        // Update clone labels
        foreach (var visual in _visuals)
        {
            if (visual.SpriteClone == null) continue;

            CoopPlayerSummary summary = null;
            foreach (var s in summaries)
                if (s.SlotIndex == visual.SlotIndex) { summary = s; break; }
            if (summary == null) continue;

            // Position clone: offset left per slot
            var offset = new Vector3(-2.5f * visual.SlotIndex, -0.2f * visual.SlotIndex, 0);
            visual.SpriteClone.transform.position = basePos + offset;

            var clonePos = visual.SpriteClone.transform.position;
            UpdatePlayerLabel(visual, summary, clonePos, activeSlot, cam);

            // Scale highlight
            float targetScale = (activeSlot == visual.SlotIndex) ? 1.15f : 1f;
            visual.SpriteClone.transform.localScale = Vector3.Lerp(
                visual.SpriteClone.transform.localScale,
                Vector3.one * targetScale, Time.deltaTime * 5f);

            // Tint
            var sr = visual.SpriteClone.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var baseColor = GetSlotColor(visual.SlotIndex);
                sr.color = (activeSlot == visual.SlotIndex)
                    ? Color.Lerp(sr.color, Color.white, Time.deltaTime * 3f)
                    : Color.Lerp(sr.color, baseColor, Time.deltaTime * 3f);
            }
        }
    }

    // Colors for name pulse animation
    private static readonly Color _nameColorBase = new Color(0.9f, 0.5f, 0.1f); // dark orange
    private static readonly Color _nameColorActive = new Color(1f, 0.95f, 0.4f); // bright yellow
    private static readonly Color _arrowColorDim = new Color(1f, 0.9f, 0.3f, 0.5f);
    private static readonly Color _arrowColorBright = new Color(1f, 1f, 0.6f, 1f);

    private void UpdatePlayerLabel(PlayerVisual visual, CoopPlayerSummary summary,
        Vector3 charPos, int activeSlot, Camera cam)
    {
        // Update texts
        if (visual.HpText != null)
            visual.HpText.text = $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}";
        if (visual.NameText != null)
            visual.NameText.text = FormatName(summary.PlayerName, summary.SlotIndex);

        // Position name above character, HP below, arrow to the right-middle
        PositionPanelAtWorld(visual.NamePanel, charPos + new Vector3(0, 2.0f, 0), cam);
        PositionPanelAtWorld(visual.HpPanel, charPos + new Vector3(0, -0.6f, 0), cam);

        bool isActive = (activeSlot == visual.SlotIndex);

        // Pulsate name color when this player is active
        if (visual.NameText != null)
        {
            if (isActive)
            {
                float t = (Mathf.Sin(Time.unscaledTime * 3f) + 1f) * 0.5f; // 0..1 at ~3Hz
                visual.NameText.color = Color.Lerp(_nameColorBase, _nameColorActive, t);
            }
            else
            {
                visual.NameText.color = _nameColorBase;
            }
        }

        // Arrow indicator — pulse scale and color
        if (visual.ArrowPanel != null)
        {
            visual.ArrowPanel.SetActive(isActive);
            if (isActive)
            {
                PositionPanelAtWorld(visual.ArrowPanel, charPos + new Vector3(1.0f, 0.5f, 0), cam);

                float pulse = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f; // 0..1 at ~4Hz
                float scale = Mathf.Lerp(0.85f, 1.15f, pulse);
                visual.ArrowPanel.transform.localScale = new Vector3(scale, scale, 1f);

                if (visual.ArrowText != null)
                    visual.ArrowText.color = Color.Lerp(_arrowColorDim, _arrowColorBright, pulse);
            }
        }

        // Status effects — native-style icons above the player's head
        UpdateStatusIcons(visual, summary.StatusEffects, charPos, cam);
    }

    private void UpdateStatusIcons(PlayerVisual visual, List<StatusEffectEntry> effects,
        Vector3 charPos, Camera cam)
    {
        if (visual.StatusIconContainer == null) return;

        bool hasEffects = effects != null && effects.Count > 0;

        // Build set of current effect types for quick lookup
        var activeTypes = new HashSet<int>();
        if (hasEffects)
        {
            foreach (var e in effects)
                if (e.Intensity > 0) activeTypes.Add(e.EffectType);
        }

        // Remove icons for effects that are no longer active
        var toRemove = new List<int>();
        foreach (var kvp in visual.StatusIcons)
        {
            if (!activeTypes.Contains(kvp.Key))
            {
                try { if (kvp.Value.Root != null) Destroy(kvp.Value.Root); } catch { }
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var key in toRemove)
            visual.StatusIcons.Remove(key);

        if (!hasEffects)
        {
            visual.StatusIconContainer.SetActive(false);
            return;
        }

        // Add or update icons for each active effect
        foreach (var e in effects)
        {
            if (e.Intensity <= 0) continue;

            if (visual.StatusIcons.TryGetValue(e.EffectType, out var existing))
            {
                // Update intensity text
                if (existing.IntensityText != null)
                    existing.IntensityText.text = e.Intensity > 999
                        ? (e.Intensity / 1000) + "K"
                        : e.Intensity.ToString();
            }
            else
            {
                // Create new icon
                var entry = CreateStatusIcon(e.EffectType, e.Intensity,
                    visual.StatusIconContainer.transform);
                if (entry != null)
                    visual.StatusIcons[e.EffectType] = entry;
            }
        }

        visual.StatusIconContainer.SetActive(true);
        PositionPanelAtWorld(visual.StatusIconContainer, charPos + new Vector3(0, 2.6f, 0), cam);
    }

    private StatusIconEntry CreateStatusIcon(int effectType, int intensity, Transform parent)
    {
        var icon = new GameObject($"StatusIcon_{effectType}");
        icon.transform.SetParent(parent, false);

        var rect = icon.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(38, 38);

        // Add LayoutElement so HorizontalLayoutGroup respects preferred size
        var le = icon.AddComponent<LayoutElement>();
        le.preferredWidth = 38;
        le.preferredHeight = 38;

        // Icon image (status effect sprite)
        var img = icon.AddComponent<Image>();
        var sprite = GetStatusEffectSprite(effectType);
        if (sprite != null)
        {
            img.sprite = sprite;
            img.preserveAspect = true;
        }
        else
        {
            // Fallback: tinted square with effect abbreviation
            img.color = new Color(0.4f, 0.3f, 0.6f, 0.8f);
        }

        // Intensity number overlaid at bottom-center
        var textObj = new GameObject("Intensity");
        textObj.transform.SetParent(icon.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = intensity > 999 ? (intensity / 1000) + "K" : intensity.ToString();
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        var font = GetGameFont();
        if (font != null) tmp.font = font;
        tmp.alignment = TextAlignmentOptions.Bottom;
        tmp.color = Color.white;
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = Color.black;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        var textRect = tmp.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(0, -2);
        textRect.offsetMax = new Vector2(0, 0);

        return new StatusIconEntry
        {
            Root = icon,
            IconImage = img,
            IntensityText = tmp,
        };
    }

    private static Battle.StatusEffects.StatusEffectData GetStatusEffectData()
    {
        if (_statusEffectDataSearched) return _statusEffectData;
        _statusEffectDataSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Battle.StatusEffects.StatusEffectData>();
            if (all.Length > 0) _statusEffectData = all[0];
        }
        catch { }
        return _statusEffectData;
    }

    private static Sprite GetStatusEffectSprite(int effectType)
    {
        var data = GetStatusEffectData();
        if (data == null) return null;
        return data.GetStatusEffectIcon((Battle.StatusEffects.StatusEffectType)effectType);
    }

    /// <summary>
    /// Position a screen-space overlay panel at a world point.
    /// Uses RectTransformUtility to convert properly with CanvasScaler,
    /// ensuring consistent sizing across different screen resolutions.
    /// </summary>
    private void PositionPanelAtWorld(GameObject panel, Vector3 worldPos, Camera cam)
    {
        if (panel == null || cam == null) return;
        var screenPos = cam.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0) { panel.SetActive(false); return; }
        if (!panel.activeSelf) panel.SetActive(true);

        var rect = panel.GetComponent<RectTransform>();
        if (rect == null) return;

        // Convert screen pixels to canvas local coords (accounts for CanvasScaler)
        if (_overlayCanvasRect != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayCanvasRect, screenPos, null, out var localPoint);
            rect.localPosition = localPoint;
        }
        else
        {
            rect.position = screenPos;
        }
    }

    private void CleanupVisuals()
    {
        foreach (var v in _visuals)
            DestroyVisual(v);
        _visuals.Clear();

        if (_hostLabel != null)
        {
            DestroyPanels(_hostLabel);
            _hostLabel = null;
        }
    }

    private void DestroyVisual(PlayerVisual v)
    {
        try { if (v.SpriteClone != null) Destroy(v.SpriteClone); } catch { }
        DestroyPanels(v);
    }

    private void DestroyPanels(PlayerVisual v)
    {
        try { if (v.NamePanel != null) Destroy(v.NamePanel); } catch { }
        try { if (v.HpPanel != null) Destroy(v.HpPanel); } catch { }
        try { if (v.ArrowPanel != null) Destroy(v.ArrowPanel); } catch { }
        try { if (v.StatusIconContainer != null) Destroy(v.StatusIconContainer); } catch { }
        v.StatusIcons?.Clear();
    }

    private static Color GetSlotColor(int slot)
    {
        switch (slot)
        {
            case 1: return new Color(0.7f, 0.85f, 1f);
            case 2: return new Color(1f, 0.8f, 0.7f);
            case 3: return new Color(0.8f, 1f, 0.7f);
            default: return new Color(0.9f, 0.9f, 0.9f);
        }
    }

    public static void Reset()
    {
        LatestPlayerSummaries = null;
        LatestActiveSlot = -1;
        _statusEffectDataSearched = false;
        _statusEffectData = null;
    }
}
