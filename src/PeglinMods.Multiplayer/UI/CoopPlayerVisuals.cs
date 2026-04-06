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

namespace PeglinMods.Multiplayer.UI;

/// <summary>
/// Manages player sprites and HUD in battle scenes for co-op multiplayer.
/// For each co-op player, clones the player sprite with an offset and shows
/// a world-space name/HP label. Highlights the active player.
/// </summary>
public class CoopPlayerVisuals : MonoBehaviour
{
    private static ManualLogSource Log => MultiplayerPlugin.Logger;

    /// <summary>Latest player summaries from the host heartbeat.</summary>
    public static List<CoopPlayerSummary> LatestPlayerSummaries { get; set; }

    /// <summary>Active player slot index from the host heartbeat.</summary>
    public static int LatestActiveSlot { get; set; } = -1;

    // Per-player visual data
    private class PlayerVisual
    {
        public int SlotIndex;
        public GameObject SpriteClone; // null for slot 0 (host uses real player)
        public GameObject LabelCanvas;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI HpText;
    }

    private readonly List<PlayerVisual> _visuals = new List<PlayerVisual>();
    private bool _inBattle;
    private string _lastScene = "";
    private GameObject _playerRef; // The original player GameObject
    private PlayerVisual _hostLabel; // Label attached to the real player (slot 0)

    // Shared screen-space overlay canvas for all player labels.
    // This guarantees labels render on top of ALL game sprites.
    private static GameObject _overlayCanvasObj;
    private static Canvas _overlayCanvas;
    private static RectTransform _overlayCanvasRect;

