namespace PeglinMods.Multiplayer.UI;

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders persistent damage preview text above enemies during coop battles.
/// Shows each player's accumulating damage above their targeted enemy as pegs
/// are hit, persisting across turns so all players' pending damage is visible
/// before the attack phase resolves.
/// </summary>
public sealed class PendingDamageOverlay : MonoBehaviour
{
    private static PendingDamageOverlay _instance;
    public static PendingDamageOverlay Instance => _instance;

    private GameObject _canvasObj;
    private Canvas _canvas;
    private RectTransform _canvasRect;

    /// <summary>Per-slot player damage state.</summary>
    private readonly Dictionary<int, PlayerDamageInfo> _playerData = new();

    /// <summary>Per-enemy rendered panels keyed by enemy GUID.</summary>
    private readonly Dictionary<string, EnemyPanel> _panels = new();

    private static TMP_FontAsset _gameFont;
    private static bool _gameFontSearched;

    private class PlayerDamageInfo
    {
        public string PlayerName;
        public long Damage;
        public string TargetEnemyGuid;
        public bool IsAoE;
    }

    private class EnemyPanel
    {
        public GameObject Root;
        public readonly Dictionary<int, TextMeshProUGUI> SlotLabels = new();
    }

    void Awake()
    {
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DestroyCanvas();
    }

    void LateUpdate()
    {
        if (_panels.Count == 0) return;
        RepositionPanels();
    }

    // =====================================================================
    // Public API
    // =====================================================================

    /// <summary>
    /// Update a player's pending damage entry. Called on each peg hit
    /// with the running damage total.
    /// </summary>
    public static void SetPlayerDamage(int slotIndex, string playerName, long damage,
        string targetEnemyGuid, bool isAoE)
    {
        if (_instance == null) return;
        _instance.SetPlayerDamageInternal(slotIndex, playerName, damage, targetEnemyGuid, isAoE);
    }

    /// <summary>Clear all overlay panels (e.g. after attack resolves).</summary>
    public static void ClearAll()
    {
        if (_instance == null) return;
        _instance.ClearAllInternal();
    }

    // =====================================================================
    // Internal implementation
    // =====================================================================

    private void SetPlayerDamageInternal(int slotIndex, string playerName, long damage,
        string targetGuid, bool isAoE)
    {
        _playerData[slotIndex] = new PlayerDamageInfo
        {
            PlayerName = playerName,
            Damage = damage,
            TargetEnemyGuid = targetGuid,
            IsAoE = isAoE,
        };

        RebuildPanels();
    }

    private void ClearAllInternal()
    {
        _playerData.Clear();
        foreach (var panel in _panels.Values)
        {
            if (panel.Root != null) Destroy(panel.Root);
        }
        _panels.Clear();
    }

