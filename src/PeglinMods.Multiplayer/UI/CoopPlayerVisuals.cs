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
        public GameObject SpriteClone;
        public GameObject LabelCanvas;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI HpText;
    }

    private readonly List<PlayerVisual> _visuals = new List<PlayerVisual>();
    private bool _inBattle;
    private string _lastScene = "";
    private GameObject _playerRef; // The original player GameObject

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

            // Create a world-space canvas for labels
            var canvasObj = new GameObject($"CoopPlayerLabel_Slot{summary.SlotIndex}");
            canvasObj.transform.SetParent(clone.transform, false);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;

            var canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(3f, 1f);
            canvasRect.localPosition = new Vector3(0, -0.8f, 0);
            canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            // Player name text
            var nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(canvasObj.transform, false);
            var nameText = nameObj.AddComponent<TextMeshProUGUI>();
            nameText.text = summary.PlayerName ?? $"Player {summary.SlotIndex}";
            nameText.fontSize = 28;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = Color.white;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0.5f, 1);
            nameRect.anchorMax = new Vector2(0.5f, 1);
            nameRect.pivot = new Vector2(0.5f, 1);
            nameRect.anchoredPosition = new Vector2(0, 0);
            nameRect.sizeDelta = new Vector2(300, 36);

            // HP text
            var hpObj = new GameObject("HpText");
            hpObj.transform.SetParent(canvasObj.transform, false);
            var hpText = hpObj.AddComponent<TextMeshProUGUI>();
            hpText.text = $"{summary.CurrentHealth:F0} / {summary.MaxHealth:F0}";
            hpText.fontSize = 24;
            hpText.alignment = TextAlignmentOptions.Center;
            hpText.color = new Color(0.8f, 1f, 0.8f);
            var hpRect = hpText.rectTransform;
            hpRect.anchorMin = new Vector2(0.5f, 1);
            hpRect.anchorMax = new Vector2(0.5f, 1);
            hpRect.pivot = new Vector2(0.5f, 1);
            hpRect.anchoredPosition = new Vector2(0, -36);
            hpRect.sizeDelta = new Vector2(300, 30);

            return new PlayerVisual
            {
                SlotIndex = summary.SlotIndex,
                SpriteClone = clone,
                LabelCanvas = canvasObj,
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

            // Position: offset left and slightly down per slot
            var offset = new Vector3(-1.5f * visual.SlotIndex, -0.3f * visual.SlotIndex, 0);
            visual.SpriteClone.transform.position = basePos + offset;

            // Update HP text
            if (visual.HpText != null)
                visual.HpText.text = $"{summary.CurrentHealth:F0} / {summary.MaxHealth:F0}";

            // Update name text with class and turn indicator
            if (visual.NameText != null)
            {
                string playerName = summary.PlayerName ?? $"Player {summary.SlotIndex}";
                string className = LobbyHelper.GetClassName(summary.ChosenClass);
                string turnMarker = (activeSlot == visual.SlotIndex) ? " [YOUR TURN]" : "";
                visual.NameText.text = $"{playerName} ({className}){turnMarker}";
                visual.NameText.color = (activeSlot == visual.SlotIndex)
                    ? new Color(1f, 1f, 0.6f) // yellow-ish for active
                    : Color.white;
            }

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

    private void CleanupVisuals()
    {
        foreach (var v in _visuals)
            DestroyVisual(v);
        _visuals.Clear();
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