    private void Update()
    {
        try
        {
            var scene = SceneManager.GetActiveScene().name;

            // Detect scene change
            if (scene != _lastScene)
            {
                _lastScene = scene;
                CleanupVisuals();
                _playerRef = null;
                _inBattle = false;
            }

            // Only active in multiplayer
            var services = MultiplayerPlugin.Services;
            if (services == null) return;
            if (!services.TryResolve<IMultiplayerMode>(out var mode)) return;
            if (!mode.IsHosting && !mode.IsSpectating) return;

            // Check for battle scene
            if (scene != "Battle")
            {
                if (_inBattle)
                {
                    CleanupVisuals();
                    _inBattle = false;
                }
                return;
            }

            _inBattle = true;

            // On the HOST, LatestPlayerSummaries is never set by the heartbeat
            // (the host sends heartbeats, it doesn't receive them). Build summaries
            // directly from CoopStateManager so the host sees all player visuals.
            if (mode.IsHosting)
            {
                BuildHostSummaries(services);
            }

            var summaries = LatestPlayerSummaries;
            if (summaries == null || summaries.Count <= 1) return; // No co-op data or solo

            // Find the player GameObject if we haven't yet
            if (_playerRef == null)
            {
                _playerRef = GameObject.FindGameObjectWithTag("Player");
                if (_playerRef == null) return;
            }

            // Ensure visuals exist for each player
            EnsureVisuals(summaries);

            // Update positions, HP text, and active highlight
            UpdateVisuals(summaries);
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] Update error: {ex.Message}");
        }
    }

    /// <summary>
    /// On the host, build player summaries from CoopStateManager so the host
    /// can see all players (including non-host clones). The host never receives
    /// heartbeats, so LatestPlayerSummaries would otherwise be null.
    /// </summary>
    private void BuildHostSummaries(IServiceContainer services)
    {
        try
        {
            if (!services.TryResolve<CoopStateManager>(out var coopState)) return;
            if (coopState.TotalPlayerCount < 2) return;

            var summaries = new List<CoopPlayerSummary>();
            foreach (var kvp in coopState.PlayerStates)
            {
                var ps = kvp.Value;
                summaries.Add(new CoopPlayerSummary
                {
                    SlotIndex = ps.SlotIndex,
                    PlayerName = ps.PlayerName,
                    ChosenClass = ps.ChosenClass,
                    CurrentHealth = ps.CurrentHealth,
                    MaxHealth = ps.MaxHealth,
                    Gold = ps.Gold,
                    HasShotThisRound = ps.HasShotThisRound,
                });
            }

            LatestPlayerSummaries = summaries;
            LatestActiveSlot = coopState.ActivePlayerSlot;
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] BuildHostSummaries failed: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        CleanupVisuals();
    }

    private void EnsureVisuals(List<CoopPlayerSummary> summaries)
    {
        // Remove stale visuals for slots that no longer exist
        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            var v = _visuals[i];
            bool found = false;
            foreach (var s in summaries)
            {
                if (s.SlotIndex == v.SlotIndex) { found = true; break; }
            }
            if (!found || v.SpriteClone == null)
            {
                DestroyVisual(v);
                _visuals.RemoveAt(i);
            }
        }

        // Create visuals for new players (skip slot 0 -- that's the main player sprite)
        // Create label for host player (slot 0) attached to the real player GO
        if (_hostLabel == null && _playerRef != null)
        {
            CoopPlayerSummary hostSummary = null;
            foreach (var s in summaries)
                if (s.SlotIndex == 0) { hostSummary = s; break; }

            if (hostSummary != null)
                _hostLabel = CreateLabel(_playerRef, hostSummary);
        }

        // Create clones + labels for non-host players
        foreach (var summary in summaries)
        {
            if (summary.SlotIndex == 0) continue; // Host uses the real player sprite

            bool exists = false;
            foreach (var v in _visuals)
            {
                if (v.SlotIndex == summary.SlotIndex) { exists = true; break; }
            }
            if (exists) continue;

            var visual = CreatePlayerVisual(summary);
            if (visual != null)
                _visuals.Add(visual);
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
        var scaler = _overlayCanvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _overlayCanvasRect = _overlayCanvasObj.GetComponent<RectTransform>();
    }

    /// <summary>Create a screen-space label panel for a player.</summary>
    private GameObject CreateScreenLabel(CoopPlayerSummary summary, out TextMeshProUGUI nameText, out TextMeshProUGUI hpText)
    {
        EnsureOverlayCanvas();

        var panel = new GameObject($"CoopLabel_Slot{summary.SlotIndex}");
        panel.transform.SetParent(_overlayCanvasObj.transform, false);

        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(160, 50);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        nameText = null;
        hpText = null;

        // Find the game's TMP font
        TMP_FontAsset gameFont = null;
        try
        {
            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
                if (tmp.font != null) { gameFont = tmp.font; break; }
        }
        catch { }

        // --- Name label (above character) ---
        var nameBg = new GameObject("NameBg");
        nameBg.transform.SetParent(panel.transform, false);
        var nameBgImg = nameBg.AddComponent<UnityEngine.UI.Image>();
        nameBgImg.color = new Color(0, 0, 0, 0.3f);
        var nameBgRect = nameBg.GetComponent<RectTransform>();
        nameBgRect.anchorMin = new Vector2(0, 0.5f);
        nameBgRect.anchorMax = new Vector2(1, 1);
        nameBgRect.offsetMin = Vector2.zero;
        nameBgRect.offsetMax = Vector2.zero;

        var nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(nameBg.transform, false);
        nameText = nameObj.AddComponent<TextMeshProUGUI>();
        string truncName = summary.PlayerName ?? $"P{summary.SlotIndex}";
        if (truncName.Length > 9) truncName = truncName.Substring(0, 9);
        nameText.text = truncName;
        nameText.fontSize = 28;
        nameText.fontStyle = FontStyles.Bold;
        if (gameFont != null) nameText.font = gameFont;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = new Color(0.9f, 0.5f, 0.1f);
        nameText.outlineWidth = 0.3f;
        nameText.outlineColor = Color.black;
        var nameRect = nameText.rectTransform;
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        // --- HP label (below character) ---
        var hpBg = new GameObject("HpBg");
        hpBg.transform.SetParent(panel.transform, false);
        var hpBgImg = hpBg.AddComponent<UnityEngine.UI.Image>();
        hpBgImg.color = new Color(0, 0, 0, 0.3f);
        var hpBgRect = hpBg.GetComponent<RectTransform>();
        hpBgRect.anchorMin = new Vector2(0, 0);
        hpBgRect.anchorMax = new Vector2(1, 0.5f);
        hpBgRect.offsetMin = Vector2.zero;
        hpBgRect.offsetMax = Vector2.zero;

        var hpObj = new GameObject("HpText");
        hpObj.transform.SetParent(hpBg.transform, false);
        hpText = hpObj.AddComponent<TextMeshProUGUI>();
        hpText.text = $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}";
        hpText.fontSize = 24;
        hpText.fontStyle = FontStyles.Bold;
        if (gameFont != null) hpText.font = gameFont;
        hpText.alignment = TextAlignmentOptions.Center;
        hpText.color = new Color(0.6f, 1f, 0.6f);
        hpText.outlineWidth = 0.2f;
        hpText.outlineColor = Color.black;
        var hpRect = hpText.rectTransform;
        hpRect.anchorMin = Vector2.zero;
        hpRect.anchorMax = Vector2.one;
        hpRect.offsetMin = Vector2.zero;
        hpRect.offsetMax = Vector2.zero;

        return panel;
    }

    /// <summary>Create just a name/HP label for the host player (slot 0).</summary>
    private PlayerVisual CreateLabel(GameObject parent, CoopPlayerSummary summary)
    {
        try
        {
            var panel = CreateScreenLabel(summary, out var nameText, out var hpText);

            return new PlayerVisual
            {
                SlotIndex = summary.SlotIndex,
                SpriteClone = null,
                LabelCanvas = panel,
                NameText = nameText,
                HpText = hpText,
            };
        }
        catch (Exception ex)
        {
            Log?.LogError($"[CoopPlayerVisuals] CreateLabel failed for slot {summary.SlotIndex}: {ex.Message}");
            return null;
        }
    }

    private PlayerVisual CreatePlayerVisual(CoopPlayerSummary summary)
    {
        if (_playerRef == null) return null;

        try
        {
            // Clone the player sprite
            var clone = new GameObject($"CoopPlayer_Slot{summary.SlotIndex}");
            clone.transform.position = _playerRef.transform.position;

            // Copy the sprite renderer from the original player
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

            // Create screen-space label (separate from clone so it renders on top)
            var panel = CreateScreenLabel(summary, out var nameText, out var hpText);

            return new PlayerVisual
            {
                SlotIndex = summary.SlotIndex,
                SpriteClone = clone,
                LabelCanvas = panel,
                NameText = nameText,
                HpText = hpText,
            };
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

        // Update the real player's scale to indicate active state
        try
        {
            float hostScale = (activeSlot == 0) ? 1.15f : 1f;
            _playerRef.transform.localScale = Vector3.Lerp(
                _playerRef.transform.localScale,
                Vector3.one * hostScale,
                Time.deltaTime * 5f);
        }
        catch { }

        var cam = Camera.main;

        // Update host label (slot 0)
        if (_hostLabel != null)
        {
            CoopPlayerSummary hostSummary = null;
            foreach (var s in summaries)
                if (s.SlotIndex == 0) { hostSummary = s; break; }

            if (hostSummary != null)
            {
                if (_hostLabel.HpText != null)
                    _hostLabel.HpText.text = $"{hostSummary.CurrentHealth:F0}/{hostSummary.MaxHealth:F0}";
                if (_hostLabel.NameText != null)
                {
                    string hostName = hostSummary.PlayerName ?? "Host";
                    if (hostName.Length > 9) hostName = hostName.Substring(0, 9);
                    string turnMarker = (activeSlot == 0) ? " <" : "";
                    _hostLabel.NameText.text = $"{hostName}{turnMarker}";
                }
                // Position screen-space label below the player sprite
                PositionLabelAtWorldPoint(_hostLabel, basePos + new Vector3(0, 0.5f, 0), cam);
            }
        }

        foreach (var visual in _visuals)
        {
            if (visual.SpriteClone == null) continue;

            // Find the summary for this slot
            CoopPlayerSummary summary = null;
            foreach (var s in summaries)
            {
                if (s.SlotIndex == visual.SlotIndex) { summary = s; break; }
            }
            if (summary == null) continue;

            // Position: offset further left and spaced out more per slot
            var offset = new Vector3(-2.5f * visual.SlotIndex, -0.2f * visual.SlotIndex, 0);
            visual.SpriteClone.transform.position = basePos + offset;

            // Update HP text
            if (visual.HpText != null)
                visual.HpText.text = $"{summary.CurrentHealth:F0}/{summary.MaxHealth:F0}";

            // Update name text — truncated, no class
            if (visual.NameText != null)
            {
                string playerName = summary.PlayerName ?? $"P{summary.SlotIndex}";
                if (playerName.Length > 9) playerName = playerName.Substring(0, 9);
                string turnMarker = (activeSlot == visual.SlotIndex) ? " <" : "";
                visual.NameText.text = $"{playerName}{turnMarker}";
            }

            // Position screen-space label below the clone sprite
            var clonePos = visual.SpriteClone.transform.position;
            PositionLabelAtWorldPoint(visual, clonePos + new Vector3(0, 0.5f, 0), cam);

            // Active player highlight
            float targetScale = (activeSlot == visual.SlotIndex) ? 1.15f : 1f;
            visual.SpriteClone.transform.localScale = Vector3.Lerp(
                visual.SpriteClone.transform.localScale,
                Vector3.one * targetScale,
                Time.deltaTime * 5f);

            // Tint active player brighter
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

    /// <summary>Position a screen-space label at a world-space point.</summary>
    private void PositionLabelAtWorldPoint(PlayerVisual visual, Vector3 worldPos, Camera cam)
    {
        if (visual?.LabelCanvas == null || cam == null) return;
        var screenPos = cam.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0) { visual.LabelCanvas.SetActive(false); return; }
        visual.LabelCanvas.SetActive(true);
        var rect = visual.LabelCanvas.GetComponent<RectTransform>();
        if (rect != null)
            rect.position = screenPos;
    }

    private void CleanupVisuals()
    {
        foreach (var v in _visuals)
            DestroyVisual(v);
        _visuals.Clear();

        if (_hostLabel != null)
        {
            if (_hostLabel.LabelCanvas != null) Destroy(_hostLabel.LabelCanvas);
            _hostLabel = null;
        }
    }

    private void DestroyVisual(PlayerVisual v)
    {
        try
        {
            if (v.SpriteClone != null)
                Destroy(v.SpriteClone);
        }
        catch { }
    }

    /// <summary>
    /// Returns a tint color for each slot so co-op players are visually distinct.
    /// </summary>
    private static Color GetSlotColor(int slot)
    {
        switch (slot)
        {
            case 1: return new Color(0.7f, 0.85f, 1f);   // light blue
            case 2: return new Color(1f, 0.8f, 0.7f);     // light orange
            case 3: return new Color(0.8f, 1f, 0.7f);     // light green
            default: return new Color(0.9f, 0.9f, 0.9f);  // light gray
        }
    }

    /// <summary>
    /// Reset state on disconnect.
    /// </summary>
    public static void Reset()
    {
        LatestPlayerSummaries = null;
        LatestActiveSlot = -1;
    }
}