    /// <summary>
    /// Rebuild text panels based on current player data.
    /// For each enemy, compute which players' damage applies, then create/update labels.
    /// </summary>
    private void RebuildPanels()
    {
        EnsureCanvas();

        // Collect enemy GUIDs → list of (slotIndex, playerName, damage)
        var enemyDamage = new Dictionary<string, List<(int slot, string name, long dmg)>>();

        // Resolve EnemyIdentifier + EnemyManager for GUID lookups
        Utility.EnemyIdentifier enemyId = null;
        EnemyManager em = null;
        try
        {
            var services = MultiplayerPlugin.Services;
            services?.TryResolve(out enemyId);
            em = FindObjectOfType<EnemyManager>();
        }
        catch { }

        foreach (var kvp in _playerData)
        {
            var data = kvp.Value;
            if (data.Damage <= 0) continue;

            if (data.IsAoE && em != null && enemyId != null)
            {
                // AoE: add to every living enemy
                foreach (var enemy in em.Enemies)
                {
                    if (enemy == null || enemy.CurrentHealth <= 0f) continue;
                    var guid = enemyId.GetGuid(enemy);
                    if (string.IsNullOrEmpty(guid)) continue;
                    if (!enemyDamage.ContainsKey(guid))
                        enemyDamage[guid] = new List<(int, string, long)>();
                    enemyDamage[guid].Add((kvp.Key, data.PlayerName, data.Damage));
                }
            }
            else if (!string.IsNullOrEmpty(data.TargetEnemyGuid))
            {
                var guid = data.TargetEnemyGuid;
                if (!enemyDamage.ContainsKey(guid))
                    enemyDamage[guid] = new List<(int, string, long)>();
                enemyDamage[guid].Add((kvp.Key, data.PlayerName, data.Damage));
            }
        }

        // Remove panels for enemies no longer targeted
        var toRemove = new List<string>();
        foreach (var guid in _panels.Keys)
        {
            if (!enemyDamage.ContainsKey(guid))
                toRemove.Add(guid);
        }
        foreach (var guid in toRemove)
        {
            if (_panels[guid].Root != null) Destroy(_panels[guid].Root);
            _panels.Remove(guid);
        }

        // Create/update panels
        foreach (var kvp in enemyDamage)
        {
            var guid = kvp.Key;
            var entries = kvp.Value;

            if (!_panels.TryGetValue(guid, out var panel))
            {
                panel = new EnemyPanel { Root = new GameObject($"PendingDmg_{guid}") };
                panel.Root.transform.SetParent(_canvasObj.transform, false);
                panel.Root.AddComponent<RectTransform>();
                _panels[guid] = panel;
            }

            // Update/create slot labels
            var activeSlots = new HashSet<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                var (slot, name, dmg) = entries[i];
                activeSlots.Add(slot);

                if (!panel.SlotLabels.TryGetValue(slot, out var label))
                {
                    label = CreateLabel(panel.Root.transform, slot);
                    panel.SlotLabels[slot] = label;
                }

                label.text = $"{name}: {DamageCountDisplay.FormatDamageNumberAsString(dmg)}";
                label.color = GetSlotColor(slot);
            }

            // Remove labels for slots no longer targeting this enemy
            var slotsToRemove = new List<int>();
            foreach (var slotKvp in panel.SlotLabels)
            {
                if (!activeSlots.Contains(slotKvp.Key))
                    slotsToRemove.Add(slotKvp.Key);
            }
            foreach (var slot in slotsToRemove)
            {
                if (panel.SlotLabels[slot] != null)
                    Destroy(panel.SlotLabels[slot].gameObject);
                panel.SlotLabels.Remove(slot);
            }

            // Stack labels vertically
            int idx = 0;
            foreach (var slotKvp in panel.SlotLabels)
            {
                var rect = slotKvp.Value.rectTransform;
                rect.anchoredPosition = new Vector2(0, -idx * 28f);
                idx++;
            }
        }
    }

    private void RepositionPanels()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Utility.EnemyIdentifier enemyId = null;
        try
        {
            MultiplayerPlugin.Services?.TryResolve(out enemyId);
        }
        catch { }

        if (enemyId == null) return;

        foreach (var kvp in _panels)
        {
            var panel = kvp.Value;
            if (panel.Root == null) continue;

            var enemy = enemyId.Find(kvp.Key);
            if (enemy == null || enemy.CurrentHealth <= 0f)
            {
                panel.Root.SetActive(false);
                continue;
            }

            var worldPos = enemy.transform.position + new Vector3(0f, 1.8f, 0f);
            var screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0)
            {
                panel.Root.SetActive(false);
                continue;
            }

            if (!panel.Root.activeSelf) panel.Root.SetActive(true);

            var rect = panel.Root.GetComponent<RectTransform>();
            if (rect != null && _canvasRect != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPos, null, out var localPoint);
                rect.localPosition = localPoint;
            }
        }
    }

    // =====================================================================
    // UI helpers
    // =====================================================================

    private void EnsureCanvas()
    {
        if (_canvasObj != null) return;
        _canvasObj = new GameObject("PendingDamageOverlayCanvas");
        DontDestroyOnLoad(_canvasObj);
        _canvas = _canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9500; // above CoopPlayerVisuals (9000)
        var scaler = _canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = _canvasObj.GetComponent<RectTransform>();
    }

    private TextMeshProUGUI CreateLabel(Transform parent, int slotIndex)
    {
        var obj = new GameObject($"Label_Slot{slotIndex}");
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 26;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = GetSlotColor(slotIndex);
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = Color.black;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        var font = GetGameFont();
        if (font != null) tmp.font = font;

        var rect = tmp.rectTransform;
        rect.sizeDelta = new Vector2(300, 30);
        rect.pivot = new Vector2(0.5f, 1f); // anchor at top-center

        return tmp;
    }

    private static Color GetSlotColor(int slotIndex)
    {
        return slotIndex switch
        {
            0 => new Color(1f, 0.85f, 0.2f),      // Gold for host
            1 => new Color(0.3f, 0.85f, 1f),       // Cyan for client 1
            2 => new Color(0.5f, 1f, 0.5f),        // Green for client 2
            3 => new Color(1f, 0.5f, 0.8f),        // Pink for client 3
            _ => Color.white,
        };
    }

    private static TMP_FontAsset GetGameFont()
    {
        if (_gameFontSearched) return _gameFont;
        _gameFontSearched = true;
        try
        {
            foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>())
                if (tmp.font != null) { _gameFont = tmp.font; break; }
        }
        catch { }
        return _gameFont;
    }

    private void DestroyCanvas()
    {
        ClearAllInternal();
        if (_canvasObj != null) Destroy(_canvasObj);
        _canvasObj = null;
        _canvas = null;
        _canvasRect = null;
    }
}
